import { api } from './cliente';

export interface LocalEstoque { id: string; code: string; name: string }

export const listarLocais = async (signal?: AbortSignal) =>
  (await api<{ items: LocalEstoque[] }>('/api/v1/inventory/locations', { signal })).items;
