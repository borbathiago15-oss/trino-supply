"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";
import { useAuth } from "@/lib/store";

const NAV = [
  { href: "/dashboard", label: "Painel" },
  { href: "/materiais", label: "Materiais" },
  { href: "/reposicao", label: "Reposição" },
  { href: "/compras", label: "Compras" },
];

export default function AppLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { token, email, logout } = useAuth();

  useEffect(() => {
    if (!token) router.replace("/login");
  }, [token, router]);

  if (!token) return null;

  return (
    <div className="min-h-screen">
      <div className="mx-auto flex max-w-6xl gap-6 p-4 md:p-6">
        <aside className="hidden w-52 shrink-0 md:block">
          <div className="mb-6 flex items-center gap-2 px-2">
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-brand text-white">T</div>
            <span className="font-semibold text-slate-800">Trino Supply</span>
          </div>
          <nav className="space-y-1">
            {NAV.map((n) => {
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
              {NAV.map((n) => (
                <Link key={n.href} href={n.href} className="rounded-lg px-2 py-1 text-sm text-slate-600 hover:bg-slate-100">
                  {n.label}
                </Link>
              ))}
            </nav>
            <div className="ml-auto flex items-center gap-3 text-sm text-slate-500">
              <span className="hidden sm:inline">{email}</span>
              <button onClick={() => { logout(); router.replace("/login"); }}
                className="rounded-lg border border-slate-300 px-3 py-1 font-medium text-slate-700 hover:bg-slate-50">
                Sair
              </button>
            </div>
          </header>
          <main className="space-y-6">{children}</main>
        </div>
      </div>
    </div>
  );
}
