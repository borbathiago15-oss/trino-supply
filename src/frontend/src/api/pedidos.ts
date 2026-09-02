import { api, baixar, enviarArquivo } from './cliente';

/** Situações do pedido conforme `PoView` em Program.cs. */
export type SituacaoPedido = 'EMITIDO' | 'FATURADO' | 'PARCIAL' | 'RECEBIDO' | 'CANCELADO';

export interface ItemPedido {
  itemId: string;
  description: string;
  unitOfMeasure: string;
  quantity: number;
  receivedQuantity: number;
  pendingQuantity: number;
  rejectedQuantity: number;
  rejectionReason: string | null;
  lastPaidUnitPrice: number | null;
  referenceSaving: number | null;
  sourcePrNumber: string | null;
  unitPrice: number;
  catalogCode: string | null;
  catalogItemId: string | null;
  family: string | null;
}

export interface NotaFiscal {
  id: string;
  number: string;
  issuedOn: string | null;
  value: number | null;
  documentId: string | null;
  fileName: string | null;
  createdByLabel: string | null;
  createdAt: string;
}

export interface PedidoCompra {
  id: string;
  number: string;
  status: SituacaoPedido;
  supplierId: string;
  supplierName: string;
  sourcePrNumber: string | null;
  quotationNumber: string | null;
  paymentTerms: string | null;
  deliveryDays: number | null;
  freightValue: number | null;
  families: string[];
  notes: string | null;
  totalValue: number;
  issuedByLabel: string | null;
  receivedByLabel: string | null;
  receivedAt: string | null;
  cancelReason: string | null;
  createdAt: string;
  erpNumber: string | null;
  erpIssuedOn: string | null;
  promisedDate: string | null;
  onTime: boolean | null;
  inFull: boolean | null;
  otif: boolean | null;
  referenceSavingTotal: number | null;
  erpDocumentId: string | null;
  erpFileName: string | null;
  deliveryCompletedAt: string | null;
  pendingDelivery: boolean;
  invoices: NotaFiscal[];
  items: ItemPedido[];
}

export const ROTULO_SITUACAO: Record<SituacaoPedido, { rotulo: string; classe: string }> = {
  EMITIDO: { rotulo: 'OC/Faturamento', classe: 'bg-blue-50 text-blue-800' },
  FATURADO: { rotulo: 'Faturado', classe: 'bg-blue-50 text-blue-800' },
  PARCIAL: { rotulo: 'Entregue parcial', classe: 'bg-orange-50 text-orange-800' },
  RECEBIDO: { rotulo: 'Pedido entregue', classe: 'bg-purple-50 text-purple-800' },
  CANCELADO: { rotulo: 'Cancelado', classe: 'bg-slate-100 text-slate-500' },
};

/** Encerrado = entrega concluída ou saldo cancelado; aí o pedido não recebe mais nada. */
export const pedidoEncerrado = (o: Pick<PedidoCompra, 'deliveryCompletedAt' | 'status'>) =>
  !!o.deliveryCompletedAt || o.status === 'RECEBIDO' || o.status === 'CANCELADO';

export const totalPedido = (itens: Pick<ItemPedido, 'quantity'>[]) => itens.reduce((a, i) => a + i.quantity, 0);
export const totalRecebido = (itens: Pick<ItemPedido, 'receivedQuantity'>[]) =>
  itens.reduce((a, i) => a + (i.receivedQuantity || 0), 0);

const base = '/api/v1/purchase-orders';

/** A API pode mandar `families`/`invoices`/`items` nulos; a tela sempre trabalha com listas. */
export function normalizarPedido(bruto: PedidoCompra): PedidoCompra {
  return { ...bruto, families: bruto.families ?? [], invoices: bruto.invoices ?? [], items: bruto.items ?? [] };
}

export const listarPedidos = async (signal?: AbortSignal) =>
  (await api<{ items: PedidoCompra[] }>(`${base}/`, { signal })).items.map(normalizarPedido);

export const obterPedido = async (id: string, signal?: AbortSignal) =>
  normalizarPedido(await api<PedidoCompra>(`${base}/${id}`, { signal }));

export interface RegistroOc { erpNumber: string; issuedOn: string | null }
export const registrarOc = (id: string, dados: RegistroOc) =>
  api<PedidoCompra>(`${base}/${id}/erp-order`, { method: 'POST', body: dados });
export const anexarOc = (id: string, arquivo: File) => enviarArquivo(`${base}/${id}/erp-order/attachment`, arquivo);

export interface LancamentoNota { number: string; issuedOn: string | null; value: number | null }
export const lancarNota = (id: string, dados: LancamentoNota) =>
  api<{ id: string; number: string; issuedOn: string | null }>(`${base}/${id}/invoices`, { method: 'POST', body: dados });
export const anexarNota = (id: string, notaId: string, arquivo: File) =>
  enviarArquivo(`${base}/${id}/invoices/${notaId}/attachment`, arquivo);

export interface LinhaEntrega { itemId: string; quantity: number; rejected: number }
export interface RegistroEntrega {
  locationId: string | null;
  items: LinhaEntrega[];
  closeRemaining: boolean;
  closeReason: string | null;
  rejectReason: string | null;
}
export const registrarEntrega = (id: string, dados: RegistroEntrega) =>
  api<PedidoCompra>(`${base}/${id}/deliveries`, { method: 'POST', body: dados });

export const pdfPedido = (id: string) => baixar(`${base}/${id}/pdf`, 'Falha ao gerar o PDF da OC.');
