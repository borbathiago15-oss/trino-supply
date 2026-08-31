'use strict';

// Testes UNITÁRIOS da política de aprovação (Fase F3) — domínio puro, sem
// banco e sem HTTP:
//   node test/politica-aprovacao.test.js
// Cobre CT-01 a CT-08 (fronteiras de R$ 5k e R$ 25k, auto-aprovação e
// delegação) mais os bloqueios B1–B5 e os erros de estado.

const { assertPodeAprovar } = require('../dist/aprovacoes/dominio/politica-aprovacao');
const { nivelExigidoPara, regraParaValor, AlcadaError } = require('../dist/aprovacoes/dominio/faixas-alcada');

let ok = 0, fail = 0;
const check = (label, cond, extra = '') => {
  if (cond) { ok++; console.log(`  OK   ${label}`); }
  else { fail++; console.log(`  FAIL ${label} ${extra}`); }
};

/** Executa e devolve o código do bloqueio, ou 'OK' quando passa. */
function codigoDe(fn) {
  try { fn(); return 'OK'; }
  catch (e) { return e instanceof AlcadaError ? e.codigo : `INESPERADO:${e.message}`; }
}

const AGORA = new Date('2026-06-15T12:00:00.000Z');
const dia = (iso) => new Date(`${iso}T00:00:00.000Z`);

// Alçada de três degraus: 0–5k (N1), 5k–25k (N2), 25k+ (N3).
const REGRAS = [
  { id: 'r1', nivel: 1, papelExigido: 'GESTOR', valorMin: 0, valorMax: 5000, vigenciaInicio: dia('2026-01-01'), vigenciaFim: null },
  { id: 'r2', nivel: 2, papelExigido: 'GERENTE', valorMin: 5000, valorMax: 25000, vigenciaInicio: dia('2026-01-01'), vigenciaFim: null },
  { id: 'r3', nivel: 3, papelExigido: 'DIRETOR', valorMin: 25000, valorMax: null, vigenciaInicio: dia('2026-01-01'), vigenciaFim: null },
];

const SOLICITANTE = 'u-solicitante';
const COMPRADOR = 'u-comprador';
const GESTOR = 'u-gestor';
const GERENTE = 'u-gerente';
const DIRETOR = 'u-diretor';
const SUPLENTE = 'u-suplente';
const CC = 'cc-recife';
const OUTRO_CC = 'cc-natal';

const atribuicao = (usuarioId, nivel, extras = {}) => ({
  usuarioId,
  centroCustoId: CC,
  nivel,
  vigenciaInicio: dia('2026-01-01'),
  vigenciaFim: null,
  ...extras,
});

const ATRIBUICOES = [
  atribuicao(GESTOR, 1),
  atribuicao(GERENTE, 2),
  atribuicao(DIRETOR, 3),
];

const delegacao = (extras = {}) => ({
  id: 'd1',
  deleganteId: GERENTE,
  delegadoId: SUPLENTE,
  centroCustoId: CC,
  ativa: true,
  vigenciaInicio: new Date('2026-06-01T00:00:00.000Z'),
  vigenciaFim: new Date('2026-06-30T23:59:59.000Z'),
  ...extras,
});

/**
 * Contexto de uma instância de dois níveis (valor na faixa N2), com a etapa 1
 * já aprovada pelo gestor e a etapa 2 em aberto — o cenário mais comum.
 */
function contexto(extras = {}) {
  const etapa = {
    id: 'e2',
    nivel: 2,
    solicitanteId: SOLICITANTE,
    compradorId: COMPRADOR,
    decisao: 'PENDENTE',
    ...(extras.etapa ?? {}),
  };
  return {
    agora: AGORA,
    usuarioId: GERENTE,
    centroCustoId: CC,
    instancia: { id: 'i1', status: 'PENDENTE', nivelExigido: 2, ...(extras.instancia ?? {}) },
    etapa,
    etapasDaInstancia: extras.etapasDaInstancia ?? [
      { id: 'e1', nivel: 1, aprovadorId: GESTOR, decisao: 'APROVADO' },
      { id: etapa.id, nivel: etapa.nivel, aprovadorId: null, decisao: etapa.decisao },
    ],
    atribuicoes: extras.atribuicoes ?? ATRIBUICOES,
    delegacoes: extras.delegacoes ?? [],
    ...(extras.raiz ?? {}),
    ...(extras.usuarioId ? { usuarioId: extras.usuarioId } : {}),
  };
}

