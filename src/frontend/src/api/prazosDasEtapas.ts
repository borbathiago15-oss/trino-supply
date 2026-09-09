import { api } from './cliente';

/** O prazo de uma etapa, com o rótulo que a Torre usa para ela. */
export interface PrazoDaEtapa {
  stage: string;
  label: string;
  maxDays: number;
  updatedAt: string;
  updatedByLabel: string;
}

export interface PrazosDasEtapas {
  /** A partir de que percentual do prazo a etapa entra em atenção. Quem define é o servidor. */
  warnAtPercent: number;
  canEdit: boolean;
  items: PrazoDaEtapa[];
}

export const lerPrazosDasEtapas = async (signal?: AbortSignal) => {
  const r = await api<PrazosDasEtapas>('/api/v1/stage-sla', { signal });
  return { ...r, items: r.items ?? [] };
};

export const salvarPrazosDasEtapas = (dias: Record<string, number>) =>
  api<{ items: { stage: string; maxDays: number }[] }>('/api/v1/stage-sla', {
    method: 'PUT', body: { days: dias },
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
