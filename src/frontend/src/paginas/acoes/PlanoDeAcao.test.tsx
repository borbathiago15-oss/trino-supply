import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Plano } from '@/api/acoes';
import { ToastProvider } from '@/componentes/Toast';
import { PlanoDeAcaoTela, rotuloDaLinha, tomDaLinha } from './PlanoDeAcao';

vi.mock('@/api/acoes', async (importar) => ({
  ...(await importar<typeof import('@/api/acoes')>()),
  listarPlanos: vi.fn(),
  criarPlano: vi.fn(),
}));
import { criarPlano, listarPlanos } from '@/api/acoes';

const plano = (p: Partial<Plano>): Plano => ({
  id: 'p-' + (p.code ?? 'AP-2026-001'), code: 'AP-2026-001',
  title: 'Reduzir avarias na doca 2', description: null,
  costCenter: 'BAH-001', areas: ['Operações'], otherArea: null,
  priority: 'ALTA', criticality: 'ALTA', complexity: 'MEDIA',
  category: 'Melhoria Contínua', sponsor: null, managerName: null,
  startDate: '2026-09-01', dueDate: '2026-12-31', completion: 0,
  problem: 'Avarias sobem desde julho', businessReason: 'Custa 40 mil por mês',
  operationalImpact: null, financialImpact: null, kpiAffected: null, targetGoal: null,
  roiExpected: 0, savingExpected: 10000, savingRealized: 4000,
  investmentPlanned: 0, investmentActual: 2000,
  cancelled: false, cancelReason: null, life: 'ATIVO',
  closedAt: null, closedByLabel: null, evidenceNote: null,
  cycleId: null, autoKey: null,
  responsibles: [{ userId: 'u1', label: 'Ana' }],
  status: 'EM_ANDAMENTO', itemsProgress: 40,
  savingTotalExpected: 15000, savingTotalRealized: 4000, roi: 100,
  closedWithPending: false, itemCount: 2, openItems: 1,
  createdByLabel: 'Ana', createdAt: '2026-09-01T00:00:00Z', ...p,
});

const resposta = (itens: Plano[]) => ({
  items: itens,
  placar: {
    total: itens.length, pendentes: 0, emAndamento: 1, atrasados: 1,
    concluidos: 0, cancelados: 0, encerrados: 1, encerradosComPendencia: 1,
    savingEsperado: 15000, savingRealizado: 4000,
  },
  filterOptions: {
    responsibles: [{ id: 'u1', label: 'Ana' }],
    costCenters: ['BAH-001'],
    areas: ['Operações', 'RH'],
    categories: ['Melhoria Contínua'],
    priorities: ['ALTA', 'MEDIA', 'BAIXA'],
    degrees: ['BAIXA', 'MEDIA', 'ALTA'],
    riskDegrees: ['BAIXA', 'MEDIA', 'ALTA', 'MUITO_ALTA'],
    statuses: ['PENDENTE', 'EM_ANDAMENTO', 'ATRASADO', 'CONCLUIDO', 'CANCELADO'],
  },
});

const montar = () => render(
  <MemoryRouter><ToastProvider><PlanoDeAcaoTela /></ToastProvider></MemoryRouter>);

describe('a linha do plano', () => {
  it('encerrado diz "encerrado", e não a situação do trabalho', () => {
    // a pergunta que interessa no plano encerrado é outra: alguém decidiu parar, e quando
    const encerrado = plano({ life: 'ENCERRADO', status: 'ATRASADO' });
    expect(rotuloDaLinha(encerrado)).toBe('Encerrado');
    expect(tomDaLinha(encerrado)).toContain('slate');
  });

  it('encerrado com pendência é dito na própria linha', () => {
    expect(rotuloDaLinha(plano({ life: 'ENCERRADO', closedWithPending: true })))
      .toBe('Encerrado com pendência');
  });

  it('atrasado fala mais alto que a situação', () => {
    expect(tomDaLinha(plano({ status: 'ATRASADO' }))).toContain('perigo');
    expect(rotuloDaLinha(plano({ status: 'ATRASADO' }))).toBe('Atrasado');
  });
});

describe('<PlanoDeAcaoTela />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listarPlanos).mockResolvedValue(resposta([
      plano({}),
      plano({ code: 'AP-2026-002', title: 'Reduzir retrabalho', status: 'ATRASADO' }),
    ]));
    vi.mocked(criarPlano).mockResolvedValue(plano({ code: 'AP-2026-003' }));
  });

  it('lista os planos com o problema que cada um ataca', async () => {
    montar();
    const tabela = await screen.findByTestId('tabela-planos');
    expect(within(tabela).getByText('Reduzir avarias na doca 2')).toBeInTheDocument();
    // o problema aparece na linha: é o que separa o plano de uma lista de tarefas
    expect(within(tabela).getAllByText('Avarias sobem desde julho').length).toBeGreaterThan(0);
  });

  it('o placar mostra os encerrados com pendência', async () => {
    montar();
    await screen.findByTestId('tabela-planos');
    expect(screen.getByText(/1 com ação em aberto/)).toBeInTheDocument();
  });

  it('a linha mostra quantas ações fecharam, e não só o total', async () => {
    montar();
    const tabela = await screen.findByTestId('tabela-planos');
    const linha = tabela.querySelector('[data-plano="AP-2026-001"]')!;
    expect(within(linha as HTMLElement).getByText('1/2')).toBeInTheDocument();
  });

  it('filtra por situação e o limpar só aparece quando há filtro', async () => {
    montar();
    await screen.findByTestId('tabela-planos');
    expect(screen.queryByRole('button', { name: 'Limpar' })).toBeNull();

    await userEvent.selectOptions(screen.getByLabelText('Situação'), 'ATRASADO');
    await waitFor(() => expect(listarPlanos).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'ATRASADO' }), expect.anything()));
    expect(screen.getByRole('button', { name: 'Limpar' })).toBeInTheDocument();
  });

  it('abrir um plano já pede o problema e o porquê', async () => {
    // plano sem os dois vira lista de tarefas, e é o que este módulo existe para não ser
    montar();
    await screen.findByTestId('tabela-planos');
    await userEvent.type(screen.getByLabelText(/O que o plano vai resolver/), 'Reduzir retrabalho na doca');
    await userEvent.type(screen.getByLabelText(/Problema encontrado/), 'Retrabalho em 3 de 10 cargas');
    await userEvent.type(screen.getByLabelText(/Por quê/), 'Perde-se meio turno por dia');
    await userEvent.click(screen.getByRole('button', { name: 'Abrir plano' }));

    await waitFor(() => expect(criarPlano).toHaveBeenCalledWith({
      title: 'Reduzir retrabalho na doca',
      problem: 'Retrabalho em 3 de 10 cargas',
      businessReason: 'Perde-se meio turno por dia',
    }));
  });
});
