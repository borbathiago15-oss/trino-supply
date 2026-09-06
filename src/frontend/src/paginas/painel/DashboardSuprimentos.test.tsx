import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Usuario } from '@/api/auth';
import { consultaDoPainel, FILTROS_PAINEL_VAZIOS, variacao, type DashboardSuprimentos as Dados } from '@/api/painel';
import { ToastProvider } from '@/componentes/Toast';
import { AvisosProvider } from '@/sessao/AvisosProvider';
import { DashboardSuprimentos, podeVerAnalises } from './DashboardSuprimentos';

vi.mock('@/api/painel', async (importar) => ({
  ...(await importar<typeof import('@/api/painel')>()),
  dashboardDeSuprimentos: vi.fn(), listarAvisos: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import { dashboardDeSuprimentos, listarAvisos } from '@/api/painel';

let eu: Usuario = {
  id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'],
};

const dados = (p: Partial<Dados>): Dados => ({
  from: '2026-01-01', to: '2026-09-30',
  kpis: {
    prCount: 12, prPrevCount: 10, prTotalValue: 45000, approvedCount: 8, approvedValue: 30000,
    pendingApproval: 3, overdue: 1, avgApprovalDays: 2.4, poCount: 6, poTotalValue: 28000,
    poOpen: 2, poLate: 1, avgReceiveDays: 9.1,
  },
  months: [
    { month: '2026-08', created: 5, approved: 3, inApproval: 1, returned: 1, rejectedOrCancelled: 0, draft: 0, poValue: 12000 },
    { month: '2026-09', created: 7, approved: 5, inApproval: 2, returned: 0, rejectedOrCancelled: 0, draft: 0, poValue: 16000 },
  ],
  rankings: {
    suppliers: [{ label: 'Alfa EPIs', value: 28000, count: 6 }], buyers: [], requesters: [],
    families: [], categories: [], costCenters: [], regions: [], managers: [], clients: [],
  },
  supplierTable: [{ supplier: 'Alfa EPIs', orders: 6, quantity: 120, value: 28000, open: 2, otifMeasured: 4, otifPercent: 75 }],
  leadTimes: [],
  saving: { total: 0, baseline: 0, closed: 0, processes: 0, percent: 0, referenceTotal: 0, referenceOrders: 0, items: [] },
  buyerPanel: [{ label: 'Carla', processes: 4, closed: 3, savingTotal: 900, poValue: 28000, avgDaysToPo: 5.2, backlog: 1, otifPercent: 92, compositeScore: 88 }],
  filterOptions: {
    suppliers: [{ id: 's1', label: 'Alfa EPIs' }], buyers: [], requesters: [],
    families: ['EPI'], costCenters: [{ code: 'BAH-001', name: 'Obra Bahia' }],
    regions: [], managers: [], clients: [],
  },
  ...p,
});

const abrir = () => render(
  <MemoryRouter><AvisosProvider><ToastProvider><DashboardSuprimentos /></ToastProvider></AvisosProvider></MemoryRouter>,
);

describe('regras do painel', () => {
  it('a variação compara com o período anterior e trata a base zero', () => {
    expect(variacao(12, 10)).toMatchObject({ pct: 20, sinal: '▲' });
    expect(variacao(8, 10)).toMatchObject({ pct: -20, sinal: '▼' });
    expect(variacao(5, 0)).toMatchObject({ pct: 100 });
    expect(variacao(0, 0)).toMatchObject({ pct: 0, sinal: '•' });
  });
  it('só os filtros preenchidos entram na consulta', () => {
    expect(consultaDoPainel(FILTROS_PAINEL_VAZIOS).toString()).toBe('');
    expect(consultaDoPainel({ ...FILTROS_PAINEL_VAZIOS, de: '2026-01-01', centroCusto: 'BAH-001' }).toString())
      .toBe('from=2026-01-01&costCenter=BAH-001');
  });
  it('as análises exigem papel e módulo, como no clássico', () => {
    expect(podeVerAnalises({ role: 'PurchasingOfficer', modules: ['COMPRAS'] })).toBe(true);
    expect(podeVerAnalises({ role: 'Auditor', modules: ['COMPRAS'] })).toBe(true);
    expect(podeVerAnalises({ role: 'PurchasingOfficer', modules: ['PRODUTOS'] })).toBe(false);
    expect(podeVerAnalises({ role: 'Requester', modules: ['SOLICITACOES'] })).toBe(false);
  });
});

describe('tela Dashboard de Suprimentos', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'] };
    vi.mocked(listarAvisos).mockResolvedValue([]);
  });

  it('mostra os KPIs e a variação contra o período anterior', async () => {
    vi.mocked(dashboardDeSuprimentos).mockResolvedValue(dados({}));
    abrir();
    expect(await screen.findByText(/20% vs período anterior/)).toBeInTheDocument();
    expect(screen.getByTestId('painel-comprador')).toBeInTheDocument();
  });

  it('o filtro só recarrega quando “Aplicar” é clicado', async () => {
    const usuario = userEvent.setup();
    vi.mocked(dashboardDeSuprimentos).mockResolvedValue(dados({}));
    abrir();
    await screen.findByTestId('painel-comprador');
    expect(dashboardDeSuprimentos).toHaveBeenCalledTimes(1);

    await usuario.selectOptions(screen.getByLabelText('Centro de custo'), 'BAH-001');
    expect(dashboardDeSuprimentos).toHaveBeenCalledTimes(1);

    await usuario.click(screen.getByRole('button', { name: 'Aplicar filtros' }));
    await waitFor(() => expect(dashboardDeSuprimentos).toHaveBeenCalledWith(
      expect.objectContaining({ centroCusto: 'BAH-001' }), expect.anything()));
  });

  it('OTIF baixo do fornecedor sai marcado, e sem medição fica dito', async () => {
    vi.mocked(dashboardDeSuprimentos).mockResolvedValue(dados({
      supplierTable: [
        { supplier: 'Alfa', orders: 2, quantity: 10, value: 100, open: 0, otifMeasured: 2, otifPercent: 50 },
        { supplier: 'Beta', orders: 1, quantity: 5, value: 50, open: 0, otifMeasured: 0, otifPercent: null },
      ],
    }));
    abrir();
    const tabela = await screen.findByTestId('tabela-fornecedores');
    expect(within(tabela).getByText('50%')).toHaveClass('bg-perigo-fundo');
    expect(within(tabela).getByText('sem medição')).toBeInTheDocument();
  });

  it('sem ganho no período, o painel de saving explica onde ele é apurado', async () => {
    vi.mocked(dashboardDeSuprimentos).mockResolvedValue(dados({}));
    abrir();
    expect(await screen.findByText(/Nenhuma negociação com ganho registrada/)).toBeInTheDocument();
    expect(screen.queryByTestId('tabela-saving')).not.toBeInTheDocument();
  });

  it('quem não vê análises fica só com a Central de Avisos', async () => {
    eu = { id: 'u5', email: 'ana@t.com', name: 'Ana', role: 'Requester', modules: ['SOLICITACOES'] };
    vi.mocked(listarAvisos).mockResolvedValue([
      { kind: 'DEVOLVIDO', severity: 'alta', count: 2, view: 'pr-mine', text: '2 pedido(s) devolvido(s) para ajuste.' },
    ]);
    abrir();
    expect(await screen.findByTestId('lista-avisos')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aplicar filtros' })).not.toBeInTheDocument();
    expect(dashboardDeSuprimentos).not.toHaveBeenCalled();
  });
});