console.log('== CT-01 a CT-04: fronteiras de faixa (R$ 5k e R$ 25k)');
check('CT-01 R$ 4.999,99 exige nível 1', nivelExigidoPara(4999.99, REGRAS, AGORA) === 1);
check('CT-02 R$ 5.000,00 exige nível 2 (mínimo é inclusivo)', nivelExigidoPara(5000, REGRAS, AGORA) === 2);
check('CT-03 R$ 24.999,99 exige nível 2', nivelExigidoPara(24999.99, REGRAS, AGORA) === 2);
check('CT-04 R$ 25.000,00 exige nível 3 (teto é exclusivo)', nivelExigidoPara(25000, REGRAS, AGORA) === 3);
check('valor zero cai no nível 1', nivelExigidoPara(0, REGRAS, AGORA) === 1);
check('valor acima do topo continua no nível 3 (sem teto)', nivelExigidoPara(9999999, REGRAS, AGORA) === 3);
check('a regra devolvida é a do nível certo', regraParaValor(5000, REGRAS, AGORA).papelExigido === 'GERENTE');
check('regra fora de vigência não conta',
  codigoDe(() => nivelExigidoPara(100, REGRAS, new Date('2025-12-31T12:00:00Z'))) === 'ALC-ERR-001');
check('valor sem faixa configurada falha explicitamente',
  codigoDe(() => nivelExigidoPara(100, [REGRAS[2]], AGORA)) === 'ALC-ERR-001');

console.log('== CT-05: bloqueio de auto-aprovação (B1 e B2)');
check('CT-05a solicitante não aprova a própria requisição (APV-B1)',
  codigoDe(() => assertPodeAprovar(contexto({ usuarioId: SOLICITANTE }))) === 'APV-B1');
check('CT-05b comprador não aprova o que conduz (APV-B2)',
  codigoDe(() => assertPodeAprovar(contexto({ usuarioId: COMPRADOR }))) === 'APV-B2');
check('B1 vale mesmo se o solicitante tiver alçada no nível',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SOLICITANTE,
    atribuicoes: [...ATRIBUICOES, atribuicao(SOLICITANTE, 2)],
  }))) === 'APV-B1');
check('sem comprador definido, B2 não atrapalha o aprovador legítimo',
  codigoDe(() => assertPodeAprovar(contexto({ etapa: { compradorId: null } }))) === 'OK');

console.log('== CT-06: aprovador legítimo passa');
const autorizado = assertPodeAprovar(contexto());
check('CT-06 gerente com alçada vigente aprova o nível 2',
  autorizado.aprovadorId === GERENTE && autorizado.viaDelegacao === false && autorizado.nivel === 2);
check('quem não tem alçada no nível é barrado (APV-B5)',
  codigoDe(() => assertPodeAprovar(contexto({ usuarioId: DIRETOR }))) === 'APV-B5');
check('alçada em OUTRO centro de custo não serve (APV-B5)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    atribuicoes: [...ATRIBUICOES, atribuicao(SUPLENTE, 2, { centroCustoId: OUTRO_CC })],
  }))) === 'APV-B5');
check('alçada expirada não serve (APV-B5)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    atribuicoes: [...ATRIBUICOES, atribuicao(SUPLENTE, 2, { vigenciaFim: dia('2026-05-31') })],
  }))) === 'APV-B5');
check('alçada que começa amanhã ainda não serve (APV-B5)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    atribuicoes: [...ATRIBUICOES, atribuicao(SUPLENTE, 2, { vigenciaInicio: dia('2026-07-01') })],
  }))) === 'APV-B5');
check('alçada que vence hoje ainda vale (fronteira inclusiva)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    atribuicoes: [...ATRIBUICOES, atribuicao(SUPLENTE, 2, { vigenciaFim: dia('2026-06-15') })],
  }))) === 'OK');

