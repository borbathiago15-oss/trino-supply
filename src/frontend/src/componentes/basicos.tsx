import type { ReactNode } from 'react';

export function Painel({ titulo, acoes, children, className = '', id }:
  { titulo?: ReactNode; acoes?: ReactNode; children: ReactNode; className?: string; id?: string }) {
  return (
    <section id={id} className={'painel ' + className}>
      {(titulo || acoes) && (
        <header className="mb-3 flex flex-wrap items-center justify-between gap-2">
          {titulo && <h2 className="text-[15px] font-bold">{titulo}</h2>}
          {acoes && <div className="flex flex-wrap gap-2">{acoes}</div>}
        </header>
      )}
      {children}
    </section>
  );
}

export const Carregando = ({ texto = 'Carregando…' }: { texto?: string }) =>
  <p className="sub py-6 text-center" role="status">{texto}</p>;

export const Vazio = ({ children }: { children: ReactNode }) =>
  <p className="rounded-lg bg-superficie-suave px-4 py-5 text-center text-texto-suave">{children}</p>;

export const Erro = ({ children }: { children: ReactNode }) =>
  <p role="alert" className="rounded-lg bg-perigo-fundo px-4 py-3 text-perigo">{children}</p>;

export function Badge({ classe, children, title }: { classe: string; children: ReactNode; title?: string }) {
  return <span className={'badge ' + classe} title={title}>{children}</span>;
}

/** Rótulo + valor em duas linhas, para cabeçalhos de detalhe. */
export function Dado({ rotulo, children }: { rotulo: string; children: ReactNode }) {
  return (
    <div>
      <div className="text-[11.5px] font-bold uppercase tracking-wide text-texto-suave">{rotulo}</div>
      <div className="mt-0.5 text-[14px]">{children}</div>
    </div>
  );
}

/** Cartão de indicador: rótulo, número grande e a leitura em uma linha. */
export function Kpi({ rotulo, valor, detalhe }: { rotulo: string; valor: ReactNode; detalhe?: ReactNode }) {
  return (
    <div className="rounded-painel border border-borda bg-superficie px-4 py-3">
      <div className="text-[11.5px] font-bold uppercase tracking-wide text-texto-suave">{rotulo}</div>
      <div className="mt-1 text-[22px] font-bold leading-tight">{valor}</div>
      {detalhe && <div className="sub mt-0.5">{detalhe}</div>}
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
