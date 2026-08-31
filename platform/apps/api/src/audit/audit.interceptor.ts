import { CallHandler, ExecutionContext, Injectable, NestInterceptor } from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import { randomUUID } from 'node:crypto';
import { lastValueFrom, Observable, of } from 'rxjs';
import { AUDITAR_METADATA, AuditarConfig } from './auditar.decorator';

const METODOS_MUTANTES: Record<string, string> = {
  POST: 'CREATE',
  PUT: 'UPDATE',
  PATCH: 'UPDATE',
  DELETE: 'DELETE',
};

// Nunca persistimos segredos no snapshot de auditoria.
const CAMPOS_SENSIVEIS = new Set(['senhaHash', 'mfaSecret', 'senha']);

/** Converte a entidade em JSON plano auditável (sem segredos, BigInt→string). */
function snapshotJson(valor: unknown): unknown {
  if (valor === null || valor === undefined) return null;
  return JSON.parse(
    JSON.stringify(valor, (chave, v) => {
      if (CAMPOS_SENSIVEIS.has(chave)) return undefined;
      if (typeof v === 'bigint') return v.toString();
      return v;
    }),
  );
}

/**
 * AuditInterceptor — registrado GLOBALMENTE (APP_INTERCEPTOR). Para todo
 * handler mutante (POST/PUT/PATCH/DELETE) marcado com @Auditar('entidade'):
 *   1. captura o snapshot "before" (busca por params.id via request.db);
 *   2. executa o handler;
 *   3. grava em auditoria.audit_log: actor, entidade, entityId, ação,
 *      before/after em JSONB, ip, user-agent e correlation id.
 * A escrita usa request.db (client escopado pelo TenantGuard), então o
 * tenant_id do log vem do token — nunca do payload. A gravação é aguardada
 * dentro do fluxo: se o log falhar, a requisição falha (auditoria de mutação
 * crítica não é melhor-esforço).
 */
@Injectable()
export class AuditInterceptor implements NestInterceptor {
  constructor(private readonly reflector: Reflector) {}

  async intercept(context: ExecutionContext, next: CallHandler): Promise<Observable<unknown>> {
    const config = this.reflector.get<AuditarConfig | undefined>(AUDITAR_METADATA, context.getHandler());
    const request = context.switchToHttp().getRequest();
    const acao = METODOS_MUTANTES[request.method as string];

    // Fora do contrato de auditoria: sem marcação, sem mutação ou sem escopo de tenant.
    if (!config || !acao || !request.db) return next.handle();

    const db = request.db;
    const entityIdParam: string | undefined = request.params?.id;

    let before: unknown = null;
    if (entityIdParam && acao !== 'CREATE') {
      before = await db[config.delegate ?? config.entidade].findUnique({ where: { id: entityIdParam } });
    }

    const resposta = await lastValueFrom(next.handle());

    const after = acao === 'DELETE' ? null : resposta;
    const idDaResposta = (resposta as { id?: string } | undefined)?.id;
    // Em rota aninhada (POST /fornecedores/:id/documentos) o :id é o pai, não o
    // recurso criado: numa criação, o id auditado é sempre o da entidade nova.
    const entityId = acao === 'CREATE' ? (idDaResposta ?? entityIdParam) : (entityIdParam ?? idDaResposta);
    if (entityId) {
      await db.auditLog.create({
        data: {
          actorId: request.user?.userId ?? null,
          entidade: config.entidade,
          entityId,
          acao,
          beforeJson: snapshotJson(before) ?? undefined,
          afterJson: snapshotJson(after) ?? undefined,
          ip: request.ip ?? null,
          userAgent: request.headers?.['user-agent'] ?? null,
          correlationId: this.correlationId(request),
        },
      });
    }

    return of(resposta);
  }

  private correlationId(request: { headers?: Record<string, unknown> }): string {
    const doHeader = request.headers?.['x-correlation-id'];
    if (typeof doHeader === 'string' && /^[0-9a-f-]{36}$/i.test(doHeader)) return doHeader;
    return randomUUID();
  }
}
