import { api } from './cliente';

// ---- Central de Avisos -----------------------------------------------------

export type Severidade = 'alta' | 'media' | 'info';

export interface Aviso {
  kind: string;
  severity: Severidade;
  count: number;
  /** Id da tela (`pr-mine`, `triage`…); `enderecoDoId` resolve para a rota. */
  view: string;
  text: string;
  /**
   * Se entra na contagem do menu. Falso para o achado de insight: ele é uma
   * constatação, não fila — contá-lo faria o menu dizer "Pedidos 3" com a lista
   * de pedidos vazia. Ausente significa verdadeiro, que é o caso da maioria.
   */
  counts?: boolean;
}

export const CLASSE_AVISO: Record<Severidade, string> = {
  alta: 'border-perigo/30 bg-perigo-fundo text-perigo',
  media: 'border-aviso/30 bg-aviso-fundo text-aviso',
  info: 'border-borda bg-superficie-suave text-texto',
};

export const listarAvisos = async (signal?: AbortSignal) =>
  (await api<{ alerts: Aviso[] }>('/api/v1/dashboard', { signal })).alerts ?? [];

// ---- Dashboard de Suprimentos ---------------------------------------------

export interface KpisSuprimentos {
  prCount: number;
  prPrevCount: number;
  prTotalValue: number;
  approvedCount: number;
  approvedValue: number;
  pendingApproval: number;
  overdue: number;
  avgApprovalDays: number | null;
  poCount: number;
  poTotalValue: number;
  poOpen: number;
  poLate: number;
  avgReceiveDays: number | null;
}

export interface MesSuprimentos {
  month: string;
  created: number;
  approved: number;
  inApproval: number;
  returned: number;
  rejectedOrCancelled: number;
  draft: number;
  poValue: number;
}

export interface LinhaRank { label: string; value: number; count: number }

export interface Rankings {
  suppliers: LinhaRank[]; buyers: LinhaRank[]; requesters: LinhaRank[];
  families: LinhaRank[]; categories: LinhaRank[]; costCenters: LinhaRank[];
  regions: LinhaRank[]; managers: LinhaRank[]; clients: LinhaRank[];
}

export interface LinhaFornecedor {
  supplier: string; orders: number; quantity: number; value: number;
  open: number; otifMeasured: number; otifPercent: number | null;
}

export interface EtapaPrazo {
  stage: string;
  target: number | null;
  actual: number | null;
  median: number | null;
  measured: number;
  withinSlaPct: number | null;
  late: boolean;
}

export interface PrazoFamilia {
  family: string; stages: EtapaPrazo[]; targetTotal: number | null; actualTotal: number | null;
}

export interface ItemSaving {
  number: string; supplier: string | null; baseline: number; closed: number;
  value: number; byLabel: string | null;
}

export interface Saving {
  total: number; baseline: number; closed: number; processes: number; percent: number;
  referenceTotal: number; referenceOrders: number; items: ItemSaving[];
}

export interface LinhaComprador {
  label: string; processes: number; closed: number; savingTotal: number; poValue: number;
  avgDaysToPo: number | null; backlog: number; otifPercent: number | null; compositeScore: number | null;
}

export interface OpcoesFiltro {
  suppliers: { id: string; label: string }[];
  buyers: { id: string; label: string }[];
  requesters: { id: string; label: string }[];
  families: string[];
  costCenters: { code: string; name: string }[];
  regions: string[];
  managers: string[];
  clients: string[];
}

export interface DashboardSuprimentos {
  from: string;
  to: string;
  kpis: KpisSuprimentos;
  months: MesSuprimentos[];
  rankings: Rankings;
  supplierTable: LinhaFornecedor[];
  leadTimes: PrazoFamilia[];
  saving: Saving | null;
  buyerPanel: LinhaComprador[];
  filterOptions: OpcoesFiltro;
}

export interface FiltrosPainel {
  de: string; ate: string; fornecedor: string; comprador: string; solicitante: string;
  familia: string; centroCusto: string; regional: string; gerente: string; cliente: string;
}

export const FILTROS_PAINEL_VAZIOS: FiltrosPainel = {
  de: '', ate: '', fornecedor: '', comprador: '', solicitante: '',
  familia: '', centroCusto: '', regional: '', gerente: '', cliente: '',
};

const PARAMETRO: Record<keyof FiltrosPainel, string> = {
  de: 'from', ate: 'to', fornecedor: 'supplierId', comprador: 'buyerId', solicitante: 'requesterId',
  familia: 'family', centroCusto: 'costCenter', regional: 'region', gerente: 'manager', cliente: 'client',
};

export function consultaDoPainel(f: FiltrosPainel) {
  const q = new URLSearchParams();
  for (const [campo, parametro] of Object.entries(PARAMETRO) as [keyof FiltrosPainel, string][])
    if (f[campo]) q.set(parametro, f[campo]);
  return q;
}

export const dashboardDeSuprimentos = (f: FiltrosPainel, signal?: AbortSignal) =>
  api<DashboardSuprimentos>(`/api/v1/analytics/supply?${consultaDoPainel(f)}`, { signal });

/**
 * Variação do período contra o anterior. Sem base anterior, qualquer coisa
 * acima de zero é 100% — foi o que o legado mostrava, e evita dividir por zero.
 */
export function variacao(atual: number, anterior: number) {
  const pct = anterior > 0 ? Math.round(((atual - anterior) / anterior) * 100) : (atual > 0 ? 100 : 0);
  return { pct, sinal: pct > 0 ? '▲' : pct < 0 ? '▼' : '•', classe: pct > 0 ? 'text-ok' : pct < 0 ? 'text-perigo' : 'text-texto-suave' };
}

/** OTIF e score composto usam a mesma régua de cor em todo o painel. */
export const classeDeFaixa = (v: number, bom: number, medio: number) =>
  v >= bom ? 'bg-ok-fundo text-ok' : v >= medio ? 'bg-aviso-fundo text-aviso' : 'bg-perigo-fundo text-perigo';
