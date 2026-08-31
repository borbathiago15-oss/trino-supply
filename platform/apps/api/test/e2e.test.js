'use strict';

// E2E da Fase F1 contra Postgres real + app NestJS de verdade:
//   npm --workspace @trino/api run test:e2e
// Prova: login real contra core.usuario (sem mock), rejeição de senha errada e
// de usuário inativo, tenant vindo SÓ do token (header forjado é ignorado),
// isolamento entre tenants e audit_log com before/after em JSON.

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-f1';

require('reflect-metadata');
const argon2 = require('argon2');
const { NestFactory } = require('@nestjs/core');
const { PrismaClient, forTenant } = require('@trino/db');
const { AppModule } = require('../dist/app.module');

const prisma = new PrismaClient();
let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};

const CNPJ_ALFA = '33444555000166';
const CNPJ_BETA = '44555666000177';
const SENHA_ALFA = 'SenhaForte#2026';

let base;

async function req(caminho, { method = 'GET', token, body, headers = {} } = {}) {
  const resposta = await fetch(`${base}${caminho}`, {
    method,
    headers: {
      'content-type': 'application/json',
      ...(token ? { authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const texto = await resposta.text();
  let json = null;
  try { json = texto ? JSON.parse(texto) : null; } catch { json = texto; }
  return { status: resposta.status, body: json };
}

async function limpar() {
  await prisma.auditLog.deleteMany({});
  await prisma.escopoAcesso.deleteMany({});
  await prisma.papelPermissao.deleteMany({});
  await prisma.papel.deleteMany({});
  await prisma.contratoOperacao.deleteMany({});
  await prisma.orcamentoCentroCusto.deleteMany({});
  await prisma.centroCusto.deleteMany({});
  await prisma.regional.deleteMany({});
  await prisma.usuario.deleteMany({});
  await prisma.permissao.deleteMany({});
  await prisma.tenant.deleteMany({});
}

async function semear() {
  const alfa = await prisma.tenant.create({ data: { cnpj: CNPJ_ALFA, razaoSocial: 'Grupo Alfa LTDA' } });
  const beta = await prisma.tenant.create({ data: { cnpj: CNPJ_BETA, razaoSocial: 'Grupo Beta LTDA' } });
  const hash = await argon2.hash(SENHA_ALFA, { type: argon2.argon2id });

  const dbAlfa = forTenant(prisma, alfa.id);
  const dbBeta = forTenant(prisma, beta.id);
  const adminAlfa = await dbAlfa.usuario.create({
    data: { nome: 'Ana Alfa', email: 'ana@alfa.com', senhaHash: hash, cargoFuncional: 'Administrador' },
  });
  await dbAlfa.usuario.create({
    data: { nome: 'Desligado', email: 'ex@alfa.com', senhaHash: hash, ativo: false },
  });
  const usuarioBeta = await dbBeta.usuario.create({
    data: { nome: 'Beto Beta', email: 'beto@beta.com', senhaHash: hash },
  });
  return { alfa, beta, adminAlfa, usuarioBeta };
}

async function main() {
  await limpar();
  const { alfa, beta, adminAlfa, usuarioBeta } = await semear();

  const app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    console.log('== login real contra core.usuario');
    const bom = await req('/auth/login', { method: 'POST', body: { cnpj: CNPJ_ALFA, email: 'ana@alfa.com', senha: SENHA_ALFA } });
    check('login com credenciais válidas devolve 201 + token', bom.status === 201 && !!bom.body?.tokenAcesso, `status=${bom.status}`);
    const token = bom.body?.tokenAcesso;

    const senhaErrada = await req('/auth/login', { method: 'POST', body: { cnpj: CNPJ_ALFA, email: 'ana@alfa.com', senha: 'errada' } });
    check('senha errada é 401 AUTH-ERR-001', senhaErrada.status === 401 && senhaErrada.body?.codigo === 'AUTH-ERR-001');

    const inexistente = await req('/auth/login', { method: 'POST', body: { cnpj: CNPJ_ALFA, email: 'ninguem@alfa.com', senha: SENHA_ALFA } });
    check('usuário inexistente é 401 (nada de admin assumido)', inexistente.status === 401);

    const inativo = await req('/auth/login', { method: 'POST', body: { cnpj: CNPJ_ALFA, email: 'ex@alfa.com', senha: SENHA_ALFA } });
    check('usuário inativo não entra', inativo.status === 401);

    const tenantErrado = await req('/auth/login', { method: 'POST', body: { cnpj: CNPJ_BETA, email: 'ana@alfa.com', senha: SENHA_ALFA } });
    check('email do tenant A não loga no tenant B', tenantErrado.status === 401);

    const semSenha = await req('/auth/login', { method: 'POST', body: { cnpj: CNPJ_ALFA, email: 'ana@alfa.com' } });
    check('payload incompleto é 400 AUTH-ERR-002', semSenha.status === 400 && semSenha.body?.codigo === 'AUTH-ERR-002');

    const registro = await prisma.usuario.findUnique({ where: { id: adminAlfa.id } });
    check('ultimo_login_em foi gravado no login', registro.ultimoLoginEm !== null);
    check('senha nunca volta na resposta', JSON.stringify(bom.body).includes('senhaHash') === false);

    console.log('== JwtAuthGuard protege as rotas');
    check('sem token é 401', (await req('/usuarios')).status === 401);
    check('token adulterado é 401', (await req('/usuarios', { token: `${token}x` })).status === 401);
    check('com token válido é 200', (await req('/usuarios', { token })).status === 200);

    console.log('== TenantGuard tira o tenant SÓ do token');
    const me = await req('/auth/me', { token, headers: { 'x-tenant-id': beta.id } });
    check('header x-tenant-id forjado é ignorado', me.status === 200 && me.body?.tenantId === alfa.id);

    const lista = await req('/usuarios', { token, headers: { 'x-tenant-id': beta.id } });
    const emails = (lista.body ?? []).map((u) => u.email);
    check('lista traz só usuários do tenant do token',
      emails.includes('ana@alfa.com') && !emails.includes('beto@beta.com'), JSON.stringify(emails));

    const alheio = await req(`/usuarios/${usuarioBeta.id}`, { method: 'PATCH', token, body: { nome: 'Invadido' } });
    check('editar usuário de outro tenant é 404', alheio.status === 404, `status=${alheio.status}`);
    check('registro do outro tenant ficou intacto',
      (await prisma.usuario.findUnique({ where: { id: usuarioBeta.id } })).nome === 'Beto Beta');

    console.log('== AuditInterceptor grava before/after no audit_log');
    await prisma.auditLog.deleteMany({});
    const criado = await req('/usuarios', {
      method: 'POST', token,
      body: { nome: 'Carlos Compras', email: 'carlos@alfa.com', senha: 'OutraSenha#2026', cargoFuncional: 'Comprador' },
    });
    check('POST /usuarios cria (201)', criado.status === 201 && !!criado.body?.id, `status=${criado.status}`);
    check('resposta do cadastro não expõe hash', !JSON.stringify(criado.body).includes('senhaHash'));

    const logCreate = await prisma.auditLog.findFirst({ where: { entityId: criado.body.id, acao: 'CREATE' } });
    check('CREATE auditado com tenant do token e ator',
      logCreate && logCreate.tenantId === alfa.id && logCreate.actorId === adminAlfa.id);
    check('CREATE tem after_json e before_json nulo',
      logCreate?.beforeJson === null && logCreate?.afterJson?.email === 'carlos@alfa.com');
    check('user_agent/ip/correlation registrados',
      !!logCreate?.correlationId && logCreate?.ip !== undefined);

    const editado = await req(`/usuarios/${criado.body.id}`, {
      method: 'PATCH', token, body: { cargoFuncional: 'Comprador Sênior' },
      headers: { 'x-correlation-id': '7f1a2b3c-4d5e-4f60-8a91-b2c3d4e5f607' },
    });
    check('PATCH /usuarios/:id atualiza (200)', editado.status === 200);
    const logUpdate = await prisma.auditLog.findFirst({ where: { entityId: criado.body.id, acao: 'UPDATE' } });
    check('UPDATE guarda o snapshot ANTES e DEPOIS',
      logUpdate?.beforeJson?.cargoFuncional === 'Comprador' && logUpdate?.afterJson?.cargoFuncional === 'Comprador Sênior',
      JSON.stringify({ b: logUpdate?.beforeJson?.cargoFuncional, a: logUpdate?.afterJson?.cargoFuncional }));
    check('snapshot de auditoria não carrega senha_hash',
      !JSON.stringify(logUpdate?.beforeJson ?? {}).includes('senhaHash'));
    check('correlation id do cliente é preservado',
      logUpdate?.correlationId === '7f1a2b3c-4d5e-4f60-8a91-b2c3d4e5f607');

    const removido = await req(`/usuarios/${criado.body.id}`, { method: 'DELETE', token });
    check('DELETE /usuarios/:id remove (200)', removido.status === 200);
    const logDelete = await prisma.auditLog.findFirst({ where: { entityId: criado.body.id, acao: 'DELETE' } });
    check('DELETE guarda o estado anterior e after nulo',
      logDelete?.beforeJson?.email === 'carlos@alfa.com' && logDelete?.afterJson === null);

    check('GET não gera registro de auditoria',
      (await prisma.auditLog.count({ where: { acao: 'READ' } })) === 0);
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main()
  .catch((e) => { console.error(e); process.exitCode = 1; })
  .finally(() => prisma.$disconnect());
