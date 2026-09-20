import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RelatorioExecutivo } from '@/api/relatorios';
import { Relatorios } from './Relatorios';

vi.mock('@/api/relatorios', async (importar) => ({
  ...(await importar<typeof import('@/api/relatorios')>()),
  relatorioExecutivo: vi.fn(), pdfDoRelatorio: vi.fn(),
}));
vi.mock('@/api/cliente', async (importar) => ({
  ...(await importar<typeof import('@/api/cliente')>()),
  abrirBlob: vi.fn(),
}));

import { abrirBlob } from '@/api/cliente';
import { pdfDoRelatorio, relatorioExecutivo } from '@/api/relatorios';

const relatorio = (p: Partial<RelatorioExecutivo> = {}): RelatorioExecutivo => ({
  from: '2026-08-01', to: '2026-08-31',
  companyLabel: null, costCenterLabel: null, buyerLabel: null,
  generatedAt: '2026-08-24T12:00:00Z',
  kpis: {
    spend: 20000, orders: 3, suppliers: 2, savingTotal: 2000,
    savingPercent: 16.7, urgentPercent: 30, otifPercent: 50, withoutErpValue: 3000,
  },
  coverage: { ordersWithoutPr: 0, valueWithoutPr: 0, capped: false, cap: 5000 },
  families: [
    { family: 'MATERIAL DE LIMPEZA', value: 16000, quantity: 200, orders: 2, percent: 80 },
    { family: 'EPI', value: 4000, quantity: 40, orders: 1, percent: 20 },
  ],
  buyers: [{
    buyer: 'Carla Compradora', processes: 1, baseline: 12000, closed: 10000,
    saving: 2000, savingPercent: 16.7, spend: 10000, orders: 1,
  }],
  suppliers: {
    rows: [{ supplier: 'Alfa', orders: 2, value: 16000, percent: 80 }],
    supplierCount: 2, top1Percent: 80, top3Percent: 100, top5Percent: 100,
  },
  urgent: {
    orders: 1, value: 6000, percent: 30,
    items: [{
      number: 'PO-2026-000001', prNumber: 'PR-2026-000001', supplier: 'Alfa', value: 6000,
      issuedOn: '2026-08-10', requester: 'Ana', reason: 'parada de linha', impact: 'para a operação',
    }],
  },
  otif: [{ supplier: 'Alfa', measured: 2, onTimePercent: 50, inFullPercent: 100, otifPercent: 50 }],
  withoutErp: {
    orders: 1, value: 2000, percent: 10,
    pendingOrders: 3, pendingValue: 4500, closedWithoutReason: 1,
    items: [
      {
        number: 'PO-2026-000002', supplier: 'Beta', value: 2000, issuedOn: '2026-08-12',
        buyer: 'Carla Compradora', costCenter: 'CC-NE-01', reason: 'ERP indisponível na emissão',
      },
      {
        number: 'PO-2026-000003', supplier: 'Gama', value: 1000, issuedOn: '2026-08-14',
        buyer: 'Carla Compradora', costCenter: null, reason: null,
      },
    ],
  },
  filterOptions: {
    companies: ['Trino Nordeste LTDA', 'Trino São Paulo LTDA'],
    costCenters: [{ code: 'CC-NE-01', name: 'Filial Recife' }],
    buyers: [{ id: 'b1', label: 'Carla Compradora' }],
  },
  months: [
    { month: '2026-07', spend: 0, orders: 0, processes: 0, saving: 0, savingPercent: null },
    { month: '2026-08', spend: 20000, orders: 3, processes: 1, saving: 2000, savingPercent: 16.7 },
  ],
  savingRulers: {
    negotiation: { processes: 1, baseline: 12000, closed: 10000, saving: 2000, percent: 16.7 },
    competition: { processes: 1, baseline: 15000, closed: 10000, saving: 5000, percent: 33.3 },
    budget: { processes: 0, baseline: 0, closed: 0, saving: 0, percent: null },
  },
  previous: {
    from: '2026-07-01', to: '2026-07-31', spend: 16000, orders: 2, savingTotal: 2500,
    urgentPercent: 10, otifPercent: null,
  },
  cycleTimes: [
    { stage: 'solicitacao_escolha', title: 'Solicitação → escolha do fornecedor', measured: 1, medianDays: 4 },
    { stage: 'oc_recebimento', title: 'O.C. → recebimento', measured: 0, medianDays: null },
  ],
  ...p,
});

const abrir = () => render(<MemoryRouter><Relatorios /></MemoryRouter>);

