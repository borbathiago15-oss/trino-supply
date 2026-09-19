"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  api, QuotationView, QuotationLineView, RequisitionView, SupplierFullView,
} from "@/lib/api";
import { Button, Card, Empty, Input, StatusPill, Table } from "@/components/ui";
import { OtifBadge, TierChip, useScorecards } from "@/components/otif";

const money = (v: number) => Number(v).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const dt = (s?: string | null) => (s ? new Date(s).toLocaleDateString("pt-BR") : "—");

/**
 * Abre a concorrência a partir de uma requisição aprovada: escolhe os fornecedores convidados e o
 * prazo de resposta. Só itens ainda sem OC vão a mercado — o backend recusa o resto.
 */
export function AbrirCotacao({ req, onSuccess, onErr }: {
  req: RequisitionView; onSuccess: (id: string) => void; onErr: (e: unknown) => void;
}) {
  const [open, setOpen] = useState(false);
  const [escolhidos, setEscolhidos] = useState<Record<string, boolean>>({});
  const [prazo, setPrazo] = useState("");
  const [notas, setNotas] = useState("");

  const suppliers = useQuery({
    queryKey: ["suppliers"], enabled: open,
    queryFn: () => api<SupplierFullView[]>("/purchases/suppliers"),
  });
  const otif = useScorecards();
  const convidados = Object.entries(escolhidos).filter(([, v]) => v).map(([k]) => k);
  const pendentes = req.lines.filter((l) => !l.purchaseOrderId);

  const abrir = useMutation({
    mutationFn: () => api<{ quotationId: string }>(`/purchases/requisitions/${req.id}/quotation`, {
      method: "POST",
      body: JSON.stringify({
        supplierCodes: convidados,
        // Fim do dia escolhido — o prazo é uma data no calendário do comprador, não um instante.
        closesAt: new Date(`${prazo}T23:59:59`).toISOString(),
        notes: notas || null,
      }),
    }),
    onSuccess: (r) => { setOpen(false); setEscolhidos({}); setNotas(""); onSuccess(r.quotationId); },
    onError: onErr,
  });

  if (!open) {
    return (
      <Button variant="ghost" onClick={() => setOpen(true)}>
        Levar a cotação ({pendentes.length} item{pendentes.length === 1 ? "" : "ns"})
      </Button>
    );
  }

  return (
    <div className="mt-3 space-y-3 border-t border-slate-100 pt-3">
      <p className="text-xs text-slate-500">
        Vão a mercado os {pendentes.length} item(ns) ainda sem OC. Convide ao menos <strong>dois</strong>
        {" "}fornecedores — com um só isto é compra direta, não concorrência.
      </p>

      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <Input label="Prazo para receber propostas" type="date" value={prazo}
          min={new Date().toISOString().slice(0, 10)}
          onChange={(e) => setPrazo(e.target.value)} />
        <Input label="Observações ao fornecedor (opcional)" value={notas}
          onChange={(e) => setNotas(e.target.value)} />
      </div>

      <div className="max-h-56 overflow-y-auto rounded-lg border border-slate-200">
        {(suppliers.data ?? []).map((s) => (
          <label key={s.id} className="flex items-center gap-2 border-b border-slate-100 px-3 py-2 text-sm last:border-0">
            <input type="checkbox" className="h-4 w-4 rounded border-slate-300"
              checked={escolhidos[s.code] ?? false}
              onChange={(e) => setEscolhidos({ ...escolhidos, [s.code]: e.target.checked })} />
            <span className="font-mono text-xs text-slate-500">{s.code}</span>
            <span className="flex-1">{s.name}</span>
            <OtifBadge score={otif.porCodigo(s.code)} />
          </label>
        ))}
        {suppliers.data?.length === 0 && <Empty>Nenhum fornecedor cadastrado.</Empty>}
      </div>

      <div className="flex items-center gap-2">
        <Button disabled={convidados.length < 2 || !prazo || abrir.isPending} onClick={() => abrir.mutate()}>
          Abrir concorrência
        </Button>
        <Button variant="ghost" onClick={() => setOpen(false)}>Cancelar</Button>
        <span className="text-xs text-slate-400">{convidados.length} convidado(s)</span>
      </div>
    </div>
  );
}

/**
 * Mapa de equalização: cada item com todas as propostas lado a lado. Destaca o menor preço, marca
 * a proposta muito acima do último preço pago (o alerta informa, não bloqueia) e recolhe a escolha
 * do comprador — com justificativa obrigatória quando o vencedor não é o mais barato.
 */
