'use strict';

const { Prisma } = require('@prisma/client');

/**
 * Isolamento multi-tenant (Fase F0): todo acesso a dados passa por um client
 * escopado que injeta o tenantId automaticamente — a aplicação nunca escreve
 * `where: { tenantId }` à mão e nunca consegue esquecer o filtro.
 *
 * Regras:
 *  - Modelos SEM campo tenantId (Tenant, Permissao, PapelPermissao) são o
 *    catálogo global/junções e ficam fora do filtro, por decisão de modelo.
 *  - Leituras e escritas em massa ganham `AND: [{ tenantId }, where]`.
 *  - `create*` injeta tenantId nos dados (e recusa dados apontando outro tenant).
 *  - Operações por chave única (update/delete/upsert/findUnique) validam a
 *    posse do registro ANTES de tocar nele; registro de outro tenant se
 *    comporta como inexistente (TENANT-ERR-404).
 *  - $queryRaw/$executeRaw NÃO passam por aqui: SQL cru é responsabilidade do
 *    chamador e deve filtrar tenant_id explicitamente.
 */

// Modelos sem tenant_id no DDL validado — descobertos do próprio schema.
const MODELOS_SEM_TENANT = new Set(
  Prisma.dmmf.datamodel.models
    .filter((m) => !m.fields.some((f) => f.name === 'tenantId'))
    .map((m) => m.name),
);

const OPS_FILTRO_WHERE = new Set([
  'findMany', 'findFirst', 'findFirstOrThrow',
  'count', 'aggregate', 'groupBy',
  'updateMany', 'updateManyAndReturn', 'deleteMany',
]);

const OPS_CHAVE_UNICA = new Set(['update', 'delete', 'upsert']);

const delegateName = (model) => model.charAt(0).toLowerCase() + model.slice(1);

class TenantScopeError extends Error {
  constructor(code, message) {
    super(message);
    this.name = 'TenantScopeError';
    this.code = code;
  }
}

/**
 * Devolve um Prisma Client estendido em que TODAS as consultas dos modelos com
 * tenant_id ficam presas ao tenant informado.
 *
 * @param {import('@prisma/client').PrismaClient} prisma client base (sem escopo)
 * @param {string} tenantId uuid do tenant da requisição
 */
function forTenant(prisma, tenantId) {
  if (!tenantId || typeof tenantId !== 'string') {
    throw new TenantScopeError('TENANT-ERR-001', 'tenantId é obrigatório para abrir um client escopado.');
  }

  return prisma.$extends({
    name: `tenant:${tenantId}`,
    query: {
      $allModels: {
        async $allOperations({ model, operation, args, query }) {
          if (MODELOS_SEM_TENANT.has(model)) return query(args);
          args = args ?? {};

          // criação: o tenant vem do escopo, nunca do chamador
          if (operation === 'create') {
            rejeitaOutroTenant(args.data, tenantId);
            args.data = { ...(args.data ?? {}), tenantId };
            return query(args);
          }
          if (operation === 'createMany' || operation === 'createManyAndReturn') {
            const linhas = Array.isArray(args.data) ? args.data : [args.data];
            linhas.forEach((d) => rejeitaOutroTenant(d, tenantId));
            args.data = linhas.map((d) => ({ ...d, tenantId }));
            return query(args);
          }

          // leituras/escritas em massa: filtro composto preservando o where original
          if (OPS_FILTRO_WHERE.has(operation)) {
            args.where = { AND: [{ tenantId }, args.where ?? {}] };
            return query(args);
          }

          // busca por chave única: executa e trata registro alheio como inexistente
          if (operation === 'findUnique' || operation === 'findUniqueOrThrow') {
            const resultado = await query(args);
            if (resultado && resultado.tenantId !== tenantId) {
              if (operation === 'findUniqueOrThrow') {
                throw new TenantScopeError('TENANT-ERR-404', `${model} não encontrado neste tenant.`);
              }
              return null;
            }
            return resultado;
          }

          // mutação por chave única: valida a posse ANTES de tocar o registro
          if (OPS_CHAVE_UNICA.has(operation)) {
            const atual = await prisma[delegateName(model)]
              .findUnique({ where: args.where, select: { tenantId: true } });
            if (operation === 'upsert') {
              if (atual && atual.tenantId !== tenantId) {
                throw new TenantScopeError('TENANT-ERR-404', `${model} não encontrado neste tenant.`);
              }
              rejeitaOutroTenant(args.create, tenantId);
              args.create = { ...(args.create ?? {}), tenantId };
              return query(args);
            }
            if (!atual || atual.tenantId !== tenantId) {
              throw new TenantScopeError('TENANT-ERR-404', `${model} não encontrado neste tenant.`);
            }
            return query(args);
          }

          // operação não mapeada em um modelo com tenant: falha fechado, nunca aberto
          throw new TenantScopeError('TENANT-ERR-002',
            `Operação "${operation}" em ${model} não é coberta pelo escopo de tenant.`);
        },
      },
    },
  });
}

function rejeitaOutroTenant(data, tenantId) {
  if (data && data.tenantId !== undefined && data.tenantId !== tenantId) {
    throw new TenantScopeError('TENANT-ERR-003',
      'Os dados apontam para outro tenant — o tenant vem do escopo da sessão, não do payload.');
  }
}

module.exports = { forTenant, TenantScopeError, MODELOS_SEM_TENANT };
