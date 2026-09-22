import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Processo } from '@/api/cotacoes';
import { processo } from '@/test/cotacoes';
import { filtrosAtivos, origemDe, ProcessosDeCotacao, vencedorDe } from './ProcessosDeCotacao';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  listarProcessos: vi.fn(),
}));

import { listarProcessos } from '@/api/cotacoes';

const abrir = () => render(<MemoryRouter><ProcessosDeCotacao /></MemoryRouter>);

const pagina = (itens: Processo[], total = itens.length, opcoes = OPCOES) => ({ itens, total, opcoes });
const OPCOES = {
  costCenters: ['CC-01', 'PER-001'],
  createdBy: [{ id: 'u1', label: 'Carla Compradora' }, { id: 'u2', label: 'Caio Comprador' }],
};

describe('leituras da lista', () => {
  it('o vencedor sai da seleção, quando já houve escolha', () => {
    expect(vencedorDe(processo({}))).toBeNull();
    expect(vencedorDe(processo({
      selection: { winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: 'Preço', justification: 'menor preço', by: 'u9', byLabel: 'Carla' },
    }))).toBe('Alfa EPIs');
  });
  it('a origem aceita uma SC ou o agrupamento de várias', () => {
    expect(origemDe(processo({}))).toEqual(['SC-2026-000001']);
    expect(origemDe(processo({ sourcePrNumbers: ['SC-1', 'SC-2'] }))).toEqual(['SC-1', 'SC-2']);
    expect(origemDe(processo({ sourcePrNumbers: [], sourcePrNumber: null }))).toEqual([]);
  });
});

describe('filtrosAtivos', () => {
  const nenhum = { busca: '', situacao: '', de: '', ate: '', centroCusto: '', abertoPor: '' };
  it('sem recorte, a tela diz "ainda não há", e não "nada neste filtro"', () => {
    expect(filtrosAtivos(nenhum)).toBe(false);
    expect(filtrosAtivos({ ...nenhum, busca: '   ' })).toBe(false);
  });
  it('qualquer um dos seis recortes conta', () => {
    expect(filtrosAtivos({ ...nenhum, centroCusto: 'CC-01' })).toBe(true);
    expect(filtrosAtivos({ ...nenhum, abertoPor: 'u1' })).toBe(true);
    expect(filtrosAtivos({ ...nenhum, de: '2026-01-01' })).toBe(true);
    expect(filtrosAtivos({ ...nenhum, ate: '2026-01-31' })).toBe(true);
  });
});

