"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, AuditView } from "@/lib/api";
import { Button, Card, Empty, Input, Table } from "@/components/ui";
import { downloadCsv } from "@/lib/csv";

const when = (s: string) => new Date(s).toLocaleString("pt-BR");

export default function AuditoriaPage() {
  const audit = useQuery({ queryKey: ["audit"], queryFn: () => api<AuditView[]>("/audit?limit=200") });
  const [q, setQ] = useState("");

  const filtered = useMemo(() => {
    const term = q.trim().toLowerCase();
    const rows = audit.data ?? [];
    if (!term) return rows;
    return rows.filter((a) =>
      a.action.toLowerCase().includes(term) ||
      a.actor.toLowerCase().includes(term) ||
      (a.targetType ?? "").toLowerCase().includes(term));
  }, [audit.data, q]);

  const exportar = () => downloadCsv(
    "auditoria.csv",
    ["Data/hora", "Ator", "Ação", "TipoRecurso", "IdRecurso", "Detalhes"],
    filtered.map((a) => [when(a.occurredAt), a.actor, a.action, a.targetType, a.targetId, a.metadata]),
  );

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Auditoria</h1>
      <p className="text-sm text-slate-500">
        Trilha imutável de ações sensíveis (append-only). Registra quem fez o quê, quando e sobre qual recurso —
        gravada na mesma transação da operação.
      </p>

      <Card
        title="Registros recentes"
        actions={
          <div className="flex items-center gap-2">
            <Input placeholder="Filtrar por ação/ator/recurso" value={q} onChange={(e) => setQ(e.target.value)} className="w-64" />
            <Button variant="ghost" onClick={exportar}>Exportar CSV</Button>
          </div>
        }
      >
        {audit.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : filtered.length > 0 ? (
          <Table head={["Data/hora", "Ator", "Ação", "Recurso", "Detalhes"]}>
            {filtered.map((a) => (
              <tr key={a.id}>
                <td className="whitespace-nowrap px-3 py-2 text-slate-500">{when(a.occurredAt)}</td>
                <td className="px-3 py-2">{a.actor}</td>
                <td className="px-3 py-2"><code className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-700">{a.action}</code></td>
                <td className="px-3 py-2 text-slate-500">
                  {a.targetType ? <span>{a.targetType}{a.targetId ? <span className="font-mono text-xs"> · {a.targetId.slice(0, 8)}</span> : null}</span> : "—"}
                </td>
                <td className="max-w-xs truncate px-3 py-2 text-xs text-slate-400" title={a.metadata ?? ""}>{a.metadata ?? "—"}</td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>{q ? "Nenhum registro para o filtro." : "Nenhum registro de auditoria."}</Empty>
        )}
      </Card>
    </>
  );
}
