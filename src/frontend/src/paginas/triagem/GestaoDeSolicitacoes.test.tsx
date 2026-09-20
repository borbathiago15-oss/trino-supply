import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import { diasNaFila, FAIXAS_AGING, faixaDeAging, type Demanda, type ItemDemanda, type Responsavel } from '@/api/triagem';
import { ToastProvider } from '@/componentes/Toast';
import { contarPorFaixa, GestaoDeSolicitacoes, linhasDe } from './GestaoDeSolicitacoes';

vi.mock('@/api/triagem', async (importar) => ({
  ...(await importar<typeof import('@/api/triagem')>()),
  listarDemandas: vi.fn(), listarResponsaveis: vi.fn(),
  designar: vi.fn(), designarEmLote: vi.fn(), alterarPrioridade: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import {
  designar, designarEmLote, listarDemandas, listarResponsaveis,
} from '@/api/triagem';

let eu: Usuario = {
  id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'],
};

const HOJE = new Date('2026-09-10T12:00:00Z').getTime();
const diasAtras = (n: number) => new Date(HOJE - n * 86_400_000).toISOString();

const item = (p: Partial<ItemDemanda>): ItemDemanda => ({
  id: 'i1', sequence: 1, code: 'EPI-001', description: 'Luva nitrílica', size: 'M',
  quantity: 10, unitOfMeasure: 'PAR', family: 'EPI', quotationNumber: null, purchaseOrderNumber: null,
  processStatusLabel: null, processStatusTone: null, processStatusHint: null,
  ...p,
});

const demanda = (p: Partial<Demanda>): Demanda => ({
  kind: 'MR', id: 'mr1', number: 'SM-2026-000001', costCenter: 'BAH-001', requesterLabel: 'Ana',
  summary: '10× Luva', estimatedValue: 250, openedAt: diasAtras(1), status: 'SUBMITTED',
  processStatusLabel: 'Pendente', processStatusTone: '', processStatusHint: 'Aguardando o comprador.',
  splitProcesses: false, priority: 'NORMAL', neededBy: null, justification: 'Reposição de EPI',
  urgencyReason: null, urgencyImpact: null, priorityChangedByLabel: null, priorityChangeReason: null,
  assignedToId: null, assignedToLabel: null, assignedByLabel: null, items: [item({})],
  ...p,
});

const resposta = (itens: Demanda[]) => ({
  items: itens, total: itens.length,
  requesters: ['Ana'], assignees: ['Carla'],
  statuses: [{ key: 'PENDENTE', label: 'Pendente' }],
});

const equipe: Responsavel[] = [
  { id: 'u1', name: 'Carla', role: 'PurchasingOfficer' },
  { id: 'u2', name: 'Zé', role: 'WarehouseOperator' },
];

const abrir = () => render(<ToastProvider><GestaoDeSolicitacoes /></ToastProvider>);

describe('regras da fila de demandas', () => {
  it('a demanda sem itens detalhados ainda rende uma linha, com o resumo', () => {
    const linhas = linhasDe(demanda({ items: [], summary: 'Serviço de calibração' }));
    expect(linhas).toHaveLength(1);
    expect(linhas[0].description).toBe('Serviço de calibração');
  });
  it('o aging conta dias corridos desde a entrada na fila', () => {
    expect(diasNaFila(demanda({ openedAt: diasAtras(4) }), HOJE)).toBe(4);
    expect(diasNaFila(demanda({ openedAt: null }), HOJE)).toBe(0);
  });
  it('as quatro faixas de aging separam 0–2, 3–5, 6–10 e mais de 10 dias', () => {
    expect(faixaDeAging(demanda({ openedAt: diasAtras(0) }), HOJE)).toBe(0);
    expect(faixaDeAging(demanda({ openedAt: diasAtras(4) }), HOJE)).toBe(1);
    expect(faixaDeAging(demanda({ openedAt: diasAtras(8) }), HOJE)).toBe(2);
    expect(faixaDeAging(demanda({ openedAt: diasAtras(30) }), HOJE)).toBe(3);
  });
  it('conta quantas demandas caem em cada faixa', () => {
    expect(contarPorFaixa([
      demanda({ openedAt: diasAtras(1) }), demanda({ openedAt: diasAtras(2) }),
      demanda({ openedAt: diasAtras(30) }),
    ], HOJE)).toEqual([2, 0, 0, 1]);
  });
});

describe('tela Triagem de Demandas', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'] };
    vi.mocked(listarResponsaveis).mockResolvedValue(equipe);
  });

  it('uma demanda de dois itens vira duas linhas, com o cabeçalho só na primeira', async () => {
    vi.mocked(listarDemandas).mockResolvedValue(resposta([
      demanda({ items: [item({}), item({ id: 'i2', sequence: 2, description: 'Bota', code: 'EPI-002' })] }),
    ]));
    abrir();
    const tabela = await screen.findByTestId('tabela-demandas');
    expect(within(tabela).getAllByRole('row')).toHaveLength(3); // cabeçalho + 2 itens
    expect(within(tabela).getByText('SM-2026-000001/0001')).toBeInTheDocument();
    expect(within(tabela).getByText('SM-2026-000001/0002')).toBeInTheDocument();
    // só a primeira linha traz a caixa de seleção do lote
    expect(within(tabela).getAllByLabelText(/Selecionar SM-2026-000001/)).toHaveLength(1);
  });

  it('o filtro de situação recarrega pela API; o de tempo na fila filtra na tela', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarDemandas).mockResolvedValue(resposta([demanda({})]));
    abrir();
    await screen.findByTestId('tabela-demandas');

    await usuario.selectOptions(screen.getByLabelText('Situação'), 'PENDENTE');
    await waitFor(() => expect(listarDemandas).toHaveBeenCalledWith(
      expect.objectContaining({ situacao: 'PENDENTE' }), expect.anything()));
    await screen.findByTestId('tabela-demandas');

    // a faixa escolhida é uma em que a demanda NÃO está, calculada agora: a tela mede a idade
    // contra o relógio real, e uma faixa fixa passava a conter a demanda conforme os dias corriam
    const semADemanda = String((faixaDeAging(demanda({})) + 1) % FAIXAS_AGING.length);
    const antes = vi.mocked(listarDemandas).mock.calls.length;
    await usuario.selectOptions(await screen.findByLabelText('Tempo na fila'), semADemanda);
    expect(await screen.findByText('Nenhuma demanda neste filtro. ✔')).toBeInTheDocument();
    expect(vi.mocked(listarDemandas).mock.calls.length).toBe(antes);
  });

  it('designar um responsável manda o tipo da demanda junto', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarDemandas).mockResolvedValue(resposta([demanda({ kind: 'MR', id: 'mr9' })]));
    vi.mocked(designar).mockResolvedValue(undefined);
    abrir();

    await usuario.selectOptions(await screen.findByLabelText('Responsável por SM-2026-000001'), 'u2');
    await waitFor(() => expect(designar).toHaveBeenCalledWith('MR', 'mr9', 'u2'));
  });

  it('o lote só libera com demandas marcadas e um responsável escolhido', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarDemandas).mockResolvedValue(resposta([demanda({})]));
    vi.mocked(designarEmLote).mockResolvedValue({ assigned: 1, failed: [] });
    abrir();

    const botao = await screen.findByRole('button', { name: /Designar selecionadas \(0\)/ });
    expect(botao).toBeDisabled();

    await usuario.click(screen.getByLabelText(/Selecionar SM-2026-000001/));
    expect(screen.getByRole('button', { name: /Designar selecionadas \(1\)/ })).toBeDisabled();

    await usuario.selectOptions(screen.getByLabelText('Responsável do lote'), 'u2');
    await usuario.click(screen.getByRole('button', { name: /Designar selecionadas \(1\)/ }));
    await waitFor(() => expect(designarEmLote).toHaveBeenCalledWith([{ kind: 'MR', id: 'mr1' }], 'u2'));
  });

  // A mudança de prioridade saiu daqui: ela é da SOLICITAÇÃO DE COMPRA, e esta tela
  // passou a triar só material. Quem a exercita agora é a Torre, e a régua em si está
  // testada em DialogoDePrioridade.test.tsx, onde o componente compartilhado vive.

  it('quem não tria vê o responsável como texto e não recebe as ações', async () => {
    eu = { id: 'u5', email: 'ana@t.com', name: 'Ana', role: 'Requester', modules: ['SOLICITACOES'] };
    vi.mocked(listarDemandas).mockResolvedValue(resposta([demanda({ assignedToLabel: 'Carla' })]));
    abrir();

    const tabela = await screen.findByTestId('tabela-demandas');
    expect(within(tabela).getByText('Carla')).toBeInTheDocument();
    expect(screen.queryByLabelText('Responsável por SM-2026-000001')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Tornar Urgente' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Designar selecionadas/ })).not.toBeInTheDocument();
    expect(listarResponsaveis).not.toHaveBeenCalled();
  });

  it('SC dividida em processos mostra a situação por item', async () => {
    vi.mocked(listarDemandas).mockResolvedValue(resposta([demanda({
      splitProcesses: true,
      items: [
        item({ processStatusLabel: 'Em Cotação', processStatusTone: 'warn', quotationNumber: 'RFQ-1' }),
        item({ id: 'i2', sequence: 2, description: 'Bota', processStatusLabel: 'Pedido Entregue', processStatusTone: 'purple' }),
      ],
    })]));
    abrir();
    const tabela = await screen.findByTestId('tabela-demandas');
    expect(within(tabela).getByText('Em Cotação')).toBeInTheDocument();
    expect(within(tabela).getByText('Pedido Entregue')).toBeInTheDocument();
    expect(within(tabela).getByText('sem processo')).toBeInTheDocument();
  });
});
