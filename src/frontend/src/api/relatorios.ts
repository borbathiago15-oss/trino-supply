import { api, baixar } from './cliente';

/**
 * Relatório executivo de compras — os seis blocos que a diretoria lê sobre um
 * mesmo recorte, e o PDF que sai desse mesmo recorte para a reunião.
 */

export interface LinhaFamilia {
  family: string;
  value: number;
  quantity: number;
  orders: number;
  percent: number;
}

export interface LinhaSaving {
  buyer: string;
  processes: number;
  baseline: number;
  closed: number;
  saving: number;
  savingPercent: number;
  spend: number;
  orders: number;
}

export interface LinhaFornecedor {
  supplier: string;
  orders: number;
  value: number;
  percent: number;
}

export interface LinhaOtif {
  supplier: string;
  measured: number;
  onTimePercent: number | null;
  inFullPercent: number | null;
  otifPercent: number | null;
}

export interface CompraUrgente {
  number: string;
  prNumber: string | null;
  supplier: string;
  value: number;
  issuedOn: string;
  requester: string | null;
  reason: string | null;
  impact: string | null;
}

export interface CompraSemOc {
  number: string;
  supplier: string;
  value: number;
  issuedOn: string;
  buyer: string;
  costCenter: string | null;
  reason: string | null;
}

export interface RelatorioExecutivo {
  from: string;
  to: string;
  companyLabel: string | null;
  costCenterLabel: string | null;
  buyerLabel: string | null;
  generatedAt: string;
  kpis: {
    spend: number;
    orders: number;
    suppliers: number;
    savingTotal: number;
    savingPercent: number | null;
    urgentPercent: number;
    otifPercent: number | null;
    withoutErpValue: number;
  };
  /** O que o recorte não alcança — ver `Relatorios.tsx`, o aviso sai na tela e no PDF. */
  coverage: { ordersWithoutPr: number; valueWithoutPr: number; capped: boolean; cap: number };
  families: LinhaFamilia[];
  buyers: LinhaSaving[];
  suppliers: {
    rows: LinhaFornecedor[];
    supplierCount: number;
    top1Percent: number | null;
    top3Percent: number | null;
    top5Percent: number | null;
  };
  urgent: { orders: number; value: number; percent: number; items: CompraUrgente[] };
  otif: LinhaOtif[];
  /**
   * `orders`/`value`: fecharam pela exceção do PO-BR-011, com justificativa.
   * `pendingOrders`: ainda vão registrar a O.C. — fila, não violação.
   * `closedWithoutReason`: andaram sem O.C. e sem justificativa (anomalia).
   */
  withoutErp: {
    orders: number; value: number; percent: number;
    pendingOrders: number; pendingValue: number; closedWithoutReason: number;
    items: CompraSemOc[];
  };
  filterOptions: {
    companies: string[];
    costCenters: { code: string; name: string }[];
    buyers: { id: string; label: string }[];
  };
}

export interface FiltrosRelatorio {
  de: string;
  ate: string;
  empresa: string;
  centroCusto: string;
  comprador: string;
}

export const FILTROS_RELATORIO_VAZIOS: FiltrosRelatorio =
  { de: '', ate: '', empresa: '', centroCusto: '', comprador: '' };

/** Só o que foi preenchido vira parâmetro: vazio é "todos", não um filtro em branco. */
export function consultaDoRelatorio(f: FiltrosRelatorio): string {
  const q = new URLSearchParams();
  if (f.de) q.set('from', f.de);
  if (f.ate) q.set('to', f.ate);
  if (f.empresa) q.set('company', f.empresa);
  if (f.centroCusto) q.set('costCenter', f.centroCusto);
  if (f.comprador) q.set('buyerId', f.comprador);
  const s = q.toString();
  return s ? `?${s}` : '';
}

const base = '/api/v1/analytics/report';

export const relatorioExecutivo = (f: FiltrosRelatorio, signal?: AbortSignal) =>
  api<RelatorioExecutivo>(base + consultaDoRelatorio(f), { signal });

export const pdfDoRelatorio = (f: FiltrosRelatorio) =>
  baixar(`${base}/pdf${consultaDoRelatorio(f)}`, 'Falha ao gerar o PDF do relatório.');
