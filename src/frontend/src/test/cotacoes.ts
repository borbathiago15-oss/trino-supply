import type { LoteDaFamilia, OfertaDaFamilia, Processo, Proposta } from '@/api/cotacoes';

/**
 * Fixtures do processo de cotação, compartilhadas pelos testes da lista e do
 * detalhe. Ficam fora de um `*.test.tsx` porque importar de um arquivo de
 * teste carregaria os `vi.mock` dele junto.
 */
export const proposta = (p: Partial<Proposta>): Proposta => ({
  id: 'p1', supplierId: 's1', supplierName: 'Alfa EPIs', version: 1, totalValue: 1200,
  deliveryDays: 10, paymentTerms: '30 dias', paymentMethodName: null, paymentDays: 30,
  freightValue: 100, taxValue: null,
  otherCosts: null, discountValue: null, validUntil: null, currency: 'BRL', notes: null,
  submittedVia: 'PORTAL', submittedByLabel: null, submittedAt: '2026-09-01T12:00:00Z',
  attachmentDocumentId: null, attachmentFileName: null, isLatest: true, isWinner: false,
  items: [{ quotationItemId: 'i1', unitPrice: 12, quantity: 100 }],
  ...p,
});

export const processo = (p: Partial<Processo>): Processo => ({
  id: 'q1', number: 'RFQ-2026-000001', kind: 'COMPRA', status: 'EM_ANALISE',
  sourcePrNumber: 'SC-2026-000001', sourcePrNumbers: ['SC-2026-000001'], costCenter: 'BAH-001',
  justification: null, deadline: '2026-10-01', notes: null, createdByLabel: 'Carla',
  createdAt: '2026-09-01T10:00:00Z', decisionReason: null,
  items: [{
    id: 'i1', sequence: 1, catalogItemId: null, catalogCode: 'EPI-001', description: 'Luva nitrílica',
    quantity: 100, unitOfMeasure: 'PAR', sourcePrNumber: 'SC-2026-000001', family: 'EPI',
  }],
  families: ['EPI'],
  suppliers: [{
    supplierId: 's1', supplierName: 'Alfa EPIs', taxId: '12345678000199',
    invitedAt: '2026-09-01T11:00:00Z', invitedByLabel: 'Carla', hasProposal: true,
  }],
  proposals: [proposta({})],
  selection: null, managerApproval: null, directorApproval: null,
  awards: [], splitAward: false, pendingPoSuppliers: [], purchaseOrders: [],
  purchaseOrderId: null, purchaseOrderNumber: null, saving: null,
  ...p,
});

/** Oferta de um fornecedor para uma família, como o mapa por família devolve. */
export const oferta = (o: Partial<OfertaDaFamilia>): OfertaDaFamilia => ({
  supplierId: 's1', supplierName: 'Alfa EPIs', proposalId: 'p1', proposalVersion: 1,
  itemsValue: 1200, totalValue: 1200, deliveryDays: 10, paymentTerms: '30 dias',
  complete: true, cheapest: false, homologation: 'HOMOLOGADO', active: true, canWin: true,
  ...o,
});

export const lote = (l: Partial<LoteDaFamilia>): LoteDaFamilia => ({
  family: 'EPI', itemCount: 1, quantity: 100, offers: [oferta({})], ...l,
});
