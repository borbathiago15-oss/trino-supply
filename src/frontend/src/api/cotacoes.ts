import { api, enviarArquivo } from './cliente';
import { ROTULO_HOMOLOGACAO, type SituacaoHomologacao } from './fornecedores';

/**
 * Situações do processo de cotação, com as chaves que o backend emite
 * (`QStatusLabel`) e os rótulos que o time já lê no sistema clássico.
 */
export const ROTULO_RFQ: Record<string, { rotulo: string; classe: string }> = {
  COTACAO_ABERTA: { rotulo: 'Aguardando propostas', classe: 'bg-slate-100 text-slate-600' },
  EM_ANALISE: { rotulo: 'Em análise', classe: 'bg-blue-50 text-blue-800' },
  AGUARDANDO_GERENTE: { rotulo: 'Aguardando Aprovador 01', classe: 'bg-aviso-fundo text-aviso' },
  AGUARDANDO_DIRETOR: { rotulo: 'Aguardando Aprovador 02', classe: 'bg-aviso-fundo text-aviso' },
  APROVADO_PARA_EMISSAO: { rotulo: 'Aprovado — registrar O.C.', classe: 'bg-teal-50 text-teal-800' },
  OC_REGISTRADA: { rotulo: 'O.C. registrada', classe: 'bg-ok-fundo text-ok' },
  REJEITADO: { rotulo: 'Rejeitado', classe: 'bg-perigo-fundo text-perigo' },
  CANCELADA: { rotulo: 'Cancelada', classe: 'bg-slate-100 text-slate-500' },
};

export interface PropostaResumo { id: string; supplierName: string; totalValue: number | null }

export interface ProcessoParaAprovar {
  id: string;
  number: string;
  status: string;
  costCenter: string;
  sourcePrNumber: string | null;
  justification: string | null;
  selection: { winnerProposalId: string; justification: string | null } | null;
  proposals: PropostaResumo[];
}

/** Processos cotados aguardando a alçada de quem está logado. */
export const processosParaMinhaAprovacao = async (signal?: AbortSignal) =>
  (await api<{ items: ProcessoParaAprovar[] }>('/api/v1/quotations/my-approvals', { signal })).items;

/** Proposta vencedora escolhida pelo comprador, quando já houver seleção. */
export function propostaVencedora(q: ProcessoParaAprovar): PropostaResumo | null {
  if (!q.selection) return null;
  return q.proposals?.find((p) => p.id === q.selection!.winnerProposalId) ?? null;
}

// ---- fila de solicitações aguardando cotação ------------------------------

export type TipoCotacao = 'COMPRA' | 'SERVICO' | 'BID';

export const ROTULO_TIPO: Record<TipoCotacao, string> = {
  COMPRA: 'Cotação de compra',
  SERVICO: 'Cotação de serviço',
  BID: 'BID',
};

export interface ItemDaFila {
  id: string;
  sequence: number;
  catalogCode: string | null;
  description: string;
  quantity: number;
  unitOfMeasure: string | null;
  estimatedUnitPrice: number | null;
  family: string;
}

export interface ScNaFila {
  id: string;
  number: string;
  requesterLabel: string;
  costCenter: string;
  justification: string | null;
  totalEstimatedValue: number;
  neededBy: string | null;
  assignedToId: string | null;
  assignedToLabel: string | null;
  /** Preenchido quando a SC está retida: diz de quem é a aprovação pendente. */
  blockReason: string | null;
  /** Parte da SC já foi para um processo; o que sobrou continua cotável. */
  partial: boolean;
  families: string[];
  items: ItemDaFila[];
}

export const filaDeCotacao = async (signal?: AbortSignal) =>
  ((await api<{ items: ScNaFila[] }>('/api/v1/quotations/queue', { signal })).items ?? [])
    .map((r) => ({ ...r, items: r.items ?? [], families: r.families ?? [] }));

export interface NovoProcesso {
  prItemIds: string[];
  kind: TipoCotacao;
  deadline: string | null;
  notes?: string | null;
}

export const abrirProcesso = (dados: NovoProcesso) =>
  api<{ id: string; number: string }>('/api/v1/quotations/', { method: 'POST', body: dados });

