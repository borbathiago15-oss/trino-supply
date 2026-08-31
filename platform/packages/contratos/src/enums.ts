/**
 * Enums do domínio — a MESMA lista que o banco tem. Ficam aqui, e não
 * duplicados na API e no frontend, porque duas cópias divergem na primeira
 * fase que acrescentar um estado.
 */

export const STATUS_REQUISICAO = [
  'RASCUNHO', 'SUBMETIDA', 'EM_TRIAGEM', 'DEVOLVIDA_AJUSTE', 'EM_COTACAO', 'COTADA',
  'APROVACAO_ALCADA', 'PEDIDO_GERADO', 'RECEBIDA_PARCIAL', 'RECEBIDA_TOTAL', 'REJEITADA', 'CANCELADA',
] as const;
export type StatusRequisicao = (typeof STATUS_REQUISICAO)[number];

export const PRIORIDADES = ['BAIXA', 'NORMAL', 'ALTA', 'EMERGENCIAL'] as const;
export type Prioridade = (typeof PRIORIDADES)[number];

export const STATUS_ITEM_REQUISICAO = ['ATIVO', 'EM_COTACAO', 'COTADO', 'PEDIDO', 'ATENDIDO', 'CANCELADO'] as const;
export type StatusItemRequisicao = (typeof STATUS_ITEM_REQUISICAO)[number];

export const STATUS_COTACAO = ['ABERTA', 'ENCERRADA', 'CANCELADA'] as const;
export type StatusCotacao = (typeof STATUS_COTACAO)[number];

export const STATUS_PEDIDO = [
  'EMITIDO', 'ENVIADO', 'CONFIRMADO', 'RECEBIDO_PARCIAL', 'RECEBIDO_TOTAL', 'CANCELADO',
] as const;
export type StatusPedido = (typeof STATUS_PEDIDO)[number];

export const OCORRENCIAS_RECEBIMENTO = [
  'SEM_OCORRENCIA', 'AVARIA', 'DIVERGENCIA_QUANTIDADE', 'DIVERGENCIA_ESPECIFICACAO', 'ATRASO', 'RECUSA_TOTAL',
] as const;
export type OcorrenciaRecebimento = (typeof OCORRENCIAS_RECEBIMENTO)[number];

export const DECISOES_ETAPA = ['PENDENTE', 'APROVADO', 'REJEITADO'] as const;
export type DecisaoEtapa = (typeof DECISOES_ETAPA)[number];

export const STATUS_INSTANCIA = ['PENDENTE', 'APROVADA', 'REJEITADA', 'INVALIDADA'] as const;
export type StatusInstancia = (typeof STATUS_INSTANCIA)[number];

export const TIPOS_LOTE_IMPORTACAO = ['CATALOGO_SKU', 'PARAMETRO_ESTOQUE', 'FORNECEDOR', 'ORCAMENTO_CC'] as const;
export type TipoLoteImportacao = (typeof TIPOS_LOTE_IMPORTACAO)[number];

/** Rótulos em português para a interface — um lugar só. */
export const ROTULO_STATUS_REQUISICAO: Record<StatusRequisicao, string> = {
  RASCUNHO: 'Rascunho',
  SUBMETIDA: 'Submetida',
  EM_TRIAGEM: 'Em triagem',
  DEVOLVIDA_AJUSTE: 'Devolvida para ajuste',
  EM_COTACAO: 'Em cotação',
  COTADA: 'Cotada',
  APROVACAO_ALCADA: 'Aprovação de alçada',
  PEDIDO_GERADO: 'Pedido gerado',
  RECEBIDA_PARCIAL: 'Recebida parcialmente',
  RECEBIDA_TOTAL: 'Recebida',
  REJEITADA: 'Rejeitada',
  CANCELADA: 'Cancelada',
};

export const ROTULO_PRIORIDADE: Record<Prioridade, string> = {
  BAIXA: 'Baixa',
  NORMAL: 'Normal',
  ALTA: 'Alta',
  EMERGENCIAL: 'Emergencial',
};

export const ROTULO_OCORRENCIA: Record<OcorrenciaRecebimento, string> = {
  SEM_OCORRENCIA: 'Sem ocorrência',
  AVARIA: 'Avaria',
  DIVERGENCIA_QUANTIDADE: 'Divergência de quantidade',
  DIVERGENCIA_ESPECIFICACAO: 'Divergência de especificação',
  ATRASO: 'Atraso',
  RECUSA_TOTAL: 'Recusa total',
};
