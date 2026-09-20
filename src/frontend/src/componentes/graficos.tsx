import { useId, useState, type MouseEvent } from 'react';
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

interface Dica { x: number; y: number; texto: string }

/**
 * Tooltip escuro que flutua sobre o gráfico, na posição do ponteiro.
 * Substitui o `<title>` nativo, que demora um segundo a aparecer e não se estiliza.
 */
function Tooltip({ dica }: { dica: Dica | null }) {
  if (!dica) return null;
  return (
    <div role="tooltip" className="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full whitespace-nowrap
      rounded-lg border border-slate-800 bg-slate-900 p-2 text-xs text-slate-100 shadow-tooltip"
      style={{ left: dica.x, top: dica.y - 8 }}>
      {dica.texto}
    </div>
  );
}

/** Posição do ponteiro relativa ao contêiner do gráfico, para ancorar a dica. */
function posicao(e: MouseEvent<SVGElement>): { x: number; y: number } {
  const caixa = e.currentTarget.ownerSVGElement?.parentElement?.getBoundingClientRect();
  return { x: e.clientX - (caixa?.left ?? 0), y: e.clientY - (caixa?.top ?? 0) };
}

/**
 * Colunas mensais, agrupadas ou empilhadas. SVG puro: o legado já desenhava
 * assim, e uma biblioteca de gráficos custaria mais do que estas duas formas.
 *
 * A barra tem só os cantos de cima arredondados: ela é desenhada 4px mais comprida
 * que o valor e a área do gráfico corta o excesso na linha de base — é o que dá canto
 * redondo em cima e reto embaixo sem trocar o `rect` por um caminho. Numa pilha, a
 * fatia de cima cobre o arredondamento da de baixo, e só o topo da pilha fica redondo.
 */
export function GraficoColunas({ rotulos, series, empilhado = false, formatar = quantidade, titulo }: {
  rotulos: string[];
  series: Serie[];
  empilhado?: boolean;
  formatar?: (v: number) => string;
  titulo: string;
}) {
  const id = useId();
  const [dica, setDica] = useState<Dica | null>(null);
  const A = 175, topo = 12, base = 24, RAIO = 3, SOBRA = 4;
  const vao = Math.max(40, empilhado ? 40 : series.length * 14 + 16);
  const L = Math.max(320, rotulos.length * vao + 30);
  const totais = rotulos.map((_, i) => empilhado
    ? series.reduce((a, s) => a + (s.valores[i] || 0), 0)
    : Math.max(0, ...series.map((s) => s.valores[i] || 0)));
  const max = Math.max(1e-9, ...totais);
  const y = (v: number) => (A - base) - (v / max) * (A - topo - base);
  const gradiente = (si: number) => `url(#${id}-g${si})`;
  const mostrar = (texto: string) => (e: MouseEvent<SVGElement>) => setDica({ ...posicao(e), texto });

  if (!rotulos.length) return <p className="sub py-6 text-center">Sem dados no período.</p>;

  return (
    <div className="relative overflow-x-auto">
      <Tooltip dica={dica} />
      {/* altura máxima e alinhamento à esquerda: com um ou dois meses o gráfico não estica
          até a largura da tela — era o que fazia o rótulo do mês virar um título */}
      <svg viewBox={`0 0 ${L} ${A}`} style={{ width: '100%', maxHeight: 260 }} preserveAspectRatio="xMinYMid meet"
        role="img" aria-label={titulo} onMouseLeave={() => setDica(null)}>
        <defs>
          {series.map((s, si) => (
            <linearGradient key={s.nome} id={`${id}-g${si}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={s.cor} stopOpacity="1" />
              <stop offset="100%" stopColor={s.cor} stopOpacity="0.72" />
            </linearGradient>
          ))}
          <clipPath id={`${id}-area`}><rect x="0" y="0" width={L} height={A - base} /></clipPath>
        </defs>
        {[0.5, 1].map((f) => (
          <g key={f}>
            <line x1="28" y1={y(max * f)} x2={L} y2={y(max * f)} stroke="#f1f5f9" strokeWidth="1" strokeDasharray="3 3" />
            <text x="0" y={y(max * f) + 3} fontSize="9" fill="#94a3b8">{formatar(max * f)}</text>
          </g>
        ))}
        <line x1="28" y1={A - base} x2={L} y2={A - base} stroke="#e2e8f0" strokeWidth="1" />
        <g clipPath={`url(#${id}-area)`}>
          {rotulos.map((lb, i) => {
            const x0 = 32 + i * vao;
            let acumulado = 0;
            return (
              <g key={lb}>
                {series.map((s, si) => {
                  const v = s.valores[i] || 0;
                  const altura = (v / max) * (A - topo - base);
                  const rotulo = `${rotuloDoMes(lb)} — ${s.nome}: ${formatar(v)}`;
                  if (empilhado) {
                    if (v <= 0) return null;
                    acumulado += v;
                    return (
                      <rect key={s.nome} x={x0} y={y(acumulado)} width={26} height={Math.max(1, altura - 1) + SOBRA}
                        rx={RAIO} fill={gradiente(si)} aria-label={rotulo}
                        onMouseEnter={mostrar(rotulo)} onMouseMove={mostrar(rotulo)} />
                    );
                  }
                  return (
                    <rect key={s.nome} x={x0 + si * 13} y={y(v)} width={11}
                      height={Math.max(v > 0 ? 2 : 0, altura) + SOBRA} rx={RAIO} fill={gradiente(si)} aria-label={rotulo}
                      onMouseEnter={mostrar(rotulo)} onMouseMove={mostrar(rotulo)} />
                  );
                })}
              </g>
            );
          })}
        </g>
        {rotulos.map((lb, i) => (
          <text key={lb} x={32 + i * vao + (empilhado ? 13 : series.length * 6.5)} y={A - 8} fontSize="9.5"
            fill="#64748b" textAnchor="middle">
            {rotuloDoMes(lb)}
          </text>
        ))}
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
          <span className="h-2.5 overflow-hidden rounded-full bg-slate-100">
            <span className="block h-2.5 rounded-full"
              style={{ width: `${Math.max(2, (r.value / max) * 100)}%`, background: `linear-gradient(90deg, ${cor}b8, ${cor})` }} />
          </span>
          <span className="whitespace-nowrap font-semibold">{formatar(r.value)}</span>
        </div>
      ))}
    </div>
  );
}
