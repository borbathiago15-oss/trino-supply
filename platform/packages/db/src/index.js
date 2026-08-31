'use strict';

const { PrismaClient, Prisma } = require('@prisma/client');
const { forTenant, TenantScopeError, MODELOS_SEM_TENANT } = require('./tenant-extension');
const { limparBancoDeTestes } = require('./limpeza-testes');

/**
 * @trino/db — Fase F0 do monorepo.
 * Exponha SEMPRE `forTenant(prisma, tenantId)` para o código de aplicação;
 * o client base fica reservado a bootstrap (criar tenant), jobs administrativos
 * e ao catálogo global de permissões.
 */
module.exports = {
  PrismaClient,
  Prisma,
  forTenant,
  TenantScopeError,
  MODELOS_SEM_TENANT,
  // utilitário de teste — apaga tudo na ordem das FKs
  limparBancoDeTestes,
};
