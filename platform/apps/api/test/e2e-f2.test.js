'use strict';

// E2E da Fase F2 contra Postgres real + app NestJS de verdade:
//   node test/e2e-f2.test.js
// Prova: CRUD hierárquico do catálogo com class-validator, ROP, saldo com
// bloqueio otimista, cadastro de fornecedores/documentos/CA, isolamento entre
// tenants e a regra de elegibilidade (homologação + certidões + CA de EPI).

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-f2';

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
let base, token, tokenBeta;

async function req(caminho, { method = 'GET', body, headers = {}, comToken = token } = {}) {
  const resposta = await fetch(`${base}${caminho}`, {
    method,
    headers: {
      'content-type': 'application/json',
      ...(comToken ? { authorization: `Bearer ${comToken}` } : {}),
      ...headers,
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const texto = await resposta.text();
  let json = null;
  try { json = texto ? JSON.parse(texto) : null; } catch { json = texto; }
  return { status: resposta.status, body: json };
}

const dataEm = (dias) => {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + dias);
  return d.toISOString().slice(0, 10);
};

const limpar = () => limparBancoDeTestes(prisma);

async function semear() {
  const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
  const alfa = await prisma.tenant.create({ data: { cnpj: '55666777000188', razaoSocial: 'Grupo Alfa LTDA' } });
  const beta = await prisma.tenant.create({ data: { cnpj: '66777888000199', razaoSocial: 'Grupo Beta LTDA' } });
  const dbAlfa = forTenant(prisma, alfa.id);
  const dbBeta = forTenant(prisma, beta.id);

  await dbAlfa.usuario.create({ data: { nome: 'Ana Alfa', email: 'ana@alfa.com', senhaHash: hash } });
  await dbBeta.usuario.create({ data: { nome: 'Beto Beta', email: 'beto@beta.com', senhaHash: hash } });

  const regional = await dbAlfa.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
  const cc = await dbAlfa.centroCusto.create({
    data: { regionalId: regional.id, codigo: 'CC-01', nome: 'Operação Recife' },
  });
  const regionalBeta = await dbBeta.regional.create({ data: { codigo: 'SE', nome: 'Sudeste' } });
  const ccBeta = await dbBeta.centroCusto.create({
    data: { regionalId: regionalBeta.id, codigo: 'CC-99', nome: 'Operação Beta' },
  });
  return { alfa, beta, cc, ccBeta };
}

async function main() {
  await limpar();
  const { cc, ccBeta } = await semear();

  const app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    token = (await req('/auth/login', { method: 'POST', body: { cnpj: '55666777000188', email: 'ana@alfa.com', senha: SENHA }, comToken: null })).body.tokenAcesso;
    tokenBeta = (await req('/auth/login', { method: 'POST', body: { cnpj: '66777888000199', email: 'beto@beta.com', senha: SENHA }, comToken: null })).body.tokenAcesso;
    check('login dos dois tenants', !!token && !!tokenBeta);

    console.log('== catálogo hierárquico');
    const familia = await req('/catalogo/familias', { method: 'POST', body: { codigo: 'epi', nome: 'EPI', leadTimeMedioDias: 7 } });
    check('cria família (código normalizado)', familia.status === 201 && familia.body.codigo === 'EPI', JSON.stringify(familia.body));

    const duplicada = await req('/catalogo/familias', { method: 'POST', body: { codigo: 'EPI', nome: 'Outra' } });
    check('código repetido é 409 CAT-ERR-001', duplicada.status === 409 && duplicada.body?.codigo === 'CAT-ERR-001');

    const invalida = await req('/catalogo/familias', { method: 'POST', body: { codigo: 'X', nome: 'Y', leadTimeMedioDias: -3 } });
    check('class-validator barra leadTime negativo (400)', invalida.status === 400);

    const campoEstranho = await req('/catalogo/familias', { method: 'POST', body: { codigo: 'Z9', nome: 'Z', invadir: true } });
    check('campo desconhecido no payload é 400 (whitelist)', campoEstranho.status === 400);

    const tipo = await req('/catalogo/tipos', { method: 'POST', body: { familiaId: familia.body.id, codigo: 'LUV', nome: 'Luvas' } });
    check('cria tipo sob a família', tipo.status === 201);

    const tipoOrfao = await req('/catalogo/tipos', { method: 'POST', body: { familiaId: '00000000-0000-4000-8000-000000000000', codigo: 'XX', nome: 'Órfão' } });
    check('tipo com família inexistente é 404', tipoOrfao.status === 404);

    const sku = await req('/catalogo/skus', { method: 'POST', body: { tipoProdutoId: tipo.body.id, codigo: 'LUV-NIT', descricao: 'Luva nitrílica', unidadeMedida: 'PAR', exigeCa: true } });
    check('cria SKU base de EPI (exigeCa)', sku.status === 201 && sku.body.exigeCa === true);

    const unidadeInvalida = await req('/catalogo/skus', { method: 'POST', body: { tipoProdutoId: tipo.body.id, codigo: 'X1', descricao: 'x', unidadeMedida: 'DUZIA' } });
    check('unidade de medida fora do enum é 400', unidadeInvalida.status === 400);

    const variante = await req('/catalogo/variantes', { method: 'POST', body: { skuBaseId: sku.body.id, codigo: 'LUV-NIT-M', tamanho: 'M', codigoBarras: '7891234567890' } });
    check('cria variante com EAN', variante.status === 201);

    const eanRepetido = await req('/catalogo/variantes', { method: 'POST', body: { skuBaseId: sku.body.id, codigo: 'LUV-NIT-G', tamanho: 'G', codigoBarras: '7891234567890' } });
    check('EAN duplicado no tenant é 409 (índice parcial ux_variante_ean)', eanRepetido.status === 409, `status=${eanRepetido.status}`);

    const semEan = await req('/catalogo/variantes', { method: 'POST', body: { skuBaseId: sku.body.id, codigo: 'LUV-NIT-G', tamanho: 'G' } });
    check('variante sem EAN convive com outra sem EAN', semEan.status === 201);

    const filtradas = await req(`/catalogo/variantes?skuBaseId=${sku.body.id}`);
    check('filtro por skuBaseId funciona', filtradas.status === 200 && filtradas.body.length === 2);
    check('query param não-UUID é 400', (await req('/catalogo/variantes?skuBaseId=abc')).status === 400);

    console.log('== ROP e saldo com bloqueio otimista');
    const rop = await req('/catalogo/parametros-estoque', { method: 'PUT', body: { varianteId: variante.body.id, centroCustoId: cc.id, pontoPedidoRop: 50, estoqueSeguranca: 20, leadTimeDias: 10, gerarScAutomatica: true } });
    check('define ROP', rop.status === 200 && Number(rop.body.pontoPedidoRop) === 50, JSON.stringify(rop.body));

    const ropAtualizado = await req('/catalogo/parametros-estoque', { method: 'PUT', body: { varianteId: variante.body.id, centroCustoId: cc.id, pontoPedidoRop: 80, estoqueSeguranca: 30 } });
    check('redefinir o par (variante, CC) atualiza em vez de duplicar',
      Number(ropAtualizado.body.pontoPedidoRop) === 80 && (await prisma.parametroEstoque.count()) === 1);

    const ropNegativo = await req('/catalogo/parametros-estoque', { method: 'PUT', body: { varianteId: variante.body.id, centroCustoId: cc.id, pontoPedidoRop: -1, estoqueSeguranca: 0 } });
    check('ROP negativo é 400', ropNegativo.status === 400);

    const ccAlheio = await req('/catalogo/parametros-estoque', { method: 'PUT', body: { varianteId: variante.body.id, centroCustoId: ccBeta.id, pontoPedidoRop: 1, estoqueSeguranca: 1 } });
    check('centro de custo de outro tenant é 404', ccAlheio.status === 404);

    const saldo = await req('/catalogo/saldos', { method: 'POST', body: { varianteId: variante.body.id, centroCustoId: cc.id, qtdDisponivel: 100, qtdReservada: 10 } });
    check('cria saldo (version 0)', saldo.status === 201 && saldo.body.version === 0);

    const ajuste = await req(`/catalogo/saldos/${saldo.body.id}`, { method: 'PATCH', body: { version: 0, qtdDisponivel: 90 } });
    check('ajuste com a versão correta sobe para version 1',
      ajuste.status === 200 && ajuste.body.version === 1 && Number(ajuste.body.qtdDisponivel) === 90);

    const ajusteVelho = await req(`/catalogo/saldos/${saldo.body.id}`, { method: 'PATCH', body: { version: 0, qtdDisponivel: 5 } });
    check('ajuste com versão velha é 409 EST-ERR-409', ajusteVelho.status === 409 && ajusteVelho.body?.codigo === 'EST-ERR-409');
    check('saldo não foi sobrescrito pela leitura velha',
      Number((await prisma.saldoEstoque.findUnique({ where: { id: saldo.body.id } })).qtdDisponivel) === 90);

    const reservaMaior = await req(`/catalogo/saldos/${saldo.body.id}`, { method: 'PATCH', body: { version: 1, qtdReservada: 999 } });
    check('reserva maior que o disponível é barrada pelo ck_saldo_reserva', reservaMaior.status >= 400, `status=${reservaMaior.status}`);

    console.log('== isolamento entre tenants no catálogo');
    check('tenant Beta não enxerga as famílias do Alfa',
      (await req('/catalogo/familias', { comToken: tokenBeta })).body.length === 0);
    check('tenant Beta não edita a família do Alfa',
      (await req(`/catalogo/familias/${familia.body.id}`, { method: 'PATCH', body: { nome: 'Invadida' }, comToken: tokenBeta })).status === 404);

    console.log('== fornecedores, documentos e CA');
    const forn = await req('/fornecedores', { method: 'POST', body: { cnpj: '12345678000195', razaoSocial: 'Fornecedora EPI SA', emailContato: 'Vendas@EPI.com' } });
    check('cria fornecedor (PENDENTE por padrão)', forn.status === 201 && forn.body.statusHomologacao === 'PENDENTE');
    check('email normalizado', forn.body.emailContato === 'vendas@epi.com');

    check('CNPJ com pontuação é 400',
      (await req('/fornecedores', { method: 'POST', body: { cnpj: '12.345.678/0001-95', razaoSocial: 'X' } })).status === 400);
    check('CNPJ repetido no tenant é 409',
      (await req('/fornecedores', { method: 'POST', body: { cnpj: '12345678000195', razaoSocial: 'Outra' } })).status === 409);
    check('email inválido é 400',
      (await req('/fornecedores', { method: 'POST', body: { cnpj: '99888777000166', razaoSocial: 'Y', emailContato: 'nao-e-email' } })).status === 400);

    console.log('== elegibilidade');
    let eleg = await req(`/fornecedores/${forn.body.id}/elegibilidade`);
    check('pendente + sem certidões: inelegível com os dois motivos',
      eleg.body.elegivel === false &&
      eleg.body.motivos.some((m) => m.codigo === 'FOR-ELG-001') &&
      eleg.body.motivos.some((m) => m.codigo === 'FOR-ELG-003'),
      JSON.stringify(eleg.body.motivos));

    const semMotivo = await req(`/fornecedores/${forn.body.id}/status`, { method: 'PATCH', body: { status: 'SUSPENSO' } });
    check('suspender sem motivo é 400 FOR-ERR-002', semMotivo.status === 400 && semMotivo.body?.codigo === 'FOR-ERR-002');

    await req(`/fornecedores/${forn.body.id}/status`, { method: 'PATCH', body: { status: 'HOMOLOGADO' } });
    const doc = await req(`/fornecedores/${forn.body.id}/documentos`, { method: 'POST', body: { tipo: 'CND_FEDERAL', numero: '123', dataEmissao: dataEm(-30), dataValidade: dataEm(60) } });
    check('registra CND federal válida', doc.status === 201);
    await req(`/fornecedores/${forn.body.id}/documentos`, { method: 'POST', body: { tipo: 'FGTS', dataEmissao: dataEm(-30), dataValidade: dataEm(30) } });
    await req(`/fornecedores/${forn.body.id}/documentos`, { method: 'POST', body: { tipo: 'TRABALHISTA', dataEmissao: dataEm(-30), dataValidade: dataEm(30) } });

    check('validade anterior à emissão é 400 FOR-ERR-003',
      (await req(`/fornecedores/${forn.body.id}/documentos`, { method: 'POST', body: { tipo: 'ALVARA', dataEmissao: dataEm(10), dataValidade: dataEm(1) } })).status === 400);

    eleg = await req(`/fornecedores/${forn.body.id}/elegibilidade`);
    check('homologado com as três certidões válidas: elegível', eleg.body.elegivel === true, JSON.stringify(eleg.body.motivos));

    // Item de EPI sem CA: elegível no geral, inelegível para aquela variante.
    eleg = await req(`/fornecedores/${forn.body.id}/elegibilidade?varianteId=${variante.body.id}`);
    check('EPI sem CA válido: FOR-ELG-004',
      eleg.body.elegivel === false && eleg.body.motivos.some((m) => m.codigo === 'FOR-ELG-004'));

    const vinculoBarrado = await req(`/fornecedores/${forn.body.id}/skus`, { method: 'POST', body: { varianteId: variante.body.id, ultimoPreco: 12.5 } });
    check('vincular SKU de EPI sem CA é 409 FOR-ELG-409',
      vinculoBarrado.status === 409 && vinculoBarrado.body?.codigo === 'FOR-ELG-409');

    const caVencido = await req(`/fornecedores/${forn.body.id}/certificados`, { method: 'POST', body: { varianteId: variante.body.id, numeroCa: '12345', dataValidade: dataEm(-1) } });
    check('registra CA vencido', caVencido.status === 201);
    eleg = await req(`/fornecedores/${forn.body.id}/elegibilidade?varianteId=${variante.body.id}`);
    check('CA vencido não vale', eleg.body.elegivel === false);

    await req(`/fornecedores/${forn.body.id}/certificados`, { method: 'POST', body: { varianteId: variante.body.id, numeroCa: '67890', dataValidade: dataEm(365) } });
    eleg = await req(`/fornecedores/${forn.body.id}/elegibilidade?varianteId=${variante.body.id}`);
    check('com CA válido o fornecedor fica elegível para a variante', eleg.body.elegivel === true, JSON.stringify(eleg.body.motivos));

    const vinculo = await req(`/fornecedores/${forn.body.id}/skus`, { method: 'POST', body: { varianteId: variante.body.id, ultimoPreco: 12.5, leadTimeDias: 5 } });
    check('agora o vínculo passa', vinculo.status === 201 && Number(vinculo.body.ultimoPreco) === 12.5, `status=${vinculo.status}`);

    // Certidão vencida derruba a elegibilidade mesmo com tudo o mais em dia.
    await prisma.documentoFornecedor.updateMany({
      where: { fornecedorId: forn.body.id, tipo: 'FGTS' },
      data: { dataValidade: new Date(`${dataEm(-5)}T00:00:00.000Z`) },
    });
    eleg = await req(`/fornecedores/${forn.body.id}/elegibilidade`);
    check('documento obrigatório vencido bloqueia (FOR-ELG-002)',
      eleg.body.elegivel === false && eleg.body.motivos.some((m) => m.codigo === 'FOR-ELG-002'));
    check('o documento vencido é identificado na resposta',
      eleg.body.documentosVencidos.some((d) => d.tipo === 'FGTS'));
    check('vínculo de SKU é barrado com certidão vencida',
      (await req(`/fornecedores/${forn.body.id}/skus`, { method: 'POST', body: { varianteId: variante.body.id, ultimoPreco: 13 } })).status === 409);

    // Reemissão: o documento mais recente do tipo é o que vale.
    await req(`/fornecedores/${forn.body.id}/documentos`, { method: 'POST', body: { tipo: 'FGTS', dataEmissao: dataEm(-1), dataValidade: dataEm(90) } });
    eleg = await req(`/fornecedores/${forn.body.id}/elegibilidade`);
    check('reemissão da certidão restaura a elegibilidade', eleg.body.elegivel === true, JSON.stringify(eleg.body.motivos));

    // Documento não obrigatório vencido não bloqueia ninguém.
    await req(`/fornecedores/${forn.body.id}/documentos`, { method: 'POST', body: { tipo: 'CERTIFICADO_ISO', dataEmissao: dataEm(-400), dataValidade: dataEm(-10), obrigatorio: false } });
    eleg = await req(`/fornecedores/${forn.body.id}/elegibilidade`);
    check('documento opcional vencido não bloqueia', eleg.body.elegivel === true);

    check('suspender derruba a elegibilidade',
      (await req(`/fornecedores/${forn.body.id}/status`, { method: 'PATCH', body: { status: 'SUSPENSO', motivo: 'Auditoria em curso' } })).status === 200 &&
      (await req(`/fornecedores/${forn.body.id}/elegibilidade`)).body.elegivel === false);

    check('fornecedor de outro tenant é 404',
      (await req(`/fornecedores/${forn.body.id}/elegibilidade`, { comToken: tokenBeta })).status === 404);

    console.log('== auditoria das mutações da F2');
    const logFornecedor = await prisma.auditLog.findFirst({ where: { entidade: 'fornecedor', entityId: forn.body.id, acao: 'CREATE' } });
    check('criação de fornecedor foi auditada', !!logFornecedor && logFornecedor.afterJson?.cnpj === '12345678000195');

    const logStatus = await prisma.auditLog.findFirst({
      where: { entidade: 'fornecedor', entityId: forn.body.id, acao: 'UPDATE' },
      orderBy: { timestampUtc: 'desc' },
    });
    check('mudança de status guarda antes/depois',
      logStatus?.beforeJson?.statusHomologacao === 'HOMOLOGADO' && logStatus?.afterJson?.statusHomologacao === 'SUSPENSO',
      JSON.stringify({ b: logStatus?.beforeJson?.statusHomologacao, a: logStatus?.afterJson?.statusHomologacao }));

    const logDoc = await prisma.auditLog.findFirst({ where: { entidade: 'documento_fornecedor', acao: 'CREATE' } });
    check('documento auditado com o id do próprio documento (não o do fornecedor)',
      !!logDoc && logDoc.entityId !== forn.body.id && !!(await prisma.documentoFornecedor.findUnique({ where: { id: logDoc.entityId } })));

    const logSaldo = await prisma.auditLog.findFirst({ where: { entidade: 'saldo_estoque', acao: 'UPDATE' } });
    check('ajuste de saldo auditado com as duas versões',
      logSaldo?.beforeJson?.version === 0 && logSaldo?.afterJson?.version === 1);
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main()
  .catch((e) => { console.error(e); process.exitCode = 1; })
  .finally(() => prisma.$disconnect());
