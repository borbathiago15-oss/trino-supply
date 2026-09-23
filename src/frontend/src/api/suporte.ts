import { api, enviarArquivo } from './cliente';

/**
 * A situação diz de quem é a vez. "Aberto" não basta: aberto esperando o suporte e aberto
 * esperando quem pediu são filas diferentes, com donos diferentes.
 */
export type SituacaoDoChamado = 'AGUARDANDO_SUPORTE' | 'AGUARDANDO_USUARIO' | 'RESOLVIDO';
export type CategoriaDoChamado = 'DUVIDA' | 'ERRO' | 'ACESSO' | 'SUGESTAO';

export const ROTULO_SITUACAO: Record<SituacaoDoChamado, string> = {
  AGUARDANDO_SUPORTE: 'Aguardando o suporte',
  AGUARDANDO_USUARIO: 'Aguardando você',
  RESOLVIDO: 'Resolvido',
};

export const ROTULO_CATEGORIA: Record<CategoriaDoChamado, string> = {
  DUVIDA: 'Dúvida de uso',
  ERRO: 'Algo não funcionou',
  ACESSO: 'Acesso ou permissão',
  SUGESTAO: 'Sugestão de melhoria',
};

export interface Chamado {
  id: string;
  number: string;
  status: SituacaoDoChamado;
  category: CategoriaDoChamado;
  subject: string;
  screen: string;
  screenLabel: string;
  createdByLabel: string;
  createdAt: string;
  updatedAt: string;
  assignedToLabel: string | null;
  resolvedAt: string | null;
  resolvedByLabel: string | null;
}

export interface MensagemDoChamado {
  id: string;
  authorLabel: string;
  fromSupport: boolean;
  text: string;
  attachmentId: string | null;
  attachmentName: string | null;
  createdAt: string;
}

export interface ChamadoCompleto {
  ticket: Chamado;
  clientInfo: string | null;
  messages: MensagemDoChamado[];
  /** Quem olha é do suporte neste chamado — decidido no servidor, com a régua da gravação. */
  souDoSuporte: boolean;
}

export interface ResumoDoSuporte {
  /** Chamados meus que o suporte respondeu: a vez é minha. */
  paraMim: number;
  /** O que espera o suporte — nulo para quem não atende. */
  fila: number | null;
  atende: boolean;
}

const base = '/api/v1/support';

export const resumoDoSuporte = (signal?: AbortSignal) => api<ResumoDoSuporte>(`${base}/summary`, { signal });

export const listarChamados = async (escopo: 'meus' | 'fila', situacao?: SituacaoDoChamado, signal?: AbortSignal) => {
  const q = new URLSearchParams({ escopo });
  if (situacao) q.set('situacao', situacao);
  return (await api<{ items: Chamado[] }>(`${base}/tickets?${q}`, { signal })).items;
};

export const lerChamado = (id: string, signal?: AbortSignal) =>
  api<ChamadoCompleto>(`${base}/tickets/${id}`, { signal });

export interface NovoChamado {
  category: CategoriaDoChamado;
  subject: string;
  description: string;
  screen: string;
  screenLabel: string;
  clientInfo?: string;
}

export const abrirChamado = (dados: NovoChamado) =>
  api<{ ticket: Chamado; firstMessageId: string | null }>(`${base}/tickets`, { method: 'POST', body: dados });

export const responderChamado = (id: string, dados: { text?: string; resolve?: boolean }) =>
  api<{ ticket: Chamado; message: MensagemDoChamado | null }>(`${base}/tickets/${id}/messages`,
    { method: 'POST', body: dados });

export const anexarAoChamado = (id: string, mensagemId: string, arquivo: File) =>
  enviarArquivo<{ documentId: string; fileName: string }>(
    `${base}/tickets/${id}/messages/${mensagemId}/attachment`, arquivo);

/**
 * O que ajuda a reproduzir o defeito que só acontece "aqui": navegador e tamanho da janela.
 * Nada de identificação — o chamado já diz quem abriu.
 */
export function infoDoNavegador(): string {
  try {
    return `${navigator.userAgent} · janela ${window.innerWidth}x${window.innerHeight}`.slice(0, 300);
  } catch {
    return '';
  }
}
