"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api, ApiError, CenterConsumptionRow, CollaboratorConsumptionRow, ItemView, Suggestion } from "@/lib/api";
import { Button, Card, Empty, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Perm, useHas } from "@/lib/me";
import { ReqHeader, RequisitionHeaderFields, emptyHeader, headerError, useRequisitionRefData } from "@/components/requisitionHeader";

/** Dashboard de Estoque (v2): alimentado pelo Estoque/Almox — famílias, reposição e ponte p/ compra. */
export default function DashboardEstoquePage() {
  const qc = useQueryClient();
  const router = useRouter();
  const toast = useToast();
  const has = useHas();
  const [open, setOpen] = useState(false);
  const [header, setHeader] = useState<ReqHeader>(emptyHeader);

  const items = useQuery({ queryKey: ["items"], queryFn: () => api<ItemView[]>("/materials/items") });
  const suggestions = useQuery({ queryKey: ["suggestions"], queryFn: () => api<Suggestion[]>("/materials/replenishment/suggestions") });
  const porCentro = useQuery({
    queryKey: ["consumption-by-center"],
    queryFn: () => api<CenterConsumptionRow[]>("/materials/analytics/consumption-by-center?days=90"),
  });
  const porColab = useQuery({
    queryKey: ["consumption-by-collaborator"],
    queryFn: () => api<CollaboratorConsumptionRow[]>("/materials/analytics/consumption-by-collaborator?days=90"),
  });
  const { paying, centers, approvers } = useRequisitionRefData(open);

  const generate = useMutation({
    mutationFn: () => api<{ requisitionId: string; lines: number }>("/purchases/requisitions/from-suggestions",
      { method: "POST", body: JSON.stringify(header) }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      setOpen(false); setHeader(emptyHeader);
      toast.push("success", `Pedido criado com ${r.lines} item(ns).`);
      setTimeout(() => router.push("/compras"), 700);
    },
    onError: (e) => toast.push("error", e instanceof ApiError ? e.message : "Erro"),
  });

  const gerar = () => {
    const err = headerError(header);
    if (err) { toast.push("error", err); return; }
    generate.mutate();
  };

  const list = items.data ?? [];
  // Total de saídas por centro (soma das quantidades) para as barras.
  const centros = [...(porCentro.data ?? []).reduce((m, r) => m.set(r.costCenterCode, (m.get(r.costCenterCode) ?? 0) + Number(r.totalQuantity)), new Map<string, number>())]
    .sort((a, b) => b[1] - a[1]);
  const maxCentro = centros[0]?.[1] ?? 1;
  const familias = [...list.reduce((m, it) => m.set(it.group, (m.get(it.group) ?? 0) + 1), new Map<string, number>())]
    .sort((a, b) => b[1] - a[1]);
  const maxFam = familias[0]?.[1] ?? 1;
  const count = suggestions.data?.length ?? 0;

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Dashboard de Estoque</h1>
      <p className="-mt-3 text-sm text-slate-500">
        Alimentado pelo <Link href="/materiais" className="text-brand underline">Estoque (Almox)</Link>:
        famílias de produto, itens no ponto de reposição e a ponte para o pedido de compra.
      </p>

      <div className="grid gap-4 sm:grid-cols-3">
        {[
          { label: "Itens cadastrados", value: list.length },
          { label: "Famílias", value: familias.length },
          { label: "No ponto de reposição", value: count, accent: true },
        ].map((s) => (
          <div key={s.label} className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
            <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{s.label}</p>
            <p className={`mt-2 text-3xl font-semibold ${s.accent ? "text-brand" : "text-slate-800"}`}>{s.value}</p>
          </div>
        ))}
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card title="Itens por família">
          {familias.length > 0 ? (
            <div className="space-y-2">
              {familias.map(([fam, n]) => (
                <div key={fam} className="grid grid-cols-[130px_1fr_36px] items-center gap-2 text-sm">
                  <span className="truncate text-slate-600">{fam}</span>
                  <span className="h-2.5 overflow-hidden rounded-full border border-slate-200 bg-slate-50">
                    <span className="block h-full rounded-full bg-brand" style={{ width: `${(n / maxFam) * 100}%` }} />
                  </span>
                  <span className="text-right tabular-nums text-slate-500">{n}</span>
                </div>
              ))}
            </div>
          ) : (
            <Empty>Nenhum item cadastrado ainda.</Empty>
          )}
        </Card>

        <Card
          title={`Reposição sugerida (${count})`}
          actions={has(Perm.PurchasesRequest) ? (
            <Button disabled={count === 0} onClick={() => setOpen((v) => !v)}>
              {open ? "Fechar" : "Gerar pedido das sugestões"}
            </Button>
          ) : undefined}
        >
          {count > 0 ? (
            <Table head={["Item", "Saldo", "Mínimo", "Máximo", "Comprar"]}>
              {suggestions.data!.map((s) => (
                <tr key={s.itemId}>
                  <td className="px-3 py-2">
                    <span className="font-mono text-xs">{s.itemCode}</span>{" "}
                    <span className="text-slate-500">{s.itemName}</span>
                  </td>
                  <td className="px-3 py-2 tabular-nums">{Number(s.balance)}</td>
                  <td className="px-3 py-2 tabular-nums text-slate-500">{Number(s.minLevel)}</td>
                  <td className="px-3 py-2 tabular-nums text-slate-500">{Number(s.maxLevel)}</td>
                  <td className="px-3 py-2 font-semibold tabular-nums text-brand">{Number(s.suggestedQuantity)}</td>
                </tr>
              ))}
            </Table>
          ) : (
            <Empty>Nenhum item no ponto de reposição. Defina políticas (mín/máx) no Estoque.</Empty>
          )}
        </Card>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card title="Consumo por centro de custo (90 dias)">
          {centros.length > 0 ? (
            <div className="space-y-2">
              {centros.map(([cc, total]) => (
                <div key={cc} className="grid grid-cols-[130px_1fr_60px] items-center gap-2 text-sm">
                  <span className="truncate font-mono text-xs text-slate-600">{cc}</span>
                  <span className="h-2.5 overflow-hidden rounded-full border border-slate-200 bg-slate-50">
                    <span className="block h-full rounded-full bg-brand" style={{ width: `${(total / maxCentro) * 100}%` }} />
                  </span>
                  <span className="text-right tabular-nums text-slate-500">{total}</span>
                </div>
              ))}
              <p className="pt-1 text-xs text-slate-400">Soma das quantidades de saída identificadas por centro.</p>
            </div>
          ) : (
            <Empty>Nenhuma saída com centro de custo na janela.</Empty>
          )}
        </Card>

        <Card title="Consumo por colaborador (90 dias)">
          {(porColab.data ?? []).length > 0 ? (
            <Table head={["Colaborador", "Matrícula", "Centro", "Entregas", "Itens"]}>
              {porColab.data!.slice(0, 10).map((r) => (
                <tr key={`${r.collaboratorName}-${r.registration ?? ""}`}>
                  <td className="px-3 py-2">{r.collaboratorName}</td>
                  <td className="px-3 py-2 font-mono text-xs text-slate-500">{r.registration || "—"}</td>
                  <td className="px-3 py-2 font-mono text-xs text-slate-500">{r.costCenterCode}</td>
                  <td className="px-3 py-2 tabular-nums">{r.deliveries}</td>
                  <td className="px-3 py-2 tabular-nums font-semibold">{Number(r.totalItems)}</td>
                </tr>
              ))}
            </Table>
          ) : (
            <Empty>Nenhuma entrega de EPI/fardamento na janela.</Empty>
          )}
        </Card>
      </div>

      {open && (
        <Card title="Cabeçalho do pedido (a partir das sugestões)">
          <RequisitionHeaderFields value={header} onChange={setHeader} paying={paying.data} centers={centers.data} approvers={approvers.data} />
          <div className="mt-4">
            <Button disabled={generate.isPending} onClick={gerar}>
              {generate.isPending ? "Gerando…" : `Gerar pedido (${count} item${count === 1 ? "" : "ns"})`}
            </Button>
          </div>
        </Card>
      )}
    </>
  );
}
