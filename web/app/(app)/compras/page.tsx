"use client";

import { FormEvent, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  api, ApiError, download, upload, OrderView, PayingCompanyView, RequisitionView, SupplierFullView,
} from "@/lib/api";
import { Button, Card, Empty, Input, Select, StatusPill, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Perm, useHas } from "@/lib/me";

const money = (v: number) => Number(v).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export default function ComprasPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const has = useHas();

  const reqs = useQuery({ queryKey: ["requisitions"], queryFn: () => api<RequisitionView[]>("/purchases/requisitions") });
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => api<OrderView[]>("/purchases/orders") });

  const invalidate = () => { qc.invalidateQueries({ queryKey: ["requisitions"] }); qc.invalidateQueries({ queryKey: ["orders"] }); };
  const onErr = (e: unknown) => toast.push("error", e instanceof ApiError ? e.message : "Erro");
  const ok = (msg: string) => { invalidate(); toast.push("success", msg); };

  const submit = useMutation({ mutationFn: (id: string) => api(`/purchases/requisitions/${id}/submit`, { method: "POST" }), onSuccess: () => ok("Requisição enviada."), onError: onErr });
  const approve = useMutation({ mutationFn: (id: string) => api(`/purchases/requisitions/${id}/approve`, { method: "POST" }), onSuccess: () => ok("Requisição aprovada."), onError: onErr });
  const reject = useMutation({ mutationFn: (id: string) => api(`/purchases/requisitions/${id}/reject`, { method: "POST", body: JSON.stringify({ note: "Rejeitada via UI" }) }), onSuccess: () => ok("Requisição rejeitada."), onError: onErr });

  const baixarOc = async (o: OrderView) => {
    try { await download(`/purchases/orders/${o.id}/pdf`, `OC-${o.number}.pdf`); }
    catch (e) { onErr(e); }
  };

  const cancelar = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api(`/purchases/orders/${id}/cancel`, { method: "POST", body: JSON.stringify({ reason }) }),
    onSuccess: () => ok("OC cancelada."), onError: onErr,
  });

  const onCancelar = (o: OrderView) => {
    const reason = typeof window !== "undefined" ? window.prompt(`Motivo do cancelamento da OC nº ${o.number}:`) : null;
    if (reason && reason.trim()) cancelar.mutate({ id: o.id, reason: reason.trim() });
  };

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Compras</h1>

      {has(Perm.PurchasesRequest) && <NovaRequisicao onDone={() => ok("Requisição criada.")} onErr={onErr} />}

      <Card title="Requisições">
        {reqs.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : reqs.data && reqs.data.length > 0 ? (
          <div className="space-y-3">
            {reqs.data.map((r) => (
              <div key={r.id} className="rounded-lg border border-slate-200 p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <div className="flex items-center gap-2">
                      <StatusPill status={r.status} />
                      <span className="text-sm text-slate-600">{r.requester}</span>
                    </div>
                    <div className="mt-1 text-xs text-slate-500">
                      {r.lines.map((l) => `${l.itemCode}×${Number(l.quantity)} ${l.unit}`).join(", ")}
                    </div>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    {r.status === "Draft" && has(Perm.PurchasesRequest) && <Button variant="ghost" onClick={() => submit.mutate(r.id)}>Enviar</Button>}
                    {r.status === "Submitted" && has(Perm.PurchasesApprove) && (
                      <>
                        <Button onClick={() => approve.mutate(r.id)}>Aprovar</Button>
                        <Button variant="danger" onClick={() => reject.mutate(r.id)}>Rejeitar</Button>
                      </>
                    )}
                  </div>
                </div>
                {r.status === "Approved" && has(Perm.PurchasesOrder) && (
                  <EmitirOc req={r} onSuccess={() => ok("OC emitida.")} onErr={onErr} />
                )}
              </div>
            ))}
          </div>
        ) : (
          <Empty>Nenhuma requisição. Gere pela Reposição ou crie acima.</Empty>
        )}
      </Card>

      <Card title="Ordens de Compra emitidas">
        {orders.data && orders.data.length > 0 ? (
          <Table head={["OC nº", "Empresa pagadora", "Fornecedor", "Valor líquido", "Situação", "OC"]}>
            {orders.data.map((o) => (
              <tr key={o.id}>
                <td className="px-3 py-2 font-mono text-xs">{o.number}</td>
                <td className="px-3 py-2">{o.payingCompanyName}</td>
                <td className="px-3 py-2 text-slate-600">{o.supplierCode} — {o.supplierName}</td>
                <td className="px-3 py-2 text-right tabular-nums">R$ {money(o.netValue)}</td>
                <td className="px-3 py-2"><StatusPill status={o.status} /></td>
                <td className="px-3 py-2">
                  <div className="flex items-center gap-2">
                    <Button variant="ghost" onClick={() => baixarOc(o)}>Baixar OC (PDF)</Button>
                    {o.status === "Issued" && has(Perm.PurchasesOrder) && (
                      <Button variant="danger" onClick={() => onCancelar(o)}>Cancelar</Button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Nenhuma OC emitida ainda.</Empty>
        )}
      </Card>
    </>
  );
}

/** Criação de requisição: itens manuais (um a um) + importação em lote via planilha Excel. */
function NovaRequisicao({ onDone, onErr }: { onDone: () => void; onErr: (e: unknown) => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const fileRef = useRef<HTMLInputElement>(null);
  const [linhas, setLinhas] = useState<{ itemCode: string; quantity: string; unit: string }[]>([]);
  const [item, setItem] = useState({ itemCode: "", quantity: "", unit: "un" });

  const addItem = () => {
    const qty = Number(item.quantity.replace(",", "."));
    if (!item.itemCode.trim()) { toast.push("error", "Informe o código do item."); return; }
    if (!(qty > 0)) { toast.push("error", "Quantidade deve ser positiva."); return; }
    setLinhas([...linhas, { itemCode: item.itemCode.trim(), quantity: String(qty), unit: item.unit.trim() || "un" }]);
    setItem({ itemCode: "", quantity: "", unit: "un" });
  };

  const criar = useMutation({
    mutationFn: () => api<{ requisitionId: string }>("/purchases/requisitions", {
      method: "POST",
      body: JSON.stringify({ lines: linhas.map((l) => ({ itemCode: l.itemCode, quantity: Number(l.quantity), unit: l.unit })) }),
    }),
    onSuccess: () => { setLinhas([]); qc.invalidateQueries({ queryKey: ["requisitions"] }); onDone(); },
    onError: onErr,
  });

  const baixarModelo = async () => {
    try { await download("/purchases/requisitions/import-template", "modelo-itens-requisicao.xlsx"); }
    catch (e) { onErr(e); }
  };

  const importar = useMutation({
    mutationFn: (file: File) => upload<{ requisitionId: string; imported: number; warnings?: string[] }>("/purchases/requisitions/import", file),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      toast.push("success", `Planilha importada: ${r.imported} item(ns).`);
      if (r.warnings && r.warnings.length > 0) toast.push("error", `${r.warnings.length} linha(s) ignorada(s).`);
      if (fileRef.current) fileRef.current.value = "";
    },
    onError: (e) => { onErr(e); if (fileRef.current) fileRef.current.value = ""; },
  });

  return (
    <Card
      title="Nova requisição"
      actions={
        <div className="flex items-center gap-2">
          <Button variant="ghost" onClick={baixarModelo}>Baixar modelo (Excel)</Button>
          <Button variant="ghost" onClick={() => fileRef.current?.click()} disabled={importar.isPending}>
            {importar.isPending ? "Importando…" : "Importar planilha"}
          </Button>
          <input ref={fileRef} type="file" accept=".xlsx" className="hidden"
            onChange={(e) => { const f = e.target.files?.[0]; if (f) importar.mutate(f); }} />
        </div>
      }
    >
      <form className="grid grid-cols-1 gap-2 sm:grid-cols-4" onSubmit={(e: FormEvent) => { e.preventDefault(); addItem(); }}>
        <Input label="Código do item" value={item.itemCode} onChange={(e) => setItem({ ...item, itemCode: e.target.value })} />
        <Input label="Quantidade" inputMode="decimal" value={item.quantity} onChange={(e) => setItem({ ...item, quantity: e.target.value })} />
        <Input label="Unidade" value={item.unit} onChange={(e) => setItem({ ...item, unit: e.target.value })} />
        <div className="flex items-end"><Button type="submit" variant="ghost">Adicionar item</Button></div>
      </form>

      {linhas.length > 0 && (
        <div className="mt-4">
          <Table head={["Código", "Qtd", "Unidade", ""]}>
            {linhas.map((l, i) => (
              <tr key={i}>
                <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
                <td className="px-3 py-2">{Number(l.quantity)}</td>
                <td className="px-3 py-2">{l.unit}</td>
                <td className="px-3 py-2 text-right">
                  <button className="text-xs text-rose-600 hover:underline" onClick={() => setLinhas(linhas.filter((_, j) => j !== i))}>remover</button>
                </td>
              </tr>
            ))}
          </Table>
          <div className="mt-3">
            <Button onClick={() => criar.mutate()} disabled={criar.isPending}>Criar requisição ({linhas.length} item{linhas.length > 1 ? "ns" : ""})</Button>
          </div>
        </div>
      )}
    </Card>
  );
}

/** Emissão da OC: escolhe empresa pagadora + fornecedor vencedor + preços por linha (concorrência/BID). */
function EmitirOc({ req, onSuccess, onErr }: { req: RequisitionView; onSuccess: () => void; onErr: (e: unknown) => void }) {
  const [open, setOpen] = useState(false);
  const [payingCode, setPayingCode] = useState("");
  const [supplierCode, setSupplierCode] = useState("");
  const [prices, setPrices] = useState<Record<string, string>>({});

  const paying = useQuery({ queryKey: ["paying-companies"], queryFn: () => api<PayingCompanyView[]>("/purchases/paying-companies"), enabled: open });
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: () => api<SupplierFullView[]>("/purchases/suppliers"), enabled: open });

  const total = req.lines.reduce((acc, l) => acc + Number(l.quantity) * (Number((prices[l.itemCode] ?? "").replace(",", ".")) || 0), 0);

  const emitir = useMutation({
    mutationFn: () => api<{ orderId: string }>(`/purchases/requisitions/${req.id}/order`, {
      method: "POST",
      body: JSON.stringify({
        payingCompanyCode: payingCode,
        supplierCode,
        lines: req.lines.map((l) => ({ itemCode: l.itemCode, unitPrice: Number((prices[l.itemCode] ?? "").replace(",", ".")) || 0 })),
      }),
    }),
    onSuccess: () => { setOpen(false); onSuccess(); },
    onError: onErr,
  });

  if (!open) {
    return <div className="mt-3 border-t border-slate-100 pt-3"><Button onClick={() => setOpen(true)}>Emitir OC</Button></div>;
  }

  return (
    <div className="mt-3 space-y-3 border-t border-slate-100 pt-3">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <Select label="Empresa pagadora (CNPJ)" value={payingCode} onChange={(e) => setPayingCode(e.target.value)}>
          <option value="">Selecione…</option>
          {(paying.data ?? []).map((p) => <option key={p.id} value={p.code}>{p.code} — {p.legalName}</option>)}
        </Select>
        <Select label="Fornecedor vencedor" value={supplierCode} onChange={(e) => setSupplierCode(e.target.value)}>
          <option value="">Selecione…</option>
          {(suppliers.data ?? []).map((s) => <option key={s.id} value={s.code}>{s.code} — {s.name}</option>)}
        </Select>
      </div>

      <Table head={["Item", "Qtd", "Valor unitário (R$)", "Valor serviço (R$)"]}>
        {req.lines.map((l) => {
          const price = Number((prices[l.itemCode] ?? "").replace(",", ".")) || 0;
          return (
            <tr key={l.itemCode}>
              <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
              <td className="px-3 py-2">{Number(l.quantity)} {l.unit}</td>
              <td className="px-3 py-2">
                <input inputMode="decimal" value={prices[l.itemCode] ?? ""} placeholder="0,00"
                  onChange={(e) => setPrices({ ...prices, [l.itemCode]: e.target.value })}
                  className="w-28 rounded-lg border border-slate-300 px-2 py-1 text-sm" />
              </td>
              <td className="px-3 py-2 text-right tabular-nums">{money(Number(l.quantity) * price)}</td>
            </tr>
          );
        })}
      </Table>

      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-slate-700">Total dos produtos: R$ {money(total)}</span>
        <div className="flex gap-2">
          <Button variant="ghost" onClick={() => setOpen(false)}>Cancelar</Button>
          <Button onClick={() => emitir.mutate()} disabled={emitir.isPending || !payingCode || !supplierCode}>
            {emitir.isPending ? "Emitindo…" : "Emitir OC"}
          </Button>
        </div>
      </div>
    </div>
  );
}
