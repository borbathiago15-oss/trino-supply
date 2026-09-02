import { api } from './cliente';

export interface LocalEntrega { id: string; code: string; name: string }

/** Locais de entrega para os formulários de SC (sem dados de estoque). */
export const listarLocaisDeEntrega = async (signal?: AbortSignal) =>
  (await api<{ items: LocalEntrega[] }>('/api/v1/delivery-locations', { signal })).items;

/** Como o legado grava: o campo é texto livre, preenchido com "CÓDIGO — Nome". */
export const rotuloDoLocal = (l: LocalEntrega) => `${l.code} — ${l.name}`;