describe('tela de Relatórios', () => {
  beforeEach(() => vi.resetAllMocks());

  it('cada KPI diz o que era no período anterior, e a tela diz que janela é essa', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();
    await screen.findByTestId('relatorio-familias');

    const antes = screen.getAllByTestId('antes');
    // total comprado: 20 mil contra 16 mil → 25% a mais; saving: 2 mil contra 2,5 mil → 20% a menos
    expect(antes[0]).toHaveTextContent('antes: R$ 16.000,00 (▲ 25%)');
    expect(antes[1]).toHaveTextContent('antes: R$ 2.500,00 (▼ 20%)');
    expect(screen.getByTestId('periodo-anterior')).toHaveTextContent('01/07/2026 a 31/07/2026');
  });

  it('o saving mês a mês não pula mês vazio, e as três réguas ficam separadas', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();

    const meses = within(await screen.findByTestId('relatorio-meses'));
    expect(meses.getByText('jul/26')).toBeInTheDocument();
    const agosto = meses.getByText('ago/26').closest('tr')!;
    expect(agosto).toHaveTextContent('R$ 2.000,00');
    expect(agosto).toHaveTextContent('16,7%');
    // o mês sem pedido aparece zerado, não some
    expect(meses.getByText('jul/26').closest('tr')).toHaveTextContent('R$ 0,00');

    const reguas = screen.getByTestId('relatorio-reguas');
    expect(reguas.querySelector('[data-regua="negotiation"]')).toHaveTextContent('R$ 2.000,00');
    expect(reguas.querySelector('[data-regua="competition"]')).toHaveTextContent('R$ 5.000,00');
    // a régua sem processo diz que não se aplica, em vez de mostrar zero de ganho
    expect(reguas.querySelector('[data-regua="budget"]')).toHaveTextContent('Não se aplica');
    expect(reguas.querySelector('[data-regua="budget"]')).not.toHaveTextContent('R$ 0,00');
  });

  it('o tempo do ciclo mostra a mediana da etapa medida e diz quando não há medição', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();

    const ciclo = await screen.findByTestId('relatorio-ciclo');
    expect(ciclo.querySelector('[data-etapa="solicitacao_escolha"]')).toHaveTextContent('4 d');
    expect(ciclo.querySelector('[data-etapa="oc_recebimento"]')).toHaveTextContent('sem medição');
  });

  it('abre no recorte cheio e traz os seis blocos', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();

    await screen.findByTestId('relatorio-familias');
    expect(relatorioExecutivo).toHaveBeenCalledWith(
      { de: '', ate: '', empresa: '', centroCusto: '', comprador: '' }, expect.anything());

    for (const bloco of ['relatorio-familias', 'relatorio-saving', 'relatorio-fornecedores',
      'relatorio-urgentes', 'relatorio-otif', 'relatorio-sem-oc'])
      expect(screen.getByTestId(bloco)).toBeInTheDocument();

    // 1 — a família com o maior gasto e a fatia dela
    const familias = within(screen.getByTestId('relatorio-familias'));
    expect(familias.getByText('MATERIAL DE LIMPEZA')).toBeInTheDocument();
    expect(familias.getByText('80%')).toBeInTheDocument();

    // 2 — o saving do comprador vem com a base contra a qual foi apurado
    const saving = within(screen.getByTestId('relatorio-saving'));
    expect(saving.getByText('Carla Compradora')).toBeInTheDocument();
    expect(saving.getByText(/12\.000,00/)).toBeInTheDocument();

    // o recorte aplicado fica escrito na tela, não só nos campos
    expect(screen.getByTestId('recorte-aplicado')).toHaveTextContent('01/08/2026 a 31/08/2026');
    expect(screen.getByTestId('recorte-aplicado')).toHaveTextContent('Empresa: todas');
  });

  it('a compra sem O.C. do ERP mostra a justificativa, e a fila não vira violação', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();

    const semOc = within(await screen.findByTestId('relatorio-sem-oc'));
    expect(semOc.getByText('ERP indisponível na emissão')).toBeInTheDocument();
    expect(semOc.getByText('Sem justificativa registrada')).toBeInTheDocument();

    // o pedido que só ainda não registrou a O.C. é contado à parte, com este nome:
    // somá-lo à exceção faria a diretoria ler descumprimento onde há fila
    expect(screen.getByText(/Outros 3 pedido\(s\).*seguem em aberto com a O\.C\. por registrar/))
      .toBeInTheDocument();
    expect(screen.getByText(/1 andaram sem O\.C\. e sem justificativa nenhuma/)).toBeInTheDocument();
  });

  it('filtrar por empresa e centro de custo refaz a leitura com o recorte escolhido', async () => {
    const usuario = userEvent.setup();
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();
    await screen.findByTestId('relatorio-familias');

    await usuario.selectOptions(screen.getByLabelText('Empresa'), 'Trino Nordeste LTDA');
    await usuario.selectOptions(screen.getByLabelText('Centro de custo'), 'CC-NE-01');
    await usuario.click(screen.getByRole('button', { name: 'Aplicar filtros' }));

    await waitFor(() => expect(relatorioExecutivo).toHaveBeenLastCalledWith(
      { de: '2026-08-01', ate: '2026-08-31', empresa: 'Trino Nordeste LTDA', centroCusto: 'CC-NE-01', comprador: '' },
      expect.anything()));
  });

  it('o PDF sai com o recorte aplicado, não com o que ainda está sendo digitado', async () => {
    const usuario = userEvent.setup();
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    vi.mocked(pdfDoRelatorio).mockResolvedValue(new Blob(['%PDF']));
    abrir();
    await screen.findByTestId('relatorio-familias');

    // mexer no campo sem aplicar não pode mudar a folha que vai para a reunião
    await usuario.selectOptions(screen.getByLabelText('Comprador'), 'b1');
    await usuario.click(screen.getByRole('button', { name: 'Exportar em PDF' }));

    await waitFor(() => expect(pdfDoRelatorio).toHaveBeenCalledWith(
      { de: '', ate: '', empresa: '', centroCusto: '', comprador: '' }));
    expect(abrirBlob).toHaveBeenCalled();
  });

  it('avisa o que o recorte não alcança quando há pedido sem solicitação de origem', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio({
      coverage: { ordersWithoutPr: 4, valueWithoutPr: 12500, capped: false, cap: 5000 },
    }));
    abrir();

    const aviso = await screen.findByTestId('cobertura-do-recorte');
    expect(aviso).toHaveTextContent('4 pedido(s)');
    expect(aviso).toHaveTextContent(/não têm solicitação de origem/);
  });

  it('sem cobertura em falta, não inventa aviso', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();
    await screen.findByTestId('relatorio-familias');
    expect(screen.queryByTestId('cobertura-do-recorte')).not.toBeInTheDocument();
  });
});
