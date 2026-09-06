import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { SolicitacaoCompra } from '@/api/solicitacoes';
import { podeEnviar, podeMexer, situacaoDaSc } from '@/api/solicitacoes';
import { ToastProvider } from '@/componentes/Toast';
import { hojeIso } from '@/util/formato';
import { andamentoDaSc, dataNoPassado, MeusPedidos, resumoDosItens } from './MeusPedidos';

vi.mock('@/api/solicitacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/solicitacoes')>()),
  listarSolicitacoes: vi.fn(),
  enviarSolicitacao: vi.fn(),
  excluirSolicitacao: vi.fn(),
  atualizarSolicitacao: vi.fn(),
}));
vi.mock('@/api/centrosCusto', () => ({ listarCentrosCusto: vi.fn() }));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import { atualizarSolicitacao, enviarSolicitacao, excluirSolicitacao, listarSolicitacoes } from '@/api/solicitacoes';
import { listarCentrosCusto } from '@/api/centrosCusto';

const eu: Usuario = { id: 'u1', email: 'ana@t.com', name: 'Ana', role: 'Requester', modules: ['SOLICITACOES'] };

const sc = (p: Partial<SolicitacaoCompra>): SolicitacaoCompra => ({
  id: 'sc-' + (p.number ?? 'SC-1'), number: 'SC-2026-000001', kind: 'AVULSA', status: 'DRAFT', cycle: 1,
  priority: 'NORMAL', urgencyReason: null, urgencyImpact: null, neededBy: null,
  justification: 'Reposição de EPI', needType: null, deliveryLocation: null, company: null, internalNotes: null,
  costCenter: 'BAH-001', requesterId: 'u1', requesterLabel: 'Ana', totalEstimatedValue: 250,
  decisionReason: null, decidedByLabel: null, approverLabel: null, approvalIssue: null, assignedToLabel: null,
  submittedAt: null, decidedAt: null, processStatusLabel: null, processStatusTone: null, processStatusHint: null,
  attachments: [], items: [{ itemId: 'i1', sequence: 1, description: 'Luva nitrílica', catalogCode: 'EPI-001', quantity: 10, unitOfMeasure: 'PAR', estimatedUnitPrice: 25 }],
  ...p,
});

describe('regras da SC', () => {
  it('quem criou manda nela até um comprador assumir a cotação', () => {
    expect(podeMexer(sc({ status: 'DRAFT' }))).toBe(true);
    expect(podeMexer(sc({ status: 'RETURNED' }))).toBe(true);
    expect(podeMexer(sc({ status: 'SUBMITTED', assignedToLabel: null }))).toBe(true);
    expect(podeMexer(sc({ status: 'SUBMITTED', assignedToLabel: 'Carla' }))).toBe(false);
    expect(podeMexer(sc({ status: 'APPROVED' }))).toBe(false);
  });
  it('enviar só vale para rascunho e devolvida', () => {
    expect(podeEnviar(sc({ status: 'DRAFT' }))).toBe(true);
    expect(podeEnviar(sc({ status: 'RETURNED' }))).toBe(true);
    expect(podeEnviar(sc({ status: 'SUBMITTED' }))).toBe(false);
  });
  it('a situação do processo tem prioridade sobre a bruta', () => {
    expect(situacaoDaSc(sc({ status: 'SUBMITTED' })).rotulo).toBe('Em cotação');
    expect(situacaoDaSc(sc({ status: 'SUBMITTED', processStatusLabel: 'Entregue', processStatusTone: 'on' })).rotulo).toBe('Entregue');
  });
  it('o andamento explica o que trava a SC', () => {
    expect(andamentoDaSc(sc({ status: 'SUBMITTED' }))).toEqual({
      texto: expect.stringContaining('aguardando a designação'), alerta: false,
    });
    expect(andamentoDaSc(sc({ status: 'SUBMITTED', assignedToLabel: 'Carla' }))?.texto).toContain('Em cotação com Carla');
    // só o problema da alçada vira alerta; andamento normal não
    expect(andamentoDaSc(sc({ status: 'IN_APPROVAL', approvalIssue: 'Centro sem aprovador' })))
      .toEqual({ texto: 'Centro sem aprovador', alerta: true });
    expect(andamentoDaSc(sc({ status: 'IN_APPROVAL', approverLabel: 'Bruno' })))
      .toEqual({ texto: 'Aguardando aprovação de: Bruno', alerta: false });
    expect(andamentoDaSc(sc({ status: 'DRAFT' }))).toBeNull();
  });
  it('o resumo lista quantidade, código e descrição', () => {
    expect(resumoDosItens(sc({}))).toBe('10× [EPI-001] Luva nitrílica');
  });
  it('data de necessidade vencida é reconhecida antes do envio (PR-ERR-050)', () => {
    expect(dataNoPassado('2020-01-01')).toBe(true);
    expect(dataNoPassado(hojeIso())).toBe(false);       // hoje ainda vale
    expect(dataNoPassado('')).toBe(false);              // sem data não há o que avisar
  });
});

