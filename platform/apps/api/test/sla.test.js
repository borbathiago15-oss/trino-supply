'use strict';

// Testes UNITÁRIOS do cálculo de tempo útil (Fase F7) — domínio puro:
//   node test/sla.test.js
// Valida travessias de fim de semana, feriado e pausas, nos dois regimes.

const {
  segundosUteis,
  calcularMetricas,
  ajustarPausaSla,
  ehDiaUtil,
  ESTADOS_COM_SLA_PAUSADO,
} = require('../dist/sla/dominio/calculadora-sla');

let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};

const H = 3600;
// Fuso zero nos testes: as horas dos instantes são as horas locais, sem conta
// mental. O offset em si é testado num bloco próprio.
const cfg = (extras = {}) => ({
  regime: 'COMERCIAL', horaInicio: 8, horaFim: 18, offsetMinutos: 0, feriados: new Set(), ...extras,
});
// 2026-06-15 é segunda-feira.
const d = (dia, hora, minuto = 0) => new Date(Date.UTC(2026, 5, dia, hora, minuto));

console.log('== janela comercial num único dia');
check('9h às 12h = 3h úteis', segundosUteis(d(15, 9), d(15, 12), cfg()) === 3 * H);
check('antes do expediente não conta (6h às 10h = 2h)', segundosUteis(d(15, 6), d(15, 10), cfg()) === 2 * H);
check('depois do expediente não conta (17h às 22h = 1h)', segundosUteis(d(15, 17), d(15, 22), cfg()) === 1 * H);
check('madrugada inteira = 0', segundosUteis(d(15, 19), d(16, 7), cfg()) === 0);
check('o dia útil cheio tem 10h', segundosUteis(d(15, 0), d(16, 0), cfg()) === 10 * H);
check('intervalo invertido vale 0, nunca negativo', segundosUteis(d(15, 12), d(15, 9), cfg()) === 0);
check('minutos contam (9:00 às 9:30 = 1800s)', segundosUteis(d(15, 9), d(15, 9, 30), cfg()) === 1800);

console.log('== atravessando o fim de semana');
// Sexta 19/06 17h → segunda 22/06 9h: 1h da sexta + 1h da segunda.
check('sexta 17h → segunda 9h = 2h úteis (não 64h corridas)',
  segundosUteis(d(19, 17), d(22, 9), cfg()) === 2 * H);
check('sábado e domingo valem 0', segundosUteis(d(20, 0), d(22, 0), cfg()) === 0);
check('semana cheia (segunda 8h → sexta 18h) = 50h úteis',
  segundosUteis(d(15, 8), d(19, 18), cfg()) === 50 * H);
check('duas semanas com um fim de semana no meio = 100h',
  segundosUteis(d(15, 8), d(26, 18), cfg()) === 100 * H);

console.log('== feriados');
const comFeriado = cfg({ feriados: new Set(['2026-06-16']) });
check('terça de feriado zera o dia (segunda 9h → quarta 9h = 10h)',
  segundosUteis(d(15, 9), d(17, 9), comFeriado) === 10 * H);
check('o mesmo intervalo sem feriado daria 20h',
  segundosUteis(d(15, 9), d(17, 9), cfg()) === 20 * H);
check('feriado em fim de semana não desconta duas vezes',
  segundosUteis(d(19, 17), d(22, 9), cfg({ feriados: new Set(['2026-06-20']) })) === 2 * H);
check('início DENTRO do feriado conta só do dia seguinte útil',
  segundosUteis(d(16, 9), d(17, 12), comFeriado) === 4 * H);
check('ehDiaUtil enxerga o feriado', !ehDiaUtil(d(16, 12), comFeriado) && ehDiaUtil(d(15, 12), comFeriado));

console.log('== regime 24/7');
const cfg247 = cfg({ regime: 'VINTE_QUATRO_SETE' });
check('24/7 conta o fim de semana inteiro (sexta 17h → segunda 9h = 64h)',
  segundosUteis(d(19, 17), d(22, 9), cfg247) === 64 * H);
check('24/7 ignora feriado', segundosUteis(d(15, 9), d(17, 9), { ...cfg247, feriados: new Set(['2026-06-16']) }) === 48 * H);

