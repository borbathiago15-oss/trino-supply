'use strict';

// Testes UNITÁRIOS da máquina de estados da requisição (Fase F4) — domínio
// puro, sem banco e sem HTTP:
//   node test/maquina-estados.test.js
// Cobre as transições T01 a T10 válidas, garante que TODA transição ilegal
// lança TransicaoNaoPermitidaException, e checa guardas, bloqueio otimista,
// congelamento de SLA e cálculo do TTO.

const {
  TRANSICOES,
  aplicarTransicao,
  assertTransicaoPermitida,
  transicoesDisponiveis,
  calcularTtoSegundos,
  TransicaoNaoPermitidaException,
  GuardaViolada,
  ConflitoDeVersao,
} = require('../dist/requisicoes/dominio/maquina-estados');

let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};

const AGORA = new Date('2026-06-15T12:00:00.000Z');
const SOLICITANTE = 'u-solicitante';
const COMPRADOR = 'u-comprador';

const estado = (extras = {}) => ({
  id: 'r1',
  status: 'RASCUNHO',
  solicitanteId: SOLICITANTE,
  compradorId: null,
  justificativa: 'Reposição de EPI da equipe de campo',
  motivoRecusa: null,
  valorEstimado: 1200,
  version: 3,
  submetidaEm: null,
  triagemEm: null,
  concluidaEm: null,
  slaPausadoEm: null,
  slaSegundosPausados: 0,
  totalItensAtivos: 2,
  ...extras,
});

/** Devolve o código do erro, o nome da exceção, ou 'OK'. */
function erroDe(fn) {
  try { fn(); return 'OK'; }
  catch (e) {
    if (e instanceof TransicaoNaoPermitidaException) return 'TransicaoNaoPermitida';
    if (e instanceof GuardaViolada) return e.codigo;
    if (e instanceof ConflitoDeVersao) return e.codigo;
    return `INESPERADO:${e.message}`;
  }
}

const TODOS_ESTADOS = [
  'RASCUNHO', 'SUBMETIDA', 'EM_TRIAGEM', 'DEVOLVIDA_AJUSTE', 'EM_COTACAO', 'COTADA',
  'APROVACAO_ALCADA', 'PEDIDO_GERADO', 'RECEBIDA_PARCIAL', 'RECEBIDA_TOTAL', 'REJEITADA', 'CANCELADA',
];

console.log('== a tabela de transições é a especificação');
check('as 15 transições da esteira estão declaradas', Object.keys(TRANSICOES).length === 15,
  String(Object.keys(TRANSICOES).length));
check('T01 nasce em RASCUNHO', TRANSICOES.T01_CRIAR.para === 'RASCUNHO' && TRANSICOES.T01_CRIAR.de.length === 0);
check('do RASCUNHO saem editar, submeter e cancelar',
  transicoesDisponiveis('RASCUNHO').sort().join() === ['T02_EDITAR', 'T03_SUBMETER', 'T04_CANCELAR'].sort().join(),
  transicoesDisponiveis('RASCUNHO').join());
check('estado terminal não oferece transição',
  transicoesDisponiveis('CANCELADA').length === 0 && transicoesDisponiveis('REJEITADA').length === 0);
check('PEDIDO_GERADO não é mais cancelável, só recebido',
  transicoesDisponiveis('PEDIDO_GERADO').sort().join() === ['T17_RECEBER_PARCIAL', 'T18_RECEBER_TOTAL'].sort().join(),
  transicoesDisponiveis('PEDIDO_GERADO').join());
check('RECEBIDA_TOTAL é terminal', transicoesDisponiveis('RECEBIDA_TOTAL').length === 0);
check('recebimento parcial aceita nova remessa',
  transicoesDisponiveis('RECEBIDA_PARCIAL').includes('T18_RECEBER_TOTAL'));

