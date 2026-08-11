"use client";

import { FormEvent, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, BalanceView, download, ItemView, upload } from "@/lib/api";
import { Button, Card, Empty, Input, Select, StatusPill, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Perm, useHas } from "@/lib/me";

function BalanceCell({ code }: { code: string }) {
  const { data } = useQuery({ queryKey: ["balance", code], queryFn: () => api<BalanceView>(`/materials/items/${code}/balance`) });
  return <span className="font-mono tabular-nums">{data ? Number(data.quantity) : "…"}</span>;
}

export default function MateriaisPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const has = useHas();
  const fileRef = useRef<HTMLInputElement>(null);
  const [filtro, setFiltro] = useState("");
  const items = useQuery({
    queryKey: ["items", "group", filtro],
    queryFn: () => api<ItemView[]>(`/materials/items${filtro ? `?group=${encodeURIComponent(filtro)}` : ""}`),
  });
  const groups = useQuery({ queryKey: ["product-groups"], queryFn: () => api<string[]>("/materials/product-groups") });

  const [unit, setUnit] = useState({ code: "", name: "", dimension: "contagem", factorToBase: "1" });
  const [item, setItem] = useState({ code: "", name: "", baseUnitCode: "", group: "", ca: "" });
  const [mov, setMov] = useState({ itemCode: "", direction: "1", quantity: "" });
  const [pol, setPol] = useState({ itemCode: "", minLevel: "", maxLevel: "" });

  const fail = (e: unknown) => toast.push("error", e instanceof ApiError ? e.message : "Erro inesperado");

  const createUnit = useMutation({
    mutationFn: () => api("/materials/units", { method: "POST", body: JSON.stringify({ ...unit, factorToBase: Number(unit.factorToBase) }) }),
    onSuccess: () => { setUnit({ code: "", name: "", dimension: "contagem", factorToBase: "1" }); toast.push("success", "Unidade criada."); },
    onError: fail,
  });
  const createItem = useMutation({
    mutationFn: () => api("/materials/items", { method: "POST", body: JSON.stringify(item) }),
    onSuccess: () => { setItem({ code: "", name: "", baseUnitCode: "", group: "", ca: "" }); qc.invalidateQueries({ queryKey: ["items"] }); toast.push("success", "Item criado."); },
    onError: fail,
  });

  const baixarModelo = async () => {
    try { await download("/materials/items/import-template", "modelo-itens-produtos.xlsx"); }
    catch (e) { fail(e); }
  };
  const importar = useMutation({
    mutationFn: (file: File) => upload<{ imported: number; warnings?: string[] }>("/materials/items/import", file),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["items"] });
      toast.push("success", `Planilha importada: ${r.imported} item(ns).`);
      if (r.warnings && r.warnings.length > 0) toast.push("error", `${r.warnings.length} linha(s) com aviso.`);
      if (fileRef.current) fileRef.current.value = "";
    },
    onError: (e) => { fail(e); if (fileRef.current) fileRef.current.value = ""; },
  });
  const postMov = useMutation({
    mutationFn: () => api(`/materials/items/${mov.itemCode}/movements`, {
      method: "POST",
      body: JSON.stringify({ direction: Number(mov.direction), quantity: Number(mov.quantity) }),
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["balance"] }); qc.invalidateQueries({ queryKey: ["suggestions"] }); setMov({ ...mov, quantity: "" }); toast.push("success", "Movimento lançado."); },
    onError: fail,
  });
  const setPolicy = useMutation({
    mutationFn: () => api(`/materials/items/${pol.itemCode}/replenishment`, {
      method: "PUT",
      body: JSON.stringify({ minLevel: Number(pol.minLevel), maxLevel: Number(pol.maxLevel) }),
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["suggestions"] }); setPol({ itemCode: "", minLevel: "", maxLevel: "" }); toast.push("success", "Política de reposição definida."); },
    onError: fail,
  });

  const submit = (e: FormEvent, fn: () => void) => { e.preventDefault(); fn(); };

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Materiais</h1>

      <Card
        title="Itens e saldo"
        actions={
          <div className="flex items-center gap-2">
            <select value={filtro} onChange={(e) => setFiltro(e.target.value)}
              className="rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm">
              <option value="">Todas as famílias</option>
              {(groups.data ?? []).map((g) => <option key={g} value={g}>{g}</option>)}
            </select>
            {has(Perm.MaterialsManage) && (
              <>
                <Button variant="ghost" onClick={baixarModelo}>Baixar modelo (Excel)</Button>
                <Button variant="ghost" onClick={() => fileRef.current?.click()} disabled={importar.isPending}>
                  {importar.isPending ? "Importando…" : "Importar planilha"}
                </Button>
                <input ref={fileRef} type="file" accept=".xlsx" className="hidden"
                  onChange={(e) => { const f = e.target.files?.[0]; if (f) importar.mutate(f); }} />
              </>
            )}
          </div>
        }
      >
        {items.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : items.data && items.data.length > 0 ? (
          <Table head={["Código", "Nome", "Família", "Nº C.A", "Situação", "Saldo"]}>
            {items.data.map((it) => (
              <tr key={it.id}>
                <td className="px-3 py-2 font-mono text-xs">{it.code}</td>
                <td className="px-3 py-2">{it.name}</td>
                <td className="px-3 py-2 text-slate-500">{it.group}</td>
                <td className="px-3 py-2 text-slate-500">{it.ca || "—"}</td>
                <td className="px-3 py-2"><StatusPill status={it.status} /></td>
                <td className="px-3 py-2"><BalanceCell code={it.code} /></td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>{filtro ? "Nenhum item nessa família." : "Nenhum item ainda. Crie uma unidade e um item abaixo."}</Empty>
        )}
      </Card>

      {has(Perm.MaterialsManage) && (
      <div className="grid gap-6 lg:grid-cols-2">
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
            <Select label="Família / grupo" value={item.group} onChange={(e) => setItem({ ...item, group: e.target.value })}>
              <option value="">Sem grupo</option>
              {(groups.data ?? []).map((g) => <option key={g} value={g}>{g}</option>)}
            </Select>
            <Input label="Nº C.A (EPI — opcional)" value={item.ca} onChange={(e) => setItem({ ...item, ca: e.target.value })} />
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

        <Card title="Política de reposição (mín/máx)">
          <form className="space-y-3" onSubmit={(e) => submit(e, () => setPolicy.mutate())}>
            <Input label="Item (código)" value={pol.itemCode} onChange={(e) => setPol({ ...pol, itemCode: e.target.value })} />
            <div className="grid grid-cols-2 gap-3">
              <Input label="Mínimo (repor quando ≤)" value={pol.minLevel} onChange={(e) => setPol({ ...pol, minLevel: e.target.value })} />
              <Input label="Máximo (repor até)" value={pol.maxLevel} onChange={(e) => setPol({ ...pol, maxLevel: e.target.value })} />
            </div>
            <Button type="submit" disabled={setPolicy.isPending}>Definir política</Button>
            <p className="text-xs text-slate-400">Quando o saldo cair a ≤ mínimo, o item aparece em Reposição.</p>
          </form>
        </Card>
      </div>
      )}
    </>
  );
}
