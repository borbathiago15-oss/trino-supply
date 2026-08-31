'use strict';

// Cenário do E2E do frontend: um tenant com requisições em vários estágios do
// SLA, uma cotação com propostas para equalizar, uma aprovação pendente e um
// pedido aguardando recebimento. Idempotente — limpa antes de semear.
//
//   node scripts/seed-e2e.js

const CAMINHO = require('node:path');
const RAIZ = CAMINHO.resolve(__dirname, '../../..');
require('dotenv').config({ path: CAMINHO.join(RAIZ, 'packages/db/.env') });

const argon2 = require('argon2');
const { PrismaClient, forTenant, limparBancoDeTestes } = require('@trino/db');

const prisma = new PrismaClient();
const API = process.env.API_URL ?? 'http://127.0.0.1:3001/api/v1';
const CNPJ = process.env.E2E_CNPJ ?? '70809010000144';
const SENHA = process.env.E2E_SENHA ?? 'SenhaForte#2026';

const emDias = (d) => { const x = new Date(); x.setUTCDate(x.getUTCDate() + d); return x; };
const soDia = (d) => d.toISOString().slice(0, 10);
const horasAtras = (h) => new Date(Date.now() - h * 3600000);

async function login(email) {
  const r = await fetch(`${API}/auth/login`, {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ cnpj: CNPJ, email, senha: SENHA }),
  });
  if (!r.ok) throw new Error(`Login falhou para ${email}: ${r.status}`);
  return (await r.json()).tokenAcesso;
}

