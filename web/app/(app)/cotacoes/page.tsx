"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, QuotationSummaryView } from "@/lib/api";
import { Button, Card, Empty, StatusPill, Table } from "@/components/ui";
import { useToast } from "@/lib/toast";
import { Perm, useHas } from "@/lib/me";
import { MapaEqualizacao } from "@/components/cotacao";
import { LancarProposta } from "@/components/proposta";

const dt = (s?: string | null) => (s ? new Date(s).toLocaleDateString("pt-BR") : "—");

/**
 * Concorrências abertas e encerradas. A cotação nasce na tela de Pedido (requisição aprovada) e
 * termina aqui: lançar as propostas, comparar no mapa de equalização e adjudicar item a item.
 */
export default function CotacoesPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const has = useHas();
  const [aberta, setAberta] = useState<string | null>(null);
  const [propondo, setPropondo] = useState<string | null>(null);

  const cotacoes = useQuery({
    queryKey: ["quotations"],
    queryFn: () => api<QuotationSummaryView[]>("/purchases/quotations"),
  });

  const onErr = (e: unknown) =>
    toast.push("error", e instanceof ApiError ? e.message : "Não foi possível concluir a operação.");
  const onOk = (m: string) => toast.push("success", m);

  const cancelar = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api(`/purchases/quotations/${id}/cancel`, { method: "POST", body: JSON.stringify({ reason }) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["quotations"] }); onOk("Cotação cancelada."); },
    onError: onErr,
  });

  const podeOperar = has(Perm.PurchasesOrder);
  const lista = cotacoes.data ?? [];

  return (
    <>
      <Card title="Cotações">
        <p className="mb-3 text-xs text-slate-500">
          A concorrência é aberta na tela de <strong>Pedido</strong>, a partir de uma requisição aprovada.
          Aqui entram as propostas e sai a decisão: o mapa compara preço, prazo e desempenho de entrega,
          e a escolha fora do menor preço exige justificativa registrada.
        </p>

        {lista.length > 0 ? (
          <Table head={["Nº", "Situação", "Itens", "Convidados", "Responderam", "Adjudicados", "Prazo", ""]}>
            {lista.map((q) => (
              <tr key={q.id}>
                <td className="px-3 py-2 font-mono text-xs">{q.number}</td>
                <td className="px-3 py-2"><StatusPill status={q.status} /></td>
                <td className="px-3 py-2 tabular-nums">{q.linesCount}</td>
                <td className="px-3 py-2 tabular-nums">{q.invitedCount}</td>
                <td className="px-3 py-2 tabular-nums">
                  {q.respondedCount}
                  {q.respondedCount < q.invitedCount && q.isClosed && (
                    <span className="ml-1 text-xs text-amber-600">prazo vencido</span>
                  )}
                </td>
                <td className="px-3 py-2 tabular-nums">{q.awardedCount}/{q.linesCount}</td>
                <td className="px-3 py-2 text-slate-500">{dt(q.closesAt)}</td>
                <td className="px-3 py-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <Button variant="ghost" onClick={() => { setAberta(aberta === q.id ? null : q.id); setPropondo(null); }}>
                      {aberta === q.id ? "Ocultar mapa" : "Mapa de equalização"}
                    </Button>
                    {podeOperar && q.status === "Open" && (
                      <Button variant="ghost" onClick={() => { setPropondo(propondo === q.id ? null : q.id); setAberta(null); }}>
                        {propondo === q.id ? "Fechar" : "Lançar proposta"}
                      </Button>
                    )}
                    {podeOperar && q.status !== "Cancelled" && q.awardedCount === 0 && (
                      <Button variant="danger" onClick={() => {
                        const reason = window.prompt("Motivo do cancelamento da cotação:");
                        if (reason?.trim()) cancelar.mutate({ id: q.id, reason: reason.trim() });
                      }}>Cancelar</Button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </Table>
        ) : (
          <Empty>Nenhuma cotação aberta. Comece por uma requisição aprovada na tela de Pedido.</Empty>
        )}
      </Card>

      {propondo && (
        <Card title="Lançar proposta recebida">
          <LancarProposta id={propondo} onErr={onErr} onOk={(m) => { onOk(m); }} />
        </Card>
      )}

      {aberta && (
        <Card title="Mapa de equalização">
          <MapaEqualizacao id={aberta} onErr={onErr} onOk={onOk} />
        </Card>
      )}
    </>
  );
}
