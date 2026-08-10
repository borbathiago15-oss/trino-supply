"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { api, download, ItemView, OrderView, RequisitionView, Suggestion } from "@/lib/api";
import { Button, Card, Empty, StatusPill, Table } from "@/components/ui";

const money = (v: number) => Number(v).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

function Stat({ label, value, href, accent }: { label: string; value: string | number; href: string; accent?: boolean }) {
  return (
    <Link href={href} className="block">
      <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm transition hover:border-brand">
        <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</p>
        <p className={`mt-2 text-3xl font-semibold ${accent ? "text-brand" : "text-slate-800"}`}>{value}</p>
      </div>
    </Link>
  );
}

export default function DashboardPage() {
  const items = useQuery({ queryKey: ["items"], queryFn: () => api<ItemView[]>("/materials/items") });
  const suggestions = useQuery({ queryKey: ["suggestions"], queryFn: () => api<Suggestion[]>("/materials/replenishment/suggestions") });
  const reqs = useQuery({ queryKey: ["requisitions"], queryFn: () => api<RequisitionView[]>("/purchases/requisitions") });
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => api<OrderView[]>("/purchases/orders") });

  const pending = reqs.data?.filter((r) => r.status === "Submitted").length ?? 0;
  const issued = orders.data?.filter((o) => o.status === "Issued") ?? [];
  const issuedTotal = issued.reduce((acc, o) => acc + Number(o.netValue), 0);
  const recent = [...(orders.data ?? [])].slice(0, 6);

  const baixarOc = async (o: OrderView) => {
    try { await download(`/purchases/orders/${o.id}/pdf`, `OC-${o.number}.pdf`); } catch { /* ignore */ }
  };

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Painel</h1>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Itens cadastrados" value={items.data?.length ?? "—"} href="/materiais" />
        <Stat label="Sugestões de reposição" value={suggestions.data?.length ?? "—"} href="/reposicao" accent />
        <Stat label="Aguardando aprovação" value={pending} href="/compras" accent />
        <Stat label="OCs emitidas" value={issued.length} href="/compras" />
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm sm:col-span-2">
          <p className="text-xs font-medium uppercase tracking-wide text-slate-400">Valor emitido em OCs</p>
          <p className="mt-2 text-3xl font-semibold text-slate-800">R$ {money(issuedTotal)}</p>
        </div>
        <Stat label="Requisições" value={reqs.data?.length ?? "—"} href="/compras" />
        <Stat label="OCs canceladas" value={orders.data?.filter((o) => o.status === "Cancelled").length ?? 0} href="/compras" />
      </div>

      <Card title="Últimas Ordens de Compra">
        {recent.length > 0 ? (
          <Table head={["OC nº", "Empresa pagadora", "Fornecedor", "Valor líquido", "Situação", "OC"]}>
            {recent.map((o) => (
              <tr key={o.id}>
                <td className="px-3 py-2 font-mono text-xs">{o.number}</td>
                <td className="px-3 py-2">{o.payingCompanyName}</td>
                <td className="px-3 py-2 text-slate-600">{o.supplierCode} — {o.supplierName}</td>
                <td className="px-3 py-2 text-right tabular-nums">R$ {money(o.netValue)}</td>
                <td className="px-3 py-2"><StatusPill status={o.status} /></td>
                <td className="px-3 py-2"><Button variant="ghost" onClick={() => baixarOc(o)}>PDF</Button></td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Nenhuma OC emitida ainda. O ciclo termina na tela de Compras.</Empty>
        )}
      </Card>

      <Card title="Como funciona o ciclo">
        <ol className="grid gap-3 text-sm text-slate-600 sm:grid-cols-5">
          {["Cadastrar item", "Movimentar estoque", "Reposição sugere", "Requisição + aprovação", "Emitir OC + PDF"].map(
            (step, i) => (
              <li key={step} className="rounded-lg border border-slate-100 bg-slate-50 p-3">
                <span className="mb-1 block font-mono text-xs text-brand">0{i + 1}</span>
                {step}
              </li>
            ),
          )}
        </ol>
      </Card>
    </>
  );
}
