import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { ProcessoParaAprovar } from '@/api/cotacoes';
import { propostaVencedora } from '@/api/cotacoes';
import type { SolicitacaoMaterial } from '@/api/material';
import type { SolicitacaoCompra } from '@/api/solicitacoes';
import { ToastProvider } from '@/componentes/Toast';
import { processo as processoBase, proposta } from '@/test/cotacoes';
import { alcadaDe, CentralDeAprovacao, linkDoProcesso } from './CentralDeAprovacao';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  processosParaMinhaAprovacao: vi.fn(), minhasDecisoes: vi.fn(), decidir: vi.fn(),
}));
vi.mock('@/api/material', () => ({
  listarSolicitacoesMaterial: vi.fn(), aprovarMaterial: vi.fn(), recusarMaterial: vi.fn(),
}));
vi.mock('@/api/solicitacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/solicitacoes')>()),
  aprovacoesPendentes: vi.fn(), aprovarSolicitacao: vi.fn(), devolverSolicitacao: vi.fn(), rejeitarSolicitacao: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import { decidir, minhasDecisoes, processosParaMinhaAprovacao } from '@/api/cotacoes';
import { aprovarMaterial, listarSolicitacoesMaterial, recusarMaterial } from '@/api/material';
import { aprovacoesPendentes, devolverSolicitacao } from '@/api/solicitacoes';

let eu: Usuario = { id: 'u-gustavo', email: 'g@t.com', name: 'Gustavo', role: 'Approver', modules: ['APROVACAO'] };

/** Um processo na fila do Nível 1: duas propostas, o comprador escolheu a mais cara. */
const processo: ProcessoParaAprovar = {
  ...processoBase({
    status: 'AGUARDANDO_GERENTE', justification: 'Reposição de EPI', createdByLabel: 'Carla',
    proposals: [
      proposta({ id: 'p1', supplierId: 's1', supplierName: 'Alfa EPIs', totalValue: 980, isLatest: true }),
      proposta({ id: 'p2', supplierId: 's2', supplierName: 'Beta Química', totalValue: 1200, isLatest: true, deliveryDays: 5, paymentTerms: '28 dias' }),
    ],
    selection: { winnerSupplierId: 's2', winnerProposalId: 'p2', criteria: 'Prazo de entrega', justification: 'entrega em 5 dias', by: 'u-carla', byLabel: 'Carla' },
  }),
  decisao: {
    requesterLabel: 'Ana Paula', priority: 'NORMAL', urgencyReason: null, urgencyImpact: null,
    neededBy: '2026-09-30', budget: 1500, level: 1, waitingSince: new Date(Date.now() - 4 * 86_400_000).toISOString(),
    complianceScore: 100, compliancePenalties: [], contractNumber: null,
  },
};

const material: SolicitacaoMaterial = {
  id: 'mr1', number: 'MR-2026-000004', status: 'AGUARDANDO_APROVACAO', costCenter: 'BAH-001',
  requesterLabel: 'Ana Paula', notes: 'para a obra',
  items: [{ itemId: 'mi1', description: 'Luva nitrílica', quantity: 10, unitOfMeasure: 'PAR' }],
};

const scLegado = {
  id: 'sc9', number: 'SC-2026-000009', kind: 'AVULSA', status: 'IN_APPROVAL', cycle: 1, priority: 'NORMAL',
  urgencyReason: null, urgencyImpact: null, neededBy: null, justification: 'Compra antiga',
  needType: null, deliveryLocation: null, company: null, internalNotes: null, budget: null, costCenter: 'BAH-001',
  requesterId: 'u9', requesterLabel: 'João', totalEstimatedValue: 500, decisionReason: null,
  decidedByLabel: null, approverLabel: null, approvalIssue: null, assignedToLabel: null, submittedAt: null,
  decidedAt: null, processStatusLabel: null, processStatusTone: null, processStatusHint: null,
  acompanhamento: null, attachments: [], items: [{ itemId: 'i1', sequence: 1, description: 'Cabo', catalogCode: null, quantity: 2, unitOfMeasure: 'RL', estimatedUnitPrice: 250 }],
} as SolicitacaoCompra;

