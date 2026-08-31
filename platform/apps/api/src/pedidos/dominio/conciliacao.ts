/**
 * Conciliação de recebimento — DOMÍNIO PURO (sem Prisma, sem Nest, sem relógio).
 *
 * 3-WAY MATCH: confronta as três fontes que precisam contar a mesma história —
 * o que foi PEDIDO, o que a NOTA FISCAL diz e o que de fato CHEGOU. Divergir em
 * qualquer par é achado, não erro fatal: o recebimento é registrado com a
 * ocorrência, porque negar o registro só faria a mercadoria existir no pátio e
 * não no sistema.
 *
 * O que a função decide:
 *  - se o recebimento é TOTAL ou PARCIAL (acumulado, não só desta remessa);
 *  - quanto de cada item entra em estoque (o avariado NÃO entra);
 *  - quais divergências existem, cada uma com código;
 *  - quais eventos vão para a Conta 408 (avaria e divergência).
 */

/** Tolerância de excesso aceita no acumulado — espelha ck_item_pedido_qtd. */
export const TOLERANCIA_EXCESSO = 0.1;
/** Diferença de centavos aceita entre NF e valor recebido. */
export const TOLERANCIA_VALOR = 0.02;

export type Ocorrencia =
  | 'SEM_OCORRENCIA'
  | 'AVARIA'
  | 'DIVERGENCIA_QUANTIDADE'
  | 'DIVERGENCIA_ESPECIFICACAO'
  | 'ATRASO'
  | 'RECUSA_TOTAL';

export interface ItemPedidoParaConciliar {
  id: string;
  varianteId: string;
  qtdPedida: number;
  /** Já recebido em remessas anteriores. */
  qtdRecebidaAcumulada: number;
  precoUnitario: number;
}

export interface LinhaRecebida {
  itemPedidoId: string;
  qtdRecebida: number;
  qtdAvariada?: number;
  ocorrencia?: Ocorrencia;
  descricaoOcorrencia?: string | null;
}

export interface NotaFiscalParaConciliar {
  id: string;
  valorTotal: number;
  fornecedorId: string;
}

export interface Divergencia {
  codigo: string;
  mensagem: string;
  itemPedidoId?: string;
  detalhe: Record<string, unknown>;
}

export interface EventoConta408 {
  conta: '408';
  tipo: 'AVARIA' | 'DIVERGENCIA_QUANTIDADE' | 'DIVERGENCIA_VALOR' | 'DIVERGENCIA_ESPECIFICACAO';
  itemPedidoId?: string;
  varianteId?: string;
  quantidade?: number;
  valor: number;
  descricao: string;
}

export interface LinhaConciliada {
  itemPedidoId: string;
  varianteId: string;
  qtdRecebida: number;
  qtdAvariada: number;
  /** O que efetivamente entra no estoque: recebido menos avariado. */
  qtdParaEstoque: number;
  qtdAcumuladaApos: number;
  qtdPedida: number;
  precoUnitario: number;
  valorRecebido: number;
  completo: boolean;
  ocorrencia: Ocorrencia;
}

export interface ResultadoConciliacao {
  tipo: 'TOTAL' | 'PARCIAL';
  linhas: LinhaConciliada[];
  valorRecebido: number;
  valorNotaFiscal: number | null;
  divergencias: Divergencia[];
  eventosConta408: EventoConta408[];
  /** true quando pedido, NF e recebido fecham sem nenhuma divergência. */
  conciliado: boolean;
}

export class ConciliacaoError extends Error {
  readonly codigo: string;
  readonly detalhe: Record<string, unknown>;

  constructor(codigo: string, mensagem: string, detalhe: Record<string, unknown> = {}) {
    super(mensagem);
    this.name = 'ConciliacaoError';
    this.codigo = codigo;
    this.detalhe = detalhe;
  }
}

const arredonda = (v: number, casas = 4) => Number(v.toFixed(casas));
const dinheiro = (v: number) => Number(v.toFixed(2));

