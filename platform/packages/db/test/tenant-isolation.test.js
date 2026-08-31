'use strict';

// Teste de isolamento multi-tenant (Fase F0) — roda contra o Postgres da migration:
//   node test/tenant-isolation.test.js
// Prova que a extensão injeta o tenantId em todas as operações e que registro
// de outro tenant se comporta como inexistente.

const { PrismaClient, forTenant, TenantScopeError, limparBancoDeTestes } = require('../src');

const base = new PrismaClient();
let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};

async function main() {
  // limpeza para reexecução idempotente (ordem das FKs mora no @trino/db)
  await limparBancoDeTestes(base);

  console.log('== bootstrap: dois tenants (client base, sem escopo)');
  const alfa = await base.tenant.create({ data: { cnpj: '11111111000191', razaoSocial: 'Grupo Alfa LTDA' } });
  const beta = await base.tenant.create({ data: { cnpj: '22222222000102', razaoSocial: 'Grupo Beta LTDA' } });
  const dbAlfa = forTenant(base, alfa.id);
  const dbBeta = forTenant(base, beta.id);

  console.log('== create injeta o tenantId do escopo');
  const uAlfa = await dbAlfa.usuario.create({ data: { nome: 'Ana Alfa', email: 'ana@alfa.com', senhaHash: 'x' } });
  const uBeta = await dbBeta.usuario.create({ data: { nome: 'Beto Beta', email: 'beto@beta.com', senhaHash: 'x' } });
  check('usuario criado com tenantId do escopo', uAlfa.tenantId === alfa.id && uBeta.tenantId === beta.id);

  let barrado = false;
  try { await dbAlfa.usuario.create({ data: { nome: 'Intruso', email: 'i@x.com', senhaHash: 'x', tenantId: beta.id } }); }
  catch (e) { barrado = e instanceof TenantScopeError && e.code === 'TENANT-ERR-003'; }
  check('payload apontando outro tenant é recusado (TENANT-ERR-003)', barrado);

  const rAlfa = await dbAlfa.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
  await dbBeta.regional.create({ data: { codigo: 'NE', nome: 'Nordeste do Beta' } });
  const ccAlfa = await dbAlfa.centroCusto.create({ data: { regionalId: rAlfa.id, codigo: 'CC-01', nome: 'Operação Alfa' } });

  console.log('== leituras ficam presas ao tenant');
  check('findMany só vê o próprio tenant',
    (await dbAlfa.regional.findMany()).length === 1 && (await dbBeta.regional.findMany()).length === 1);
  check('count isolado', await dbAlfa.usuario.count() === 1 && await base.usuario.count() === 2);
  check('where do chamador é preservado dentro do filtro',
    (await dbBeta.regional.findMany({ where: { codigo: 'NE' } }))[0].nome === 'Nordeste do Beta');
  check('findUnique de registro alheio devolve null',
    await dbBeta.usuario.findUnique({ where: { id: uAlfa.id } }) === null);
  check('groupBy isolado',
    (await dbAlfa.usuario.groupBy({ by: ['tenantId'], _count: true })).every(g => g.tenantId === alfa.id));

  console.log('== mutações por chave única validam a posse antes');
  let negado = false;
  try { await dbBeta.usuario.update({ where: { id: uAlfa.id }, data: { nome: 'Hackeado' } }); }
  catch (e) { negado = e instanceof TenantScopeError && e.code === 'TENANT-ERR-404'; }
  check('update de registro alheio falha como inexistente', negado);
  check('nome intacto após a tentativa',
    (await base.usuario.findUnique({ where: { id: uAlfa.id } })).nome === 'Ana Alfa');

  negado = false;
  try { await dbBeta.centroCusto.delete({ where: { id: ccAlfa.id } }); }
  catch (e) { negado = e instanceof TenantScopeError; }
  check('delete de registro alheio falha como inexistente', negado);

  await dbBeta.usuario.updateMany({ data: { cargoFuncional: 'invadido' } });
  check('updateMany sem where não vaza para o outro tenant',
    (await base.usuario.findUnique({ where: { id: uAlfa.id } })).cargoFuncional === null);

  await dbBeta.regional.deleteMany({});
  check('deleteMany apaga só do próprio tenant', await base.regional.count() === 1);

  console.log('== catálogo global e auditoria');
  const perm = await dbAlfa.permissao.create({ data: { codigo: 'ORCAMENTO_LER', descricao: 'Ler orçamentos' } });
  check('permissao (global, sem tenant) passa direto', !!perm.id);
  const log = await dbAlfa.auditLog.create({ data: { entidade: 'usuario', entityId: uAlfa.id, acao: 'CREATE' } });
  check('audit_log particionada recebe escrita com tenantId injetado',
    log.tenantId === alfa.id && typeof log.id === 'bigint');

  console.log('== CHECKs do DDL vivem no banco');
  let cnpjRuim = false;
  try { await base.tenant.create({ data: { cnpj: 'ABC', razaoSocial: 'Inválida' } }); }
  catch { cnpjRuim = true; }
  check('ck_tenant_cnpj rejeita CNPJ não numérico', cnpjRuim);

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main().finally(() => base.$disconnect());
