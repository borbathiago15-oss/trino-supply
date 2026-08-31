'use server';

import { revalidatePath } from 'next/cache';
import { api, ErroDaApi } from '@/lib/api';

export interface ResultadoEqualizacao {
  ok: boolean;
  mensagem: string;
  codigo?: string;
}

/**
 * Fecha a equalização escolhendo a vencedora. A justificativa é exigida aqui
 * e no servidor (EQL-ERR-005) e no banco (ck_equalizacao_desvio) — três
 * camadas, porque escolher fora do menor preço é a decisão mais auditada do
 * processo.
 */
export async function equalizar(
  cotacaoId: string,
  propostaVencedoraId: string,
  justificativaDesvio: string | undefined,
): Promise<ResultadoEqualizacao> {
  try {
    await api(`/cotacoes/${cotacaoId}/equalizacao`, {
      method: 'POST',
      body: { propostaVencedoraId, justificativaDesvio: justificativaDesvio?.trim() || undefined },
    });
    revalidatePath(`/compras/cotacoes/${cotacaoId}/equalizacao`);
    return { ok: true, mensagem: 'Equalização registrada e cotação encerrada.' };
  } catch (e) {
    if (e instanceof ErroDaApi) return { ok: false, mensagem: e.message, codigo: e.codigo };
    return { ok: false, mensagem: 'Não foi possível registrar a equalização.' };
  }
}
