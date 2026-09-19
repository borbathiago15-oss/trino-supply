"use client";

import { Fragment, FormEvent, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  api, ApiError, download, upload, ItemView, OrderView, PayingCompanyView, RequisitionView, SupplierFullView,
} from "@/lib/api";
import { Button, Card, Empty, Input, Select, StatusPill, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Perm, useHas } from "@/lib/me";
import { downloadCsv } from "@/lib/csv";
import { ReqHeader, RequisitionHeaderFields, emptyHeader, headerError, useRequisitionRefData } from "@/components/requisitionHeader";
import { ConferenciaRecebimento } from "@/components/recebimento";
import { ConciliacaoFiscal } from "@/components/conciliacao";
import { SaldoBadge, useSaldoAlmox } from "@/components/saldoAlmox";
import { OtifBadge, useScorecards } from "@/components/otif";
import { AbrirCotacao } from "@/components/cotacao";

const money = (v: number) => Number(v).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export default function ComprasPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const has = useHas();
  const [openOc, setOpenOc] = useState<string | null>(null);
  const [ocStatus, setOcStatus] = useState<string>("");

  const reqs = useQuery({ queryKey: ["requisitions"], queryFn: () => api<RequisitionView[]>("/purchases/requisitions") });
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => api<OrderView[]>("/purchases/orders") });

  const invalidate = () => { qc.invalidateQueries({ queryKey: ["requisitions"] }); qc.invalidateQueries({ queryKey: ["orders"] }); };
  const onErr = (e: unknown) => toast.push("error", e instanceof ApiError ? e.message : "Erro");
  const ok = (msg: string) => { invalidate(); toast.push("success", msg); };

  const submit = useMutation({ mutationFn: (id: string) => api(`/purchases/requisitions/${id}/submit`, { method: "POST" }), onSuccess: () => ok("Requisição enviada."), onError: onErr });
  const approve = useMutation({
    mutationFn: (id: string) => api<{ route?: string } | undefined>(`/purchases/requisitions/${id}/approve`, { method: "POST" }),
    onSuccess: (body) => ok(body?.route === "stock"
      ? "Pedido aprovado e atendido pelo estoque interno (baixa efetuada)."
      : "Pedido aprovado."),
    onError: onErr,
  });
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
      <h1 className="text-xl font-semibold text-slate-800">Pedido</h1>

      {has(Perm.PurchasesRequest) && <NovaRequisicao onDone={() => ok("Requisição criada.")} onErr={onErr} />}

      <Card title="Pedidos">
        {reqs.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : reqs.data && reqs.data.length > 0 ? (
          <div className="space-y-3">
            {reqs.data.map((r) => {
              const pendingLevel = r.status === "Submitted" ? 1 : r.status === "ApprovedLevel1" ? 2 : 0;
              return (
              <div key={r.id} className="rounded-lg border border-slate-200 p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <StatusPill status={r.status} />
                      {r.priority === "Emergencial" && <span className="rounded-full bg-rose-100 px-2 py-0.5 text-xs font-medium text-rose-700">Emergencial</span>}
                      <span className="text-sm text-slate-600">{r.requester}</span>
                    </div>
                    <div className="mt-1 text-xs text-slate-500">
                      {[r.payingCompanyName && `Custo: ${r.payingCompanyName}`, r.costCenterName && `Centro: ${r.costCenterName}`]
                        .filter(Boolean).join(" · ")}
                    </div>
                    <div className="mt-0.5 text-xs text-slate-500">
                      {r.lines.map((l) => `${l.itemCode}×${Number(l.quantity)} ${l.unit}${l.purchaseOrderId ? " ✓" : ""}`).join(", ")}
                    </div>
                    <div className="mt-0.5 text-xs text-slate-400">
                      Aprovação: N1 {r.approverLevel1}{r.level1DecidedBy ? " ✓" : ""} → N2 {r.approverLevel2}{r.level2DecidedBy ? " ✓" : ""}
                      {r.justification ? ` · ${r.justification}` : ""}
                    </div>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    {r.status === "Draft" && has(Perm.PurchasesRequest) && <Button variant="ghost" onClick={() => submit.mutate(r.id)}>Enviar</Button>}
                    {pendingLevel > 0 && has(Perm.PurchasesApprove) && (
                      <>
                        <Button onClick={() => approve.mutate(r.id)}>Aprovar (nível {pendingLevel})</Button>
                        <Button variant="danger" onClick={() => reject.mutate(r.id)}>Rejeitar</Button>
                      </>
                    )}
                  </div>
                </div>
                {(r.status === "Approved" || r.status === "PartiallyOrdered") && has(Perm.PurchasesOrder) && (
                  <>
                    <EmitirOc req={r} onSuccess={() => ok("OC emitida.")} onErr={onErr} />
                    {/* Dois caminhos legítimos: comprar direto (preço já conhecido) ou disputar. */}
                    <div className="mt-2 border-t border-slate-100 pt-2">
                      <AbrirCotacao req={r} onErr={onErr}
                        onSuccess={() => ok("Concorrência aberta — lance as propostas em Cotações.")} />
                    </div>
                  </>
                )}
              </div>
            );})}
          </div>
        ) : (
          <Empty>Nenhuma requisição. Gere pela Reposição ou crie acima.</Empty>
        )}
      </Card>

      <Card
        title="Ordens de Compra emitidas"
        actions={
          <div className="flex items-center gap-2">
            <select value={ocStatus} onChange={(e) => setOcStatus(e.target.value)}
              className="rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm">
              <option value="">Todas</option>
              <option value="Issued">Emitidas</option>
              <option value="PartiallyReceived">Recebidas parcial</option>
              <option value="Received">Recebidas</option>
              <option value="Cancelled">Canceladas</option>
            </select>
            <Button variant="ghost" onClick={() => {
              const rows = (orders.data ?? []).filter((o) => !ocStatus || o.status === ocStatus)
                .map((o) => [o.number, o.payingCompanyName, o.supplierCode, o.supplierName, o.netValue.toFixed(2), o.status, o.issuedAt?.slice(0, 10)]);
              downloadCsv("ordens-de-compra.csv", ["OC", "Pagadora", "Cod.Fornecedor", "Fornecedor", "Valor líquido", "Situação", "Emissão"], rows);
            }}>Exportar CSV</Button>
          </div>
        }
      >
        {(() => { const list = (orders.data ?? []).filter((o) => !ocStatus || o.status === ocStatus); return list.length > 0 ? (
          <Table head={["OC nº", "Empresa pagadora", "Fornecedor", "Valor líquido", "Situação", "OC"]}>
            {list.map((o) => (
              <Fragment key={o.id}>
                <tr>
                  <td className="px-3 py-2 font-mono text-xs">{o.number}</td>
                  <td className="px-3 py-2">{o.payingCompanyName}</td>
                  <td className="px-3 py-2 text-slate-600">{o.supplierCode} — {o.supplierName}</td>
                  <td className="px-3 py-2 text-right tabular-nums">R$ {money(o.netValue)}</td>
                  <td className="px-3 py-2"><StatusPill status={o.status} /></td>
                  <td className="px-3 py-2">
                    <div className="flex items-center gap-2">
                      <Button variant="ghost" onClick={() => setOpenOc(openOc === o.id ? null : o.id)}>
                        {openOc === o.id ? "Ocultar" : "Detalhes"}
                      </Button>
                      <Button variant="ghost" onClick={() => baixarOc(o)}>Baixar OC (PDF)</Button>
                      {o.status === "Issued" && has(Perm.PurchasesOrder) && (
                        <Button variant="danger" onClick={() => onCancelar(o)}>Cancelar</Button>
                      )}
                    </div>
                  </td>
                </tr>
                {openOc === o.id && (
                  <tr>
                    <td colSpan={6} className="bg-slate-50 px-3 py-4"><OcDetalhe o={o} /></td>
                  </tr>
                )}
              </Fragment>
            ))}
          </Table>
        ) : (
          <Empty>{ocStatus ? "Nenhuma OC nesse filtro." : "Nenhuma OC emitida ainda."}</Empty>
        ); })()}
      </Card>
    </>
  );
}

