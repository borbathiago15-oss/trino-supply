/**
 * Os tons que a API usa para colorir situações (`processStatusTone`), traduzidos
 * para as classes do tema. Ficam num lugar só porque a mesma situação aparece
 * em Meus Pedidos, na Gestão de Solicitações e no painel do almoxarifado.
 */
const TOM: Record<string, string> = {
  on: 'bg-ok-fundo text-ok',
  off: 'bg-slate-100 text-slate-500',
  dev: 'bg-slate-100 text-slate-600',
  warn: 'bg-aviso-fundo text-aviso',
  info: 'bg-blue-50 text-blue-800',
  orange: 'bg-orange-50 text-orange-800',
  purple: 'bg-purple-50 text-purple-800',
  teal: 'bg-teal-50 text-teal-800',
};

export const NEUTRO = 'bg-slate-100 text-slate-600';

export const classeDoTom = (tom: string | null | undefined) => TOM[tom ?? ''] ?? NEUTRO;
