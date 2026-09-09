import { api } from './cliente';

/**
 * Torre de Controle: uma linha por **item** de compra, com a etapa em que ele
 * está e a situação em que se encontra.
 */

export interface LinhaDaTorre {
  itemId: string;
  requisitionId: string;
  prNumber: string;
  sequence: number;
  catalogCode: string | null;
  description: string;
  quantity: number;
  unitOfMeasure: string;
  requesterLabel: string;
  company: string | null;
  costCenter: string;
  buyerLabel: string | null;
  supplierName: string | null;
  /** Onde o item está no fluxo. */
  stage: string;
  stageLabel: string;
  /** Como ele está — a mesma situação que o solicitante vê. */
  statusKey: string;
  statusLabel: string;
  statusTone: string;
  priority: string;
  neededBy: string | null;
  promisedDate: string | null;
  late: boolean;
  value: number | null;
  quotationId: string | null;
  quotationNumber: string | null;
  purchaseOrderId: string | null;
  purchaseOrderNumber: string | null;
  /** Por que a linha é exceção, ou nulo quando ela segue o caminho normal. */
  exceptionReason: string | null;
  /** A próxima ação esperada — o que a linha pede que se faça agora. */
  actionLabel: string | null;
  /** Se essa ação é do comprador; é o que define a fila prioritária. */
  needsBuyer: boolean;
}

export interface KpisDaTorre {
  total: number;
  novos: number;
  emCotacao: number;
  aguardandoAprovacao: number;
  aguardandoOc: number;
  aguardandoRecebimento: number;
  atrasados: number;
  urgentes: number;
  valor: number;
  /** O.C. emitida e nenhuma NF: quem deve agir é o fornecedor. */
  emFaturamento: number;
  /** O que o sistema já grava como fora do padrão — sem O.C. do ERP, cancelado, devolvido. */
  excecoes: number;
  /** Quantos itens em cada faixa de tempo na fila, na ordem de FAIXAS_DE_FILA. */
  porFaixaDeAging: number[] | null;
}

/**
 * As faixas de tempo na fila. Vieram da tela de triagem sem mudar de fronteira: eram
 * elas que diziam ao comprador "isto está parado há tempo demais", e o número tem de
 * continuar querendo dizer a mesma coisa agora que as duas telas viraram uma.
 */
export const FAIXAS_DE_FILA = [
  { rotulo: '0–2 dias', classe: 'bg-ok-fundo text-ok' },
  { rotulo: '3–5 dias', classe: 'bg-teal-50 text-teal-800' },
  { rotulo: '6–10 dias', classe: 'bg-aviso-fundo text-aviso' },
  { rotulo: '+10 dias', classe: 'bg-perigo-fundo text-perigo' },
] as const;

export interface PaginaDaTorre {
  items: LinhaDaTorre[];
  kpis: KpisDaTorre;
  filterOptions: {
    companies: string[];
    costCenters: { code: string; name: string }[];
    families: string[];
    requesters: { id: string; label: string }[];
    buyers: { id: string; label: string }[];
  };
  page: number;
  pageSize: number;
  total: number;
  pages: number;
  /** O filtro derivado bateu no teto: a tela avisa em vez de calar. */
  capped: boolean;
  cap: number;
}

export interface FiltrosDaTorre {
  busca: string;
  etapa: string;
  situacao: string;
  empresa: string;
  centroCusto: string;
  familia: string;
  solicitante: string;
  comprador: string;
  prioridade: string;
  atrasados: boolean;
  /** §5.1 — período de criação da SC. */
  de: string;
  ate: string;
  /** §5.1 — fornecedor e número da O.C. (própria ou do ERP): casam por trecho. */
  fornecedor: string;
  numeroOc: string;
  /** §5.1 — faixa de prazo, sobre a previsão que a linha mostra. */
  prazoDe: string;
  prazoAte: string;
  /** §5.1 — faixa de valor da linha. */
  valorDe: string;
  valorAte: string;
  /** §5 — só o que virou exceção. */
  excecoes: boolean;
  /** §5 — a fila prioritária: só o que espera ação do comprador. */
  minhaFila: boolean;
  /** Faixa de tempo na fila (índice em FAIXAS_DE_FILA), ou vazio para todas. */
  faixaDeFila: string;
  pagina: number;
}

