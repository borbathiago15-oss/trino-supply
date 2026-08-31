'use server';

import { revalidatePath } from 'next/cache';
import type { PedidoDetalhado, RegistrarRecebimentoBody, RespostaRecebimento } from '@trino/contratos';
import { api, ErroDaApi } from '@/lib/api';

export interface ResultadoRecebimento {
  ok: boolean;
  mensagem: string;
  codigo?: string;
  conferencia?: RespostaRecebimento;
}

export async function carregarPedido(pedidoId: string): Promise<PedidoDetalhado> {
  return api<PedidoDetalhado>(`/pedidos/${pedidoId}`);
}

/**
 * Registra o recebimento. A conciliação 3-way é do servidor — a tela devolve
 * as divergências encontradas para o conferente ver o que ficou apontado.
 */
export async function registrarRecebimento(
  pedidoId: string,
  corpo: RegistrarRecebimentoBody,
): Promise<ResultadoRecebimento> {
  try {
    const conferencia = await api<RespostaRecebimento>(`/pedidos/${pedidoId}/recebimentos`, {
      method: 'POST',
      body: corpo,
    });
    revalidatePath('/compras/recebimento');
    return {
      ok: true,
      mensagem:
        conferencia.tipo === 'TOTAL'
          ? 'Recebimento total registrado — pedido concluído.'
          : 'Recebimento parcial registrado.',
      conferencia,
    };
  } catch (e) {
    if (e instanceof ErroDaApi) return { ok: false, mensagem: e.message, codigo: e.codigo };
    return { ok: false, mensagem: 'Não foi possível registrar o recebimento.' };
  }
}