/**
 * Fecha os itens pelo contrato de parceria: o processo nasce já decidido, com o preço
 * acordado, e para nas aprovações. Não pula o BID — reconhece que ele aconteceu quando o
 * contrato foi negociado. Item fora do contrato é recusado (CT-ERR-021) com o nome dele
 * na mensagem, para o comprador separar o que fecha do que ainda precisa ser cotado.
 */
export const fecharPorContrato = (prItemIds: string[], supplierId: string) =>
  api<{ id: string; number: string }>('/api/v1/quotations/por-contrato',
    { method: 'POST', body: { prItemIds, supplierId } });

/**
 * Itens marcados na fila, agrupados por família — a base das duas ações da
 * tela: um processo só, ou um processo por família.
 */
export function agruparPorFamilia(marcados: { id: string; familia: string }[]) {
  const grupos = new Map<string, string[]>();
  for (const m of marcados) {
    const atual = grupos.get(m.familia) ?? [];
    atual.push(m.id);
    grupos.set(m.familia, atual);
  }
  return grupos;
}

/**
 * O que a tela pode fazer com a seleção atual. Itens de centros de custo
 * diferentes não entram no mesmo processo — o backend recusa, e o legado já
 * avisava antes de tentar.
 */
export function situacaoDaSelecao(marcados: { id: string; centroCusto: string; familia: string }[]) {
  const centros = new Set(marcados.map((m) => m.centroCusto.toUpperCase()));
  const familias = new Set(marcados.map((m) => m.familia));
  const misturado = centros.size > 1;
  return {
    total: marcados.length,
    familias: familias.size,
    misturado,
    podeJuntar: marcados.length > 0 && !misturado,
    /** Separar só faz sentido com duas famílias ou mais. */
    podeSeparar: marcados.length > 0 && !misturado && familias.size > 1,
    aviso: misturado ? 'Centros de custo diferentes — desmarque para juntar num processo.' : null,
  };
}

// ---- processo de cotação (detalhe) ----------------------------------------

export interface ItemDoProcesso {
  id: string;
  sequence: number;
  catalogItemId: string | null;
  catalogCode: string | null;
  description: string;
  quantity: number;
  unitOfMeasure: string;
  sourcePrNumber: string | null;
  family: string;
}

export interface FornecedorConvidado {
  supplierId: string;
  supplierName: string;
  taxId: string;
  invitedAt: string;
  invitedByLabel: string | null;
  hasProposal: boolean;
}

export interface ItemDaProposta { quotationItemId: string; unitPrice: number; quantity: number }

export interface Proposta {
  id: string;
  supplierId: string;
  supplierName: string;
  version: number;
  totalValue: number;
  deliveryDays: number | null;
  paymentTerms: string | null;
  paymentMethodName: string | null;
  paymentDays: number | null;
  freightValue: number | null;
  taxValue: number | null;
  otherCosts: number | null;
  discountValue: number | null;
  validUntil: string | null;
  currency: string | null;
  notes: string | null;
  submittedVia: string;
  submittedByLabel: string | null;
  submittedAt: string;
  attachmentDocumentId: string | null;
  attachmentFileName: string | null;
  /** Só a última versão de cada fornecedor entra no mapa de comparação. */
  isLatest: boolean;
  isWinner: boolean;
  items: ItemDaProposta[];
}

export interface Adjudicacao {
  id: string;
  family: string;
  /** Item adjudicado quando a divisão é por item; nulo significa a família inteira. */
  quotationItemId: string | null;
  supplierId: string;
  supplierName: string;
  proposalId: string;
  proposalVersion: number;
  itemsValue: number;
  totalValue: number;
  criteria: string | null;
  justification: string | null;
  byLabel: string | null;
  purchaseOrderId: string | null;
  purchaseOrderNumber: string | null;
}

export interface OcPendente {
  supplierId: string;
  supplierName: string;
  families: string[];
  totalValue: number;
}

export interface OcDoProcesso {
  id: string;
  number: string | null;
  supplierName: string;
  families: string[];
  totalValue: number;
}

/**
 * As três réguas do saving (§17), lado a lado porque respondem perguntas diferentes:
 *
 * | régua        | baseline                              | responde                          |
 * |--------------|---------------------------------------|-----------------------------------|
 * | negociação   | primeira proposta do vencedor         | quanto o comprador arrancou       |
 * | concorrência | maior proposta completa do BID        | quanto a disputa valeu            |
 * | orçamento    | o que o solicitante disse que tinha   | quanto sobrou do previsto         |
 *
 * As duas últimas são nulas quando não se aplicam — proponente único não é
 * concorrência, e SC sem orçamento não tem meta a bater.
 */
