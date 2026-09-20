import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RelatorioInsights } from '@/api/analytics';
import type { ProcessoParaAprovar } from '@/api/cotacoes';
import type { RelatorioExecutivo } from '@/api/relatorios';
import { processo, proposta } from '@/test/cotacoes';
import { VisaoDaDiretoria } from './VisaoDaDiretoria';

vi.mock('@/api/relatorios', async (importar) => ({
  ...(await importar<typeof import('@/api/relatorios')>()), relatorioExecutivo: vi.fn(),
}));
vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()), processosParaMinhaAprovacao: vi.fn(),
}));
vi.mock('@/api/analytics', async (importar) => ({
  ...(await importar<typeof import('@/api/analytics')>()), relatorioDeInsights: vi.fn(),
}));

import { relatorioDeInsights } from '@/api/analytics';
import { processosParaMinhaAprovacao } from '@/api/cotacoes';
import { relatorioExecutivo } from '@/api/relatorios';

const relatorio = (): RelatorioExecutivo => ({
  from: '2026-09-01', to: '2026-09-20', companyLabel: null, costCenterLabel: null, buyerLabel: null, generatedAt: '',
  kpis: { spend: 20000, orders: 3, suppliers: 2, savingTotal: 2000, savingPercent: 16.7, urgentPercent: 30, otifPercent: 50, withoutErpValue: 2000 },
  coverage: { ordersWithoutPr: 0, valueWithoutPr: 0, capped: false, cap: 5000 },
  families: [{ family: 'EPI', value: 16000, quantity: 1, orders: 2, percent: 80 }],
  buyers: [], suppliers: { rows: [], supplierCount: 2, top1Percent: null, top3Percent: null, top5Percent: null },
  urgent: { orders: 1, value: 6000, percent: 30, items: [] }, otif: [],
  withoutErp: { orders: 1, value: 2000, percent: 10, pendingOrders: 0, pendingValue: 0, closedWithoutReason: 0, items: [] },
  filterOptions: { companies: [], costCenters: [], buyers: [] }, months: [],
  savingRulers: {
    negotiation: { processes: 1, baseline: 12000, closed: 10000, saving: 2000, percent: 16.7 },
    competition: { processes: 0, baseline: 0, closed: 0, saving: 0, percent: null },
    budget: { processes: 0, baseline: 0, closed: 0, saving: 0, percent: null },
  },
  previous: { from: '2026-08-01', to: '2026-08-31', spend: 16000, orders: 2, savingTotal: 2500, urgentPercent: 10, otifPercent: 40 },
  cycleTimes: [], savingByFamily: [], savingBySupplier: [],
  reference: { orders: 0, items: 0, gain: 0, loss: 0, net: 0, rows: [] },
  demand: { costCenters: [], requesters: [], scope: { materials: 0, services: 0, materialsPercent: 0, servicesPercent: 0 } },
  bids: { processes: 0, averageProponents: null, withCompetition: 0, winners: [] },
  payment: { weightedDays: null, ordersWithDays: 0, valueWithDays: 0, terms: [] },
  adherence: { orders: 0, formal: 0, value: 0, formalValue: 0, percent: null },
});

const pendente: ProcessoParaAprovar = {
  ...processo({
    status: 'AGUARDANDO_DIRETOR', justification: 'Reposição de EPI',
    proposals: [proposta({ id: 'p2', supplierId: 's2', supplierName: 'Beta Química', totalValue: 1200, isLatest: true })],
    selection: { winnerSupplierId: 's2', winnerProposalId: 'p2', criteria: null, justification: 'prazo', by: 'u-carla', byLabel: 'Carla' },
    managerApproval: { by: 'u-g', byLabel: 'Gustavo', at: '2026-09-15T10:00:00Z' },
  }),
  decisao: {
    requesterLabel: 'Ana', priority: 'URGENT', urgencyReason: null, urgencyImpact: null, neededBy: null, budget: null,
    level: 2, waitingSince: new Date(Date.now() - 6 * 86_400_000).toISOString(), complianceScore: 100, compliancePenalties: [], contractNumber: null,
  },
};

const insights = (achados: RelatorioInsights['insights']): RelatorioInsights => ({
  months: 3, executive: { spend: 0, orders: 0, processes: 0, closedProcesses: 0, savingTotal: 0, referenceSavingTotal: 0, costAvoidanceTotal: 0, otifPercent: null, complianceAverage: null },
  backlog: { total: 0, unassigned: 0, aging: [], byAssignee: [] }, insights: achados,
});

