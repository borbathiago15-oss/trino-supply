import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { consultaDeMaterial, FILTROS_MATERIAL_VAZIOS, type RelatorioDeMaterial } from '@/api/material';
import { MaterialNoPainel } from './MaterialNoPainel';

vi.mock('@/api/material', async (importar) => ({
  ...(await importar<typeof import('@/api/material')>()), analyticsDeMaterial: vi.fn(),
}));
import { analyticsDeMaterial } from '@/api/material';

const relatorio = (p: Partial<RelatorioDeMaterial> = {}): RelatorioDeMaterial => ({
  from: '2026-10-01', to: '2026-10-07',
  kpis: {
    requested: 4, requestedPrev: 2, requestedQty: 20, deliveredQty: 10, awaitingApproval: 1, inWarehouseQueue: 1,
    fulfilled: 1, partial: 0, rejected: 1, cancelled: 0, purchaseRouteItems: 0, avgApprovalHours: 2, avgFulfillDays: 2,
    avgTotalDays: 2.2, slaMetPercent: 100, slaMeasured: 1, slaBreachedOpen: 1,
  },
  months: [{ month: '2026-10', requested: 4, fulfilled: 1, rejected: 1 }],
  byCostCenter: [{ label: 'BAH-001 — Obra Bahia', key: 'BAH-001', count: 2, qty: 12, delivered: 10 },
    { label: 'BAH-002 — Obra Salvador', key: 'BAH-002', count: 2, qty: 8, delivered: 0 }],
  byFamily: [{ label: 'EPI', key: 'EPI', count: 3, qty: 15, delivered: 10 }],
  byProduct: [{ label: '[EPI-001] Luva nitrílica', key: 'p1', count: 3, qty: 15, delivered: 10 }],
  byRequester: [{ label: 'Ana', key: 'u1', count: 3, qty: 15, delivered: 10 }],
  slaByFamily: [{ family: 'EPI', maxDays: 2, measured: 1, met: 1, avgDays: 2 }],
  filterOptions: { costCenters: [{ code: 'BAH-001', name: 'Obra Bahia' }, { code: 'BAH-002', name: 'Obra Salvador' }], families: ['EPI', 'LIMPEZA'] },
  indicators: { awaitingApproval: 'Solicitações que esperam a aprovação do responsável do centro hoje.' },
  ...p,
});

const abrir = (props: Parameters<typeof MaterialNoPainel>[0] = {}) =>
  render(<MemoryRouter><MaterialNoPainel {...props} /></MemoryRouter>);

describe('a consulta do bloco de material', () => {
  it('só manda o que está preenchido, e o produto sem espaços', () => {
    expect(consultaDeMaterial(FILTROS_MATERIAL_VAZIOS)).toBe('');
    expect(consultaDeMaterial({ de: '2026-10-01', ate: '', centroCusto: 'BAH-001', familia: 'EPI', produto: ' luva ' }))
      .toBe('from=2026-10-01&costCenter=BAH-001&family=EPI&product=luva');
  });
});