export interface GanhoNegociado {
  baselineValue: number;
  closedValue: number;
  value: number;
  percent: number | null;
  competitionBaselineValue: number | null;
  competitionValue: number | null;
  budgetBaselineValue: number | null;
  budgetValue: number | null;
  notes: string | null;
  byLabel: string | null;
  at: string;
}

export interface Processo {
  id: string;
  number: string;
  kind: string;
  status: string;
  sourcePrNumber: string | null;
  sourcePrNumbers: string[];
  costCenter: string;
  justification: string | null;
  deadline: string | null;
  notes: string | null;
  createdByLabel: string | null;
  createdAt: string;
  decisionReason: string | null;
  items: ItemDoProcesso[];
  families: string[];
  suppliers: FornecedorConvidado[];
  proposals: Proposta[];
  selection: {
    winnerSupplierId: string; winnerProposalId: string; criteria: string | null;
    justification: string; by: string | null; byLabel: string | null;
  } | null;
  managerApproval: { by: string | null; byLabel: string | null; at: string } | null;
  directorApproval: { by: string | null; byLabel: string | null; at: string } | null;
  awards: Adjudicacao[];
  /** A compra ficou com mais de um fornecedor. */
  splitAward: boolean;
  pendingPoSuppliers: OcPendente[];
  purchaseOrders: OcDoProcesso[];
  purchaseOrderId: string | null;
  purchaseOrderNumber: string | null;
  saving: GanhoNegociado | null;
}

/** Critérios oferecidos na escolha do vencedor — os mesmos do sistema clássico. */
export const CRITERIOS = [
  'Preço', 'Prazo de entrega', 'Condição de pagamento', 'Atendimento aos requisitos',
  'Qualidade', 'Condição comercial', 'Histórico do fornecedor',
] as const;

const base = '/api/v1/quotations';

const normalizar = (q: Processo): Processo => ({
  ...q,
  items: q.items ?? [], families: q.families ?? [], suppliers: q.suppliers ?? [],
  proposals: (q.proposals ?? []).map((p) => ({ ...p, items: p.items ?? [] })),
  awards: q.awards ?? [], pendingPoSuppliers: q.pendingPoSuppliers ?? [],
  purchaseOrders: q.purchaseOrders ?? [], sourcePrNumbers: q.sourcePrNumbers ?? [],
});

export interface PaginaDeProcessos { itens: Processo[]; total: number }

/** Busca e situação no servidor; `total` é quantos existem, não quantos vieram. */
export async function listarProcessos(
  { busca, situacao, tamanho }: { busca?: string; situacao?: string; tamanho?: number } = {},
  signal?: AbortSignal,
): Promise<PaginaDeProcessos> {
  const params = new URLSearchParams();
  if (busca?.trim()) params.set('q', busca.trim());
  if (situacao) params.set('status', situacao);
  if (tamanho) params.set('tamanho', String(tamanho));
  const consulta = params.toString();
  const r = await api<{ items: Processo[]; total: number }>(
    `${base}/${consulta ? `?${consulta}` : ''}`, { signal });
  return { itens: (r.items ?? []).map(normalizar), total: r.total ?? 0 };
}

export const lerProcesso = async (id: string, signal?: AbortSignal) =>
  normalizar(await api<Processo>(`${base}/${id}`, { signal }));

export const convidarFornecedor = (id: string, supplierIds: string[]) =>
  api<Processo>(`${base}/${id}/suppliers`, { method: 'POST', body: { supplierIds } });

export interface PropostaManual {
  supplierId: string;
  deliveryDays: number | null;
  paymentTerms: string | null;
  paymentMethodName: string | null;
  paymentDays: number | null;
  freightValue: number | null;
  taxValue: number | null;
  otherCosts: number | null;
  discountValue: number | null;
  validUntil: string | null;
  currency: string;
  notes: string | null;
  items: { quotationItemId: string; unitPrice: number }[];
}

/** O que já se pagou por um item deste processo. Ausente = nunca comprado. */
export interface HistoricoDoItem {
  quotationItemId: string;
  average: number;
  last: number;
  min: number;
  max: number;
  purchases: number;
  lastSupplier: string;
  lastAt: string;
}

