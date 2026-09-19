"use client";

import { useCallback, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, OrderReceiptSummary, OrderView, SupplierFullView } from "@/lib/api";
import { StatusPill } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Scanner, useBarcodeSupport } from "@/components/scanner";

const OCORRENCIAS = ["Avaria", "Falta", "Excesso", "Divergencia"] as const;
const digitos = (s: string) => s.replace(/\D/g, "");
const qtd = (v: number) => Number(v).toLocaleString("pt-BR", { maximumFractionDigits: 4 });

/** Chave de acesso da NF-e: CNPJ do emitente nas posições 6–19, número da nota nas 25–33. */
function lerChave(texto: string): { cnpj: string; nf: string } | null {
  const d = digitos(texto);
  return d.length === 44 ? { cnpj: d.substring(6, 20), nf: String(Number(d.substring(25, 34))) } : null;
}

type Linha = { recebida: number; avariada: number; ocorrencia: string; nota: string };
const vazia: Linha = { recebida: 0, avariada: 0, ocorrencia: "", nota: "" };

/**
 * Conferência na doca para telemóvel. Bipa o DANFE → a chave aponta o fornecedor e preenche o
 * número da nota; bipa a caixa → soma 1 na linha do item. Onde a câmara não lê código
 * (iOS), o mesmo campo aceita digitação. O registro usa o endpoint da conferência de mesa.
 */