const req = async (token, caminho, method = 'GET', body) => {
  const r = await fetch(`${API}${caminho}`, {
    method,
    headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const t = await r.text();
  return { status: r.status, body: t ? JSON.parse(t) : null };
};

async function main() {
  await limparBancoDeTestes(prisma);

  const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
  const tenant = await prisma.tenant.create({ data: { cnpj: CNPJ, razaoSocial: 'Grupo Trino LTDA' } });
  const db = forTenant(prisma, tenant.id);

  const u = {};
  for (const papel of ['comprador', 'solicitante', 'gestor', 'diretor', 'almoxarife']) {
    u[papel] = (await db.usuario.create({
      data: { nome: `${papel[0].toUpperCase()}${papel.slice(1)} Trino`, email: `${papel}@trino.com`, senhaHash: hash },
    })).id;
  }

  const regional = await db.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
  const cc = await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-01', nome: 'Operação Recife' } });
  await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-02', nome: 'Operação Natal' } });
  await db.orcamentoCentroCusto.create({
    data: { centroCustoId: cc.id, exercicio: new Date().getUTCFullYear(), valorOrcado: 50000 },
  });

  const familia = await db.familiaProduto.create({ data: { codigo: 'EPI', nome: 'EPI' } });
  const tipo = await db.tipoProduto.create({ data: { familiaId: familia.id, codigo: 'LUV', nome: 'Luvas' } });
  const sku = await db.skuBase.create({
    data: { tipoProdutoId: tipo.id, codigo: 'LUV-NIT', descricao: 'Luva nitrílica', unidadeMedida: 'PAR' },
  });
  const variante = await db.varianteSku.create({ data: { skuBaseId: sku.id, codigo: 'LUV-M', tamanho: 'M' } });

  const fornecedores = [];
  for (const [nome, cnpj] of [['Alfa Suprimentos', '11111111000191'], ['Beta Distribuidora', '22222222000102']]) {
    const f = await db.fornecedor.create({
      data: { cnpj, razaoSocial: nome, statusHomologacao: 'HOMOLOGADO', homologadoEm: new Date() },
    });
    for (const doc of ['CND_FEDERAL', 'FGTS', 'TRABALHISTA']) {
      await db.documentoFornecedor.create({
        data: { fornecedorId: f.id, tipo: doc, dataEmissao: emDias(-10), dataValidade: emDias(120), obrigatorio: true },
      });
    }
    fornecedores.push(f.id);
  }

  const comprador = await login('comprador@trino.com');
  const solicitante = await login('solicitante@trino.com');

  // Alçadas: gestor (N1) e diretor (N2, final).
  for (const f of [
    { nivel: 1, papelExigido: 'GESTOR', valorMin: 0, valorMax: 5000 },
    { nivel: 2, papelExigido: 'DIRETOR', valorMin: 5000 },
  ]) {
    await req(comprador, '/aprovacoes/regras', 'POST', { ...f, vigenciaInicio: soDia(emDias(-30)) });
  }
  for (const [papel, nivel] of [['gestor', 1], ['diretor', 2]]) {
    await req(comprador, '/aprovacoes/aprovadores', 'POST', {
      centroCustoId: cc.id, usuarioId: u[papel], nivel, vigenciaInicio: soDia(emDias(-30)),
    });
  }

  // Requisições em estágios variados do SLA — o farol precisa das quatro cores.
  const cenarios = [
    { just: 'Reposição de EPI da equipe de campo', prio: 'NORMAL', qtd: 10, preco: 50, submetida: horasAtras(2) },
    { just: 'Compra urgente para parada de manutenção', prio: 'EMERGENCIAL', qtd: 20, preco: 60, submetida: horasAtras(3) },
    { just: 'Reposição trimestral do almoxarifado', prio: 'ALTA', qtd: 30, preco: 40, submetida: horasAtras(30) },
    { just: 'Itens de escritório', prio: 'BAIXA', qtd: 5, preco: 20, submetida: null },
  ];
  for (const c of cenarios) {
    const criada = await req(solicitante, '/requisicoes', 'POST', {
      centroCustoId: cc.id, prioridade: c.prio, justificativa: c.just,
    });
    await req(solicitante, `/requisicoes/${criada.body.id}/itens`, 'POST', {
      varianteId: variante.id, quantidade: c.qtd, precoReferencia: c.preco,
    });
    if (c.submetida) {
      await req(solicitante, `/requisicoes/${criada.body.id}/submeter`, 'POST', {});
      await prisma.requisicaoCompra.update({ where: { id: criada.body.id }, data: { submetidaEm: c.submetida } });
    }
  }

  // Cotação com duas propostas, pronta para equalizar.
  const paraCotar = await req(solicitante, '/requisicoes', 'POST', {
    centroCustoId: cc.id, prioridade: 'ALTA', justificativa: 'Reposição para a frente de obra do metrô',
  });
  await req(solicitante, `/requisicoes/${paraCotar.body.id}/itens`, 'POST', {
    varianteId: variante.id, quantidade: 200, precoReferencia: 45,
  });
  await req(solicitante, `/requisicoes/${paraCotar.body.id}/submeter`, 'POST', {});
  await req(comprador, `/requisicoes/${paraCotar.body.id}/assumir-triagem`, 'POST', {});
  await req(comprador, `/requisicoes/${paraCotar.body.id}/enviar-cotacao`, 'POST', {});
  const detalhe = await req(comprador, `/requisicoes/${paraCotar.body.id}`);
  const cotacao = await req(comprador, '/cotacoes', 'POST', {
    itensRequisicaoIds: [detalhe.body.itens[0].id], fornecedorIds: fornecedores,
    dataLimiteResposta: emDias(5).toISOString(),
  });
  const rfqItemId = cotacao.body.itens[0].id;
  await req(comprador, `/cotacoes/${cotacao.body.id}/propostas`, 'POST', {
    fornecedorId: fornecedores[0], itens: [{ rfqItemId, precoUnitario: 45 }],
    frete: 0, condicaoPagamento: '30', prazoEntregaDias: 20, validadeProposta: soDia(emDias(30)),
  });
  await req(comprador, `/cotacoes/${cotacao.body.id}/propostas`, 'POST', {
    fornecedorId: fornecedores[1], itens: [{ rfqItemId, precoUnitario: 47 }],
    frete: 0, condicaoPagamento: '30/60/90', prazoEntregaDias: 3, validadeProposta: soDia(emDias(30)),
  });

  // Aprovação pendente no nível 1 (para o portal do aprovador).
  const paraAprovar = await req(solicitante, '/requisicoes', 'POST', {
    centroCustoId: cc.id, justificativa: 'Compra de ferramental para a equipe noturna',
  });
  await req(solicitante, `/requisicoes/${paraAprovar.body.id}/itens`, 'POST', {
    varianteId: variante.id, quantidade: 150, precoReferencia: 60,
  });
  await req(solicitante, `/requisicoes/${paraAprovar.body.id}/submeter`, 'POST', {});
  await prisma.requisicaoCompra.update({
    where: { id: paraAprovar.body.id }, data: { status: 'APROVACAO_ALCADA' },
  });
  await req(comprador, '/aprovacoes/instancias', 'POST', {
    requisicaoId: paraAprovar.body.id, centroCustoId: cc.id,
    solicitanteId: u.solicitante, compradorId: u.comprador, valorBase: 9000,
  });

  // Pedido emitido, aguardando recebimento.
  const paraReceber = await req(solicitante, '/requisicoes', 'POST', {
    centroCustoId: cc.id, justificativa: 'Reposição do almoxarifado central',
  });
  await req(solicitante, `/requisicoes/${paraReceber.body.id}/itens`, 'POST', {
    varianteId: variante.id, quantidade: 80, precoReferencia: 25,
  });
  await req(solicitante, `/requisicoes/${paraReceber.body.id}/submeter`, 'POST', {});
  await req(comprador, `/requisicoes/${paraReceber.body.id}/assumir-triagem`, 'POST', {});
  const det = await req(comprador, `/requisicoes/${paraReceber.body.id}`);
  await prisma.requisicaoCompra.update({
    where: { id: paraReceber.body.id }, data: { status: 'APROVACAO_ALCADA' },
  });
  await prisma.instanciaAprovacao.create({
    data: {
      tenantId: tenant.id, requisicaoId: paraReceber.body.id, valorBase: 2000, nivelExigido: 1,
      regraSnapshot: { centroCustoId: cc.id, nivelFinal: 2 }, status: 'APROVADA', encerradoEm: new Date(),
    },
  });
  const pedido = await req(comprador, '/pedidos', 'POST', {
    requisicaoId: paraReceber.body.id, fornecedorId: fornecedores[0],
    itens: [{ itemRequisicaoId: det.body.itens[0].id, precoUnitario: 25 }],
    condicaoPagamento: '30/60', prazoEntrega: soDia(emDias(15)),
  });
  if (pedido.status !== 201) throw new Error(`Pedido não emitido: ${JSON.stringify(pedido.body)}`);

  console.log(`Cenário E2E pronto — tenant ${CNPJ}, pedido ${pedido.body.numero}`);
}

main()
  .catch((e) => { console.error(e); process.exitCode = 1; })
  .finally(() => prisma.$disconnect());