export interface HistoricoDoProcesso {
  /** Acima de quantos por cento a variação merece aviso — quem define é o servidor. */
  warnAbovePct: number;
  items: HistoricoDoItem[];
}

export const historicoDoProcesso = (id: string, signal?: AbortSignal) =>
  api<HistoricoDoProcesso>(`${base}/${id}/price-history`, { signal });

/**
 * Variação do preço digitado contra a média já paga. Nula quando não há histórico:
 * zero diria "está no preço de sempre" para um produto que nunca foi comprado, e isso
 * é afirmar em vez de admitir que não se sabe.
 */
export function variacaoDoPreco(h: HistoricoDoItem | undefined, precoDigitado: string): number | null {
  const preco = Number(precoDigitado.replace(',', '.'));
  if (!h || h.average <= 0 || !precoDigitado.trim() || Number.isNaN(preco) || preco <= 0) return null;
  return Math.round((preco / h.average - 1) * 1000) / 10;
}

// ---- Score multicritério (informativo) -------------------------------------

/** Um critério do score e quanto ele pesa. O peso vem do servidor: a tela não o inventa. */
export interface CriterioDoScore {
  code: string;
  label: string;
  weightPct: number;
  help: string;
}

/**
 * A nota de um fornecedor na disputa. Cada `*Pct` é o componente já normalizado contra o
 * melhor da disputa; `null` é dado que não existe — e componente sem dado sai da conta em
 * vez de valer zero, senão o fornecedor novo seria punido por ser novo.
 */
export interface LinhaDeScore {
  supplierId: string;
  supplierName: string;
  score: number;
  pricePct: number | null;
  deliveryPct: number | null;
  paymentPct: number | null;
  otifPct: number | null;
  riskPct: number | null;
}

export interface MapaDeScore {
  note: string;
  criteria: CriterioDoScore[];
  items: LinhaDeScore[];
}

export const mapaDeScore = async (id: string, signal?: AbortSignal) => {
  const r = await api<MapaDeScore>(`${base}/${id}/score-map`, { signal });
  return { ...r, criteria: r.criteria ?? [], items: r.items ?? [] };
};

/** O componente do critério dentro da linha, para a tela não repetir o mapeamento. */
export const componenteDoScore = (linha: LinhaDeScore, code: string): number | null => ({
  price: linha.pricePct, delivery: linha.deliveryPct, payment: linha.paymentPct,
  otif: linha.otifPct, risk: linha.riskPct,
}[code] ?? null);

/** Preço que o contrato de parceria já fixou para um item deste processo. */
export interface PrecoDeContrato {
  quotationItemId: string;
  description: string;
  unitPrice: number;
  deliveryDays: number | null;
  paymentTerms: string | null;
  paymentDays: number | null;
}

/**
 * O que o contrato de parceria com este fornecedor já responde sobre o processo.
 * `current` falso significa que não há contrato vigente — e aí nada é preenchido:
 * preço de contrato vencido entrando calado na proposta é pior do que campo vazio.
 */
export interface CoberturaDoContrato {
  current: boolean;
  contractNumber: string | null;
  validUntil: string | null;
  items: PrecoDeContrato[];
}

export const precosDeContrato = (id: string, supplierId: string, signal?: AbortSignal) =>
  api<CoberturaDoContrato>(`${base}/${id}/contract-prices/${supplierId}`, { signal });

export const registrarProposta = (id: string, dados: PropostaManual) =>
  api<Processo>(`${base}/${id}/proposals`, { method: 'POST', body: dados });

/** Orçamento que o fornecedor mandou por e-mail, arquivado na proposta. */
export const anexarNaProposta = (quotationId: string, proposalId: string, arquivo: File) =>
  enviarArquivo<{ documentId: string; fileName: string }>(
    `${base}/${quotationId}/proposals/${proposalId}/attachment`, arquivo);

/**
 * A proposta recém-registrada, achada no processo que o POST devolve: é a
 * versão vigente daquele fornecedor. O endpoint responde com o processo
 * inteiro, não com o id da proposta.
 */
export const propostaVigenteDe = (q: Processo, supplierId: string) =>
  propostasVigentes(q).find((p) => p.supplierId === supplierId) ?? null;

export const encerrarParaAnalise = (id: string) =>
  api<Processo>(`${base}/${id}/close`, { method: 'POST' });