export default function DocaPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const suportaCamera = useBarcodeSupport();
  const [camera, setCamera] = useState(false);
  const [codigo, setCodigo] = useState("");
  const [cnpjLido, setCnpjLido] = useState<string | null>(null);
  const [nf, setNf] = useState("");
  const [oc, setOc] = useState<OrderView | null>(null);
  const [linhas, setLinhas] = useState<Record<string, Linha>>({});
  const [aviso, setAviso] = useState<string | null>(null);

  const orders = useQuery({ queryKey: ["orders"], queryFn: () => api<OrderView[]>("/purchases/orders") });
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: () => api<SupplierFullView[]>("/purchases/suppliers") });
  const resumo = useQuery({
    queryKey: ["receipts", oc?.id], enabled: !!oc,
    queryFn: () => api<OrderReceiptSummary>(`/purchases/orders/${oc!.id}/receipts`),
  });

  const pendentes = (resumo.data?.lines ?? []).filter((l) => l.quantityPending > 0);
  const linha = (id: string) => linhas[id] ?? vazia;
  const set = (id: string, patch: Partial<Linha>) => setLinhas((s) => ({ ...s, [id]: { ...(s[id] ?? vazia), ...patch } }));

  // OCs abertas a recebimento; com o DANFE bipado, só as do fornecedor da nota.
  const abertas = (orders.data ?? []).filter((o) => o.status === "Issued" || o.status === "PartiallyReceived");
  const cnpjDo = (code: string) => digitos((suppliers.data ?? []).find((s) => s.code === code)?.taxId ?? "");
  const candidatas = cnpjLido ? abertas.filter((o) => cnpjDo(o.supplierCode) === cnpjLido) : abertas;

  const processar = useCallback((texto: string) => {
    const chave = lerChave(texto);
    if (chave) {
      setCnpjLido(chave.cnpj);
      setNf(chave.nf);
      setAviso(`DANFE lido: NF ${chave.nf}.`);
      return;
    }
    if (!oc) { setAviso(`"${texto}" não é uma chave de NF-e. Escolha a OC primeiro.`); return; }
    const alvo = pendentes.find((l) => l.itemCode.toUpperCase() === texto.trim().toUpperCase());
    if (!alvo) { setAviso(`Item "${texto}" não está pendente nesta OC.`); return; }
    setLinhas((s) => ({ ...s, [alvo.orderLineId]: { ...(s[alvo.orderLineId] ?? vazia), recebida: (s[alvo.orderLineId]?.recebida ?? 0) + 1 } }));
    setAviso(`+1 ${alvo.itemCode}`);
  }, [oc, pendentes]);

  const registrar = useMutation({
    mutationFn: () => api<{ orderComplete: boolean; hasOccurrence: boolean }>(`/purchases/orders/${oc!.id}/receipts`, {
      method: "POST",
      body: JSON.stringify({
        invoiceNumber: nf,
        lines: pendentes.filter((l) => linha(l.orderLineId).recebida > 0).map((l) => ({
          orderLineId: l.orderLineId,
          quantityReceived: linha(l.orderLineId).recebida,
          quantityDamaged: linha(l.orderLineId).avariada,
          occurrence: linha(l.orderLineId).ocorrencia || null,
          occurrenceNote: linha(l.orderLineId).nota || null,
        })),
      }),
    }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["receipts", oc!.id] });
      qc.invalidateQueries({ queryKey: ["orders"] });
      setLinhas({});
      toast.push("success", r.orderComplete ? "Recebimento registrado — OC completa." : "Recebimento parcial registrado.");
      if (r.orderComplete) { setOc(null); setCnpjLido(null); setNf(""); }
    },
    onError: (e) => toast.push("error", e instanceof ApiError ? e.message : "Não foi possível registrar."),
  });

  const conferidas = pendentes.filter((l) => linha(l.orderLineId).recebida > 0).length;
  const podeRegistrar = !!oc && conferidas > 0 && nf.trim().length > 0 && !registrar.isPending;

  return (
    <div className="mx-auto max-w-md pb-28">
      <h1 className="text-lg font-semibold text-slate-800">Doca — conferência</h1>

      {/* ---- Leitor: câmara quando há suporte, digitação sempre ---- */}
      <div className="mt-3 space-y-2">
        {camera && <Scanner onRead={processar} onClose={() => setCamera(false)} />}
        {!camera && suportaCamera && (
          <button type="button" onClick={() => setCamera(true)}
            className="w-full rounded-xl bg-brand py-3 text-base font-medium text-white">
            📷 Ler código (DANFE ou caixa)
          </button>
        )}
        <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); if (codigo.trim()) { processar(codigo); setCodigo(""); } }}>
          <input value={codigo} onChange={(e) => setCodigo(e.target.value)} inputMode="text" autoComplete="off"
            placeholder={suportaCamera ? "ou digite o código" : "chave da NF-e ou código do item"}
            className="flex-1 rounded-xl border border-slate-300 px-3 py-3 text-base" />
          <button type="submit" className="rounded-xl border border-slate-300 px-4 text-base">OK</button>
        </form>
        {!suportaCamera && (
          <p className="text-xs text-slate-400">Este navegador não lê código pela câmara — use o coletor ou digite.</p>
        )}
        {aviso && <p className="rounded-lg bg-slate-100 px-3 py-2 text-sm text-slate-700">{aviso}</p>}
      </div>

      {/* ---- Escolha da OC ---- */}
      {!oc ? (
        <div className="mt-4 space-y-2">
          <div className="text-xs uppercase tracking-wide text-slate-400">
            {cnpjLido ? "OCs abertas deste fornecedor" : "OCs abertas a recebimento"}
          </div>
          {candidatas.length === 0 && (
            <p className="py-6 text-center text-sm text-slate-400">
              {cnpjLido ? "Nenhuma OC aberta para o CNPJ do DANFE." : "Nenhuma OC aguardando recebimento."}
            </p>
          )}
          {candidatas.map((o) => (
            <button key={o.id} type="button" onClick={() => setOc(o)}
              className="flex w-full items-center justify-between rounded-xl border border-slate-200 bg-white px-4 py-3 text-left active:bg-slate-50">
              <div>
                <div className="font-medium">OC {o.number}</div>
                <div className="text-xs text-slate-500">{o.supplierName}</div>
              </div>
              <StatusPill status={o.status} />
            </button>
          ))}
          {cnpjLido && (
            <button type="button" onClick={() => setCnpjLido(null)} className="text-xs text-slate-500 underline">
              ver todas as OCs
            </button>
          )}
        </div>
      ) : (
        <div className="mt-4 space-y-3">
          <div className="flex items-center justify-between rounded-xl bg-brand-soft px-4 py-3">
            <div>
              <div className="font-medium text-brand-fg">OC {oc.number}</div>
              <div className="text-xs text-slate-600">{oc.supplierName}</div>
            </div>
            <button type="button" onClick={() => { setOc(null); setLinhas({}); }} className="text-sm text-slate-600 underline">trocar</button>
          </div>

          <label className="block">
            <span className="mb-1 block text-xs font-medium text-slate-600">Nota fiscal</span>
            <input value={nf} onChange={(e) => setNf(e.target.value)} inputMode="numeric"
              className="w-full rounded-xl border border-slate-300 px-3 py-3 text-lg tabular-nums" />
          </label>

          {pendentes.length === 0 && !resumo.isLoading && (
            <p className="py-6 text-center text-sm text-slate-400">Nada pendente nesta OC.</p>
          )}
          {pendentes.map((l) => {
            const v = linha(l.orderLineId);
            const exigeOcorrencia = v.avariada > 0 || v.recebida > l.quantityPending;
            return (
              <div key={l.orderLineId} className={`rounded-xl border bg-white p-3 ${v.recebida > 0 ? "border-emerald-300" : "border-slate-200"}`}>
                <div className="flex items-baseline justify-between">
                  <div className="font-mono text-sm font-medium">{l.itemCode}</div>
                  <div className="text-xs text-slate-500">falta {qtd(l.quantityPending)} {l.unit}</div>
                </div>
                <div className="mt-2 grid grid-cols-2 gap-2">
                  <Contador rotulo="Recebido" valor={v.recebida} onChange={(n) => set(l.orderLineId, { recebida: n })} />
                  <Contador rotulo="Avariado" valor={v.avariada} onChange={(n) => set(l.orderLineId, { avariada: n })} tom="rose" />
                </div>
                {(exigeOcorrencia || v.ocorrencia) && (
                  <div className="mt-2 space-y-2">
                    <select value={v.ocorrencia} onChange={(e) => set(l.orderLineId, { ocorrencia: e.target.value })}
                      className={`w-full rounded-xl border px-3 py-2 text-base ${exigeOcorrencia && !v.ocorrencia ? "border-rose-400" : "border-slate-300"}`}>
                      <option value="">Ocorrência…</option>
                      {OCORRENCIAS.map((o) => <option key={o} value={o}>{o}</option>)}
                    </select>
                    <input value={v.nota} onChange={(e) => set(l.orderLineId, { nota: e.target.value })}
                      placeholder="descreva a não-conformidade"
                      className="w-full rounded-xl border border-slate-300 px-3 py-2 text-base" />
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* ---- Ação fixa no rodapé ---- */}
      {oc && (
        <div className="fixed inset-x-0 bottom-0 border-t border-slate-200 bg-white/95 p-3 backdrop-blur">
          <div className="mx-auto max-w-md">
            <button type="button" disabled={!podeRegistrar} onClick={() => registrar.mutate()}
              className="w-full rounded-xl bg-brand py-4 text-base font-semibold text-white disabled:opacity-40">
              {registrar.isPending ? "Registrando…" : `Registrar recebimento (${conferidas} item${conferidas === 1 ? "" : "ns"})`}
            </button>
            {!nf.trim() && oc && <p className="mt-1 text-center text-xs text-rose-600">Informe a nota fiscal.</p>}
          </div>
        </div>
      )}
    </div>
  );
}

/** Campo numérico com botões grandes — dedo na doca, não cursor de desktop. */
function Contador({ rotulo, valor, onChange, tom = "slate" }: {
  rotulo: string; valor: number; onChange: (n: number) => void; tom?: "slate" | "rose";
}) {
  const cor = tom === "rose" ? "text-rose-700" : "text-slate-800";
  return (
    <div>
      <div className="mb-1 text-xs text-slate-500">{rotulo}</div>
      <div className="flex items-stretch overflow-hidden rounded-xl border border-slate-300">
        <button type="button" onClick={() => onChange(Math.max(0, valor - 1))} className="w-11 text-xl active:bg-slate-100">−</button>
        <input inputMode="decimal" value={valor === 0 ? "" : String(valor)} placeholder="0"
          onChange={(e) => onChange(Number(e.target.value.replace(",", ".")) || 0)}
          className={`w-full min-w-0 py-2 text-center text-xl tabular-nums outline-none ${cor}`} />
        <button type="button" onClick={() => onChange(valor + 1)} className="w-11 text-xl active:bg-slate-100">+</button>
      </div>
    </div>
  );
}