export function MapaEqualizacao({ id, onErr, onOk }: {
  id: string; onErr: (e: unknown) => void; onOk: (m: string) => void;
}) {
  const qc = useQueryClient();
  const [escolha, setEscolha] = useState<Record<string, string>>({});
  const [motivo, setMotivo] = useState<Record<string, string>>({});

  const q = useQuery({ queryKey: ["quotation", id], queryFn: () => api<QuotationView>(`/purchases/quotations/${id}`) });
  const cot = q.data;

  const invalidar = () => {
    qc.invalidateQueries({ queryKey: ["quotation", id] });
    qc.invalidateQueries({ queryKey: ["quotations"] });
    qc.invalidateQueries({ queryKey: ["requisitions"] });
    qc.invalidateQueries({ queryKey: ["orders"] });
  };

  const adjudicar = useMutation({
    mutationFn: () => api(`/purchases/quotations/${id}/award`, {
      method: "POST",
      body: JSON.stringify({
        awards: Object.entries(escolha)
          .filter(([, code]) => code)
          .map(([lineId, supplierCode]) => ({ lineId, supplierCode, note: motivo[lineId] || null })),
      }),
    }),
    onSuccess: () => { setEscolha({}); setMotivo({}); invalidar(); onOk("Adjudicação registrada."); },
    onError: onErr,
  });

  const gerarOcs = useMutation({
    mutationFn: () => api<{ orderIds: string[] }>(`/purchases/quotations/${id}/orders`, { method: "POST" }),
    onSuccess: (r) => { invalidar(); onOk(`${r.orderIds.length} OC(s) emitida(s) a partir da cotação.`); },
    onError: onErr,
  });

  if (q.isLoading) return <p className="py-6 text-center text-sm text-slate-400">Carregando o mapa…</p>;
  if (!cot) return <Empty>Cotação não encontrada.</Empty>;

  const pendentesDeEscolha = cot.lines.filter((l) => !l.awardedSupplierId);
  const temEscolha = Object.values(escolha).some((v) => v);

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-1 text-sm sm:grid-cols-2">
        <div><span className="text-slate-400">Cotação:</span> nº {cot.number} · <StatusPill status={cot.status} /></div>
        <div>
          <span className="text-slate-400">Prazo de resposta:</span> {dt(cot.closesAt)}
          {cot.isClosed && <span className="ml-1 text-xs text-amber-600">(encerrado)</span>}
        </div>
        <div><span className="text-slate-400">Aberta por:</span> {cot.createdBy} em {dt(cot.createdAt)}</div>
        {cot.notes && <div><span className="text-slate-400">Observações:</span> {cot.notes}</div>}
      </div>

      {cot.status === "Cancelled" && (
        <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">
          Cotação cancelada por {cot.cancelledBy}: {cot.cancelReason}
        </p>
      )}

      {/* ---- Quem respondeu, em que condições e como entrega ---- */}
      <Card title="Fornecedores convidados">
        <Table head={["Fornecedor", "Resposta", "Itens cotados", "Total proposto", "Cond. Pgto", "Entrega (OTIF)"]}>
          {cot.participants.map((p) => (
            <tr key={p.supplierId} className={p.hasResponded ? "" : "text-slate-400"}>
              <td className="px-3 py-2">{p.supplierCode} — {p.supplierName}</td>
              <td className="px-3 py-2 text-xs">
                {p.hasResponded ? dt(p.respondedAt) : "aguardando"}
                {p.isLate && <span className="ml-1 rounded bg-amber-100 px-1 text-amber-700">fora do prazo</span>}
              </td>
              <td className="px-3 py-2 tabular-nums">{p.itemsQuoted}</td>
              <td className="px-3 py-2 text-right tabular-nums">{p.hasResponded ? `R$ ${money(p.total)}` : "—"}</td>
              <td className="px-3 py-2 text-slate-500">{p.paymentTerms ?? "—"}</td>
              <td className="px-3 py-2"><TierChip tier={p.otifTier} otif={p.otifIndex} /></td>
            </tr>
          ))}
        </Table>
      </Card>

      {/* ---- O mapa item a item ---- */}
      {cot.lines.map((l) => (
        <ItemDoMapa key={l.id} linha={l}
          escolhido={escolha[l.id] ?? ""}
          motivo={motivo[l.id] ?? ""}
          editavel={cot.status === "Open" || cot.status === "PartiallyAwarded"}
          onEscolher={(code) => setEscolha({ ...escolha, [l.id]: code })}
          onMotivo={(m) => setMotivo({ ...motivo, [l.id]: m })} />
      ))}

      <div className="flex flex-wrap items-center gap-2 border-t border-slate-100 pt-3">
        {pendentesDeEscolha.length > 0 && (
          <Button disabled={!temEscolha || adjudicar.isPending} onClick={() => adjudicar.mutate()}>
            Adjudicar selecionados
          </Button>
        )}
        {cot.lines.some((l) => l.awardedSupplierId) && cot.status !== "Cancelled" && (
          <Button variant="ghost" disabled={gerarOcs.isPending} onClick={() => gerarOcs.mutate()}>
            Gerar OCs dos itens adjudicados
          </Button>
        )}
        {pendentesDeEscolha.length > 0 && (
          <span className="text-xs text-slate-400">{pendentesDeEscolha.length} item(ns) sem vencedor</span>
        )}
      </div>
    </div>
  );
}

