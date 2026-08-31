import { cookies } from 'next/headers';
import type { ErroApi } from '@trino/contratos';

/**
 * Cliente da API da plataforma. Roda no SERVIDOR (Server Components e Server
 * Actions): o token JWT fica num cookie httpOnly e nunca chega ao JavaScript
 * do navegador — é o que impede que um XSS leve a sessão embora.
 */

export const BASE_API = process.env.API_URL ?? 'http://127.0.0.1:3001/api/v1';

export const COOKIE_TOKEN = 'trino_token';
export const COOKIE_CONTEXTO = 'trino_contexto';

export class ErroDaApi extends Error {
  readonly status: number;
  readonly corpo: ErroApi;

  constructor(status: number, corpo: ErroApi) {
    const detalhe = Array.isArray(corpo?.message) ? corpo.message.join(' ') : corpo?.message;
    super(corpo?.mensagem ?? detalhe ?? `Falha na API (${status}).`);
    this.name = 'ErroDaApi';
    this.status = status;
    this.corpo = corpo ?? {};
  }

  /** Código de negócio (REQ-ERR-002, APV-B6…) para a UI decidir o que dizer. */
  get codigo(): string | undefined {
    return this.corpo.codigo;
  }
}

interface OpcoesApi {
  method?: 'GET' | 'POST' | 'PATCH' | 'DELETE';
  body?: unknown;
  /** Segundos de cache; 0 (padrão) sempre busca do servidor. */
  revalidate?: number;
}

export function tokenAtual(): string | null {
  return cookies().get(COOKIE_TOKEN)?.value ?? null;
}

export async function api<T>(caminho: string, opcoes: OpcoesApi = {}): Promise<T> {
  const token = tokenAtual();
  const resposta = await fetch(`${BASE_API}${caminho}`, {
    method: opcoes.method ?? 'GET',
    headers: {
      'content-type': 'application/json',
      ...(token ? { authorization: `Bearer ${token}` } : {}),
    },
    ...(opcoes.body !== undefined ? { body: JSON.stringify(opcoes.body) } : {}),
    cache: opcoes.revalidate ? undefined : 'no-store',
    ...(opcoes.revalidate ? { next: { revalidate: opcoes.revalidate } } : {}),
  });

  const texto = await resposta.text();
  const corpo = texto ? (JSON.parse(texto) as unknown) : null;
  if (!resposta.ok) throw new ErroDaApi(resposta.status, (corpo ?? {}) as ErroApi);
  return corpo as T;
}

/** Versão que devolve null em 401/404, para telas que lidam com ausência. */
export async function apiOuNulo<T>(caminho: string, opcoes: OpcoesApi = {}): Promise<T | null> {
  try {
    return await api<T>(caminho, opcoes);
  } catch (e) {
    if (e instanceof ErroDaApi && [401, 403, 404].includes(e.status)) return null;
    throw e;
  }
}
