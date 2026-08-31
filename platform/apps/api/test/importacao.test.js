'use strict';

// Testes UNITÁRIOS da leitura e validação de arquivos (Fase F8) — puros:
//   node test/importacao.test.js

const { lerCsv, checksumArquivo, VALIDADORES, mensagemDeErros } = require('../dist/importacao/dominio/parsers');

let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};

console.log('== checksum SHA-256 (idempotência)');
const a = checksumArquivo('codigo;descricao\nA;Item');
check('mesmo conteúdo, mesmo hash', a === checksumArquivo('codigo;descricao\nA;Item'));
check('um byte diferente muda o hash', a !== checksumArquivo('codigo;descricao\nA;Item '));
check('o hash tem 64 hex', /^[0-9a-f]{64}$/.test(a));

console.log('== leitura de CSV');
const csv = lerCsv('codigo;descricao;unidade_medida\nA4;Papel A4;CX\nLUV;Luva;PAR');
check('cabeçalho normalizado em minúsculas', csv.cabecalho.join() === 'codigo,descricao,unidade_medida');
check('duas linhas de dados', csv.linhas.length === 2);
check('a numeração é a do ARQUIVO (2 é a primeira linha de dados)',
  csv.linhas[0].numeroLinha === 2 && csv.linhas[1].numeroLinha === 3);
check('os valores casam com as colunas', csv.linhas[0].conteudo.descricao === 'Papel A4');
check('separador vírgula também funciona',
  lerCsv('codigo,descricao\nA,B').linhas[0].conteudo.descricao === 'B');
check('aspas protegem o separador',
  lerCsv('codigo;descricao\nA;"Papel; branco"').linhas[0].conteudo.descricao === 'Papel; branco');
check('aspas duplicadas viram uma aspa',
  lerCsv('codigo;descricao\nA;"Papel ""extra"""').linhas[0].conteudo.descricao === 'Papel "extra"');
check('BOM do Excel não vira parte da primeira coluna',
  lerCsv('﻿codigo;descricao\nA;B').cabecalho[0] === 'codigo');
check('linhas em branco são ignoradas', lerCsv('codigo\nA\n\n\nB').linhas.length === 2);
check('arquivo só com cabeçalho não tem linhas', lerCsv('codigo;descricao').linhas.length === 0);
check('coluna faltando vira string vazia, não quebra',
  lerCsv('codigo;descricao\nA').linhas[0].conteudo.descricao === '');

console.log('== validação: CATALOGO_SKU');
const sku = VALIDADORES.CATALOGO_SKU;
check('linha completa é válida',
  sku({ codigo_tipo: 'LUV', codigo: 'LUV-NIT', descricao: 'Luva', unidade_medida: 'PAR' }).valido);
check('unidade em minúscula é normalizada',
  sku({ codigo_tipo: 'LUV', codigo: 'X', descricao: 'Y', unidade_medida: 'par' }).dados.unidadeMedida === 'PAR');
check('exige_ca aceita "sim", "1" e "x"',
  sku({ codigo_tipo: 'T', codigo: 'X', descricao: 'Y', unidade_medida: 'UN', exige_ca: 'sim' }).dados.exigeCa === true);
check('sem variante_codigo, a variante herda o código do SKU',
  sku({ codigo_tipo: 'T', codigo: 'ABC', descricao: 'Y', unidade_medida: 'UN' }).dados.varianteCodigo === 'ABC');
const skuRuim = sku({ codigo_tipo: '', codigo: '', descricao: '', unidade_medida: 'XX' });
check('linha ruim é inválida', !skuRuim.valido);
check('TODOS os erros da linha voltam de uma vez', skuRuim.erros.length === 4, String(skuRuim.erros.length));
check('a mensagem consolidada nomeia os campos',
  mensagemDeErros(skuRuim.erros).includes('codigo:') && mensagemDeErros(skuRuim.erros).includes('unidade_medida:'));

console.log('== validação: PARAMETRO_ESTOQUE');
const par = VALIDADORES.PARAMETRO_ESTOQUE;
check('linha válida',
  par({ variante_codigo: 'A4-75', centro_custo: 'CC-01', ponto_pedido_rop: '10', estoque_seguranca: '5', lead_time_dias: '7' }).valido);
check('número em formato BR ("1.234,56") é lido',
  par({ variante_codigo: 'A', centro_custo: 'C', ponto_pedido_rop: '1.234,56', estoque_seguranca: '0', lead_time_dias: '1' })
    .dados.pontoPedidoRop === 1234.56);
check('ROP negativo é erro',
  !par({ variante_codigo: 'A', centro_custo: 'C', ponto_pedido_rop: '-1', estoque_seguranca: '0', lead_time_dias: '1' }).valido);
check('lead time fracionado é erro',
  !par({ variante_codigo: 'A', centro_custo: 'C', ponto_pedido_rop: '1', estoque_seguranca: '0', lead_time_dias: '2,5' }).valido);
check('texto no lugar de número é erro',
  !par({ variante_codigo: 'A', centro_custo: 'C', ponto_pedido_rop: 'dez', estoque_seguranca: '0', lead_time_dias: '1' }).valido);

console.log('== validação: FORNECEDOR');
const forn = VALIDADORES.FORNECEDOR;
check('CNPJ com máscara é limpo',
  forn({ cnpj: '11.111.111/0001-91', razao_social: 'Alfa' }).dados.cnpj === '11111111000191');
check('CNPJ curto é erro', !forn({ cnpj: '123', razao_social: 'Alfa' }).valido);
check('razão social vazia é erro', !forn({ cnpj: '11111111000191', razao_social: '' }).valido);
check('e-mail inválido é erro',
  !forn({ cnpj: '11111111000191', razao_social: 'A', email_contato: 'nao-e-email' }).valido);
check('e-mail vazio é aceito (campo opcional)',
  forn({ cnpj: '11111111000191', razao_social: 'A', email_contato: '' }).valido);

console.log('== validação: ORCAMENTO_CC');
const orc = VALIDADORES.ORCAMENTO_CC;
check('linha válida', orc({ centro_custo: 'CC-01', exercicio: '2026', valor_orcado: '150000,00' }).valido);
check('valor em formato BR é lido',
  orc({ centro_custo: 'C', exercicio: '2026', valor_orcado: '150.000,50' }).dados.valorOrcado === 150000.5);
check('exercício fora da faixa é erro',
  !orc({ centro_custo: 'C', exercicio: '1800', valor_orcado: '1' }).valido);
check('valor negativo é erro', !orc({ centro_custo: 'C', exercicio: '2026', valor_orcado: '-5' }).valido);

console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
process.exitCode = fail ? 1 : 0;
