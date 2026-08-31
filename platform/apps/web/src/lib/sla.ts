import type { ConfiguracaoSla, Feriado } from '@trino/contratos';
import { CONFIG_COMERCIAL_PADRAO } from '@trino/contratos';
import { api } from './api';

/**
 * Configuração de SLA para o farol da esteira: os feriados são buscados UMA
 * vez por render e o cálculo roda localmente para todas as linhas — em vez de
 * uma ida ao servidor por requisição listada.
 */
export async function configuracaoSla(): Promise<ConfiguracaoSla> {
  const feriados = await api<Feriado[]>('/feriados', { revalidate: 300 });
  return {
    ...CONFIG_COMERCIAL_PADRAO,
    regime: 'COMERCIAL',
    feriados: new Set(feriados.map((f) => f.data.slice(0, 10))),
  };
}
