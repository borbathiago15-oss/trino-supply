import { api, baixar, enviarArquivo } from './cliente';

/**
 * Comunicados do administrador: o recado que aparece ao abrir o sistema, dentro
 * de uma vigência, e que cada pessoa fecha quando já leu.
 */
export interface Comunicado {
  id: string;
  title: string;
  body: string | null;
  imageDocumentId: string | null;
  imageFileName: string | null;
  /** Vigência, com as duas pontas inclusivas (`yyyy-MM-dd`). */
  startsOn: string;
  endsOn: string;
  active: boolean;
  createdByLabel: string;
  createdAt: string;
  /** Quantas pessoas já fecharam — só o administrador enxerga. */
  dismissedCount: number;
}

export interface DadosComunicado {
  title?: string;
  body?: string | null;
  startsOn?: string;
  endsOn?: string;
  active?: boolean;
}

const base = '/api/v1/announcements';

/** O que esta pessoa precisa ver agora. Roda a cada abertura do sistema. */
export const comunicadosDoMomento = async (signal?: AbortSignal) =>
  (await api<{ items: Comunicado[] }>(`${base}/current`, { signal })).items ?? [];

export const fecharComunicado = (id: string) =>
  api<{ dismissed: boolean }>(`${base}/${id}/dismiss`, { method: 'POST' });

/**
 * A imagem vem pela rota do próprio comunicado — a rota genérica de documentos
 * recusaria um solicitante comum, e comunicado é para todo mundo.
 *
 * Buscada como Blob, e não posta direto no `src`: a rota exige o token da sessão,
 * e `<img src>` não manda cabeçalho. O endereço volta em cache para a imagem não
 * ser baixada de novo a cada montagem do modal.
 */
const cacheDeImagem = new Map<string, Promise<string>>();

export function urlDaImagem(id: string): Promise<string> {
  let url = cacheDeImagem.get(id);
  if (!url) {
    url = baixar(`${base}/${id}/image`, 'Falha ao carregar a imagem do comunicado.')
      .then((blob) => URL.createObjectURL(blob));
    url.catch(() => cacheDeImagem.delete(id));
    cacheDeImagem.set(id, url);
  }
  return url;
}

// ---- administração --------------------------------------------------------

export const listarComunicados = async (signal?: AbortSignal) =>
  (await api<{ items: Comunicado[] }>(`${base}/`, { signal })).items ?? [];

export const criarComunicado = (dados: DadosComunicado) =>
  api<Comunicado>(`${base}/`, { method: 'POST', body: dados });

export const atualizarComunicado = (id: string, dados: DadosComunicado) =>
  api<Comunicado>(`${base}/${id}`, { method: 'PATCH', body: dados });

export const excluirComunicado = (id: string) =>
  api<{ deleted: boolean }>(`${base}/${id}`, { method: 'DELETE' });

export const enviarImagemDoComunicado = (id: string, arquivo: File) =>
  enviarArquivo<{ documentId: string; fileName: string; announcement: Comunicado }>(
    `${base}/${id}/image`, arquivo);
