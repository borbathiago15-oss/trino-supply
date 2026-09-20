import { describe, expect, it } from 'vitest';
import { comparacaoDaEscolha, diasDesde } from './cotacoes';
import { processo, proposta } from '@/test/cotacoes';

const duas = () => processo({
  proposals: [
    proposta({ id: 'p1', supplierId: 's1', supplierName: 'Alfa', totalValue: 1000, isLatest: true }),
    proposta({ id: 'p2', supplierId: 's2', supplierName: 'Beta', totalValue: 1250, isLatest: true, deliveryDays: 3 }),
  ],
});

describe('comparação da escolha do comprador', () => {
  it('escolheu a mais cara: diz quanto acima e de quem', () => {
    const c = comparacaoDaEscolha({ ...duas(), selection: { winnerSupplierId: 's2', winnerProposalId: 'p2', criteria: null, justification: 'prazo', by: null, byLabel: null } });
    expect(c.total).toBe(1250);
    expect(c.fornecedor).toBe('Beta');
    expect(c.maisBarata).toEqual({ supplierName: 'Alfa', totalValue: 1000 });
    expect(c.acimaPercent).toBe(25);
    expect(c.deliveryDays).toBe(3);
  });

  it('escolheu a mais barata: nada acima, e a tela diz "a mais barata entre N"', () => {
    const c = comparacaoDaEscolha({ ...duas(), selection: { winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: null, justification: '', by: null, byLabel: null } });
    expect(c.maisBarata).toBeNull();
    expect(c.acimaPercent).toBe(0);
    expect(c.propostas).toBe(2);
  });

  it('proposta única não tem com o que comparar', () => {
    const q = duas();
    const c = comparacaoDaEscolha({ ...q, proposals: [q.proposals[0]], selection: { winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: null, justification: '', by: null, byLabel: null } });
    expect(c.acimaPercent).toBeNull();
    expect(c.propostas).toBe(1);
  });

  it('versão antiga da proposta não entra: só a vigente de cada fornecedor', () => {
    const q = duas();
    const c = comparacaoDaEscolha({
      ...q,
      proposals: [...q.proposals, proposta({ id: 'p0', supplierId: 's1', supplierName: 'Alfa', totalValue: 500, isLatest: false })],
      selection: { winnerSupplierId: 's2', winnerProposalId: 'p2', criteria: null, justification: '', by: null, byLabel: null },
    });
    expect(c.maisBarata?.totalValue).toBe(1000);
  });

  it('compra dividida soma as adjudicações e nomeia os dois fornecedores', () => {
    const c = comparacaoDaEscolha({
      ...duas(), selection: null,
      awards: [
        { id: 'a1', family: 'EPI', quotationItemId: null, supplierId: 's1', supplierName: 'Alfa', proposalId: 'p1', proposalVersion: 1, itemsValue: 600, totalValue: 600, criteria: null, justification: null, byLabel: null, purchaseOrderId: null, purchaseOrderNumber: null },
        { id: 'a2', family: 'FERR', quotationItemId: null, supplierId: 's2', supplierName: 'Beta', proposalId: 'p2', proposalVersion: 1, itemsValue: 300, totalValue: 300, criteria: null, justification: null, byLabel: null, purchaseOrderId: null, purchaseOrderNumber: null },
      ],
    });
    expect(c.total).toBe(900);
    expect(c.fornecedor).toBe('Alfa + Beta');
    expect(c.maisBarata).toBeNull();   // os dois foram escolhidos: não sobra alternativa
  });
});

describe('dias desde', () => {
  it('conta dias inteiros e nunca fica negativo', () => {
    const agora = new Date('2026-09-20T12:00:00Z');
    expect(diasDesde('2026-09-16T09:00:00Z', agora)).toBe(4);
    expect(diasDesde('2026-09-20T08:00:00Z', agora)).toBe(0);
    expect(diasDesde('2026-09-21T08:00:00Z', agora)).toBe(0);
    expect(diasDesde(null, agora)).toBeNull();
    expect(diasDesde('não é data', agora)).toBeNull();
  });
});
