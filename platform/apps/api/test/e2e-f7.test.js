'use strict';

// E2E da Fase F7 contra Postgres real:  node test/e2e-f7.test.js
// Prova o CRUD de feriados, o endpoint de métricas TTO/TTR em tempo útil e a
// pausa automática persistida pela esteira.

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-f7';

require('reflect-metadata');
const argon2 = require('argon2');
const { NestFactory } = require('@nestjs/core');
const { PrismaClient, forTenant, limparBancoDeTestes } = require('@trino/db');
const { AppModule } = require('../dist/app.module');

const prisma = new PrismaClient();
let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};

const SENHA = 'SenhaForte#2026';
const CNPJ = '10203040000155';
let base;
const tokens = {};
const ids = {};

async function req(caminho, { method = 'GET', body, como = 'solicitante' } = {}) {
  const r = await fetch(`${base}${caminho}`, {
    method,
    headers: { 'content-type': 'application/json', ...(tokens[como] ? { authorization: `Bearer ${tokens[como]}` } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const t = await r.text();
  let j = null; try { j = t ? JSON.parse(t) : null; } catch { j = t; }
  return { status: r.status, body: j };
}

async function main() {
  await limparBancoDeTestes(prisma);
  const app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
    const tenant = await prisma.tenant.create({ data: { cnpj: CNPJ, razaoSocial: 'Grupo Trino LTDA' } });
    ids.tenant = tenant.id;
    const db = forTenant(prisma, tenant.id);
    for (const papel of ['solicitante', 'comprador']) {
      ids[papel] = (await db.usuario.create({ data: { nome: papel, email: `${papel}@t.com`, senhaHash: hash } })).id;
      tokens[papel] = (await fetch(`${base}/auth/login`, {
        method: 'POST', headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ cnpj: CNPJ, email: `${papel}@t.com`, senha: SENHA }),
      }).then((r) => r.json())).tokenAcesso;
    }
    const regional = await db.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
    ids.cc = (await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-01', nome: 'Recife' } })).id;
    await db.orcamentoCentroCusto.create({
      data: { centroCustoId: ids.cc, exercicio: new Date().getUTCFullYear(), valorOrcado: 100000 },
    });
    const familia = await db.familiaProduto.create({ data: { codigo: 'GER', nome: 'Geral' } });
    const tipo = await db.tipoProduto.create({ data: { familiaId: familia.id, codigo: 'PAP', nome: 'Papel' } });
    const sku = await db.skuBase.create({ data: { tipoProdutoId: tipo.id, codigo: 'A4', descricao: 'Papel A4', unidadeMedida: 'CX' } });
    ids.variante = (await db.varianteSku.create({ data: { skuBaseId: sku.id, codigo: 'A4-75' } })).id;

    console.log('== calendário de feriados');
    const criado = await req('/feriados', { method: 'POST', body: { data: '2026-12-25', descricao: 'Natal' } });
    check('feriado cadastrado', criado.status === 201 && criado.body.descricao === 'Natal');
    check('feriado duplicado é 409 (SLA-ERR-001)',
      (await req('/feriados', { method: 'POST', body: { data: '2026-12-25', descricao: 'De novo' } })).body?.codigo === 'SLA-ERR-001');
    check('listagem devolve o calendário', (await req('/feriados')).body.length === 1);
    check('data inválida é 400', (await req('/feriados', { method: 'POST', body: { data: 'natal', descricao: 'x' } })).status === 400);

    console.log('== métricas de SLA pela esteira real');
    const criada = await req('/requisicoes', {
      method: 'POST', body: { centroCustoId: ids.cc, justificativa: 'Reposição' },
    });
    await req(`/requisicoes/${criada.body.id}/itens`, {
      method: 'POST', body: { varianteId: ids.variante, quantidade: 1, precoReferencia: 10 },
    });

    const antes = await req(`/requisicoes/${criada.body.id}/sla`);
    check('rascunho ainda não tem métrica (não entrou na fila)',
      antes.status === 200 && antes.body.tto.segundosUteis === null && antes.body.regime === 'COMERCIAL');

    await req(`/requisicoes/${criada.body.id}/submeter`, { method: 'POST', body: {} });
    const submetida = await req(`/requisicoes/${criada.body.id}/sla`);
    check('depois de submeter, o TTO está em andamento',
      submetida.body.tto.emAndamento === true && submetida.body.pausadoAgora === false);

    await req(`/requisicoes/${criada.body.id}/assumir-triagem`, { method: 'POST', como: 'comprador', body: {} });
    const emTriagem = await req(`/requisicoes/${criada.body.id}/sla`);
    check('assumir a triagem fecha o TTO e CONGELA o cronômetro (F7)',
      emTriagem.body.tto.emAndamento === false && emTriagem.body.pausadoAgora === true,
      JSON.stringify(emTriagem.body));
    check('o congelamento está persistido em sla_pausado_em',
      (await prisma.requisicaoCompra.findUnique({ where: { id: criada.body.id } })).slaPausadoEm !== null);

    await req(`/requisicoes/${criada.body.id}/devolver`, {
      method: 'POST', como: 'comprador', body: { motivo: 'Detalhar a justificativa' },
    });
    check('devolvida continua congelada',
      (await req(`/requisicoes/${criada.body.id}/sla`)).body.pausadoAgora === true);

    await req(`/requisicoes/${criada.body.id}/reenviar`, { method: 'POST', body: {} });
    const reenviada = await req(`/requisicoes/${criada.body.id}/sla`);
    check('reenviar destrava e o tempo parado fica contabilizado',
      reenviada.body.pausadoAgora === false && typeof reenviada.body.slaSegundosPausados === 'number');

    await req(`/requisicoes/${criada.body.id}/rejeitar`, {
      method: 'POST', como: 'comprador', body: { motivo: 'Sem necessidade comprovada' },
    });
    const final = await req(`/requisicoes/${criada.body.id}/sla`);
    check('TTR fecha na conclusão', final.body.ttr.emAndamento === false && final.body.ttr.segundosUteis !== null);
    check('as métricas saem nos dois relógios (útil e corrido)',
      typeof final.body.ttr.segundosCorridos === 'number' && final.body.ttr.segundosUteis <= final.body.ttr.segundosCorridos + 1);
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main().catch((e) => { console.error(e); process.exitCode = 1; }).finally(() => prisma.$disconnect());
