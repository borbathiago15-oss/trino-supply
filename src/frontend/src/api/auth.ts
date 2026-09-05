import { api } from './cliente';
import { sessao, type Tokens } from './sessao';
import type { Papel, Modulo } from '@/dominio/papeis';

export interface CentroCustoVinculo { id: string; code?: string; name?: string }

export interface Usuario {
  id: string;
  email: string;
  name: string;
  role: Papel;
  modules: Modulo[];
  costCenters?: CentroCustoVinculo[];
  /** Senha ainda é a de cadastro: a sessão só abre a tela de troca (SEC-004). */
  mustChangePassword?: boolean;
}

interface RespostaLogin extends Tokens { user: Usuario }

export async function entrar(email: string, password: string): Promise<Usuario> {
  const data = await api<RespostaLogin>('/api/v1/auth/login', { method: 'POST', body: { email, password } });
  sessao.set(data);
  return data.user;
}

export const quemSou = () => api<Usuario>('/api/v1/auth/me');

/**
 * Troca a senha do próprio usuário. O servidor derruba as sessões antigas e
 * devolve tokens novos — já sem a marca de provisória — que substituem os desta
 * aba, para quem trocou seguir sem precisar entrar de novo.
 */
export async function trocarSenha(senhaAtual: string, senhaNova: string): Promise<Usuario> {
  const data = await api<RespostaLogin>('/api/v1/auth/change-password', {
    method: 'POST',
    body: { currentPassword: senhaAtual, newPassword: senhaNova },
  });
  sessao.set(data);
  return data.user;
}

export async function sair(): Promise<void> {
  try {
    await api('/api/v1/auth/logout', { method: 'POST', body: { refreshToken: sessao.refresh } });
  } catch {
    // o logout local vale mesmo se o servidor não responder
  } finally {
    sessao.clear();
  }
}
