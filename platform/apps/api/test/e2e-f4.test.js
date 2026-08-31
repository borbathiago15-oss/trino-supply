'use strict';

// E2E da Fase F4 contra Postgres real + app NestJS de verdade:
//   node test/e2e-f4.test.js
// Prova a esteira ponta a ponta: criação com numeração, itens com valor
// derivado, saldo orçamentário R9, TTO, congelamento de SLA, bloqueio otimista
// por version, transições ilegais e AuditLog com snapshot em cada transição.

require('dotenv').config({ path: `${__dirname}/../.env` });
process.env.JWT_SECRET = process.env.JWT_SECRET ?? 'segredo-de-teste-trino-f4';

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
const CNPJ = '88999000000111';
let base;
const tokens = {};
const ids = {};

async function req(caminho, { method = 'GET', body, como = 'solicitante', headers = {} } = {}) {
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

async function semear() {
  const hash = await argon2.hash(SENHA, { type: argon2.argon2id });
  const tenant = await prisma.tenant.create({ data: { cnpj: CNPJ, razaoSocial: 'Grupo Trino LTDA' } });
  ids.tenant = tenant.id;
  const db = forTenant(prisma, tenant.id);

  for (const papel of ['solicitante', 'comprador', 'gestor']) {
    const u = await db.usuario.create({ data: { nome: papel, email: `${papel}@trino.com`, senhaHash: hash } });
    ids[papel] = u.id;
  }

  const regional = await db.regional.create({ data: { codigo: 'NE', nome: 'Nordeste' } });
  const cc = await db.centroCusto.create({ data: { regionalId: regional.id, codigo: 'CC-01', nome: 'Operação Recife' } });
  const ccSemOrcamento = await db.centroCusto.create({
    data: { regionalId: regional.id, codigo: 'CC-99', nome: 'Sem orçamento' },
  });
  ids.cc = cc.id;
  ids.ccSemOrcamento = ccSemOrcamento.id;

  // Orçamento do exercício corrente: R$ 10.000 livres.
  await db.orcamentoCentroCusto.create({
    data: {
      centroCustoId: cc.id,
      exercicio: new Date().getUTCFullYear(),
      valorOrcado: 12000,
      valorComprometido: 1000,
      valorRealizado: 1000,
    },
  });

  const familia = await db.familiaProduto.create({ data: { codigo: 'EPI', nome: 'EPI' } });
  const tipo = await db.tipoProduto.create({ data: { familiaId: familia.id, codigo: 'LUV', nome: 'Luvas' } });
  const sku = await db.skuBase.create({
    data: { tipoProdutoId: tipo.id, codigo: 'LUV-NIT', descricao: 'Luva nitrílica', unidadeMedida: 'PAR' },
  });
  const variante = await db.varianteSku.create({ data: { skuBaseId: sku.id, codigo: 'LUV-NIT-M', tamanho: 'M' } });
  ids.variante = variante.id;

  for (const papel of ['solicitante', 'comprador', 'gestor']) {
    const login = await fetch(`${base}/auth/login`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ cnpj: CNPJ, email: `${papel}@trino.com`, senha: SENHA }),
    }).then((r) => r.json());
    tokens[papel] = login.tokenAcesso;
  }
}

/** Cria uma requisição com um item e devolve o corpo já com itens. */
async function novaRequisicao({ quantidade = 10, preco = 100, centroCustoId = ids.cc } = {}) {
  const criada = await req('/requisicoes', {
    method: 'POST',
    body: { centroCustoId, justificativa: 'Reposição de EPI da equipe de campo' },
  });
  await req(`/requisicoes/${criada.body.id}/itens`, {
    method: 'POST',
    body: { varianteId: ids.variante, quantidade, precoReferencia: preco },
  });
  return (await req(`/requisicoes/${criada.body.id}`)).body;
}

