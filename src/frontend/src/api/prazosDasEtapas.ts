import { api } from './cliente';

/** O prazo de uma etapa, com o rótulo que a Torre usa para ela. */
export interface PrazoDaEtapa {
  stage: string;
  label: string;
  maxDays: number;
  /** O tipo não definiu esta etapa e segue o padrão — mudar o padrão move esta junto. */
  inherited: boolean;
  updatedAt: string;
  updatedByLabel: string;
}

export interface PrazosDasEtapas {
  /** A partir de que percentual do prazo a etapa entra em atenção. Quem define é o servidor. */
  warnAtPercent: number;
  canEdit: boolean;
  /** De que tipo é este conjunto. Nulo é o padrão — o que vale para quem não tem tipo. */
  requestType: string | null;
  /** Os tipos cadastrados, para o seletor não precisar de uma segunda consulta. */
  types: { code: string; name: string }[];
  items: PrazoDaEtapa[];
}

export const lerPrazosDasEtapas = async (tipo?: string, signal?: AbortSignal) => {
  const r = await api<PrazosDasEtapas>(
    `/api/v1/stage-sla${tipo ? `?requestType=${encodeURIComponent(tipo)}` : ''}`, { signal });
  return { ...r, items: r.items ?? [], types: r.types ?? [] };
};

/**
 * Grava os prazos de um tipo. `herdar` lista as etapas que voltam a seguir o padrão — é como
 * se desfaz uma exceção sem copiar o número do padrão para cá, o que a congelaria.
 */
export const salvarPrazosDasEtapas = (
  dias: Record<string, number>, tipo?: string, herdar: string[] = [],
) => api<{ items: { stage: string; maxDays: number }[] }>('/api/v1/stage-sla', {
  method: 'PUT', body: { days: dias, requestType: tipo || null, inherit: herdar },
});

/**
 * Como o prazo será lido na Torre. Zero é desligado, e a tela diz isso em vez de mostrar
 * "0 dias" — que se leria como "tem de resolver hoje", o oposto do que zero significa.
 */
export const leituraDoPrazo = (dias: number, avisoEmPct: number): string =>
  dias <= 0
    ? 'sem cobrança de tempo nesta etapa'
    : `atenção a partir de ${Math.max(1, Math.ceil(dias * avisoEmPct / 100))} dia(s), `
      + `estouro depois de ${dias}`;
