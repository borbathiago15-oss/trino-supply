/**
 * Máquina de estados da requisição de compra — DOMÍNIO PURO (sem Prisma, sem
 * Nest, sem relógio global: o `agora` entra pelo comando).
 *
 * Determinística por construção: cada transição declara de quais estados sai,
 * para qual estado leva e quais guardas precisa. Não existe caminho implícito —
 * o que não está na tabela é `TransicaoNaoPermitidaException`, e é assim que
 * um estado novo (ou uma transição esquecida) aparece como erro em vez de
 * virar comportamento silencioso.
 */

export type StatusRequisicao =
  | 'RASCUNHO'
  | 'SUBMETIDA'
  | 'EM_TRIAGEM'
  | 'DEVOLVIDA_AJUSTE'
  | 'EM_COTACAO'
  | 'COTADA'
  | 'APROVACAO_ALCADA'
  | 'PEDIDO_GERADO'
  | 'RECEBIDA_PARCIAL'
  | 'RECEBIDA_TOTAL'
  | 'REJEITADA'
  | 'CANCELADA';

export type Transicao =
  | 'T01_CRIAR'
  | 'T02_EDITAR'
  | 'T03_SUBMETER'
  | 'T04_CANCELAR'
  | 'T05_ASSUMIR_TRIAGEM'
  | 'T06_DEVOLVER_AJUSTE'
  | 'T07_EDITAR_EM_AJUSTE'
  | 'T08_REENVIAR'
  | 'T09_REJEITAR'
  | 'T10_ENVIAR_COTACAO'
  | 'T11_ENCERRAR_COTACAO'
  | 'T12_ENVIAR_ALCADA'
  | 'T16_EMITIR_PEDIDO'
  | 'T17_RECEBER_PARCIAL'
  | 'T18_RECEBER_TOTAL';

export class TransicaoNaoPermitidaException extends Error {
  readonly codigo = 'REQ-ERR-TRANSICAO';
  readonly transicao: Transicao;
  readonly estadoAtual: StatusRequisicao;
  readonly estadosPermitidos: readonly StatusRequisicao[];

  constructor(transicao: Transicao, estadoAtual: StatusRequisicao, estadosPermitidos: readonly StatusRequisicao[]) {
    super(
      `Transição ${transicao} não é permitida a partir de ${estadoAtual} ` +
        `(esperado: ${estadosPermitidos.join(', ') || 'nenhum estado — só na criação'}).`,
    );
    this.name = 'TransicaoNaoPermitidaException';
    this.transicao = transicao;
    this.estadoAtual = estadoAtual;
    this.estadosPermitidos = estadosPermitidos;
  }
}

export class GuardaViolada extends Error {
  readonly codigo: string;
  readonly detalhe: Record<string, unknown>;

  constructor(codigo: string, mensagem: string, detalhe: Record<string, unknown> = {}) {
    super(mensagem);
    this.name = 'GuardaViolada';
    this.codigo = codigo;
    this.detalhe = detalhe;
  }
}

export class ConflitoDeVersao extends Error {
  readonly codigo = 'REQ-ERR-409';
  readonly versionAtual: number;
  readonly versionEnviada: number;

  constructor(versionAtual: number, versionEnviada: number) {
    super(
      `A requisição mudou desde a sua leitura (versão atual ${versionAtual}, enviada ${versionEnviada}). ` +
        'Recarregue antes de gravar.',
    );
    this.name = 'ConflitoDeVersao';
    this.versionAtual = versionAtual;
    this.versionEnviada = versionEnviada;
  }
}

/** Estado da requisição do ponto de vista do domínio. */
export interface EstadoRequisicao {
  id: string;
  status: StatusRequisicao;
  solicitanteId: string;
  compradorId: string | null;
  justificativa: string | null;
  motivoRecusa: string | null;
  valorEstimado: number;
  version: number;
  submetidaEm: Date | null;
  triagemEm: Date | null;
  concluidaEm: Date | null;
  slaPausadoEm: Date | null;
  slaSegundosPausados: number;
  /**
   * Quantidade de itens VIVOS (não cancelados) — a máquina não precisa da
   * lista inteira, só de saber se a requisição tem conteúdo.
   */
  totalItensAtivos: number;
}

export interface ComandoTransicao {
  transicao: Transicao;
  agora: Date;
  /** Versão que o chamador leu — bloqueio otimista. */
  versionEsperada?: number;
  /** Quem executa (usado nas guardas de papel). */
  autorId?: string;
  justificativa?: string | null;
  motivo?: string | null;
  compradorId?: string | null;
}

/** Campos que a transição altera — o caso de uso só persiste isto. */
export type PatchRequisicao = Partial<{
  status: StatusRequisicao;
  compradorId: string | null;
  justificativa: string | null;
  motivoRecusa: string | null;
  submetidaEm: Date | null;
  triagemEm: Date | null;
  concluidaEm: Date | null;
  slaPausadoEm: Date | null;
  slaSegundosPausados: number;
}>;

