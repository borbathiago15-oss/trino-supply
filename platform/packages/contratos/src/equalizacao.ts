/**
 * Equalização de propostas — DOMÍNIO PURO (sem Prisma, sem Nest, sem relógio).
 * Recebe as propostas já normalizadas e devolve a matriz completa de notas.
 *
 * Como cada critério vira nota (0 a 1):
 *  - normalização MIN-MAX dentro do conjunto avaliado, não contra um teto
 *    arbitrário: a melhor proposta de cada critério tira 1, a pior tira 0, e o
 *    resto fica proporcional entre elas;
 *  - `preco`, `lead_time` e `frete` são "menor é melhor" (nota invertida);
 *  - `cond_pagto` é "maior é melhor": mais dias para pagar é melhor para o caixa;
 *  - quando TODAS empatam num critério, todas tiram 1 — ninguém é penalizado
 *    por uma diferença que não existe.
 *
 * A nota final é a soma ponderada. Os pesos vêm da cotação e o banco garante
 * que somam 1,0000 (ck_cotacao_pesos), então a nota final também fica em 0..1.
 */

export type CriterioEqualizacao = 'preco' | 'lead_time' | 'frete' | 'cond_pagto';

export interface PesosEqualizacao {
  preco: number;
  lead_time: number;
  frete: number;
  cond_pagto: number;
}

export const PESOS_PADRAO: PesosEqualizacao = { preco: 0.6, lead_time: 0.2, frete: 0.1, cond_pagto: 0.1 };

export interface PropostaParaEqualizar {
  id: string;
  fornecedorId: string;
  /** valor_total da proposta (itens + frete - desconto). */
  valorTotal: number;
  frete: number;
  prazoEntregaDias: number;
  /** Prazo médio de pagamento em dias, derivado da condição comercial. */
  prazoPagamentoDias: number;
}

export interface NotaProposta {
  propostaId: string;
  fornecedorId: string;
  valorTotal: number;
  notas: Record<CriterioEqualizacao, number>;
  notaFinal: number;
  posicao: number;
  /** true na proposta de menor valor_total do conjunto. */
  menorPreco: boolean;
}

export interface ResultadoEqualizacao {
  pesos: PesosEqualizacao;
  matriz: NotaProposta[];
  vencedoraPorNota: NotaProposta;
  menorPreco: NotaProposta;
  /** true quando a melhor nota NÃO é a proposta mais barata. */
  exigeJustificativa: boolean;
}

export class EqualizacaoError extends Error {
  readonly codigo: string;
  readonly detalhe: Record<string, unknown>;

  constructor(codigo: string, mensagem: string, detalhe: Record<string, unknown> = {}) {
    super(mensagem);
    this.name = 'EqualizacaoError';
    this.codigo = codigo;
    this.detalhe = detalhe;
  }
}

const arredonda = (valor: number, casas = 4) => Number(valor.toFixed(casas));

/**
 * Nota 0..1 por min-max. `menorEhMelhor` inverte a escala.
 * Conjunto homogêneo (todos iguais) devolve 1 para todos.
 */
function normalizar(valores: number[], valor: number, menorEhMelhor: boolean): number {
  const min = Math.min(...valores);
  const max = Math.max(...valores);
  if (max === min) return 1;
  const bruto = menorEhMelhor ? (max - valor) / (max - min) : (valor - min) / (max - min);
  return arredonda(bruto);
}

/**
 * Deriva o prazo médio de pagamento de uma condição comercial escrita à mão
 * ("30/60/90", "28 DDL", "À VISTA"). Sem número na string, assume 0 — à vista
 * é o pior caso para o caixa, e é o palpite conservador quando o texto é
 * ilegível: nunca premia uma condição que não conseguimos ler.
 */
export function prazoPagamentoDeCondicao(condicao: string | null | undefined): number {
  const numeros = String(condicao ?? '').match(/\d+/g);
  if (!numeros || numeros.length === 0) return 0;
  const dias = numeros.map(Number).filter((n) => Number.isFinite(n));
  return arredonda(dias.reduce((s, d) => s + d, 0) / dias.length, 2);
}

