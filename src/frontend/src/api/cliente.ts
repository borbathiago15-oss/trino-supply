import { EVENTO_SESSAO_EXPIRADA, sessao } from './sessao';

/** Erro da API no formato `{ error: { code, message } }`, com o status HTTP. */
export class ErroApi extends Error {
  constructor(message: string, public readonly status: number, public readonly code?: string) {
    super(message);
    this.name = 'ErroApi';
  }
}

export interface OpcoesRequisicao {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  signal?: AbortSignal;
}

const MENSAGEM_EXPIROU = 'Sua sessão expirou. Entre novamente.';

/** Uma renovação por vez: várias chamadas em 401 simultâneas dividem a mesma promessa. */
let renovacaoEmCurso: Promise<boolean> | null = null;

export async function renovarSessao(): Promise<boolean> {
  const refreshToken = sessao.refresh;
  if (!refreshToken) return false;
  if (!renovacaoEmCurso) {
    renovacaoEmCurso = (async () => {
      try {
        const res = await fetch('/api/v1/auth/refresh', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken }),
        });
        if (!res.ok) return false;
        const json = await res.json().catch(() => ({}));
        const data = json.data ?? json;
        if (!data?.accessToken) return false;
        sessao.set(data);
        return true;
      } catch {
        return false;
      } finally {
        // quem já estava esperando recebe esta promessa; a próxima chamada renova de novo
        renovacaoEmCurso = null;
      }
    })();
  }
  return renovacaoEmCurso;
}

function encerrarSessao() {
  sessao.clear();
  globalThis.dispatchEvent?.(new Event(EVENTO_SESSAO_EXPIRADA));
}

function cabecalhos(json = true): Record<string, string> {
  const h: Record<string, string> = {};
  if (json) h['Content-Type'] = 'application/json';
  if (sessao.access) h.Authorization = 'Bearer ' + sessao.access;
  return h;
}

/**
 * Chamada JSON à API. Devolve `json.data` (ou o próprio JSON quando não há
 * envelope). Em 401 renova o token uma vez e repete; se não der, encerra a
 * sessão e avisa a aplicação.
 */
export async function api<T = unknown>(caminho: string, opcoes: OpcoesRequisicao = {}, _repetida = false): Promise<T> {
  const res = await fetch(caminho, {
    method: opcoes.method ?? 'GET',
    headers: cabecalhos(),
    body: opcoes.body !== undefined ? JSON.stringify(opcoes.body) : undefined,
    signal: opcoes.signal,
  });

  if (res.status === 401 && !_repetida && sessao.refresh && !caminho.includes('/auth/')) {
    if (await renovarSessao()) return api<T>(caminho, opcoes, true);
    encerrarSessao();
    throw new ErroApi(MENSAGEM_EXPIROU, 401);
  }

  const json = await res.json().catch(() => ({}));
  if (!res.ok) {
    const padrao = res.status === 401 ? MENSAGEM_EXPIROU
      : res.status >= 500 ? 'O servidor não respondeu a esta operação. Tente novamente em instantes.'
      : 'Falha na operação.';
    throw new ErroApi(json?.error?.message || padrao, res.status, json?.error?.code);
  }
  return (json.data ?? json) as T;
}

/** Baixa um arquivo autenticado (PDF da OC, anexos) como Blob. */
export async function baixar(caminho: string, mensagemErro = 'Falha ao baixar o arquivo.'): Promise<Blob> {
  let res = await fetch(caminho, { headers: cabecalhos(false) });
  if (res.status === 401 && sessao.refresh && (await renovarSessao()))
    res = await fetch(caminho, { headers: cabecalhos(false) });
  if (!res.ok) throw new ErroApi(mensagemErro, res.status);
  return res.blob();
}

/** Upload multipart simples com o token da sessão (campo `file`, como no legado). */
export async function enviarArquivo(caminho: string, arquivo: File): Promise<void> {
  const fd = new FormData();
  fd.append('file', arquivo);
  let res = await fetch(caminho, { method: 'POST', headers: cabecalhos(false), body: fd });
  if (res.status === 401 && sessao.refresh && (await renovarSessao()))
    res = await fetch(caminho, { method: 'POST', headers: cabecalhos(false), body: fd });
  if (!res.ok) throw new ErroApi('Não consegui enviar ' + arquivo.name + '.', res.status);
}

/** Abre um Blob numa nova aba (mesmo comportamento do legado para PDF e anexos). */
export function abrirBlob(blob: Blob) {
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
