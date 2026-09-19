"use client";

import { useQuery } from "@tanstack/react-query";
import { api, StockCheckRow } from "@/lib/api";

/**
 * Saldo do Almox para uma lista de itens (MMS-004 — evitar compra desnecessária).
 * Consulta em Compras, não em Materiais: quem pede e quem aprova precisa ver o saldo e nem sempre
 * tem permissão de estoque. Uma chamada só para todos os códigos da tela.
 */
export function useSaldoAlmox(codes: (string | undefined | null)[]) {
  const limpos = [...new Set(
    codes.map((c) => (c ?? "").trim().toUpperCase()).filter((c) => c.length >= 2),
  )].sort();

  const q = useQuery({
    queryKey: ["stock-check", limpos.join(",")],
    enabled: limpos.length > 0,
    staleTime: 30_000,
    queryFn: () => api<StockCheckRow[]>(`/purchases/stock-check?items=${encodeURIComponent(limpos.join(","))}`),
  });

  const porCodigo = new Map((q.data ?? []).map((r) => [r.itemCode, r]));
  return (code?: string | null) => porCodigo.get((code ?? "").trim().toUpperCase());
}

/**
 * Farol do saldo local. Verde = dá para atender pelo armazém (considere solicitar em vez de comprar);
 * âmbar = tem, mas não cobre tudo; branco = sem saldo ou item que nem existe no Almox.
 */
export function SaldoBadge({ saldo, quantidade }: { saldo?: StockCheckRow; quantidade?: number }) {
  if (!saldo) return null;

  if (!saldo.inCatalog) {
    return (
      <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-500"
        title="Item não cadastrado no Almox — segue como compra externa.">
        ⚪ fora do Almox
      </span>
    );
  }
  if (saldo.balance <= 0) {
    return (
      <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-500">
        ⚪ sem saldo local
      </span>
    );
  }

  const cobre = quantidade === undefined || saldo.balance >= quantidade;
  return cobre ? (
    <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700"
      title="Há saldo no armazém: considere solicitar ao almoxarifado em vez de comprar.">
      🟢 {Number(saldo.balance)} no Almox
    </span>
  ) : (
    <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700"
      title="Há saldo, mas não cobre a quantidade pedida.">
      🟡 {Number(saldo.balance)} no Almox (parcial)
    </span>
  );
}