export const FILTROS_TORRE_VAZIOS: FiltrosDaTorre = {
  busca: '', etapa: '', situacao: '', empresa: '', centroCusto: '', familia: '',
  solicitante: '', comprador: '', prioridade: '', atrasados: false,
  de: '', ate: '', fornecedor: '', numeroOc: '', prazoDe: '', prazoAte: '',
  valorDe: '', valorAte: '', excecoes: false, minhaFila: false, faixaDeFila: '', pagina: 1,
};

/** As etapas, na ordem em que o item as percorre — os mesmos nomes do servidor. */
export const ETAPAS = [
  { key: 'SOLICITACAO', label: 'Solicitação' },
  { key: 'COTACAO', label: 'Cotação' },
  { key: 'APROVACAO', label: 'Aprovação' },
  { key: 'ORDEM_DE_COMPRA', label: 'Ordem de Compra' },
  { key: 'RECEBIMENTO', label: 'Recebimento' },
  { key: 'ENCERRADO', label: 'Encerrado' },
] as const;

/** A cor de cada tom devolvido pelo servidor. */
export const CLASSE_DO_TOM: Record<string, string> = {
  '': 'bg-slate-100 text-slate-600',
  dev: 'bg-slate-100 text-slate-500',
  warn: 'bg-aviso-fundo text-aviso',
  teal: 'bg-teal-50 text-teal-800',
  on: 'bg-ok-fundo text-ok',
  off: 'bg-perigo-fundo text-perigo',
  info: 'bg-blue-50 text-blue-800',
  purple: 'bg-purple-50 text-purple-800',
  orange: 'bg-orange-50 text-orange-800',
};

export function consultaDaTorre(f: FiltrosDaTorre, tamanho = 50): string {
  const q = new URLSearchParams();
  if (f.busca) q.set('search', f.busca);
  if (f.etapa) q.set('stage', f.etapa);
  if (f.situacao) q.set('status', f.situacao);
  if (f.empresa) q.set('company', f.empresa);
  if (f.centroCusto) q.set('costCenter', f.centroCusto);
  if (f.familia) q.set('family', f.familia);
  if (f.solicitante) q.set('requesterId', f.solicitante);
  if (f.comprador) q.set('buyerId', f.comprador);
  if (f.prioridade) q.set('priority', f.prioridade);
  if (f.atrasados) q.set('late', 'true');
  if (f.de) q.set('from', f.de);
  if (f.ate) q.set('to', f.ate);
  if (f.fornecedor.trim()) q.set('supplier', f.fornecedor.trim());
  if (f.numeroOc.trim()) q.set('orderNumber', f.numeroOc.trim());
  if (f.prazoDe) q.set('dueFrom', f.prazoDe);
  if (f.prazoAte) q.set('dueTo', f.prazoAte);
  // faixa de valor: zero é um valor legítimo, então o que descarta é o campo vazio
  if (f.valorDe.trim() && Number.isFinite(Number(f.valorDe))) q.set('minValue', f.valorDe.trim());
  if (f.valorAte.trim() && Number.isFinite(Number(f.valorAte))) q.set('maxValue', f.valorAte.trim());
  if (f.excecoes) q.set('exception', 'true');
  if (f.minhaFila) q.set('needsBuyer', 'true');
  if (f.faixaDeFila !== '') q.set('agingBand', f.faixaDeFila);
  q.set('page', String(f.pagina));
  q.set('pageSize', String(tamanho));
  return `?${q.toString()}`;
}

export const torreDeControle = (f: FiltrosDaTorre, signal?: AbortSignal) =>
  api<PaginaDaTorre>(`/api/v1/control-tower${consultaDaTorre(f)}`, { signal });

/**
 * Para onde a ação da linha leva. O rótulo vem do servidor (é a mesma regra que
 * monta a fila prioritária); o destino é do navegador, porque só ele conhece as
 * rotas da aplicação.
 */
export function destinoDaAcao(i: LinhaDaTorre): string {
  if (i.purchaseOrderId) return `/pedidos/${i.purchaseOrderId}`;
  if (i.quotationId) return `/cotacoes/${i.quotationId}`;
  // sem processo ainda: quem não tem comprador vai para a triagem; o resto, para
  // a tela que abre a cotação
  return i.buyerLabel ? '/cotacoes/abrir' : '/gestao-solicitacoes';
}
