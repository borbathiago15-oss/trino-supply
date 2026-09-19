"use client";

import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, upload, MatchLineView, OrderMatchView, PurchaseInvoiceView } from "@/lib/api";
import { Button, Empty, StatusPill, Table } from "@/components/ui";
import { Perm, useHas } from "@/lib/me";

const money = (v: number) => Number(v).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const qtd = (v: number) => Number(v).toLocaleString("pt-BR", { maximumFractionDigits: 4 });
const dt = (s?: string | null) => (s ? new Date(s).toLocaleDateString("pt-BR") : "—");
const chave = (k: string) => k.replace(/(\d{4})(?=\d)/g, "$1 ");

/**
 * Conciliação fiscal de três pontas (Fase 05) no detalhe da OC: sobe o XML da NF-e e mostra, item a
 * item, o que foi <b>pedido</b>, o que foi <b>faturado</b> e o que <b>chegou</b> na doca — com o
 * veredito de cada linha. Divergência trava o envio ao financeiro; liberar a exceção exige motivo.
 */
export function ConciliacaoFiscal({ orderId, onErr, onOk }: {
  orderId: string; onErr: (e: unknown) => void; onOk: (m: string) => void;
}) {
  const qc = useQueryClient();
  const has = useHas();
  const fileRef = useRef<HTMLInputElement>(null);
  const [arrastando, setArrastando] = useState(false);

  const conc = useQuery({
    queryKey: ["order-invoices", orderId],
    queryFn: () => api<OrderMatchView>(`/purchases/orders/${orderId}/invoices`),
  });

  const importar = useMutation({
    mutationFn: (file: File) => upload(`/purchases/orders/${orderId}/invoices`, file),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["order-invoices", orderId] });
      onOk("NF-e importada e conciliada.");
    },
    onError: onErr,
  });

  const podeOperar = has(Perm.PurchasesOrder) || has(Perm.MaterialsManage);
  const dados = conc.data;

  const receber = (files: FileList | null) => {
    const file = files?.[0];
    if (!file) return;
    if (!file.name.toLowerCase().endsWith(".xml")) {
      onErr(new Error("Envie o arquivo XML da NF-e."));
      return;
    }
    importar.mutate(file);
  };

  return (
    <div className="space-y-3">
      {podeOperar && (
        <div
          onDragOver={(e) => { e.preventDefault(); setArrastando(true); }}
          onDragLeave={() => setArrastando(false)}
          onDrop={(e) => { e.preventDefault(); setArrastando(false); receber(e.dataTransfer.files); }}
          className={`rounded-xl border-2 border-dashed px-4 py-6 text-center text-sm transition ${
            arrastando ? "border-brand bg-brand-soft" : "border-slate-300 bg-slate-50"}`}
        >
          <p className="text-slate-600">
            Arraste aqui o <strong>XML da NF-e</strong> (modelo 55) ou
            {" "}
            <button type="button" className="text-brand underline" onClick={() => fileRef.current?.click()}>
              escolha o arquivo
            </button>.
          </p>
          <p className="mt-1 text-xs text-slate-400">
            Nada é digitado: chave de acesso, emitente, itens e valores saem do próprio arquivo.
          </p>
          <input ref={fileRef} type="file" accept=".xml,text/xml,application/xml" className="hidden"
            onChange={(e) => { receber(e.target.files); e.target.value = ""; }} />
          {importar.isPending && <p className="mt-2 text-xs text-slate-500">Lendo e conciliando…</p>}
        </div>
      )}

      {dados && dados.invoices.length > 0 && (
        <p className="text-xs text-slate-400">
          Tolerâncias: preço {dados.priceTolerancePercent}% · quantidade {dados.quantityTolerancePercent}%
        </p>
      )}

      {conc.isLoading ? (
        <p className="py-4 text-center text-sm text-slate-400">Carregando…</p>
      ) : !dados || dados.invoices.length === 0 ? (
        <Empty>Nenhuma nota fiscal importada para esta OC.</Empty>
      ) : (
        dados.invoices.map((nf) => (
          <NotaConciliada key={nf.id} nf={nf} orderId={orderId} onErr={onErr} onOk={onOk} podeOperar={podeOperar} />
        ))
      )}
    </div>
  );
}