console.log('== transições válidas (caminho feliz da esteira)');
const submetida = aplicarTransicao(estado(), { transicao: 'T03_SUBMETER', agora: AGORA });
check('T03 submeter leva a SUBMETIDA e carimba submetida_em',
  submetida.status === 'SUBMETIDA' && submetida.submetidaEm.getTime() === AGORA.getTime());

const triagem = aplicarTransicao(
  estado({ status: 'SUBMETIDA', submetidaEm: new Date('2026-06-15T11:00:00.000Z') }),
  { transicao: 'T05_ASSUMIR_TRIAGEM', agora: AGORA, compradorId: COMPRADOR },
);
check('T05 assumir triagem grava o comprador e fecha o TTO',
  triagem.status === 'EM_TRIAGEM' && triagem.compradorId === COMPRADOR && triagem.triagemEm.getTime() === AGORA.getTime());

const devolvida = aplicarTransicao(
  estado({ status: 'EM_TRIAGEM', compradorId: COMPRADOR }),
  { transicao: 'T06_DEVOLVER_AJUSTE', agora: AGORA, motivo: 'Faltou o centro de custo correto' },
);
check('T06 devolver registra o motivo e CONGELA o SLA',
  devolvida.status === 'DEVOLVIDA_AJUSTE' &&
  devolvida.motivoRecusa === 'Faltou o centro de custo correto' &&
  devolvida.slaPausadoEm.getTime() === AGORA.getTime());

const reenviada = aplicarTransicao(
  estado({
    status: 'DEVOLVIDA_AJUSTE',
    submetidaEm: new Date('2026-06-15T10:00:00.000Z'),
    slaPausadoEm: new Date('2026-06-15T11:00:00.000Z'),
    slaSegundosPausados: 120,
  }),
  { transicao: 'T08_REENVIAR', agora: AGORA },
);
check('T08 reenviar volta para SUBMETIDA e DESTRAVA o SLA somando o tempo parado',
  reenviada.status === 'SUBMETIDA' && reenviada.slaPausadoEm === null && reenviada.slaSegundosPausados === 120 + 3600,
  JSON.stringify({ pausado: reenviada.slaPausadoEm, segundos: reenviada.slaSegundosPausados }));
check('reenviar preserva a submetida_em original (não reinicia o relógio)',
  reenviada.submetidaEm.toISOString() === '2026-06-15T10:00:00.000Z');

const rejeitada = aplicarTransicao(
  estado({ status: 'EM_TRIAGEM' }),
  { transicao: 'T09_REJEITAR', agora: AGORA, motivo: 'Compra sem cobertura orçamentária' },
);
check('T09 rejeitar encerra com motivo e concluida_em',
  rejeitada.status === 'REJEITADA' && !!rejeitada.motivoRecusa && rejeitada.concluidaEm.getTime() === AGORA.getTime());

const cancelada = aplicarTransicao(estado({ status: 'SUBMETIDA' }), { transicao: 'T04_CANCELAR', agora: AGORA });
check('T04 cancelar encerra a requisição', cancelada.status === 'CANCELADA' && !!cancelada.concluidaEm);

const emCotacao = aplicarTransicao(
  estado({ status: 'EM_TRIAGEM', compradorId: COMPRADOR }),
  { transicao: 'T10_ENVIAR_COTACAO', agora: AGORA },
);
check('T10 enviar para cotação', emCotacao.status === 'EM_COTACAO');

const editado = aplicarTransicao(estado(), { transicao: 'T02_EDITAR', agora: AGORA, justificativa: '  Nova justificativa  ' });
check('T02 editar não muda o estado e apara a justificativa',
  editado.status === undefined && editado.justificativa === 'Nova justificativa');

const editadoEmAjuste = aplicarTransicao(
  estado({ status: 'DEVOLVIDA_AJUSTE' }),
  { transicao: 'T07_EDITAR_EM_AJUSTE', agora: AGORA, justificativa: 'Corrigido' },
);
check('T07 editar em ajuste mantém DEVOLVIDA_AJUSTE', editadoEmAjuste.status === undefined);