export interface EscolhaDoVencedor {
  proposalId: string;
  criteria: string[];
  justification: string;
  /**
   * Compra dividida. Cada entrada adjudica um escopo: a família (`family`), ou um item
   * dentro dela (`quotationItemId`) — o papel com um fornecedor e a caneta com outro.
   * Com o item apontado, a família vem dele no servidor e não precisa ser repetida aqui.
   */
  awards?: {
    family: string; proposalId: string; criteria: string[]; justification: string | null;
    quotationItemId?: string;
  }[];
}

export const escolherVencedor = (id: string, escolha: EscolhaDoVencedor) =>
  api<Processo>(`${base}/${id}/select-winner`, { method: 'POST', body: escolha });

export type Decisao = 'APROVAR' | 'AJUSTES' | 'REJEITAR';
export type Alcada = 'manager' | 'director';

export const decidir = (id: string, alcada: Alcada, decision: Decisao, reason: string | null) =>
  api<Processo>(`${base}/${id}/${alcada}-decision`, { method: 'POST', body: { decision, reason } });

export interface RegistroDeOc {
  erpNumber: string;
  issuedOn: string | null;
  notes: string | null;
  supplierId?: string | null;
  overLimitJustification?: string | null;
  /** Obrigatório quando `erpNumber` vem vazio (PO-BR-011). */
  noErpReason?: string | null;
}

/**
 * A O.C. é gerada no ERP e sem ela o processo não fecha. A única exceção é a
 * observação dizendo por que ela não foi gerada — este é o mínimo que o
 * servidor aceita para tratá-la como justificativa de verdade (PO-BR-011).
 */
export const MINIMO_MOTIVO_SEM_OC = 10;

export const registrarOc = (id: string, dados: RegistroDeOc) =>
  api<Processo>(`${base}/${id}/register-po`, { method: 'POST', body: dados });

export interface Negociacao {
  supplierId: string;
  closedValue: number | null;
  discountPercent: number | null;
  notes: string | null;
}

export const registrarNegociacao = (id: string, dados: Negociacao) =>
  api<Processo>(`${base}/${id}/negotiation`, { method: 'POST', body: dados });

export const cancelarProcesso = (id: string, reason: string) =>
  api<Processo>(`${base}/${id}/cancel`, { method: 'POST', body: { reason } });

// ---- mapa por família: quem pode levar cada lote ---------------------------

/**
 * A oferta de um fornecedor para uma família inteira, com a situação dele no
 * cadastro. `canWin` é a régua do servidor: cotou a família inteira, está ativo
 * e está homologado. A tela usa isso para não oferecer uma escolha que a API
 * vai recusar depois da justificativa escrita.
 */
export interface OfertaDaFamilia {
  supplierId: string;
  supplierName: string;
  proposalId: string;
  proposalVersion: number;
  itemsValue: number;
  totalValue: number;
  deliveryDays: number | null;
  paymentTerms: string | null;
  /** Cotou todos os itens da família. Meia cotação não leva o lote (RFQ-ERR-024). */
  complete: boolean;
  /** Menor total entre as ofertas que podem vencer esta família. */
  cheapest: boolean;
  homologation: SituacaoHomologacao;
  active: boolean;
  canWin: boolean;
}

export interface LoteDaFamilia {
  family: string;
  itemCount: number;
  quantity: number;
  offers: OfertaDaFamilia[];
}

export const mapaDeFamilias = async (id: string, signal?: AbortSignal) =>
  (await api<{ note: string; items: LoteDaFamilia[] }>(`${base}/${id}/family-map`, { signal })).items;

/**
 * Por que esta oferta não pode levar a família — na ordem em que o servidor
 * verifica, para a tela dizer a mesma coisa que a API diria. `null` quando pode.
 */
export function impedimentoDaOferta(o: OfertaDaFamilia): string | null {
  if (!o.complete) return 'não cotou a família inteira (RFQ-ERR-024)';
  if (!o.active) return 'fornecedor inativo no cadastro (RFQ-ERR-040)';
  if (o.homologation !== 'HOMOLOGADO')
    return `${ROTULO_HOMOLOGACAO[o.homologation]?.rotulo ?? o.homologation} — homologação pendente (SUP-ERR-030)`;
  return null;
}

/** A oferta de cada proposta dentro de um lote, achada pelo id da proposta. */
export const ofertaDaProposta = (lote: LoteDaFamilia | null, proposalId: string) =>
  lote?.offers.find((o) => o.proposalId === proposalId) ?? null;