console.log('== fuso: o expediente é local, não UTC');
// Com offset -180 (Brasília), 08h local = 11h UTC.
const brasilia = cfg({ offsetMinutos: -180 });
check('11h→14h UTC são as 3 primeiras horas úteis do dia em Brasília',
  segundosUteis(d(15, 11), d(15, 14), brasilia) === 3 * H);
check('8h→11h UTC (5h→8h locais) valem 0',
  segundosUteis(d(15, 8), d(15, 11), brasilia) === 0);

console.log('== pausas: TTR desconta, TTO não');
const marcos = (extras = {}) => ({
  submetidaEm: d(15, 9), triagemEm: null, concluidaEm: null,
  slaPausadoEm: null, slaSegundosPausados: 0, ...extras,
});
const m1 = calcularMetricas(marcos({ triagemEm: d(15, 11) }), d(15, 12), cfg());
check('TTO = submissão → triagem em tempo útil (2h)', m1.tto.segundosUteis === 2 * H);
check('TTO fechado não está em andamento', m1.tto.emAndamento === false);
check('TTR aberto corre até o agora (3h)', m1.ttr.segundosUteis === 3 * H && m1.ttr.emAndamento === true);

const m2 = calcularMetricas(
  marcos({ triagemEm: d(15, 11), concluidaEm: d(16, 11), slaSegundosPausados: 2 * H }),
  d(17, 0), cfg(),
);
check('TTR fechado desconta as pausas acumuladas (12h úteis - 2h = 10h)',
  m2.ttr.segundosUteis === 10 * H, String(m2.ttr.segundosUteis));
check('TTO não desconta pausa nenhuma (as zonas de pausa começam na triagem)',
  m2.tto.segundosUteis === 2 * H);

const m3 = calcularMetricas(
  marcos({ slaPausadoEm: d(15, 10) }), d(15, 14), cfg(),
);
check('pausa em aberto desconta o trecho congelado corrente (5h - 4h = 1h)',
  m3.ttr.segundosUteis === 1 * H && m3.pausadoAgora === true);

const m4 = calcularMetricas(marcos({ submetidaEm: null }), d(15, 12), cfg());
check('sem submissão não há métrica', m4.tto.segundosUteis === null && m4.ttr.segundosUteis === null);

console.log('== pausa que atravessa o fim de semana vale só o tempo útil');
// Devolvida sexta 17h, reenviada segunda 9h: parou 2h ÚTEIS, não 64.
const ajusteResumo = ajustarPausaSla(
  'DEVOLVIDA_AJUSTE', 'SUBMETIDA',
  { slaPausadoEm: d(19, 17), slaSegundosPausados: 0 },
  d(22, 9),
  (i, f) => segundosUteis(i, f, cfg()),
);
check('a pausa de um fim de semana soma 2h úteis, não 64h corridas',
  ajusteResumo.slaSegundosPausados === 2 * H && ajusteResumo.slaPausadoEm === null,
  String(ajusteResumo.slaSegundosPausados));

console.log('== a máquina de pausa por estado');
check('os 4 estados da especificação congelam',
  ['EM_TRIAGEM', 'DEVOLVIDA_AJUSTE', 'EM_COTACAO', 'APROVACAO_ALCADA'].every((e) => ESTADOS_COM_SLA_PAUSADO.has(e)));
check('entrar na zona congela',
  ajustarPausaSla('SUBMETIDA', 'EM_TRIAGEM', { slaPausadoEm: null, slaSegundosPausados: 0 }, d(15, 9), () => 0)
    .slaPausadoEm.getTime() === d(15, 9).getTime());
check('mover-se DENTRO da zona não mexe na pausa',
  Object.keys(ajustarPausaSla('EM_TRIAGEM', 'EM_COTACAO', { slaPausadoEm: d(15, 9), slaSegundosPausados: 0 }, d(15, 10), () => 0)).length === 0);
check('sair da zona destrava e acumula',
  ajustarPausaSla('APROVACAO_ALCADA', 'PEDIDO_GERADO', { slaPausadoEm: d(15, 9), slaSegundosPausados: 100 }, d(15, 10),
    (i, f) => segundosUteis(i, f, cfg())).slaSegundosPausados === 100 + H);
check('fora da zona, nada acontece',
  Object.keys(ajustarPausaSla('RASCUNHO', 'SUBMETIDA', { slaPausadoEm: null, slaSegundosPausados: 0 }, d(15, 9), () => 0)).length === 0);

console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
process.exitCode = fail ? 1 : 0;
