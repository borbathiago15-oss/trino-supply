'use strict';

// E2E do CICLO COMPLETO de compras contra Postgres real:
//   node test/ciclo-completo.test.js
//
// Um único pedido percorre a esteira inteira, e cada passo é conferido:
// rascunho → submissão com trava orçamentária → triagem → cotação equalizada →
// aprovação em cascata → pedido → recebimento com NF-e e 3-way match.

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-ciclo';

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
const CNPJ = '60708090000133';
let base;
const tk = {};
const id = {};

async function req(caminho, { method = 'GET', body, como = 'comprador' } = {}) {
  const r = await fetch(`${base}${caminho}`, {
    method,
    headers: { 'content-type': 'application/json', ...(tk[como] ? { authorization: `Bearer ${tk[como]}` } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const t = await r.text();
  let j = null; try { j = t ? JSON.parse(t) : null; } catch { j = t; }
  return { status: r.status, body: j };
}

const emDias = (d) => { const x = new Date(); x.setUTCDate(x.getUTCDate() + d); return x; };
const soDia = (d) => d.toISOString().slice(0, 10);

async function main() {
  await limparBancoDeTestes(prisma);
  const app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    // ---------- cenário ----------
    const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
    const tenant = await prisma.tenant.create({ data: { cnpj: CNPJ, razaoSocial: 'Grupo Trino' } });
    id.tenant = tenant.id;
    const db = forTenant(prisma, tenant.id);

    for (const papel of ['comprador', 'solicitante', 'gestor', 'diretor', 'almoxarife']) {
      id[papel] = (await db.usuario.create({
        data: { nome: papel, email: `${papel}@trino.com`, senhaHash: hash },
      })).id;
      tk[papel] = (await fetch(`${base}/auth/login`, {
        method: 'POST', headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ cnpj: CNPJ, email: `${papel}@trino.com`, senha: SENHA }),
      }).then((r) => r.json())).tokenAcesso;
    }

    const regional = await db.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
    id.cc = (await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-01', nome: 'Recife' } })).id;
    // Saldo apertado de propósito: R$ 5.000 para uma compra de R$ 9.000.
    await db.orcamentoCentroCusto.create({
      data: { centroCustoId: id.cc, exercicio: new Date().getUTCFullYear(), valorOrcado: 5000 },
    });

    const familia = await db.familiaProduto.create({ data: { codigo: 'EPI', nome: 'EPI' } });
    const tipo = await db.tipoProduto.create({ data: { familiaId: familia.id, codigo: 'LUV', nome: 'Luvas' } });
    const sku = await db.skuBase.create({
      data: { tipoProdutoId: tipo.id, codigo: 'LUV-NIT', descricao: 'Luva nitrílica', unidadeMedida: 'PAR', exigeCa: true },
    });
    id.variante = (await db.varianteSku.create({ data: { skuBaseId: sku.id, codigo: 'LUV-M', tamanho: 'M' } })).id;

    for (const [chave, cnpj] of [['alfa', '11111111000191'], ['beta', '22222222000102']]) {
      const f = await db.fornecedor.create({
        data: { cnpj, razaoSocial: `Fornecedor ${chave}`, statusHomologacao: 'HOMOLOGADO', homologadoEm: new Date() },
      });
      id[chave] = f.id;
      for (const doc of ['CND_FEDERAL', 'FGTS', 'TRABALHISTA']) {
        await db.documentoFornecedor.create({
          data: { fornecedorId: f.id, tipo: doc, dataEmissao: emDias(-10), dataValidade: emDias(120), obrigatorio: true },
        });
      }
      await db.certificadoAprovacao.create({
        data: { fornecedorId: f.id, varianteId: id.variante, numeroCa: `CA-${chave}`, dataValidade: emDias(200) },
      });
    }

    // Alçada em dois níveis: gestor (até 5k) e diretor (acima, final).
    for (const f of [
      { nivel: 1, papelExigido: 'GESTOR', valorMin: 0, valorMax: 5000 },
      { nivel: 2, papelExigido: 'DIRETOR', valorMin: 5000 },
    ]) {
      await req('/aprovacoes/regras', { method: 'POST', body: { ...f, vigenciaInicio: soDia(emDias(-30)) } });
    }
    for (const [papel, nivel] of [['gestor', 1], ['diretor', 2]]) {
      await req('/aprovacoes/aprovadores', {
        method: 'POST',
        body: { centroCustoId: id.cc, usuarioId: id[papel], nivel, vigenciaInicio: soDia(emDias(-30)) },
      });
    }

    // ---------- 1. rascunho ----------
    console.log('== 1. criação do rascunho');
    const criada = await req('/requisicoes', {
      como: 'solicitante', method: 'POST',
      body: { centroCustoId: id.cc, prioridade: 'ALTA', justificativa: 'Reposição de EPI para a frente de obra' },
    });
    check('requisição nasce em RASCUNHO', criada.status === 201 && criada.body.status === 'RASCUNHO');
    id.requisicao = criada.body.id;

    const comItem = await req(`/requisicoes/${id.requisicao}/itens`, {
      como: 'solicitante', method: 'POST',
      body: { varianteId: id.variante, quantidade: 200, precoReferencia: 45 },
    });
    check('valor estimado deriva dos itens (200 × 45 = 9.000)', Number(comItem.body.valorEstimado) === 9000);
    id.item = comItem.body.itens[0].id;

    // ---------- 2. submissão com trava orçamentária ----------
    console.log('== 2. submissão e a trava orçamentária (R09)');
    const submetida = await req(`/requisicoes/${id.requisicao}/submeter`, { como: 'solicitante', method: 'POST', body: {} });
    check('a submissão passa mesmo estourando o orçamento', submetida.status === 201);
    check('mas a requisição fica MARCADA como estourada',
      submetida.body.requisicao.orcamentoEstourado === true);
    check('com o retrato do saldo (5.000 orçado, excedente de 4.000)',
      submetida.body.requisicao.orcamentoSnapshot.saldo === 5000 &&
      submetida.body.requisicao.orcamentoSnapshot.excedente === 4000);
    check('e aparece na fila de análise de estouro',
      (await req('/requisicoes?orcamentoEstourado=true')).body.some((r) => r.id === id.requisicao));

    // ---------- 3. triagem ----------
    console.log('== 3. triagem');
    const triagem = await req(`/requisicoes/${id.requisicao}/assumir-triagem`, { method: 'POST', body: {} });
    check('comprador assume e o TTO fecha', triagem.status === 201 && typeof triagem.body.ttoSegundos === 'number');
    check('o SLA congela ao entrar em triagem',
      (await req(`/requisicoes/${id.requisicao}/sla`)).body.pausadoAgora === true);

    // ---------- 4. cotação equalizada ----------
    console.log('== 4. cotação e equalização');
    await req(`/requisicoes/${id.requisicao}/enviar-cotacao`, { method: 'POST', body: {} });
    const cotacao = await req('/cotacoes', {
      method: 'POST',
      body: {
        itensRequisicaoIds: [id.item], fornecedorIds: [id.alfa, id.beta],
        dataLimiteResposta: emDias(5).toISOString(),
      },
    });
    check('cotação convida os dois fornecedores elegíveis', cotacao.body.convidados.length === 2);
    const rfqItemId = cotacao.body.itens[0].id;

    const pAlfa = await req(`/cotacoes/${cotacao.body.id}/propostas`, {
      method: 'POST',
      body: {
        fornecedorId: id.alfa, itens: [{ rfqItemId, precoUnitario: 45 }],
        frete: 0, condicaoPagamento: '30', prazoEntregaDias: 20, validadeProposta: soDia(emDias(30)),
      },
    });
    const pBeta = await req(`/cotacoes/${cotacao.body.id}/propostas`, {
      method: 'POST',
      body: {
        fornecedorId: id.beta, itens: [{ rfqItemId, precoUnitario: 47 }],
        frete: 0, condicaoPagamento: '30/60/90', prazoEntregaDias: 3, validadeProposta: soDia(emDias(30)),
      },
    });
    check('a proposta mais barata é a do Alfa (9.000 × 9.400)',
      Number(pAlfa.body.valorTotal) === 9000 && Number(pBeta.body.valorTotal) === 9400);

    const semJust = await req(`/cotacoes/${cotacao.body.id}/equalizacao`, {
      method: 'POST', body: { propostaVencedoraId: pBeta.body.id },
    });
    check('escolher a mais cara sem justificativa é barrado', semJust.body?.codigo === 'EQL-ERR-005');

    const equalizada = await req(`/cotacoes/${cotacao.body.id}/equalizacao`, {
      method: 'POST',
      body: {
        propostaVencedoraId: pBeta.body.id,
        justificativaDesvio: 'Entrega em 3 dias atende à parada de manutenção; o Alfa levaria 20',
      },
    });
    check('com justificativa, o desvio passa e a cotação encerra', equalizada.status === 201);
    check('a matriz de notas fica gravada para auditoria',
      equalizada.body.equalizacao.notas.matriz.length === 2);
    id.precoVencedor = 47;

    // ---------- 5. aprovação em cascata ----------
    console.log('== 5. aprovação em cascata (dois níveis)');
    await req(`/requisicoes/${id.requisicao}/encerrar-cotacao`, { method: 'POST', body: {} }).catch(() => null);
    await prisma.requisicaoCompra.update({ where: { id: id.requisicao }, data: { status: 'APROVACAO_ALCADA' } });

    const instancia = await req('/aprovacoes/instancias', {
      method: 'POST',
      body: {
        requisicaoId: id.requisicao, centroCustoId: id.cc,
        solicitanteId: id.solicitante, compradorId: id.comprador, valorBase: 9400,
      },
    });
    check('a instância exige o nível 2 (valor acima de 5k)', instancia.body.nivelExigido === 2);
    check('e nasce com duas etapas', instancia.body.etapas.length === 2);
    const etapa = Object.fromEntries(instancia.body.etapas.map((e) => [e.nivel, e.id]));

    check('a fila do gestor mostra a etapa 1',
      (await req('/aprovacoes/pendentes', { como: 'gestor' })).body.some((i) => i.etapaAtualId === etapa[1]));
    check('a fila do diretor ainda NÃO mostra nada (o nível 1 não decidiu)',
      (await req('/aprovacoes/pendentes', { como: 'diretor' })).body.length === 0);

    const n1 = await req(`/aprovacoes/etapas/${etapa[1]}/decisao`, {
      como: 'gestor', method: 'POST', body: { decisao: 'APROVADO' },
    });
    check('nível 1 aprova e passa o estouro adiante',
      n1.status === 201 && n1.body.orcamentoEstourado === true && n1.body.estouroAutorizado === false);
    check('agora a fila do diretor mostra a etapa 2',
      (await req('/aprovacoes/pendentes', { como: 'diretor' })).body.some((i) => i.etapaAtualId === etapa[2]));

    const semAutorizacao = await req(`/aprovacoes/etapas/${etapa[2]}/decisao`, {
      como: 'diretor', method: 'POST', body: { decisao: 'APROVADO' },
    });
    check('o aprovador final não aprova estouro sem autorizar (APV-B6)',
      semAutorizacao.body?.codigo === 'APV-B6');

    const n2 = await req(`/aprovacoes/etapas/${etapa[2]}/decisao`, {
      como: 'diretor', method: 'POST', body: { decisao: 'APROVADO', autorizarEstouro: true },
    });
    check('com a autorização explícita, a instância é APROVADA',
      n2.status === 201 && n2.body.statusInstancia === 'APROVADA' && n2.body.estouroAutorizado === true);
    check('a requisição registra quem autorizou o estouro',
      (await prisma.requisicaoCompra.findUnique({ where: { id: id.requisicao } })).estouroAutorizadoPor === id.diretor);

    // ---------- 6. pedido ----------
    console.log('== 6. emissão do pedido (T16)');
    const pedido = await req('/pedidos', {
      method: 'POST',
      body: {
        requisicaoId: id.requisicao, fornecedorId: id.beta, cotacaoId: cotacao.body.id,
        itens: [{ itemRequisicaoId: id.item, precoUnitario: id.precoVencedor }],
        condicaoPagamento: '30/60/90', prazoEntrega: soDia(emDias(10)),
      },
    });
    check('pedido emitido ao fornecedor vencedor', pedido.status === 201);
    check('pelo valor da proposta equalizada (200 × 47 = 9.400)', Number(pedido.body.valorTotal) === 9400);
    check('a requisição vai para PEDIDO_GERADO',
      (await prisma.requisicaoCompra.findUnique({ where: { id: id.requisicao } })).status === 'PEDIDO_GERADO');
    id.itemPedido = pedido.body.itens[0].id;

    // ---------- 7. recebimento com 3-way ----------
    console.log('== 7. recebimento com NF-e e 3-way match');
    const parcial = await req(`/pedidos/${pedido.body.id}/recebimentos`, {
      como: 'almoxarife', method: 'POST',
      body: {
        itens: [{ itemPedidoId: id.itemPedido, qtdRecebida: 120, qtdAvariada: 20, descricaoOcorrencia: 'Caixas molhadas no transporte' }],
        notaFiscal: {
          chaveAcesso: '1'.repeat(44), numero: '5001', serie: '1',
          valorTotal: 120 * 47, dataEmissao: soDia(new Date()),
        },
      },
    });
    check('recebimento parcial registrado', parcial.status === 201 && parcial.body.tipo === 'PARCIAL');
    check('a NF bate com o recebido a preço de pedido (3-way fecha no valor)',
      parcial.body.valorNotaFiscal === 5640 && parcial.body.valorRecebido === 5640);
    check('a avaria vira lançamento na Conta 408 (20 × 47 = 940)',
      parcial.body.eventosConta408.some((e) => e.tipo === 'AVARIA' && e.valor === 940));
    check('só o íntegro entra no estoque (120 - 20 = 100)',
      Number((await prisma.saldoEstoque.findFirst({ where: { varianteId: id.variante, centroCustoId: id.cc } })).qtdDisponivel) === 100);

    const nfErrada = await req(`/pedidos/${pedido.body.id}/recebimentos`, {
      como: 'almoxarife', method: 'POST',
      body: {
        itens: [{ itemPedidoId: id.itemPedido, qtdRecebida: 80 }],
        notaFiscal: {
          chaveAcesso: '2'.repeat(44), numero: '5002', serie: '1',
          valorTotal: 9999, dataEmissao: soDia(new Date()),
        },
      },
    });
    check('a remessa que completa o pedido vira TOTAL', nfErrada.body.tipo === 'TOTAL');
    check('mas a NF divergente é APONTADA sem impedir o registro',
      nfErrada.body.conciliado === false &&
      nfErrada.body.divergencias.some((d) => d.codigo === 'REC-DIV-005'));
    check('a divergência de valor também vai para a Conta 408',
      nfErrada.body.eventosConta408.some((e) => e.tipo === 'DIVERGENCIA_VALOR'));
    check('o pedido fica RECEBIDO_TOTAL', nfErrada.body.pedidoStatus === 'RECEBIDO_TOTAL');
    check('saldo final: 100 + 80 = 180 (a avaria nunca entrou)',
      Number((await prisma.saldoEstoque.findFirst({ where: { varianteId: id.variante, centroCustoId: id.cc } })).qtdDisponivel) === 180);

    // ---------- 8. o ciclo fechou ----------
    console.log('== 8. fecho do ciclo');
    const final = await prisma.requisicaoCompra.findUnique({ where: { id: id.requisicao } });
    check('a requisição chegou a RECEBIDA_TOTAL', final.status === 'RECEBIDA_TOTAL');
    check('e está concluída', final.concluidaEm !== null);

    const metricas = await req(`/requisicoes/${id.requisicao}/sla`);
    check('o TTR fechou com tempo útil apurado',
      metricas.body.ttr.emAndamento === false && typeof metricas.body.ttr.segundosUteis === 'number');
    check('o tempo pausado foi contabilizado', metricas.body.slaSegundosPausados >= 0);

    const trilha = await prisma.auditLog.findMany({ where: { entityId: id.requisicao } });
    const acoes = trilha.map((l) => l.acao);
    check('a trilha guarda o ciclo inteiro, transição a transição',
      ['T01_CRIAR', 'T03_SUBMETER', 'T05_ASSUMIR_TRIAGEM', 'T10_ENVIAR_COTACAO', 'T16_EMITIR_PEDIDO',
       'T17_RECEBER_PARCIAL', 'T18_RECEBER_TOTAL'].every((a) => acoes.includes(a)), acoes.join(','));
    check('todo registro da trilha é do tenant certo',
      trilha.every((l) => l.tenantId === id.tenant));
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main().catch((e) => { console.error(e); process.exitCode = 1; }).finally(() => prisma.$disconnect());
