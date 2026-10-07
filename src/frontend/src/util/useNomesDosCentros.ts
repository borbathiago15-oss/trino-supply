import { listarCentrosCusto } from '@/api/centrosCusto';
import { mapaDeNomes } from '@/dominio/centrosDeCusto';
import { useCarregar } from './useCarregar';

/**
 * Código do centro de custo → nome, para as telas que listam solicitação.
 *
 * <p>
 * Lê o cadastro inteiro (é cadastro: algumas centenas de linhas, e a rota é aberta a qualquer
 * papel interno justamente porque os formulários de solicitação já a usam). Não guarda cache
 * entre telas de propósito: um mapa global vivo pela sessão mostraria o nome antigo depois de
 * alguém corrigir o cadastro, e nome errado com cara de oficial é pior que o código cru.
 * </p>
 *
 * <p>
 * Falhar aqui <b>não quebra a tela</b>: sem o mapa, <c>rotuloDoCentro</c> devolve o código, que
 * é exatamente o que a tela mostrava antes. O rótulo do centro nunca pode derrubar a lista de
 * solicitações que o usuário veio ver.
 * </p>
 */
export function useNomesDosCentros(): Record<string, string> {
  const { dados } = useCarregar(async (signal) => mapaDeNomes(await listarCentrosCusto(false, signal)), []);
  return dados ?? {};
}