console.log('== TODA transição ilegal lança TransicaoNaoPermitidaException');
let ilegaisTestadas = 0;
let ilegaisCorretas = 0;
for (const transicao of Object.keys(TRANSICOES)) {
  for (const status of TODOS_ESTADOS) {
    if (TRANSICOES[transicao].de.includes(status)) continue;
    ilegaisTestadas += 1;
    try {
      assertTransicaoPermitida(transicao, status);
    } catch (e) {
      if (e instanceof TransicaoNaoPermitidaException) ilegaisCorretas += 1;
    }
  }
}
check(`as ${ilegaisTestadas} combinações ilegais lançam a exceção certa`,
  ilegaisTestadas > 0 && ilegaisCorretas === ilegaisTestadas, `corretas=${ilegaisCorretas}`);

check('submeter algo já submetido é ilegal',
  erroDe(() => aplicarTransicao(estado({ status: 'SUBMETIDA' }), { transicao: 'T03_SUBMETER', agora: AGORA })) === 'TransicaoNaoPermitida');
check('assumir triagem de rascunho é ilegal',
  erroDe(() => aplicarTransicao(estado(), { transicao: 'T05_ASSUMIR_TRIAGEM', agora: AGORA, compradorId: COMPRADOR })) === 'TransicaoNaoPermitida');
check('cancelar requisição já cancelada é ilegal',
  erroDe(() => aplicarTransicao(estado({ status: 'CANCELADA' }), { transicao: 'T04_CANCELAR', agora: AGORA })) === 'TransicaoNaoPermitida');
check('reenviar sem ter sido devolvida é ilegal',
  erroDe(() => aplicarTransicao(estado({ status: 'SUBMETIDA' }), { transicao: 'T08_REENVIAR', agora: AGORA })) === 'TransicaoNaoPermitida');
check('editar depois de submetida é ilegal',
  erroDe(() => aplicarTransicao(estado({ status: 'SUBMETIDA' }), { transicao: 'T02_EDITAR', agora: AGORA })) === 'TransicaoNaoPermitida');
check('rejeitar o que já foi rejeitado é ilegal',
  erroDe(() => aplicarTransicao(estado({ status: 'REJEITADA' }), { transicao: 'T09_REJEITAR', agora: AGORA, motivo: 'x' })) === 'TransicaoNaoPermitida');
check('a exceção diz de onde saiu e o que era esperado', (() => {
  try { assertTransicaoPermitida('T05_ASSUMIR_TRIAGEM', 'RASCUNHO'); return false; }
  catch (e) { return e.estadoAtual === 'RASCUNHO' && e.estadosPermitidos.includes('SUBMETIDA') && e.codigo === 'REQ-ERR-TRANSICAO'; }
})());

console.log('== guardas');
check('submeter sem justificativa é barrado (REQ-ERR-001)',
  erroDe(() => aplicarTransicao(estado({ justificativa: null }), { transicao: 'T03_SUBMETER', agora: AGORA })) === 'REQ-ERR-001');
check('justificativa só com espaços não vale',
  erroDe(() => aplicarTransicao(estado({ justificativa: '   ' }), { transicao: 'T03_SUBMETER', agora: AGORA })) === 'REQ-ERR-001');
check('a justificativa do comando supre a que falta no estado',
  erroDe(() => aplicarTransicao(estado({ justificativa: null }), { transicao: 'T03_SUBMETER', agora: AGORA, justificativa: 'Agora tem' })) === 'OK');
check('submeter sem itens é barrado (REQ-ERR-002)',
  erroDe(() => aplicarTransicao(estado({ totalItensAtivos: 0 }), { transicao: 'T03_SUBMETER', agora: AGORA })) === 'REQ-ERR-002');
