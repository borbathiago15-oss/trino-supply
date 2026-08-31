'use strict';

// E2E/integrado da Fase F8 contra Postgres + BullMQ real (Redis):
//   node test/e2e-f8.test.js
// Prova a idempotência por SHA-256, a ingestão não bloqueante, o worker
// resiliente (linha ruim não derruba o arquivo) e o relatório de
// inconformidades. Com IMPORTACAO_SINCRONA=1 roda sem Redis.

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-f8';
process.env.REDIS_PORT = process.env.REDIS_PORT ?? '56379';

require('reflect-metadata');
const argon2 = require('argon2');
const { NestFactory } = require('@nestjs/core');
const { PrismaClient, forTenant, limparBancoDeTestes } = require('@trino/db');
const { AppModule } = require('../dist/app.module');
const { ProcessadorDeImportacaoWorker } = require('../dist/importacao/processador.worker');

const prisma = new PrismaClient();
let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};

const SENHA = 'SenhaForte#2026';
const CNPJ = '50607080000199';
let base, app;
const tokens = {};
const ids = {};

async function req(caminho, { method = 'GET', body, bruto = false } = {}) {
  const r = await fetch(`${base}${caminho}`, {
    method,
    headers: { 'content-type': 'application/json', authorization: `Bearer ${tokens.admin}` },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const t = await r.text();
  if (bruto) return { status: r.status, texto: t, headers: r.headers };
  let j = null; try { j = t ? JSON.parse(t) : null; } catch { j = t; }
  return { status: r.status, body: j };
}

/** Espera o lote sair de RECEBIDO/PROCESSANDO (o worker é assíncrono). */
async function aguardarLote(loteId, tentativas = 60) {
  for (let i = 0; i < tentativas; i += 1) {
    const lote = await prisma.loteImportacao.findUnique({ where: { id: loteId } });
    if (lote && !['RECEBIDO', 'PROCESSANDO'].includes(lote.status)) return lote;
    await new Promise((r) => setTimeout(r, 250));
  }
  return prisma.loteImportacao.findUnique({ where: { id: loteId } });
}

async function main() {
  await limparBancoDeTestes(prisma);
  app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
    const tenant = await prisma.tenant.create({ data: { cnpj: CNPJ, razaoSocial: 'Grupo Trino LTDA' } });
    ids.tenant = tenant.id;
    const db = forTenant(prisma, tenant.id);
    ids.admin = (await db.usuario.create({ data: { nome: 'admin', email: 'admin@t.com', senhaHash: hash } })).id;
    tokens.admin = (await fetch(`${base}/auth/login`, {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ cnpj: CNPJ, email: 'admin@t.com', senha: SENHA }),
    }).then((r) => r.json())).tokenAcesso;

    const regional = await db.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
    await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-01', nome: 'Recife' } });
    const familia = await db.familiaProduto.create({ data: { codigo: 'GER', nome: 'Geral' } });
    await db.tipoProduto.create({ data: { familiaId: familia.id, codigo: 'PAP', nome: 'Papel' } });

    console.log('== ingestão não bloqueante + processamento resiliente');
    // 5 linhas: 3 boas, 1 com unidade inválida, 1 com tipo inexistente.
    const csv = [
      'codigo_tipo;codigo;descricao;unidade_medida;exige_ca',
      'PAP;A4-75;Papel A4 75g;CX;',
      'PAP;A4-90;Papel A4 90g;CX;',
      'PAP;RASCUNHO;Bloco;XX;',                 // unidade inválida → ERRO_VALIDACAO
      'PAP;CANETA;Caneta azul;UN;sim',
      'INEXISTENTE;X1;Item órfão;UN;',          // tipo não existe → ERRO_PERSISTENCIA
    ].join('\n');

    const envio = await req('/importacoes', {
      method: 'POST',
      body: { tipo: 'CATALOGO_SKU', nomeArquivo: 'catalogo.csv', conteudo: csv },
    });
    check('a ingestão responde na hora, sem esperar o processamento',
      envio.status === 201 && envio.body.status === 'RECEBIDO' && envio.body.jaProcessado === false,
      JSON.stringify(envio.body).slice(0, 160));
    check('o total de linhas é contado na ingestão', envio.body.totalLinhas === 5);
    check('o checksum SHA-256 fica gravado', /^[0-9a-f]{64}$/.test(envio.body.checksumSha256));
    const pendentes = await prisma.stagingLinhaImportacao.count({ where: { loteId: envio.body.id } });
    check('todas as linhas foram para o staging', pendentes === 5);

    const lote = await aguardarLote(envio.body.id);
    check('o lote fecha como CONCLUIDO_COM_FALHAS (teve linha ruim)',
      lote.status === 'CONCLUIDO_COM_FALHAS', lote.status);
    check('as 3 linhas válidas foram importadas', lote.linhasSucesso === 3, String(lote.linhasSucesso));
    check('as 2 linhas ruins foram contadas como erro', lote.linhasErro === 2, String(lote.linhasErro));
    check('a soma bate com o total', lote.linhasSucesso + lote.linhasErro === lote.totalLinhas);
    check('concluido_em foi carimbado', lote.concluidoEm !== null);

    console.log('== a linha ruim NÃO derruba as boas');
    const skus = await prisma.skuBase.findMany({ where: { tenantId: ids.tenant }, orderBy: { codigo: 'asc' } });
    check('os SKUs válidos existem no catálogo',
      skus.map((s) => s.codigo).join() === 'A4-75,A4-90,CANETA', skus.map((s) => s.codigo).join());
    check('a variante foi criada junto',
      (await prisma.varianteSku.count({ where: { tenantId: ids.tenant } })) === 3);
    check('exige_ca foi respeitado', skus.find((s) => s.codigo === 'CANETA').exigeCa === true);
    check('o SKU da linha inválida NÃO entrou', !skus.some((s) => s.codigo === 'RASCUNHO'));

    console.log('== relatório de inconformidades');
    const relatorio = await req(`/importacoes/${lote.id}/inconformidades`);
    check('o relatório traz só as linhas que falharam', relatorio.body.linhas.length === 2);
    const erroValidacao = relatorio.body.linhas.find((l) => l.status === 'ERRO_VALIDACAO');
    const erroPersistencia = relatorio.body.linhas.find((l) => l.status === 'ERRO_PERSISTENCIA');
    check('erro de VALIDAÇÃO aponta o campo e o motivo',
      erroValidacao?.mensagemErro?.includes('unidade_medida'), erroValidacao?.mensagemErro);
    check('erro de PERSISTÊNCIA explica a referência que faltou',
      erroPersistencia?.mensagemErro?.includes('INEXISTENTE'), erroPersistencia?.mensagemErro);
    check('cada linha do relatório aponta a linha do arquivo',
      relatorio.body.linhas.every((l) => l.numeroLinha >= 2));
    check('o conteúdo bruto original fica guardado para conferência',
      erroValidacao.conteudoBruto.codigo === 'RASCUNHO');

    const csvRelatorio = await req(`/importacoes/${lote.id}/inconformidades.csv`, { bruto: true });
    check('o CSV de inconformidades baixa como anexo',
      csvRelatorio.status === 200 && csvRelatorio.headers.get('content-disposition')?.includes('attachment'));
    check('o CSV tem cabeçalho e uma linha por erro',
      csvRelatorio.texto.trim().split('\n').length === 3, String(csvRelatorio.texto.trim().split('\n').length));
    check('o CSV traz o motivo de cada linha', csvRelatorio.texto.includes('unidade_medida'));

    console.log('== idempotência do reenvio');
    const reenvio = await req('/importacoes', {
      method: 'POST',
      body: { tipo: 'CATALOGO_SKU', nomeArquivo: 'catalogo-copia.csv', conteudo: csv },
    });
    check('reenviar o MESMO arquivo devolve o lote anterior',
      reenvio.body.id === lote.id && reenvio.body.jaProcessado === true, JSON.stringify(reenvio.body).slice(0, 160));
    check('nenhum lote novo foi criado',
      (await prisma.loteImportacao.count({ where: { tenantId: ids.tenant } })) === 1);
    check('as linhas de staging não foram duplicadas',
      (await prisma.stagingLinhaImportacao.count({ where: { loteId: lote.id } })) === 5);

    const alterado = await req('/importacoes', {
      method: 'POST',
      body: { tipo: 'CATALOGO_SKU', nomeArquivo: 'catalogo.csv', conteudo: `${csv}\nPAP;NOVO;Item novo;UN;` },
    });
    check('arquivo com uma linha a mais é um lote NOVO', alterado.body.id !== lote.id);
    const loteNovo = await aguardarLote(alterado.body.id);
    check('e o novo processa normalmente (upsert não duplica os antigos)',
      loteNovo.linhasSucesso === 4 && loteNovo.linhasErro === 2,
      `${loteNovo.linhasSucesso}/${loteNovo.linhasErro}`);
    check('o catálogo ganhou só o SKU novo',
      (await prisma.skuBase.count({ where: { tenantId: ids.tenant } })) === 4);

    console.log('== outros tipos de lote');
    const orcamento = await req('/importacoes', {
      method: 'POST',
      body: {
        tipo: 'ORCAMENTO_CC', nomeArquivo: 'orcamento.csv',
        conteudo: 'centro_custo;exercicio;valor_orcado\nCC-01;2026;150.000,00\nCC-99;2026;10\nCC-01;1700;5',
      },
    });
    const loteOrc = await aguardarLote(orcamento.body.id);
    check('orçamento: 1 importado, 2 com erro (CC inexistente e exercício inválido)',
      loteOrc.linhasSucesso === 1 && loteOrc.linhasErro === 2, `${loteOrc.linhasSucesso}/${loteOrc.linhasErro}`);
    check('o valor em formato BR foi convertido certo',
      Number((await prisma.orcamentoCentroCusto.findFirst({ where: { exercicio: 2026 } })).valorOrcado) === 150000);

    const fornecedores = await req('/importacoes', {
      method: 'POST',
      body: {
        tipo: 'FORNECEDOR', nomeArquivo: 'fornecedores.csv',
        conteudo: 'cnpj;razao_social;email_contato\n11.111.111/0001-91;Alfa Ltda;alfa@x.com\n123;Ruim;x',
      },
    });
    const loteForn = await aguardarLote(fornecedores.body.id);
    check('fornecedores: 1 importado e 1 com CNPJ inválido',
      loteForn.linhasSucesso === 1 && loteForn.linhasErro === 1);
    check('o CNPJ entrou sem máscara',
      (await prisma.fornecedor.findFirst({ where: { tenantId: ids.tenant } })).cnpj === '11111111000191');

    console.log('== validações de entrada e isolamento');
    check('arquivo vazio é 400 (IMP-ERR-001)',
      (await req('/importacoes', { method: 'POST', body: { tipo: 'CATALOGO_SKU', nomeArquivo: 'v.csv', conteudo: 'a;b' } }))
        .body?.codigo === 'IMP-ERR-001');
    check('tipo de lote desconhecido é 400',
      (await req('/importacoes', { method: 'POST', body: { tipo: 'QUALQUER', nomeArquivo: 'x.csv', conteudo: 'a\n1' } })).status === 400);
    check('lote de outro tenant é 404',
      (await req('/importacoes/00000000-0000-4000-8000-000000000000')).status === 404);
    check('a listagem mostra os lotes do tenant', (await req('/importacoes')).body.length === 4);
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main().catch((e) => { console.error(e); process.exitCode = 1; }).finally(() => prisma.$disconnect());
