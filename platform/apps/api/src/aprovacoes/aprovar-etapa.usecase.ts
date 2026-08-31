import { ConflictException, Injectable, NotFoundException } from '@nestjs/common';
import { ClientEscopado, Prisma } from '@trino/db';
import { naoEncontrado } from '../comum/erros';
import { AlcadaError } from './dominio/faixas-alcada';
import { assertPodeAprovar, ContextoAprovacao } from './dominio/politica-aprovacao';

export interface EntradaAprovarEtapa {
  db: ClientEscopado;
  tenantId: string;
  usuarioId: string;
  etapaId: string;
  decisao: 'APROVADO' | 'REJEITADO';
  comentario?: string | null;
  ip?: string | null;
  userAgent?: string | null;
  correlationId?: string | null;
}

/** Só os campos que interessam na trilha — nada de payload cru. */
function snapshotEtapa(etapa: any) {
  return {
    id: etapa.id,
    instanciaId: etapa.instanciaId,
    nivel: etapa.nivel,
    aprovadorId: etapa.aprovadorId,
    deleganteId: etapa.deleganteId,
    solicitanteId: etapa.solicitanteId,
    compradorId: etapa.compradorId,
    decisao: etapa.decisao,
    comentario: etapa.comentario,
    decididoEm: etapa.decididoEm,
    version: etapa.version,
    instancia: etapa.instancia
      ? {
          id: etapa.instancia.id,
          status: etapa.instancia.status,
          nivelExigido: etapa.instancia.nivelExigido,
          valorBase: String(etapa.instancia.valorBase),
          version: etapa.instancia.version,
          encerradoEm: etapa.instancia.encerradoEm,
        }
      : null,
  };
}

/**
 * Decidir uma etapa é a operação mais disputada do fluxo: dois aprovadores
 * podem clicar no mesmo segundo, e a instância inteira muda de estado junto.
 * Por isso tudo acontece dentro de UMA transação SERIALIZABLE que começa
 * travando a instância com SELECT ... FOR UPDATE — o segundo clique espera o
 * primeiro terminar e então enxerga o estado já decidido, em vez de decidir
 * sobre uma leitura velha.
 *
 * Ordem dentro da transação:
 *   1. lock pessimista da instância (FOR UPDATE);
 *   2. leitura da etapa e de todo o contexto (etapas irmãs, atribuições,
 *      delegações);
 *   3. política pura assertPodeAprovar (B1–B5);
 *   4. gravação da etapa e, se for o caso, encerramento da instância;
 *   5. AuditLog com snapshot antes/depois.
 */