interface DefinicaoTransicao {
  rotulo: string;
  de: readonly StatusRequisicao[];
  para: StatusRequisicao | null;
}

/**
 * A tabela de transições. Ler esta constante é ler a esteira inteira.
 * `para: null` = o estado não muda (edições dentro do mesmo estado).
 */
export const TRANSICOES: Record<Transicao, DefinicaoTransicao> = {
  T01_CRIAR: { rotulo: 'Criar requisição', de: [], para: 'RASCUNHO' },
  T02_EDITAR: { rotulo: 'Editar rascunho', de: ['RASCUNHO'], para: null },
  T03_SUBMETER: { rotulo: 'Submeter', de: ['RASCUNHO'], para: 'SUBMETIDA' },
  T04_CANCELAR: {
    rotulo: 'Cancelar',
    de: ['RASCUNHO', 'SUBMETIDA', 'EM_TRIAGEM', 'DEVOLVIDA_AJUSTE'],
    para: 'CANCELADA',
  },
  T05_ASSUMIR_TRIAGEM: { rotulo: 'Assumir triagem', de: ['SUBMETIDA'], para: 'EM_TRIAGEM' },
  T06_DEVOLVER_AJUSTE: { rotulo: 'Devolver para ajuste', de: ['EM_TRIAGEM'], para: 'DEVOLVIDA_AJUSTE' },
  T07_EDITAR_EM_AJUSTE: { rotulo: 'Editar em ajuste', de: ['DEVOLVIDA_AJUSTE'], para: null },
  T08_REENVIAR: { rotulo: 'Reenviar após ajuste', de: ['DEVOLVIDA_AJUSTE'], para: 'SUBMETIDA' },
  T09_REJEITAR: { rotulo: 'Rejeitar', de: ['SUBMETIDA', 'EM_TRIAGEM', 'DEVOLVIDA_AJUSTE'], para: 'REJEITADA' },
  T10_ENVIAR_COTACAO: { rotulo: 'Enviar para cotação', de: ['EM_TRIAGEM'], para: 'EM_COTACAO' },
  // F5/F6 — o resto da esteira. T13 a T15 ficam reservados para os passos do
  // plano que ainda não chegaram; a numeração respeita T16 = emissão do pedido.
  T11_ENCERRAR_COTACAO: { rotulo: 'Encerrar cotação', de: ['EM_COTACAO'], para: 'COTADA' },
  T12_ENVIAR_ALCADA: { rotulo: 'Enviar para alçada', de: ['COTADA'], para: 'APROVACAO_ALCADA' },
  T16_EMITIR_PEDIDO: { rotulo: 'Emitir pedido de compra', de: ['APROVACAO_ALCADA'], para: 'PEDIDO_GERADO' },
  T17_RECEBER_PARCIAL: {
    rotulo: 'Registrar recebimento parcial',
    de: ['PEDIDO_GERADO', 'RECEBIDA_PARCIAL'],
    para: 'RECEBIDA_PARCIAL',
  },
  T18_RECEBER_TOTAL: {
    rotulo: 'Registrar recebimento total',
    de: ['PEDIDO_GERADO', 'RECEBIDA_PARCIAL'],
    para: 'RECEBIDA_TOTAL',
  },
};

/** Transições possíveis a partir de um estado — serve à UI e ao teste. */
export function transicoesDisponiveis(status: StatusRequisicao): Transicao[] {
  return (Object.keys(TRANSICOES) as Transicao[]).filter((t) => TRANSICOES[t].de.includes(status));
}

export function assertTransicaoPermitida(transicao: Transicao, status: StatusRequisicao): void {
  const definicao = TRANSICOES[transicao];
  if (!definicao) {
    throw new TransicaoNaoPermitidaException(transicao, status, []);
  }
  if (!definicao.de.includes(status)) {
    throw new TransicaoNaoPermitidaException(transicao, status, definicao.de);
  }
}

const textoUtil = (valor: string | null | undefined): string | null => {
  const limpo = (valor ?? '').trim();
  return limpo.length > 0 ? limpo : null;
};

/**
 * Aplica a transição sobre o estado e devolve o patch. Pura: não escreve em
 * lugar nenhum e não olha o relógio — quem chama decide o `agora` e persiste.
 *
 * Ordem das checagens: transição permitida → versão → guardas. Assim, uma
 * transição ilegal responde "ilegal" mesmo com versão velha, que é a
 * informação útil para quem chamou.
 */
