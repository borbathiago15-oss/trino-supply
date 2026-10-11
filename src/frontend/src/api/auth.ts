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
  /** A verificação em duas etapas está ligada nesta conta. */
  mfaEnabled?: boolean;
  /** O papel exige a segunda etapa (diretor e administrador). */
  mfaRequired?: boolean;
  /** O papel exige e ainda não há autenticador: a sessão só abre o cadastro dele (IAM-ERR-024). */
  mustSetupMfa?: boolean;
}

interface RespostaLogin extends Tokens { user: Usuario }
/** A senha conferiu e falta o código: o token só serve para a segunda etapa, por cinco minutos. */
interface RespostaMfaPendente { mfaRequired: true; mfaToken: string; expiresIn: number }

/** O que o login devolve: a pessoa, ou a vez do código do autenticador. */
export type Entrada = { usuario: Usuario; mfaToken?: undefined } | { usuario?: undefined; mfaToken: string };

/** `lembrar` guarda a sessão no aparelho em vez de na aba: é o que deixa o app de bolso aberto entre usos. */
export async function entrar(email: string, password: string, lembrar = false): Promise<Entrada> {
  const data = await api<RespostaLogin | RespostaMfaPendente>('/api/v1/auth/login', { method: 'POST', body: { email, password } });
  if ('mfaRequired' in data && data.mfaRequired) return { mfaToken: data.mfaToken };
  sessao.set(data as RespostaLogin, lembrar);
  return { usuario: (data as RespostaLogin).user };
}

/** A segunda etapa do login: código do aplicativo ou código de recuperação. */
export async function confirmarSegundaEtapa(mfaToken: string, code: string, lembrar = false): Promise<Usuario & { usedRecoveryCode: boolean; recoveryCodesLeft: number }> {
  const data = await api<{ sessao: RespostaLogin; usedRecoveryCode: boolean; recoveryCodesLeft: number }>(
    '/api/v1/auth/mfa/verify', { method: 'POST', body: { mfaToken, code } });
  sessao.set(data.sessao, lembrar);
  return { ...data.sessao.user, usedRecoveryCode: data.usedRecoveryCode, recoveryCodesLeft: data.recoveryCodesLeft };
}

export interface EstadoDoMfa { enabled: boolean; required: boolean; enabledAt: string | null; recoveryCodesLeft: number }
export const estadoDoMfa = () => api<EstadoDoMfa>('/api/v1/auth/mfa/');

/** Começa o cadastro: o segredo vem uma vez, para o QR e para digitar no aplicativo. */
export const iniciarMfa = () => api<{ secret: string; otpauthUrl: string }>('/api/v1/auth/mfa/setup', { method: 'POST' });

/**
 * Confirma o cadastro com o primeiro código do aplicativo. O servidor devolve uma sessão nova —
 * a antiga carregava a marca de "presa no cadastro" — e os códigos de recuperação, uma vez só.
 */
export async function ativarMfa(code: string): Promise<{ usuario: Usuario; recoveryCodes: string[] }> {
  const data = await api<{ sessao: RespostaLogin; recoveryCodes: string[] }>('/api/v1/auth/mfa/enable', { method: 'POST', body: { code } });
  sessao.set(data.sessao);
  return { usuario: data.sessao.user, recoveryCodes: data.recoveryCodes };
}

export const desativarMfa = (code: string) => api<unknown>('/api/v1/auth/mfa/disable', { method: 'POST', body: { code } });

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
