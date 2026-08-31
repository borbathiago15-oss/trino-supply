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
  /**
   * Nome da ação em audit_log.acao. Default: CREATE/UPDATE/DELETE pelo método
   * HTTP. Informe quando a operação tem nome próprio no domínio — a criação de
   * uma requisição é a transição `T01_CRIAR`, e a trilha fica legível se ela
   * aparecer assim, ao lado das demais transições, em vez de um CREATE solto.
   */
  acao?: string;
}

/**
 * Marca um handler mutante como crítico: o AuditInterceptor global registra a
 * operação em auditoria.audit_log com snapshot antes/depois em JSON.
 */
export const Auditar = (entidade: string, delegate?: string, acao?: string) =>
  SetMetadata(AUDITAR_METADATA, { entidade, delegate: delegate ?? entidade, acao } satisfies AuditarConfig);
