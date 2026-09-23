import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LinhaTco, RelatorioInsights } from '@/api/analytics';
import { Insights } from './Insights';

vi.mock('@/api/analytics', async (importar) => ({
  ...(await importar<typeof import('@/api/analytics')>()),
  relatorioDeInsights: vi.fn(), tcoPorProduto: vi.fn(),
}));
vi.mock('@/api/melhoria', () => ({ tratarAchado: vi.fn() }));

import { relatorioDeInsights, tcoPorProduto } from '@/api/analytics';
import { tratarAchado } from '@/api/melhoria';

const achado = {
  code: 'INS-01', kind: 'sobrepreco', severity: 'alta' as const,
  title: 'Sobrepreço em LUVA NITRÍLICA TAM. M',
  evidence: 'Pago R$ 12,40 contra R$ 8,90 no último pedido.',
  action: 'Renegocie com o fornecedor.', view: 'quotations',
};

const relatorio = (p: Partial<RelatorioInsights>): RelatorioInsights => ({
  months: 6,
  executive: {
    spend: 128000, orders: 14, processes: 9, closedProcesses: 6, savingTotal: 4200,
    referenceSavingTotal: 1800, costAvoidanceTotal: 6000, otifPercent: 87.5, complianceAverage: 82.5,
  },
  backlog: {
    total: 12, unassigned: 3,
    aging: [
      { label: '0–2 dias', count: 5 }, { label: '3–5 dias', count: 4 },
      { label: '6–10 dias', count: 2 }, { label: '+10 dias', count: 1 },
    ],
    byAssignee: [{ label: 'Carla', count: 7 }],
  },
  insights: [],
  ...p,
});

const tco: LinhaTco = {
  catalogItemId: 'p1', code: 'EPI-001', description: 'Luva nitrílica', family: 'EPI', category: 'Segurança',
  unitOfMeasure: 'PAR', quantity: 200, orders: 4, itemsValue: 2400, extrasValue: 300,
  tcoTotal: 2700, unitPriceAvg: 12, tcoUnitAvg: 13.5, extrasPercent: 11.1,
};

const abrir = () => render(<MemoryRouter><Insights /></MemoryRouter>);

