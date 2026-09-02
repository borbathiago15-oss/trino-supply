import { api } from './cliente';

/** CNPJ do grupo: cada centro de custo pode apontar para um. */
export interface Empresa { id: string; legalName: string; taxId: string; city?: string | null; state?: string | null; active?: boolean }

export const listarEmpresas = async (signal?: AbortSignal) =>
  (await api<{ items: Empresa[] }>('/api/v1/companies/', { signal })).items;