const abrir = () => render(<MemoryRouter><VisaoDaDiretoria /></MemoryRouter>);

describe('visão da diretoria', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([pendente]);
    vi.mocked(relatorioDeInsights).mockResolvedValue(insights([]));
  });

  it('cinco números com tendência, na ordem em que a diretoria pergunta', async () => {
    abrir();
    const numeros = within(await screen.findByTestId('numeros-da-diretoria'));
    expect(numeros.getByTestId('numero-gasto')).toHaveTextContent('R$ 20.000,00');
    expect(numeros.getByTestId('numero-gasto')).toHaveTextContent('▲ 25% vs. anterior');
    // gasto subindo é ruim; saving caindo é ruim
    expect(within(numeros.getByTestId('numero-gasto')).getByText(/vs\. anterior/)).toHaveClass('text-perigo');
    expect(numeros.getByTestId('numero-saving')).toHaveTextContent('▼ 20% vs. anterior');
    expect(within(numeros.getByTestId('numero-saving')).getByText(/vs\. anterior/)).toHaveClass('text-perigo');
    expect(numeros.getByTestId('numero-fila')).toHaveTextContent('1');
    expect(numeros.getByTestId('numero-fila')).toHaveTextContent('R$ 1.200,00 · a mais antiga há 6 dia(s)');
    expect(numeros.getByTestId('numero-excecoes')).toHaveTextContent('2');
    expect(numeros.getByTestId('numero-excecoes')).toHaveTextContent('1 sem O.C. do ERP · 1 urgente(s)');
    expect(numeros.getByTestId('numero-otif')).toHaveTextContent('50%');
    expect(numeros.getByTestId('numero-otif')).toHaveTextContent('▲ 25% vs. anterior');
    // as três frases, e o caminho para o relatório inteiro
    expect(screen.getByTestId('resumo-da-diretoria').querySelectorAll('li')).toHaveLength(3);
    expect(screen.getByRole('link', { name: /Relatórios completos/ })).toHaveAttribute('href', '/relatorios');
  });

  it('a fila de decisão fica logo abaixo e leva à Central', async () => {
    abrir();
    const fila = within(await screen.findByTestId('fila-da-diretoria'));
    expect(fila.getByText('RFQ-2026-000001')).toBeInTheDocument();
    expect(fila.getByText('URGENTE')).toBeInTheDocument();
    expect(fila.getByText('Beta Química')).toBeInTheDocument();
    expect(fila.getByText('6 dia(s)')).toBeInTheDocument();
    expect(fila.getByRole('link', { name: /Decidir/ })).toHaveAttribute('href', '/aprovacoes');
  });

  it('o período troca o recorte da leitura: mês, 90 dias, ano', async () => {
    abrir();
    await screen.findByTestId('numeros-da-diretoria');
    const primeira = vi.mocked(relatorioExecutivo).mock.calls[0][0];
    expect(primeira.de).toMatch(/-01$/);   // este mês começa no dia 1
    await userEvent.click(screen.getByRole('button', { name: 'Este ano' }));
    await waitFor(() => expect(relatorioExecutivo).toHaveBeenLastCalledWith(
      expect.objectContaining({ de: expect.stringMatching(/-01-01$/) }), expect.anything()));
    expect(screen.getByRole('button', { name: 'Este ano' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('sem fila e sem achado, diz isso em uma linha cada', async () => {
    vi.mocked(processosParaMinhaAprovacao).mockResolvedValue([]);
    abrir();
    expect(await screen.findByTestId('fila-vazia')).toHaveTextContent('Nada aguardando a sua aprovação agora.');
    expect(screen.getByTestId('sem-achados')).toBeInTheDocument();
    expect(screen.getByTestId('numero-fila')).toHaveTextContent('fila limpa');
  });

  it('os achados aparecem com código, título e o caminho para agir', async () => {
    vi.mocked(relatorioDeInsights).mockResolvedValue(insights([
      { code: 'INS-01', kind: 'sobrepreco', severity: 'alta', title: 'Sobrepreço em Luva', evidence: '+22% vs último preço', action: 'Renegociar', view: 'quotations' },
    ]));
    abrir();
    const achados = within(await screen.findByTestId('achados-da-diretoria'));
    expect(achados.getByText('Sobrepreço em Luva')).toBeInTheDocument();
    expect(achados.getByRole('link', { name: /ver/ })).toHaveAttribute('href', '/cotacoes');
  });
});
