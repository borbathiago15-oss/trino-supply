'use strict';

/**
 * Limpeza total do banco para testes — SOMENTE testes.
 *
 * Vive aqui, e não copiada dentro de cada teste, porque a ordem de exclusão
 * depende das FKs do schema: toda fase nova que acrescentar tabelas atualiza
 * ESTA lista e todos os testes continuam funcionando. (Foi exatamente o que
 * quebrou quando a F2 passou a referenciar core.tenant e core.centro_custo.)
 *
 * A ordem vai dos filhos para os pais.
 */
const ORDEM_EXCLUSAO = [
  // core (F8) — staging antes do lote
  'stagingLinhaImportacao',
  'loteImportacao',
  // compras (F5/F6) — dos filhos do recebimento até a cotação
  'itemRecebimento',
  'recebimento',
  'itemPedido',
  'pedidoCompra',
  'notaFiscalEntrada',
  'equalizacao',
  'itemProposta',
  'proposta',
  'conviteFornecedor',
  'rfqItem',
  'processoCotacao',
  // compras (F4) — itens antes da requisição
  'itemRequisicao',
  'requisicaoCompra',
  // compras (F3) — apontam para usuario e centro_custo com RESTRICT
  'etapaAprovacao',
  'instanciaAprovacao',
  'delegacaoAlcada',
  'aprovadorCentroCusto',
  'regraAlcada',
  // fornecimento (F2)
  'fornecedorSku',
  'certificadoAprovacao',
  'documentoFornecedor',
  'fornecedor',
  // catalogo (F2)
  'saldoEstoque',
  'parametroEstoque',
  'varianteSku',
  'skuBase',
  'tipoProduto',
  'familiaProduto',
  // auditoria (F0)
  'auditLog',
  // core (F0)
  'escopoAcesso',
  'papelPermissao',
  'papel',
  'contratoOperacao',
  'orcamentoCentroCusto',
  'centroCusto',
  'regional',
  'usuario',
  'feriado',
  'permissao',
  'tenant',
];

async function limparBancoDeTestes(prisma) {
  if (process.env.NODE_ENV === 'production') {
    throw new Error('limparBancoDeTestes não roda com NODE_ENV=production.');
  }
  for (const modelo of ORDEM_EXCLUSAO) {
    await prisma[modelo].deleteMany({});
  }
}

module.exports = { limparBancoDeTestes, ORDEM_EXCLUSAO };
