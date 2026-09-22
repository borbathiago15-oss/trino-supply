import type { Analise, Causa } from '@/api/melhoria';
import { Nota } from '@/componentes/formulario';

const pct = (v: number | null) => (v === null ? '—' : `${v.toFixed(1).replace('.', ',')}%`);

/** A marca do vital é a mesma em toda ferramenta: quem elege muda, o que significa não. */
export function Vital({ children }: { children?: React.ReactNode }) {
  return (
    <span className="badge bg-ok-fundo text-ok" title="Causa vital — eleita pela própria ferramenta">
      {children ?? 'VITAL'}
    </span>
  );
}

/**
 * O Pareto em barras. A ordem, o percentual e o acumulado vêm <b>do servidor</b>: quem ordena
 * decide qual é a causa vital, e essa decisão não pode depender de em qual tela se olha.
 */
function ParetoEmBarras({ causas }: { causas: Causa[] }) {
  const maior = Math.max(...causas.map((c) => c.value ?? 0), 1);
  return (
    <div className="mt-2 space-y-1.5" data-testid="pareto">
      {causas.map((c) => (
        <div key={c.label} className="grid grid-cols-[minmax(120px,1fr)_2fr_auto] items-center gap-2 text-[12.5px]">
          <span className="truncate" title={c.label}>{c.label}</span>
          <div className="h-3.5 rounded bg-slate-100">
            <div className={'h-3.5 rounded ' + (c.vital ? 'bg-ok' : 'bg-slate-300')}
              style={{ width: `${Math.round(((c.value ?? 0) / maior) * 100)}%` }} />
          </div>
          <span className="whitespace-nowrap tabular-nums text-texto-suave">
            {pct(c.percent)} · acum. {pct(c.cumulative)} {c.vital && <Vital />}
          </span>
        </div>
      ))}
      <Nota>A faixa vital é a que fecha 80% do acumulado — inclusive a causa que <b>cruza</b> os 80%.</Nota>
    </div>
  );
}

/** O GUT já ordenado, com o produto à vista: é ele que decide, não a ordem de digitação. */
function GutOrdenado({ causas }: { causas: Causa[] }) {
  return (
    <table className="mt-2" data-testid="gut">
      <thead><tr><th>Problema</th><th>Notas</th><th>G×U×T</th><th /></tr></thead>
      <tbody>
        {causas.map((c) => (
          <tr key={c.label}>
            <td>{c.label}</td>
            <td className="sub whitespace-nowrap">{c.detail}</td>
            <td className="tabular-nums font-semibold">{c.value}</td>
            <td>{c.vital && <Vital />}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Os 5 Porquês em escada: cada degrau recua, que é o desenho da ferramenta. */
function EscadaDosPorques({ analise }: { analise: Analise }) {
  return (
    <div className="mt-2" data-testid="cinco-porques">
      {analise.problem && <p className="text-[13px] font-semibold">{analise.problem}</p>}
      {analise.steps.map((d) => (
        <div key={d.number} className="mt-1.5 border-l-2 border-borda pl-3"
          style={{ marginLeft: (d.number - 1) * 14 }}>
          <div className="text-[12.5px] text-texto-suave">{d.question}</div>
          <div className="text-[13px]">{d.answer || '—'}</div>
        </div>
      ))}
      {analise.rootCause && (
        <p className="mt-2 rounded-lg bg-ok-fundo p-2 text-[13px] text-ok">
          <Vital>CAUSA RAIZ</Vital> {analise.rootCause}
        </p>
      )}
    </div>
  );
}

/** O Ishikawa em grade — os 6M lado a lado, que é como se lê o espinha-de-peixe. */
function IshikawaEmGrade({ analise }: { analise: Analise }) {
  return (
    <div className="mt-2" data-testid="ishikawa">
      {analise.effect && <p className="text-[13px]"><b>Efeito:</b> {analise.effect}</p>}
      <div className="mt-2 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {analise.groups.map((g) => (
          <div key={g.key} className="rounded-lg border border-borda p-2.5">
            <div className="text-[12px] font-semibold uppercase tracking-wide text-texto-suave">{g.label}</div>
            {g.items.length === 0
              ? <div className="text-[12.5px] text-texto-suave">—</div>
              : <ul className="mt-1 list-disc pl-4 text-[12.5px]">
                  {g.items.map((i) => <li key={i}>{i}</li>)}
                </ul>}
          </div>
        ))}
      </div>
      <Nota>O espinha-de-peixe levanta as causas; ele não elege nenhuma como vital.</Nota>
    </div>
  );
}

/** A ferramenta preenchida, desenhada do jeito dela. */
export function FerramentaRenderizada({ analise }: { analise: Analise }) {
  if (analise.warning) return <Nota>{analise.warning}</Nota>;
  switch (analise.key) {
    case 'PARETO': return <ParetoEmBarras causas={analise.causes} />;
    case 'GUT': return <GutOrdenado causas={analise.causes} />;
    case 'CINCO_PORQUES': return <EscadaDosPorques analise={analise} />;
    case 'ISHIKAWA': return <IshikawaEmGrade analise={analise} />;
    default:
      return (
        <ul className="mt-2 list-disc pl-5 text-[13px]" data-testid="brainstorming">
          {analise.ideas.map((i) => <li key={i}>{i}</li>)}
        </ul>
      );
  }
}