describe('o bloco de material no Dashboard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(analyticsDeMaterial).mockResolvedValue(relatorio());
  });

  it('mostra os números, os rankings e o prazo por família', async () => {
    abrir();
    const kpis = await screen.findByTestId('kpis-material');
    expect(kpis).toHaveTextContent('Solicitadas');
    expect(kpis).toHaveTextContent('▲ 100% vs. período anterior');
    expect(kpis).toHaveTextContent('1 com prazo estourado');
    expect(kpis).toHaveTextContent('2 h');
    expect(kpis).toHaveTextContent('2,2 d');
    expect(kpis).toHaveTextContent('100%');
    expect(screen.getByText('Top solicitações por centro de custo')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /BAH-001 — Obra Bahia/ })).toBeInTheDocument();
    expect(screen.getByText('[EPI-001] Luva nitrílica')).toBeInTheDocument();
    expect(screen.getByText('Ana')).toBeInTheDocument();
    expect(within(screen.getByTestId('tabela-prazo-material')).getByText('1 de 1')).toBeInTheDocument();
    // o período do bloco é o que o servidor resolveu
    expect(screen.getByText('Período: 01/10/2026 a 07/10/2026.')).toBeInTheDocument();
  });

  it('os filtros são do bloco: só consultam ao aplicar, e tocar a barra do centro filtra na hora', async () => {
    const usuario = userEvent.setup();
    abrir();
    await screen.findByTestId('kpis-material');
    expect(analyticsDeMaterial).toHaveBeenCalledTimes(1);

    await usuario.selectOptions(screen.getByLabelText('Família'), 'EPI');
    await usuario.type(screen.getByLabelText(/Produto/), 'luva');
    expect(analyticsDeMaterial).toHaveBeenCalledTimes(1);
    await usuario.click(screen.getByRole('button', { name: 'Aplicar ao material' }));
    await waitFor(() => expect(analyticsDeMaterial).toHaveBeenCalledTimes(2));
    expect(vi.mocked(analyticsDeMaterial).mock.calls[1][0]).toMatchObject({ familia: 'EPI', produto: 'luva' });

    await usuario.click(screen.getByRole('button', { name: /BAH-002 — Obra Salvador/ }));
    await waitFor(() => expect(analyticsDeMaterial).toHaveBeenCalledTimes(3));
    expect(vi.mocked(analyticsDeMaterial).mock.calls[2][0]).toMatchObject({ centroCusto: 'BAH-002', familia: 'EPI' });
    // tocar de novo tira
    await usuario.click(screen.getByRole('button', { name: /BAH-002 — Obra Salvador/ }));
    await waitFor(() => expect(analyticsDeMaterial).toHaveBeenCalledTimes(4));
    expect(vi.mocked(analyticsDeMaterial).mock.calls[3][0]).toMatchObject({ centroCusto: '' });
  });

  it('sem medição, o tempo e o prazo aparecem como traço, não como zero', async () => {
    vi.mocked(analyticsDeMaterial).mockResolvedValue(relatorio({
      kpis: { ...relatorio().kpis, avgApprovalHours: null, avgFulfillDays: null, avgTotalDays: null, slaMetPercent: null, slaMeasured: 0, slaBreachedOpen: 0 },
      months: [{ month: '2026-10', requested: 0, fulfilled: 0, rejected: 0 }], slaByFamily: [],
    }));
    abrir();
    const kpis = await screen.findByTestId('kpis-material');
    expect(kpis).toHaveTextContent('Tempo até a aprovação—');
    expect(kpis).toHaveTextContent('Tempo do pedido à entrega—');
    expect(kpis).toHaveTextContent('Atendidas no prazo—sem atendimento medido');
    expect(screen.getByText('Nenhuma solicitação de material no período.')).toBeInTheDocument();
  });
});

describe('o bloco de material na Visão da diretoria', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(analyticsDeMaterial).mockResolvedValue(relatorio());
  });

  it('é compacto: sem formulário, com o período e o centro da página, quatro números e os rankings', async () => {
    abrir({ compacto: true, de: '2026-10-01', ate: '2026-10-07', centroCusto: 'BAH-001' });
    const numeros = await screen.findByTestId('numeros-de-material');
    expect(screen.queryByTestId('filtros-material')).not.toBeInTheDocument();
    expect(vi.mocked(analyticsDeMaterial).mock.calls[0][0]).toMatchObject({ de: '2026-10-01', ate: '2026-10-07', centroCusto: 'BAH-001' });
    expect(numeros).toHaveTextContent('Pendentes hoje');
    expect(numeros).toHaveTextContent('1 aguardando aprovação · 1 na fila do estoque');
    expect(numeros).toHaveTextContent('Atendidas no período');
    expect(numeros).toHaveTextContent('1 na fila com prazo estourado');
    expect(screen.getByText('Centros que mais pedem')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Painel de Atendimentos →' })).toHaveAttribute('href', '/estoque/atendimentos');
  });
});
