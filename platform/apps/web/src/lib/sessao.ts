'use server';

import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import type { RespostaLogin, UsuarioAutenticado } from '@trino/contratos';
import { api, apiOuNulo, BASE_API, COOKIE_CONTEXTO, COOKIE_TOKEN } from './api';

/**
 * Sessão do usuário. O token vive em cookie httpOnly; o contexto ativo
 * (centro de custo escolhido no seletor) vive em cookie comum, porque é
 * preferência de navegação, não credencial.
 *
 * O TENANT nunca vem daqui: ele está dentro do JWT e o backend só aceita o do
 * token (TenantGuard). O seletor de contexto escolhe centro de custo, não tenant.
 */

export interface ContextoAtivo {
  centroCustoId: string | null;
}

export async function entrar(_estadoAnterior: unknown, dados: FormData): Promise<{ erro?: string }> {
  const cnpj = String(dados.get('cnpj') ?? '').replace(/\D/g, '');
  const email = String(dados.get('email') ?? '').trim();
  const senha = String(dados.get('senha') ?? '');

  if (!cnpj || !email || !senha) return { erro: 'Informe CNPJ, e-mail e senha.' };

  const resposta = await fetch(`${BASE_API}/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ cnpj, email, senha }),
    cache: 'no-store',
  });

  if (!resposta.ok) {
    // A API responde sempre AUTH-ERR-001 genérico — não enumeramos contas.
    return { erro: 'Credenciais inválidas.' };
  }

  const login = (await resposta.json()) as RespostaLogin;
  cookies().set(COOKIE_TOKEN, login.tokenAcesso, {
    httpOnly: true,
    sameSite: 'lax',
    secure: process.env.NODE_ENV === 'production',
    path: '/',
    maxAge: 60 * 60 * 8,
  });
  redirect('/compras/requisicoes');
}

export async function sair(): Promise<void> {
  cookies().delete(COOKIE_TOKEN);
  cookies().delete(COOKIE_CONTEXTO);
  redirect('/login');
}

export async function usuarioAtual(): Promise<UsuarioAutenticado | null> {
  const resposta = await apiOuNulo<{ usuario: UsuarioAutenticado; tenantId: string }>('/auth/me');
  return resposta?.usuario ?? null;
}

/** Exige sessão: telas internas chamam isto antes de qualquer leitura. */
export async function exigirUsuario(): Promise<UsuarioAutenticado> {
  const usuario = await usuarioAtual();
  if (!usuario) redirect('/login');
  return usuario;
}

export async function contextoAtivo(): Promise<ContextoAtivo> {
  const bruto = cookies().get(COOKIE_CONTEXTO)?.value;
  if (!bruto) return { centroCustoId: null };
  try {
    return JSON.parse(bruto) as ContextoAtivo;
  } catch {
    return { centroCustoId: null };
  }
}

export async function definirCentroCusto(centroCustoId: string | null): Promise<void> {
  cookies().set(COOKIE_CONTEXTO, JSON.stringify({ centroCustoId }), {
    sameSite: 'lax',
    path: '/',
    maxAge: 60 * 60 * 24 * 30,
  });
}
