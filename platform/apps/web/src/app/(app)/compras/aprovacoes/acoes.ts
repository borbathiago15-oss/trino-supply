'use server';

import { revalidatePath } from 'next/cache';
import type { DecidirEtapaBody } from '@trino/contratos';
import { api, ErroDaApi } from '@/lib/api';

export interface ResultadoDecisao {
  ok: boolean;
  mensagem: string;
  codigo?: string;
  /** true quando a recusa foi só por falta da autorização do estouro (B6). */
  exigeAutorizacaoEstouro?: boolean;
}

/**
 * Decide uma etapa. Os bloqueios da política (B1 a B7) voltam com código, e a
 * tela usa isso para explicar o motivo em vez de mostrar "erro 409".
 */
export async function decidirEtapa(etapaId: string, corpo: DecidirEtapaBody): Promise<ResultadoDecisao> {
  try {
    await api(`/aprovacoes/etapas/${etapaId}/decisao`, { method: 'POST', body: corpo });
    revalidatePath('/compras/aprovacoes');
    return { ok: true, mensagem: corpo.decisao === 'APROVADO' ? 'Etapa aprovada.' : 'Requisição rejeitada.' };
  } catch (e) {
    if (e instanceof ErroDaApi) {
      return {
        ok: false,
        mensagem: e.message,
        codigo: e.codigo,
        exigeAutorizacaoEstouro: e.codigo === 'APV-B6',
      };
    }
    return { ok: false, mensagem: 'Não foi possível registrar a decisão.' };
  }
}
