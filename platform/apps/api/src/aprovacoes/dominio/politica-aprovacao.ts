import { AlcadaError, soData } from './faixas-alcada';

/**
 * Política de aprovação — DOMÍNIO PURO: sem Prisma, sem Nest, sem relógio
 * global (o `agora` entra pelo contexto). Tudo o que ela precisa saber chega
 * como dado, então ela é testável sem banco e é a MESMA regra em qualquer
 * caminho de entrada (API, job, importação).
 *
 * Bloqueios:
 *  B1 — o solicitante não aprova a própria requisição.
 *  B2 — o comprador não aprova a requisição que ele mesmo conduz.
 *  B3 — delegação não vale se estiver inativa/fora de vigência, se for de
 *       outro centro de custo, ou se servir para burlar B1/B2 (delegante que é
 *       solicitante ou comprador). Delegante e delegado são sempre distintos.
 *  B4 — o mesmo usuário não decide dois níveis da mesma instância.
 *  B5 — quem decide precisa de alçada VIGENTE naquele nível e naquele centro
 *       de custo (própria, ou do delegante quando age por delegação).
 *
 * O banco repete B1–B4 em CHECKs e índices parciais. A política existe para
 * dar a resposta certa ANTES, com código e motivo; as constraints são a rede
 * de segurança para qualquer caminho que escape daqui.
 */

export interface EtapaAlvo {
  id: string;
  nivel: number;
  solicitanteId: string;
  compradorId: string | null;
  decisao: 'PENDENTE' | 'APROVADO' | 'REJEITADO';
}

export interface EtapaIrma {
  id: string;
  nivel: number;
  aprovadorId: string | null;
  decisao: 'PENDENTE' | 'APROVADO' | 'REJEITADO';
}

export interface AtribuicaoAprovador {
  usuarioId: string;
  centroCustoId: string;
  nivel: number;
  vigenciaInicio: Date;
  vigenciaFim: Date | null;
}

export interface DelegacaoVigente {
  id: string;
  deleganteId: string;
  delegadoId: string;
  /** NULL = vale para qualquer centro de custo do delegante. */
  centroCustoId: string | null;
  ativa: boolean;
  vigenciaInicio: Date;
  vigenciaFim: Date;
}

export interface ContextoAprovacao {
  agora: Date;
  /** Quem está clicando. */
  usuarioId: string;
  centroCustoId: string;
  instancia: { id: string; status: 'PENDENTE' | 'APROVADA' | 'REJEITADA' | 'INVALIDADA'; nivelExigido: number };
  etapa: EtapaAlvo;
  /** Todas as etapas da instância, inclusive a alvo. */
  etapasDaInstancia: EtapaIrma[];
  atribuicoes: AtribuicaoAprovador[];
  delegacoes: DelegacaoVigente[];
}

export interface AutorizacaoAprovacao {
  aprovadorId: string;
  /** Preenchido quando o usuário age por delegação — vai para a etapa e para a auditoria. */
  deleganteId: string | null;
  viaDelegacao: boolean;
  nivel: number;
}

function atribuicaoVigente(a: AtribuicaoAprovador, agora: Date): boolean {
  const dia = soData(agora);
  return soData(a.vigenciaInicio) <= dia && (a.vigenciaFim === null || dia <= soData(a.vigenciaFim));
}

function temAlcadaNoCentroCusto(
  usuarioId: string,
  nivel: number,
  centroCustoId: string,
  atribuicoes: AtribuicaoAprovador[],
  agora: Date,
): boolean {
  return atribuicoes.some(
    (a) =>
      a.usuarioId === usuarioId &&
      a.nivel === nivel &&
      a.centroCustoId === centroCustoId &&
      atribuicaoVigente(a, agora),
  );
}

function delegacaoVigente(d: DelegacaoVigente, centroCustoId: string, agora: Date): boolean {
  return (
    d.ativa &&
    d.vigenciaInicio <= agora &&
    agora <= d.vigenciaFim &&
    (d.centroCustoId === null || d.centroCustoId === centroCustoId)
  );
}

/**
 * Autoriza (ou recusa) a decisão do usuário sobre a etapa.
 * Lança AlcadaError com o código do bloqueio; devolve quem assina a etapa
 * quando tudo passa.
 */
