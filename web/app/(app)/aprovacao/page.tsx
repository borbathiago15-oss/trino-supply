"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, ApprovalsView } from "@/lib/api";
import { Button, Card, Empty, StatusPill } from "@/components/ui";
import { useToast } from "@/lib/toast";

/**
 * Central de Aprovação (v2): a fila do aprovador — só o que aguarda a decisão DELE (pedidos de compra
 * na etapa dele, nível 1/2, e solicitações de EPI/fardamento onde ele é o gestor), já limitada aos
 * centros de custo sob sua responsabilidade. Ações: Aprovar · Reprovar (com motivo) · Cancelar.
 */
export default function AprovacaoPage() {
  const qc = useQueryClient();
  const toast = useToast();

  const approvals = useQuery({ queryKey: ["approvals"], queryFn: () => api<ApprovalsView>("/purchases/approvals") });

  const done = (msg: string) => {
    qc.invalidateQueries({ queryKey: ["approvals"] });
    qc.invalidateQueries({ queryKey: ["requisitions"] });
    qc.invalidateQueries({ queryKey: ["stock-requests"] });
    toast.push("success", msg);
  };
  const onErr = (e: unknown) => toast.push("error", e instanceof ApiError ? e.message : "Erro");

  const act = useMutation({
    mutationFn: ({ path, note }: { path: string; note?: string }) =>
      api(path, { method: "POST", body: note !== undefined ? JSON.stringify({ note }) : undefined }),
    onSuccess: () => done("Decisão registrada."),
    onError: onErr,
  });

  const reprovar = (path: string) => {
    const note = typeof window !== "undefined" ? window.prompt("Motivo da reprovação:") : null;
    if (note && note.trim()) act.mutate({ path, note: note.trim() });
  };

  const reqs = approvals.data?.requisitions ?? [];
  const stock = approvals.data?.stockRequests ?? [];
  const total = reqs.length + stock.length;

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Central de Aprovação</h1>
      <p className="-mt-3 text-sm text-slate-500">
        Sua fila: apenas o que aguarda a <strong>sua</strong> decisão, nos centros de custo sob sua
        responsabilidade. {total > 0 ? `${total} pendência${total > 1 ? "s" : ""}.` : ""}
      </p>

      <Card title={`Pedidos de compra (${reqs.length})`}>
        {approvals.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : reqs.length > 0 ? (
          <div className="space-y-3">
            {reqs.map((r) => (
              <div key={r.id} className="rounded-lg border border-slate-200 p-3">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <StatusPill status={r.status} />
                      {r.priority === "Emergencial" && (
                        <span className="rounded-full bg-rose-100 px-2 py-0.5 text-xs font-medium text-rose-700">Emergencial</span>
                      )}
                      <span className="text-sm text-slate-600">solicitante {r.requester}</span>
                    </div>
                    <div className="mt-1 text-xs text-slate-500">
                      {r.payingCompanyName} · {r.costCenterCode} — {r.costCenterName}
                    </div>
                    <div className="mt-0.5 text-xs text-slate-500">
                      {r.lines.map((l) => `${l.itemCode}×${Number(l.quantity)} ${l.unit}`).join(", ")}
                    </div>
                    {r.justification && <div className="mt-0.5 text-xs text-slate-400">Motivo: {r.justification}</div>}
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button onClick={() => act.mutate({ path: `/purchases/requisitions/${r.id}/approve` })}>Aprovar</Button>
                    <Button variant="danger" onClick={() => reprovar(`/purchases/requisitions/${r.id}/reject`)}>Reprovar</Button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <Empty>Nenhum pedido de compra aguardando você.</Empty>
        )}
      </Card>

      <Card title={`Solicitações de EPI/Fardamento (${stock.length})`}>
        {approvals.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : stock.length > 0 ? (
          <div className="space-y-3">
            {stock.map((r) => (
              <div key={r.id} className="rounded-lg border border-slate-200 p-3">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <StatusPill status={r.status} />
                      <span className="text-sm text-slate-600">{r.reason}</span>
                      <span className="text-xs text-slate-400">· {r.companyCode}/{r.costCenterCode} · solicitante {r.requesterSubject}</span>
                    </div>
                    <div className="mt-1 text-xs text-slate-500">
                      {r.lines.map((l) => `${l.itemCode}×${Number(l.quantity)}`).join(", ")}
                    </div>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button onClick={() => act.mutate({ path: `/materials/requests/${r.id}/approve` })}>Aprovar</Button>
                    <Button variant="danger" onClick={() => reprovar(`/materials/requests/${r.id}/reject`)}>Reprovar</Button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <Empty>Nenhuma solicitação de EPI/fardamento aguardando você.</Empty>
        )}
      </Card>
    </>
  );
}
