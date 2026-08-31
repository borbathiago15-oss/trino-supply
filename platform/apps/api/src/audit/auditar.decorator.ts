import { SetMetadata } from '@nestjs/common';

export const AUDITAR_METADATA = 'trino:auditar';

export interface AuditarConfig {
  /** Nome da entidade gravado em audit_log.entidade (ex.: 'usuario'). */
  entidade: string;
  /**
   * Delegate do Prisma usado para o snapshot "before" (ex.: 'usuario').
   * Default: o próprio nome da entidade.
   */
  delegate?: string;
}

/**
 * Marca um handler mutante como crítico: o AuditInterceptor global registra a
 * operação em auditoria.audit_log com snapshot antes/depois em JSON.
 */
export const Auditar = (entidade: string, delegate?: string) =>
  SetMetadata(AUDITAR_METADATA, { entidade, delegate: delegate ?? entidade } satisfies AuditarConfig);
