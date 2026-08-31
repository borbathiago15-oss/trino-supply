'use server';

import { revalidatePath } from 'next/cache';
import type { RequisicaoDetalhada } from '@trino/contratos';
import { api, ErroDaApi } from '@/lib/api';

export interface ResultadoAcao {
  ok: boolean;
  mensagem: string;
  codigo?: string;
}

/** Traduz o erro da API para uma frase que o usuário consegue agir em cima. */
function traduzir(e: unknown): ResultadoAcao {
  if (e instanceof ErroDaApi) {
    const complemento =
      e.codigo === 'REQ-ERR-TRANSICAO' && e.corpo.estadoAtual
        ? ` (situação atual: ${e.corpo.estadoAtual})`
        : '';
    return { ok: false, mensagem: `${e.message}${complemento}`, codigo: e.codigo };
  }
  return { ok: false, mensagem: 'Não foi possível concluir a operação.' };
}

export async function detalharRequisicao(id: string): Promise<RequisicaoDetalhada> {
  return api<RequisicaoDetalhada>(`/requisicoes/${id}`);
}

export async function historicoDaRequisicao(id: string): Promise<
  { acao: string; timestampUtc: string; antes: string | null; depois: string | null }[]
> {
  // A trilha de auditoria é o histórico: cada transição gravou um registro.
  const trilha = await api<
    { acao: string; timestampUtc: string; beforeJson: { status?: string } | null; afterJson: { status?: string } | null }[]
  >(`/auditoria?entidade=requisicao_compra&entityId=${id}`).catch(() => []);
  return trilha.map((linha) => ({
    acao: linha.acao,
    timestampUtc: linha.timestampUtc,
    antes: linha.beforeJson?.status ?? null,
    depois: linha.afterJson?.status ?? null,
  }));
}

export async function assumirTriagem(id: string, version: number): Promise<ResultadoAcao> {
  try {
    await api(`/requisicoes/${id}/assumir-triagem`, { method: 'POST', body: { version } });
    revalidatePath('/compras/requisicoes');
    return { ok: true, mensagem: 'Triagem assumida.' };
  } catch (e) {
    return traduzir(e);
  }
}

export async function devolverParaAjuste(id: string, version: number, motivo: string): Promise<ResultadoAcao> {
  try {
    await api(`/requisicoes/${id}/devolver`, { method: 'POST', body: { version, motivo } });
    revalidatePath('/compras/requisicoes');
    return { ok: true, mensagem: 'Requisição devolvida para ajuste.' };
  } catch (e) {
    return traduzir(e);
  }
}
