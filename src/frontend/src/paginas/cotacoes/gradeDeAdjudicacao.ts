import {
  precoDoItem, propostasVigentes, type ItemDoProcesso, type Processo, type Proposta,
} from '@/api/cotacoes';
import { impedimentoDaOferta, type LoteDaFamilia } from '@/api/cotacoes';

/** O que um fornecedor ofereceu para um item da grade. */
export interface CelulaDaGrade {
  proposalId: string;
  supplierId: string;
  supplierName: string;
  /** Nulo quando o fornecedor não cotou este item — a célula fica vazia, não zerada. */
  unitPrice: number | null;
  total: number | null;
  /** Motivo pelo qual esta proposta não pode vencer, ou null. */
  impedimento: string | null;
  /**
   * Esta é a oferta mais barata do item, entre as que podem vencer. Empate marca as duas:
   * desempatar por ordem de coluna elegeria um vencedor que o preço não elegeu.
   */
  menorPreco: boolean;
  /**
   * Quanto esta oferta está acima da mais barata, em por cento. Nulo na própria mais barata,
   * em quem não cotou e em quem não pode vencer.
   *
   * É percentual, e não uma seta, porque a pergunta do comprador não é "é mais caro?" — isso
   * a coluna do preço já responde — e sim "mais caro o suficiente para eu abrir mão do prazo
   * de entrega deste aqui?". Dois por cento e oitenta por cento pedem decisões diferentes.
   */
  acimaDoMenor: number | null;
}

export interface LinhaDaGrade {
  item: ItemDoProcesso;
  celulas: CelulaDaGrade[];
}

/** Coluna da grade: um fornecedor com proposta vigente. */
export interface ColunaDaGrade {
  proposalId: string;
  supplierId: string;
  supplierName: string;
  version: number;
}

export const colunasDaGrade = (processo: Processo): ColunaDaGrade[] =>
  propostasVigentes(processo).map((p) => ({
    proposalId: p.id, supplierId: p.supplierId, supplierName: p.supplierName, version: p.version,
  }));

/**
 * A grade item × fornecedor. É a leitura que o comprador faz de verdade: uma linha por
 * item, uma coluna por proposta, e o preço no cruzamento — inclusive vazio, que é
 * informação (o fornecedor não cotou aquele item).
 *
 * O impedimento vem da mesma régua do resto da tela: quem não pode vencer aparece
 * marcado, e não some, senão o comprador não entende por que a opção não está lá.
 */
export function linhasDaGrade(
  processo: Processo, lotes: LoteDaFamilia[] | null,
): LinhaDaGrade[] {
  const vigentes = propostasVigentes(processo);
  return [...processo.items].sort((a, b) => a.sequence - b.sequence).map((item) => ({
    item,
    celulas: comparadas(vigentes.map((p) => celula(p, item, impedimentoNaGrade(lotes, p)))),
  }));
}

function celula(p: Proposta, item: ItemDoProcesso, impedimento: string | null): CelulaDaGrade {
  const unitPrice = precoDoItem(p, item.id);
  return {
    proposalId: p.id, supplierId: p.supplierId, supplierName: p.supplierName,
    unitPrice,
    total: unitPrice == null ? null : unitPrice * item.quantity,
    impedimento,
    menorPreco: false,
    acimaDoMenor: null,
  };
}

/**
 * Marca, na própria linha, qual oferta é a mais barata e quanto as outras estão acima dela.
 *
 * A comparação é **dado da célula**, não efeito de um botão. Antes o menor preço só aparecia
 * quando o comprador apertava "Melhor preço por item" — e apertar <b>substituía todas</b> as
 * escolhas já feitas. Ou seja: para ver a informação ele tinha de perder a decisão. O sistema
 * indica o tempo todo e não decide nunca; quem decide é o comprador.
 *
 * Só entra na comparação quem pode vencer: destacar como "menor preço" uma oferta que a
 * adjudicação vai recusar seria apontar para uma porta fechada.
 */
function comparadas(celulas: CelulaDaGrade[]): CelulaDaGrade[] {
  const elegiveis = celulas.filter((c) => c.total != null && !c.impedimento);
  if (elegiveis.length === 0) return celulas;
  const menor = Math.min(...elegiveis.map((c) => c.total!));
  return celulas.map((c) => {
    if (c.total == null || c.impedimento) return c;
    return {
      ...c,
      menorPreco: c.total === menor,
      // menor > 0 evita divisão por zero no item cotado a custo zero (brinde, bonificação)
      acimaDoMenor: c.total === menor || menor <= 0 ? null
        : Math.round(((c.total - menor) / menor) * 1000) / 10,
    };
  });
}

