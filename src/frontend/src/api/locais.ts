import { api } from './cliente';

/** De onde o local veio: o almoxarifado do estoque ou um centro de custo que recebe material. */
export type TipoDeLocal = 'ALMOXARIFADO' | 'CENTRO_DE_CUSTO';

export interface LocalEntrega { id: string; code: string; name: string; kind: TipoDeLocal }

export const ROTULO_TIPO_DE_LOCAL: Record<TipoDeLocal, string> = {
  ALMOXARIFADO: 'Almoxarifado',
  CENTRO_DE_CUSTO: 'Centro de custo',
};

/** Locais de entrega para os formulários de SC (sem dados de estoque). */
export const listarLocaisDeEntrega = async (signal?: AbortSignal) =>
  (await api<{ items: LocalEntrega[] }>('/api/v1/delivery-locations', { signal })).items;

/** Como o legado grava: o campo é texto livre, preenchido com "CÓDIGO — Nome". */
export const rotuloDoLocal = (l: LocalEntrega) => `${l.code} — ${l.name}`;

/**
 * Os locais agrupados por origem, na ordem em que a tela os mostra. Sem o agrupamento,
 * "Whirlpool PB" e "Almoxarifado Sede" ficariam lado a lado como se fossem a mesma coisa.
 * Grupo vazio não entra: um `<optgroup>` sem opção é um título que não leva a lugar nenhum.
 */
export const locaisPorTipo = (locais: LocalEntrega[]) =>
  (['ALMOXARIFADO', 'CENTRO_DE_CUSTO'] as TipoDeLocal[])
    .map((kind) => ({ kind, rotulo: ROTULO_TIPO_DE_LOCAL[kind], locais: locais.filter((l) => l.kind === kind) }))
    .filter((g) => g.locais.length > 0);
