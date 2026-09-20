import { describe, expect, it } from 'vitest';
import type { RelatorioExecutivo } from '@/api/relatorios';
import { resumoExecutivo } from './resumoExecutivo';

/** `moeda` usa espaço fixo entre R$ e o número; o teste lê como gente lê. */
const legivel = (r: RelatorioExecutivo) => resumoExecutivo(r).map((f) => f.replace(/\u00a0/g, ' ')) as [string, string, string];

const base = (): RelatorioExecutivo => ({
  from: '2026-08-01', to: '2026-08-31', companyLabel: null, costCenterLabel: null, buyerLabel: null, generatedAt: '',
  kpis: { spend: 20000, orders: 3, suppliers: 2, savingTotal: 2000, savingPercent: 16.7, urgentPercent: 30, otifPercent: 50, withoutErpValue: 2000 },
  coverage: { ordersWithoutPr: 0, valueWithoutPr: 0, capped: false, cap: 5000 },
  families: [{ family: 'EPI', value: 16000, quantity: 1, orders: 2, percent: 80 }],
  buyers: [], suppliers: { rows: [], supplierCount: 2, top1Percent: null, top3Percent: null, top5Percent: null },
  urgent: { orders: 1, value: 6000, percent: 30, items: [] }, otif: [],
  withoutErp: { orders: 1, value: 2000, percent: 10, pendingOrders: 3, pendingValue: 0, closedWithoutReason: 0, items: [] },
  filterOptions: { companies: [], costCenters: [], buyers: [] }, months: [],
  savingRulers: {
    negotiation: { processes: 1, baseline: 12000, closed: 10000, saving: 2000, percent: 16.7 },
    competition: { processes: 1, baseline: 15000, closed: 10000, saving: 5000, percent: 33.3 },
    budget: { processes: 0, baseline: 0, closed: 0, saving: 0, percent: null },
  },
  previous: { from: '2026-07-01', to: '2026-07-31', spend: 16000, orders: 2, savingTotal: 2500, urgentPercent: 10, otifPercent: 40 },
  cycleTimes: [], savingByFamily: [], savingBySupplier: [],
  reference: { orders: 0, items: 0, gain: 0, loss: 0, net: 0, rows: [] },
  demand: { costCenters: [], requesters: [], scope: { materials: 0, services: 0, materialsPercent: 0, servicesPercent: 0 } },
  bids: { processes: 0, averageProponents: null, withCompetition: 0, winners: [] },
  payment: { weightedDays: null, ordersWithDays: 0, valueWithDays: 0, terms: [] },
  adherence: { orders: 0, formal: 0, value: 0, formalValue: 0, percent: null },
});

describe('o relatório em três frases', () => {
  it('gasto com tendência e a família que mais pesou', () => {
    const [gasto] = legivel(base());
    expect(gasto).toBe('Foram R$ 20.000,00 em 3 pedido(s) com 2 fornecedor(es), 25% acima do período anterior (R$ 16.000,00). A família que mais pesou foi EPI (80%).');
  });
  it('as réguas que se aplicam, e só elas', () => {
    const [, saving] = legivel(base());
    expect(saving).toBe('A negociação segurou R$ 2.000,00 (16,7% da primeira proposta); a concorrência valeu R$ 5.000,00.');
    const r = base(); r.savingRulers.negotiation.processes = 0; r.savingRulers.competition.processes = 0;
    expect(legivel(r)[1]).toMatch(/não há saving a medir/);
  });
  it('exceções: sem O.C., fila da O.C., urgência e OTIF com o antes', () => {
    const [, , excecoes] = legivel(base());
    expect(excecoes).toBe('1 compra(s) fecharam sem O.C. do ERP (R$ 2.000,00), 3 ainda com a O.C. por registrar; 30% do valor foi urgente; OTIF de 50% (antes 40%).');
  });
  it('sem compra no período diz isso, em vez de zeros', () => {
    const r = base(); r.kpis.orders = 0; r.kpis.spend = 0; r.families = [];
    expect(legivel(r)[0]).toBe('Nenhuma compra fechou no período.');
  });
});
