/**
 * Tokens da sessão, guardados por aba. Os nomes vêm do sistema clássico, que
 * conviveu com este durante a migração — mantidos para não deslogar quem já
 * estava com a sessão aberta quando o clássico saiu.
 */
const ACESSO = 'ts.access';
const RENOVACAO = 'ts.refresh';

export interface Tokens { accessToken: string; refreshToken: string }

function armazem(): Storage | null {
  try { return globalThis.sessionStorage ?? null; } catch { return null; }
}

export const sessao = {
  get access(): string | null { return armazem()?.getItem(ACESSO) ?? null; },
  get refresh(): string | null { return armazem()?.getItem(RENOVACAO) ?? null; },
  set(t: Tokens) { armazem()?.setItem(ACESSO, t.accessToken); armazem()?.setItem(RENOVACAO, t.refreshToken); },
  clear() { armazem()?.removeItem(ACESSO); armazem()?.removeItem(RENOVACAO); },
  get ativa(): boolean { return !!this.access || !!this.refresh; },
};

/** Disparado quando a renovação falha: a casca da aplicação volta para o login. */
export const EVENTO_SESSAO_EXPIRADA = 'trino:sessao-expirada';