console.log('== CT-07: mesmo usuário não aprova dois níveis (B4)');
check('CT-07 quem já decidiu o nível 1 não decide o nível 2 (APV-B4)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: GESTOR,
    atribuicoes: [...ATRIBUICOES, atribuicao(GESTOR, 2)],
  }))) === 'APV-B4');
check('B4 não confunde etapa ainda pendente com decidida',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: GERENTE,
    etapasDaInstancia: [
      { id: 'e1', nivel: 1, aprovadorId: null, decisao: 'PENDENTE' },
      { id: 'e2', nivel: 2, aprovadorId: null, decisao: 'PENDENTE' },
    ],
  }))) === 'APV-ERR-003');

console.log('== CT-08: delegação (B3)');
const porDelegacao = assertPodeAprovar(contexto({ usuarioId: SUPLENTE, delegacoes: [delegacao()] }));
check('CT-08 delegado do gerente aprova em nome dele',
  porDelegacao.aprovadorId === SUPLENTE && porDelegacao.deleganteId === GERENTE && porDelegacao.viaDelegacao === true);
check('delegação inativa não vale (APV-B3)',
  codigoDe(() => assertPodeAprovar(contexto({ usuarioId: SUPLENTE, delegacoes: [delegacao({ ativa: false })] }))) === 'APV-B3');
check('delegação vencida não vale (APV-B3)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    delegacoes: [delegacao({ vigenciaFim: new Date('2026-06-10T00:00:00Z') })],
  }))) === 'APV-B3');
check('delegação futura não vale (APV-B3)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    delegacoes: [delegacao({ vigenciaInicio: new Date('2026-07-01T00:00:00Z'), vigenciaFim: new Date('2026-07-31T00:00:00Z') })],
  }))) === 'APV-B3');
check('delegação de outro centro de custo não vale (APV-B3)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    delegacoes: [delegacao({ centroCustoId: OUTRO_CC })],
  }))) === 'APV-B3');
check('delegação sem centro de custo vale para qualquer um',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    delegacoes: [delegacao({ centroCustoId: null })],
  }))) === 'OK');
check('delegação do SOLICITANTE não burla B1 (APV-B3)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    atribuicoes: [...ATRIBUICOES, atribuicao(SOLICITANTE, 2)],
    delegacoes: [delegacao({ deleganteId: SOLICITANTE })],
  }))) === 'APV-B3');
check('delegação do COMPRADOR não burla B2 (APV-B3)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    atribuicoes: [...ATRIBUICOES, atribuicao(COMPRADOR, 2)],
    delegacoes: [delegacao({ deleganteId: COMPRADOR })],
  }))) === 'APV-B3');
check('ninguém delega alçada que não tem (APV-B5)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    delegacoes: [delegacao({ deleganteId: DIRETOR })],
  }))) === 'APV-B5');
check('delegação não devolve segundo voto a quem já decidiu (APV-B4)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SUPLENTE,
    atribuicoes: [...ATRIBUICOES, atribuicao(GESTOR, 2)],
    delegacoes: [delegacao({ deleganteId: GESTOR })],
  }))) === 'APV-B4');

console.log('== R09: estouro de orçamento (B6 e B7)');
const estouro = (extras = {}) => ({ estourado: true, autorizado: false, nivelFinal: 2, ...extras });

check('nível final não aprova estouro sem autorizar (APV-B6)',
  codigoDe(() => assertPodeAprovar(contexto({ raiz: { estouro: estouro() } }))) === 'APV-B6');
check('nível final aprova quando autoriza explicitamente',
  codigoDe(() => assertPodeAprovar(contexto({ raiz: { estouro: estouro(), autorizarEstouro: true } }))) === 'OK');
check('a autorização volta marcada na resposta',
  assertPodeAprovar(contexto({ raiz: { estouro: estouro(), autorizarEstouro: true } })).autorizaEstouro === true);
check('estouro já autorizado antes não pede de novo',
  codigoDe(() => assertPodeAprovar(contexto({ raiz: { estouro: estouro({ autorizado: true }) } }))) === 'OK');
