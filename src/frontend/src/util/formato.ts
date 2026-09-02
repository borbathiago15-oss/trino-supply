export const moeda = (v: number | null | undefined) =>
  (v ?? 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

/** Quantidades: até uma casa decimal, como o `fmtInt` do legado. */
export const quantidade = (v: number | string | null | undefined) =>
  (+(v ?? 0) || 0).toLocaleString('pt-BR', { maximumFractionDigits: 1 });

/** `yyyy-MM-dd` (DateOnly) → `dd/MM/yyyy`; passa adiante o que não reconhecer. */
export function data(iso: string | null | undefined): string {
  if (!iso) return '—';
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : iso;
}

export function dataHora(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

/** Data de hoje no formato do `<input type="date">`. */
export const hojeIso = () => new Date().toISOString().slice(0, 10);