// ---- leituras do mapa de comparação ---------------------------------------

/** Só a última versão de cada fornecedor entra na comparação. */
export const propostasVigentes = (q: Processo) => q.proposals.filter((p) => p.isLatest);

/** Preço unitário de um item numa proposta, ou null quando não foi cotado. */
export const precoDoItem = (p: Proposta, itemId: string) =>
  p.items.find((x) => x.quotationItemId === itemId)?.unitPrice ?? null;

/** Menor preço de um item entre as propostas vigentes — destacado no mapa. */
export function melhorPreco(q: Processo, itemId: string): number | null {
  const precos = propostasVigentes(q).map((p) => precoDoItem(p, itemId)).filter((v): v is number => v != null);
  return precos.length ? Math.min(...precos) : null;
}

/** Menor total entre as propostas vigentes. Com uma proposta só, não há o que comparar. */
export function menorTotal(q: Processo): number | null {
  const vigentes = propostasVigentes(q);
  return vigentes.length > 1 ? Math.min(...vigentes.map((p) => p.totalValue)) : null;
}

/** O que o usuário pode fazer no processo, dado o papel e a etapa. */
/**
 * Segregação de funções (RFQ-ERR-030): quem escolheu o fornecedor não aprova a
 * própria escolha, e o Nível 2 não pode ser quem já resolveu o Nível 1. O
 * servidor recusa de qualquer jeito — aqui a regra existe para a tela não
 * oferecer um botão que só vai dar erro, e para dizer o porquê.
 *
 * `de` é o id de quem está logado. Sem ele (ou sem os ids no processo) a tela
 * não trava nada: prefere-se o botão que falha à ação escondida por engano.
 */
export function conflitoDeSegregacao(q: Processo, de: string | undefined, alcada: Alcada): string | null {
  if (!de) return null;
  if (q.selection?.by === de)
    return 'Você escolheu o fornecedor deste processo — a aprovação é de outra pessoa (RFQ-ERR-030).';
  if (alcada === 'director' && q.managerApproval?.by === de)
    return 'Você deu a aprovação de Nível 1 deste processo — o Nível 2 é de outra pessoa (RFQ-ERR-030).';
  return null;
}

export function acoesDisponiveis(
  q: Processo,
  { conduz, aprovaNivel1, aprovaNivel2, de }: {
    conduz: boolean; aprovaNivel1: boolean; aprovaNivel2: boolean; de?: string;
  },
) {
  const vigentes = propostasVigentes(q);
  const emAberto = q.status === 'COTACAO_ABERTA';
  const emAnalise = q.status === 'EM_ANALISE';
  const barrado1 = conflitoDeSegregacao(q, de, 'manager');
  const barrado2 = conflitoDeSegregacao(q, de, 'director');
  return {
    /** Motivo pelo qual a aprovação da etapa atual está vedada a quem olha, se houver. */
    conflitoSegregacao: q.status === 'AGUARDANDO_GERENTE' ? barrado1
      : q.status === 'AGUARDANDO_DIRETOR' ? barrado2 : null,
    convidar: conduz && (emAberto || emAnalise),
    registrarProposta: conduz && (emAberto || emAnalise) && q.suppliers.length > 0,
    negociar: conduz && (emAberto || emAnalise) && vigentes.length > 0,
    encerrar: conduz && emAberto,
    escolherVencedor: conduz && emAnalise && vigentes.length > 0,
    /**
     * Mais de um item: a escolha vira grade item × fornecedor. É o que permite dividir
     * a compra — entre famílias e, agora, dentro da mesma família (o papel com um
     * fornecedor e a caneta com outro). Com um item só, a grade seria uma tabela de uma
     * linha e a escolha simples diz mais, porque mostra prazo e condição de pagamento.
     */
    porItem: q.items.length > 1,
    decidirNivel1: aprovaNivel1 && q.status === 'AGUARDANDO_GERENTE' && !barrado1,
    decidirNivel2: aprovaNivel2 && q.status === 'AGUARDANDO_DIRETOR' && !barrado2,
    registrarOc: conduz && q.status === 'APROVADO_PARA_EMISSAO',
    cancelar: conduz && !['OC_REGISTRADA', 'REJEITADO', 'CANCELADA'].includes(q.status),
  };
}