/**
 * "Levar tudo deste fornecedor": o atalho oposto ao do melhor preço. Um concentra a compra,
 * o outro a espalha, e os dois são pontos de partida — a linha continua editável depois.
 *
 * Ele leva **só o que aquele fornecedor pode levar**: item que ele não cotou, ou em que está
 * impedido, fica como estava. Arrastar o item para uma coluna que não o cotou daria uma
 * escolha que a confirmação recusaria, e o comprador descobriria no erro.
 *
 * O que já estava escolhido em outros itens permanece: o atalho preenche, não zera a mesa.
 * A linha em <b>divisão</b> também fica intacta — ela tem decisão própria, com quantidade
 * digitada, e um atalho que a apagasse destruiria trabalho sem pedir licença.
 */
export function levarTudoDe(
  linhas: LinhaDaGrade[], proposalId: string, escolhas: Record<string, string>,
  divisoes: Divisoes = {},
): Record<string, string> {
  const nova = { ...escolhas };
  for (const linha of linhas) {
    if (podeLevar(linha, proposalId, divisoes)) nova[linha.item.id] = proposalId;
  }
  return nova;
}

/**
 * Quantos itens o "levar tudo" deste fornecedor pegaria. O número vai no botão porque ele
 * conta a história antes do clique: "levar tudo (8)" numa grade de doze itens diz, sem abrir
 * nada, que este fornecedor não cotou quatro.
 */
export const quantosLevaria = (
  linhas: LinhaDaGrade[], proposalId: string, divisoes: Divisoes = {},
) => linhas.filter((l) => podeLevar(l, proposalId, divisoes)).length;

const podeLevar = (linha: LinhaDaGrade, proposalId: string, divisoes: Divisoes) => {
  if (estaDividido(divisoes, linha.item.id)) return false;
  const c = linha.celulas.find((x) => x.proposalId === proposalId);
  return !!c && c.total != null && !c.impedimento;
};

/**
 * Impedimento do fornecedor na grade. Só o que é do <em>fornecedor</em> conta aqui —
 * homologação e cadastro inativo. "Não cotou este item" não é impedimento de coluna: é
 * a célula vazia, que a grade já mostra linha a linha.
 */
function impedimentoNaGrade(lotes: LoteDaFamilia[] | null, p: Proposta): string | null {
  if (!lotes) return null;
  const oferta = lotes.flatMap((l) => l.offers).find((o) => o.proposalId === p.id);
  if (!oferta) return null;   // mapa mais velho que a proposta: quem decide é a API
  return impedimentoDaOferta({ ...oferta, complete: true });
}

/**
 * O "melhor preço" da tela do ERP: para cada item, a proposta mais barata entre as que
 * podem vencer. Item que ninguém cotou fica de fora — sugerir um vencedor para ele seria
 * inventar escolha.
 */
export function melhorPrecoPorItem(linhas: LinhaDaGrade[]): Record<string, string> {
  const escolha: Record<string, string> = {};
  for (const linha of linhas) {
    const elegiveis = linha.celulas.filter((c) => c.total != null && !c.impedimento);
    if (elegiveis.length === 0) continue;
    escolha[linha.item.id] = elegiveis.reduce((a, b) => (b.total! < a.total! ? b : a)).proposalId;
  }
  return escolha;
}

/**
 * A divisão de um item entre fornecedores: quanto vai com cada proposta, como o comprador
 * digitou. Item sem entrada aqui vai inteiro para o vencedor único de `escolhas`.
 *
 * O texto fica cru de propósito: enquanto se digita, "7" e "70" e "700" são estados
 * legítimos do mesmo campo, e converter cedo faria o total pular a cada tecla.
 */
export type Divisoes = Record<string, Record<string, string>>;

/** Quanto foi digitado para esta proposta neste item — zero para o que não é número. */
export const quantoDivide = (divisoes: Divisoes, itemId: string, proposalId: string): number => {
  const v = Number((divisoes[itemId]?.[proposalId] ?? '').replace(',', '.'));
  return Number.isFinite(v) && v > 0 ? v : 0;
};

/**
 * O item está em divisão? É a <b>presença da chave</b>, e não o que já foi digitado: ligar o
 * modo e ainda não ter digitado nada é um item <b>sem escolha feita</b>, e não um item com
 * vencedor único. Fosse o contrário, uma divisão vazia passaria por resolvida.
 */
export const estaDividido = (divisoes: Divisoes, itemId: string): boolean =>
  divisoes[itemId] !== undefined;