const pagina = (itens: SolicitacaoCompra[], total = itens.length) => ({ itens, total });

describe('<MeusPedidos />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listarCentrosCusto).mockResolvedValue([]);
    vi.mocked(listarSolicitacoes).mockResolvedValue(pagina([
      sc({ number: 'SC-2026-000001', status: 'DRAFT' }),
      sc({ number: 'SC-2026-000002', status: 'SUBMITTED', assignedToLabel: 'Carla Menezes' }),
      sc({ number: 'SC-2026-000003', status: 'APPROVED', requesterId: 'outro' }),
    ]));
  });

  const montar = () => render(<ToastProvider><MeusPedidos /></ToastProvider>);

  it('mostra situação, andamento e só oferece ações na SC de quem está logado', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-solicitacoes')).toBeInTheDocument());
    const rascunho = within(screen.getByText('SC-2026-000001').closest('tr')!);
    expect(rascunho.getByText('Rascunho')).toBeInTheDocument();
    expect(rascunho.getByRole('button', { name: 'Enviar solicitação' })).toBeInTheDocument();

    const emCotacao = within(screen.getByText('SC-2026-000002').closest('tr')!);
    expect(emCotacao.getByText(/Em cotação com Carla Menezes/)).toBeInTheDocument();
    // já tem comprador: sai de cena para o solicitante
    expect(emCotacao.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();

    const deOutro = within(screen.getByText('SC-2026-000003').closest('tr')!);
    expect(deOutro.queryByRole('button')).not.toBeInTheDocument();
  });

  it('enviar chama a API e recarrega a lista', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-solicitacoes')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Enviar solicitação' }));
    await waitFor(() => expect(enviarSolicitacao).toHaveBeenCalledWith('sc-SC-2026-000001'));
    expect(screen.getAllByTestId('toast').at(-1)).toHaveTextContent('enviada');
  });

  it('a busca vai para o servidor, e a tela diz quantas existem', async () => {
    vi.mocked(listarSolicitacoes).mockResolvedValue(pagina([sc({ number: 'SC-2026-000001' })], 640));
    montar();
    await waitFor(() => expect(screen.getByTestId('contagem-solicitacoes'))
      .toHaveTextContent('Mostrando 1 de 640'));

    await userEvent.type(screen.getByLabelText('Buscar'), 'notebook');
    await waitFor(() => expect(listarSolicitacoes).toHaveBeenCalledWith(
      expect.objectContaining({ busca: 'notebook' }), expect.anything()));

    // e oferece o resto em vez de fingir que a lista acabou
    await userEvent.click(screen.getByRole('button', { name: 'Carregar mais' }));
    await waitFor(() => expect(listarSolicitacoes).toHaveBeenCalledWith(
      expect.objectContaining({ tamanho: 100 }), expect.anything()));
  });

  it('sem busca, a lista vazia diz por onde criar a primeira', async () => {
    vi.mocked(listarSolicitacoes).mockResolvedValue(pagina([]));
    montar();
    await waitFor(() => expect(screen.getByText(/Nenhum pedido ainda/)).toBeInTheDocument());
  });

  it('excluir pede confirmação antes de chamar a API', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-solicitacoes')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: 'Excluir' })[0]);
    expect(screen.getByRole('dialog')).toHaveTextContent('SC-2026-000001');
    expect(excluirSolicitacao).not.toHaveBeenCalled();
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Excluir' }));
    await waitFor(() => expect(excluirSolicitacao).toHaveBeenCalledWith('sc-SC-2026-000001'));
  });

  it('editar abre o formulário e a data vazia limpa a necessidade', async () => {
    vi.mocked(atualizarSolicitacao).mockResolvedValue(sc({}));
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-solicitacoes')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: 'Editar' })[0]);
    expect(screen.getByRole('heading', { name: /Editar pedido SC-2026-000001/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }));
    await waitFor(() => expect(atualizarSolicitacao).toHaveBeenCalledWith('sc-SC-2026-000001', expect.objectContaining({
      justification: 'Reposição de EPI', clearNeededBy: true, neededBy: null,
    })));
  });

  it('a data que já passou avisa na edição, em vez de esperar o envio falhar', async () => {
    // o rascunho pode ter nascido com a data vencida: travar o campo impediria
    // de salvar qualquer outra correção, então a tela avisa e deixa salvar
    vi.mocked(listarSolicitacoes).mockResolvedValue(pagina([sc({ neededBy: '2020-01-01' })]));
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-solicitacoes')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: 'Editar' })[0]);
    expect(screen.getByTestId('data-vencida')).toHaveTextContent('PR-ERR-050');

    await userEvent.clear(screen.getByLabelText(/Data de necessidade/));
    expect(screen.queryByTestId('data-vencida')).not.toBeInTheDocument();
  });

  it('urgência só aparece quando a prioridade é urgente', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-solicitacoes')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: 'Editar' })[0]);
    expect(screen.queryByLabelText('Justificativa da urgência')).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Prioridade'), 'URGENT');
    expect(screen.getByLabelText('Justificativa da urgência')).toBeInTheDocument();
  });
});
