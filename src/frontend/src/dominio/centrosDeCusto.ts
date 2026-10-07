import type { CentroCusto } from '@/api/centrosCusto';

/**
 * Como o centro de custo se mostra na tela.
 *
 * <p>
 * A solicitação grava o <b>código</b> (`PER-001`), que é a identidade e não muda. Quem lê a
 * tela, porém, não decora código: "PER-001" não diz se a compra é da obra de Pernambuco ou do
 * administrativo, e era isso que a lista de solicitações mostrava em toda linha.
 * </p>
 *
 * <p>
 * A régua é a mesma que a Torre já usava: <b>o nome quando o cadastro conhece o centro, o código
 * quando não</b> — e o código fica na dica, porque é por ele que se confere com o ERP. Centro
 * fora do cadastro (ou apagado depois) continua aparecendo pelo código, em vez de virar um traço
 * que esconde a que centro a solicitação pertence.
 * </p>
 */
export const mapaDeNomes = (centros: CentroCusto[]): Record<string, string> =>
  Object.fromEntries(centros.map((c) => [c.code.trim().toUpperCase(), c.name]));

export const rotuloDoCentro = (nomes: Record<string, string>, codigo: string | null | undefined) =>
  (codigo ? nomes[codigo.trim().toUpperCase()] ?? codigo : '—');
