"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, QuotationView } from "@/lib/api";
import { Button, Empty, Input, Select, Table } from "@/components/ui";

/**
 * Lançamento da proposta que o fornecedor mandou (e-mail, planilha, telefone). Enquanto não existe
 * o portal do fornecedor, é o comprador quem digita — o contrato do endpoint já é o mesmo que o
 * portal vai usar, então a tela some sem mexer no backend.
 * <p>Recotar é normal: reenviar substitui a proposta anterior daquele fornecedor por inteiro.</p>
 */
export function LancarProposta({ id, onErr, onOk }: {
  id: string; onErr: (e: unknown) => void; onOk: (m: string) => void;
}) {
  const qc = useQueryClient();
  const [supplierCode, setSupplierCode] = useState("");
  const [pagamento, setPagamento] = useState("");
  const [frete, setFrete] = useState("");
  const [validade, setValidade] = useState("");
  const [precos, setPrecos] = useState<Record<string, string>>({});
  const [prazos, setPrazos] = useState<Record<string, string>>({});

  const q = useQuery({ queryKey: ["quotation", id], queryFn: () => api<QuotationView>(`/purchases/quotations/${id}`) });
  const cot = q.data;

  const num = (s?: string) => Number((s ?? "").replace(",", ".")) || 0;
  const cotados = (cot?.lines ?? []).filter((l) => num(precos[l.id]) > 0);

  const enviar = useMutation({
    mutationFn: () => api(`/purchases/quotations/${id}/proposals`, {
      method: "POST",
      body: JSON.stringify({
        supplierCode,
        paymentTerms: pagamento || null,
        freightTerms: frete || null,
        validUntil: validade || null,
        bids: cotados.map((l) => ({
          lineId: l.id,
          unitPrice: num(precos[l.id]),
          deliveryDays: prazos[l.id] ? Number(prazos[l.id]) : null,
        })),
      }),
    }),
    onSuccess: () => {
      setPrecos({}); setPrazos({}); setSupplierCode("");
      qc.invalidateQueries({ queryKey: ["quotation", id] });
      qc.invalidateQueries({ queryKey: ["quotations"] });
      onOk("Proposta registrada.");
    },
    onError: onErr,
  });

  if (!cot) return <p className="py-6 text-center text-sm text-slate-400">Carregando a cotação…</p>;
  if (cot.participants.length === 0) return <Empty>Esta cotação não tem fornecedores convidados.</Empty>;

  const jaRespondeu = cot.participants.find((p) => p.supplierCode === supplierCode)?.hasResponded;

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-4">
        <Select label="Fornecedor" value={supplierCode} onChange={(e) => setSupplierCode(e.target.value)}>
          <option value="">Selecione…</option>
          {cot.participants.map((p) => (
            <option key={p.supplierId} value={p.supplierCode}>
              {p.supplierCode} — {p.supplierName}{p.hasResponded ? " (já respondeu)" : ""}
            </option>
          ))}
        </Select>
        <Input label="Cond. pagamento" value={pagamento} placeholder="30 dias"
          onChange={(e) => setPagamento(e.target.value)} />
        <Input label="Frete" value={frete} placeholder="CIF" onChange={(e) => setFrete(e.target.value)} />
        <Input label="Validade da proposta" type="date" value={validade}
          onChange={(e) => setValidade(e.target.value)} />
      </div>

      {jaRespondeu && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
          Este fornecedor já respondeu. Enviar de novo <strong>substitui</strong> a proposta anterior
          dele por inteiro — é a recotação.
        </p>
      )}
      {cot.isClosed && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
          O prazo de resposta venceu. A proposta ainda entra, mas fica marcada como fora do prazo.
        </p>
      )}

      <Table head={["Item", "Qtd", "Preço unitário (R$)", "Prazo (dias)", "Total"]}>
        {cot.lines.map((l) => (
          <tr key={l.id}>
            <td className="px-3 py-2 font-mono text-xs">{l.itemCode}</td>
            <td className="px-3 py-2">{Number(l.quantity)} {l.unit}</td>
            <td className="px-3 py-2">
              <input inputMode="decimal" value={precos[l.id] ?? ""} placeholder="não cota"
                onChange={(e) => setPrecos({ ...precos, [l.id]: e.target.value })}
                className="w-28 rounded-lg border border-slate-300 px-2 py-1 text-sm" />
            </td>
            <td className="px-3 py-2">
              <input inputMode="numeric" value={prazos[l.id] ?? ""} placeholder="—"
                onChange={(e) => setPrazos({ ...prazos, [l.id]: e.target.value })}
                className="w-20 rounded-lg border border-slate-300 px-2 py-1 text-sm" />
            </td>
            <td className="px-3 py-2 text-right tabular-nums text-slate-500">
              {num(precos[l.id]) > 0
                ? (num(precos[l.id]) * Number(l.quantity)).toLocaleString("pt-BR", { minimumFractionDigits: 2 })
                : "—"}
            </td>
          </tr>
        ))}
      </Table>

      <div className="flex items-center gap-2">
        <Button disabled={!supplierCode || cotados.length === 0 || enviar.isPending} onClick={() => enviar.mutate()}>
          Registrar proposta
        </Button>
        <span className="text-xs text-slate-400">
          Item sem preço fica de fora — é o fornecedor declinando aquele item.
        </span>
      </div>
    </div>
  );
}
