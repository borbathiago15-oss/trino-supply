export type Periodo = 'mes' | 'trimestre' | 'ano';

export const PERIODOS: { chave: Periodo; rotulo: string }[] = [
  { chave: 'mes', rotulo: 'Este mês' },
  { chave: 'trimestre', rotulo: 'Últimos 90 dias' },
  { chave: 'ano', rotulo: 'Este ano' },
];

const iso = (d: Date) => d.toISOString().slice(0, 10);

/**
 * O recorte de cada botão, em datas: o mês corrente, os últimos 90 dias ou o ano até hoje.
 * Três escolhas em vez de dez filtros — a diretoria pergunta "como está o mês", não
 * "como está o centro X do comprador Y".
 */
export function intervaloDoPeriodo(p: Periodo, hoje: Date = new Date()): { de: string; ate: string } {
  const ate = iso(hoje);
  const y = hoje.getUTCFullYear(); const m = hoje.getUTCMonth();
  if (p === 'mes') return { de: iso(new Date(Date.UTC(y, m, 1))), ate };
  if (p === 'ano') return { de: iso(new Date(Date.UTC(y, 0, 1))), ate };
  return { de: iso(new Date(hoje.getTime() - 90 * 86_400_000)), ate };
}