describe('tela Insights & Executivo', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(tcoPorProduto).mockResolvedValue([]);
  });

  it('abre em 6 meses e refaz as duas leituras ao trocar a janela', async () => {
    const usuario = userEvent.setup();
    vi.mocked(relatorioDeInsights).mockResolvedValue(relatorio({}));
    abrir();
    await screen.findByText('Spend (O.C.s)');
    expect(relatorioDeInsights).toHaveBeenCalledWith(6, expect.anything());

    await usuario.selectOptions(screen.getByLabelText('Janela de análise'), '12');
    await waitFor(() => expect(relatorioDeInsights).toHaveBeenCalledWith(12, expect.anything()));
    await waitFor(() => expect(tcoPorProduto).toHaveBeenCalledWith(12, expect.anything()));
  });

  it('sem achado no período, diz o que foi verificado em vez de ficar em branco', async () => {
    vi.mocked(relatorioDeInsights).mockResolvedValue(relatorio({}));
    abrir();
    expect(await screen.findByText(/Nenhum achado no período/)).toBeInTheDocument();
    expect(screen.queryByTestId('lista-insights')).not.toBeInTheDocument();
  });

  it('cada achado mostra código, título, evidência e o que fazer', async () => {
    vi.mocked(relatorioDeInsights).mockResolvedValue(relatorio({
      insights: [
        { code: 'INS-01', kind: 'SOBREPRECO', severity: 'alta', title: 'Sobrepreço de 32% em Luva nitrílica', evidence: 'O.C. PO-1 (Alfa): pago 15,84 contra último preço 12,00.', action: 'Confira o pedido PO-1 e leve o preço anterior para a próxima negociação.', view: 'buy-orders' },
        { code: 'INS-04', kind: 'CONCENTRACAO', severity: 'media', title: 'Concentração em Alfa EPIs', evidence: '58% do spend da categoria Segurança.', action: 'Convide outros fornecedores homologados.', view: 'suppliers' },
      ],
    }));
    abrir();
    const lista = await screen.findByTestId('lista-insights');
    expect(within(lista).getByText('INS-01')).toBeInTheDocument();
    expect(within(lista).getByText(/pago 15,84 contra último preço/)).toBeInTheDocument();
    expect(within(lista).getByText('INS-04')).toBeInTheDocument();

    // descrever sem dizer o que fazer devolve o trabalho a quem lê (INTEL-A)
    expect(within(lista).getByText(/leve o preço anterior/)).toBeInTheDocument();
    const sobrepreco = within(lista.querySelector('[data-achado="INS-01"]') as HTMLElement);
    expect(sobrepreco.getByRole('link', { name: /Ir para a tela/ })).toHaveAttribute('href', '/pedidos');
    const concentracao = within(lista.querySelector('[data-achado="INS-04"]') as HTMLElement);
    expect(concentracao.getByRole('link', { name: /Ir para a tela/ })).toHaveAttribute('href', '/fornecedores');
  });

  it('achado sem destino mostra a providência, mas nenhum link', async () => {
    vi.mocked(relatorioDeInsights).mockResolvedValue(relatorio({
      insights: [
        { code: 'INS-09', kind: 'OUTRO', severity: 'info', title: 'Algo a olhar', evidence: 'evidência', action: 'Converse com o time.', view: null },
      ],
    }));
    abrir();
    const lista = await screen.findByTestId('lista-insights');
    expect(within(lista).getByText('Converse com o time.')).toBeInTheDocument();
    expect(within(lista).queryByRole('link', { name: /Ir para a tela/ })).not.toBeInTheDocument();
  });

  it('o backlog mostra as faixas de espera e quem está com o quê', async () => {
    vi.mocked(relatorioDeInsights).mockResolvedValue(relatorio({}));
    abrir();
    expect(await screen.findByText('0–2 dias: 5')).toBeInTheDocument();
    expect(screen.getByText('+10 dias: 1')).toBeInTheDocument();
    expect(within(screen.getByTestId('backlog-por-responsavel')).getByText('Carla')).toBeInTheDocument();
  });

  it('o TCO falhar não derruba o resto da tela', async () => {
    vi.mocked(relatorioDeInsights).mockResolvedValue(relatorio({}));
    vi.mocked(tcoPorProduto).mockRejectedValue(new Error('sem permissão'));
    abrir();
    expect(await screen.findByText('Spend (O.C.s)')).toBeInTheDocument();
    expect(await screen.findByText(/Nenhuma O.C. com produto de catálogo/)).toBeInTheDocument();
  });

  it('o TCO compara o preço unitário com o total de aquisição', async () => {
    vi.mocked(relatorioDeInsights).mockResolvedValue(relatorio({}));
    vi.mocked(tcoPorProduto).mockResolvedValue([tco]);
    abrir();
    const tabela = await screen.findByTestId('tabela-tco');
    expect(within(tabela).getByText('R$ 12,00')).toBeInTheDocument();
    expect(within(tabela).getByText('R$ 13,50')).toBeInTheDocument();
    expect(within(tabela).getByText('(11.1%)')).toBeInTheDocument();
  });

  // o achado aponta o problema com a evidência junto e parava aí: quem lia abria um
  // ciclo na mão e redigitava o que a tela já dizia
  describe('tratar a causa', () => {
    beforeEach(() => {
      vi.mocked(relatorioDeInsights).mockResolvedValue(relatorio({ insights: [achado] }));
      vi.mocked(tratarAchado).mockResolvedValue({
        cycle: { id: 'c1', code: 'PDCA-2026-001' } as never,
        planId: 'p1', planCode: 'AP-2026-001', alreadyExisted: false,
      });
    });

    it('o achado oferece tratar a causa, ao lado de ir para a tela', async () => {
      render(<MemoryRouter><Insights /></MemoryRouter>);
      const lista = await screen.findByTestId('lista-insights');
      expect(within(lista).getByRole('button', { name: 'Tratar a causa' })).toBeInTheDocument();
      expect(within(lista).getByRole('link', { name: /Ir para a tela/ })).toBeInTheDocument();
    });

    it('manda o achado inteiro, e por padrão abre o plano junto', async () => {
      render(<MemoryRouter><Insights /></MemoryRouter>);
      await screen.findByTestId('lista-insights');
      await userEvent.click(screen.getByRole('button', { name: 'Tratar a causa' }));
      await userEvent.click(screen.getByRole('button', { name: 'Abrir ciclo' }));

      await waitFor(() => expect(tratarAchado).toHaveBeenCalledWith({
        code: 'INS-01',
        title: 'Sobrepreço em LUVA NITRÍLICA TAM. M',
        evidence: 'Pago R$ 12,40 contra R$ 8,90 no último pedido.',
        action: 'Renegocie com o fornecedor.',
        createPlan: true,
      }));
    });

    it('dá para abrir só o ciclo, sem o plano', async () => {
      render(<MemoryRouter><Insights /></MemoryRouter>);
      await screen.findByTestId('lista-insights');
      await userEvent.click(screen.getByRole('button', { name: 'Tratar a causa' }));
      await userEvent.click(screen.getByLabelText(/plano da contramedida/));
      await userEvent.click(screen.getByRole('button', { name: 'Abrir ciclo' }));

      await waitFor(() => expect(tratarAchado).toHaveBeenCalledWith(
        expect.objectContaining({ createPlan: false })));
    });

    it('o diálogo mostra a evidência antes de decidir', async () => {
      render(<MemoryRouter><Insights /></MemoryRouter>);
      await screen.findByTestId('lista-insights');
      await userEvent.click(screen.getByRole('button', { name: 'Tratar a causa' }));
      const dialogo = screen.getByRole('dialog');
      expect(within(dialogo).getByText(/R\$ 8,90/)).toBeInTheDocument();
      expect(within(dialogo).getByText(/não abre dois ciclos/)).toBeInTheDocument();
    });
  });
});
