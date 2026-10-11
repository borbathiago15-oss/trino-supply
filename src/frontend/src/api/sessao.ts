/**
 * A sessão no navegador. O nome do access token vem do sistema clássico, que conviveu com este
 * durante a migração — mantido para não deslogar quem já estava com a sessão aberta.
 *
 * O **refresh token não mora aqui**: ele vai num cookie `HttpOnly` que o JavaScript não lê
 * (`ts_refresh`, só para `/api/v1/auth`), gravado e renovado pelo servidor. No `sessionStorage`
 * ele ficava ao alcance de qualquer script da página, e um XSS — barrado pela CSP, que era a
 * única barreira — levava uma sessão de sete dias. O que fica aqui é o access token de quinze
 * minutos, que é o teto do que um script alcança.
 *
 * "Manter conectado neste aparelho" é a duração do cookie (persistente pelos dias do refresh
 * token, ou de sessão, que some quando o navegador fecha) e, aqui, onde o access token fica:
 * - **na aba** (`sessionStorage`), o padrão no computador, que pode ser compartilhado;
 * - **no aparelho** (`localStorage`), para o app de bolso, que é fechado a cada uso.
 * Sem access token guardado a casca tenta a renovação pelo cookie: é assim que a aba nova e o
 * app reaberto voltam logados sem o token passar pelo script.
 */
const ACESSO = 'ts.access';
/** O refresh token antigo, de antes do cookie: só se apaga, nunca se lê. */
const RENOVACAO_ANTIGA = 'ts.refresh';
/** A escolha de "manter conectado" neste aparelho — e o padrão da caixa no próximo login. */
const LEMBRAR = 'ts.lembrar';

export interface Tokens { accessToken: string; refreshToken?: string | null }

function porAba(): Storage | null {
  try { return globalThis.sessionStorage ?? null; } catch { return null; }
}
function noAparelho(): Storage | null {
  try { return globalThis.localStorage ?? null; } catch { return null; }
}
/** Onde o access token mora: no aparelho quando a pessoa pediu para ficar conectada, senão na aba. */
function armazem(): Storage | null {
  return noAparelho()?.getItem(LEMBRAR) === '1' ? noAparelho() : porAba();
}
const ler = (chave: string) => noAparelho()?.getItem(chave) ?? porAba()?.getItem(chave) ?? null;
const apagarTudo = () => {
  for (const s of [porAba(), noAparelho()]) { s?.removeItem(ACESSO); s?.removeItem(RENOVACAO_ANTIGA); }
};

export const sessao = {
  get access(): string | null { return ler(ACESSO); },
  /**
   * Grava o access token onde a sessão mora. `lembrar` só vem na entrada e decide o lugar; a
   * renovação e a troca de senha não o passam e mantêm o lugar já escolhido. O refresh token
   * que vier no corpo é ignorado: o servidor já o pôs no cookie.
   */
  set(t: Tokens, lembrar?: boolean) {
    if (lembrar !== undefined) {
      noAparelho()?.setItem(LEMBRAR, lembrar ? '1' : '0');
      // a sessão não pode ficar nos dois lugares: a antiga sairia de um e sobreviveria no outro
      apagarTudo();
    }
    armazem()?.setItem(ACESSO, t.accessToken);
  },
  clear() { apagarTudo(); },
  /** Há access token guardado. Sem ele ainda pode haver o cookie: a casca tenta renovar antes de desistir. */
  get ativa(): boolean { return !!this.access; },
  /** A pessoa pediu, da última vez, para ficar conectada neste aparelho. */
  get lembrada(): boolean { return noAparelho()?.getItem(LEMBRAR) === '1'; },
};

/** Disparado quando a renovação falha: a casca da aplicação volta para o login. */
export const EVENTO_SESSAO_EXPIRADA = 'trino:sessao-expirada';
