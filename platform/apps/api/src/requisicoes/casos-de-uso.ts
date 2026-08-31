import { Injectable } from '@nestjs/common';
import { ClientEscopado } from '@trino/db';
import { calcularTtoSegundos } from './dominio/maquina-estados';
import { ContextoAutor, TransicaoRequisicaoService } from './transicao-requisicao.service';

/**
 * Comandos da esteira. Cada um é fino de propósito: a decisão de estado vive
 * na máquina pura e a mecânica (versão, auditoria, transação) vive no
 * TransicaoRequisicaoService. O que sobra aqui é só o que aquele passo tem de
 * particular.
 */

@Injectable()
export class SubmeterRequisicaoUseCase {
  constructor(private readonly transicao: TransicaoRequisicaoService) {}

  /**
   * Submeter exige justificativa e itens (guardas da máquina) e AVALIA o
   * orçamento do centro de custo — a regra R09.
   *
   * R09 não barra: estourar o orçamento é informação, não impedimento. A
   * requisição segue marcada (`orcamento_estourado`) e levando o retrato do
   * saldo (`orcamento_snapshot`), para os aprovadores analisarem e o aprovador
   * FINAL decidir se autoriza o estouro. Quem barra é quem tem alçada para
   * isso — e a recusa volta dizendo que o orçamento estourou, não um erro de
   * formulário.
   *
   * A submissão NÃO reserva orçamento (não incrementa valor_comprometido) —
   * comprometer e estornar pertencem ao pedido, com caminho de volta em
   * cancelamento e rejeição.
   */
  async executar(
    db: ClientEscopado,
    requisicaoId: string,
    entrada: { versionEsperada?: number; justificativa?: string | null },
    autor: ContextoAutor,
  ) {
    const agora = new Date();
    return this.transicao.executar(
      db,
      requisicaoId,
      {
        transicao: 'T03_SUBMETER',
        agora,
        versionEsperada: entrada.versionEsperada,
        autorId: autor.usuarioId,
        justificativa: entrada.justificativa ?? null,
      },
      autor,
      async (tx, estado, requisicao) => avaliarOrcamento(tx, requisicao, estado.valorEstimado, agora),
    );
  }
}

@Injectable()
export class ReenviarRequisicaoUseCase {
  constructor(private readonly transicao: TransicaoRequisicaoService) {}

  /** Reenviar depois do ajuste: mesmas guardas do submeter e destrava o SLA. */
  async executar(
    db: ClientEscopado,
    requisicaoId: string,
    entrada: { versionEsperada?: number; justificativa?: string | null },
    autor: ContextoAutor,
  ) {
    const agora = new Date();
    return this.transicao.executar(
      db,
      requisicaoId,
      {
        transicao: 'T08_REENVIAR',
        agora,
        versionEsperada: entrada.versionEsperada,
        autorId: autor.usuarioId,
        justificativa: entrada.justificativa ?? null,
      },
      autor,
      // Reenviar reavalia o orçamento: o ajuste pode ter mudado o valor, e o
      // que estourava pode não estourar mais (ou vice-versa).
      async (tx, estado, requisicao) => avaliarOrcamento(tx, requisicao, estado.valorEstimado, agora),
    );
  }
}

@Injectable()
export class AssumirTriagemUseCase {
  constructor(private readonly transicao: TransicaoRequisicaoService) {}

  /**
   * Quem assume vira o comprador responsável (a máquina barra o próprio
   * solicitante) e o TTO — tempo até o atendimento — para de contar aqui:
   * `triagem_em` é o seu fim, e o resultado em segundos volta na resposta e
   * fica no snapshot da auditoria.
   */
  async executar(
    db: ClientEscopado,
    requisicaoId: string,
    entrada: { versionEsperada?: number; compradorId?: string },
    autor: ContextoAutor,
  ) {
    const resultado = await this.transicao.executar(
      db,
      requisicaoId,
      {
        transicao: 'T05_ASSUMIR_TRIAGEM',
        agora: new Date(),
        versionEsperada: entrada.versionEsperada,
        autorId: autor.usuarioId,
        compradorId: entrada.compradorId ?? autor.usuarioId,
      },
      autor,
    );
    return { ...resultado, ttoSegundos: calcularTtoSegundos(resultado.requisicao) };
  }
}

@Injectable()
export class DevolverAjusteUseCase {
  constructor(private readonly transicao: TransicaoRequisicaoService) {}

  /**
   * Devolver para ajuste registra o motivo e CONGELA o cronômetro de SLA
   * (`sla_pausado_em`): enquanto a bola está com o solicitante, o relógio de
   * atendimento não corre contra o time de compras. O reenvio soma o tempo
   * parado em `sla_segundos_pausados` e destrava.
   */
  async executar(
    db: ClientEscopado,
    requisicaoId: string,
    entrada: { versionEsperada?: number; motivo: string },
    autor: ContextoAutor,
  ) {
    return this.transicao.executar(
      db,
      requisicaoId,
      {
        transicao: 'T06_DEVOLVER_AJUSTE',
        agora: new Date(),
        versionEsperada: entrada.versionEsperada,
        autorId: autor.usuarioId,
        motivo: entrada.motivo,
      },
      autor,
    );
  }
}

/**
 * R09 — avalia o orçamento do exercício corrente e devolve os campos de
 * marcação para entrarem no mesmo UPDATE da transição. NÃO lança: estouro é
 * informação que sobe a esteira.
 *
 * Dois motivos levam ao mesmo lugar:
 *  - SALDO_INSUFICIENTE: o valor estimado passa do que sobra;
 *  - SEM_ORCAMENTO: o centro de custo não tem linha do exercício, então não há
 *    teto contra o que comparar — segue marcado, para o aprovador final ver
 *    que está decidindo no escuro.
 *
 * Autorização de estouro anterior é limpa a cada submissão/reenvio: o que foi
 * autorizado valia para o valor de então.
 */
async function avaliarOrcamento(tx: any, requisicao: any, valorEstimado: number, agora: Date) {
  const exercicio = agora.getUTCFullYear();
  const orcamento = await tx.orcamentoCentroCusto.findFirst({
    where: { centroCustoId: requisicao.centroCustoId, exercicio },
  });

  const orcado = orcamento ? Number(orcamento.valorOrcado) : 0;
  const comprometido = orcamento ? Number(orcamento.valorComprometido) : 0;
  const realizado = orcamento ? Number(orcamento.valorRealizado) : 0;
  const saldo = orcado - comprometido - realizado;
  const estourado = !orcamento || valorEstimado > saldo;

  const snapshot = {
    exercicio,
    centroCustoId: requisicao.centroCustoId,
    avaliadoEm: agora.toISOString(),
    valorEstimado,
    orcado,
    comprometido,
    realizado,
    saldo: orcamento ? saldo : null,
    excedente: estourado && orcamento ? Number((valorEstimado - saldo).toFixed(2)) : null,
    estourado,
    motivo: !orcamento ? 'SEM_ORCAMENTO' : estourado ? 'SALDO_INSUFICIENTE' : null,
  };

  return {
    orcamentoEstourado: estourado,
    orcamentoSnapshot: snapshot,
    // A autorização acompanha o valor que foi autorizado: mudou o valor, cai.
    estouroAutorizadoPor: null,
    estouroAutorizadoEm: null,
  };
}
