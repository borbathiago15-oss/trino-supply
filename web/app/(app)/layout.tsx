"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@/lib/store";
import { Toaster } from "@/components/Toaster";
import { Perm, useHas } from "@/lib/me";

const NAV = [
  { href: "/dashboard", label: "Painel", perm: null },
  { href: "/materiais", label: "Materiais", perm: Perm.MaterialsRead },
  { href: "/reposicao", label: "Reposição", perm: Perm.MaterialsRead },
  { href: "/compras", label: "Compras", perm: Perm.PurchasesRead },
  { href: "/cadastros", label: "Cadastros", perm: Perm.PurchasesRead },
];

export default function AppLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { token, email, logout } = useAuth();
  const has = useHas();
  const qc = useQueryClient();

  useEffect(() => {
    if (!token) router.replace("/login");
  }, [token, router]);

  if (!token) return null;

  const nav = NAV.filter((n) => n.perm === null || has(n.perm));

  return (
    <div className="min-h-screen">
      <div className="mx-auto flex max-w-6xl gap-6 p-4 md:p-6">
        <aside className="hidden w-52 shrink-0 md:block">
          <div className="mb-6 flex items-center gap-2 px-2">
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-brand text-white">T</div>
            <span className="font-semibold text-slate-800">Trino Supply</span>
          </div>
          <nav className="space-y-1">
            {nav.map((n) => {
              const active = pathname === n.href;
              return (
                <Link key={n.href} href={n.href}
                  className={`block rounded-lg px-3 py-2 text-sm font-medium transition ${
                    active ? "bg-brand-soft text-brand-fg" : "text-slate-600 hover:bg-slate-100"
                  }`}>
                  {n.label}
                </Link>
              );
            })}
          </nav>
        </aside>

        <div className="min-w-0 flex-1">
          <header className="mb-6 flex items-center justify-between">
            <nav className="flex gap-2 md:hidden">
              {nav.map((n) => (
                <Link key={n.href} href={n.href} className="rounded-lg px-2 py-1 text-sm text-slate-600 hover:bg-slate-100">
                  {n.label}
                </Link>
              ))}
            </nav>
            <div className="ml-auto flex items-center gap-3 text-sm text-slate-500">
              <span className="hidden sm:inline">{email}</span>
              <button onClick={() => { qc.clear(); logout(); router.replace("/login"); }}
                className="rounded-lg border border-slate-300 px-3 py-1 font-medium text-slate-700 hover:bg-slate-50">
                Sair
              </button>
            </div>
          </header>
          <main className="space-y-6">{children}</main>
        </div>
      </div>
      <Toaster />
    </div>
  );
}
