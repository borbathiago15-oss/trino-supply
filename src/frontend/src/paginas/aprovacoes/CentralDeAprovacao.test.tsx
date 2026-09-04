import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProcessoParaAprovar } from '@/api/cotacoes';
import { propostaVencedora } from '@/api/cotacoes';
import type { SolicitacaoMaterial } from '@/api/material';
import type { SolicitacaoCompra } from '@/api/solicitacoes';
import { ToastProvider } from '@/componentes/Toast';
import { CentralDeAprovacao, linkDoProcesso } from './CentralDeAprovacao';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  processosParaMinhaAprovacao: vi.fn(),
}));
vi.mock('@/api/material', () => ({
  listarSolicitacoesMaterial: vi.fn(), aprovarMaterial: vi.fn(), recusarMaterial: vi.fn(),
}));
vi.mock('@/api/solicitacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/solicitacoes')>()),
  aprovacoesPendentes: vi.fn(), aprovarSolicitacao: vi.fn(), devolverSolicitacao: vi.fn(), rejeitarSolicitacao: vi.fn(),
}));

import { processosParaMinhaAprovacao } from '@/api/cotacoes';
import { aprovarMaterial, listarSolicitacoesMaterial, recusarMaterial } from '@/api/material';
import { aprovacoesPendentes, devolverSolicitacao } from '@/api/solicitacoes';

const processo: ProcessoParaAprovar = {
  id: 'q1', number: 'RFQ-2026-000001', status: 'AGUARDANDO_GERENTE', costCenter: 'BAH-001',
  sourcePrNumber: 'SC-2026-000001', justification: 'Reposição de EPI',
  selection: { winnerProposalId: 'p2', justification: 'menor preço' },
  proposals: [
    { id: 'p1', supplierName: 'Alfa EPIs', totalValue: 1200 },
    { id: 'p2', supplierName: 'Beta Química', totalValue: 980 },
  ],
};

const material: SolicitacaoMaterial = {
  id: 'mr1', number: 'MR-2026-000004', status: 'AGUARDANDO_APROVACAO', costCenter: 'BAH-001',
  requesterLabel: 'Ana Paula', notes: 'para a obra',
  items: [{ itemId: 'mi1', description: 'Luva nitrílica', quantity: 10, unitOfMeasure: 'PAR' }],
};

const scLegado = {
  id: 'sc9', number: 'SC-2026-000009', kind: 'AVULSA', status: 'IN_APPROVAL', cycle: 1, priority: 'NORMAL',
  urgencyReason: null, urgencyImpact: null, neededBy: null, justification: 'Compra antiga',
  needType: null, deliveryLocation: null, company: null, internalNotes: null, costCenter: 'BAH-001',
  requesterId: 'u9', requesterLabel: 'João', totalEstimatedValue: 500, decisionReason: null,
  decidedByLabel: null, approverLabel: null, approvalIssue: null, assignedToLabel: null, submittedAt: null,
  decidedAt: null, processStatusLabel: null, processStatusTone: null, processStatusHint: null,
  attachments: [], items: [{ itemId: 'i1', sequence: 1, description: 'Cabo', catalogCode: null, quantity: 2, unitOfMeasure: 'RL', estimatedUnitPrice: 250 }],
} as SolicitacaoCompra;

describe('regras da central', () => {
  it('a proposta vencedora vem da seleção do comprador', () => {
    expect(propostaVencedora(processo)?.supplierName).toBe('Beta Química');
    expect(propostaVencedora({ ...processo, selection: null })).toBeNull();
  });
  it('o processo abre no sistema clássico, que ainda tem a tela de cotações', () => {
    expect(linkDoProcesso('q1')).toBe('/cotacoes/q1');
  });
});

describe('<CentralDeAprovacao />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([processo]);
    vi.mocked(listarSolicitacoesMaterial).mockResolvedValue([material]);
    vi.mocked(aprovacoesPendentes).mockResolvedValue([scLegado]);
  });

  const montar = () => render(<ToastProvider><CentralDeAprovacao /></ToastProvider>);

  it('mostra as três filas com o essencial de cada uma', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-processos')).toBeInTheDocument());
    const proc = within(screen.getByTestId('tabela-processos'));
    expect(proc.getByText('Beta Química')).toBeInTheDocument();
    expect(proc.getByText(/980,00/)).toBeInTheDocument();
    expect(proc.getByRole('link', { name: 'Analisar e decidir' })).toHaveAttribute('href', '/cotacoes/q1');

    expect(within(screen.getByTestId('tabela-material')).getByText('Ana Paula')).toBeInTheDocument();
    expect(within(screen.getByTestId('tabela-scs')).getByText('SC-2026-000009')).toBeInTheDocument();
  });

  it('cada fila some quando quem está logado não tem acesso a ela', async () => {
    vi.mocked(processosParaMinhaAprovacao).mockRejectedValue(new Error('403'));
    vi.mocked(listarSolicitacoesMaterial).mockRejectedValue(new Error('403'));
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-scs')).toBeInTheDocument());
    expect(screen.queryByTestId('tabela-processos')).not.toBeInTheDocument();
    expect(screen.queryByTestId('tabela-material')).not.toBeInTheDocument();
    expect(screen.getByText('Nenhuma compra aguardando a sua aprovação.')).toBeInTheDocument();
  });

  it('devolver exige o motivo antes de liberar o botão', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-scs')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Devolver' }));
    const dialogo = within(screen.getByRole('dialog'));
    expect(dialogo.getByRole('button', { name: 'Devolver' })).toBeDisabled();
    await userEvent.type(screen.getByLabelText('O que o solicitante deve ajustar?'), 'faltou o orçamento');
    await userEvent.click(dialogo.getByRole('button', { name: 'Devolver' }));
    await waitFor(() => expect(devolverSolicitacao).toHaveBeenCalledWith('sc9', 'faltou o orçamento'));
  });

  it('material: aprova com a quantidade liberada que o aprovador ajustou', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-material')).toBeInTheDocument());
    const campo = screen.getByLabelText('Quantidade liberada de Luva nitrílica');
    expect(campo).toHaveValue(10);
    await userEvent.clear(campo);
    await userEvent.type(campo, '6');
    // há um "Aprovar" na fila de material e outro na de SCs do fluxo anterior
    await userEvent.click(within(screen.getByTestId('tabela-material')).getByRole('button', { name: 'Aprovar' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Liberar' }));
    await waitFor(() => expect(aprovarMaterial).toHaveBeenCalledWith('mr1', [{ itemId: 'mi1', quantity: 6 }], null));
  });

  it('material: recusar exige justificativa', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-material')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Recusar' }));
    await userEvent.type(screen.getByLabelText('Justificativa da recusa'), 'sem saldo em estoque');
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Recusar' }));
    await waitFor(() => expect(recusarMaterial).toHaveBeenCalledWith('mr1', 'sem saldo em estoque'));
  });

  it('sem nada nas filas, diz que não há pendências', async () => {
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([]);
    vi.mocked(listarSolicitacoesMaterial).mockResolvedValue([]);
    vi.mocked(aprovacoesPendentes).mockResolvedValue([]);
    montar();
    await waitFor(() => expect(screen.getByText('Nenhuma aprovação pendente para você agora.')).toBeInTheDocument());
  });
});