export function validarPesos(pesos: PesosEqualizacao): void {
  const soma = arredonda(pesos.preco + pesos.lead_time + pesos.frete + pesos.cond_pagto, 4);
  if (soma !== 1) {
    throw new EqualizacaoError('EQL-ERR-001', `Os pesos da equalização precisam somar 1,0000 (somam ${soma}).`, {
      pesos,
      soma,
    });
  }
  for (const [criterio, peso] of Object.entries(pesos)) {
    if (peso < 0) throw new EqualizacaoError('EQL-ERR-002', `Peso negativo em ${criterio}.`, { criterio, peso });
  }
}

/**
 * Monta a matriz de equalização. Não decide nada sozinha: devolve a melhor por
 * nota e a mais barata, e sinaliza quando escolher a primeira exige
 * justificativa — a decisão (e o ônus de justificá-la) é do comprador.
 */
export function equalizar(propostas: PropostaParaEqualizar[], pesos: PesosEqualizacao = PESOS_PADRAO): ResultadoEqualizacao {
  if (propostas.length === 0) {
    throw new EqualizacaoError('EQL-ERR-003', 'Não há propostas para equalizar.');
  }
  validarPesos(pesos);

  const totais = propostas.map((p) => p.valorTotal);
  const fretes = propostas.map((p) => p.frete);
  const prazos = propostas.map((p) => p.prazoEntregaDias);
  const pagamentos = propostas.map((p) => p.prazoPagamentoDias);
  const menorTotal = Math.min(...totais);

  const matriz: NotaProposta[] = propostas.map((p) => {
    const notas: Record<CriterioEqualizacao, number> = {
      preco: normalizar(totais, p.valorTotal, true),
      lead_time: normalizar(prazos, p.prazoEntregaDias, true),
      frete: normalizar(fretes, p.frete, true),
      cond_pagto: normalizar(pagamentos, p.prazoPagamentoDias, false),
    };
    const notaFinal = arredonda(
      notas.preco * pesos.preco +
        notas.lead_time * pesos.lead_time +
        notas.frete * pesos.frete +
        notas.cond_pagto * pesos.cond_pagto,
    );
    return {
      propostaId: p.id,
      fornecedorId: p.fornecedorId,
      valorTotal: p.valorTotal,
      notas,
      notaFinal,
      posicao: 0,
      menorPreco: p.valorTotal === menorTotal,
    };
  });

  // Empate na nota: desempata o mais barato; persistindo, o de entrega mais rápida.
  const ordenada = [...matriz].sort(
    (a, b) =>
      b.notaFinal - a.notaFinal ||
      a.valorTotal - b.valorTotal ||
      (propostas.find((p) => p.id === a.propostaId)!.prazoEntregaDias -
        propostas.find((p) => p.id === b.propostaId)!.prazoEntregaDias),
  );
  ordenada.forEach((n, i) => {
    n.posicao = i + 1;
  });

  const vencedoraPorNota = ordenada[0];
  const menorPreco = ordenada.find((n) => n.menorPreco)!;

  return {
    pesos,
    matriz: ordenada,
    vencedoraPorNota,
    menorPreco,
    exigeJustificativa: vencedoraPorNota.propostaId !== menorPreco.propostaId,
  };
}

/**
 * Guarda da escolha final: quem não é o menor preço só passa com justificativa.
 * O banco repete a regra em ck_equalizacao_desvio — aqui a resposta sai antes,
 * com código e motivo.
 */
export function assertEscolhaJustificada(
  resultado: ResultadoEqualizacao,
  propostaEscolhidaId: string,
  justificativa: string | null | undefined,
): void {
  const escolhida = resultado.matriz.find((n) => n.propostaId === propostaEscolhidaId);
  if (!escolhida) {
    throw new EqualizacaoError('EQL-ERR-004', 'A proposta escolhida não está entre as equalizadas.', {
      propostaEscolhidaId,
    });
  }
  if (!escolhida.menorPreco && !(justificativa ?? '').trim()) {
    throw new EqualizacaoError(
      'EQL-ERR-005',
      'A proposta escolhida não é a de menor preço — justificativa do desvio é obrigatória.',
      {
        escolhida: escolhida.propostaId,
        valorEscolhido: escolhida.valorTotal,
        menorPreco: resultado.menorPreco.propostaId,
        valorMenorPreco: resultado.menorPreco.valorTotal,
      },
    );
  }
}
