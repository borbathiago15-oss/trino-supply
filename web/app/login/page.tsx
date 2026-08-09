"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import { z } from "zod";
import { api, ApiError, LoginResponse } from "@/lib/api";
import { useAuth } from "@/lib/store";
import { Button, Input } from "@/components/ui";

const schema = z.object({
  companyId: z.string().uuid("Company ID deve ser um UUID válido."),
  email: z.string().email("E-mail inválido."),
  password: z.string().min(1, "Informe a senha."),
});

export default function LoginPage() {
  const router = useRouter();
  const setAuth = useAuth((s) => s.setAuth);
  const [form, setForm] = useState({ companyId: "", email: "", password: "" });
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const parsed = schema.safeParse(form);
    if (!parsed.success) {
      setError(parsed.error.issues[0].message);
      return;
    }
    setLoading(true);
    try {
      const res = await api<LoginResponse>("/auth/login", {
        method: "POST",
        body: JSON.stringify(parsed.data),
      });
      setAuth({
        token: res.accessToken,
        refreshToken: res.refreshToken,
        companyId: parsed.data.companyId,
        email: parsed.data.email,
      });
      router.replace("/dashboard");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Falha no login.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="grid min-h-screen place-items-center bg-slate-100 p-4">
      <form onSubmit={onSubmit} className="w-full max-w-sm space-y-4 rounded-2xl border border-slate-200 bg-white p-8 shadow-sm">
        <div className="space-y-1 text-center">
          <div className="mx-auto mb-2 flex h-10 w-10 items-center justify-center rounded-lg bg-brand text-lg text-white">T</div>
          <h1 className="text-lg font-semibold text-slate-800">Trino Supply</h1>
          <p className="text-sm text-slate-500">Acesse com sua empresa e credenciais</p>
        </div>
        <Input label="Empresa (Company ID)" placeholder="00000000-0000-0000-0000-000000000000"
          value={form.companyId} onChange={(e) => setForm({ ...form, companyId: e.target.value })} />
        <Input label="E-mail" type="email" autoComplete="username"
          value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
        <Input label="Senha" type="password" autoComplete="current-password"
          value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
        {error && <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}
        <Button type="submit" disabled={loading} className="w-full">
          {loading ? "Entrando…" : "Entrar"}
        </Button>
      </form>
    </main>
  );
}
