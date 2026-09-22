import { describe, expect, it } from 'vitest';
import { linhaDoTempo, normalizarPedido, pedidoEncerrado, type PedidoCompra } from './pedidos';

describe('pedidos', () => {
  it('normaliza listas nulas vindas da API', () => {
    const bruto = { id: 'x', number: 'PO-1', status: 'EMITIDO', families: null, invoices: null, items: null } as unknown as PedidoCompra;
    const o = normalizarPedido(bruto);
    expect(o.families).toEqual([]);
    expect(o.invoices).toEqual([]);
    expect(o.items).toEqual([]);
    expect(o.erpDocuments).toEqual([]);
  });
  it('families em texto vira lista: era o que derrubava /pedidos com duas famílias', () => {
    // a vista mandava o CSV cru da entidade; `"EPI, UNIFORME"` passava no `?? []` por não ser
    // nulo e morria no `.join(', ')` da lista, que string não tem
    const bruto = { id: 'x', number: 'PO-1', status: 'EMITIDO', families: 'EPI, UNIFORME' } as unknown as PedidoCompra;
    expect(normalizarPedido(bruto).families).toEqual(['EPI', 'UNIFORME']);
  });

  it('uma família só, texto sem vírgula, também vira lista', () => {
    const bruto = { id: 'x', number: 'PO-1', status: 'EMITIDO', families: 'EPI' } as unknown as PedidoCompra;
    expect(normalizarPedido(bruto).families).toEqual(['EPI']);
  });

  it('texto vazio não vira uma família em branco', () => {
    const bruto = { id: 'x', number: 'PO-1', status: 'EMITIDO', families: '  ' } as unknown as PedidoCompra;
    expect(normalizarPedido(bruto).families).toEqual([]);
  });

  it('pedido encerrado = entrega concluída, recebido ou cancelado', () => {
    expect(pedidoEncerrado({ deliveryCompletedAt: null, status: 'EMITIDO' })).toBe(false);
    expect(pedidoEncerrado({ deliveryCompletedAt: '2026-09-01T00:00:00Z', status: 'PARCIAL' })).toBe(true);
    expect(pedidoEncerrado({ deliveryCompletedAt: null, status: 'RECEBIDO' })).toBe(true);
    expect(pedidoEncerrado({ deliveryCompletedAt: null, status: 'CANCELADO' })).toBe(true);
  });
});

const pedido = (p: Partial<PedidoCompra>): PedidoCompra => normalizarPedido({
  id: 'po1', number: 'PO-1', status: 'EMITIDO', supplierId: 's1', supplierName: 'Alfa',
  sourcePrNumber: null, quotationNumber: null, paymentTerms: null, deliveryDays: null, freightValue: null,
  families: [], notes: null, totalValue: 10, issuedByLabel: null, receivedByLabel: null, receivedAt: null,
  cancelReason: null, createdAt: '2026-09-01T10:00:00Z', erpNumber: null, erpIssuedOn: null, noErpReason: null,
  erpPending: true, erpDocuments: [], promisedDate: null, onTime: null, inFull: null, otif: null,
  referenceSavingTotal: null, erpDocumentId: null, erpFileName: null, deliveryCompletedAt: null,
  pendingDelivery: true, inactiveCatalogCodes: null, invoices: [],
  items: [{ itemId: 'i1', description: 'Luva', unitOfMeasure: 'PAR', quantity: 10, receivedQuantity: 0,
    pendingQuantity: 10, rejectedQuantity: 0, rejectionReason: null, lastPaidUnitPrice: null, referenceSaving: null,
    sourcePrNumber: null, unitPrice: 1, catalogCode: null, catalogItemId: null, family: null,
    erpCoveredQuantity: 0, erpPendingQuantity: 10 }],
  ...p,
});
const situacoes = (p: Partial<PedidoCompra>) => linhaDoTempo(pedido(p)).map((e) => e.situacao);

describe('linha do tempo do pedido: O.C. → faturamento → entrega', () => {
  it('recém-criado na aprovação, a O.C. é o passo de agora', () => {
    expect(situacoes({})).toEqual(['atual', 'pendente', 'pendente']);
  });

  it('a O.C. que cobre parte do pedido deixa a etapa em andamento, não concluída', () => {
    expect(situacoes({
      erpNumber: 'OC-1', erpPending: true,
      erpDocuments: [{ id: 'd', number: 'OC-1', issuedOn: '2026-09-01', documentId: null, fileName: null, notes: null,
        createdByLabel: null, createdAt: '', items: [{ itemId: 'i1', quantity: 6 }] }],
    })).toEqual(['parcial', 'pendente', 'pendente']);
  });

  it('a observação da exceção fecha a etapa da O.C. (PO-BR-011)', () => {
    expect(situacoes({ noErpReason: 'Compra emergencial de balcão.', erpPending: false })).toEqual(['feita', 'atual', 'pendente']);
  });

  it('nota lançada conclui o faturamento e a entrega vira o passo de agora', () => {
    expect(situacoes({
      erpNumber: 'OC-1', erpPending: false, status: 'FATURADO',
      invoices: [{ id: 'n', number: '1', issuedOn: null, value: null, documentId: null, fileName: null, createdByLabel: null, createdAt: '' }],
    })).toEqual(['feita', 'feita', 'atual']);
  });

  it('entrega de parte da quantidade fica em andamento; concluída, tudo fecha', () => {
    const nf = [{ id: 'n', number: '1', issuedOn: null, value: null, documentId: null, fileName: null, createdByLabel: null, createdAt: '' }];
    const item = { ...pedido({}).items[0], receivedQuantity: 4, pendingQuantity: 6 };
    expect(situacoes({ erpNumber: 'OC-1', erpPending: false, status: 'PARCIAL', invoices: nf, items: [item] }))
      .toEqual(['feita', 'feita', 'parcial']);
    expect(situacoes({ erpNumber: 'OC-1', erpPending: false, status: 'RECEBIDO', invoices: nf, deliveryCompletedAt: '2026-09-10T10:00:00Z' }))
      .toEqual(['feita', 'feita', 'feita']);
  });

  it('a API antiga sem os campos de cobertura é lida como pedido inteiro sem O.C.', () => {
    const bruto = { ...pedido({}) } as Record<string, unknown>;
    delete bruto.erpPending; delete bruto.erpDocuments;
    const normalizado = normalizarPedido(bruto as unknown as PedidoCompra);
    expect(normalizado.erpPending).toBe(true);
    expect(normalizado.erpDocuments).toEqual([]);
  });
});
