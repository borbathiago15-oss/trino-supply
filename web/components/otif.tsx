"use client";

import { useQuery } from "@tanstack/react-query";
import { api, SupplierScoreView } from "@/lib/api";

const CORES: Record<string, string> = {
  Ouro: "bg-amber-100 text-amber-800",
  Prata: "bg-slate-200 text-slate-700",
  Bronze: "bg-orange-100 text-orange-800",
  Critico: "bg-rose-100 text-rose-700",
  SemDados: "bg-slate-100 text-slate-500",
};
const ROTULOS: Record<string, string> = { Critico: "Crítico", SemDados: "sem histórico" };

/** Scorecard OTIF de todos os fornecedores na janela — uma consulta, indexada por id e por código. */
export function useScorecards(months = 12) {
  const q = useQuery({
    queryKey: ["supplier-scorecard", months],
    staleTime: 60_000,
    queryFn: () => api<SupplierScoreView[]>(`/purchases/suppliers/scorecard?months=${months}`),
  });
  const ranking = q.data ?? [];   // já vem ordenado por OTIF desc
  const porId = new Map(ranking.map((s) => [s.supplierId, s]));
  const porCodigo = new Map(ranking.map((s) => [s.supplierCode, s]));
  return {
    ranking,
    porId: (id?: string) => porId.get(id ?? ""),
    porCodigo: (c?: string) => porCodigo.get(c ?? ""),
  };
}

/**
 * Nota de entrega do fornecedor (OTIF): % de linhas entregues no prazo E completas, com a faixa
 * de desempenho. Sem linha com prazo prometido na OC não há o que classificar.
 */
export function OtifBadge({ score, detalhado = false }: { score?: SupplierScoreView; detalhado?: boolean }) {
  if (!score || score.linesEvaluated === 0) {
    return <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-500">sem entregas</span>;
  }
  const cor = CORES[score.tier] ?? CORES.SemDados;
  const faixa = ROTULOS[score.tier] ?? score.tier;

  return (
    <span className="inline-flex items-center gap-1">
      <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${cor}`}
        title={`No prazo ${score.onTimeRate}% · Completo ${score.inFullRate}% · Avaria ${score.damageRate}%`}>
        {score.tier === "SemDados" ? faixa : `OTIF ${score.otifIndex}% · ${faixa}`}
      </span>
      {detalhado && (
        <span className="text-xs text-slate-400">
          {score.linesEvaluated} entrega(s)
          {score.linesWithoutDeadline > 0 && ` · ${score.linesWithoutDeadline} sem prazo na OC`}
          {score.occurrences > 0 && ` · ${score.occurrences} ocorrência(s)`}
        </span>
      )}
    </span>
  );
}
