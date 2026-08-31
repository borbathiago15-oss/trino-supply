'use strict';

// Testes UNITÁRIOS da matriz de equalização (Fase F5) — domínio puro, sem
// banco e sem HTTP:  node test/equalizacao.test.js

const {
  equalizar,
  assertEscolhaJustificada,
  prazoPagamentoDeCondicao,
  validarPesos,
  PESOS_PADRAO,
  EqualizacaoError,
} = require('../dist/cotacoes/dominio/equalizacao');

let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};
const codigoDe = (fn) => {
  try { fn(); return 'OK'; }
  catch (e) { return e instanceof EqualizacaoError ? e.codigo : `INESPERADO:${e.message}`; }
};

const p = (id, valorTotal, frete, prazoEntregaDias, prazoPagamentoDias) => ({
  id, fornecedorId: `f-${id}`, valorTotal, frete, prazoEntregaDias, prazoPagamentoDias,
});

console.log('== pesos');
check('os pesos padrão somam 1', codigoDe(() => validarPesos(PESOS_PADRAO)) === 'OK');
check('pesos que não somam 1 são recusados (EQL-ERR-001)',
  codigoDe(() => validarPesos({ preco: 0.5, lead_time: 0.2, frete: 0.1, cond_pagto: 0.1 })) === 'EQL-ERR-001');
check('peso negativo é recusado (EQL-ERR-002)',
  codigoDe(() => validarPesos({ preco: 1.2, lead_time: -0.2, frete: 0, cond_pagto: 0 })) === 'EQL-ERR-002');
check('equalizar sem propostas falha (EQL-ERR-003)', codigoDe(() => equalizar([])) === 'EQL-ERR-003');

console.log('== condição de pagamento vira prazo médio');
check('"30/60/90" dá 60 dias', prazoPagamentoDeCondicao('30/60/90') === 60);
check('"28 DDL" dá 28 dias', prazoPagamentoDeCondicao('28 DDL') === 28);
check('"À VISTA" dá 0 — pior caso para o caixa', prazoPagamentoDeCondicao('À VISTA') === 0);
check('texto ilegível vira 0 em vez de premiar o desconhecido', prazoPagamentoDeCondicao('a combinar') === 0);
check('nulo não quebra', prazoPagamentoDeCondicao(null) === 0);

console.log('== matriz: melhor e pior de cada critério');
// A é a mais cara mas entrega rápido; B é a mais barata e lenta; C fica no meio.
const tres = [
  p('A', 12000, 500, 5, 30),
  p('B', 10000, 300, 30, 0),
  p('C', 11000, 400, 15, 60),
];
const r = equalizar(tres, PESOS_PADRAO);
const nota = (id) => r.matriz.find((n) => n.propostaId === id);
check('a mais barata tira 1 em preço e a mais cara tira 0',
  nota('B').notas.preco === 1 && nota('A').notas.preco === 0);
check('a de menor frete tira 1 em frete', nota('B').notas.frete === 1 && nota('A').notas.frete === 0);
check('a de entrega mais rápida tira 1 em lead time',
  nota('A').notas.lead_time === 1 && nota('B').notas.lead_time === 0);
check('o maior prazo de pagamento tira 1 (melhor para o caixa)',
  nota('C').notas.cond_pagto === 1 && nota('B').notas.cond_pagto === 0);
check('a nota do meio fica proporcional, não é 0 nem 1',
  nota('C').notas.preco > 0 && nota('C').notas.preco < 1);
check('toda nota final fica entre 0 e 1',
  r.matriz.every((n) => n.notaFinal >= 0 && n.notaFinal <= 1));
check('a menor proposta é marcada como menor preço',
  r.menorPreco.propostaId === 'B' && nota('B').menorPreco === true);
check('as posições são 1, 2 e 3 sem repetição',
  r.matriz.map((n) => n.posicao).sort().join() === '1,2,3');

console.log('== o peso manda no resultado');
const soPreco = equalizar(tres, { preco: 1, lead_time: 0, frete: 0, cond_pagto: 0 });
check('com peso 100% em preço, vence a mais barata', soPreco.vencedoraPorNota.propostaId === 'B');
check('e não exige justificativa, porque é a mais barata', soPreco.exigeJustificativa === false);

const soPrazo = equalizar(tres, { preco: 0, lead_time: 1, frete: 0, cond_pagto: 0 });
check('com peso 100% em lead time, vence a mais rápida', soPrazo.vencedoraPorNota.propostaId === 'A');
check('aí a escolha DESVIA do menor preço e exige justificativa', soPrazo.exigeJustificativa === true);

const soPagamento = equalizar(tres, { preco: 0, lead_time: 0, frete: 0, cond_pagto: 1 });
check('com peso 100% em pagamento, vence o maior prazo', soPagamento.vencedoraPorNota.propostaId === 'C');

console.log('== casos de contorno');
const iguais = equalizar([p('X', 1000, 100, 10, 30), p('Y', 1000, 100, 10, 30)]);
check('empate total dá nota 1 em todos os critérios para ambas',
  iguais.matriz.every((n) => n.notaFinal === 1));
check('empate não inventa desvio (as duas são menor preço)',
  iguais.exigeJustificativa === false && iguais.matriz.every((n) => n.menorPreco));

const uma = equalizar([p('U', 5000, 200, 20, 15)]);
check('proposta única vence com nota 1', uma.vencedoraPorNota.notaFinal === 1 && uma.exigeJustificativa === false);

const empateNota = equalizar(
  [p('barata', 900, 100, 10, 30), p('cara', 1100, 100, 10, 30)],
  { preco: 0, lead_time: 0.5, frete: 0.25, cond_pagto: 0.25 },
);
check('empate na nota desempata pelo menor preço', empateNota.vencedoraPorNota.propostaId === 'barata');

console.log('== guarda da justificativa (EQL-ERR-005)');
check('escolher a mais barata dispensa justificativa',
  codigoDe(() => assertEscolhaJustificada(r, 'B', null)) === 'OK');
check('escolher outra SEM justificativa é barrado',
  codigoDe(() => assertEscolhaJustificada(r, 'A', null)) === 'EQL-ERR-005');
check('justificativa só com espaços não vale',
  codigoDe(() => assertEscolhaJustificada(r, 'A', '   ')) === 'EQL-ERR-005');
check('com justificativa, o desvio passa',
  codigoDe(() => assertEscolhaJustificada(r, 'A', 'Entrega em 5 dias atende a parada de manutenção')) === 'OK');
check('proposta fora do conjunto é erro (EQL-ERR-004)',
  codigoDe(() => assertEscolhaJustificada(r, 'inexistente', 'x')) === 'EQL-ERR-004');

console.log('== a matriz é auditável');
check('cada linha carrega as quatro notas e a final',
  r.matriz.every((n) =>
    ['preco', 'lead_time', 'frete', 'cond_pagto'].every((c) => typeof n.notas[c] === 'number') &&
    typeof n.notaFinal === 'number'));
check('a matriz guarda os pesos usados', r.pesos.preco === PESOS_PADRAO.preco);

console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
process.exitCode = fail ? 1 : 0;
