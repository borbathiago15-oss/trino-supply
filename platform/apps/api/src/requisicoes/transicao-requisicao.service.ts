import { ConflictException, Injectable, NotFoundException } from '@nestjs/common';
import { ClientEscopado } from '@trino/db';
import {
  aplicarTransicao,
  calcularTtoSegundos,
  ComandoTransicao,
  ConflitoDeVersao,
  EstadoRequisicao,
  GuardaViolada,
  PatchRequisicao,
  TransicaoNaoPermitidaException,
} from './dominio/maquina-estados';

export interface ContextoAutor {
  usuarioId: string;
  ip?: string | null;
  userAgent?: string | null;
  correlationId?: string | null;
}

/** Só o que interessa na trilha — nada de payload cru. */
function snapshot(req: any, totalItensAtivos: number) {
  return {
    id: req.id,
    numero: req.numero,
    status: req.status,
    prioridade: req.prioridade,
    centroCustoId: req.centroCustoId,
    solicitanteId: req.solicitanteId,
    compradorId: req.compradorId,
    justificativa: req.justificativa,
    motivoRecusa: req.motivoRecusa,
    valorEstimado: String(req.valorEstimado),
    submetidaEm: req.submetidaEm,
    triagemEm: req.triagemEm,
    concluidaEm: req.concluidaEm,
    slaPausadoEm: req.slaPausadoEm,
    slaSegundosPausados: req.slaSegundosPausados,
    version: req.version,
    totalItensAtivos,
  };
}

/**
 * Executor único de transições. Toda mudança de estado da requisição passa por
 * aqui, e por isso três coisas valem sempre, sem depender de quem chamou:
 *
 *  1. a decisão é da máquina de estados PURA (guardas incluídas);
 *  2. a gravação usa BLOQUEIO OTIMISTA pela coluna `version` — o UPDATE só
 *     acerta a linha se a versão ainda for a que foi lida; zero linhas
 *     afetadas significa que alguém gravou no meio do caminho, e a resposta é
 *     409 em vez de sobrescrever;
 *  3. cada transição vira um AuditLog com snapshot antes/depois, escrito na
 *     mesma transação da mudança — auditar depois do commit deixaria brecha.
 */
@Injectable()
export class TransicaoRequisicaoService {
  async executar(
    db: ClientEscopado,
    requisicaoId: string,
    comando: ComandoTransicao,
    autor: ContextoAutor,
    /** Validações que precisam de banco (ex.: saldo orçamentário R9). */
    validacaoExtra?: (tx: any, estado: EstadoRequisicao, requisicao: any) => Promise<void>,
  ) {
    try {
      return await db.$transaction(async (tx: any) => {
        const requisicao = await tx.requisicaoCompra.findUnique({ where: { id: requisicaoId } });
        if (!requisicao) {
          throw new NotFoundException({ codigo: 'REQ-ERR-404', mensagem: 'Requisição não encontrada neste tenant.' });
        }

        const totalItensAtivos = await tx.itemRequisicao.count({
          where: { requisicaoId, status: 'ATIVO' },
        });

        const estado: EstadoRequisicao = {
          id: requisicao.id,
          status: requisicao.status,
          solicitanteId: requisicao.solicitanteId,
          compradorId: requisicao.compradorId,
          justificativa: requisicao.justificativa,
          motivoRecusa: requisicao.motivoRecusa,
          valorEstimado: Number(requisicao.valorEstimado),
          version: requisicao.version,
          submetidaEm: requisicao.submetidaEm,
          triagemEm: requisicao.triagemEm,
          concluidaEm: requisicao.concluidaEm,
          slaPausadoEm: requisicao.slaPausadoEm,
          slaSegundosPausados: requisicao.slaSegundosPausados,
          totalItensAtivos,
        };

        // A máquina decide primeiro: nem consulta saldo quem nem podia transicionar.
        const patch: PatchRequisicao = aplicarTransicao(estado, comando);
        if (validacaoExtra) await validacaoExtra(tx, estado, requisicao);

        const antes = snapshot(requisicao, totalItensAtivos);

        // Bloqueio otimista: a versão lida é parte do WHERE.
        const { count } = await tx.requisicaoCompra.updateMany({
          where: { id: requisicaoId, version: requisicao.version },
          data: { ...patch, version: { increment: 1 } },
        });
        if (count === 0) {
          throw new ConflitoDeVersao(requisicao.version, requisicao.version);
        }

        const atualizada = await tx.requisicaoCompra.findUnique({ where: { id: requisicaoId } });
        const depois = snapshot(atualizada, totalItensAtivos);

        await tx.auditLog.create({
          data: {
            actorId: autor.usuarioId,
            entidade: 'requisicao_compra',
            entityId: requisicaoId,
            acao: comando.transicao,
            beforeJson: antes as any,
            afterJson: depois as any,
            ip: autor.ip ?? null,
            userAgent: autor.userAgent ?? null,
            correlationId: autor.correlationId ?? null,
          },
        });

        return {
          requisicao: atualizada,
          transicao: comando.transicao,
          statusAnterior: antes.status,
          ttoSegundos: calcularTtoSegundos(atualizada),
        };
      });
    } catch (e) {
      throw this.traduzir(e);
    }
  }

  /**
   * Transição ilegal e guarda violada são coisas diferentes para quem chamou:
   * a primeira diz "não dá a partir daqui", a segunda diz "falta isto".
   */
  traduzir(e: unknown) {
    if (e instanceof TransicaoNaoPermitidaException) {
      return new ConflictException({
        codigo: e.codigo,
        mensagem: e.message,
        transicao: e.transicao,
        estadoAtual: e.estadoAtual,
        estadosPermitidos: e.estadosPermitidos,
      });
    }
    if (e instanceof GuardaViolada) {
      return new ConflictException({ codigo: e.codigo, mensagem: e.message, detalhe: e.detalhe });
    }
    if (e instanceof ConflitoDeVersao) {
      return new ConflictException({
        codigo: e.codigo,
        mensagem: e.message,
        versionAtual: e.versionAtual,
        versionEnviada: e.versionEnviada,
      });
    }
    return e;
  }
}
