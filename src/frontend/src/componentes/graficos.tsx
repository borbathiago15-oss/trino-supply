import { moeda, quantidade } from '@/util/formato';

/** Paleta das séries — a mesma ordem de cores do sistema clássico. */
export const CORES = ['#94a3b8', '#2563eb', '#0d9488', '#16a34a', '#f59e0b', '#dc2626'];

export interface Serie { nome: string; cor: string; valores: number[] }

/** `2026-09` → `set/26`; o que não for mês passa direto. */
export function rotuloDoMes(iso: string): string {
  const m = /^(\d{4})-(\d{2})$/.exec(iso);
  if (!m) return iso;
  const nomes = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];
  return `${nomes[Number(m[2]) - 1] ?? m[2]}/${m[1].slice(2)}`;
}

/** Valores grandes viram 1,2 mi / 340 mil — cabe no eixo sem cortar. */
export function moedaCurta(v: number): string {
  const abs = Math.abs(v);
  if (abs >= 1_000_000) return `${(v / 1_000_000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} mi`;
  if (abs >= 1_000) return `${(v / 1_000).toLocaleString('pt-BR', { maximumFractionDigits: 0 })} mil`;
  return quantidade(v);
}

/**
 * Colunas mensais, agrupadas ou empilhadas. SVG puro: o legado já desenhava
 * assim, e uma biblioteca de gráficos custaria mais do que estas duas formas.
 * Cada barra leva um `<title>`, que o navegador mostra como tooltip.
 */
export function GraficoColunas({ rotulos, series, empilhado = false, formatar = quantidade, titulo }: {
  rotulos: string[];
  series: Serie[];
  empilhado?: boolean;
  formatar?: (v: number) => string;
  titulo: string;
}) {
  const A = 175, topo = 12, base = 24;
  const vao = Math.max(40, empilhado ? 40 : series.length * 14 + 16);
  const L = Math.max(320, rotulos.length * vao + 30);
  const totais = rotulos.map((_, i) => empilhado
    ? series.reduce((a, s) => a + (s.valores[i] || 0), 0)
    : Math.max(0, ...series.map((s) => s.valores[i] || 0)));
  const max = Math.max(1e-9, ...totais);
  const y = (v: number) => (A - base) - (v / max) * (A - topo - base);

  if (!rotulos.length) return <p className="sub py-6 text-center">Sem dados no período.</p>;

  return (
    <div className="overflow-x-auto">
      <svg viewBox={`0 0 ${L} ${A}`} style={{ width: '100%' }} role="img" aria-label={titulo}>
        {[0.5, 1].map((f) => (
          <g key={f}>
            <line x1="28" y1={y(max * f)} x2={L} y2={y(max * f)} stroke="currentColor" strokeWidth="1" className="text-borda" />
            <text x="0" y={y(max * f) + 3} fontSize="9" fill="currentColor" className="text-texto-suave">{formatar(max * f)}</text>
          </g>
        ))}
        <line x1="28" y1={A - base} x2={L} y2={A - base} stroke="currentColor" strokeWidth="1" className="text-borda" />
        {rotulos.map((lb, i) => {
          const x0 = 32 + i * vao;
          let acumulado = 0;
          return (
            <g key={lb}>
              {series.map((s, si) => {
                const v = s.valores[i] || 0;
                const altura = (v / max) * (A - topo - base);
                if (empilhado) {
                  if (v <= 0) return null;
                  acumulado += v;
                  return (
                    <rect key={s.nome} x={x0} y={y(acumulado)} width={26} height={Math.max(1, altura - 1)} rx="2" fill={s.cor}>
                      <title>{rotuloDoMes(lb)} — {s.nome}: {formatar(v)}</title>
                    </rect>
                  );
                }
                return (
                  <rect key={s.nome} x={x0 + si * 13} y={y(v)} width={11}
                    height={Math.max(v > 0 ? 2 : 0, altura)} rx="2" fill={s.cor}>
                    <title>{rotuloDoMes(lb)} — {s.nome}: {formatar(v)}</title>
                  </rect>
                );
              })}
              <text x={x0 + (empilhado ? 13 : series.length * 6.5)} y={A - 8} fontSize="9.5"
                fill="currentColor" className="text-texto-suave" textAnchor="middle">
                {rotuloDoMes(lb)}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}

export const Legenda = ({ series }: { series: Serie[] }) => (
  <div className="mb-2 flex flex-wrap gap-3">
    {series.map((s) => (
      <span key={s.nome} className="flex items-center gap-1.5 text-[12px] text-texto-suave">
        <span className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: s.cor }} />
        {s.nome}
      </span>
    ))}
  </div>
);

export interface LinhaRanking { label: string; value?: number; qty?: number; count?: number }

/** Ranking horizontal: rótulo, barra proporcional e valor, todos visíveis. */
export function ListaBarras({ linhas, formatar = moeda, cor = CORES[1] }:
  { linhas: LinhaRanking[]; formatar?: (v: number) => string; cor?: string }) {
  const dados = (linhas ?? []).map((r) => ({ label: r.label, value: Number(r.value ?? r.qty) || 0 }));
  if (!dados.length) return <p className="sub py-4 text-center">Sem dados no período.</p>;
  const max = Math.max(...dados.map((r) => r.value), 1e-9);

  return (
    <div className="flex flex-col gap-1.5">
      {dados.map((r) => (
        <div key={r.label} className="grid grid-cols-[minmax(0,1fr)_2fr_auto] items-center gap-2 text-[12.5px]"
          title={`${r.label}: ${formatar(r.value)}`}>
          <span className="truncate text-texto-suave">{r.label}</span>
          <span className="h-2.5 rounded-full bg-superficie-suave">
            <span className="block h-2.5 rounded-full"
              style={{ width: `${Math.max(2, (r.value / max) * 100)}%`, background: cor }} />
          </span>
          <span className="whitespace-nowrap font-semibold">{formatar(r.value)}</span>
        </div>
      ))}
    </div>
  );
}
