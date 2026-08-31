import {
  DecisaoEtapa, OcorrenciaRecebimento, Prioridade, StatusCotacao, StatusInstancia,
  StatusItemRequisicao, StatusPedido, StatusRequisicao, TipoLoteImportacao,
} from './enums';

/**
 * Formatos de requisição e resposta da API. O frontend tipa contra ISTO, não
 * contra `any` — se a API mudar um campo, o build do web quebra em vez de a
 * tela mostrar "undefined".
 *
 * Decimais chegam como string (Prisma serializa Decimal assim); datas como
 * string ISO. Os tipos refletem o que trafega, não o que está no banco.
 */

export interface RespostaLogin {
  tokenAcesso: string;
  tipoToken: string;
  expiraEm: string;
  usuario: { id: string; nome: string; email: string; cargoFuncional: string | null };
}

export interface UsuarioAutenticado {
  userId: string;
  tenantId: string;
  email: string;
  nome: string;
}

export interface ErroApi {
  codigo?: string;
  mensagem?: string;
  message?: string | string[];
  detalhe?: Record<string, unknown>;
  estadoAtual?: StatusRequisicao;
  estadosPermitidos?: StatusRequisicao[];
  versionAtual?: number;
}

export interface CentroCusto {
  id: string;
  codigo: string;
  nome: string;
  regionalId: string;
  ativo: boolean;
}

export interface ItemRequisicao {
  id: string;
  requisicaoId: string;
  varianteId: string;
  sequencia: number;
  quantidade: string;
  precoReferencia: string | null;
  status: StatusItemRequisicao;
  observacao: string | null;
}

export interface Requisicao {
  id: string;
  numero: string;
  centroCustoId: string;
  contratoId: string | null;
  solicitanteId: string;
  compradorId: string | null;
  status: StatusRequisicao;
  prioridade: Prioridade;
  justificativa: string | null;
  motivoRecusa: string | null;
  valorEstimado: string;
  dataNecessidade: string | null;
  orcamentoEstourado: boolean;
  orcamentoSnapshot: SnapshotOrcamento | null;
  estouroAutorizadoPor: string | null;
  submetidaEm: string | null;
  triagemEm: string | null;
  concluidaEm: string | null;
  slaPausadoEm: string | null;
  slaSegundosPausados: number;
  version: number;
  criadoEm: string;
}

export interface SnapshotOrcamento {
  exercicio: number;
  orcado: number;
  comprometido: number;
  realizado: number;
  saldo: number | null;
  excedente: number | null;
  estourado: boolean;
  motivo: 'SALDO_INSUFICIENTE' | 'SEM_ORCAMENTO' | null;
}

export interface RequisicaoDetalhada extends Requisicao {
  itens: ItemRequisicao[];
  transicoesDisponiveis: string[];
}

export interface MetricasSla {
  requisicaoId: string;
  numero: string;
  status: StatusRequisicao;
  regime: 'COMERCIAL' | 'VINTE_QUATRO_SETE';
  tto: { segundosUteis: number | null; segundosCorridos: number | null; emAndamento: boolean };
  ttr: { segundosUteis: number | null; segundosCorridos: number | null; emAndamento: boolean };
  slaSegundosPausados: number;
  pausadoAgora: boolean;
}

export interface EtapaAprovacao {
  id: string;
  instanciaId: string;
  nivel: number;
  aprovadorId: string | null;
  deleganteId: string | null;
  solicitanteId: string;
  compradorId: string | null;
  decisao: DecisaoEtapa;
  comentario: string | null;
  decididoEm: string | null;
}

export interface InstanciaAprovacao {
  id: string;
  requisicaoId: string;
  valorBase: string;
  nivelExigido: number;
  status: StatusInstancia;
  regraSnapshot: {
    centroCustoId: string;
    nivelFinal: number;
    nivelPorValor?: number;
    escalonadoPorEstouro?: boolean;
    orcamentoEstourado?: boolean;
    regra?: { nivel: number; papelExigido: string };
  };
  criadoEm: string;
  encerradoEm: string | null;
  etapas: EtapaAprovacao[];
}

