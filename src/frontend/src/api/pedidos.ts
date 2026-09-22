import { api, baixar, enviarArquivo } from './cliente';

/** Situações do pedido conforme `PoView` em Program.cs. */
export type SituacaoPedido = 'EMITIDO' | 'FATURADO' | 'PARCIAL' | 'RECEBIDO' | 'CANCELADO';

export interface ItemPedido {
  itemId: string;
  description: string;
  unitOfMeasure: string;
  quantity: number;
  /** Quanto deste item já está coberto por O.C. do ERP, e quanto ainda não está. */
  erpCoveredQuantity: number;
  erpPendingQuantity: number;
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

/** Uma O.C. do ERP registrada no pedido, com o que ela cobre de cada item. */
export interface OcDoErp {
  id: string;
  number: string;
  issuedOn: string;
  documentId: string | null;
  fileName: string | null;
  notes: string | null;
  createdByLabel: string | null;
  createdAt: string;
  items: { itemId: string; quantity: number }[];
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
  /** Observação que autorizou o fechamento sem O.C. do ERP (PO-BR-011). */
  noErpReason: string | null;
  /**
   * Ainda há item ou quantidade sem O.C. do ERP e sem a observação da exceção. Uma O.C. pode
   * cobrir parte do pedido; o saldo fica aqui até a próxima O.C. ou até a observação.
   */
  erpPending: boolean;
  erpDocuments: OcDoErp[];
  promisedDate: string | null;
  onTime: boolean | null;
  inFull: boolean | null;
  otif: boolean | null;
  referenceSavingTotal: number | null;
  erpDocumentId: string | null;
  erpFileName: string | null;
  deliveryCompletedAt: string | null;
  pendingDelivery: boolean;
  /**
   * Códigos do pedido que estão inativos no catálogo. Receber um deles dá entrada em
   * estoque e é recusado (IV-ERR-010) — devolver continua valendo.
   *
   * `null` quer dizer "não conferido nesta leitura", e não "nenhum": só a leitura de um
   * pedido faz essa consulta, a lista não.
   */
  inactiveCatalogCodes: string[] | null;
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

/**
 * Se este item do pedido não pode mais dar entrada em estoque: foi inativado no catálogo
 * depois da emissão, e o recebimento dele é recusado (IV-ERR-010). Item sem código de
 * catálogo não entra em estoque de qualquer forma, então nunca é barrado por aqui.
 */
export const entradaBloqueada = (
  pedido: Pick<PedidoCompra, 'inactiveCatalogCodes'>, item: Pick<ItemPedido, 'catalogCode'>,
) => !!item.catalogCode && !!pedido.inactiveCatalogCodes?.includes(item.catalogCode);

export const totalPedido = (itens: Pick<ItemPedido, 'quantity'>[]) => itens.reduce((a, i) => a + i.quantity, 0);
export const totalRecebido = (itens: Pick<ItemPedido, 'receivedQuantity'>[]) =>
  itens.reduce((a, i) => a + (i.receivedQuantity || 0), 0);

const base = '/api/v1/purchase-orders';

/**
 * `families` já viajou como texto: a entidade guarda um CSV e a vista mandava a string crua,
 * o que derrubava a lista inteira no `.join` do primeiro pedido com duas famílias. O servidor
 * agora manda lista, e esta função aceita as três formas — lista, texto e nulo — porque a tela
 * não pode morrer por causa do formato de um campo de exibição.
 */
export const listaDeFamilias = (valor: unknown): string[] =>
  Array.isArray(valor) ? valor.filter((f): f is string => typeof f === 'string')
    : typeof valor === 'string' ? valor.split(',').map((f) => f.trim()).filter(Boolean)
    : [];

/** A API pode mandar `families`/`invoices`/`items` nulos; a tela sempre trabalha com listas. */
export function normalizarPedido(bruto: PedidoCompra): PedidoCompra {
  const items = (bruto.items ?? []).map((i) => ({
    ...i,
    erpCoveredQuantity: i.erpCoveredQuantity ?? 0,
    erpPendingQuantity: i.erpPendingQuantity ?? Math.max(0, i.quantity - (i.erpCoveredQuantity ?? 0)),
  }));
  const erpDocuments = (bruto.erpDocuments ?? []).map((d) => ({ ...d, items: d.items ?? [] }));
  return {
    ...bruto, families: listaDeFamilias(bruto.families), invoices: bruto.invoices ?? [], items, erpDocuments,
    erpPending: bruto.erpPending ?? (bruto.noErpReason == null && items.some((i) => i.erpPendingQuantity > 0)),
  };
}

/** As três etapas que vêm depois da aprovação, na ordem em que acontecem. */
export type EtapaDoPedido = 'oc' | 'faturamento' | 'entrega';
export type SituacaoDaEtapa = 'feita' | 'parcial' | 'atual' | 'pendente';

/**
 * A linha do tempo do pedido: O.C. → faturamento → entrega. É derivada do que o pedido
 * já diz — não há estado próprio a gravar. "Parcial" é a etapa que começou e não fechou:
 * O.C. que cobre parte dos itens, entrega de parte da quantidade.
 */
export function linhaDoTempo(o: PedidoCompra): { etapa: EtapaDoPedido; situacao: SituacaoDaEtapa }[] {
  const ocFechada = !o.erpPending;
  const ocComecou = o.erpDocuments.length > 0 || !!o.noErpReason;
  const faturado = o.invoices.length > 0;
  const entregue = pedidoEncerrado(o) && o.status !== 'CANCELADO';
  const entregaComecou = o.items.some((i) => i.receivedQuantity > 0);
  const etapas: { etapa: EtapaDoPedido; feita: boolean; comecou: boolean }[] = [
    { etapa: 'oc', feita: ocFechada, comecou: ocComecou },
    { etapa: 'faturamento', feita: faturado, comecou: faturado },
    { etapa: 'entrega', feita: entregue, comecou: entregaComecou },
  ];
  let atualMarcada = false;
  return etapas.map(({ etapa, feita, comecou }) => {
    if (feita) return { etapa, situacao: 'feita' as const };
    if (!atualMarcada) { atualMarcada = true; return { etapa, situacao: comecou ? 'parcial' as const : 'atual' as const }; }
    return { etapa, situacao: 'pendente' as const };
  });
}

export interface PaginaDePedidos { itens: PedidoCompra[]; total: number }

/**
 * Busca e situação vão para o servidor. `total` é quantos existem, não quantos
 * vieram — é o que permite a tela dizer "mostrando 50 de 312" em vez de fingir
 * que a lista acabou.
 */
export async function listarPedidos(
  { busca, situacao, tamanho }: { busca?: string; situacao?: string; tamanho?: number } = {},
  signal?: AbortSignal,
): Promise<PaginaDePedidos> {
  const params = new URLSearchParams();
  if (busca?.trim()) params.set('q', busca.trim());
  if (situacao) params.set('status', situacao);
  if (tamanho) params.set('tamanho', String(tamanho));
  const consulta = params.toString();
  const r = await api<{ items: PedidoCompra[]; total: number }>(
    `${base}/${consulta ? `?${consulta}` : ''}`, { signal });
  return { itens: (r.items ?? []).map(normalizarPedido), total: r.total ?? 0 };
}

export const obterPedido = async (id: string, signal?: AbortSignal) =>
  normalizarPedido(await api<PedidoCompra>(`${base}/${id}`, { signal }));

export interface CoberturaDaOc { itemId: string; quantity: number }
export interface RegistroOc {
  erpNumber: string;
  issuedOn: string | null;
  /** Obrigatório quando `erpNumber` vem vazio (PO-BR-011). */
  noErpReason?: string | null;
  /**
   * O que esta O.C. cobre de cada item. Sem a lista, cobre tudo o que ainda falta — é o caso
   * comum, uma O.C. para o pedido inteiro. A soma nunca passa do pedido (PO-ERR-059).
   */
  items?: CoberturaDaOc[];
}
export const registrarOc = (id: string, dados: RegistroOc) =>
  api<PedidoCompra>(`${base}/${id}/erp-order`, { method: 'POST', body: dados }).then(normalizarPedido);
export const anexarOc = (id: string, arquivo: File) => enviarArquivo(`${base}/${id}/erp-order/attachment`, arquivo);
/** Anexo de uma O.C. específica, quando o pedido tem mais de uma. */
export const anexarOcDocumento = (id: string, docId: string, arquivo: File) =>
  enviarArquivo(`${base}/${id}/erp-documents/${docId}/attachment`, arquivo);

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