async function main() {
  await limparBancoDeTestes(prisma);

  const app = await NestFactory.create(AppModule, { logger: false });
  app.setGlobalPrefix('api/v1');
  await app.listen(0);
  base = `${await app.getUrl()}/api/v1`.replace('[::1]', '127.0.0.1');

  try {
    await semear();

    console.log('== T01 criar e numeração');
    const primeira = await req('/requisicoes', {
      method: 'POST',
      body: { centroCustoId: ids.cc, prioridade: 'ALTA', justificativa: 'Primeira' },
    });
    check('cria em RASCUNHO', primeira.status === 201 && primeira.body.status === 'RASCUNHO');
    check('numeração RC-AAAA-NNNNNN a partir da sequência',
      /^RC-\d{4}-\d{6}$/.test(primeira.body.numero), primeira.body.numero);
    const segunda = await req('/requisicoes', { method: 'POST', body: { centroCustoId: ids.cc, justificativa: 'Segunda' } });
    check('números não se repetem', segunda.body.numero !== primeira.body.numero);
    check('o solicitante é quem criou', primeira.body.solicitanteId === ids.solicitante);
    check('centro de custo inexistente é 404',
      (await req('/requisicoes', { method: 'POST', body: { centroCustoId: '00000000-0000-4000-8000-000000000000' } })).status === 404);
    check('prioridade fora do enum é 400',
      (await req('/requisicoes', { method: 'POST', body: { centroCustoId: ids.cc, prioridade: 'URGENTISSIMA' } })).status === 400);

    console.log('== itens e valor estimado derivado');
    const comItem = await req(`/requisicoes/${primeira.body.id}/itens`, {
      method: 'POST',
      body: { varianteId: ids.variante, quantidade: 4, precoReferencia: 250 },
    });
    check('item entra com sequência 1', comItem.status === 201 && comItem.body.itens[0].sequencia === 1);
    check('valor estimado é DERIVADO dos itens (4 × 250)', Number(comItem.body.valorEstimado) === 1000,
      String(comItem.body.valorEstimado));
    const doisItens = await req(`/requisicoes/${primeira.body.id}/itens`, {
      method: 'POST',
      body: { varianteId: ids.variante, quantidade: 2, precoReferencia: 100 },
    });
    check('segundo item soma ao valor estimado', Number(doisItens.body.valorEstimado) === 1200);
    check('quantidade zero é 400',
      (await req(`/requisicoes/${primeira.body.id}/itens`, { method: 'POST', body: { varianteId: ids.variante, quantidade: 0 } })).status === 400);
    const removido = await req(`/requisicoes/${primeira.body.id}/itens/${doisItens.body.itens[1].id}`, { method: 'DELETE' });
    check('remover item recalcula o valor', Number(removido.body.valorEstimado) === 1000);
    check('a resposta traz as transições disponíveis',
      removido.body.transicoesDisponiveis.includes('T03_SUBMETER'));

    console.log('== T03 submeter: guardas e saldo orçamentário (R9)');
    const semItens = await req('/requisicoes', { method: 'POST', body: { centroCustoId: ids.cc, justificativa: 'Vazia' } });
    const submeteVazia = await req(`/requisicoes/${semItens.body.id}/submeter`, { method: 'POST', body: {} });
    check('submeter sem itens é 409 REQ-ERR-002',
      submeteVazia.status === 409 && submeteVazia.body?.codigo === 'REQ-ERR-002');

    const semJustificativa = await novaRequisicao();
    await req(`/requisicoes/${semJustificativa.id}`, { method: 'PATCH', body: { justificativa: undefined } });
    const semJust = await req('/requisicoes', { method: 'POST', body: { centroCustoId: ids.cc } });
    await req(`/requisicoes/${semJust.body.id}/itens`, { method: 'POST', body: { varianteId: ids.variante, quantidade: 1, precoReferencia: 10 } });
    const submeteSemJust = await req(`/requisicoes/${semJust.body.id}/submeter`, { method: 'POST', body: {} });
    check('submeter sem justificativa é 409 REQ-ERR-001',
      submeteSemJust.status === 409 && submeteSemJust.body?.codigo === 'REQ-ERR-001');

    // R09: estouro NÃO barra — marca e a esteira segue para o aprovador final.
    const cara = await novaRequisicao({ quantidade: 100, preco: 500 }); // R$ 50.000
    const estourada = await req(`/requisicoes/${cara.id}/submeter`, { method: 'POST', body: {} });
    check('R09 não barra o estouro: a submissão passa',
      estourada.status === 201 && estourada.body.requisicao.status === 'SUBMETIDA', JSON.stringify(estourada.body));
    check('a requisição fica marcada como orçamento estourado',
      estourada.body.requisicao.orcamentoEstourado === true);
    check('o snapshot do orçamento acompanha a requisição',
      estourada.body.requisicao.orcamentoSnapshot?.saldo === 10000 &&
      estourada.body.requisicao.orcamentoSnapshot?.excedente === 40000 &&
      estourada.body.requisicao.orcamentoSnapshot?.motivo === 'SALDO_INSUFICIENTE',
      JSON.stringify(estourada.body.requisicao.orcamentoSnapshot));

    const semOrcamento = await novaRequisicao({ centroCustoId: ids.ccSemOrcamento, quantidade: 1, preco: 5 });
    const semTeto = await req(`/requisicoes/${semOrcamento.id}/submeter`, { method: 'POST', body: {} });
    check('centro de custo sem orçamento do exercício também segue, marcado',
      semTeto.status === 201 && semTeto.body.requisicao.orcamentoEstourado === true &&
      semTeto.body.requisicao.orcamentoSnapshot?.motivo === 'SEM_ORCAMENTO',
      JSON.stringify(semTeto.body.requisicao.orcamentoSnapshot));
    check('a fila de análise lista as requisições estouradas',
      (await req('/requisicoes?orcamentoEstourado=true')).body.length >= 2);

    const boa = await novaRequisicao({ quantidade: 10, preco: 100 }); // R$ 1.000
    const submetida = await req(`/requisicoes/${boa.id}/submeter`, { method: 'POST', body: {} });
    check('submissão dentro do saldo passa e vira SUBMETIDA',
      submetida.status === 201 && submetida.body.requisicao.status === 'SUBMETIDA', JSON.stringify(submetida.body));
    check('dentro do saldo não marca estouro', submetida.body.requisicao.orcamentoEstourado === false);
    check('submetida_em foi carimbada', submetida.body.requisicao.submetidaEm !== null);
    check('version subiu no bloqueio otimista', submetida.body.requisicao.version > boa.version);

    console.log('== transições ilegais pela API');
    const reSubmeter = await req(`/requisicoes/${boa.id}/submeter`, { method: 'POST', body: {} });
    check('submeter de novo é 409 REQ-ERR-TRANSICAO',
      reSubmeter.status === 409 && reSubmeter.body?.codigo === 'REQ-ERR-TRANSICAO', JSON.stringify(reSubmeter.body));
    check('a resposta diz o estado atual e os permitidos',
      reSubmeter.body?.estadoAtual === 'SUBMETIDA' && Array.isArray(reSubmeter.body?.estadosPermitidos));
    check('reenviar sem ter sido devolvida é ilegal',
      (await req(`/requisicoes/${boa.id}/reenviar`, { method: 'POST', body: {} })).body?.codigo === 'REQ-ERR-TRANSICAO');
    check('itens não podem mudar depois de submetida (REQ-ERR-010)',
      (await req(`/requisicoes/${boa.id}/itens`, { method: 'POST', body: { varianteId: ids.variante, quantidade: 1 } })).body?.codigo === 'REQ-ERR-010');

    console.log('== T05 assumir triagem e TTO');
    const proprioSolicitante = await req(`/requisicoes/${boa.id}/assumir-triagem`, { method: 'POST', body: {} });
    check('o solicitante não assume a própria triagem (REQ-ERR-004)',
      proprioSolicitante.status === 409 && proprioSolicitante.body?.codigo === 'REQ-ERR-004');

    const triagem = await req(`/requisicoes/${boa.id}/assumir-triagem`, { method: 'POST', como: 'comprador', body: {} });
    check('comprador assume e a requisição vai para EM_TRIAGEM',
      triagem.status === 201 && triagem.body.requisicao.status === 'EM_TRIAGEM');
    check('comprador_id gravado', triagem.body.requisicao.compradorId === ids.comprador);
    check('TTO calculado e devolvido', typeof triagem.body.ttoSegundos === 'number' && triagem.body.ttoSegundos >= 0,
      String(triagem.body.ttoSegundos));
    check('triagem_em carimbada (fim do TTO)', triagem.body.requisicao.triagemEm !== null);

    console.log('== T06 devolver para ajuste congela o SLA');
    const semMotivo = await req(`/requisicoes/${boa.id}/devolver`, { method: 'POST', como: 'comprador', body: {} });
    check('devolver sem motivo é 400 (class-validator)', semMotivo.status === 400);

    const devolvida = await req(`/requisicoes/${boa.id}/devolver`, {
      method: 'POST', como: 'comprador', body: { motivo: 'Informe o contrato da operação' },
    });
    check('devolve para DEVOLVIDA_AJUSTE com motivo',
      devolvida.status === 201 && devolvida.body.requisicao.status === 'DEVOLVIDA_AJUSTE' &&
      devolvida.body.requisicao.motivoRecusa === 'Informe o contrato da operação');
    check('SLA congelado (sla_pausado_em preenchido)', devolvida.body.requisicao.slaPausadoEm !== null);
    check('itens voltam a ser editáveis em DEVOLVIDA_AJUSTE',
      (await req(`/requisicoes/${boa.id}/itens`, { method: 'POST', body: { varianteId: ids.variante, quantidade: 1, precoReferencia: 50 } })).status === 201);

    console.log('== T08 reenviar destrava o SLA');
    const reenviada = await req(`/requisicoes/${boa.id}/reenviar`, { method: 'POST', body: {} });
    check('volta para SUBMETIDA', reenviada.status === 201 && reenviada.body.requisicao.status === 'SUBMETIDA');
    check('SLA destravado', reenviada.body.requisicao.slaPausadoEm === null);
    check('tempo parado foi contabilizado', reenviada.body.requisicao.slaSegundosPausados >= 0);

    console.log('== bloqueio otimista por version');
    const atual = (await req(`/requisicoes/${boa.id}`)).body;
    const versaoVelha = await req(`/requisicoes/${boa.id}/assumir-triagem`, {
      method: 'POST', como: 'comprador', body: { version: atual.version - 1 },
    });
    check('version antiga é 409 REQ-ERR-409',
      versaoVelha.status === 409 && versaoVelha.body?.codigo === 'REQ-ERR-409', JSON.stringify(versaoVelha.body));
    check('a resposta informa a versão atual', versaoVelha.body?.versionAtual === atual.version);
    const versaoCerta = await req(`/requisicoes/${boa.id}/assumir-triagem`, {
      method: 'POST', como: 'comprador', body: { version: atual.version },
    });
    check('version correta passa', versaoCerta.status === 201);

    console.log('== concorrência: duas transições simultâneas');
    const disputada = await novaRequisicao({ quantidade: 1, preco: 100 });
    await req(`/requisicoes/${disputada.id}/submeter`, { method: 'POST', body: {} });
    const [a, b] = await Promise.all([
      req(`/requisicoes/${disputada.id}/assumir-triagem`, { method: 'POST', como: 'comprador', body: {} }),
      req(`/requisicoes/${disputada.id}/assumir-triagem`, { method: 'POST', como: 'gestor', body: {} }),
    ]);
    const status = [a.status, b.status].sort();
    check('só uma das duas assunções simultâneas passa',
      status[0] === 201 && status[1] === 409, JSON.stringify({ a: a.status, b: b.status }));
    const depoisDaDisputa = await prisma.requisicaoCompra.findUnique({ where: { id: disputada.id } });
    check('a requisição tem um único comprador',
      [ids.comprador, ids.gestor].includes(depoisDaDisputa.compradorId) && depoisDaDisputa.status === 'EM_TRIAGEM');

    console.log('== T09 rejeitar e T04 cancelar');
    const paraRejeitar = await novaRequisicao({ quantidade: 1, preco: 100 });
    await req(`/requisicoes/${paraRejeitar.id}/submeter`, { method: 'POST', body: {} });
    const rejeitada = await req(`/requisicoes/${paraRejeitar.id}/rejeitar`, {
      method: 'POST', como: 'comprador', body: { motivo: 'Compra sem cobertura orçamentária' },
    });
    check('rejeita com motivo e conclui',
      rejeitada.status === 201 && rejeitada.body.requisicao.status === 'REJEITADA' &&
      rejeitada.body.requisicao.concluidaEm !== null);
    check('requisição rejeitada não aceita mais nada',
      (await req(`/requisicoes/${paraRejeitar.id}/cancelar`, { method: 'POST', body: {} })).body?.codigo === 'REQ-ERR-TRANSICAO');

    const paraCancelar = await novaRequisicao({ quantidade: 1, preco: 100 });
    const cancelada = await req(`/requisicoes/${paraCancelar.id}/cancelar`, { method: 'POST', body: { motivo: 'Duplicada' } });
    check('cancela um rascunho', cancelada.status === 201 && cancelada.body.requisicao.status === 'CANCELADA');

    console.log('== T10 enviar para cotação');
    const paraCotar = await novaRequisicao({ quantidade: 2, preco: 100 });
    await req(`/requisicoes/${paraCotar.id}/submeter`, { method: 'POST', body: {} });
    check('sem comprador não vai a cotação (estado errado)',
      (await req(`/requisicoes/${paraCotar.id}/enviar-cotacao`, { method: 'POST', body: {} })).body?.codigo === 'REQ-ERR-TRANSICAO');
    await req(`/requisicoes/${paraCotar.id}/assumir-triagem`, { method: 'POST', como: 'comprador', body: {} });
    const emCotacao = await req(`/requisicoes/${paraCotar.id}/enviar-cotacao`, { method: 'POST', como: 'comprador', body: {} });
    check('vai para EM_COTACAO', emCotacao.status === 201 && emCotacao.body.requisicao.status === 'EM_COTACAO');

    console.log('== auditoria de cada transição');
    const logs = await prisma.auditLog.findMany({
      where: { entidade: 'requisicao_compra', entityId: boa.id },
      orderBy: { timestampUtc: 'asc' },
    });
    check('cada transição gerou um AuditLog', logs.length >= 4, `logs=${logs.length}`);
    check('as ações são os códigos das transições',
      logs.every((l) => /^T\d{2}_/.test(l.acao)), logs.map((l) => l.acao).join(','));
    const logSubmeter = logs.find((l) => l.acao === 'T03_SUBMETER');
    check('o snapshot guarda o antes RASCUNHO e o depois SUBMETIDA',
      logSubmeter?.beforeJson?.status === 'RASCUNHO' && logSubmeter?.afterJson?.status === 'SUBMETIDA');
    check('o snapshot registra a version antes e depois',
      logSubmeter?.afterJson?.version === logSubmeter?.beforeJson?.version + 1);
    // F7: o congelamento é automático por estado — começa ao ENTRAR em
    // EM_TRIAGEM (T05), e a devolução apenas o mantém.
    const logTriagemPausa = logs.find((l) => l.acao === 'T05_ASSUMIR_TRIAGEM');
    check('a auditoria mostra o SLA congelando na entrada da triagem (F7)',
      logTriagemPausa?.beforeJson?.slaPausadoEm === null && logTriagemPausa?.afterJson?.slaPausadoEm !== null);
    const logDevolver = logs.find((l) => l.acao === 'T06_DEVOLVER_AJUSTE');
    check('a devolução mantém o SLA congelado',
      logDevolver?.afterJson?.slaPausadoEm !== null);
    check('o ator e o tenant vêm do token',
      logs.every((l) => !!l.actorId && l.tenantId === ids.tenant));
    const logTriagem = logs.find((l) => l.acao === 'T05_ASSUMIR_TRIAGEM');
    check('a triagem auditada registra o comprador que assumiu',
      logTriagem?.afterJson?.compradorId === ids.comprador && logTriagem?.beforeJson?.compradorId === null);
    check('transições barradas não viraram log',
      logs.filter((l) => l.acao === 'T03_SUBMETER').length === 1);

    console.log('== isolamento e filtros');
    check('filtro por status funciona',
      (await req('/requisicoes?status=CANCELADA')).body.every((r) => r.status === 'CANCELADA'));
    check('filtro por centro de custo funciona',
      (await req(`/requisicoes?centroCustoId=${ids.ccSemOrcamento}`)).body.every((r) => r.centroCustoId === ids.ccSemOrcamento));
    check('requisição inexistente é 404',
      (await req('/requisicoes/00000000-0000-4000-8000-000000000000')).status === 404);
  } finally {
    await app.close();
  }

  console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
  process.exitCode = fail ? 1 : 0;
}

main()
  .catch((e) => { console.error(e); process.exitCode = 1; })
  .finally(() => prisma.$disconnect());
