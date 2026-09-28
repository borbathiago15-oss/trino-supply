import type { SolicitacaoCompra } from '@/api/solicitacoes';
import { podeAprovarAlgo, podeTriar, podeVerCotacao, podeVerPedidos, type Perfil } from '@/dominio/papeis';

export interface DestinoDaSc { rotulo: string; rota: string }

/**
 * Aonde a linha da SC leva quando se clica nela: a tela onde a SC **está agora**, e não uma
 * ficha — a lista já é a ficha. O destino sai da etapa atual do acompanhamento e do que a
 * pessoa **pode** fazer lá (as mesmas réguas do menu): o pedido para quem gere pedidos, a
 * Central para quem aprova, o processo para quem vê cotação, a Torre para quem tria. Quem
 * não pode abrir nenhuma dessas telas não ganha destino — a linha do tempo é o que ela tem,
 * e um link para um 403 seria pior que nenhum. Rascunho e devolvida ficam sem destino de
 * propósito: a bola está com quem pediu, e os botões "Editar"/"Corrigir" já estão na linha.
 */
export function destinoDaSc(r: SolicitacaoCompra, u: Perfil): DestinoDaSc | null {
  const a = r.acompanhamento;
  if (!a || a.precisaDoSolicitante) return null;
  if (a.purchaseOrderId && (a.etapaAtual === 'pedido' || a.etapaAtual === 'entrega') && podeVerPedidos(u))
    return { rotulo: 'Abrir pedido', rota: `/pedidos/${a.purchaseOrderId}` };
  if (a.etapaAtual === 'aprovacao' && podeAprovarAlgo(u))
    return { rotulo: 'Decidir na Central', rota: '/aprovacoes' };
  if (a.quotationId && podeVerCotacao(u))
    return { rotulo: 'Abrir cotação', rota: `/cotacoes/${a.quotationId}` };
  if (a.etapaAtual === 'comprador' && podeTriar(u))
    return { rotulo: 'Abrir na Torre', rota: `/torre?busca=${encodeURIComponent(r.number)}` };
  return null;
}

/** Clique na linha não deve engolir o clique num botão, link ou campo dentro dela. */
export const cliqueEmControle = (alvo: EventTarget | null) =>
  alvo instanceof Element && alvo.closest('a, button, input, select, textarea, label') !== null;