export interface DecidirEtapaBody {
  decisao: 'APROVADO' | 'REJEITADO';
  comentario?: string;
  autorizarEstouro?: boolean;
}

// NotaProposta vem de ./equalizacao — é o mesmo tipo que o cálculo produz.
import type { NotaProposta } from './equalizacao';

export interface Proposta {
  id: string;
  cotacaoId: string;
  fornecedorId: string;
  valorItens: string;
  frete: string;
  desconto: string;
  valorTotal: string | null;
  condicaoPagamento: string;
  prazoEntregaDias: number;
  validadeProposta: string;
}

export interface Cotacao {
  id: string;
  numero: string;
  compradorId: string;
  status: StatusCotacao;
  dataLimiteResposta: string;
  criterioEqualizacao: { preco: number; lead_time: number; frete: number; cond_pagto: number };
  itens: { id: string; itemRequisicaoId: string; quantidadeConsolidada: string }[];
  convites: { id: string; fornecedorId: string; status: string }[];
  propostas: Proposta[];
  equalizacao: {
    id: string;
    propostaVencedoraId: string;
    propostaMenorPrecoId: string;
    justificativaDesvio: string | null;
    notas: { pesos: Record<string, number>; matriz: NotaProposta[] };
  } | null;
}

export interface Fornecedor {
  id: string;
  cnpj: string;
  razaoSocial: string;
  nomeFantasia: string | null;
  statusHomologacao: string;
}

export interface ItemPedido {
  id: string;
  pedidoId: string;
  itemRequisicaoId: string;
  varianteId: string;
  sequencia: number;
  qtdPedida: string;
  qtdRecebida: string;
  precoUnitario: string;
}

export interface Pedido {
  id: string;
  numero: string;
  requisicaoId: string;
  fornecedorId: string;
  centroCustoId: string;
  status: StatusPedido;
  valorTotal: string;
  condicaoPagamento: string;
  prazoEntrega: string;
  emitidoEm: string;
}

export interface PedidoDetalhado extends Pedido {
  itens: ItemPedido[];
  recebimentos: { id: string; tipo: 'TOTAL' | 'PARCIAL'; dataRecebimento: string }[];
}

export interface LinhaRecebimentoBody {
  itemPedidoId: string;
  qtdRecebida: number;
  qtdAvariada?: number;
  ocorrencia?: OcorrenciaRecebimento;
  descricaoOcorrencia?: string;
}

export interface RegistrarRecebimentoBody {
  itens: LinhaRecebimentoBody[];
  notaFiscal?: {
    chaveAcesso: string;
    numero: string;
    serie: string;
    valorTotal: number;
    dataEmissao: string;
    arquivoXmlUri?: string;
  };
  observacao?: string;
}

export interface Divergencia {
  codigo: string;
  mensagem: string;
  itemPedidoId?: string;
  detalhe: Record<string, unknown>;
}

export interface RespostaRecebimento {
  tipo: 'TOTAL' | 'PARCIAL';
  pedidoStatus: StatusPedido;
  conciliado: boolean;
  valorRecebido: number;
  valorNotaFiscal: number | null;
  divergencias: Divergencia[];
  eventosConta408: { conta: '408'; tipo: string; valor: number; descricao: string }[];
}

export interface LoteImportacao {
  id: string;
  tipo: TipoLoteImportacao;
  nomeArquivo: string;
  totalLinhas: number;
  linhasSucesso: number;
  linhasErro: number;
  status: string;
  iniciadoEm: string;
  concluidoEm: string | null;
}

export interface Feriado {
  id: string;
  data: string;
  descricao: string;
}

/** Chave da NF-e: 44 dígitos. A mesma regra do banco (ck_nfe_chave). */
export const REGEX_CHAVE_NFE = /^[0-9]{44}$/;
export const validarChaveNfe = (chave: string): boolean => REGEX_CHAVE_NFE.test(chave.replace(/\s/g, ''));
