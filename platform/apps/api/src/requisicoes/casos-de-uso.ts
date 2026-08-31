import { ConflictException, Injectable } from '@nestjs/common';
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
   * Submeter valida três coisas: justificativa e itens (guardas da máquina) e
   * o saldo orçamentário do centro de custo — a regra R9.
   *
   * R9: saldo = valor_orcado - valor_comprometido - valor_realizado, no
   * exercício corrente. Sem linha de orçamento do exercício, não passa: é
   * melhor barrar do que gastar contra um teto que ninguém definiu.
   *
   * A submissão NÃO reserva orçamento (não incrementa valor_comprometido) —
   * comprometer e estornar pertencem ao pedido, com caminho de volta em
   * cancelamento e rejeição. Aqui é validação, como especificado.
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
      async (tx, estado, requisicao) => {
        await validarSaldoOrcamentario(tx, requisicao, estado.valorEstimado, agora);
      },
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
      async (tx, estado, requisicao) => {
        await validarSaldoOrcamentario(tx, requisicao, estado.valorEstimado, agora);
      },
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

/** R9 — saldo do exercício corrente cobre o valor estimado? */
async function validarSaldoOrcamentario(tx: any, requisicao: any, valorEstimado: number, agora: Date) {
  const exercicio = agora.getUTCFullYear();
  const orcamento = await tx.orcamentoCentroCusto.findFirst({
    where: { centroCustoId: requisicao.centroCustoId, exercicio },
  });

  if (!orcamento) {
    throw new ConflictException({
      codigo: 'REQ-ERR-008',
      mensagem: `Centro de custo sem orçamento definido para o exercício ${exercicio}.`,
      detalhe: { centroCustoId: requisicao.centroCustoId, exercicio },
    });
  }

  const saldo =
    Number(orcamento.valorOrcado) - Number(orcamento.valorComprometido) - Number(orcamento.valorRealizado);
  if (valorEstimado > saldo) {
    throw new ConflictException({
      codigo: 'REQ-ERR-009',
      mensagem: 'Saldo orçamentário insuficiente no centro de custo para o valor estimado.',
      detalhe: { exercicio, saldo, valorEstimado },
    });
  }
}
