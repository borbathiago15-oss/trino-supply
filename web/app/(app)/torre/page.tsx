"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ControlTowerRow, ControlTowerView } from "@/lib/api";
import { Card, Empty, StatusPill, Table } from "@/components/ui";

const money = (v: number) => Number(v).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const qtd = (v: number) => Number(v).toLocaleString("pt-BR", { maximumFractionDigits: 4 });
const dt = (s?: string | null) => (s ? new Date(s).toLocaleDateString("pt-BR") : "—");

const FAROL: Record<string, { cor: string; rotulo: string }> = {
  Red: { cor: "bg-rose-500", rotulo: "atrasado" },
  Yellow: { cor: "bg-amber-400", rotulo: "vence em breve" },
  Green: { cor: "bg-emerald-500", rotulo: "no prazo" },
  None: { cor: "bg-slate-300", rotulo: "encerrado" },
};

/**
 * Torre de Controlo (Fase 04): cada item da solicitação com tudo o que aconteceu com ele —
 * aprovação, cotação, OC, recebimento e nota — e um farol de SLA. Leitura viva dos registros:
 * não há tabela própria, então o que aparece aqui é o que aconteceu.
 */
export default function TorrePage() {
  const [dias, setDias] = useState(90);
  const [busca, setBusca] = useState("");
  const [apenasAtrasados, setApenasAtrasados] = useState(false);
  const [apenasUrgentes, setApenasUrgentes] = useState(false);
  const [semOc, setSemOc] = useState(false);
  const [etapa, setEtapa] = useState("");

  const params = new URLSearchParams({ days: String(dias) });
  if (busca.trim()) params.set("q", busca.trim());
  if (apenasAtrasados) params.set("late", "true");
  if (apenasUrgentes) params.set("urgent", "true");
  if (semOc) params.set("withoutOrder", "true");
  if (etapa) params.set("stage", etapa);

  const torre = useQuery({
    queryKey: ["control-tower", params.toString()],
    queryFn: () => api<ControlTowerView>(`/purchases/control-tower?${params}`),
  });
  const s = torre.data?.summary;

  const Chip = ({ ativo, onClick, children }: { ativo: boolean; onClick: () => void; children: React.ReactNode }) => (
    <button type="button" onClick={onClick}
      className={`rounded-full border px-3 py-1 text-xs ${ativo ? "border-brand bg-brand-soft text-brand-fg" : "border-slate-300 text-slate-600 hover:bg-slate-50"}`}>
      {children}
    </button>
  );

  return (
    <>
      <h1 className="text-xl font-semibold text-slate-800">Torre de Controlo</h1>

      {/* ---- Cards ---- */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[
          ["Itens em aberto", s?.openItems ?? "—", ""],
          ["Atrasados 🔴", s?.lateItems ?? "—", (s?.lateItems ?? 0) > 0 ? "text-rose-600" : ""],
          ["Lead time real (dias)", s?.avgLeadTimeDays ?? "—", ""],
          ["Backlog em OC", s ? `R$ ${money(s.backlogValue)}` : "—", ""],
        ].map(([rotulo, valor, cor]) => (
          <div key={String(rotulo)} className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="text-xs uppercase tracking-wide text-slate-400">{rotulo}</div>
            <div className={`mt-1 text-2xl font-semibold tabular-nums ${cor}`}>{valor}</div>
          </div>
        ))}
      </div>

      <Card title="Itens"
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <input value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="item, solicitante, centro, fornecedor, OC…"
              className="w-64 rounded-lg border border-slate-300 px-2 py-1 text-sm" />
            <select value={dias} onChange={(e) => setDias(Number(e.target.value))}
              className="rounded-lg border border-slate-300 bg-white px-2 py-1 text-sm">
              <option value={30}>30 dias</option>
              <option value={90}>90 dias</option>
              <option value={180}>180 dias</option>
              <option value={365}>1 ano</option>
            </select>
          </div>
        }>
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <Chip ativo={apenasAtrasados} onClick={() => setApenasAtrasados(!apenasAtrasados)}>Apenas atrasados</Chip>
          <Chip ativo={apenasUrgentes} onClick={() => setApenasUrgentes(!apenasUrgentes)}>Apenas urgentes</Chip>
          <Chip ativo={semOc} onClick={() => setSemOc(!semOc)}>Sem OC</Chip>
          <select value={etapa} onChange={(e) => setEtapa(e.target.value)}
            className="rounded-full border border-slate-300 bg-white px-3 py-1 text-xs">
            <option value="">Todas as etapas</option>
            <option value="Awaiting">Em aprovação</option>
            <option value="Approved">Aprovado sem OC</option>
            <option value="Quoting">Em cotação</option>
            <option value="Ordered">OC emitida</option>
            <option value="PartiallyReceived">Recebido parcial</option>
            <option value="Received">Recebido</option>
            <option value="FulfilledFromStock">Atendido pelo estoque</option>
            <option value="Rejected">Reprovado</option>
          </select>
          {s && (
            <span className="ml-auto text-xs text-slate-400">
              {s.urgentItems} urgente(s) · {s.withoutOrder} sem OC
            </span>
          )}
        </div>

        {torre.isLoading ? (
          <Empty>Carregando…</Empty>
        ) : (torre.data?.rows.length ?? 0) === 0 ? (
          <Empty>Nenhum item nesse recorte.</Empty>
        ) : (
          <Table head={["SLA", "Item", "Centro de custo", "Solicitante", "Etapa", "OC · Fornecedor", "Prazo", "Pendente", "NF"]}>
            {torre.data!.rows.map((r) => <Linha key={r.lineId} r={r} />)}
          </Table>
        )}
      </Card>
    </>
  );
}