check('sem estouro, nada muda',
  codigoDe(() => assertPodeAprovar(contexto({ raiz: { estouro: estouro({ estourado: false }) } }))) === 'OK');
check('REJEITAR não exige autorizar estouro — é justamente a recusa',
  codigoDe(() => assertPodeAprovar(contexto({ raiz: { estouro: estouro(), decisao: 'REJEITADO' } }))) === 'OK');
check('nível intermediário aprova normalmente e passa adiante',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: GESTOR,
    etapa: { id: 'e1', nivel: 1 },
    etapasDaInstancia: [
      { id: 'e1', nivel: 1, aprovadorId: null, decisao: 'PENDENTE' },
      { id: 'e2', nivel: 2, aprovadorId: null, decisao: 'PENDENTE' },
    ],
    raiz: { estouro: estouro({ nivelFinal: 2 }) },
  }))) === 'OK');
check('nível intermediário NÃO autoriza estouro (APV-B7)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: GESTOR,
    etapa: { id: 'e1', nivel: 1 },
    etapasDaInstancia: [
      { id: 'e1', nivel: 1, aprovadorId: null, decisao: 'PENDENTE' },
      { id: 'e2', nivel: 2, aprovadorId: null, decisao: 'PENDENTE' },
    ],
    raiz: { estouro: estouro({ nivelFinal: 2 }), autorizarEstouro: true },
  }))) === 'APV-B7');
check('não se autoriza estouro que não existe (APV-B7)',
  codigoDe(() => assertPodeAprovar(contexto({
    raiz: { estouro: estouro({ estourado: false }), autorizarEstouro: true },
  }))) === 'APV-B7');
check('instância sem requisição associada ignora B6/B7',
  codigoDe(() => assertPodeAprovar(contexto({ raiz: { estouro: null } }))) === 'OK');
check('B1 continua vindo antes do estouro',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: SOLICITANTE, raiz: { estouro: estouro(), autorizarEstouro: true },
  }))) === 'APV-B1');

console.log('== estado da instância e da etapa');
check('instância já aprovada não aceita nova decisão (APV-ERR-001)',
  codigoDe(() => assertPodeAprovar(contexto({ instancia: { status: 'APROVADA' } }))) === 'APV-ERR-001');
check('instância invalidada não aceita decisão (APV-ERR-001)',
  codigoDe(() => assertPodeAprovar(contexto({ instancia: { status: 'INVALIDADA' } }))) === 'APV-ERR-001');
check('etapa já decidida não é redecidida (APV-ERR-002)',
  codigoDe(() => assertPodeAprovar(contexto({ etapa: { decisao: 'APROVADO' } }))) === 'APV-ERR-002');
check('nível não pula a fila (APV-ERR-003)',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: DIRETOR,
    instancia: { nivelExigido: 3 },
    etapa: { id: 'e3', nivel: 3 },
    etapasDaInstancia: [
      { id: 'e1', nivel: 1, aprovadorId: GESTOR, decisao: 'APROVADO' },
      { id: 'e2', nivel: 2, aprovadorId: null, decisao: 'PENDENTE' },
      { id: 'e3', nivel: 3, aprovadorId: null, decisao: 'PENDENTE' },
    ],
  }))) === 'APV-ERR-003');
check('com o nível anterior aprovado, o diretor decide o nível 3',
  codigoDe(() => assertPodeAprovar(contexto({
    usuarioId: DIRETOR,
    instancia: { nivelExigido: 3 },
    etapa: { id: 'e3', nivel: 3 },
    etapasDaInstancia: [
      { id: 'e1', nivel: 1, aprovadorId: GESTOR, decisao: 'APROVADO' },
      { id: 'e2', nivel: 2, aprovadorId: GERENTE, decisao: 'APROVADO' },
      { id: 'e3', nivel: 3, aprovadorId: null, decisao: 'PENDENTE' },
    ],
  }))) === 'OK');

console.log(`\n== RESULTADO: ${ok} ok, ${fail} fail`);
process.exitCode = fail ? 1 : 0;