function NotaConciliada({ nf, orderId, onErr, onOk, podeOperar }: {
  nf: PurchaseInvoiceView; orderId: string; onErr: (e: unknown) => void;
  onOk: (m: string) => void; podeOperar: boolean;
}) {
  const qc = useQueryClient();
  const [motivo, setMotivo] = useState("");
  const invalidar = () => qc.invalidateQueries({ queryKey: ["order-invoices", orderId] });

  const liberar = useMutation({
    mutationFn: () => api(`/purchases/invoices/${nf.id}/release`, {
      method: "POST", body: JSON.stringify({ note: motivo }),
    }),
    onSuccess: () => { setMotivo(""); invalidar(); onOk("Nota liberada para o financeiro."); },
    onError: onErr,
  });

  const reconciliar = useMutation({
    mutationFn: () => api(`/purchases/invoices/${nf.id}/rematch`, { method: "POST" }),
    onSuccess: () => { invalidar(); onOk("Conciliação reprocessada."); },
    onError: onErr,
  });

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="text-sm">
          <div className="font-medium text-slate-700">
            NF-e {nf.number}/{nf.series} · {nf.emitterName}
          </div>
          <div className="font-mono text-xs text-slate-400">{chave(nf.accessKey)}</div>
          <div className="text-xs text-slate-500">
            Emitida em {dt(nf.issuedAt)} · R$ {money(nf.totalValue)} · importada por {nf.importedBy}
          </div>
        </div>
        <div className="flex items-center gap-2">
          <StatusPill status={nf.status} />
          {nf.releasedToFinance ? (
            <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700">
              liberada ao financeiro
            </span>
          ) : (
            <span className="rounded-full bg-rose-100 px-2 py-0.5 text-xs font-medium text-rose-700">
              envio travado
            </span>
          )}
        </div>
      </div>

      <Table head={["Item", "Pedido (OC)", "Faturado (NF-e)", "Físico (doca)", "Situação"]}>
        {nf.match.map((l) => <LinhaConciliada key={l.itemCode} l={l} />)}
      </Table>

      {nf.matchSummary && nf.status === "Divergent" && (
        <p className="mt-3 rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-800">{nf.matchSummary}</p>
      )}

      {nf.releaseNote && (
        <p className="mt-2 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
          Liberada por <strong>{nf.releasedBy}</strong> em {dt(nf.releasedAt)}: {nf.releaseNote}
        </p>
      )}

      {podeOperar && (
        <div className="mt-3 flex flex-wrap items-end gap-2 border-t border-slate-100 pt-3">
          {!nf.releasedToFinance && (
            <>
              <label className="block flex-1 text-sm">
                <span className="mb-1 block font-medium text-slate-600">
                  Justificativa para liberar mesmo com divergência
                </span>
                <input value={motivo} onChange={(e) => setMotivo(e.target.value)}
                  placeholder="acordo comercial, frete embutido, reajuste combinado…"
                  className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-sm" />
              </label>
              <Button disabled={!motivo.trim() || liberar.isPending} onClick={() => liberar.mutate()}>
                Liberar ao financeiro
              </Button>
            </>
          )}
          <Button variant="ghost" disabled={reconciliar.isPending} onClick={() => reconciliar.mutate()}>
            Reconciliar
          </Button>
        </div>
      )}
      {!nf.releasedToFinance && (
        <p className="mt-1 text-xs text-slate-400">
          Corrigiu a conferência na doca? Use <em>Reconciliar</em> antes de justificar — pode ser que feche sozinho.
        </p>
      )}
    </div>
  );
}

/** Uma linha nas quatro colunas do prompt: pedido, faturado, físico e o veredito. */
function LinhaConciliada({ l }: { l: MatchLineView }) {
  const bloqueia = l.divergences.filter((d) => !d.withinTolerance);
  const avisos = l.divergences.filter((d) => d.withinTolerance);

  return (
    <>
      <tr className={bloqueia.length > 0 ? "bg-rose-50/60" : ""}>
        <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
        <td className="px-3 py-2 tabular-nums">
          {qtd(l.quantityOrdered)} × R$ {money(l.unitPriceOrdered)}
        </td>
        <td className="px-3 py-2 tabular-nums">
          {l.notInvoiced
            ? <span className="text-slate-400">não faturado nesta nota</span>
            : <>{qtd(l.quantityInvoiced ?? 0)} × R$ {money(l.unitPriceInvoiced ?? 0)}</>}
        </td>
        <td className="px-3 py-2 tabular-nums">
          {qtd(l.quantityReceived)} líq.
          {l.quantityDamaged > 0 && (
            <span className="ml-1 rounded bg-amber-100 px-1 text-xs text-amber-700">
              {qtd(l.quantityDamaged)} avariada(s)
            </span>
          )}
        </td>
        <td className="px-3 py-2">
          {l.matched
            ? <span className="text-emerald-600" title="Match perfeito">🟢 confere</span>
            : <span className="font-medium text-rose-600">🔴 divergência</span>}
        </td>
      </tr>
      {(bloqueia.length > 0 || avisos.length > 0) && (
        <tr>
          <td colSpan={5} className="px-3 pb-2">
            {bloqueia.map((d) => (
              <div key={d.code + d.kind} className="text-xs text-rose-700">
                <span className="font-mono">{d.code}</span> — {d.message}
              </div>
            ))}
            {avisos.map((d) => (
              <div key={"ok" + d.code + d.kind} className="text-xs text-slate-500">{d.message}</div>
            ))}
          </td>
        </tr>
      )}
    </>
  );
}
