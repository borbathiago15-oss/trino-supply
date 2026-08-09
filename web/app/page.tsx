"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/store";

export default function Home() {
  const router = useRouter();
  const token = useAuth((s) => s.token);

  useEffect(() => {
    router.replace(token ? "/dashboard" : "/login");
  }, [token, router]);

  return <main className="grid min-h-screen place-items-center text-slate-400">Carregando…</main>;
}