function Linha({ r }: { r: ControlTowerRow }) {
  const farol = FAROL[r.light] ?? FAROL.None;
  const prazo = r.promisedDate ?? r.neededBy;
  return (
    <tr className={r.light === "Red" ? "bg-rose-50/50" : !r.isOpen ? "text-slate-400" : ""}>
      <td className="px-3 py-2">
        <span className="inline-flex items-center gap-1 text-xs" title={farol.rotulo}>
          <span className={`inline-block h-2.5 w-2.5 rounded-full ${farol.cor}`} />
          {r.priority === "Emergencial" && <span className="text-rose-600">⚡</span>}
        </span>
      </td>
      <td className="px-3 py-2">
        <div className="font-mono text-xs">{r.itemCode}</div>
        <div className="text-xs text-slate-500">{qtd(r.quantity)} {r.unit} · {dt(r.createdAt)}</div>
      </td>
      <td className="px-3 py-2 text-sm">{r.costCenterName || r.costCenterCode}</td>
      <td className="px-3 py-2 text-sm">{r.requester}</td>
      <td className="px-3 py-2"><StatusPill status={r.stage} /></td>
      <td className="px-3 py-2 text-sm">
        {r.orderNumber
          ? <>OC {r.orderNumber} · {r.supplierName}<div className="text-xs text-slate-400">{r.buyer}</div></>
          : <span className="text-slate-400">—</span>}
      </td>
      <td className="px-3 py-2 text-sm tabular-nums">
        {prazo ? dt(prazo) : <span className="text-slate-400">sem prazo</span>}
        {r.promisedDate && <div className="text-[11px] text-slate-400">prometido na OC</div>}
      </td>
      <td className="px-3 py-2 text-right tabular-nums">
        {r.isOpen ? qtd(r.pendingQuantity) : "—"}
      </td>
      <td className="px-3 py-2 text-xs">
        {r.invoiceNumbers
          ? <span className={r.invoiceMatch === "Divergent" ? "text-rose-600" : r.invoiceMatch === "Matched" ? "text-emerald-600" : ""}>
              {r.invoiceNumbers}
            </span>
          : <span className="text-slate-400">—</span>}
      </td>
    </tr>
  );
}
