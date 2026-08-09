"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { api, ItemView, RequisitionView, Suggestion } from "@/lib/api";
import { Card } from "@/components/ui";

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

  const pending = reqs.data?.filter((r) => r.status === "Submitted").length ?? 0;

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Painel</h1>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Itens cadastrados" value={items.data?.length ?? "—"} href="/materiais" />
        <Stat label="Sugestões de reposição" value={suggestions.data?.length ?? "—"} href="/reposicao" accent />
        <Stat label="Requisições" value={reqs.data?.length ?? "—"} href="/compras" />
        <Stat label="Aguardando aprovação" value={pending} href="/compras" accent />
      </div>

      <Card title="Como funciona o ciclo">
        <ol className="grid gap-3 text-sm text-slate-600 sm:grid-cols-5">
          {["Cadastrar item", "Movimentar estoque", "Reposição sugere", "Requisição + aprovação", "Pedido ao fornecedor"].map(
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