/** Um item do mapa: as propostas ordenadas por preço, com os sinais que apoiam a decisão. */
function ItemDoMapa({ linha, escolhido, motivo, editavel, onEscolher, onMotivo }: {
  linha: QuotationLineView; escolhido: string; motivo: string; editavel: boolean;
  onEscolher: (code: string) => void; onMotivo: (m: string) => void;
}) {
  const menor = linha.bids.find((b) => b.isLowest);
  const selecionado = linha.bids.find((b) => b.supplierCode === escolhido);
  // A justificativa só é exigida quando o escolhido não é o mais barato — a regra vive no domínio,
  // aqui é só o aviso antes de o comprador levar 400.
  const exigeMotivo = !!selecionado && !selecionado.isLowest;

  return (
    <Card title={`${linha.itemCode} · ${Number(linha.quantity)} ${linha.unit}`}>
      {linha.lastPaidPrice != null && (
        <p className="mb-2 text-xs text-slate-500">
          Último preço pago: <strong>R$ {money(linha.lastPaidPrice)}</strong> em {dt(linha.lastPaidAt)}
        </p>
      )}

      {linha.awardedSupplierId ? (
        <div className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
          Adjudicado a <strong>{linha.awardedSupplierCode}</strong> por R$ {money(linha.awardedUnitPrice ?? 0)}/{linha.unit}
          {linha.awardNote && <div className="mt-1 text-xs text-emerald-700">Justificativa: {linha.awardNote}</div>}
        </div>
      ) : linha.bids.length === 0 ? (
        <Empty>Nenhuma proposta recebida para este item.</Empty>
      ) : (
        <>
          <Table head={["", "Fornecedor", "Unitário", "Total", "Prazo", "vs. menor", "vs. último pago"]}>
            {linha.bids.map((b) => (
              <tr key={b.supplierId} className={b.isLowest ? "bg-emerald-50/50" : ""}>
                <td className="px-3 py-2">
                  <input type="radio" name={`vencedor-${linha.id}`} className="h-4 w-4"
                    disabled={!editavel}
                    checked={escolhido === b.supplierCode}
                    onChange={() => onEscolher(b.supplierCode)}
                    aria-label={`Escolher ${b.supplierCode} para ${linha.itemCode}`} />
                </td>
                <td className="px-3 py-2">
                  {b.supplierCode} — {b.supplierName}
                  {b.isLate && <span className="ml-1 rounded bg-amber-100 px-1 text-xs text-amber-700">fora do prazo</span>}
                </td>
                <td className="px-3 py-2 text-right tabular-nums">
                  R$ {money(b.unitPrice)}
                  {b.isLowest && <span className="ml-1 text-xs text-emerald-600">menor</span>}
                </td>
                <td className="px-3 py-2 text-right tabular-nums">R$ {money(b.totalPrice)}</td>
                <td className="px-3 py-2 tabular-nums">{b.deliveryDays != null ? `${b.deliveryDays} d` : "—"}</td>
                <td className="px-3 py-2 text-right text-xs tabular-nums text-slate-500">
                  {b.percentAboveLowest > 0 ? `+${b.percentAboveLowest}%` : "—"}
                </td>
                <td className="px-3 py-2 text-right text-xs tabular-nums">
                  {b.percentAboveLastPaid == null ? (
                    <span className="text-slate-400">sem histórico</span>
                  ) : b.overpriceAlert ? (
                    <span className="rounded bg-rose-100 px-1.5 py-0.5 font-medium text-rose-700"
                      title="Mais de 15% acima do último preço pago por este item">
                      ⚠️ +{b.percentAboveLastPaid}%
                    </span>
                  ) : (
                    <span className={b.percentAboveLastPaid < 0 ? "text-emerald-600" : "text-slate-500"}>
                      {b.percentAboveLastPaid > 0 ? "+" : ""}{b.percentAboveLastPaid}%
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </Table>

          {exigeMotivo && (
            <div className="mt-2">
              <Input
                label={`Por que ${selecionado!.supplierCode} e não ${menor?.supplierCode} (R$ ${money(menor?.unitPrice ?? 0)})?`}
                placeholder="prazo, qualidade, condição de pagamento, exclusividade…"
                value={motivo} onChange={(e) => onMotivo(e.target.value)} />
              <p className="mt-1 text-xs text-slate-400">
                Obrigatória: a escolha fora do menor preço fica registrada para auditoria.
              </p>
            </div>
          )}
        </>
      )}
    </Card>
  );
}
