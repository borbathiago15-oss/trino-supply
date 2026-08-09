"use client";

import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, BalanceView, ItemView } from "@/lib/api";
import { Button, Card, Empty, Input, StatusPill, Table } from "@/components/ui";

function BalanceCell({ code }: { code: string }) {
  const { data } = useQuery({ queryKey: ["balance", code], queryFn: () => api<BalanceView>(`/materials/items/${code}/balance`) });
  return <span className="font-mono tabular-nums">{data ? Number(data.quantity) : "…"}</span>;
}

export default function MateriaisPage() {
  const qc = useQueryClient();
  const items = useQuery({ queryKey: ["items"], queryFn: () => api<ItemView[]>("/materials/items") });
  const [err, setErr] = useState<string | null>(null);

  const [unit, setUnit] = useState({ code: "", name: "", dimension: "contagem", factorToBase: "1" });
  const [item, setItem] = useState({ code: "", name: "", baseUnitCode: "" });
  const [mov, setMov] = useState({ itemCode: "", direction: "1", quantity: "" });

  const createUnit = useMutation({
    mutationFn: () => api("/materials/units", { method: "POST", body: JSON.stringify({ ...unit, factorToBase: Number(unit.factorToBase) }) }),
    onSuccess: () => setUnit({ code: "", name: "", dimension: "contagem", factorToBase: "1" }),
    onError: (e) => setErr(e instanceof ApiError ? e.message : "Erro"),
  });
  const createItem = useMutation({
    mutationFn: () => api("/materials/items", { method: "POST", body: JSON.stringify(item) }),
    onSuccess: () => { setItem({ code: "", name: "", baseUnitCode: "" }); qc.invalidateQueries({ queryKey: ["items"] }); },
    onError: (e) => setErr(e instanceof ApiError ? e.message : "Erro"),
  });
  const postMov = useMutation({
    mutationFn: () => api(`/materials/items/${mov.itemCode}/movements`, {
      method: "POST",
      body: JSON.stringify({ direction: Number(mov.direction), quantity: Number(mov.quantity) }),
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["balance"] }); qc.invalidateQueries({ queryKey: ["suggestions"] }); setMov({ ...mov, quantity: "" }); },
    onError: (e) => setErr(e instanceof ApiError ? e.message : "Erro"),
  });

  const submit = (e: FormEvent, fn: () => void) => { e.preventDefault(); setErr(null); fn(); };

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Materiais</h1>
      {err && <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{err}</p>}

      <Card title="Itens e saldo">
        {items.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : items.data && items.data.length > 0 ? (
          <Table head={["Código", "Nome", "Situação", "Saldo"]}>
            {items.data.map((it) => (
              <tr key={it.id}>
                <td className="px-3 py-2 font-mono text-xs">{it.code}</td>
                <td className="px-3 py-2">{it.name}</td>
                <td className="px-3 py-2"><StatusPill status={it.status} /></td>
                <td className="px-3 py-2"><BalanceCell code={it.code} /></td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Nenhum item ainda. Crie uma unidade e um item abaixo.</Empty>
        )}
      </Card>

      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Nova unidade">
          <form className="space-y-3" onSubmit={(e) => submit(e, () => createUnit.mutate())}>
            <Input label="Código" value={unit.code} onChange={(e) => setUnit({ ...unit, code: e.target.value })} />
            <Input label="Nome" value={unit.name} onChange={(e) => setUnit({ ...unit, name: e.target.value })} />
            <Input label="Dimensão" value={unit.dimension} onChange={(e) => setUnit({ ...unit, dimension: e.target.value })} />
            <Input label="Fator para a base" value={unit.factorToBase} onChange={(e) => setUnit({ ...unit, factorToBase: e.target.value })} />
            <Button type="submit" disabled={createUnit.isPending}>Criar unidade</Button>
          </form>
        </Card>

        <Card title="Novo item">
          <form className="space-y-3" onSubmit={(e) => submit(e, () => createItem.mutate())}>
            <Input label="Código (SKU)" value={item.code} onChange={(e) => setItem({ ...item, code: e.target.value })} />
            <Input label="Nome" value={item.name} onChange={(e) => setItem({ ...item, name: e.target.value })} />
            <Input label="Unidade-base (código)" value={item.baseUnitCode} onChange={(e) => setItem({ ...item, baseUnitCode: e.target.value })} />
            <Button type="submit" disabled={createItem.isPending}>Criar item</Button>
          </form>
        </Card>

        <Card title="Movimentar estoque">
          <form className="space-y-3" onSubmit={(e) => submit(e, () => postMov.mutate())}>
            <Input label="Item (código)" value={mov.itemCode} onChange={(e) => setMov({ ...mov, itemCode: e.target.value })} />
            <label className="block text-sm">
              <span className="mb-1 block font-medium text-slate-600">Sentido</span>
              <select className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-sm"
                value={mov.direction} onChange={(e) => setMov({ ...mov, direction: e.target.value })}>
                <option value="1">Entrada</option>
                <option value="2">Saída</option>
              </select>
            </label>
            <Input label="Quantidade" value={mov.quantity} onChange={(e) => setMov({ ...mov, quantity: e.target.value })} />
            <Button type="submit" disabled={postMov.isPending}>Lançar</Button>
          </form>
        </Card>
      </div>
    </>
  );
}