describe('tela Processos de cotação', () => {
  beforeEach(() => vi.resetAllMocks());

  it('os três recortes novos vão para o servidor', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarProcessos).mockResolvedValue(pagina([processo({})]));
    abrir();
    await screen.findByTestId('tabela-processos');

    await usuario.selectOptions(screen.getByLabelText('Centro de custo'), 'CC-01');
    await waitFor(() => expect(listarProcessos).toHaveBeenCalledWith(
      expect.objectContaining({ centroCusto: 'CC-01' }), expect.anything()));

    await usuario.selectOptions(screen.getByLabelText('Aberto por'), 'u1');
    await waitFor(() => expect(listarProcessos).toHaveBeenCalledWith(
      expect.objectContaining({ abertoPor: 'u1' }), expect.anything()));

    await usuario.type(screen.getByLabelText('Aberto de'), '2026-01-01');
    await waitFor(() => expect(listarProcessos).toHaveBeenCalledWith(
      expect.objectContaining({ de: '2026-01-01' }), expect.anything()));
  });

  it('as opções dos filtros vêm com a lista, sem uma segunda chamada', async () => {
    vi.mocked(listarProcessos).mockResolvedValue(pagina([processo({})]));
    abrir();
    await screen.findByTestId('tabela-processos');
    // a tela abre com as caixas já preenchidas, em vez de povoar depois piscando
    expect(screen.getByRole('option', { name: 'PER-001' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Carla Compradora' })).toBeInTheDocument();
  });

  it('limpar só aparece quando há o que limpar, e devolve tudo ao servidor', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarProcessos).mockResolvedValue(pagina([processo({})]));
    abrir();
    await screen.findByTestId('tabela-processos');
    expect(screen.queryByRole('button', { name: 'Limpar' })).not.toBeInTheDocument();

    await usuario.selectOptions(screen.getByLabelText('Centro de custo'), 'CC-01');
    await usuario.click(await screen.findByRole('button', { name: 'Limpar' }));
    await waitFor(() => expect(listarProcessos).toHaveBeenCalledWith(
      expect.objectContaining({ centroCusto: '', abertoPor: '', de: '', ate: '' }), expect.anything()));
  });

  it('mostra a situação com o rótulo do time, não o código da API', async () => {
    vi.mocked(listarProcessos).mockResolvedValue(pagina([
      processo({ status: 'AGUARDANDO_GERENTE' }),
      processo({ id: 'q2', number: 'RFQ-2026-000002', status: 'OC_REGISTRADA' }),
    ]));
    abrir();
    const tabela = await screen.findByTestId('tabela-processos');
    expect(within(tabela).getByText('Aguardando Aprovador 01')).toBeInTheDocument();
    expect(within(tabela).getByText('O.C. registrada')).toBeInTheDocument();
  });

  it('o filtro de situação vai para o servidor, e não peneira a lista carregada', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarProcessos).mockResolvedValue(pagina([
      processo({ status: 'EM_ANALISE' }),
      processo({ id: 'q2', number: 'RFQ-2026-000002', status: 'REJEITADO' }),
    ]));
    abrir();
    await screen.findByTestId('tabela-processos');
    expect(screen.getAllByRole('link', { name: 'Abrir processo' })).toHaveLength(2);

    vi.mocked(listarProcessos).mockResolvedValue(pagina([
      processo({ id: 'q2', number: 'RFQ-2026-000002', status: 'REJEITADO' }),
    ]));
    await usuario.selectOptions(screen.getByLabelText('Situação do processo'), 'REJEITADO');
    await waitFor(() => expect(listarProcessos).toHaveBeenCalledWith(
      expect.objectContaining({ situacao: 'REJEITADO' }), expect.anything()));
    await waitFor(() => expect(screen.getAllByRole('link', { name: 'Abrir processo' })).toHaveLength(1));
  });

  it('a busca também vai para o servidor, e a tela diz quantos existem', async () => {
    vi.mocked(listarProcessos).mockResolvedValue(pagina([processo({})], 430));
    abrir();
    await waitFor(() => expect(screen.getByTestId('contagem-processos'))
      .toHaveTextContent('Mostrando 1 de 430'));

    await userEvent.type(screen.getByLabelText('Buscar'), 'martelete');
    await waitFor(() => expect(listarProcessos).toHaveBeenCalledWith(
      expect.objectContaining({ busca: 'martelete' }), expect.anything()));

    // e oferece o resto em vez de fingir que a lista acabou
    await userEvent.click(screen.getByRole('button', { name: 'Carregar mais' }));
    await waitFor(() => expect(listarProcessos).toHaveBeenCalledWith(
      expect.objectContaining({ tamanho: 100 }), expect.anything()));
  });

  it('agrupamento de SCs e compra dividida aparecem na linha', async () => {
    vi.mocked(listarProcessos).mockResolvedValue(pagina([
      processo({
        sourcePrNumbers: ['SC-1', 'SC-2'], splitAward: true,
        awards: [
          { id: 'a1', family: 'EPI', quotationItemId: null, supplierId: 's1', supplierName: 'Alfa', proposalId: 'p1', proposalVersion: 1, itemsValue: 100, totalValue: 120, criteria: null, justification: null, byLabel: null, purchaseOrderId: null, purchaseOrderNumber: null },
          { id: 'a2', family: 'FERRAMENTA', quotationItemId: null, supplierId: 's2', supplierName: 'Beta', proposalId: 'p2', proposalVersion: 1, itemsValue: 80, totalValue: 90, criteria: null, justification: null, byLabel: null, purchaseOrderId: null, purchaseOrderNumber: null },
        ],
      }),
    ]));
    abrir();
    const tabela = await screen.findByTestId('tabela-processos');
    expect(within(tabela).getByText(/agrupamento de 2 SCs/)).toBeInTheDocument();
    expect(within(tabela).getByText(/compra dividida em 2 família/)).toBeInTheDocument();
  });

  it('o link abre o processo dentro do React', async () => {
    vi.mocked(listarProcessos).mockResolvedValue(pagina([processo({})]));
    abrir();
    expect(await screen.findByRole('link', { name: 'Abrir processo' })).toHaveAttribute('href', '/cotacoes/q1');
  });

  it('sem filtro, a lista vazia diz onde se abre o primeiro processo', async () => {
    vi.mocked(listarProcessos).mockResolvedValue(pagina([]));
    abrir();
    expect(await screen.findByText(/Nenhum processo de cotação ainda/)).toBeInTheDocument();
  });
});