export function aplicarTransicao(estado: EstadoRequisicao, comando: ComandoTransicao): PatchRequisicao {
  const { transicao, agora } = comando;
  assertTransicaoPermitida(transicao, estado.status);

  if (comando.versionEsperada !== undefined && comando.versionEsperada !== estado.version) {
    throw new ConflitoDeVersao(estado.version, comando.versionEsperada);
  }

  const definicao = TRANSICOES[transicao];
  const patch: PatchRequisicao = {};
  if (definicao.para !== null) patch.status = definicao.para;

  switch (transicao) {
    case 'T02_EDITAR':
    case 'T07_EDITAR_EM_AJUSTE': {
      // Edição não muda de estado; só encosta na justificativa quando veio.
      if (comando.justificativa !== undefined) patch.justificativa = textoUtil(comando.justificativa);
      break;
    }

    case 'T03_SUBMETER':
    case 'T08_REENVIAR': {
      const justificativa = textoUtil(comando.justificativa ?? estado.justificativa);
      if (!justificativa) {
        throw new GuardaViolada('REQ-ERR-001', 'Justificativa é obrigatória para submeter a requisição.');
      }
      if (estado.totalItensAtivos <= 0) {
        throw new GuardaViolada('REQ-ERR-002', 'A requisição precisa de ao menos um item ativo para ser submetida.');
      }
      patch.justificativa = justificativa;
      patch.submetidaEm = estado.submetidaEm ?? agora;
      // Reenviar destrava o cronômetro parado na devolução.
      if (transicao === 'T08_REENVIAR' && estado.slaPausadoEm) {
        patch.slaSegundosPausados =
          estado.slaSegundosPausados + Math.max(0, Math.round((agora.getTime() - estado.slaPausadoEm.getTime()) / 1000));
        patch.slaPausadoEm = null;
      }
      break;
    }

    case 'T05_ASSUMIR_TRIAGEM': {
      const compradorId = comando.compradorId ?? comando.autorId ?? null;
      if (!compradorId) {
        throw new GuardaViolada('REQ-ERR-003', 'Informe o comprador que assume a triagem.');
      }
      if (compradorId === estado.solicitanteId) {
        throw new GuardaViolada('REQ-ERR-004', 'Quem solicitou não pode assumir a triagem da própria requisição.');
      }
      patch.compradorId = compradorId;
      // Fim do TTO: o relógio do "tempo até o atendimento" para aqui.
      patch.triagemEm = agora;
      break;
    }

    case 'T06_DEVOLVER_AJUSTE': {
      const motivo = textoUtil(comando.motivo);
      if (!motivo) {
        throw new GuardaViolada('REQ-ERR-005', 'Devolver para ajuste exige o motivo da devolução.');
      }
      patch.motivoRecusa = motivo;
      // Congela o SLA: a bola está com o solicitante, o relógio não corre.
      patch.slaPausadoEm = estado.slaPausadoEm ?? agora;
      break;
    }

    case 'T09_REJEITAR': {
      const motivo = textoUtil(comando.motivo);
      if (!motivo) {
        throw new GuardaViolada('REQ-ERR-006', 'Rejeitar exige o motivo da recusa.');
      }
      patch.motivoRecusa = motivo;
      patch.concluidaEm = agora;
      break;
    }

    case 'T04_CANCELAR': {
      patch.concluidaEm = agora;
      if (comando.motivo !== undefined) {
        const motivo = textoUtil(comando.motivo);
        if (motivo) patch.motivoRecusa = motivo;
      }
      break;
    }

    case 'T10_ENVIAR_COTACAO': {
      if (!estado.compradorId) {
        throw new GuardaViolada('REQ-ERR-007', 'Só vai para cotação uma requisição com comprador responsável.');
      }
      if (estado.totalItensAtivos <= 0) {
        throw new GuardaViolada('REQ-ERR-002', 'A requisição precisa de ao menos um item ativo para ir a cotação.');
      }
      break;
    }

    case 'T11_ENCERRAR_COTACAO':
    case 'T12_ENVIAR_ALCADA':
      // Passos de trâmite: quem valida propostas e alçada são as fases F3/F5;
      // aqui a máquina só garante que a ordem dos estados foi respeitada.
      break;

    case 'T16_EMITIR_PEDIDO': {
      if (!estado.compradorId) {
        throw new GuardaViolada('REQ-ERR-012', 'Pedido de compra exige comprador responsável.');
      }
      if (estado.totalItensAtivos <= 0) {
        throw new GuardaViolada('REQ-ERR-002', 'Não se emite pedido de requisição sem itens ativos.');
      }
      break;
    }

    case 'T17_RECEBER_PARCIAL':
      break;

    case 'T18_RECEBER_TOTAL':
      patch.concluidaEm = agora;
      break;

    case 'T01_CRIAR':
      // A criação não parte de estado nenhum: assertTransicaoPermitida já barrou.
      break;
  }

  return patch;
}

/**
 * TTO — tempo até o atendimento, em segundos: da submissão até alguém assumir
 * a triagem, descontando o tempo em que o SLA ficou congelado.
 * `null` enquanto a requisição não foi submetida ou não foi assumida.
 */
export function calcularTtoSegundos(estado: {
  submetidaEm: Date | null;
  triagemEm: Date | null;
  slaSegundosPausados: number;
}): number | null {
  if (!estado.submetidaEm || !estado.triagemEm) return null;
  const bruto = Math.round((estado.triagemEm.getTime() - estado.submetidaEm.getTime()) / 1000);
  return Math.max(0, bruto - estado.slaSegundosPausados);
}
