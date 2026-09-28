/**
 * Tokens da sessão. Os nomes vêm do sistema clássico, que conviveu com este durante a
 * migração — mantidos para não deslogar quem já estava com a sessão aberta quando o
 * clássico saiu.
 *
 * A sessão mora em um de dois lugares, e quem escolhe é a pessoa, na entrada:
 * - **na aba** (`sessionStorage`): some quando a aba ou o app fecham — é o padrão no
 *   computador, que pode ser compartilhado;
 * - **no aparelho** (`localStorage`), com "Manter conectado": o app de bolso é fechado a
 *   cada uso, e pedir a senha toda vez tornava o app inútil. Vale enquanto o refresh token
 *   valer (ele se renova a cada uso).
 * Os dois guardam a mesma coisa e correm o mesmo risco (XSS lê os dois; a CSP sem
 * `unsafe-inline` é o que segura). O cookie HttpOnly continua sendo a decisão pendente.
 */
const ACESSO = 'ts.access';
const RENOVACAO = 'ts.refresh';
/** A escolha de "manter conectado" neste aparelho — e o padrão da caixa no próximo login. */
const LEMBRAR = 'ts.lembrar';

export interface Tokens { accessToken: string; refreshToken: string }

function porAba(): Storage | null {
  try { return globalThis.sessionStorage ?? null; } catch { return null; }
}
function noAparelho(): Storage | null {
  try { return globalThis.localStorage ?? null; } catch { return null; }
}
/** Onde a sessão mora: no aparelho quando a pessoa pediu para ficar conectada, senão na aba. */
function armazem(): Storage | null {
  return noAparelho()?.getItem(LEMBRAR) === '1' ? noAparelho() : porAba();
}
const ler = (chave: string) => noAparelho()?.getItem(chave) ?? porAba()?.getItem(chave) ?? null;

export const sessao = {
  get access(): string | null { return ler(ACESSO); },
  get refresh(): string | null { return ler(RENOVACAO); },
  /**
   * Grava os tokens onde a sessão mora. `lembrar` só vem na entrada e decide o lugar; a
   * renovação e a troca de senha não o passam e mantêm o lugar já escolhido.
   */
  set(t: Tokens, lembrar?: boolean) {
    if (lembrar !== undefined) {
      noAparelho()?.setItem(LEMBRAR, lembrar ? '1' : '0');
      // a sessão não pode ficar nos dois lugares: a antiga sairia de um e sobreviveria no outro
      for (const s of [porAba(), noAparelho()]) { s?.removeItem(ACESSO); s?.removeItem(RENOVACAO); }
    }
    armazem()?.setItem(ACESSO, t.accessToken);
    armazem()?.setItem(RENOVACAO, t.refreshToken);
  },
  clear() {
    for (const s of [porAba(), noAparelho()]) { s?.removeItem(ACESSO); s?.removeItem(RENOVACAO); }
  },
  get ativa(): boolean { return !!this.access || !!this.refresh; },
  /** A pessoa pediu, da última vez, para ficar conectada neste aparelho. */
  get lembrada(): boolean { return noAparelho()?.getItem(LEMBRAR) === '1'; },
};

/** Disparado quando a renovação falha: a casca da aplicação volta para o login. */
export const EVENTO_SESSAO_EXPIRADA = 'trino:sessao-expirada';
