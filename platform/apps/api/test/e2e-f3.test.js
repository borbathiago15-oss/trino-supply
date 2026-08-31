'use strict';

// E2E da Fase F3 contra Postgres real + app NestJS de verdade:
//   node test/e2e-f3.test.js
// Prova que as regras da política valem ponta a ponta (HTTP → transação →
// banco), que as constraints do DDL seguram o que escapar, que o lock
// pessimista serializa dois cliques simultâneos e que a decisão vira AuditLog
// com snapshot antes/depois.

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-f3';

require('reflect-metadata');
const argon2 = require('argon2');
const { randomUUID } = require('node:crypto');
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
const CNPJ = '77888999000100';
let base;
const tokens = {};
const ids = {};

async function req(caminho, { method = 'GET', body, como = 'admin', headers = {} } = {}) {
  const token = tokens[como];
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

const emDias = (dias) => {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + dias);
  return d;
};
const soDia = (d) => d.toISOString().slice(0, 10);

async function semear() {
  const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
  const tenant = await prisma.tenant.create({ data: { cnpj: CNPJ, razaoSocial: 'Grupo Trino LTDA' } });
  const db = forTenant(prisma, tenant.id);

  const papeis = ['admin', 'solicitante', 'comprador', 'gestor', 'gerente', 'diretor', 'suplente'];
  for (const papel of papeis) {
    const u = await db.usuario.create({ data: { nome: papel, email: `${papel}@trino.com`, senhaHash: hash } });
    ids[papel] = u.id;
  }

  const regional = await db.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
  const cc = await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-01', nome: 'Operação Recife' } });
  const ccOutro = await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-02', nome: 'Operação Natal' } });
  ids.cc = cc.id;
  ids.ccOutro = ccOutro.id;
  ids.tenant = tenant.id;

  for (const papel of papeis) {
    const login = await fetch(`${base}/auth/login`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ cnpj: CNPJ, email: `${papel}@trino.com`, senha: SENHA }),
    }).then((r) => r.json());
    tokens[papel] = login.tokenAcesso;
  }
}

/** Abre uma instância nova e devolve { instancia, etapas por nível }. */
async function abrirInstancia(valor, { comprador = true } = {}) {
  const r = await req('/aprovacoes/instancias', {
    method: 'POST',
    body: {
      requisicaoId: randomUUID(),
      centroCustoId: ids.cc,
      solicitanteId: ids.solicitante,
      ...(comprador ? { compradorId: ids.comprador } : {}),
      valorBase: valor,
    },
  });
  const porNivel = {};
  for (const e of r.body?.etapas ?? []) porNivel[e.nivel] = e.id;
  return { resposta: r, instancia: r.body, etapa: porNivel };
}

const decidir = (etapaId, como, corpo) =>
  req(`/aprovacoes/etapas/${etapaId}/decisao`, { method: 'POST', como, body: corpo });