/** Detalhe da OC na própria tela: cabeçalho, itens com impostos, totais e — se cancelada — o motivo. */
function OcDetalhe({ o }: { o: OrderView }) {
  const toast = useToast();
  const onErr = (e: unknown) => toast.push("error", e instanceof ApiError ? e.message : "Erro");
  const onOk = (m: string) => toast.push("success", m);
  const dt = (s?: string | null) => (s ? new Date(s).toLocaleDateString("pt-BR") : "—");
  return (
    <div className="space-y-3 text-sm">
      <div className="grid grid-cols-1 gap-1 sm:grid-cols-2">
        <div><span className="text-slate-400">Empresa pagadora:</span> {o.payingCompanyName}</div>
        <div><span className="text-slate-400">Fornecedor:</span> {o.supplierCode} — {o.supplierName}</div>
        <div><span className="text-slate-400">Emissão:</span> {dt(o.issuedAt)} · por {o.issuedBy}</div>
        <div><span className="text-slate-400">Cond./Forma pgto:</span> {o.paymentTerms || "—"} · {o.paymentMethod || "—"}</div>
      </div>

      {o.status === "Cancelled" && (
        <div className="rounded-lg border border-rose-200 bg-rose-50 p-2 text-rose-700">
          <strong>Cancelada</strong> em {dt(o.cancelledAt)} por {o.cancelledBy || "—"} — motivo: {o.cancelReason || "—"}
        </div>
      )}

      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead>
            <tr className="text-slate-400">
              <th className="px-2 py-1">Item</th><th className="px-2 py-1">Descrição</th>
              <th className="px-2 py-1 text-right">Qtd</th><th className="px-2 py-1">Un.</th>
              <th className="px-2 py-1 text-right">Vlr.Unit.</th><th className="px-2 py-1 text-right">Vlr.Serviço</th>
              <th className="px-2 py-1 text-right">%IRRF</th><th className="px-2 py-1 text-right">%ISS</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {o.lines.map((l, i) => (
              <tr key={i}>
                <td className="px-2 py-1 font-mono">{l.itemCode}</td>
                <td className="px-2 py-1">{l.description || "—"}</td>
                <td className="px-2 py-1 text-right tabular-nums">{Number(l.quantity)}</td>
                <td className="px-2 py-1">{l.unit}</td>
                <td className="px-2 py-1 text-right tabular-nums">{money(l.unitPrice)}</td>
                <td className="px-2 py-1 text-right tabular-nums">{money(l.serviceValue)}</td>
                <td className="px-2 py-1 text-right tabular-nums">{Number(l.irrfPercent)}</td>
                <td className="px-2 py-1 text-right tabular-nums">{Number(l.issPercent)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="ml-auto grid max-w-xs gap-0.5">
        {([["Produtos", o.productsValue], ["IPI", o.ipiValue], ["ICMS", o.icmsValue], ["Descontos", o.discountValue], ["Outras despesas", o.otherExpenses]] as [string, number][]).map(([k, v]) => (
          <div key={k} className="flex justify-between"><span className="text-slate-400">{k}</span><span className="tabular-nums">R$ {money(v)}</span></div>
        ))}
        <div className="mt-1 flex justify-between border-t border-slate-200 pt-1 font-semibold">
          <span>Valor líquido</span><span className="tabular-nums">R$ {money(o.netValue)}</span>
        </div>
        <div className="text-xs text-slate-400">Frete: {o.freightTerms || "—"}</div>
      </div>

      {/* Conferência física da entrega — some quando a OC foi cancelada. */}
      {o.status !== "Cancelled" && <ConferenciaRecebimento orderId={o.id} />}

      {/* Conciliação fiscal (Fase 05): OC × NF-e × doca, a partir do XML da nota. */}
      {o.status !== "Cancelled" && (
        <div className="border-t border-slate-100 pt-3">
          <h4 className="mb-2 text-sm font-semibold text-slate-700">Conciliação fiscal (3 pontas)</h4>
          <ConciliacaoFiscal orderId={o.id} onErr={onErr} onOk={onOk} />
        </div>
      )}
    </div>
  );
}

type Linha = { itemCode: string; quantity: string; unit: string };

/**
 * Nova solicitação de compra (spec Sistema de Compras): cabeçalho comum (empresa do custo, centro de
 * custo, tipo de demanda, motivo, 2 aprovadores) + um de dois modos — <b>pedido simples</b> (item a
 * item ou por planilha) ou <b>solicitação em lote por família</b> (lista os produtos da família e o
 * usuário informa as quantidades).
 */
function NovaRequisicao({ onDone, onErr }: { onDone: () => void; onErr: (e: unknown) => void }) {
  const qc = useQueryClient();
  const toast = useToast();
  const fileRef = useRef<HTMLInputElement>(null);
  const [modo, setModo] = useState<"simples" | "lote">("simples");
  const [header, setHeader] = useState<ReqHeader>(emptyHeader);
  const [linhas, setLinhas] = useState<Linha[]>([]);
  const [item, setItem] = useState({ itemCode: "", quantity: "", unit: "un" });

  const { paying, centers, approvers } = useRequisitionRefData();

  const done = () => { setLinhas([]); setHeader(emptyHeader); qc.invalidateQueries({ queryKey: ["requisitions"] }); onDone(); };

  const addItem = () => {
    const qty = Number(item.quantity.replace(",", "."));
    if (!item.itemCode.trim()) { toast.push("error", "Informe o código do item."); return; }
    if (!(qty > 0)) { toast.push("error", "Quantidade deve ser positiva."); return; }
    setLinhas([...linhas, { itemCode: item.itemCode.trim(), quantity: String(qty), unit: item.unit.trim() || "un" }]);
    setItem({ itemCode: "", quantity: "", unit: "un" });
  };

  const payload = (lines: Linha[]) => ({
    ...header,
    lines: lines.map((l) => ({ itemCode: l.itemCode, quantity: Number(l.quantity), unit: l.unit })),
  });

  const criar = useMutation({
    mutationFn: (lines: Linha[]) => api<{ requisitionId: string }>("/purchases/requisitions", {
      method: "POST", body: JSON.stringify(payload(lines)),
    }),
    onSuccess: done, onError: onErr,
  });

  const criarManual = () => {
    const err = headerError(header);
    if (err) { toast.push("error", err); return; }
    if (linhas.length === 0) { toast.push("error", "Adicione ao menos um item."); return; }
    criar.mutate(linhas);
  };

  const baixarModelo = async () => {
    try { await download("/purchases/requisitions/import-template", "modelo-itens-requisicao.xlsx"); }
    catch (e) { onErr(e); }
  };

  const importar = useMutation({
    mutationFn: (file: File) => upload<{ requisitionId: string; imported: number; warnings?: string[] }>(
      "/purchases/requisitions/import", file, {
        payingCompanyCode: header.payingCompanyCode, costCenterCode: header.costCenterCode,
        priority: header.priority, justification: header.justification,
        approverLevel1Subject: header.approverLevel1Subject, approverLevel2Subject: header.approverLevel2Subject,
      }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      toast.push("success", `Planilha importada: ${r.imported} item(ns).`);
      if (r.warnings && r.warnings.length > 0) toast.push("error", `${r.warnings.length} linha(s) ignorada(s).`);
      if (fileRef.current) fileRef.current.value = "";
      setHeader(emptyHeader);
    },
    onError: (e) => { onErr(e); if (fileRef.current) fileRef.current.value = ""; },
  });

  // MMS-004: saldo do Almox dos itens em tela — evita comprar o que já existe no armazém.
  const saldoDe = useSaldoAlmox([...linhas.map((l) => l.itemCode), item.itemCode]);

  const onImport = (f: File) => {
    const err = headerError(header);
    if (err) { toast.push("error", err); if (fileRef.current) fileRef.current.value = ""; return; }
    importar.mutate(f);
  };

  return (
    <Card title="Novo pedido">
      <div className="mb-4 inline-flex rounded-lg border border-slate-200 p-0.5 text-sm">
        <button className={`rounded-md px-3 py-1 ${modo === "simples" ? "bg-brand text-white" : "text-slate-600"}`} onClick={() => setModo("simples")}>Pedido simples</button>
        <button className={`rounded-md px-3 py-1 ${modo === "lote" ? "bg-brand text-white" : "text-slate-600"}`} onClick={() => setModo("lote")}>Solicitação em lote (por família)</button>
      </div>

      <RequisitionHeaderFields value={header} onChange={setHeader} paying={paying.data} centers={centers.data} approvers={approvers.data} />

      {modo === "simples" ? (
        <div className="mt-5 border-t border-slate-100 pt-4">
          <div className="mb-2 flex items-center justify-between">
            <h3 className="text-sm font-medium text-slate-700">Itens do pedido</h3>
            <div className="flex items-center gap-2">
              <Button variant="ghost" onClick={baixarModelo}>Baixar modelo (Excel)</Button>
              <Button variant="ghost" onClick={() => fileRef.current?.click()} disabled={importar.isPending}>
                {importar.isPending ? "Importando…" : "Importar planilha"}
              </Button>
              <input ref={fileRef} type="file" accept=".xlsx" className="hidden"
                onChange={(e) => { const f = e.target.files?.[0]; if (f) onImport(f); }} />
            </div>
          </div>

          <form className="grid grid-cols-1 gap-2 sm:grid-cols-4" onSubmit={(e: FormEvent) => { e.preventDefault(); addItem(); }}>
            <div>
              <Input label="Código do item" value={item.itemCode} onChange={(e) => setItem({ ...item, itemCode: e.target.value })} />
              <div className="mt-1 min-h-[1.25rem]">
                <SaldoBadge saldo={saldoDe(item.itemCode)}
                  quantidade={Number((item.quantity ?? "").replace(",", ".")) || undefined} />
              </div>
            </div>
            <Input label="Quantidade" inputMode="decimal" value={item.quantity} onChange={(e) => setItem({ ...item, quantity: e.target.value })} />
            <Input label="Unidade" value={item.unit} onChange={(e) => setItem({ ...item, unit: e.target.value })} />
            <div className="flex items-end"><Button type="submit" variant="ghost">Adicionar item</Button></div>
          </form>

          {linhas.length > 0 && (
            <div className="mt-4">
              <Table head={["Código", "Qtd", "Unidade", "No Almox", ""]}>
                {linhas.map((l, i) => (
                  <tr key={i}>
                    <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
                    <td className="px-3 py-2">{Number(l.quantity)}</td>
                    <td className="px-3 py-2">{l.unit}</td>
                    <td className="px-3 py-2">
                      <SaldoBadge saldo={saldoDe(l.itemCode)} quantidade={Number(l.quantity)} />
                    </td>
                    <td className="px-3 py-2 text-right">
                      <button className="text-xs text-rose-600 hover:underline" onClick={() => setLinhas(linhas.filter((_, j) => j !== i))}>remover</button>
                    </td>
                  </tr>
                ))}
              </Table>
              <div className="mt-3">
                <Button onClick={criarManual} disabled={criar.isPending}>Criar pedido ({linhas.length} item{linhas.length > 1 ? "ns" : ""})</Button>
              </div>
            </div>
          )}
        </div>
      ) : (
        <SolicitacaoLote header={header} onSubmit={(lines) => {
          const err = headerError(header);
          if (err) { toast.push("error", err); return; }
          if (lines.length === 0) { toast.push("error", "Informe a quantidade de ao menos um produto."); return; }
          criar.mutate(lines);
        }} pending={criar.isPending} onErr={onErr} />
      )}
    </Card>
  );
}

/**
 * Solicitação em lote por família: escolhe a família de produtos, lista todos os itens cadastrados
 * dela e o usuário informa a quantidade de cada um. Envia só as linhas com quantidade &gt; 0.
 */
function SolicitacaoLote({ header, onSubmit, pending, onErr }: {
  header: ReqHeader; onSubmit: (lines: Linha[]) => void; pending: boolean; onErr: (e: unknown) => void;
}) {
  const [group, setGroup] = useState("");
  const [qty, setQty] = useState<Record<string, string>>({});

  const groups = useQuery({ queryKey: ["product-groups"], queryFn: () => api<string[]>("/materials/product-groups") });
  const items = useQuery({
    queryKey: ["items", "group", group],
    queryFn: () => api<ItemView[]>(`/materials/items?group=${encodeURIComponent(group)}`),
    enabled: group !== "",
  });

  const lines = (items.data ?? [])
    .map((it) => ({ itemCode: it.code, quantity: (qty[it.code] ?? "").replace(",", "."), unit: "un" }))
    .filter((l) => Number(l.quantity) > 0)
    .map((l) => ({ ...l, quantity: String(Number(l.quantity)) }));

  return (
    <div className="mt-5 border-t border-slate-100 pt-4">
      <div className="max-w-sm">
        <Select label="Família / tipo de produto" value={group} onChange={(e) => { setGroup(e.target.value); setQty({}); }}>
          <option value="">Selecione a família…</option>
          {(groups.data ?? []).map((g) => <option key={g} value={g}>{g}</option>)}
        </Select>
      </div>

      {group !== "" && (
        <div className="mt-4">
          {items.isLoading ? (
            <Empty>Carregando produtos…</Empty>
          ) : items.data && items.data.length > 0 ? (
            <>
              <Table head={["Código", "Produto", "Quantidade"]}>
                {items.data.map((it) => (
                  <tr key={it.id}>
                    <td className="px-3 py-2 font-mono text-xs">{it.code}</td>
                    <td className="px-3 py-2">{it.name}</td>
                    <td className="px-3 py-2">
                      <input inputMode="decimal" value={qty[it.code] ?? ""} placeholder="0"
                        onChange={(e) => setQty({ ...qty, [it.code]: e.target.value })}
                        className="w-24 rounded-lg border border-slate-300 px-2 py-1 text-sm" />
                    </td>
                  </tr>
                ))}
              </Table>
              <div className="mt-3">
                <Button onClick={() => onSubmit(lines)} disabled={pending || lines.length === 0}>
                  Enviar solicitação ({lines.length} produto{lines.length === 1 ? "" : "s"})
                </Button>
              </div>
            </>
          ) : (
            <Empty>Nenhum produto cadastrado nessa família. Cadastre itens em Materiais com esse grupo.</Empty>
          )}
        </div>
      )}
    </div>
  );
}

/** Emissão da OC: escolhe empresa pagadora + fornecedor vencedor + preços por linha (concorrência/BID). */
function EmitirOc({ req, onSuccess, onErr }: { req: RequisitionView; onSuccess: () => void; onErr: (e: unknown) => void }) {
  const [open, setOpen] = useState(false);
  const [payingCode, setPayingCode] = useState("");
  const [supplierCode, setSupplierCode] = useState("");
  const [prices, setPrices] = useState<Record<string, string>>({});
  // Compra dividida: o comprador escolhe QUAIS itens entram nesta OC (os demais ficam para outro
  // fornecedor). Por padrão vêm todos os que ainda não foram pedidos.
  const pendentes = req.lines.filter((l) => !l.purchaseOrderId);
  const jaPedidos = req.lines.filter((l) => l.purchaseOrderId);
  const [selecionados, setSelecionados] = useState<Record<string, boolean>>({});
  const incluido = (code: string) => selecionados[code] ?? true;
  const escolhidos = pendentes.filter((l) => incluido(l.itemCode));

  const paying = useQuery({ queryKey: ["paying-companies"], queryFn: () => api<PayingCompanyView[]>("/purchases/paying-companies"), enabled: open });
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: () => api<SupplierFullView[]>("/purchases/suppliers"), enabled: open });
  // Fase 03: a escolha do fornecedor não é só o menor preço — o histórico de entrega (OTIF) entra
  // no rótulo da lista e o detalhe aparece embaixo do selecionado.
  const otif = useScorecards();

  const preco = (code: string) => Number((prices[code] ?? "").replace(",", ".")) || 0;
  const total = escolhidos.reduce((acc, l) => acc + Number(l.quantity) * preco(l.itemCode), 0);

  const emitir = useMutation({
    mutationFn: () => api<{ orderId: string }>(`/purchases/requisitions/${req.id}/order`, {
      method: "POST",
      body: JSON.stringify({
        payingCompanyCode: payingCode,
        supplierCode,
        lines: escolhidos.map((l) => ({ itemCode: l.itemCode, unitPrice: preco(l.itemCode) })),
      }),
    }),
    onSuccess: () => { setOpen(false); setPrices({}); setSelecionados({}); onSuccess(); },
    onError: onErr,
  });

  if (!open) {
    return (
      <div className="mt-3 border-t border-slate-100 pt-3">
        <Button onClick={() => setOpen(true)}>
          {jaPedidos.length > 0 ? `Emitir OC dos itens restantes (${pendentes.length})` : "Emitir OC"}
        </Button>
        {jaPedidos.length > 0 && (
          <p className="mt-1 text-xs text-slate-500">
            {jaPedidos.length} item(ns) já em OC. Os demais podem ir para outro fornecedor.
          </p>
        )}
      </div>
    );
  }

  return (
    <div className="mt-3 space-y-3 border-t border-slate-100 pt-3">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <Select label="Empresa pagadora (CNPJ)" value={payingCode} onChange={(e) => setPayingCode(e.target.value)}>
          <option value="">Selecione…</option>
          {(paying.data ?? []).map((p) => <option key={p.id} value={p.code}>{p.code} — {p.legalName}</option>)}
        </Select>
        <div>
          <Select label="Fornecedor vencedor" value={supplierCode} onChange={(e) => setSupplierCode(e.target.value)}>
            <option value="">Selecione…</option>
            {(suppliers.data ?? []).map((s) => {
              const nota = otif.porCodigo(s.code);
              const sufixo = nota && nota.linesEvaluated > 0 ? ` · OTIF ${nota.otifIndex}%` : "";
              return <option key={s.id} value={s.code}>{s.code} — {s.name}{sufixo}</option>;
            })}
          </Select>
          {supplierCode && (
            <div className="mt-1 flex items-center gap-2">
              <span className="text-xs text-slate-400">Entrega:</span>
              <OtifBadge score={otif.porCodigo(supplierCode)} detalhado />
            </div>
          )}
        </div>
      </div>

      <Table head={["Nesta OC", "Item", "Qtd", "Valor unitário (R$)", "Valor serviço (R$)"]}>
        {pendentes.map((l) => (
          <tr key={l.itemCode} className={incluido(l.itemCode) ? "" : "opacity-40"}>
            <td className="px-3 py-2">
              <input type="checkbox" checked={incluido(l.itemCode)} aria-label={`Incluir ${l.itemCode}`}
                onChange={(e) => setSelecionados({ ...selecionados, [l.itemCode]: e.target.checked })}
                className="h-4 w-4 rounded border-slate-300" />
            </td>
            <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
            <td className="px-3 py-2">{Number(l.quantity)} {l.unit}</td>
            <td className="px-3 py-2">
              <input inputMode="decimal" value={prices[l.itemCode] ?? ""} placeholder="0,00"
                disabled={!incluido(l.itemCode)}
                onChange={(e) => setPrices({ ...prices, [l.itemCode]: e.target.value })}
                className="w-28 rounded-lg border border-slate-300 px-2 py-1 text-sm disabled:bg-slate-50" />
            </td>
            <td className="px-3 py-2 text-right tabular-nums">{money(Number(l.quantity) * preco(l.itemCode))}</td>
          </tr>
        ))}
        {jaPedidos.map((l) => (
          <tr key={l.itemCode} className="text-slate-400">
            <td className="px-3 py-2 text-xs">✓</td>
            <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
            <td className="px-3 py-2">{Number(l.quantity)} {l.unit}</td>
            <td className="px-3 py-2 text-xs" colSpan={2}>já pedido em outra OC</td>
          </tr>
        ))}
      </Table>

      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-medium text-slate-700">
          Total desta OC ({escolhidos.length} item{escolhidos.length === 1 ? "" : "ns"}): R$ {money(total)}
        </span>
        <div className="flex gap-2">
          <Button variant="ghost" onClick={() => setOpen(false)}>Cancelar</Button>
          <Button onClick={() => emitir.mutate()}
            disabled={emitir.isPending || !payingCode || !supplierCode || escolhidos.length === 0}>
            {emitir.isPending ? "Emitindo…" : "Emitir OC"}
          </Button>
        </div>
      </div>
    </div>
  );
}
