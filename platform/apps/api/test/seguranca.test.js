'use strict';

// Testes de SEGURANÇA e CONCORRÊNCIA (hardening) contra Postgres real:
//   node test/seguranca.test.js
//
// Três perguntas que só o teste responde:
//  1. um usuário do tenant A consegue ver ou tocar QUALQUER coisa do tenant B?
//  2. duas aprovações simultâneas da mesma etapa gravam duas vezes?
//  3. 50 baixas concorrentes do mesmo SKU deixam o saldo certo?

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-seg';

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
let base;
const tk = {};
const A = {};
const B = {};

async function req(caminho, { method = 'GET', body, token } = {}) {
  const r = await fetch(`${base}${caminho}`, {
    method,
    headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const t = await r.text();
  let j = null; try { j = t ? JSON.parse(t) : null; } catch { j = t; }
  return { status: r.status, body: j };
}

const emDias = (d) => { const x = new Date(); x.setUTCDate(x.getUTCDate() + d); return x; };
const soDia = (d) => d.toISOString().slice(0, 10);

/** Monta um tenant completo e devolve os ids + token do comprador. */
async function montarTenant(cnpj, sufixo, alvo, cnpjFornecedor) {
  const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
  const tenant = await prisma.tenant.create({ data: { cnpj, razaoSocial: `Grupo ${sufixo}` } });
  alvo.tenantId = tenant.id;
  const db = forTenant(prisma, tenant.id);

  for (const papel of ['comprador', 'solicitante', 'gestor', 'diretor']) {
    alvo[papel] = (await db.usuario.create({
      data: { nome: `${papel} ${sufixo}`, email: `${papel}.${sufixo}@trino.com`, senhaHash: hash },
    })).id;
  }

  const regional = await db.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
  alvo.cc = (await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-01', nome: `Op ${sufixo}` } })).id;
  await db.orcamentoCentroCusto.create({
    data: { centroCustoId: alvo.cc, exercicio: new Date().getUTCFullYear(), valorOrcado: 1000000 },
  });

  const familia = await db.familiaProduto.create({ data: { codigo: 'GER', nome: 'Geral' } });
  const tipo = await db.tipoProduto.create({ data: { familiaId: familia.id, codigo: 'PAP', nome: 'Papel' } });
  const sku = await db.skuBase.create({
    data: { tipoProdutoId: tipo.id, codigo: 'A4', descricao: 'Papel A4', unidadeMedida: 'CX' },
  });
  alvo.variante = (await db.varianteSku.create({ data: { skuBaseId: sku.id, codigo: 'A4-75' } })).id;
  alvo.fornecedor = (await db.fornecedor.create({
    data: { cnpj: cnpjFornecedor, razaoSocial: `Fornecedor ${sufixo}`, statusHomologacao: 'HOMOLOGADO' },
  })).id;

  const login = await fetch(`${base}/auth/login`, {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ cnpj, email: `comprador.${sufixo}@trino.com`, senha: SENHA }),
  }).then((r) => r.json());
  tk[sufixo] = login.tokenAcesso;
  return db;
}

async function main() {
  await limparBancoDeTestes(prisma);
  const app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    const dbA = await montarTenant('11111111000191', 'alfa', A, '33333333000114');
    const dbB = await montarTenant('22222222000102', 'beta', B, '44444444000125');

    // Dados do tenant B que o A vai tentar alcançar.
    const reqB = await req('/requisicoes', {
      token: tk.beta, method: 'POST',
      body: { centroCustoId: B.cc, justificativa: 'Requisição privada do Beta' },
    });
    B.requisicao = reqB.body.id;
    const itemB = await req(`/requisicoes/${B.requisicao}/itens`, {
      token: tk.beta, method: 'POST',
      body: { varianteId: B.variante, quantidade: 10, precoReferencia: 100 },
    });
    B.item = itemB.body.itens[0].id;
    await req(`/requisicoes/${B.requisicao}/submeter`, { token: tk.beta, method: 'POST', body: {} });

    const loteB = await req('/importacoes', {
      token: tk.beta, method: 'POST',
      body: { tipo: 'FORNECEDOR', nomeArquivo: 'b.csv', conteudo: 'cnpj;razao_social\n99999999000199;Só do Beta' },
    });
    B.lote = loteB.body.id;

    console.log('== 1. vazamento entre tenants: A tentando alcançar o B');
    // Toda rota que aceita id, com o id do OUTRO tenant.
    const tentativas = [
      ['GET', `/requisicoes/${B.requisicao}`],
      ['GET', `/requisicoes/${B.requisicao}/sla`],
      ['POST', `/requisicoes/${B.requisicao}/submeter`, {}],
      ['POST', `/requisicoes/${B.requisicao}/assumir-triagem`, {}],
      ['POST', `/requisicoes/${B.requisicao}/devolver`, { motivo: 'invasao' }],
      ['POST', `/requisicoes/${B.requisicao}/rejeitar`, { motivo: 'invasao' }],
      ['POST', `/requisicoes/${B.requisicao}/cancelar`, {}],
      ['PATCH', `/requisicoes/${B.requisicao}`, { justificativa: 'invadido' }],
      ['POST', `/requisicoes/${B.requisicao}/itens`, { varianteId: A.variante, quantidade: 1 }],
      ['DELETE', `/requisicoes/${B.requisicao}/itens/${B.item}`],
      ['GET', `/importacoes/${B.lote}`],
      ['GET', `/importacoes/${B.lote}/inconformidades`],
      ['GET', `/pedidos/${randomUUID()}`],
      ['GET', `/cotacoes/${randomUUID()}`],
      ['GET', `/aprovacoes/instancias/${randomUUID()}`],
    ];

    let vazamentos = 0;
    for (const [method, caminho, body] of tentativas) {
      const r = await req(caminho, { token: tk.alfa, method, body });
      const negado = [403, 404, 409].includes(r.status);
      if (!negado) {
        vazamentos += 1;
        console.log(`       ⚠ ${method} ${caminho} devolveu ${r.status}`);
      }
    }
    check(`as ${tentativas.length} rotas com id de outro tenant negam acesso`, vazamentos === 0, `${vazamentos} vazaram`);

    // Listagens: nunca podem trazer linha do vizinho.
    const listas = [
      ['/requisicoes', (l) => l.every((x) => x.centroCustoId === A.cc)],
      ['/pedidos', (l) => l.length === 0],
      ['/cotacoes', (l) => l.length === 0],
      ['/importacoes', (l) => l.length === 0],
      ['/fornecedores', (l) => l.every((x) => x.id === A.fornecedor)],
      ['/catalogo/centros-custo', (l) => l.every((x) => x.id === A.cc)],
      ['/aprovacoes/pendentes', (l) => l.length === 0],
      ['/auditoria', (l) => Array.isArray(l)],
      ['/feriados', (l) => Array.isArray(l)],
    ];
    let listasVazadas = 0;
    for (const [caminho, valida] of listas) {
      const r = await req(caminho, { token: tk.alfa });
      if (r.status !== 200 || !valida(r.body)) {
        listasVazadas += 1;
        console.log(`       ⚠ ${caminho} status ${r.status} tamanho ${Array.isArray(r.body) ? r.body.length : '?'}`);
      }
    }
    check(`as ${listas.length} listagens só devolvem dados do próprio tenant`, listasVazadas === 0);

    check('a requisição do Beta continua intacta depois das tentativas',
      (await prisma.requisicaoCompra.findUnique({ where: { id: B.requisicao } })).justificativa === 'Requisição privada do Beta');

    // Criação cruzada: usar id do outro tenant como referência.
    const cruzada = await req('/requisicoes', {
      token: tk.alfa, method: 'POST', body: { centroCustoId: B.cc, justificativa: 'Centro de custo alheio' },
    });
    check('criar requisição apontando centro de custo de outro tenant é 404', cruzada.status === 404);

    check('sem token, tudo é 401', (await req('/requisicoes')).status === 401);
    check('token adulterado é 401',
      (await req('/requisicoes', { token: `${tk.alfa}x` })).status === 401);

    console.log('== 2. corrida de aprovação: dois cliques na mesma etapa');
    // Alçada de um nível no tenant A.
    await req('/aprovacoes/regras', {
      token: tk.alfa, method: 'POST',
      body: { nivel: 1, papelExigido: 'GESTOR', valorMin: 0, vigenciaInicio: soDia(emDias(-10)) },
    });
    for (const papel of ['gestor', 'diretor']) {
      await req('/aprovacoes/aprovadores', {
        token: tk.alfa, method: 'POST',
        body: { centroCustoId: A.cc, usuarioId: A[papel], nivel: 1, vigenciaInicio: soDia(emDias(-10)) },
      });
    }
    const instancia = await req('/aprovacoes/instancias', {
      token: tk.alfa, method: 'POST',
      body: { requisicaoId: randomUUID(), centroCustoId: A.cc, solicitanteId: A.solicitante, valorBase: 1000 },
    });
    const etapaId = instancia.body.etapas[0].id;

    const tokenGestor = (await fetch(`${base}/auth/login`, {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ cnpj: '11111111000191', email: 'gestor.alfa@trino.com', senha: SENHA }),
    }).then((r) => r.json())).tokenAcesso;
    const tokenDiretor = (await fetch(`${base}/auth/login`, {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ cnpj: '11111111000191', email: 'diretor.alfa@trino.com', senha: SENHA }),
    }).then((r) => r.json())).tokenAcesso;

    const decidir = (token) =>
      req(`/aprovacoes/etapas/${etapaId}/decisao`, { token, method: 'POST', body: { decisao: 'APROVADO' } });

    const [d1, d2] = await Promise.all([decidir(tokenGestor), decidir(tokenDiretor)]);
    const status = [d1.status, d2.status].sort();
    check('exatamente uma das duas aprovações simultâneas grava',
      status[0] === 201 && status[1] === 409, JSON.stringify({ a: d1.status, b: d2.status }));
    const etapaFinal = await prisma.etapaAprovacao.findUnique({ where: { id: etapaId } });
    check('a etapa tem UMA decisão e UM aprovador',
      etapaFinal.decisao === 'APROVADO' && etapaFinal.version === 1 && etapaFinal.aprovadorId !== null);
    check('a instância foi encerrada uma única vez',
      (await prisma.instanciaAprovacao.findUnique({ where: { id: instancia.body.id } })).version === 1);
    check('só um AuditLog de decisão foi gravado',
      (await prisma.auditLog.count({ where: { entidade: 'etapa_aprovacao', entityId: etapaId } })) === 1);

    console.log('== 3. estoque concorrente: 50 baixas simultâneas do mesmo SKU');
    // 50 baixas de 2 unidades sobre 200 disponíveis: o saldo final tem que ser
    // exatamente 100, sem perder nem duplicar nenhuma.
    const saldo = await dbA.saldoEstoque.create({
      data: { varianteId: A.variante, centroCustoId: A.cc, qtdDisponivel: 200 },
    });
    const baixa = () =>
      prisma.$executeRaw`UPDATE catalogo.saldo_estoque
                            SET qtd_disponivel = qtd_disponivel - 2, version = version + 1
                          WHERE id = ${saldo.id}::uuid AND qtd_disponivel >= 2`;
    const resultados = await Promise.all(Array.from({ length: 50 }, () => baixa()));
    const aplicadas = resultados.filter((linhas) => linhas === 1).length;
    const depois = await prisma.saldoEstoque.findUnique({ where: { id: saldo.id } });
    check('as 50 baixas foram aplicadas', aplicadas === 50, String(aplicadas));
    check('o saldo final é exato: 200 - 50×2 = 100', Number(depois.qtdDisponivel) === 100,
      String(depois.qtdDisponivel));
    check('a versão contou todas as escritas', depois.version === 50, String(depois.version));

    // Agora o inverso: mais baixas do que o saldo permite.
    const excedentes = await Promise.all(Array.from({ length: 60 }, () => baixa()));
    const passaram = excedentes.filter((l) => l === 1).length;
    const final = await prisma.saldoEstoque.findUnique({ where: { id: saldo.id } });
    check('a trava não-negativa recusa o que passaria de zero', passaram === 50, String(passaram));
    check('o saldo para exatamente em zero, nunca negativo', Number(final.qtdDisponivel) === 0,
      String(final.qtdDisponivel));

    // E o incremento concorrente (recebimento) também tem que somar certo.
    const incrementos = await Promise.all(
      Array.from({ length: 50 }, () =>
        prisma.saldoEstoque.update({ where: { id: saldo.id }, data: { qtdDisponivel: { increment: 3 } } }),
      ),
    );
    const somado = await prisma.saldoEstoque.findUnique({ where: { id: saldo.id } });
    check('50 incrementos concorrentes somam exatamente 150',
      incrementos.length === 50 && Number(somado.qtdDisponivel) === 150, String(somado.qtdDisponivel));
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main().catch((e) => { console.error(e); process.exitCode = 1; }).finally(() => prisma.$disconnect());
