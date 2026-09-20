import type { RelatorioExecutivo } from '@/api/relatorios';
import { variacao } from '@/api/painel';
import { moeda, quantidade } from '@/util/formato';

const pct = (v: number | null | undefined) => (v == null ? '—' : `${quantidade(v)}%`);

/**
 * O relatório em três frases — gasto, saving, exceções. É o que um diretor lê de fato
 * antes de decidir se abre algum bloco. Cada frase sai dos mesmos números dos blocos;
 * nada aqui é opinião, só o número com a régua ao lado.
 */
export function resumoExecutivo(r: RelatorioExecutivo): [string, string, string] {
  const k = r.kpis; const a = r.previous;
  const v = variacao(k.spend, a.spend);
  const tendencia = a.spend > 0
    ? `, ${v.pct > 0 ? `${quantidade(Math.abs(v.pct))}% acima` : v.pct < 0 ? `${quantidade(Math.abs(v.pct))}% abaixo` : 'igual ao'} do período anterior (${moeda(a.spend)})`
    : '';
  const familia = r.families[0];
  const gasto = k.orders === 0
    ? 'Nenhuma compra fechou no período.'
    : `Foram ${moeda(k.spend)} em ${quantidade(k.orders)} pedido(s) com ${quantidade(k.suppliers)} fornecedor(es)${tendencia}.`
      + (familia ? ` A família que mais pesou foi ${familia.family} (${pct(familia.percent)}).` : '');

  const neg = r.savingRulers.negotiation; const con = r.savingRulers.competition; const orc = r.savingRulers.budget;
  const partes: string[] = [];
  if (neg.processes > 0) partes.push(`a negociação segurou ${moeda(neg.saving)} (${pct(neg.percent)} da primeira proposta)`);
  if (con.processes > 0) partes.push(`a concorrência valeu ${moeda(con.saving)}`);
  if (orc.processes > 0) partes.push(`${orc.saving >= 0 ? 'fechou' : 'estourou'} ${moeda(Math.abs(orc.saving))} ${orc.saving >= 0 ? 'abaixo' : 'acima'} do orçamento das SCs`);
  const saving = partes.length
    ? partes.join('; ').replace(/^./, (c) => c.toUpperCase()) + '.'
    : 'Nenhum processo negociado ou com concorrência no período: não há saving a medir.';

  const w = r.withoutErp;
  const excecoes = `${w.orders === 0 ? 'Toda compra fechou com O.C. do ERP' : `${quantidade(w.orders)} compra(s) fecharam sem O.C. do ERP (${moeda(w.value)})`}`
    + (w.pendingOrders > 0 ? `, ${quantidade(w.pendingOrders)} ainda com a O.C. por registrar` : '')
    + `; ${pct(k.urgentPercent)} do valor foi urgente`
    + (k.otifPercent != null ? `; OTIF de ${pct(k.otifPercent)}${a.otifPercent != null ? ` (antes ${pct(a.otifPercent)})` : ''}` : '; sem entrega medida para o OTIF')
    + '.';
  return [gasto, saving, excecoes];
}
