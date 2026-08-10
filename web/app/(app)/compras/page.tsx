"use client";

import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, OrderView, RequisitionView } from "@/lib/api";
import { Button, Card, Empty, Input, StatusPill, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Perm, useHas } from "@/lib/me";

interface SupplierView2 { id: string; code: string; name: string; taxId: string; status: string }

export default function ComprasPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const has = useHas();
  const [orderSupplier, setOrderSupplier] = useState<Record<string, string>>({});

  const reqs = useQuery({ queryKey: ["requisitions"], queryFn: () => api<RequisitionView[]>("/purchases/requisitions") });
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: () => api<SupplierView2[]>("/purchases/suppliers") });
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => api<OrderView[]>("/purchases/orders") });

  const invalidate = () => { qc.invalidateQueries({ queryKey: ["requisitions"] }); qc.invalidateQueries({ queryKey: ["orders"] }); };
  const onErr = (e: unknown) => toast.push("error", e instanceof ApiError ? e.message : "Erro");
  const ok = (msg: string) => { invalidate(); toast.push("success", msg); };

  const submit = useMutation({ mutationFn: (id: string) => api(`/purchases/requisitions/${id}/submit`, { method: "POST" }), onSuccess: () => ok("Requisição enviada."), onError: onErr });
  const approve = useMutation({ mutationFn: (id: string) => api(`/purchases/requisitions/${id}/approve`, { method: "POST" }), onSuccess: () => ok("Requisição aprovada."), onError: onErr });
  const reject = useMutation({ mutationFn: (id: string) => api(`/purchases/requisitions/${id}/reject`, { method: "POST", body: JSON.stringify({ note: "Rejeitada via UI" }) }), onSuccess: () => ok("Requisição rejeitada."), onError: onErr });
  const issue = useMutation({
    mutationFn: (id: string) => api(`/purchases/requisitions/${id}/order`, { method: "POST", body: JSON.stringify({ supplierCode: orderSupplier[id] ?? "" }) }),
    onSuccess: () => ok("Pedido emitido."), onError: onErr,
  });

  const [sup, setSup] = useState({ code: "", name: "", taxId: "" });
  const createSupplier = useMutation({
    mutationFn: () => api("/purchases/suppliers", { method: "POST", body: JSON.stringify(sup) }),
    onSuccess: () => { setSup({ code: "", name: "", taxId: "" }); qc.invalidateQueries({ queryKey: ["suppliers"] }); toast.push("success", "Fornecedor cadastrado."); },
    onError: onErr,
  });

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Compras</h1>

      <Card title="Requisições">
        {reqs.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : reqs.data && reqs.data.length > 0 ? (
          <Table head={["Requisitante", "Situação", "Itens", "Ações"]}>
            {reqs.data.map((r) => (
              <tr key={r.id}>
                <td className="px-3 py-2">{r.requester}</td>
                <td className="px-3 py-2"><StatusPill status={r.status} /></td>
                <td className="px-3 py-2 text-slate-500">
                  {r.lines.map((l) => `${l.itemCode}×${Number(l.quantity)}`).join(", ")}
                </td>
                <td className="px-3 py-2">
                  <div className="flex flex-wrap items-center gap-2">
                    {r.status === "Draft" && has(Perm.PurchasesRequest) && <Button variant="ghost" onClick={() => submit.mutate(r.id)}>Enviar</Button>}
                    {r.status === "Submitted" && has(Perm.PurchasesApprove) && (
                      <>
                        <Button onClick={() => approve.mutate(r.id)}>Aprovar</Button>
                        <Button variant="danger" onClick={() => reject.mutate(r.id)}>Rejeitar</Button>
                      </>
                    )}
                    {r.status === "Approved" && has(Perm.PurchasesOrder) && (
                      <div className="flex items-center gap-1">
                        <input placeholder="fornecedor" value={orderSupplier[r.id] ?? ""}
                          onChange={(e) => setOrderSupplier({ ...orderSupplier, [r.id]: e.target.value })}
                          className="w-28 rounded-lg border border-slate-300 px-2 py-1 text-sm" />
                        <Button variant="ghost" onClick={() => issue.mutate(r.id)}>Emitir pedido</Button>
                      </div>
                    )}
                    {r.status !== "Draft" && r.status !== "Submitted" && r.status !== "Approved" && (
                      <span className="text-xs text-slate-400">—</span>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Nenhuma requisição. Gere a partir da Reposição ou crie manualmente.</Empty>
        )}
      </Card>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card title="Pedidos emitidos">
          {orders.data && orders.data.length > 0 ? (
            <Table head={["Fornecedor", "Situação", "Itens"]}>
              {orders.data.map((o) => (
                <tr key={o.id}>
                  <td className="px-3 py-2 font-mono text-xs">{o.supplierCode}</td>
                  <td className="px-3 py-2"><StatusPill status={o.status} /></td>
                  <td className="px-3 py-2 text-slate-500">{o.lines.map((l) => `${l.itemCode}×${Number(l.quantity)}`).join(", ")}</td>
                </tr>
              ))}
            </Table>
          ) : (
            <Empty>Nenhum pedido ainda.</Empty>
          )}
        </Card>

        <Card title="Fornecedores">
          {has(Perm.PurchasesOrder) && (
          <form className="mb-4 grid grid-cols-3 gap-2" onSubmit={(e: FormEvent) => { e.preventDefault(); createSupplier.mutate(); }}>
            <Input placeholder="Código" value={sup.code} onChange={(e) => setSup({ ...sup, code: e.target.value })} />
            <Input placeholder="Nome" value={sup.name} onChange={(e) => setSup({ ...sup, name: e.target.value })} />
            <Input placeholder="CNPJ" value={sup.taxId} onChange={(e) => setSup({ ...sup, taxId: e.target.value })} />
            <div className="col-span-3"><Button type="submit" disabled={createSupplier.isPending}>Cadastrar fornecedor</Button></div>
          </form>
          )}
          {suppliers.data && suppliers.data.length > 0 ? (
            <Table head={["Código", "Nome", "Situação"]}>
              {suppliers.data.map((s) => (
                <tr key={s.id}>
                  <td className="px-3 py-2 font-mono text-xs">{s.code}</td>
                  <td className="px-3 py-2">{s.name}</td>
                  <td className="px-3 py-2"><StatusPill status={s.status} /></td>
                </tr>
              ))}
            </Table>
          ) : (
            <Empty>Nenhum fornecedor.</Empty>
          )}
        </Card>
      </div>
    </>
  );
}
