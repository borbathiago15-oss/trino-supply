import { describe, expect, it } from 'vitest';
import { normalizarPedido, pedidoEncerrado, type PedidoCompra } from './pedidos';

describe('pedidos', () => {
  it('normaliza listas nulas vindas da API', () => {
    const bruto = { id: 'x', number: 'PO-1', status: 'EMITIDO', families: null, invoices: null, items: null } as unknown as PedidoCompra;
    const o = normalizarPedido(bruto);
    expect(o.families).toEqual([]);
    expect(o.invoices).toEqual([]);
    expect(o.items).toEqual([]);
  });
  it('pedido encerrado = entrega concluída, recebido ou cancelado', () => {
    expect(pedidoEncerrado({ deliveryCompletedAt: null, status: 'EMITIDO' })).toBe(false);
    expect(pedidoEncerrado({ deliveryCompletedAt: '2026-09-01T00:00:00Z', status: 'PARCIAL' })).toBe(true);
    expect(pedidoEncerrado({ deliveryCompletedAt: null, status: 'RECEBIDO' })).toBe(true);
    expect(pedidoEncerrado({ deliveryCompletedAt: null, status: 'CANCELADO' })).toBe(true);
  });
});
