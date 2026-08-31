'use strict';

// E2E das Fases F5 e F6 contra Postgres real + NestJS de verdade:
//   node test/e2e-f5f6.test.js
// Percorre cotação → propostas → equalização → pedido → recebimento, provando
// as regras que só aparecem com banco: elegibilidade do convite, coluna gerada
// valor_total, congelamento de SLA, CA de EPI na emissão, 3-way match, saldo de
// estoque e os eventos da Conta 408.

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-f5f6';

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
const CNPJ = '99000111000122';
let base;
const tokens = {};
const ids = {};

async function req(caminho, { method = 'GET', body, como = 'comprador' } = {}) {
  const resposta = await fetch(`${base}${caminho}`, {
    method,
    headers: {
      'content-type': 'application/json',
      ...(tokens[como] ? { authorization: `Bearer ${tokens[como]}` } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const texto = await resposta.text();
  let json = null;
  try { json = texto ? JSON.parse(texto) : null; } catch { json = texto; }
  return { status: resposta.status, body: json };
}

const emDias = (d) => { const x = new Date(); x.setUTCDate(x.getUTCDate() + d); return x; };
const soDia = (d) => d.toISOString().slice(0, 10);

async function semear() {
  const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
  const tenant = await prisma.tenant.create({ data: { cnpj: CNPJ, razaoSocial: 'Grupo Trino LTDA' } });
  ids.tenant = tenant.id;
  const db = forTenant(prisma, tenant.id);

  for (const papel of ['comprador', 'solicitante', 'gestor', 'almoxarife']) {
    const u = await db.usuario.create({ data: { nome: papel, email: `${papel}@trino.com`, senhaHash: hash } });
    ids[papel] = u.id;
  }

  const regional = await db.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
  const cc = await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-01', nome: 'Recife' } });
  ids.cc = cc.id;
  await db.orcamentoCentroCusto.create({
    data: { centroCustoId: cc.id, exercicio: new Date().getUTCFullYear(), valorOrcado: 500000 },
  });

  // Catálogo: um item comum e um EPI (exige CA).
  const familia = await db.familiaProduto.create({ data: { codigo: 'EPI', nome: 'EPI' } });
  const tipo = await db.tipoProduto.create({ data: { familiaId: familia.id, codigo: 'LUV', nome: 'Luvas' } });
  const skuEpi = await db.skuBase.create({
    data: { tipoProdutoId: tipo.id, codigo: 'LUV-NIT', descricao: 'Luva nitrílica', unidadeMedida: 'PAR', exigeCa: true },
  });
  const skuComum = await db.skuBase.create({
    data: { tipoProdutoId: tipo.id, codigo: 'PAP-A4', descricao: 'Papel A4', unidadeMedida: 'CX' },
  });
  ids.varianteEpi = (await db.varianteSku.create({ data: { skuBaseId: skuEpi.id, codigo: 'LUV-M', tamanho: 'M' } })).id;
  ids.varianteComum = (await db.varianteSku.create({ data: { skuBaseId: skuComum.id, codigo: 'PAP-75G' } })).id;

  // Três fornecedores: dois aptos, um sem homologação.
  const certidoes = ['CND_FEDERAL', 'FGTS', 'TRABALHISTA'];
  for (const [chave, cnpj, homologado, comCa] of [
    ['alfa', '11111111000191', true, true],
    ['beta', '22222222000102', true, true],
    ['gama', '33333333000113', false, false],
  ]) {
    const f = await db.fornecedor.create({
      data: {
        cnpj, razaoSocial: `Fornecedor ${chave}`,
        statusHomologacao: homologado ? 'HOMOLOGADO' : 'PENDENTE',
        homologadoEm: homologado ? new Date() : null,
      },
    });
    ids[chave] = f.id;
    for (const tipoDoc of certidoes) {
      await db.documentoFornecedor.create({
        data: { fornecedorId: f.id, tipo: tipoDoc, dataEmissao: emDias(-10), dataValidade: emDias(120), obrigatorio: true },
      });
    }
    if (comCa) {
      await db.certificadoAprovacao.create({
        data: { fornecedorId: f.id, varianteId: ids.varianteEpi, numeroCa: `CA-${chave}`, dataValidade: emDias(200) },
      });
    }
  }

  for (const papel of ['comprador', 'solicitante', 'gestor', 'almoxarife']) {
    const login = await fetch(`${base}/auth/login`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ cnpj: CNPJ, email: `${papel}@trino.com`, senha: SENHA }),
    }).then((r) => r.json());
    tokens[papel] = login.tokenAcesso;
  }
}

/** Cria requisição, leva até EM_COTACAO e devolve { requisicao, itens }. */
async function requisicaoEmCotacao({ varianteId, quantidade = 10, preco = 100 }) {
  const criada = await req('/requisicoes', {
    method: 'POST', como: 'solicitante',
    body: { centroCustoId: ids.cc, justificativa: 'Reposição para a operação' },
  });
  await req(`/requisicoes/${criada.body.id}/itens`, {
    method: 'POST', como: 'solicitante',
    body: { varianteId, quantidade, precoReferencia: preco },
  });
  await req(`/requisicoes/${criada.body.id}/submeter`, { method: 'POST', como: 'solicitante', body: {} });
  await req(`/requisicoes/${criada.body.id}/assumir-triagem`, { method: 'POST', como: 'comprador', body: {} });
  await req(`/requisicoes/${criada.body.id}/enviar-cotacao`, { method: 'POST', como: 'comprador', body: {} });
  const completa = await req(`/requisicoes/${criada.body.id}`, { como: 'comprador' });
  return completa.body;
}

async function main() {
  await limparBancoDeTestes(prisma);
  const app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    await semear();

    console.log('== F5: abertura da cotação');
    const requisicao = await requisicaoEmCotacao({ varianteId: ids.varianteEpi, quantidade: 100, preco: 20 });
    const itemId = requisicao.itens[0].id;

    const pesosRuins = await req('/cotacoes', {
      method: 'POST',
      body: {
        itensRequisicaoIds: [itemId], fornecedorIds: [ids.alfa],
        dataLimiteResposta: emDias(7).toISOString(),
        criterioEqualizacao: { preco: 0.5, lead_time: 0.2, frete: 0.1, cond_pagto: 0.1 },
      },
    });
    check('pesos que não somam 1 são recusados (EQL-ERR-001)',
      pesosRuins.status === 409 && pesosRuins.body?.codigo === 'EQL-ERR-001', JSON.stringify(pesosRuins.body));

    const soInelegivel = await req('/cotacoes', {
      method: 'POST',
      body: { itensRequisicaoIds: [itemId], fornecedorIds: [ids.gama], dataLimiteResposta: emDias(7).toISOString() },
    });
    check('cotação só com fornecedor inelegível é recusada (COT-ERR-003)',
      soInelegivel.status === 409 && soInelegivel.body?.codigo === 'COT-ERR-003');

    const cotacao = await req('/cotacoes', {
      method: 'POST',
      body: {
        itensRequisicaoIds: [itemId],
        fornecedorIds: [ids.alfa, ids.beta, ids.gama],
        dataLimiteResposta: emDias(7).toISOString(),
      },
    });
    check('cotação aberta com numeração COT-AAAA-NNNNNN',
      cotacao.status === 201 && /^COT-\d{4}-\d{6}$/.test(cotacao.body.numero), cotacao.body?.numero);
    check('só os fornecedores ELEGÍVEIS foram convidados',
      cotacao.body.convidados.length === 2 && !cotacao.body.convidados.includes(ids.gama));
    check('o inelegível volta na resposta com o motivo',
      cotacao.body.recusados[0].fornecedorId === ids.gama && cotacao.body.recusados[0].motivos.length > 0,
      JSON.stringify(cotacao.body.recusados));
    check('o item virou rfq_item com a quantidade consolidada',
      cotacao.body.itens.length === 1 && Number(cotacao.body.itens[0].quantidadeConsolidada) === 100);

    const reqPausada = await prisma.requisicaoCompra.findUnique({ where: { id: requisicao.id } });
    check('o SLA da requisição foi CONGELADO na abertura da cotação', reqPausada.slaPausadoEm !== null);

    console.log('== F5: propostas com valor_total gerado pelo banco');
    const rfqItemId = cotacao.body.itens[0].id;
    const naoConvidado = await req(`/cotacoes/${cotacao.body.id}/propostas`, {
      method: 'POST',
      body: {
        fornecedorId: ids.gama, itens: [{ rfqItemId, precoUnitario: 10 }],
        condicaoPagamento: '30', prazoEntregaDias: 5, validadeProposta: soDia(emDias(30)),
      },
    });
    check('fornecedor não convidado não propõe (COT-ERR-005)',
      naoConvidado.status === 409 && naoConvidado.body?.codigo === 'COT-ERR-005');

    const propostaAlfa = await req(`/cotacoes/${cotacao.body.id}/propostas`, {
      method: 'POST',
      body: {
        fornecedorId: ids.alfa, itens: [{ rfqItemId, precoUnitario: 20, marca: 'Alfa' }],
        frete: 200, desconto: 100, condicaoPagamento: '30/60', prazoEntregaDias: 5,
        validadeProposta: soDia(emDias(30)),
      },
    });
    check('proposta registrada com valor de itens derivado (100 × 20)',
      propostaAlfa.status === 201 && Number(propostaAlfa.body.valorItens) === 2000);
    check('valor_total é COLUNA GERADA: 2000 + 200 - 100 = 2100',
      Number(propostaAlfa.body.valorTotal) === 2100, String(propostaAlfa.body.valorTotal));

    const propostaBeta = await req(`/cotacoes/${cotacao.body.id}/propostas`, {
      method: 'POST',
      body: {
        fornecedorId: ids.beta, itens: [{ rfqItemId, precoUnitario: 22 }],
        frete: 0, condicaoPagamento: '30/60/90', prazoEntregaDias: 2,
        validadeProposta: soDia(emDias(30)),
      },
    });
    check('segunda proposta: 100 × 22 = 2200', Number(propostaBeta.body.valorTotal) === 2200);

    const duplicada = await req(`/cotacoes/${cotacao.body.id}/propostas`, {
      method: 'POST',
      body: {
        fornecedorId: ids.alfa, itens: [{ rfqItemId, precoUnitario: 19 }],
        condicaoPagamento: '30', prazoEntregaDias: 5, validadeProposta: soDia(emDias(30)),
      },
    });
    check('o mesmo fornecedor não propõe duas vezes (COT-ERR-008)',
      duplicada.status === 409 && duplicada.body?.codigo === 'COT-ERR-008');

    console.log('== F5: equalização');
    const semJustificativa = await req(`/cotacoes/${cotacao.body.id}/equalizacao`, {
      method: 'POST', body: { propostaVencedoraId: propostaBeta.body.id },
    });
    check('escolher a mais cara sem justificativa é barrado (EQL-ERR-005)',
      semJustificativa.status === 409 && semJustificativa.body?.codigo === 'EQL-ERR-005',
      JSON.stringify(semJustificativa.body));

    const equalizada = await req(`/cotacoes/${cotacao.body.id}/equalizacao`, {
      method: 'POST',
      body: {
        propostaVencedoraId: propostaBeta.body.id,
        justificativaDesvio: 'Entrega em 2 dias atende a parada de manutenção programada',
      },
    });
    check('com justificativa, o desvio do menor preço passa', equalizada.status === 201);
    check('a equalização grava vencedora e menor preço',
      equalizada.body.equalizacao.propostaVencedoraId === propostaBeta.body.id &&
      equalizada.body.equalizacao.propostaMenorPrecoId === propostaAlfa.body.id);
    check('a matriz completa fica gravada em notas',
      equalizada.body.equalizacao.notas.matriz.length === 2 &&
      equalizada.body.equalizacao.notas.pesos.preco === 0.6);
    check('a cotação é encerrada pela equalização',
      (await req(`/cotacoes/${cotacao.body.id}`)).body.status === 'ENCERRADA');

    console.log('== F6: emissão do pedido (T16)');
    // Leva a requisição até APROVACAO_ALCADA e aprova a alçada.
    await req(`/requisicoes/${requisicao.id}/encerrar-cotacao`, { method: 'POST', body: {} }).catch(() => null);
    await prisma.requisicaoCompra.update({ where: { id: requisicao.id }, data: { status: 'APROVACAO_ALCADA' } });

    const semAlcada = await req('/pedidos', {
      method: 'POST',
      body: {
        requisicaoId: requisicao.id, fornecedorId: ids.beta,
        itens: [{ itemRequisicaoId: itemId, precoUnitario: 22 }],
        condicaoPagamento: '30/60/90', prazoEntrega: soDia(emDias(10)),
      },
    });
    check('sem aprovação de alçada não se emite pedido (PED-ERR-001)',
      semAlcada.status === 409 && semAlcada.body?.codigo === 'PED-ERR-001', JSON.stringify(semAlcada.body));

    // Alçada aprovada (a F3 já foi provada no seu próprio e2e).
    await prisma.instanciaAprovacao.create({
      data: {
        tenantId: ids.tenant, requisicaoId: requisicao.id, valorBase: 2200, nivelExigido: 1,
        regraSnapshot: { centroCustoId: ids.cc, nivelFinal: 1 }, status: 'APROVADA', encerradoEm: new Date(),
      },
    });

    const semCa = await req('/pedidos', {
      method: 'POST',
      body: {
        requisicaoId: requisicao.id, fornecedorId: ids.gama,
        itens: [{ itemRequisicaoId: itemId, precoUnitario: 22 }],
        condicaoPagamento: '30', prazoEntrega: soDia(emDias(10)),
      },
    });
    check('fornecedor sem homologação/CA não recebe pedido de EPI (PED-ERR-004)',
      semCa.status === 409 && semCa.body?.codigo === 'PED-ERR-004', JSON.stringify(semCa.body));

    const pedido = await req('/pedidos', {
      method: 'POST',
      body: {
        requisicaoId: requisicao.id, fornecedorId: ids.beta, cotacaoId: cotacao.body.id,
        itens: [{ itemRequisicaoId: itemId, precoUnitario: 22 }],
        condicaoPagamento: '30/60/90', prazoEntrega: soDia(emDias(10)),
      },
    });
    check('pedido emitido com numeração PC-AAAA-NNNNNN',
      pedido.status === 201 && /^PC-\d{4}-\d{6}$/.test(pedido.body.numero), JSON.stringify(pedido.body).slice(0, 200));
    check('valor do pedido = 100 × 22', Number(pedido.body.valorTotal) === 2200);
    check('a requisição foi para PEDIDO_GERADO pelo T16',
      (await prisma.requisicaoCompra.findUnique({ where: { id: requisicao.id } })).status === 'PEDIDO_GERADO');

    console.log('== F6: recebimento parcial com avaria (3-way match)');
    const itemPedidoId = pedido.body.itens[0].id;
    const parcial = await req(`/pedidos/${pedido.body.id}/recebimentos`, {
      method: 'POST', como: 'almoxarife',
      body: {
        itens: [{ itemPedidoId, qtdRecebida: 40, qtdAvariada: 5, descricaoOcorrencia: 'Cinco pares rasgados' }],
        notaFiscal: {
          chaveAcesso: '1'.repeat(44), numero: '1234', serie: '1',
          valorTotal: 880, dataEmissao: soDia(new Date()),
        },
      },
    });
    check('recebimento parcial registrado', parcial.status === 201 && parcial.body.tipo === 'PARCIAL');
    check('o pedido fica RECEBIDO_PARCIAL', parcial.body.pedidoStatus === 'RECEBIDO_PARCIAL');
    check('a NF de 880 bate com 40 × 22 recebidos', parcial.body.valorNotaFiscal === 880 && parcial.body.valorRecebido === 880);
    check('avaria gerou evento da Conta 408 com o valor da perda (5 × 22)',
      parcial.body.eventosConta408.some((e) => e.conta === '408' && e.tipo === 'AVARIA' && e.valor === 110),
      JSON.stringify(parcial.body.eventosConta408));

    const saldo = await prisma.saldoEstoque.findFirst({ where: { varianteId: ids.varianteEpi, centroCustoId: ids.cc } });
    check('só o íntegro entrou em estoque (40 - 5 = 35)', Number(saldo.qtdDisponivel) === 35,
      String(saldo?.qtdDisponivel));
    check('a quantidade recebida acumulou no item do pedido (inclusive a avariada)',
      Number((await prisma.itemPedido.findUnique({ where: { id: itemPedidoId } })).qtdRecebida) === 40);
    check('a requisição acompanhou para RECEBIDA_PARCIAL',
      (await prisma.requisicaoCompra.findUnique({ where: { id: requisicao.id } })).status === 'RECEBIDA_PARCIAL');

    console.log('== F6: recebimento que fecha o pedido');
    const excessivo = await req(`/pedidos/${pedido.body.id}/recebimentos`, {
      method: 'POST', como: 'almoxarife',
      body: { itens: [{ itemPedidoId, qtdRecebida: 80 }] },
    });
    check('recebimento acima da tolerância de 10% é recusado (REC-ERR-006)',
      excessivo.status === 409 && excessivo.body?.codigo === 'REC-ERR-006', JSON.stringify(excessivo.body));

    const total = await req(`/pedidos/${pedido.body.id}/recebimentos`, {
      method: 'POST', como: 'almoxarife',
      body: {
        itens: [{ itemPedidoId, qtdRecebida: 60 }],
        notaFiscal: {
          chaveAcesso: '2'.repeat(44), numero: '1235', serie: '1',
          valorTotal: 1320, dataEmissao: soDia(new Date()),
        },
      },
    });
    check('a remessa que completa o pedido vira TOTAL', total.status === 201 && total.body.tipo === 'TOTAL');
    check('o pedido fica RECEBIDO_TOTAL', total.body.pedidoStatus === 'RECEBIDO_TOTAL');
    check('sem divergências, o 3-way fecha', total.body.conciliado === true, JSON.stringify(total.body.divergencias));
    const saldoFinal = await prisma.saldoEstoque.findFirst({ where: { varianteId: ids.varianteEpi, centroCustoId: ids.cc } });
    check('saldo final = 35 + 60 = 95 (a avaria nunca entrou)', Number(saldoFinal.qtdDisponivel) === 95,
      String(saldoFinal.qtdDisponivel));
    check('a requisição chegou a RECEBIDA_TOTAL',
      (await prisma.requisicaoCompra.findUnique({ where: { id: requisicao.id } })).status === 'RECEBIDA_TOTAL');
    check('pedido já recebido não aceita nova remessa (PED-ERR-007)',
      (await req(`/pedidos/${pedido.body.id}/recebimentos`, {
        method: 'POST', como: 'almoxarife', body: { itens: [{ itemPedidoId, qtdRecebida: 1 }] },
      })).body?.codigo === 'PED-ERR-007');

    console.log('== validações de entrada e auditoria');
    check('chave de NF-e com menos de 44 dígitos é 400',
      (await req(`/pedidos/${pedido.body.id}/recebimentos`, {
        method: 'POST', como: 'almoxarife',
        body: {
          itens: [{ itemPedidoId, qtdRecebida: 1 }],
          notaFiscal: { chaveAcesso: '123', numero: '1', serie: '1', valorTotal: 1, dataEmissao: soDia(new Date()) },
        },
      })).status === 400);

    const trilha = await prisma.auditLog.findMany({ where: { entidade: 'evento_conta_408' } });
    check('os eventos da Conta 408 ficam na trilha durável', trilha.length >= 1);
    check('o evento identifica pedido, fornecedor e centro de custo',
      trilha[0].afterJson.pedidoId === pedido.body.id &&
      trilha[0].afterJson.fornecedorId === ids.beta &&
      trilha[0].afterJson.centroCustoId === ids.cc);
    const acoes = (await prisma.auditLog.findMany({})).map((l) => l.acao);
    check('cotação, proposta, equalização, pedido e recebimento estão auditados',
      ['ABRIR_COTACAO', 'REGISTRAR_PROPOSTA', 'EQUALIZAR_COTACAO', 'T16_EMITIR_PEDIDO', 'RECEBER_TOTAL']
        .every((a) => acoes.includes(a)), acoes.join(','));
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main().catch((e) => { console.error(e); process.exitCode = 1; }).finally(() => prisma.$disconnect());