check('reenviar sem itens também é barrado',
  erroDe(() => aplicarTransicao(estado({ status: 'DEVOLVIDA_AJUSTE', totalItensAtivos: 0 }), { transicao: 'T08_REENVIAR', agora: AGORA })) === 'REQ-ERR-002');
check('assumir triagem sem comprador é barrado (REQ-ERR-003)',
  erroDe(() => aplicarTransicao(estado({ status: 'SUBMETIDA' }), { transicao: 'T05_ASSUMIR_TRIAGEM', agora: AGORA })) === 'REQ-ERR-003');
check('o solicitante não assume a própria triagem (REQ-ERR-004)',
  erroDe(() => aplicarTransicao(estado({ status: 'SUBMETIDA' }), { transicao: 'T05_ASSUMIR_TRIAGEM', agora: AGORA, compradorId: SOLICITANTE })) === 'REQ-ERR-004');
check('devolver sem motivo é barrado (REQ-ERR-005)',
  erroDe(() => aplicarTransicao(estado({ status: 'EM_TRIAGEM' }), { transicao: 'T06_DEVOLVER_AJUSTE', agora: AGORA })) === 'REQ-ERR-005');
check('rejeitar sem motivo é barrado (REQ-ERR-006)',
  erroDe(() => aplicarTransicao(estado({ status: 'EM_TRIAGEM' }), { transicao: 'T09_REJEITAR', agora: AGORA, motivo: '  ' })) === 'REQ-ERR-006');
check('ir a cotação sem comprador é barrado (REQ-ERR-007)',
  erroDe(() => aplicarTransicao(estado({ status: 'EM_TRIAGEM', compradorId: null }), { transicao: 'T10_ENVIAR_COTACAO', agora: AGORA })) === 'REQ-ERR-007');

console.log('== bloqueio otimista');
check('versão divergente é conflito (REQ-ERR-409)',
  erroDe(() => aplicarTransicao(estado({ version: 7 }), { transicao: 'T03_SUBMETER', agora: AGORA, versionEsperada: 3 })) === 'REQ-ERR-409');
check('versão igual passa',
  erroDe(() => aplicarTransicao(estado({ version: 7 }), { transicao: 'T03_SUBMETER', agora: AGORA, versionEsperada: 7 })) === 'OK');
check('sem versão informada, não há checagem',
  erroDe(() => aplicarTransicao(estado({ version: 7 }), { transicao: 'T03_SUBMETER', agora: AGORA })) === 'OK');
check('transição ilegal responde "ilegal" mesmo com versão velha',
  erroDe(() => aplicarTransicao(estado({ status: 'CANCELADA', version: 9 }), { transicao: 'T03_SUBMETER', agora: AGORA, versionEsperada: 1 })) === 'TransicaoNaoPermitida');

console.log('== TTO (tempo até o atendimento)');
check('TTO é null antes da triagem', calcularTtoSegundos({ submetidaEm: AGORA, triagemEm: null, slaSegundosPausados: 0 }) === null);
check('TTO é null se nunca foi submetida', calcularTtoSegundos({ submetidaEm: null, triagemEm: AGORA, slaSegundosPausados: 0 }) === null);
check('TTO de uma hora dá 3600s', calcularTtoSegundos({
  submetidaEm: new Date('2026-06-15T11:00:00.000Z'), triagemEm: AGORA, slaSegundosPausados: 0,
}) === 3600);
check('TTO desconta o tempo em que o SLA ficou congelado', calcularTtoSegundos({
  submetidaEm: new Date('2026-06-15T11:00:00.000Z'), triagemEm: AGORA, slaSegundosPausados: 600,
}) === 3000);
check('TTO nunca fica negativo', calcularTtoSegundos({
  submetidaEm: new Date('2026-06-15T11:00:00.000Z'), triagemEm: AGORA, slaSegundosPausados: 99999,
}) === 0);

console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
process.exitCode = fail ? 1 : 0;
