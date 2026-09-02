import { api } from './cliente';

/** CNPJ do grupo: cada centro de custo pode apontar para um. */
export interface Empresa {
  id: string;
  legalName: string;
  taxId: string;
  stateRegistration: string | null;
  address: string;
  district: string | null;
  city: string;
  state: string;
  zip: string;
  phone: string | null;
  email: string | null;
  active: boolean;
}

export interface DadosEmpresa {
  legalName: string;
  stateRegistration: string | null;
  address: string;
  district: string | null;
  city: string;
  state: string;
  zip: string;
  phone: string | null;
  email: string | null;
}

const base = '/api/v1/companies';

export const listarEmpresas = async (incluirInativas = false, signal?: AbortSignal) =>
  (await api<{ items: Empresa[] }>(`${base}/${incluirInativas ? '?all=true' : ''}`, { signal })).items;

export const criarEmpresa = (dados: DadosEmpresa & { taxId: string }) =>
  api<Empresa>(`${base}/`, { method: 'POST', body: dados });

/** O CNPJ é a identidade da empresa e não muda depois do cadastro. */
export const atualizarEmpresa = (id: string, dados: Partial<DadosEmpresa> & { active?: boolean }) =>
  api<Empresa>(`${base}/${id}`, { method: 'PATCH', body: dados });

/**
 * Padrão da O.C.: vale quando o centro de custo do processo não tem CNPJ
 * vinculado, e traz também as cláusulas e a política de pagamento do PDF.
 */
export interface PerfilDaEmpresa {
  legalName: string;
  taxId: string;
  stateRegistration: string | null;
  address: string;
  district: string | null;
  city: string;
  state: string;
  zip: string;
  phone: string | null;
  email: string | null;
  deliveryAddress: string | null;
  deliveryTaxId: string | null;
  standardClauses: string | null;
  paymentPolicy: string | null;
}

export const perfilDaEmpresa = (signal?: AbortSignal) =>
  api<Partial<PerfilDaEmpresa>>('/api/v1/company', { signal });

export const salvarPerfilDaEmpresa = (dados: PerfilDaEmpresa) =>
  api<unknown>('/api/v1/company', { method: 'PUT', body: dados });

/** 14 dígitos viram 00.000.000/0000-00; o resto passa como veio. */
export function formatarCnpj(digitos: string | null | undefined): string {
  const d = (digitos ?? '').replace(/\D/g, '');
  return d.length === 14 ? `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}` : (digitos ?? '');
}
