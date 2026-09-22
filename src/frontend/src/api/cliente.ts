import { EVENTO_SESSAO_EXPIRADA, sessao } from './sessao';

/** Erro da API no formato `{ error: { code, message } }`, com o status HTTP. */
export class ErroApi extends Error {
  /**
   * `corpo` é o JSON que veio com a recusa. Quase toda rota só manda mensagem e código, mas
   * algumas mandam o que a tela precisa para o passo seguinte — o encerramento do ciclo de
   * melhoria devolve a lista de ações em aberto, e sem ela a tela pediria a confirmação sem
   * dizer de quê.
   */
  constructor(
    message: string, public readonly status: number, public readonly code?: string,
    public readonly corpo?: unknown,
  ) {
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

/**
 * Resultado da renovação. A diferença importa: `invalida` significa que o
 * servidor recusou o refresh (a sessão acabou), enquanto `indisponivel` é uma
 * falha passageira (rede, 5xx, limite de tentativas) — nesse caso o usuário
 * continua logado e a chamada apenas falha.
 */
export type ResultadoRenovacao = 'ok' | 'invalida' | 'indisponivel';

/** Uma renovação por vez: várias chamadas em 401 simultâneas dividem a mesma promessa. */
let renovacaoEmCurso: Promise<ResultadoRenovacao> | null = null;

export async function renovarSessao(): Promise<ResultadoRenovacao> {
  const refreshToken = sessao.refresh;
  if (!refreshToken) return 'invalida';
  if (!renovacaoEmCurso) {
    renovacaoEmCurso = (async (): Promise<ResultadoRenovacao> => {
      try {
        const res = await fetch('/api/v1/auth/refresh', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken }),
        });
        if (res.status === 401 || res.status === 403) return 'invalida';
        if (!res.ok) return 'indisponivel';
        const json = await res.json().catch(() => ({}));
        const data = json.data ?? json;
        if (!data?.accessToken) return 'indisponivel';
        sessao.set(data);
        return 'ok';
      } catch {
        return 'indisponivel';
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
    const renovacao = await renovarSessao();
    if (renovacao === 'ok') return api<T>(caminho, opcoes, true);
    // só encerra quando o servidor recusa o refresh; falha passageira mantém a sessão
    if (renovacao === 'invalida') {
      encerrarSessao();
      throw new ErroApi(MENSAGEM_EXPIROU, 401);
    }
    throw new ErroApi('Não consegui renovar sua sessão agora. Tente de novo em instantes.', 503);
  }

  const json = await res.json().catch(() => ({}));
  if (!res.ok) {
    const padrao = res.status === 401 ? MENSAGEM_EXPIROU
      : res.status >= 500 ? 'O servidor não respondeu a esta operação. Tente novamente em instantes.'
      : 'Falha na operação.';
    throw new ErroApi(json?.error?.message || padrao, res.status, json?.error?.code, json);
  }
  return (json.data ?? json) as T;
}

/** Baixa um arquivo autenticado (PDF da OC, anexos) como Blob. */
export async function baixar(caminho: string, mensagemErro = 'Falha ao baixar o arquivo.'): Promise<Blob> {
  let res = await fetch(caminho, { headers: cabecalhos(false) });
  if (res.status === 401 && sessao.refresh && (await renovarSessao()) === 'ok')
    res = await fetch(caminho, { headers: cabecalhos(false) });
  if (!res.ok) throw new ErroApi(mensagemErro, res.status);
  return res.blob();
}

/**
 * Upload multipart com o token da sessão (campo `file`, como no legado).
 * `extras` vira campos do mesmo formulário — é assim que os documentos do
 * fornecedor mandam tipo, validade e descrição junto com o arquivo.
 */
export async function enviarArquivo<T = void>(caminho: string, arquivo: File, extras: Record<string, string> = {}): Promise<T> {
  const fd = new FormData();
  fd.append('file', arquivo);
  for (const [campo, valor] of Object.entries(extras)) fd.append(campo, valor);
  let res = await fetch(caminho, { method: 'POST', headers: cabecalhos(false), body: fd });
  if (res.status === 401 && sessao.refresh && (await renovarSessao()) === 'ok')
    res = await fetch(caminho, { method: 'POST', headers: cabecalhos(false), body: fd });
  const json = await res.json().catch(() => ({}));
  if (!res.ok)
    throw new ErroApi(json?.error?.message || 'Não consegui enviar ' + arquivo.name + '.', res.status, json?.error?.code);
  return (json.data ?? json) as T;
}

/** Abre um Blob numa nova aba (mesmo comportamento do legado para PDF e anexos). */
export function abrirBlob(blob: Blob) {
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
