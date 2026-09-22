import { useEffect, useRef, useState } from 'react';
import { precisaRolar } from './cockpit';

/**
 * As peças do modo TV. Todas partem do mesmo princípio: a tela é lida de cinco metros,
 * por quem está de pé, e ninguém toca nela.
 */

/** O ponto que pulsa enquanto os dados chegam — é como a sala sabe que a tela está viva. */
export function PulsoAoVivo({ vivo }: { vivo: boolean }) {
  return (
    <span className="inline-flex items-center gap-2" data-testid="pulso-ao-vivo">
      <span className={`relative flex h-3 w-3 ${vivo ? '' : 'opacity-40'}`}>
        {vivo && (
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
        )}
        <span className={`relative inline-flex h-3 w-3 rounded-full ${vivo ? 'bg-emerald-400' : 'bg-slate-500'}`} />
      </span>
      <span className="text-[13px] font-semibold tracking-widest text-emerald-300">
        {vivo ? 'AO VIVO' : 'SEM SINAL'}
      </span>
    </span>
  );
}

/**
 * Um número que troca sem piscar. O valor anterior fica na tela até o novo chegar, e a
 * troca acende brevemente: numa TV, o número que some e volta parece defeito.
 */
export function NumeroVivo({ valor, className = '' }: { valor: string; className?: string }) {
  const [trocando, setTrocando] = useState(false);
  const anterior = useRef(valor);
  useEffect(() => {
    if (anterior.current === valor) return;
    anterior.current = valor;
    setTrocando(true);
    const id = setTimeout(() => setTrocando(false), 600);
    return () => clearTimeout(id);
  }, [valor]);
  return (
    <span
      data-testid="numero-vivo"
      className={`tabular-nums transition-colors duration-500 ${trocando ? 'text-sky-300' : ''} ${className}`}>
      {valor}
    </span>
  );
}

/**
 * Lista que rola sozinha quando passa de cinco itens. Sem barra: a TV não tem quem arraste.
 * A animação é CSS puro para não custar quadro nenhum numa tela que fica ligada o dia todo.
 */
export function ListaRolante({ itens, children }: { itens: number; children: React.ReactNode }) {
  const rola = precisaRolar(itens);
  return (
    <div className="relative flex-1 overflow-hidden" data-testid="lista-rolante" data-rolando={rola}>
      <div className={rola ? 'animate-[rolar_40s_linear_infinite]' : ''}>{children}</div>
    </div>
  );
}

/** Um cartão do nível 2, com o número grande e o rodapé de contexto. */
export function CartaoVital({
  icone, titulo, children, rodape, tom = 'text-white',
}: {
  icone: string; titulo: string; children: React.ReactNode;
  rodape?: React.ReactNode; tom?: string;
}) {
  return (
    <section className="flex flex-col justify-between rounded-2xl border border-slate-800 bg-slate-900/90 p-5 shadow-2xl">
      <h2 className="flex items-center gap-2 text-[13px] font-semibold uppercase tracking-wider text-slate-400">
        <span aria-hidden>{icone}</span>{titulo}
      </h2>
      <div className={`mt-3 font-mono text-5xl font-bold tracking-tight ${tom}`}>{children}</div>
      {rodape && <div className="mt-2 text-[13px] text-slate-400">{rodape}</div>}
    </section>
  );
}

/** A barra de meta dos cartões de saving e SLA. `progresso` nulo esconde a barra. */
export function BarraDeMeta({ progresso, atingiu }: { progresso: number | null; atingiu?: boolean }) {
  if (progresso === null) return <span className="text-slate-500">sem meta definida</span>;
  return (
    <div className="h-2 w-full overflow-hidden rounded-full bg-slate-800" data-testid="barra-de-meta">
      <div
        className={`h-full rounded-full transition-all duration-700 ${atingiu ? 'bg-emerald-400' : 'bg-sky-400'}`}
        style={{ width: `${Math.round(progresso * 100)}%` }} />
    </div>
  );
}
