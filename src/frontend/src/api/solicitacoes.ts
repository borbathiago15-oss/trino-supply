import { api, enviarArquivo } from './cliente';
import { classeDoTom } from '@/dominio/tons';

/** Situação bruta da SC. A tela prefere `processStatusLabel` quando a API manda. */
export type SituacaoSc = 'DRAFT' | 'SUBMITTED' | 'IN_APPROVAL' | 'APPROVED' | 'REJECTED' | 'RETURNED' | 'CANCELLED';
export type Prioridade = 'LOW' | 'NORMAL' | 'HIGH' | 'URGENT';

export const ROTULO_SC: Record<SituacaoSc, { rotulo: string; classe: string }> = {
  DRAFT: { rotulo: 'Rascunho', classe: 'bg-slate-100 text-slate-600' },
  SUBMITTED: { rotulo: 'Em cotação', classe: 'bg-blue-50 text-blue-800' },
  IN_APPROVAL: { rotulo: 'Em aprovação', classe: 'bg-blue-50 text-blue-800' },
  APPROVED: { rotulo: 'Compra aprovada', classe: 'bg-ok-fundo text-ok' },
  REJECTED: { rotulo: 'Rejeitado', classe: 'bg-slate-100 text-slate-500' },
  RETURNED: { rotulo: 'Devolvido p/ ajuste', classe: 'bg-aviso-fundo text-aviso' },
  CANCELLED: { rotulo: 'Cancelado', classe: 'bg-slate-100 text-slate-500' },
};

export const ROTULO_PRIORIDADE: Record<Prioridade, string> = {
  LOW: 'Baixa', NORMAL: 'Normal', HIGH: 'Alta', URGENT: 'Urgente',
};

/** Tons que a API manda no status do processo, traduzidos para classes. */

export interface ItemSc {
  itemId: string;
  sequence: number;
  description: string;
  catalogCode: string | null;
  quantity: number;
  unitOfMeasure: string;
  estimatedUnitPrice: number | null;
  catalogItemId?: string | null;
}

export interface AnexoSc {
  id: string;
  documentId: string;
  fileName: string;
  sizeBytes: number;
  uploadedAt: string;
  uploadedByLabel: string | null;
}

/** Uma etapa da linha do tempo do solicitante. */
export interface EtapaDaSc {
  chave: 'enviada' | 'comprador' | 'cotacao' | 'aprovacao' | 'pedido' | 'entrega' | string;
  rotulo: string;
  situacao: 'feita' | 'atual' | 'pendente' | 'parada';
  quando: string | null;
  quem: string | null;
}

/**
 * Onde a solicitação está, na língua de quem pediu: a etapa atual, com quem está, desde
 * quando e quando chega. Vem pronto do servidor, derivado da SC, do processo e do pedido —
 * a etiqueta e a frase saem da mesma consulta e nunca discordam.
 */
export interface AcompanhamentoDaSc {
  etapas: EtapaDaSc[];
  etapaAtual: string;
  frase: string;
  comQuem: string | null;
  desde: string | null;
  previsao: string | null;
  motivo: string | null;
  /** A bola está com quem pediu: rascunho a enviar ou devolvida a corrigir. */
  precisaDoSolicitante: boolean;
  quotationId: string | null;
  quotationNumber: string | null;
  purchaseOrderId: string | null;
  purchaseOrderNumber: string | null;
  fornecedor: string | null;
}

