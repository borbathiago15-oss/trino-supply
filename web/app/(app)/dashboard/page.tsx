"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { api, download, OrderView, RequisitionView } from "@/lib/api";
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
  const reqs = useQuery({ queryKey: ["requisitions"], queryFn: () => api<RequisitionView[]>("/purchases/requisitions") });
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => api<OrderView[]>("/purchases/orders") });

  const byStatus = (s: string) => reqs.data?.filter((r) => r.status === s).length ?? 0;
  const pending = byStatus("Submitted") + byStatus("ApprovedLevel1");
  const issued = orders.data?.filter((o) => o.status === "Issued") ?? [];
  const issuedTotal = issued.reduce((acc, o) => acc + Number(o.netValue), 0);
  const recent = [...(orders.data ?? [])].slice(0, 6);

  const baixarOc = async (o: OrderView) => {
    try { await download(`/purchases/orders/${o.id}/pdf`, `OC-${o.number}.pdf`); } catch { /* ignore */ }
  };

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Dashboard de Suprimentos</h1>
      <p className="-mt-3 text-sm text-slate-500">Visão de pedidos e compras. O estoque tem dashboard próprio.</p>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Pedidos pendentes" value={pending} href="/aprovacao" accent />
        <Stat label="Em rascunho" value={byStatus("Draft")} href="/compras" />
        <Stat label="Aprovados" value={byStatus("Approved")} href="/compras" accent />
        <Stat label="Reprovados" value={byStatus("Rejected")} href="/compras" />
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm sm:col-span-2">
          <p className="text-xs font-medium uppercase tracking-wide text-slate-400">Valor emitido em OCs</p>
          <p className="mt-2 text-3xl font-semibold text-slate-800">R$ {money(issuedTotal)}</p>
        </div>
        <Stat label="OCs emitidas" value={issued.length} href="/compras" />
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
          {["Abrir pedido", "Aprovação (2 níveis)", "Estoque interno → baixa", "Sem estoque → compra", "Emitir OC + PDF"].map(
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
