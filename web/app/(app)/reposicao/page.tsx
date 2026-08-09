"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { api, ApiError, Suggestion } from "@/lib/api";
import { Button, Card, Empty, Table } from "@/components/ui";

export default function ReposicaoPage() {
  const qc = useQueryClient();
  const router = useRouter();
  const [msg, setMsg] = useState<string | null>(null);
  const suggestions = useQuery({ queryKey: ["suggestions"], queryFn: () => api<Suggestion[]>("/materials/replenishment/suggestions") });

  const generate = useMutation({
    mutationFn: () => api<{ requisitionId: string; lines: number }>("/purchases/requisitions/from-suggestions", { method: "POST" }),
    onSuccess: (r) => { qc.invalidateQueries({ queryKey: ["requisitions"] }); setMsg(`Requisição criada com ${r.lines} item(ns).`); setTimeout(() => router.push("/compras"), 900); },
    onError: (e) => setMsg(e instanceof ApiError ? e.message : "Erro"),
  });

  const count = suggestions.data?.length ?? 0;

  return (
    <>
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-slate-800">Reposição</h1>
        <Button disabled={count === 0 || generate.isPending} onClick={() => { setMsg(null); generate.mutate(); }}>
          Gerar requisição das sugestões
        </Button>
      </div>
      {msg && <p className="rounded-lg bg-brand-soft px-3 py-2 text-sm text-brand-fg">{msg}</p>}

      <Card title={`Sugestões (${count})`}>
        {suggestions.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : count > 0 ? (
          <Table head={["Item", "Saldo", "Mínimo", "Máximo", "Comprar"]}>
            {suggestions.data!.map((s) => (
              <tr key={s.itemId}>
                <td className="px-3 py-2">
                  <span className="font-mono text-xs">{s.itemCode}</span> <span className="text-slate-500">{s.itemName}</span>
                </td>
                <td className="px-3 py-2 tabular-nums">{Number(s.balance)}</td>
                <td className="px-3 py-2 tabular-nums text-slate-500">{Number(s.minLevel)}</td>
                <td className="px-3 py-2 tabular-nums text-slate-500">{Number(s.maxLevel)}</td>
                <td className="px-3 py-2 font-semibold tabular-nums text-brand">{Number(s.suggestedQuantity)}</td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Nenhum item no ponto de reposição. Defina políticas (mín/máx) e movimente o estoque.</Empty>
        )}
      </Card>
    </>
  );
}
