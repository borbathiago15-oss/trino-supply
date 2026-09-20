import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { AcompanhamentoDaSc, SolicitacaoCompra } from '@/api/solicitacoes';
import { podeEnviar, podeMexer, situacaoDaSc } from '@/api/solicitacoes';
import { ToastProvider } from '@/componentes/Toast';
import { hojeIso } from '@/util/formato';
import { andamentoDaSc, dataNoPassado, esperaDesde, MeusPedidos, resumoDosItens } from './MeusPedidos';

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
  budget: null,
  costCenter: 'BAH-001', requesterId: 'u1', requesterLabel: 'Ana', totalEstimatedValue: 250,
  decisionReason: null, decidedByLabel: null, approverLabel: null, approvalIssue: null, assignedToLabel: null,
  submittedAt: null, decidedAt: null, processStatusLabel: null, processStatusTone: null, processStatusHint: null,
  acompanhamento: null, attachments: [], items: [{ itemId: 'i1', sequence: 1, description: 'Luva nitrílica', catalogCode: 'EPI-001', quantity: 10, unitOfMeasure: 'PAR', estimatedUnitPrice: 25 }],
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
  it('a espera é dita em dias, e "hoje" quando começou hoje', () => {
    const agora = new Date('2026-09-20T12:00:00Z');
    expect(esperaDesde('2026-09-17T09:00:00Z', agora)).toBe('há 3 dias');
    expect(esperaDesde('2026-09-19T09:00:00Z', agora)).toBe('há 1 dia');
    expect(esperaDesde('2026-09-20T09:00:00Z', agora)).toBe('hoje');
    expect(esperaDesde(null, agora)).toBeNull();
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

  const montar = () => render(<MemoryRouter><ToastProvider><MeusPedidos /></ToastProvider></MemoryRouter>);

  /** Um acompanhamento como o servidor manda: SC no Nível 1 há três dias. */
  const naAlcada = (p: Partial<AcompanhamentoDaSc> = {}): AcompanhamentoDaSc => ({
    etapas: [
      { chave: 'enviada', rotulo: 'Enviada', situacao: 'feita', quando: '2026-09-10T10:00:00Z', quem: 'Ana' },
      { chave: 'comprador', rotulo: 'Com o comprador', situacao: 'feita', quando: '2026-09-11T10:00:00Z', quem: 'Carla' },
      { chave: 'cotacao', rotulo: 'Em cotação', situacao: 'feita', quando: '2026-09-15T10:00:00Z', quem: 'Carla' },
      { chave: 'aprovacao', rotulo: 'Aprovação', situacao: 'atual', quando: null, quem: null },
      { chave: 'pedido', rotulo: 'Pedido fechado', situacao: 'pendente', quando: null, quem: null },
      { chave: 'entrega', rotulo: 'Entregue', situacao: 'pendente', quando: null, quem: null },
    ],
    etapaAtual: 'aprovacao', frase: 'Aguardando aprovação de Gustavo — Nível 1 (gestor do centro). Fornecedor escolhido: Alfa.',
    comQuem: 'Gustavo', desde: new Date(Date.now() - 3 * 86_400_000).toISOString(), previsao: null, motivo: null,
    precisaDoSolicitante: false, quotationId: 'q1', quotationNumber: 'RFQ-2026-000001',
    purchaseOrderId: null, purchaseOrderNumber: null, fornecedor: 'Alfa', ...p,
  });

  it('a linha do tempo diz onde a SC está, com quem e há quanto tempo', async () => {
    vi.mocked(listarSolicitacoes).mockResolvedValue(pagina([
      sc({ number: 'SC-2026-000005', status: 'SUBMITTED', processStatusLabel: 'Aguardando Aprovação', processStatusTone: 'teal', acompanhamento: naAlcada() }),
    ]));
    montar();
    const linha = within((await screen.findByText('SC-2026-000005')).closest('tr')!);
    expect(linha.getByText('Aguardando Aprovação')).toBeInTheDocument();           // a etiqueta
    expect(linha.getByText(/Aguardando aprovação de Gustavo/)).toBeInTheDocument(); // a frase
    expect(linha.getByText(/há 3 dias/)).toBeInTheDocument();                      // a espera
    const tempo = linha.getByTestId('linha-do-tempo-sc');
    expect(tempo.querySelector('[data-etapa="aprovacao"]')).toHaveAttribute('data-situacao', 'atual');
    expect(tempo.querySelector('[data-etapa="cotacao"]')).toHaveAttribute('data-situacao', 'feita');
    expect(tempo.querySelector('[data-etapa="entrega"]')).toHaveAttribute('data-situacao', 'pendente');
    // a frase antiga, derivada no navegador, sai de cena quando o servidor manda a dele
    expect(linha.queryByText(/aguardando a designação/)).not.toBeInTheDocument();
  });

  it('devolvida: mostra o motivo, e os botões dizem corrigir e reenviar', async () => {
    vi.mocked(listarSolicitacoes).mockResolvedValue(pagina([
      sc({ number: 'SC-2026-000006', status: 'RETURNED', acompanhamento: naAlcada({
        etapas: [{ chave: 'enviada', rotulo: 'Enviada', situacao: 'parada', quando: null, quem: 'Gustavo' }],
        etapaAtual: 'enviada', frase: 'Devolvida por Gustavo para ajuste: corrija e reenvie.',
        motivo: 'faltou o orçamento', precisaDoSolicitante: true, comQuem: 'Ana',
      }) }),
    ]));
    montar();
    const linha = within((await screen.findByText('SC-2026-000006')).closest('tr')!);
    expect(linha.getByText(/Devolvida por Gustavo/)).toBeInTheDocument();
    expect(linha.getByText('Motivo: faltou o orçamento')).toBeInTheDocument();
    expect(linha.getByRole('button', { name: 'Corrigir' })).toBeInTheDocument();
    expect(linha.getByRole('button', { name: 'Reenviar' })).toBeInTheDocument();
    expect(screen.getByTestId('resumo-solicitacoes')).toHaveTextContent('1 esperando por você');
  });

  it('pedido fechado: a previsão de chegada, o fornecedor e o pedido ficam na linha', async () => {
    vi.mocked(listarSolicitacoes).mockResolvedValue(pagina([
      sc({ number: 'SC-2026-000007', status: 'APPROVED', acompanhamento: naAlcada({
        etapaAtual: 'entrega', frase: 'Pedido fechado com Alfa, aguardando o faturamento. Chega até 28/09/2026.',
        previsao: '2026-09-28', purchaseOrderId: 'po1', purchaseOrderNumber: 'PO-2026-000003', fornecedor: 'Alfa',
      }) }),
    ]));
    montar();
    const linha = within((await screen.findByText('SC-2026-000007')).closest('tr')!);
    expect(linha.getByText(/previsão de chegada 28\/09\/2026/)).toBeInTheDocument();
    expect(linha.getByText(/pedido PO-2026-000003/)).toBeInTheDocument();
    // em andamento, sem nada com o solicitante
    expect(screen.getByTestId('resumo-solicitacoes')).toHaveTextContent('nada esperando por você');
    expect(screen.getByTestId('resumo-solicitacoes')).toHaveTextContent('1 em andamento');
  });

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
    await waitFor(() => expect(screen.getByText(/Nenhuma SC ainda/)).toBeInTheDocument());
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
    expect(screen.getByRole('heading', { name: /Editar a SC SC-2026-000001/ })).toBeInTheDocument();
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
