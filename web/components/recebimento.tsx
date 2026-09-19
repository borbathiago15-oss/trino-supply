"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, OrderReceiptSummary } from "@/lib/api";
import { Button, Empty, Input, Select, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";

const OCORRENCIAS = ["Avaria", "Falta", "Excesso", "Divergencia"] as const;

type LinhaConferida = { recebida: string; avariada: string; ocorrencia: string; nota: string };
const vazia: LinhaConferida = { recebida: "", avariada: "", ocorrencia: "", nota: "" };
const num = (v: string) => Number((v ?? "").replace(",", ".")) || 0;

/**
 * Conferência física do recebimento (MMS-005): pedido × entregue × avariado, por linha da OC.
 * A quantidade líquida (recebido − avariado) entra no estoque do Almox automaticamente.
 * Tela pensada para a doca: poucos campos, números grandes e o que falta receber sempre à vista.
 */
export function ConferenciaRecebimento({ orderId }: { orderId: string }) {
  const qc = useQueryClient();
  const toast = useToast();
  const [aberto, setAberto] = useState(false);
  const [nf, setNf] = useState({ invoiceNumber: "", invoiceDate: "", notes: "" });
  const [linhas, setLinhas] = useState<Record<string, LinhaConferida>>({});

  const resumo = useQuery({
    queryKey: ["receipts", orderId],
    queryFn: () => api<OrderReceiptSummary>(`/purchases/orders/${orderId}/receipts`),
  });

  const linha = (id: string) => linhas[id] ?? vazia;
  const set = (id: string, patch: Partial<LinhaConferida>) =>
    setLinhas({ ...linhas, [id]: { ...linha(id), ...patch } });

  const pendentes = (resumo.data?.lines ?? []).filter((l) => l.quantityPending > 0);
  const entregas = resumo.data?.receipts ?? [];

  const registrar = useMutation({
    mutationFn: () => {
      const corpo = {
        invoiceNumber: nf.invoiceNumber,
        invoiceDate: nf.invoiceDate || null,
        notes: nf.notes || null,
        lines: pendentes
          .filter((l) => num(linha(l.orderLineId).recebida) > 0)
          .map((l) => ({
            orderLineId: l.orderLineId,
            quantityReceived: num(linha(l.orderLineId).recebida),
            quantityDamaged: num(linha(l.orderLineId).avariada),
            occurrence: linha(l.orderLineId).ocorrencia || null,
            occurrenceNote: linha(l.orderLineId).nota || null,
          })),
      };
      return api<{ stockPosted: boolean; itemsNotInCatalog: string[]; orderComplete: boolean }>(
        `/purchases/orders/${orderId}/receipts`, { method: "POST", body: JSON.stringify(corpo) },
      );
    },
    onSuccess: (r) => {
      setAberto(false);
      setNf({ invoiceNumber: "", invoiceDate: "", notes: "" });
      setLinhas({});
      qc.invalidateQueries({ queryKey: ["receipts", orderId] });
      qc.invalidateQueries({ queryKey: ["orders"] });
      qc.invalidateQueries({ queryKey: ["balance"] });
      toast.push("success", r.stockPosted
        ? `Recebimento registrado — estoque atualizado${r.orderComplete ? " e OC encerrada" : ""}.`
        : "Recebimento registrado, mas nada entrou no estoque.");
      if (r.itemsNotInCatalog?.length > 0) {
        toast.push("error", `Fora do catálogo do Almox (sem entrada): ${r.itemsNotInCatalog.join(", ")}.`);
      }
    },
    onError: (e) => toast.push("error", e instanceof ApiError ? e.message : "Erro"),
  });

  const enviar = () => {
    if (!nf.invoiceNumber.trim()) { toast.push("error", "Informe o número da nota fiscal."); return; }
    const algumaQuantidade = pendentes.some((l) => num(linha(l.orderLineId).recebida) > 0);
    if (!algumaQuantidade) { toast.push("error", "Informe a quantidade recebida de ao menos um item."); return; }
    registrar.mutate();
  };

  return (
    <div className="mt-4 border-t border-slate-200 pt-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 className="text-sm font-semibold text-slate-700">Recebimento (conferência física)</h4>
        {pendentes.length > 0 && (
          <Button onClick={() => setAberto((v) => !v)}>
            {aberto ? "Fechar" : `Conferir entrega (${pendentes.length} item${pendentes.length === 1 ? "" : "ns"} a receber)`}
          </Button>
        )}
      </div>

      {resumo.isLoading ? (
        <Empty>Carregando…</Empty>
      ) : (
        <>
          <div className="mt-2 overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead>
                <tr className="text-slate-400">
                  <th className="px-2 py-1">Item</th>
                  <th className="px-2 py-1 text-right">Pedido</th>
                  <th className="px-2 py-1 text-right">Já recebido</th>
                  <th className="px-2 py-1 text-right">Falta</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {(resumo.data?.lines ?? []).map((l) => (
                  <tr key={l.orderLineId} className={l.quantityPending === 0 ? "text-slate-400" : ""}>
                    <td className="px-2 py-1 font-mono">{l.itemCode}</td>
                    <td className="px-2 py-1 text-right tabular-nums">{Number(l.quantityOrdered)} {l.unit}</td>
                    <td className="px-2 py-1 text-right tabular-nums">{Number(l.quantityAlreadyReceived)}</td>
                    <td className="px-2 py-1 text-right tabular-nums font-semibold">
                      {l.quantityPending === 0 ? "✓" : Number(l.quantityPending)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {aberto && (
            <div className="mt-3 space-y-3 rounded-lg border border-slate-200 bg-white p-3">
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                <Input label="Nota fiscal" value={nf.invoiceNumber}
                  onChange={(e) => setNf({ ...nf, invoiceNumber: e.target.value })} />
                <Input label="Data da NF" type="date" value={nf.invoiceDate}
                  onChange={(e) => setNf({ ...nf, invoiceDate: e.target.value })} />
                <Input label="Observações" value={nf.notes}
                  onChange={(e) => setNf({ ...nf, notes: e.target.value })} />
              </div>

              <Table head={["Item", "Falta receber", "Recebida", "Avariada", "Ocorrência", "Descrição"]}>
                {pendentes.map((l) => {
                  const c = linha(l.orderLineId);
                  const exigeOcorrencia = num(c.avariada) > 0 || num(c.recebida) > l.quantityPending;
                  return (
                    <tr key={l.orderLineId}>
                      <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
                      <td className="px-3 py-2 tabular-nums">{Number(l.quantityPending)} {l.unit}</td>
                      <td className="px-3 py-2">
                        <input inputMode="decimal" value={c.recebida} placeholder="0"
                          onChange={(e) => set(l.orderLineId, { recebida: e.target.value })}
                          className="w-24 rounded-lg border border-slate-300 px-2 py-1 text-base font-semibold" />
                      </td>
                      <td className="px-3 py-2">
                        <input inputMode="decimal" value={c.avariada} placeholder="0"
                          onChange={(e) => set(l.orderLineId, { avariada: e.target.value })}
                          className="w-24 rounded-lg border border-slate-300 px-2 py-1 text-base" />
                      </td>
                      <td className="px-3 py-2">
                        <select value={c.ocorrencia} onChange={(e) => set(l.orderLineId, { ocorrencia: e.target.value })}
                          className={`rounded-lg border px-2 py-1 text-sm ${exigeOcorrencia && !c.ocorrencia ? "border-rose-400 bg-rose-50" : "border-slate-300"}`}>
                          <option value="">Conforme</option>
                          {OCORRENCIAS.map((o) => <option key={o} value={o}>{o}</option>)}
                        </select>
                      </td>
                      <td className="px-3 py-2">
                        <input value={c.nota} placeholder={c.ocorrencia ? "descreva a não-conformidade" : "—"}
                          disabled={!c.ocorrencia}
                          onChange={(e) => set(l.orderLineId, { nota: e.target.value })}
                          className="w-56 rounded-lg border border-slate-300 px-2 py-1 text-sm disabled:bg-slate-50" />
                      </td>
                    </tr>
                  );
                })}
              </Table>

              <p className="text-xs text-slate-400">
                Entra no estoque o líquido de cada linha (recebida − avariada). Avaria ou quantidade acima do
                pendente exigem ocorrência classificada e descrita — é o que aciona a devolução ao fornecedor.
              </p>

              <div className="flex justify-end gap-2">
                <Button variant="ghost" onClick={() => setAberto(false)}>Cancelar</Button>
                <Button onClick={enviar} disabled={registrar.isPending}>
                  {registrar.isPending ? "Registrando…" : "Registrar recebimento"}
                </Button>
              </div>
            </div>
          )}

          {entregas.length > 0 && (
            <div className="mt-3">
              <p className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-400">Entregas recebidas</p>
              <div className="space-y-2">
                {entregas.map((e) => (
                  <div key={e.id} className="rounded-lg border border-slate-200 p-2 text-xs">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium text-slate-700">NF {e.invoiceNumber}</span>
                      <span className="text-slate-400">
                        {new Date(e.receivedAt).toLocaleDateString("pt-BR")} · por {e.receivedBy}
                      </span>
                      {e.stockPosted
                        ? <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-emerald-700">estoque atualizado</span>
                        : <span className="rounded-full bg-amber-100 px-2 py-0.5 text-amber-700">sem entrada no estoque</span>}
                    </div>
                    <div className="mt-1 text-slate-600">
                      {e.lines.map((l) => (
                        <div key={l.orderLineId + l.itemCode}>
                          {l.itemCode}: recebido {Number(l.quantityReceived)}
                          {l.quantityDamaged > 0 && <span className="text-rose-600"> · avariado {Number(l.quantityDamaged)}</span>}
                          {" "}→ entrou {Number(l.netQuantity)}
                          {l.occurrence !== "None" && (
                            <span className="text-rose-600"> · {l.occurrence}: {l.occurrenceNote}</span>
                          )}
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
