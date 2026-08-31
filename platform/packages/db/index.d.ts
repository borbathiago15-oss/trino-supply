// Tipos do @trino/db (Fase F1) — espelham src/index.js.
import { PrismaClient, Prisma } from '@prisma/client';

export { PrismaClient, Prisma };

export class TenantScopeError extends Error {
  code: string;
  constructor(code: string, message: string);
}

/**
 * Client escopado: a extensão injeta o tenantId em `create` e no `where` de
 * toda operação, então os argumentos deixam de exigir tenant_id — daí os
 * argumentos afrouxados. Os RETORNOS continuam os do Prisma.
 */
export type ClientEscopado = {
  [Modelo in keyof PrismaClient]: PrismaClient[Modelo] extends object
    ? {
        [Operacao in keyof PrismaClient[Modelo]]: PrismaClient[Modelo][Operacao] extends (
          ...args: any[]
        ) => infer Retorno
          ? (args?: any) => Retorno
          : PrismaClient[Modelo][Operacao];
      }
    : PrismaClient[Modelo];
};

/**
 * Devolve um client preso ao tenant informado: todas as consultas dos modelos
 * com tenant_id ganham o filtro automaticamente e registros de outro tenant se
 * comportam como inexistentes.
 */
export function forTenant(prisma: PrismaClient, tenantId: string): ClientEscopado;

export const MODELOS_SEM_TENANT: Set<string>;

/** Apaga todas as tabelas na ordem das FKs. Somente para testes. */
export function limparBancoDeTestes(prisma: PrismaClient): Promise<void>;