async function main() {
  await limparBancoDeTestes(prisma);

  const app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    await semear();

    console.log('== regras de alçada');
    const faixas = [
      { nivel: 1, papelExigido: 'GESTOR', valorMin: 0, valorMax: 5000 },
      { nivel: 2, papelExigido: 'GERENTE', valorMin: 5000, valorMax: 25000 },
      { nivel: 3, papelExigido: 'DIRETOR', valorMin: 25000 },
    ];
    for (const f of faixas) {
      const r = await req('/aprovacoes/regras', { method: 'POST', body: { ...f, vigenciaInicio: soDia(emDias(-30)) } });
      check(`cria regra nível ${f.nivel}`, r.status === 201, JSON.stringify(r.body));
    }

    const sobreposta = await req('/aprovacoes/regras', {
      method: 'POST',
      body: { nivel: 2, papelExigido: 'GERENTE', valorMin: 1, valorMax: 2, vigenciaInicio: soDia(emDias(-1)) },
    });
    check('EXCLUDE gist barra segunda regra vigente no mesmo nível (ALC-ERR-004)',
      sobreposta.status === 409 && sobreposta.body?.codigo === 'ALC-ERR-004', JSON.stringify(sobreposta.body));

    const nivelInvalido = await req('/aprovacoes/regras', {
      method: 'POST',
      body: { nivel: 12, papelExigido: 'X', valorMin: 0, vigenciaInicio: soDia(emDias(0)) },
    });
    check('nível fora de 1..9 é 400', nivelInvalido.status === 400);

    const faixaInvertida = await req('/aprovacoes/regras', {
      method: 'POST',
      body: { nivel: 5, papelExigido: 'X', valorMin: 100, valorMax: 50, vigenciaInicio: soDia(emDias(0)) },
    });
    check('valorMax menor que valorMin é 400 (ALC-ERR-003)',
      faixaInvertida.status === 400 && faixaInvertida.body?.codigo === 'ALC-ERR-003');

    console.log('== cadeia de aprovadores e delegações');
    for (const [papel, nivel] of [['gestor', 1], ['gerente', 2], ['diretor', 3]]) {
      const r = await req('/aprovacoes/aprovadores', {
        method: 'POST',
        body: { centroCustoId: ids.cc, usuarioId: ids[papel], nivel, vigenciaInicio: soDia(emDias(-10)) },
      });
      check(`define ${papel} como aprovador nível ${nivel}`, r.status === 201, JSON.stringify(r.body));
    }
    check('aprovador em centro de custo de outro tenant/inexistente é 404',
      (await req('/aprovacoes/aprovadores', {
        method: 'POST',
        body: { centroCustoId: randomUUID(), usuarioId: ids.gestor, nivel: 1 },
      })).status === 404);

    const delegacaoIgual = await req('/aprovacoes/delegacoes', {
      method: 'POST',
      body: {
        deleganteId: ids.gerente, delegadoId: ids.gerente, motivo: 'Férias',
        vigenciaInicio: emDias(-1).toISOString(), vigenciaFim: emDias(10).toISOString(),
      },
    });
    check('delegar para si mesmo é 400 (ALC-ERR-006)',
      delegacaoIgual.status === 400 && delegacaoIgual.body?.codigo === 'ALC-ERR-006');

    console.log('== fronteiras de faixa via API (CT-01 a CT-04)');
    const abaixo = await abrirInstancia(4999.99);
    check('CT-01 R$ 4.999,99 abre instância de nível 1',
      abaixo.instancia.nivelExigido === 1 && abaixo.instancia.etapas.length === 1);
    const exato5k = await abrirInstancia(5000);
    check('CT-02 R$ 5.000,00 abre instância de nível 2 (2 etapas)',
      exato5k.instancia.nivelExigido === 2 && exato5k.instancia.etapas.length === 2);
    const abaixo25k = await abrirInstancia(24999.99);
    check('CT-03 R$ 24.999,99 continua no nível 2', abaixo25k.instancia.nivelExigido === 2);
    const exato25k = await abrirInstancia(25000);
    check('CT-04 R$ 25.000,00 abre instância de nível 3 (3 etapas)',
      exato25k.instancia.nivelExigido === 3 && exato25k.instancia.etapas.length === 3);
    check('a regra usada fica congelada no snapshot',
      exato25k.instancia.regraSnapshot?.regra?.nivel === 3 && exato25k.instancia.regraSnapshot?.centroCustoId === ids.cc);

    console.log('== bloqueios B1, B2, B4, B5 pela API');
    const alvo = exato5k;
    check('CT-05a solicitante é barrado (APV-B1)',
      (await decidir(alvo.etapa[1], 'solicitante', { decisao: 'APROVADO' })).body?.codigo === 'APV-B1');
    check('CT-05b comprador é barrado (APV-B2)',
      (await decidir(alvo.etapa[1], 'comprador', { decisao: 'APROVADO' })).body?.codigo === 'APV-B2');
    check('usuário sem alçada no nível é barrado (APV-B5)',
      (await decidir(alvo.etapa[1], 'diretor', { decisao: 'APROVADO' })).body?.codigo === 'APV-B5');
    check('não se pula a fila de níveis (APV-ERR-003)',
      (await decidir(alvo.etapa[2], 'gerente', { decisao: 'APROVADO' })).body?.codigo === 'APV-ERR-003');

    const nivel1 = await decidir(alvo.etapa[1], 'gestor', { decisao: 'APROVADO' });
    check('CT-06 gestor aprova o nível 1',
      nivel1.status === 201 && nivel1.body?.etapa?.decisao === 'APROVADO' && nivel1.body?.statusInstancia === 'PENDENTE',
      JSON.stringify(nivel1.body));

    check('etapa já decidida não é redecidida (APV-ERR-002)',
      (await decidir(alvo.etapa[1], 'gestor', { decisao: 'APROVADO' })).body?.codigo === 'APV-ERR-002');

    console.log('== CT-07: mesmo usuário em dois níveis (B4)');
    await req('/aprovacoes/aprovadores', {
      method: 'POST',
      body: { centroCustoId: ids.cc, usuarioId: ids.gestor, nivel: 2, vigenciaInicio: soDia(emDias(-10)) },
    });
    check('CT-07 quem aprovou o nível 1 não aprova o nível 2 (APV-B4)',
      (await decidir(alvo.etapa[2], 'gestor', { decisao: 'APROVADO' })).body?.codigo === 'APV-B4');

    const nivel2 = await decidir(alvo.etapa[2], 'gerente', { decisao: 'APROVADO' });
    check('gerente fecha o nível 2 e a instância é APROVADA',
      nivel2.status === 201 && nivel2.body?.statusInstancia === 'APROVADA', JSON.stringify(nivel2.body));
    check('encerrada_em preenchido',
      (await prisma.instanciaAprovacao.findUnique({ where: { id: alvo.instancia.id } })).encerradoEm !== null);
    check('instância encerrada não aceita nova decisão (APV-ERR-001)',
      (await decidir(alvo.etapa[2], 'gerente', { decisao: 'APROVADO' })).body?.codigo === 'APV-ERR-001');

    console.log('== CT-08: delegação ponta a ponta');
    const porDelegacao = await abrirInstancia(6000);
    await decidir(porDelegacao.etapa[1], 'gestor', { decisao: 'APROVADO' });
    check('sem delegação, o suplente é barrado (APV-B5)',
      (await decidir(porDelegacao.etapa[2], 'suplente', { decisao: 'APROVADO' })).body?.codigo === 'APV-B5');

    const delegacaoSolicitante = await req('/aprovacoes/delegacoes', {
      method: 'POST',
      body: {
        deleganteId: ids.solicitante, delegadoId: ids.suplente, centroCustoId: ids.cc, motivo: 'Tentativa de burla',
        vigenciaInicio: emDias(-1).toISOString(), vigenciaFim: emDias(10).toISOString(),
      },
    });
    check('delegação do solicitante é aceita no cadastro', delegacaoSolicitante.status === 201);
    check('mas não autoriza aprovar (APV-B3)',
      (await decidir(porDelegacao.etapa[2], 'suplente', { decisao: 'APROVADO' })).body?.codigo === 'APV-B3');

    const delegacaoVencida = await req('/aprovacoes/delegacoes', {
      method: 'POST',
      body: {
        deleganteId: ids.gerente, delegadoId: ids.suplente, centroCustoId: ids.cc, motivo: 'Férias passadas',
        vigenciaInicio: emDias(-20).toISOString(), vigenciaFim: emDias(-5).toISOString(),
      },
    });
    check('delegação vencida não autoriza (APV-B3)',
      delegacaoVencida.status === 201 &&
      (await decidir(porDelegacao.etapa[2], 'suplente', { decisao: 'APROVADO' })).body?.codigo === 'APV-B3');

    const delegacaoBoa = await req('/aprovacoes/delegacoes', {
      method: 'POST',
      body: {
        deleganteId: ids.gerente, delegadoId: ids.suplente, centroCustoId: ids.cc, motivo: 'Férias do gerente',
        vigenciaInicio: emDias(-1).toISOString(), vigenciaFim: emDias(10).toISOString(),
      },
    });
    const comDelegacao = await decidir(porDelegacao.etapa[2], 'suplente', { decisao: 'APROVADO' });
    check('CT-08 suplente aprova em nome do gerente',
      comDelegacao.status === 201 && comDelegacao.body?.viaDelegacao === true &&
      comDelegacao.body?.deleganteId === ids.gerente, JSON.stringify(comDelegacao.body));
    check('a etapa guarda quem delegou',
      (await prisma.etapaAprovacao.findUnique({ where: { id: porDelegacao.etapa[2] } })).deleganteId === ids.gerente);

    const encerrada = await req(`/aprovacoes/delegacoes/${delegacaoBoa.body.id}`, { method: 'PATCH', body: { ativa: false } });
    check('delegação pode ser encerrada', encerrada.status === 200 && encerrada.body.ativa === false);

    console.log('== rejeição');
    const paraRejeitar = await abrirInstancia(9000);
    const semComentario = await decidir(paraRejeitar.etapa[1], 'gestor', { decisao: 'REJEITADO' });
    check('rejeição sem comentário é 409 (APV-ERR-004)',
      semComentario.status === 409 && semComentario.body?.codigo === 'APV-ERR-004');
    const rejeitada = await decidir(paraRejeitar.etapa[1], 'gestor', { decisao: 'REJEITADO', comentario: 'Fora do orçamento' });
    check('rejeição no nível 1 encerra a instância inteira',
      rejeitada.status === 201 && rejeitada.body?.statusInstancia === 'REJEITADA');
    check('as etapas superiores continuam pendentes, mas a instância está fechada',
      (await prisma.etapaAprovacao.findUnique({ where: { id: paraRejeitar.etapa[2] } })).decisao === 'PENDENTE' &&
      (await decidir(paraRejeitar.etapa[2], 'gerente', { decisao: 'APROVADO' })).body?.codigo === 'APV-ERR-001');

    console.log('== concorrência: lock pessimista na instância');
    const disputada = await abrirInstancia(3000);
    // Dois cliques simultâneos na MESMA etapa: um decide, o outro encontra
    // a etapa já decidida — nunca duas decisões.
    const [a, b] = await Promise.all([
      decidir(disputada.etapa[1], 'gestor', { decisao: 'APROVADO' }),
      decidir(disputada.etapa[1], 'gestor', { decisao: 'APROVADO' }),
    ]);
    const status = [a.status, b.status].sort();
    check('só uma das duas decisões simultâneas passa',
      status[0] === 201 && status[1] === 409, JSON.stringify({ a: a.status, b: b.status, ca: a.body?.codigo, cb: b.body?.codigo }));
    check('a etapa tem exatamente uma decisão gravada',
      (await prisma.etapaAprovacao.findUnique({ where: { id: disputada.etapa[1] } })).version === 1);
    check('a instância não foi encerrada duas vezes',
      (await prisma.instanciaAprovacao.findUnique({ where: { id: disputada.instancia.id } })).version === 1);

    console.log('== unicidade de instância pendente por requisição');
    const requisicaoId = randomUUID();
    const primeira = await req('/aprovacoes/instancias', {
      method: 'POST',
      body: { requisicaoId, centroCustoId: ids.cc, solicitanteId: ids.solicitante, valorBase: 100 },
    });
    const segunda = await req('/aprovacoes/instancias', {
      method: 'POST',
      body: { requisicaoId, centroCustoId: ids.cc, solicitanteId: ids.solicitante, valorBase: 100 },
    });
    check('ux_instancia_pendente barra a segunda instância PENDENTE (APV-ERR-006)',
      primeira.status === 201 && segunda.status === 409 && segunda.body?.codigo === 'APV-ERR-006',
      JSON.stringify(segunda.body));

    console.log('== auditoria da decisão');
    const logs = await prisma.auditLog.findMany({ where: { entidade: 'etapa_aprovacao' }, orderBy: { timestampUtc: 'asc' } });
    check('decisões geram AuditLog', logs.length >= 4, `logs=${logs.length}`);
    const logAprovacao = logs.find((l) => l.acao === 'APROVAR_ETAPA');
    check('AuditLog guarda o antes PENDENTE e o depois APROVADO',
      logAprovacao?.beforeJson?.decisao === 'PENDENTE' && logAprovacao?.afterJson?.decisao === 'APROVADO',
      JSON.stringify({ b: logAprovacao?.beforeJson?.decisao, a: logAprovacao?.afterJson?.decisao }));
    check('AuditLog registra o ator e o tenant do token',
      !!logAprovacao?.actorId && logAprovacao?.tenantId === ids.tenant);
    check('o snapshot inclui o estado da instância',
      logAprovacao?.beforeJson?.instancia?.status === 'PENDENTE');
    const logRejeicao = logs.find((l) => l.acao === 'REJEITAR_ETAPA');
    check('rejeição é auditada com o comentário',
      logRejeicao?.afterJson?.comentario === 'Fora do orçamento' &&
      logRejeicao?.afterJson?.instancia?.status === 'REJEITADA');
    const logDelegado = logs.find((l) => l.afterJson?.viaDelegacao === true);
    check('aprovação por delegação é marcada na auditoria',
      !!logDelegado && logDelegado.afterJson.deleganteId === ids.gerente);
    check('nenhuma decisão bloqueada virou log',
      logs.every((l) => l.afterJson?.decisao !== 'PENDENTE'));

    console.log('== isolamento por tenant');
    check('etapa de outro tenant não existe para quem não é dele',
      (await req(`/aprovacoes/etapas/${randomUUID()}/decisao`, { method: 'POST', body: { decisao: 'APROVADO' } })).status === 404);
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main()
  .catch((e) => { console.error(e); process.exitCode = 1; })
  .finally(() => prisma.$disconnect());
