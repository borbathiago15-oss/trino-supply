"use client";

import { FormEvent, Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, ApiError } from "@/lib/api";
import { Button, Input } from "@/components/ui";

function CriarSenhaForm() {
  const router = useRouter();
  const params = useSearchParams();
  const company = params.get("company") ?? "";
  const token = params.get("token") ?? "";

  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [loading, setLoading] = useState(false);

  const linkInvalido = !company || !token;

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (password.length < 8) {
      setError("A senha deve ter ao menos 8 caracteres.");
      return;
    }
    if (password !== confirm) {
      setError("As senhas não conferem.");
      return;
    }
    setLoading(true);
    try {
      await api("/auth/password/reset", {
        method: "POST",
        body: JSON.stringify({ companyId: company, token, newPassword: password }),
      });
      setDone(true);
      setTimeout(() => router.replace("/login"), 2500);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Falha ao definir a senha.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="grid min-h-screen place-items-center bg-slate-100 p-4">
      <form onSubmit={onSubmit} className="w-full max-w-sm space-y-4 rounded-2xl border border-slate-200 bg-white p-8 shadow-sm">
        <div className="space-y-1 text-center">
          <div className="mx-auto mb-2 flex h-10 w-10 items-center justify-center rounded-lg bg-brand text-lg text-white">T</div>
          <h1 className="text-lg font-semibold text-slate-800">Definir senha</h1>
          <p className="text-sm text-slate-500">Escolha a senha de acesso ao Trino Supply</p>
        </div>
        {linkInvalido ? (
          <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">
            Link inválido. Solicite um novo em &quot;Esqueci minha senha&quot; na tela de login.
          </p>
        ) : done ? (
          <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700">
            Senha definida com sucesso! Redirecionando para o login…
          </p>
        ) : (
          <>
            <Input label="Nova senha" type="password" autoComplete="new-password"
              value={password} onChange={(e) => setPassword(e.target.value)} />
            <Input label="Confirmar senha" type="password" autoComplete="new-password"
              value={confirm} onChange={(e) => setConfirm(e.target.value)} />
            {error && <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}
            <Button type="submit" disabled={loading} className="w-full">
              {loading ? "Salvando…" : "Definir senha"}
            </Button>
          </>
        )}
      </form>
    </main>
  );
}

export default function CriarSenhaPage() {
  // useSearchParams exige Suspense no App Router.
  return (
    <Suspense>
      <CriarSenhaForm />
    </Suspense>
  );
}