export interface SolicitacaoCompra {
  id: string;
  number: string;
  kind: string;
  status: SituacaoSc;
  cycle: number;
  priority: Prioridade;
  urgencyReason: string | null;
  urgencyImpact: string | null;
  neededBy: string | null;
  justification: string;
  needType: string | null;
  deliveryLocation: string | null;
  company: string | null;
  /** COMPRA ou ORCAMENTO. SC anterior à regra vem como COMPRA. */
  purpose?: Finalidade;
  internalNotes: string | null;
  /** Orçamento previsto pelo solicitante (§17): a régua contra a qual o saving é medido. */
  budget: number | null;
  costCenter: string;
  requesterId: string;
  requesterLabel: string;
  totalEstimatedValue: number;
  decisionReason: string | null;
  decidedByLabel: string | null;
  approverLabel: string | null;
  approvalIssue: string | null;
  assignedToLabel: string | null;
  submittedAt: string | null;
  decidedAt: string | null;
  /** Situação única do fluxo de compras, quando a API consegue calcular. */
  processStatusLabel: string | null;
  processStatusTone: string | null;
  processStatusHint: string | null;
  /** Linha do tempo do solicitante; nula quando a API ainda não a manda. */
  acompanhamento: AcompanhamentoDaSc | null;
  attachments: AnexoSc[];
  items: ItemSc[];
}

/** A API pode mandar listas nulas e, em versão antiga, sem o acompanhamento. */
export const normalizarSc = (r: SolicitacaoCompra): SolicitacaoCompra => ({
  ...r, attachments: r.attachments ?? [], items: r.items ?? [],
  acompanhamento: r.acompanhamento ? { ...r.acompanhamento, etapas: r.acompanhamento.etapas ?? [] } : null,
});

/** Encerrada = chegou ao fim ou parou de vez: não conta como "em andamento". */
export function scEncerrada(r: Pick<SolicitacaoCompra, 'status' | 'acompanhamento'>) {
  if (r.status === 'REJECTED' || r.status === 'CANCELLED') return true;
  const a = r.acompanhamento;
  if (!a) return false;
  return a.etapas.every((e) => e.situacao === 'feita') || a.etapas.some((e) => e.situacao === 'parada');
}

/** A bola está com o solicitante: rascunho para enviar ou devolvida para corrigir. */
export const precisaDoSolicitante = (r: Pick<SolicitacaoCompra, 'status' | 'acompanhamento'>) =>
  r.acompanhamento?.precisaDoSolicitante ?? (r.status === 'DRAFT' || r.status === 'RETURNED');

/** Situação a exibir: a do processo tem prioridade sobre a bruta da SC. */
export function situacaoDaSc(r: Pick<SolicitacaoCompra, 'status' | 'processStatusLabel' | 'processStatusTone'>) {
  if (r.processStatusLabel)
    return { rotulo: r.processStatusLabel, classe: classeDoTom(r.processStatusTone) };
  return ROTULO_SC[r.status] ?? { rotulo: r.status, classe: '' };
}

/**
 * Quem criou a SC manda nela enquanto não houver comprador designado: rascunho e
 * devolvida sempre; enviada, só antes de alguém assumir a cotação.
 */
export function podeMexer(r: Pick<SolicitacaoCompra, 'status' | 'assignedToLabel'>) {
  return r.status === 'DRAFT' || r.status === 'RETURNED'
    || (r.status === 'SUBMITTED' && !r.assignedToLabel);
}

export const podeEnviar = (r: Pick<SolicitacaoCompra, 'status'>) => r.status === 'DRAFT' || r.status === 'RETURNED';

const base = '/api/v1/purchase-requisitions';

export interface PaginaDeSolicitacoes { itens: SolicitacaoCompra[]; total: number }

/**
 * Busca e situação vão para o servidor. `total` é quantas existem, não quantas
 * vieram: procurar sobre uma lista truncada responde "nada encontrado" para
 * solicitação que existe.
 */
export async function listarSolicitacoes(
  { busca, situacao, tamanho }: { busca?: string; situacao?: string; tamanho?: number } = {},
  signal?: AbortSignal,
): Promise<PaginaDeSolicitacoes> {
  const params = new URLSearchParams();
  if (busca?.trim()) params.set('q', busca.trim());
  if (situacao) params.set('status', situacao);
  if (tamanho) params.set('tamanho', String(tamanho));
  const consulta = params.toString();
  const r = await api<{ items: SolicitacaoCompra[]; total: number }>(
    `${base}/${consulta ? `?${consulta}` : ''}`, { signal });
  return { itens: (r.items ?? []).map(normalizarSc), total: r.total ?? 0 };
}

