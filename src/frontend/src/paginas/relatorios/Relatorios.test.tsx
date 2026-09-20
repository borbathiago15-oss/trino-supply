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
    rows: [{ supplier: 'Alfa', orders: 2, value: 16000, percent: 80, cumulative: 80, class: 'A' }],
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
  savingByFamily: [{ label: 'MATERIAL DE LIMPEZA', processes: 1, spend: 8000, saving: 1600, savingPercent: 16.7 }],
  savingBySupplier: [{ label: 'Alfa', processes: 1, spend: 8000, saving: 1600, savingPercent: 16.7 }],
  reference: {
    orders: 1, items: 2, gain: 200, loss: -100, net: 100,
    rows: [
      { order: 'PO-2026-000001', supplier: 'Alfa', description: 'Bota de PVC', catalogCode: 'EPI-002',
        quantity: 10, lastPaidUnitPrice: 50, unitPrice: 60, saving: -100 },
      { order: 'PO-2026-000001', supplier: 'Alfa', description: 'Luva nitrílica', catalogCode: 'EPI-001',
        quantity: 100, lastPaidUnitPrice: 10, unitPrice: 8, saving: 200 },
    ],
  },
  demand: {
    costCenters: [{ code: 'CC-NE-01', name: 'Filial Recife', manager: 'Gerson Gerente', orders: 2, value: 16000, percent: 80 }],
    requesters: [{ requester: 'Ana Solicitante', requisitions: 1, orders: 2, value: 16000 }],
    scope: { materials: 15000, services: 5000, materialsPercent: 75, servicesPercent: 25 },
  },
  bids: {
    processes: 2, averageProponents: 1.5, withCompetition: 1,
    winners: [{ supplier: 'Alfa', wins: 1, value: 10000 }],
  },
  payment: {
    weightedDays: 54, ordersWithDays: 2, valueWithDays: 10000,
    terms: [
      { term: '60 dias', orders: 1, value: 9000, percent: 45, days: 60 },
      { term: 'não informada', orders: 1, value: 5000, percent: 25, days: null },
    ],
  },
  adherence: { orders: 2, formal: 1, value: 8000, formalValue: 6000, percent: 50 },
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

  it('o saving rateado por família e fornecedor, e a referência com a perda em destaque', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();

    const familia = within(await screen.findByTestId('relatorio-saving-familia'));
    expect(familia.getByText('MATERIAL DE LIMPEZA').closest('tr')).toHaveTextContent('R$ 1.600,00');
    expect(within(screen.getByTestId('relatorio-saving-fornecedor')).getByText('Alfa')).toBeInTheDocument();

    const referencia = screen.getByTestId('relatorio-referencia');
    // a perda vem primeiro e em vermelho: pagou mais que da última vez
    const primeira = referencia.querySelector('tbody tr')!;
    expect(primeira).toHaveTextContent('Bota de PVC');
    expect(within(primeira as HTMLElement).getByText('-R$ 100,00')).toHaveClass('text-perigo');
    expect(screen.getByText(/ganho R\$ 200,00 · perda -R\$ 100,00 · líquido R\$ 100,00/)).toBeInTheDocument();
  });

  it('origem da demanda, concorrências e pagamento: quem pediu, quem ganhou, em que prazo', async () => {
    vi.mocked(relatorioExecutivo).mockResolvedValue(relatorio());
    abrir();

    const centros = within(await screen.findByTestId('relatorio-centros'));
    expect(centros.getByText(/Filial Recife/)).toBeInTheDocument();
    expect(centros.getByText('gestor: Gerson Gerente')).toBeInTheDocument();
    expect(within(screen.getByTestId('relatorio-solicitantes')).getByText('Ana Solicitante')).toBeInTheDocument();
    expect(screen.getByTestId('relatorio-escopo')).toHaveTextContent('Serviços 25%');

    // a Beta venceu sem disputa: só quem levou com dois ou mais proponentes é vencedor de concorrência
    expect(within(screen.getByTestId('relatorio-vencedores')).getByText('Alfa')).toBeInTheDocument();
    expect(screen.getByText('Proponentes por BID').parentElement).toHaveTextContent('1,5');

    const pagamento = within(screen.getByTestId('relatorio-pagamento'));
    expect(pagamento.getByText('60 d')).toBeInTheDocument();
    expect(screen.getByText(/DPO 54 dias/)).toBeInTheDocument();
    // KPIs novos no topo: DPO e aderência
    expect(screen.getByText('Prazo médio de pagamento (DPO)').parentElement).toHaveTextContent('54 dias');
    expect(screen.getByText('Aderência à O.C. do ERP').parentElement).toHaveTextContent('50%');
    // curva ABC na concentração
    expect(within(screen.getByTestId('relatorio-fornecedores')).getByText('A')).toHaveClass('badge');
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