/**
 * Concilia uma remessa. Lança apenas no que é impossível registrar (item fora
 * do pedido, quantidade não positiva, avaria maior que o recebido, excesso
 * acima da tolerância); o resto vira divergência registrada.
 */
export function conciliarRecebimento(entrada: {
  itensPedido: ItemPedidoParaConciliar[];
  linhas: LinhaRecebida[];
  notaFiscal?: NotaFiscalParaConciliar | null;
}): ResultadoConciliacao {
  const { itensPedido, linhas } = entrada;
  const notaFiscal = entrada.notaFiscal ?? null;

  if (linhas.length === 0) {
    throw new ConciliacaoError('REC-ERR-001', 'Informe ao menos um item recebido.');
  }
  const repetidos = linhas.map((l) => l.itemPedidoId).filter((id, i, todos) => todos.indexOf(id) !== i);
  if (repetidos.length > 0) {
    throw new ConciliacaoError('REC-ERR-002', 'O mesmo item do pedido aparece duas vezes na remessa.', { repetidos });
  }

  const porId = new Map(itensPedido.map((i) => [i.id, i]));
  const divergencias: Divergencia[] = [];
  const eventos: EventoConta408[] = [];

  const conciliadas: LinhaConciliada[] = linhas.map((linha) => {
    const item = porId.get(linha.itemPedidoId);
    if (!item) {
      throw new ConciliacaoError('REC-ERR-003', 'Item recebido não pertence a este pedido.', {
        itemPedidoId: linha.itemPedidoId,
      });
    }
    const qtdRecebida = arredonda(linha.qtdRecebida);
    const qtdAvariada = arredonda(linha.qtdAvariada ?? 0);
    if (qtdRecebida <= 0) {
      throw new ConciliacaoError('REC-ERR-004', 'Quantidade recebida precisa ser maior que zero.', {
        itemPedidoId: item.id,
      });
    }
    if (qtdAvariada < 0 || qtdAvariada > qtdRecebida) {
      throw new ConciliacaoError('REC-ERR-005', 'Quantidade avariada não pode passar da recebida.', {
        itemPedidoId: item.id,
        qtdRecebida,
        qtdAvariada,
      });
    }

    const acumulada = arredonda(item.qtdRecebidaAcumulada + qtdRecebida);
    const teto = arredonda(item.qtdPedida * (1 + TOLERANCIA_EXCESSO));
    if (acumulada > teto) {
      throw new ConciliacaoError(
        'REC-ERR-006',
        `Recebimento excede o pedido além da tolerância de ${TOLERANCIA_EXCESSO * 100}%.`,
        { itemPedidoId: item.id, qtdPedida: item.qtdPedida, acumulada, teto },
      );
    }

    const ocorrencia: Ocorrencia = linha.ocorrencia ?? (qtdAvariada > 0 ? 'AVARIA' : 'SEM_OCORRENCIA');
    const qtdParaEstoque = arredonda(qtdRecebida - qtdAvariada);
    const valorRecebido = dinheiro(qtdRecebida * item.precoUnitario);

    // Avaria vira evento contábil: a mercadoria chegou, mas não pode ser usada.
    if (qtdAvariada > 0) {
      eventos.push({
        conta: '408',
        tipo: 'AVARIA',
        itemPedidoId: item.id,
        varianteId: item.varianteId,
        quantidade: qtdAvariada,
        valor: dinheiro(qtdAvariada * item.precoUnitario),
        descricao: linha.descricaoOcorrencia?.trim() || 'Avaria constatada no recebimento.',
      });
      divergencias.push({
        codigo: 'REC-DIV-001',
        mensagem: `Item com ${qtdAvariada} unidade(s) avariada(s).`,
        itemPedidoId: item.id,
        detalhe: { qtdRecebida, qtdAvariada },
      });
    }

    if (ocorrencia === 'DIVERGENCIA_ESPECIFICACAO') {
      eventos.push({
        conta: '408',
        tipo: 'DIVERGENCIA_ESPECIFICACAO',
        itemPedidoId: item.id,
        varianteId: item.varianteId,
        quantidade: qtdRecebida,
        valor: valorRecebido,
        descricao: linha.descricaoOcorrencia?.trim() || 'Especificação divergente do pedido.',
      });
      divergencias.push({
        codigo: 'REC-DIV-004',
        mensagem: 'Especificação recebida diverge do pedido.',
        itemPedidoId: item.id,
        detalhe: { qtdRecebida },
      });
    }

    // Excesso dentro da tolerância é aceito, mas não passa despercebido.
    if (acumulada > item.qtdPedida) {
      divergencias.push({
        codigo: 'REC-DIV-002',
        mensagem: 'Recebido acima do pedido, dentro da tolerância.',
        itemPedidoId: item.id,
        detalhe: { qtdPedida: item.qtdPedida, acumulada, excesso: arredonda(acumulada - item.qtdPedida) },
      });
      eventos.push({
        conta: '408',
        tipo: 'DIVERGENCIA_QUANTIDADE',
        itemPedidoId: item.id,
        varianteId: item.varianteId,
        quantidade: arredonda(acumulada - item.qtdPedida),
        valor: dinheiro((acumulada - item.qtdPedida) * item.precoUnitario),
        descricao: 'Quantidade recebida acima da pedida (dentro da tolerância).',
      });
    }

    return {
      itemPedidoId: item.id,
      varianteId: item.varianteId,
      qtdRecebida,
      qtdAvariada,
      qtdParaEstoque,
      qtdAcumuladaApos: acumulada,
      qtdPedida: item.qtdPedida,
      precoUnitario: item.precoUnitario,
      valorRecebido,
      completo: acumulada >= item.qtdPedida,
      ocorrencia,
    };
  });

  // TOTAL só quando TODOS os itens do pedido estão completos — inclusive os que
  // não vieram nesta remessa.
  const acumuladoPorItem = new Map(itensPedido.map((i) => [i.id, i.qtdRecebidaAcumulada]));
  for (const l of conciliadas) acumuladoPorItem.set(l.itemPedidoId, l.qtdAcumuladaApos);
  const pendentes = itensPedido.filter((i) => (acumuladoPorItem.get(i.id) ?? 0) < i.qtdPedida);
  const tipo: 'TOTAL' | 'PARCIAL' = pendentes.length === 0 ? 'TOTAL' : 'PARCIAL';

  for (const item of pendentes) {
    const jaRecebido = acumuladoPorItem.get(item.id) ?? 0;
    divergencias.push({
      codigo: 'REC-DIV-003',
      mensagem: 'Item ainda pendente de entrega.',
      itemPedidoId: item.id,
      detalhe: { qtdPedida: item.qtdPedida, recebido: jaRecebido, falta: arredonda(item.qtdPedida - jaRecebido) },
    });
  }

  const valorRecebido = dinheiro(conciliadas.reduce((s, l) => s + l.valorRecebido, 0));

  // Terceira via do match: a NF precisa bater com o que chegou, ao preço do pedido.
  if (notaFiscal) {
    const diferenca = dinheiro(Math.abs(notaFiscal.valorTotal - valorRecebido));
    if (diferenca > TOLERANCIA_VALOR) {
      divergencias.push({
        codigo: 'REC-DIV-005',
        mensagem: 'Valor da nota fiscal diverge do valor recebido a preço de pedido.',
        detalhe: { valorNotaFiscal: notaFiscal.valorTotal, valorRecebido, diferenca },
      });
      eventos.push({
        conta: '408',
        tipo: 'DIVERGENCIA_VALOR',
        valor: diferenca,
        descricao: `NF ${notaFiscal.id} diverge em R$ ${diferenca.toFixed(2)} do recebido.`,
      });
    }
  }

  return {
    tipo,
    linhas: conciliadas,
    valorRecebido,
    valorNotaFiscal: notaFiscal?.valorTotal ?? null,
    divergencias,
    eventosConta408: eventos,
    // Pendência de entrega em remessa parcial é o curso normal, não divergência
    // de conciliação: o que desconcilia é avaria, excesso, especificação e valor.
    conciliado: divergencias.filter((d) => d.codigo !== 'REC-DIV-003').length === 0,
  };
}