export interface ItemNovo {
  description: string;
  catalogItemId: string | null;
  unitOfMeasure: string | null;
  quantity: number;
  /**
   * Família escolhida pelo solicitante. Só vale para produto **não cadastrado**: com item
   * de catálogo o servidor usa a família do produto e ignora esta.
   */
  family?: string | null;
}

export interface DadosSc {
  justification: string;
  costCenter: string;
  priority: Prioridade;
  neededBy: string | null;
  items: ItemNovo[];
  kind: 'AVULSA' | 'CATALOGO';
  needType?: string | null;
  deliveryLocation?: string | null;
  company?: string | null;
  internalNotes?: string | null;
  /** Quanto o solicitante espera gastar. Fechar abaixo disso é saving (§17). */
  budget?: number | null;
  urgencyReason: string | null;
  urgencyImpact: string | null;
  /** Obrigatória (PR-ERR-024): orçamento para depois da cotação; compra segue para a aprovação. */
  purpose: Finalidade;
}

/**
 * Para que a SC existe. Orçamento para em "orçamento apresentado" depois da cotação e só vai à
 * aprovação se alguém decidir comprar; compra segue o caminho de sempre.
 */
export type Finalidade = 'COMPRA' | 'ORCAMENTO';

/** Corrigir a finalidade antes de a SC entrar em cotação (PR-ERR-025 depois disso). */
export const mudarFinalidade = (id: string, purpose: Finalidade) =>
  api<SolicitacaoCompra>(`${base}/${id}/purpose`, { method: 'POST', body: { purpose } });

export const criarSolicitacao = (dados: DadosSc) => api<SolicitacaoCompra>(`${base}/`, { method: 'POST', body: dados });

export interface EdicaoSc {
  justification: string;
  costCenter: string;
  priority: Prioridade;
  neededBy: string | null;
  clearNeededBy: boolean;
  urgencyReason: string | null;
  urgencyImpact: string | null;
  budget?: number | null;
  clearBudget?: boolean;
}

export const atualizarSolicitacao = (id: string, dados: EdicaoSc) =>
  api<SolicitacaoCompra>(`${base}/${id}`, { method: 'PATCH', body: dados });

export const excluirSolicitacao = (id: string) => api<unknown>(`${base}/${id}`, { method: 'DELETE' });
export const enviarSolicitacao = (id: string) => api<unknown>(`${base}/${id}/submit`, { method: 'POST', body: {} });
export const aprovarSolicitacao = (id: string, comments: string | null) =>
  api<unknown>(`${base}/${id}/approve`, { method: 'POST', body: { comments } });
export const rejeitarSolicitacao = (id: string, reason: string) =>
  api<unknown>(`${base}/${id}/reject`, { method: 'POST', body: { reason } });
export const devolverSolicitacao = (id: string, reason: string) =>
  api<unknown>(`${base}/${id}/return`, { method: 'POST', body: { reason } });

/** Anexos vão um a um; a tela conta quantos entraram e avisa os que falharam. */
export async function anexarNaSolicitacao(id: string, arquivos: File[]) {
  const falhas: string[] = [];
  let enviados = 0;
  for (const arquivo of arquivos) {
    try { await enviarArquivo(`${base}/${id}/attachments`, arquivo); enviados++; }
    catch { falhas.push(arquivo.name); }
  }
  return { enviados, falhas };
}

/** SCs do fluxo anterior aguardando decisão de quem tem alçada. */
export const aprovacoesPendentes = async (signal?: AbortSignal) =>
  (await api<{ items: SolicitacaoCompra[] }>('/api/v1/approvals/pending', { signal })).items;