describe('regras da central', () => {
  it('a proposta vencedora vem da seleção do comprador', () => {
    expect(propostaVencedora(processo)?.supplierName).toBe('Beta Química');
    expect(propostaVencedora({ ...processo, selection: null })).toBeNull();
  });
  it('o processo abre na tela React de cotações, sem recarregar o app', () => {
    expect(linkDoProcesso('q1')).toBe('/cotacoes/q1');
  });
  it('a alçada que decide sai da etapa em que o processo está', () => {
    expect(alcadaDe('AGUARDANDO_GERENTE')).toBe('manager');
    expect(alcadaDe('AGUARDANDO_DIRETOR')).toBe('director');
    expect(alcadaDe('EM_ANALISE')).toBeNull();
  });
});

describe('<CentralDeAprovacao />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    eu = { id: 'u-gustavo', email: 'g@t.com', name: 'Gustavo', role: 'Approver', modules: ['APROVACAO'] };
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([processo]);
    vi.mocked(listarSolicitacoesMaterial).mockResolvedValue([material]);
    vi.mocked(aprovacoesPendentes).mockResolvedValue([scLegado]);
    vi.mocked(minhasDecisoes).mockResolvedValue([]);
  });

  const montar = () => render(
    <MemoryRouter><ToastProvider><CentralDeAprovacao /></ToastProvider></MemoryRouter>,
  );

  it('o card de decisão diz quem pediu, o que o comprador escolheu e quanto acima da mais barata', async () => {
    montar();
    const card = within(await screen.findByTestId('fila-decisao'));
    // quem pediu e por quê
    expect(card.getByText('Ana Paula')).toBeInTheDocument();
    expect(card.getByText('Reposição de EPI')).toBeInTheDocument();
    expect(card.getByText(/precisa até 30\/09\/2026/)).toBeInTheDocument();
    // a escolha, contra a mais barata — a pergunta que o aprovador faria primeiro
    expect(card.getByText('Beta Química')).toBeInTheDocument();
    expect(card.getByTestId('valor-da-compra')).toHaveTextContent('R$ 1.200,00');
    expect(card.getByTestId('comparacao')).toHaveTextContent('22,4% acima');
    expect(card.getByTestId('comparacao')).toHaveTextContent('Alfa EPIs');
    expect(card.getByText(/entrega em 5 dias/)).toBeInTheDocument();
    expect(card.getByText(/Prazo de entrega/)).toBeInTheDocument();   // o critério
    // o que o sistema mede
    expect(card.getByText(/dentro do orçamento de R\$ 1\.500,00/)).toBeInTheDocument();
    expect(card.getByText('compliance 100')).toBeInTheDocument();
    expect(card.getByText(/espera há 4 dias/)).toBeInTheDocument();
    // a decisão fica aqui; o processo completo é o segundo caminho
    expect(card.getByRole('button', { name: 'Aprovar' })).toBeInTheDocument();
    expect(card.getByRole('link', { name: /Ver processo completo/ })).toHaveAttribute('href', '/cotacoes/q1');
    // as outras filas continuam
    expect(within(screen.getByTestId('tabela-material')).getByText('Ana Paula')).toBeInTheDocument();
    expect(within(screen.getByTestId('tabela-scs')).getByText('SC-2026-000009')).toBeInTheDocument();
  });

  it('aprovar decide na própria Central, pela alçada da etapa', async () => {
    vi.mocked(decidir).mockResolvedValue(processoBase({}));
    montar();
    const card = within(await screen.findByTestId('fila-decisao'));
    await userEvent.click(card.getByRole('button', { name: 'Aprovar' }));
    const dialogo = within(screen.getByRole('dialog'));
    expect(dialogo.getByRole('button', { name: 'Aprovar' })).toBeEnabled();   // comentário opcional
    await userEvent.click(dialogo.getByRole('button', { name: 'Aprovar' }));
    await waitFor(() => expect(decidir).toHaveBeenCalledWith('q1', 'manager', 'APROVAR', null));
  });

  it('pedir ajustes e rejeitar exigem o motivo; o Nível 2 decide pela rota da diretoria', async () => {
    eu = { id: 'u-diana', email: 'd@t.com', name: 'Diana', role: 'Director', modules: [] };
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([{
      ...processo, status: 'AGUARDANDO_DIRETOR',
      managerApproval: { by: 'u-gustavo', byLabel: 'Gustavo', at: '2026-09-18T10:00:00Z' },
      decisao: { ...processo.decisao!, level: 2 },
    }]);
    vi.mocked(decidir).mockResolvedValue(processoBase({}));
    montar();
    const card = within(await screen.findByTestId('fila-decisao'));
    expect(card.getByText('Nível 2 — diretoria')).toBeInTheDocument();
    expect(card.getByText(/Nível 1 por Gustavo em 18\/09\/2026/)).toBeInTheDocument();

    await userEvent.click(card.getByRole('button', { name: 'Solicitar ajustes' }));
    const dialogo = within(screen.getByRole('dialog'));
    expect(dialogo.getByRole('button', { name: 'Solicitar ajustes' })).toBeDisabled();
    await userEvent.type(screen.getByLabelText('O que o comprador deve ajustar?'), 'renegociar o frete');
    await userEvent.click(dialogo.getByRole('button', { name: 'Solicitar ajustes' }));
    await waitFor(() => expect(decidir).toHaveBeenCalledWith('q1', 'director', 'AJUSTES', 'renegociar o frete'));
  });

  it('quem escolheu o fornecedor vê o motivo no lugar dos botões do Nível 2 (RFQ-ERR-030)', async () => {
    eu = { id: 'u-carla', email: 'c@t.com', name: 'Carla', role: 'Director', modules: [] };
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([{ ...processo, status: 'AGUARDANDO_DIRETOR' }]);
    montar();
    const card = within(await screen.findByTestId('fila-decisao'));
    expect(card.getByTestId('conflito-segregacao')).toHaveTextContent('RFQ-ERR-030');
    expect(card.queryByRole('button', { name: 'Aprovar' })).not.toBeInTheDocument();
  });

  it('urgência, proposta única e compra acima do orçamento aparecem no card, não no erro', async () => {
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([{
      ...processo,
      proposals: [processo.proposals[1]],
      decisao: {
        ...processo.decisao!, priority: 'URGENT', urgencyReason: 'linha parada', urgencyImpact: 'produção para',
        budget: 1000, complianceScore: 80,
        compliancePenalties: [{ code: 'C1', label: 'emergencial', points: -20, evidence: 'SC urgente' }],
      },
    }]);
    montar();
    const card = within(await screen.findByTestId('fila-decisao'));
    expect(card.getByText('URGENTE')).toBeInTheDocument();
    expect(card.getByText(/linha parada — se não comprar: produção para/)).toBeInTheDocument();
    expect(card.getByTestId('comparacao')).toHaveTextContent('Proposta única');
    expect(card.getByText(/acima do orçamento de R\$ 1\.000,00/)).toBeInTheDocument();
    expect(card.getByText('compliance 80')).toBeInTheDocument();
    expect(card.getByText(/emergencial \(-20\)/)).toBeInTheDocument();
  });

  it('as decisões recentes ficam abaixo da fila, com a situação de hoje', async () => {
    vi.mocked(minhasDecisoes).mockResolvedValue([{
      quotationId: 'q7', number: 'RFQ-2026-000007', status: 'OC_REGISTRADA', eventType: 'GERENTE_APROVOU',
      occurredAt: '2026-09-15T10:00:00Z', note: null, supplierName: 'Alfa EPIs', totalValue: 500,
    }]);
    montar();
    const tabela = within(await screen.findByTestId('decisoes-recentes'));
    expect(tabela.getByText('Aprovou (Nível 1)')).toBeInTheDocument();
    expect(tabela.getByText('O.C. registrada')).toBeInTheDocument();
    expect(tabela.getByRole('link', { name: 'RFQ-2026-000007' })).toHaveAttribute('href', '/cotacoes/q7');
  });

  it('cada fila some quando quem está logado não tem acesso a ela', async () => {
    vi.mocked(processosParaMinhaAprovacao).mockRejectedValue(new Error('403'));
    vi.mocked(listarSolicitacoesMaterial).mockRejectedValue(new Error('403'));
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-scs')).toBeInTheDocument());
    expect(screen.queryByTestId('fila-decisao')).not.toBeInTheDocument();
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

  it('sem nada nas filas, diz que não há pendências — uma vez só', async () => {
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([]);
    vi.mocked(listarSolicitacoesMaterial).mockResolvedValue([]);
    vi.mocked(aprovacoesPendentes).mockResolvedValue([]);
    montar();
    await waitFor(() => expect(screen.getByText('Nenhuma aprovação pendente para você agora.')).toBeInTheDocument());
    expect(screen.getAllByText(/Fila limpa/)).toHaveLength(1);
  });
});
