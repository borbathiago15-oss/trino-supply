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
    celulas: vigentes.map((p) => celula(p, item, impedimentoNaGrade(lotes, p))),
  }));
}

function celula(p: Proposta, item: ItemDoProcesso, impedimento: string | null): CelulaDaGrade {
  const unitPrice = precoDoItem(p, item.id);
  return {
    proposalId: p.id, supplierId: p.supplierId, supplierName: p.supplierName,
    unitPrice,
    total: unitPrice == null ? null : unitPrice * item.quantity,
    impedimento,
  };
}

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
 * Totais do rodapé de cada coluna, como o comprador espera lê-los:
 * <b>cotado</b> é tudo que aquele fornecedor ofereceu; <b>selecionado</b> é só o que ele
 * está levando na escolha atual. Os dois juntos respondem "quanto ele pediu" e "quanto
 * ele leva" sem precisar de calculadora.
 */
export function totaisPorColuna(
  linhas: LinhaDaGrade[], escolhas: Record<string, string>,
): Record<string, { cotado: number; selecionado: number }> {
  const totais: Record<string, { cotado: number; selecionado: number }> = {};
  for (const linha of linhas) {
    for (const c of linha.celulas) {
      const atual = totais[c.proposalId] ?? { cotado: 0, selecionado: 0 };
      if (c.total != null) {
        atual.cotado += c.total;
        if (escolhas[linha.item.id] === c.proposalId) atual.selecionado += c.total;
      }
      totais[c.proposalId] = atual;
    }
  }
  return totais;
}

/** Itens ainda sem vencedor — o que falta para poder confirmar. */
export const itensSemVencedor = (linhas: LinhaDaGrade[], escolhas: Record<string, string>) =>
  linhas.filter((l) => !escolhas[l.item.id]).map((l) => l.item);

/**
 * Quantos fornecedores a escolha atual envolve. Um só quer dizer que a grade chegou no
 * mesmo lugar da escolha simples — e é isso que a tela diz, em vez de anunciar uma
 * "compra dividida" que não se dividiu.
 */
export const fornecedoresEscolhidos = (linhas: LinhaDaGrade[], escolhas: Record<string, string>) =>
  [...new Set(linhas.flatMap((l) => l.celulas
    .filter((c) => escolhas[l.item.id] === c.proposalId).map((c) => c.supplierId)))];
