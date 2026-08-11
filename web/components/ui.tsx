"use client";

import { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from "react";

export function Card({ title, children, actions }: { title?: string; children: ReactNode; actions?: ReactNode }) {
  return (
    <section className="rounded-xl border border-slate-200 bg-white shadow-sm">
      {(title || actions) && (
        <header className="flex items-center justify-between border-b border-slate-100 px-5 py-3">
          {title && <h2 className="text-sm font-semibold text-slate-700">{title}</h2>}
          {actions}
        </header>
      )}
      <div className="p-5">{children}</div>
    </section>
  );
}

export function Button({
  variant = "primary",
  className = "",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "ghost" | "danger" }) {
  const styles = {
    primary: "bg-brand text-white hover:bg-brand-fg disabled:opacity-50",
    ghost: "border border-slate-300 text-slate-700 hover:bg-slate-50",
    danger: "bg-rose-600 text-white hover:bg-rose-700 disabled:opacity-50",
  }[variant];
  return (
    <button
      className={`inline-flex items-center justify-center rounded-lg px-3 py-1.5 text-sm font-medium transition ${styles} ${className}`}
      {...props}
    />
  );
}

export function Input({ label, className = "", ...props }: InputHTMLAttributes<HTMLInputElement> & { label?: string }) {
  return (
    <label className="block text-sm">
      {label && <span className="mb-1 block font-medium text-slate-600">{label}</span>}
      <input
        className={`w-full rounded-lg border border-slate-300 px-3 py-1.5 text-sm outline-none focus:border-brand focus:ring-1 focus:ring-brand ${className}`}
        {...props}
      />
    </label>
  );
}

export function Select({ label, className = "", children, ...props }: SelectHTMLAttributes<HTMLSelectElement> & { label?: string }) {
  return (
    <label className="block text-sm">
      {label && <span className="mb-1 block font-medium text-slate-600">{label}</span>}
      <select
        className={`w-full rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-sm outline-none focus:border-brand focus:ring-1 focus:ring-brand ${className}`}
        {...props}
      >
        {children}
      </select>
    </label>
  );
}

const STATUS_STYLES: Record<string, string> = {
  Draft: "bg-slate-100 text-slate-600",
  Submitted: "bg-amber-100 text-amber-700",
  ApprovedLevel1: "bg-sky-100 text-sky-700",
  Approved: "bg-emerald-100 text-emerald-700",
  FulfilledFromStock: "bg-teal-100 text-teal-700",
  Rejected: "bg-rose-100 text-rose-700",
  Issued: "bg-emerald-100 text-emerald-700",
  Cancelled: "bg-rose-100 text-rose-700",
  Active: "bg-emerald-100 text-emerald-700",
  Inactive: "bg-slate-100 text-slate-600",
  // Solicitação de almoxarifado (Fluxo A)
  Pendente: "bg-amber-100 text-amber-700",
  Aprovado: "bg-sky-100 text-sky-700",
  Rejeitado: "bg-rose-100 text-rose-700",
  EmSeparacao: "bg-indigo-100 text-indigo-700",
  SolicitadoCompra: "bg-orange-100 text-orange-700",
  EmRota: "bg-violet-100 text-violet-700",
  Entregue: "bg-emerald-100 text-emerald-700",
  Parcial: "bg-yellow-100 text-yellow-700",
  Cancelado: "bg-rose-100 text-rose-700",
};

// Rótulos humanizados (v2): o usuário lê estados em português, não nomes de enum.
const STATUS_LABELS: Record<string, string> = {
  Draft: "Rascunho",
  Submitted: "Aguardando nível 1",
  ApprovedLevel1: "Aguardando nível 2",
  Approved: "Aprovado",
  FulfilledFromStock: "Atendido pelo estoque",
  Rejected: "Reprovado",
  Issued: "Emitida",
  Cancelled: "Cancelada",
  Active: "Ativo",
  Inactive: "Bloqueado",
  EmSeparacao: "Em separação",
  SolicitadoCompra: "Solicitado compra",
  EmRota: "Em rota",
};

export function StatusPill({ status }: { status: string }) {
  const style = STATUS_STYLES[status] ?? "bg-slate-100 text-slate-600";
  return <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${style}`}>{STATUS_LABELS[status] ?? status}</span>;
}

export function Table({ head, children }: { head: string[]; children: ReactNode }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-400">
            {head.map((h) => (
              <th key={h} className="px-3 py-2 font-medium">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">{children}</tbody>
      </table>
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="py-6 text-center text-sm text-slate-400">{children}</p>;
}
