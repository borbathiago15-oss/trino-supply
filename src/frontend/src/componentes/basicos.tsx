import type { ReactNode } from 'react';

/** Ícones dos estados vazios, inline: 48px, traço fino, sem dependência nova. */
const ICONE: Record<'caixa' | 'ok', ReactNode> = {
  caixa: (
    <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <polyline points="22 12 16 12 14 15 10 15 8 12 2 12" />
      <path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" />
    </svg>
  ),
  ok: (
    <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <circle cx="12" cy="12" r="10" />
      <path d="m9 12 2 2 4-4" />
    </svg>
  ),
};

export function Painel({ titulo, acoes, children, className = '', id }:
  { titulo?: ReactNode; acoes?: ReactNode; children: ReactNode; className?: string; id?: string }) {
  return (
    <section id={id} className={'painel ' + className}>
      {(titulo || acoes) && (
        <header className="mb-3 flex flex-wrap items-center justify-between gap-2">
          {titulo && <h2 className="text-[15px] font-bold tracking-tight text-slate-900">{titulo}</h2>}
          {acoes && <div className="flex flex-wrap gap-2">{acoes}</div>}
        </header>
      )}
      {children}
    </section>
  );
}

export const Carregando = ({ texto = 'Carregando…' }: { texto?: string }) =>
  <p className="sub py-6 text-center" role="status">{texto}</p>;

/**
 * Estado vazio: ícone, título e a explicação — em vez de uma caixa cinza com uma frase.
 *
 * `icone="ok"` é para fila limpa ("nada esperando por você"), que é boa notícia;
 * `caixa` para recorte sem dados, que é só ausência.
 */
export function Vazio({ children, titulo, icone = 'caixa', testid }:
  { children: ReactNode; titulo?: string; icone?: 'caixa' | 'ok'; testid?: string }) {
  return (
    <div data-testid={testid} className="flex flex-col items-center gap-2 rounded-lg border border-dashed border-slate-200 px-4 py-8 text-center">
      <span className="text-slate-300">{ICONE[icone]}</span>
      {titulo && <p className="font-semibold text-slate-700">{titulo}</p>}
      <p className="max-w-md text-[13.5px] text-texto-suave">{children}</p>
    </div>
  );
}

export const Erro = ({ children }: { children: ReactNode }) =>
  <p role="alert" className="rounded-lg bg-perigo-fundo px-4 py-3 text-perigo">{children}</p>;

/** Impedimento de regra de negócio: explica por que a ação não está disponível. */
export const Aviso = ({ children, testid }: { children: ReactNode; testid?: string }) =>
  <p role="note" data-testid={testid}
    className="rounded-lg border border-aviso/25 bg-aviso-fundo px-4 py-3 text-[13px] text-aviso">{children}</p>;

export function Badge({ classe, children, title }: { classe: string; children: ReactNode; title?: string }) {
  return <span className={'badge ' + classe} title={title}>{children}</span>;
}

/** Rótulo + valor em duas linhas, para cabeçalhos de detalhe. */
export function Dado({ rotulo, children }: { rotulo: string; children: ReactNode }) {
  return (
    <div>
      <div className="rotulo">{rotulo}</div>
      <div className="mt-0.5 text-[14px]">{children}</div>
    </div>
  );
}

/** Cartão de indicador: rótulo, número grande e a leitura em uma linha. */
export function Kpi({ rotulo, valor, detalhe }: { rotulo: string; valor: ReactNode; detalhe?: ReactNode }) {
  return (
    <div className="rounded-xl border border-slate-200/80 bg-white px-4 py-3.5 shadow-sm">
      <div className="rotulo">{rotulo}</div>
      <div className="mt-1.5 text-3xl font-extrabold leading-none tracking-tight text-slate-900 tabular-nums">{valor}</div>
      {detalhe && <div className="sub mt-1.5">{detalhe}</div>}
    </div>
  );
}

/** Faixa de KPIs que quebra sozinha no celular. */
export const FaixaKpis = ({ children }: { children: ReactNode }) =>
  <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-4">{children}</div>;

/** Seletor de janela dos painéis analíticos (3, 6 ou 12 meses). */
export function SeletorJanela({ id, valor, aoMudar, opcoes }:
  { id: string; valor: number; aoMudar: (v: number) => void; opcoes: readonly number[] }) {
  return (
    <select id={id} aria-label="Janela de análise" className="w-auto" value={valor}
      onChange={(e) => aoMudar(Number(e.target.value))}>
      {opcoes.map((m) => <option key={m} value={m}>Últimos {m} meses</option>)}
    </select>
  );
}
