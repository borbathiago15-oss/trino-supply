"use client";

import { useQuery } from "@tanstack/react-query";
import { api, ComplianceView } from "@/lib/api";

const CORES: Record<string, string> = {
  Exemplar: "bg-emerald-100 text-emerald-700",
  Aceitavel: "bg-sky-100 text-sky-700",
  Atencao: "bg-amber-100 text-amber-800",
  Critico: "bg-rose-100 text-rose-700",
};
const ROTULOS: Record<string, string> = { Aceitavel: "Aceitável", Atencao: "Atenção", Critico: "Crítico" };

/** Varredura de compliance de todas as OCs vivas, indexada por id — uma consulta para a lista. */
export function useCompliance() {
  const q = useQuery({
    queryKey: ["compliance"],
    staleTime: 60_000,
    queryFn: () => api<ComplianceView[]>("/purchases/compliance"),
  });
  const porOc = new Map((q.data ?? []).map((c) => [c.orderId, c]));
  return { lista: q.data ?? [], porOc: (id: string) => porOc.get(id) };
}

/**
 * Nota de governança do processo de compra. Compacta na lista (só o número e a faixa); detalhada
 * no pedido, com cada penalidade e a evidência — o auditor não precisa refazer a conta.
 */
export function ComplianceBadge({ c, detalhado = false }: { c?: ComplianceView; detalhado?: boolean }) {
  if (!c) return <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-500">—</span>;
  const cor = CORES[c.band] ?? CORES.Atencao;

  return (
    <div className={detalhado ? "space-y-2" : "inline-flex"}>
      <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${cor}`}
        title={c.summary}>
        Score {c.score}/100 · {ROTULOS[c.band] ?? c.band}
      </span>
      {detalhado && (
        c.penalties.length === 0 ? (
          <p className="text-xs text-slate-500">Sem desvios de governança neste processo.</p>
        ) : (
          <ul className="space-y-1 text-xs">
            {c.penalties.map((p) => (
              <li key={p.rule} className="flex gap-2">
                <span className="shrink-0 font-mono text-rose-600">−{p.points}</span>
                <span><strong className="text-slate-700">{p.title}</strong> — <span className="text-slate-500">{p.evidence}</span></span>
              </li>
            ))}
          </ul>
        )
      )}
      {detalhado && (
        <p className="text-[11px] text-slate-400">
          {c.quotationResponses < 0 ? "Compra direta, sem cotação." : `${c.quotationResponses} proposta(s) na cotação.`}
          {" "}Homologação do fornecedor lida como está hoje.
        </p>
      )}
    </div>
  );
}

/** Detalhe de um pedido: busca o score da OC e mostra as penalidades com evidência. */
export function ComplianceDetalhe({ orderId }: { orderId: string }) {
  const q = useQuery({
    queryKey: ["compliance", orderId],
    queryFn: () => api<ComplianceView>(`/purchases/orders/${orderId}/compliance`),
  });
  if (q.isLoading) return <p className="text-xs text-slate-400">Calculando o score…</p>;
  return <ComplianceBadge c={q.data} detalhado />;
}
