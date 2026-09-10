import { api } from './cliente';

/**
 * Um aviso endereçado a quem está logado.
 *
 * <p>
 * É diferente da Central de Avisos, que é <b>derivada</b>: ela conta o que está aberto e o
 * número muda sozinho quando o trabalho anda — serve para "o que há para eu fazer agora".
 * Estes aqui são <b>fatos datados</b>: a SC passou a ser sua, o processo chegou à sua alçada.
 * Um contador não diz o que mudou, porque quando você olha ele já é outro número.
 * </p>
 */
export interface MeuAviso {
  id: string;
  kind: string;
  title: string;
  body: string;
  link: string | null;
  createdAt: string;
  read: boolean;
}

export interface CaixaDeAvisos {
  unread: number;
  items: MeuAviso[];
}

export const listarMeusAvisos = async (somenteNaoLidos = false, signal?: AbortSignal) => {
  const r = await api<CaixaDeAvisos>(
    `/api/v1/notices${somenteNaoLidos ? '?unreadOnly=true' : ''}`, { signal });
  return { unread: r.unread ?? 0, items: r.items ?? [] };
};

export const marcarAvisoLido = (id: string) =>
  api<{ read: boolean }>(`/api/v1/notices/${id}/read`, { method: 'POST' });

export const marcarTodosLidos = () =>
  api<{ read: number }>('/api/v1/notices/read-all', { method: 'POST' });

/** O ícone de cada tipo — o mesmo vocabulário visual do resto do sistema. */
export const ICONE_DO_AVISO: Record<string, string> = {
  DEMANDA_PARA_TRIAR: '📥',
  DEMANDA_ATRIBUIDA: '🎯',
  APROVACAO_NIVEL_1: '✍️',
  APROVACAO_NIVEL_2: '✍️',
  LIBERADO_PARA_OC: '✅',
  PRAZO_ESTOURADO: '⏱',
};

/**
 * Quando o aviso chegou, em linguagem de quem lê. "Há 2 dias" diz mais do que a data para
 * um recado recente, e a data diz mais para um recado velho.
 */
export function quandoChegou(iso: string, agora = new Date()): string {
  const t = new Date(iso).getTime();
  if (Number.isNaN(t)) return '';
  const minutos = Math.floor((agora.getTime() - t) / 60000);
  if (minutos < 1) return 'agora';
  if (minutos < 60) return `há ${minutos} min`;
  const horas = Math.floor(minutos / 60);
  if (horas < 24) return `há ${horas}h`;
  const dias = Math.floor(horas / 24);
  return dias <= 7 ? `há ${dias} dia${dias === 1 ? '' : 's'}`
    : new Date(iso).toLocaleDateString('pt-BR');
}