@Injectable()
export class AprovarEtapaUseCase {
  async executar(entrada: EntradaAprovarEtapa) {
    const { db, tenantId, usuarioId, etapaId, decisao } = entrada;
    const comentario = entrada.comentario?.trim() || null;

    if (decisao === 'REJEITADO' && !comentario) {
      throw new ConflictException({ codigo: 'APV-ERR-004', mensagem: 'Rejeição exige comentário.' });
    }

    try {
      return await db.$transaction(
        async (tx: any) => {
          const etapa = await tx.etapaAprovacao.findUnique({
            where: { id: etapaId },
            include: { instancia: true },
          });
          if (!etapa) throw naoEncontrado('APV-ERR-404', 'Etapa de aprovação não encontrada neste tenant.');

          // Lock pessimista da instância. $queryRaw NÃO passa pela extensão de
          // tenant (regra da F0), então o tenant_id vai explícito aqui.
          const travadas: { id: string; status: string; version: number }[] = await tx.$queryRaw(
            Prisma.sql`SELECT id, status::text AS status, version
                         FROM compras.instancia_aprovacao
                        WHERE id = ${etapa.instanciaId}::uuid
                          AND tenant_id = ${tenantId}::uuid
                        FOR UPDATE`,
          );
          if (travadas.length === 0) {
            throw naoEncontrado('APV-ERR-404', 'Instância de aprovação não encontrada neste tenant.');
          }
          const instanciaTravada = travadas[0];

          // Estado relido DEPOIS do lock: é este que vale, não o do include.
          const [etapasDaInstancia, snapshotAntes] = await Promise.all([
            tx.etapaAprovacao.findMany({ where: { instanciaId: etapa.instanciaId } }),
            Promise.resolve(snapshotEtapa({ ...etapa, instancia: { ...etapa.instancia, ...instanciaTravada } })),
          ]);

          const centroCustoId = (etapa.instancia.regraSnapshot as any)?.centroCustoId;
          if (!centroCustoId) {
            throw new ConflictException({
              codigo: 'APV-ERR-005',
              mensagem: 'Instância sem centro de custo no snapshot da regra — reabra a aprovação.',
            });
          }

          const [atribuicoes, delegacoes] = await Promise.all([
            tx.aprovadorCentroCusto.findMany({ where: { centroCustoId, nivel: etapa.nivel } }),
            tx.delegacaoAlcada.findMany({ where: { delegadoId: usuarioId, ativa: true } }),
          ]);

          const agora = new Date();
          const contexto: ContextoAprovacao = {
            agora,
            usuarioId,
            centroCustoId,
            instancia: {
              id: etapa.instanciaId,
              status: instanciaTravada.status as ContextoAprovacao['instancia']['status'],
              nivelExigido: etapa.instancia.nivelExigido,
            },
            etapa: {
              id: etapa.id,
              nivel: etapa.nivel,
              solicitanteId: etapa.solicitanteId,
              compradorId: etapa.compradorId,
              decisao: etapa.decisao,
            },
            etapasDaInstancia: etapasDaInstancia.map((e: any) => ({
              id: e.id,
              nivel: e.nivel,
              aprovadorId: e.aprovadorId,
              decisao: e.decisao,
            })),
            atribuicoes,
            delegacoes,
          };

          const autorizacao = assertPodeAprovar(contexto);

          const etapaAtualizada = await tx.etapaAprovacao.update({
            where: { id: etapa.id },
            data: {
              decisao,
              aprovadorId: autorizacao.aprovadorId,
              deleganteId: autorizacao.deleganteId,
              comentario,
              decididoEm: agora,
              version: { increment: 1 },
            },
          });

          // Encerramento da instância: rejeição para tudo na hora; aprovação só
          // encerra quando o último nível exigido bateu o martelo.
          let instanciaFinal = null;
          const faltamNiveis = etapasDaInstancia.some(
            (e: any) => e.id !== etapa.id && e.decisao === 'PENDENTE' && e.nivel <= etapa.instancia.nivelExigido,
          );
          const novoStatus =
            decisao === 'REJEITADO'
              ? 'REJEITADA'
              : !faltamNiveis && etapa.nivel >= etapa.instancia.nivelExigido
                ? 'APROVADA'
                : null;

          if (novoStatus) {
            instanciaFinal = await tx.instanciaAprovacao.update({
              where: { id: etapa.instanciaId },
              data: { status: novoStatus, encerradoEm: agora, version: { increment: 1 } },
            });
          }

          const snapshotDepois = snapshotEtapa({
            ...etapaAtualizada,
            instancia: instanciaFinal ?? { ...etapa.instancia, ...instanciaTravada },
          });

          await tx.auditLog.create({
            data: {
              actorId: usuarioId,
              entidade: 'etapa_aprovacao',
              entityId: etapa.id,
              acao: decisao === 'APROVADO' ? 'APROVAR_ETAPA' : 'REJEITAR_ETAPA',
              beforeJson: snapshotAntes as any,
              afterJson: { ...snapshotDepois, viaDelegacao: autorizacao.viaDelegacao } as any,
              ip: entrada.ip ?? null,
              userAgent: entrada.userAgent ?? null,
              correlationId: entrada.correlationId ?? null,
            },
          });

          return {
            etapa: etapaAtualizada,
            instancia: instanciaFinal,
            statusInstancia: instanciaFinal?.status ?? 'PENDENTE',
            viaDelegacao: autorizacao.viaDelegacao,
            deleganteId: autorizacao.deleganteId,
          };
        },
        { isolationLevel: 'Serializable' },
      );
    } catch (e) {
      throw this.traduzir(e);
    }
  }

  /**
   * Bloqueio de política vira 409 com o código do bloqueio (o front precisa
   * distinguir B1 de B5). Conflito de serialização vira 409 para o cliente
   * repetir — não é erro do usuário.
   */
  private traduzir(e: unknown) {
    if (e instanceof AlcadaError) {
      const naoEncontrada = e.codigo === 'APV-ERR-404';
      const corpo = { codigo: e.codigo, mensagem: e.message, detalhe: e.detalhe };
      return naoEncontrada ? new NotFoundException(corpo) : new ConflictException(corpo);
    }
    const codigoPg = (e as { code?: string })?.code;
    if (codigoPg === 'P2034' || String((e as Error)?.message ?? '').includes('could not serialize')) {
      return new ConflictException({
        codigo: 'APV-ERR-409',
        mensagem: 'Outra decisão ocorreu ao mesmo tempo nesta aprovação. Recarregue e tente de novo.',
      });
    }
    return e;
  }
}
