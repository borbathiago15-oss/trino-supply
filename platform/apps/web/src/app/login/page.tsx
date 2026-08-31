'use client';

import { useFormState, useFormStatus } from 'react-dom';
import { entrar } from '@/lib/sessao';

function BotaoEntrar() {
  const { pending } = useFormStatus();
  return (
    <button type="submit" className="botao-primario w-full" disabled={pending}>
      {pending ? 'Entrando…' : 'Entrar'}
    </button>
  );
}

export default function PaginaLogin() {
  const [estado, acao] = useFormState(entrar, {});

  return (
    <main className="flex min-h-screen items-center justify-center bg-trino-900 px-4">
      <div className="card w-full max-w-md p-8">
        <h1 className="text-2xl font-semibold text-trino-900">Trino Platform</h1>
        <p className="mt-1 text-sm text-slate-500">Entre com as credenciais da sua empresa.</p>

        <form action={acao} className="mt-6 space-y-4">
          <div>
            <label className="rotulo" htmlFor="cnpj">CNPJ da empresa</label>
            <input id="cnpj" name="cnpj" className="campo mt-1" placeholder="00.000.000/0000-00" required />
          </div>
          <div>
            <label className="rotulo" htmlFor="email">E-mail</label>
            <input id="email" name="email" type="email" className="campo mt-1" required />
          </div>
          <div>
            <label className="rotulo" htmlFor="senha">Senha</label>
            <input id="senha" name="senha" type="password" className="campo mt-1" required />
          </div>

          {estado?.erro ? (
            <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{estado.erro}</p>
          ) : null}

          <BotaoEntrar />
        </form>
      </div>
    </main>
  );
}