export function assertPodeAprovar(ctx: ContextoAprovacao): AutorizacaoAprovacao {
  const { agora, usuarioId, centroCustoId, instancia, etapa, etapasDaInstancia, atribuicoes, delegacoes } = ctx;

  // ---- estado: só decide o que ainda está em aberto, e na ordem ----
  if (instancia.status !== 'PENDENTE') {
    throw new AlcadaError('APV-ERR-001', `Instância já encerrada (${instancia.status}).`, {
      instanciaId: instancia.id,
      status: instancia.status,
    });
  }
  if (etapa.decisao !== 'PENDENTE') {
    throw new AlcadaError('APV-ERR-002', `Etapa já decidida (${etapa.decisao}).`, { etapaId: etapa.id });
  }
  const pendenteAnterior = etapasDaInstancia
    .filter((e) => e.decisao === 'PENDENTE' && e.nivel < etapa.nivel)
    .sort((a, b) => a.nivel - b.nivel)[0];
  if (pendenteAnterior) {
    throw new AlcadaError('APV-ERR-003', `O nível ${pendenteAnterior.nivel} ainda não decidiu.`, {
      nivelPendente: pendenteAnterior.nivel,
      nivelSolicitado: etapa.nivel,
    });
  }

  // ---- B1: solicitante não aprova a própria requisição ----
  if (usuarioId === etapa.solicitanteId) {
    throw new AlcadaError('APV-B1', 'O solicitante não pode aprovar a própria requisição.', { etapaId: etapa.id });
  }

  // ---- B2: comprador não aprova o que ele conduz ----
  if (etapa.compradorId !== null && usuarioId === etapa.compradorId) {
    throw new AlcadaError('APV-B2', 'O comprador responsável não pode aprovar a requisição.', { etapaId: etapa.id });
  }

  // ---- B4: um usuário decide no máximo uma etapa por instância ----
  const jaDecidiu = etapasDaInstancia.find(
    (e) => e.id !== etapa.id && e.aprovadorId === usuarioId && e.decisao !== 'PENDENTE',
  );
  if (jaDecidiu) {
    throw new AlcadaError('APV-B4', `Este usuário já decidiu o nível ${jaDecidiu.nivel} desta instância.`, {
      nivelJaDecidido: jaDecidiu.nivel,
      nivelSolicitado: etapa.nivel,
    });
  }

  // ---- B5 (direto): alçada própria vigente no nível e no centro de custo ----
  if (temAlcadaNoCentroCusto(usuarioId, etapa.nivel, centroCustoId, atribuicoes, agora)) {
    return { aprovadorId: usuarioId, deleganteId: null, viaDelegacao: false, nivel: etapa.nivel };
  }

  // ---- B3: sem alçada própria, só entra por delegação válida ----
  const recebidas = delegacoes.filter((d) => d.delegadoId === usuarioId);
  if (recebidas.length === 0) {
    throw new AlcadaError(
      'APV-B5',
      `Usuário sem alçada vigente no nível ${etapa.nivel} para este centro de custo.`,
      { nivel: etapa.nivel, centroCustoId },
    );
  }

  const vigentes = recebidas.filter((d) => delegacaoVigente(d, centroCustoId, agora));
  if (vigentes.length === 0) {
    throw new AlcadaError('APV-B3', 'Delegação inativa, fora de vigência ou de outro centro de custo.', {
      centroCustoId,
      delegacoesAvaliadas: recebidas.length,
    });
  }

  // Delegação não pode ser porta dos fundos para B1/B2.
  const naoBurlaB1B2 = vigentes.filter(
    (d) =>
      d.deleganteId !== etapa.solicitanteId &&
      (etapa.compradorId === null || d.deleganteId !== etapa.compradorId) &&
      d.deleganteId !== usuarioId,
  );
  if (naoBurlaB1B2.length === 0) {
    throw new AlcadaError(
      'APV-B3',
      'Delegação inválida: o delegante é o solicitante ou o comprador da requisição.',
      { etapaId: etapa.id },
    );
  }

  // O delegante precisa ter, ele próprio, a alçada vigente no nível e no CC:
  // ninguém delega o que não tem.
  const util = naoBurlaB1B2.find((d) =>
    temAlcadaNoCentroCusto(d.deleganteId, etapa.nivel, centroCustoId, atribuicoes, agora),
  );
  if (!util) {
    throw new AlcadaError(
      'APV-B5',
      `Nenhum delegante tem alçada vigente no nível ${etapa.nivel} para este centro de custo.`,
      { nivel: etapa.nivel, centroCustoId },
    );
  }

  // B4 também vale para o delegante: se ele já decidiu outro nível, a
  // delegação não devolve um segundo voto à mesma pessoa.
  const deleganteJaDecidiu = etapasDaInstancia.find(
    (e) => e.id !== etapa.id && e.aprovadorId === util.deleganteId && e.decisao !== 'PENDENTE',
  );
  if (deleganteJaDecidiu) {
    throw new AlcadaError(
      'APV-B4',
      `O delegante já decidiu o nível ${deleganteJaDecidiu.nivel} desta instância.`,
      { nivelJaDecidido: deleganteJaDecidiu.nivel },
    );
  }

  return { aprovadorId: usuarioId, deleganteId: util.deleganteId, viaDelegacao: true, nivel: etapa.nivel };
}