/**
 * O que falta (ou sobra) para a divisão de um item fechar a quantidade pedida — nulo quando
 * fecha. É a mesma régua do `RFQ-ERR-025` do servidor, dita aqui antes de o comprador
 * apertar o botão: descobrir no erro da API que a conta não fecha é o caminho longo para
 * somar dois números.
 */
export function erroDaDivisao(linha: LinhaDaGrade, divisoes: Divisoes): string | null {
  if (!estaDividido(divisoes, linha.item.id)) return null;
  const soma = linha.celulas.reduce(
    (t, c) => t + quantoDivide(divisoes, linha.item.id, c.proposalId), 0);
  if (soma === linha.item.quantity) return null;
  const dif = Math.abs(Math.round((linha.item.quantity - soma) * 1000) / 1000);
  return soma < linha.item.quantity
    ? `faltam ${dif} ${linha.item.unitOfMeasure} para fechar ${linha.item.quantity}`
    : `sobram ${dif} ${linha.item.unitOfMeasure} além dos ${linha.item.quantity} pedidos`;
}

/**
 * As adjudicações que vão para o servidor. Item dividido vira uma linha por fornecedor com
 * quantidade; item de vencedor único vira uma linha sem quantidade — que é como o servidor
 * lê "a quantidade inteira", e é o que mantém idêntico o caminho de sempre.
 */
export function adjudicacoesDoFormulario(
  linhas: LinhaDaGrade[], escolhas: Record<string, string>, divisoes: Divisoes,
  justificativa: string,
) {
  return linhas.flatMap((l) => estaDividido(divisoes, l.item.id)
    ? l.celulas
      .filter((c) => quantoDivide(divisoes, l.item.id, c.proposalId) > 0)
      .map((c) => ({
        family: '', quotationItemId: l.item.id, proposalId: c.proposalId,
        criteria: [] as string[], justification: justificativa,
        quantity: quantoDivide(divisoes, l.item.id, c.proposalId),
      }))
    : [{
      family: '', quotationItemId: l.item.id, proposalId: escolhas[l.item.id],
      criteria: [] as string[], justification: justificativa,
      quantity: undefined as number | undefined,
    }]);
}

/**
 * Totais do rodapé de cada coluna, como o comprador espera lê-los:
 * <b>cotado</b> é tudo que aquele fornecedor ofereceu; <b>selecionado</b> é só o que ele
 * está levando na escolha atual. Os dois juntos respondem "quanto ele pediu" e "quanto
 * ele leva" sem precisar de calculadora.
 */
export function totaisPorColuna(
  linhas: LinhaDaGrade[], escolhas: Record<string, string>, divisoes: Divisoes = {},
): Record<string, { cotado: number; selecionado: number }> {
  const totais: Record<string, { cotado: number; selecionado: number }> = {};
  for (const linha of linhas) {
    const dividido = estaDividido(divisoes, linha.item.id);
    for (const c of linha.celulas) {
      const atual = totais[c.proposalId] ?? { cotado: 0, selecionado: 0 };
      if (c.total != null) {
        atual.cotado += c.total;
        // no item dividido o fornecedor leva o que ele leva, e não o item inteiro:
        // somar o total cheio dos dois faria o rodapé anunciar o dobro da compra
        if (dividido) {
          atual.selecionado += (c.unitPrice ?? 0) * quantoDivide(divisoes, linha.item.id, c.proposalId);
        } else if (escolhas[linha.item.id] === c.proposalId) {
          atual.selecionado += c.total;
        }
      }
      totais[c.proposalId] = atual;
    }
  }
  return totais;
}

/**
 * Itens ainda sem vencedor — o que falta para poder confirmar. Item dividido conta como
 * resolvido só quando a soma fecha: divisão pela metade não é escolha feita.
 */
export const itensSemVencedor = (
  linhas: LinhaDaGrade[], escolhas: Record<string, string>, divisoes: Divisoes = {},
) => linhas.filter((l) => estaDividido(divisoes, l.item.id)
  ? erroDaDivisao(l, divisoes) !== null
  : !escolhas[l.item.id]).map((l) => l.item);

/**
 * Quantos fornecedores a escolha atual envolve. Um só quer dizer que a grade chegou no
 * mesmo lugar da escolha simples — e é isso que a tela diz, em vez de anunciar uma
 * "compra dividida" que não se dividiu.
 */
export const fornecedoresEscolhidos = (
  linhas: LinhaDaGrade[], escolhas: Record<string, string>, divisoes: Divisoes = {},
) => [...new Set(linhas.flatMap((l) => l.celulas
  .filter((c) => estaDividido(divisoes, l.item.id)
    ? quantoDivide(divisoes, l.item.id, c.proposalId) > 0
    : escolhas[l.item.id] === c.proposalId)
  .map((c) => c.supplierId)))];
