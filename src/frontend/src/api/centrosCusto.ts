import { api } from './cliente';

export interface AprovadorCc { userId: string; name: string }

/** Centro de custo: dimensão dos dashboards e dono das alçadas de aprovação. */
export interface CentroCusto {
  id: string;
  code: string;
  name: string;
  region: string | null;
  companyId: string | null;
  managerUserId: string | null;
  managerName: string | null;
  clientName: string | null;
  /** O centro também é endereço de entrega: aparece no "Local de entrega" da SC. */
  receivesMaterial: boolean;
  active: boolean;
  level1ValueLimit: number | null;
  level2ValueLimit: number | null;
  level1: AprovadorCc[];
  level2: AprovadorCc[];
}

export interface DadosCentroCusto {
  name: string;
  region: string | null;
  managerUserId: string | null;
  clientName: string | null;
  companyId: string | null;
  level1UserIds: string[];
  level2UserIds: string[];
  level1ValueLimit: number | null;
  level2ValueLimit: number | null;
  /** Apagar os dois campos remove os limites em vez de mantê-los. */
  clearValueLimits: boolean;
  receivesMaterial: boolean;
}

const base = '/api/v1/cost-centers';

export const listarCentrosCusto = async (incluirInativos = false, signal?: AbortSignal) =>
  (await api<{ items: CentroCusto[] }>(`${base}/${incluirInativos ? '?all=true' : ''}`, { signal })).items;

export const criarCentroCusto = (dados: DadosCentroCusto) =>
  api<CentroCusto>(`${base}/`, { method: 'POST', body: dados });

export const atualizarCentroCusto = (id: string, dados: Partial<DadosCentroCusto> & { active?: boolean }) =>
  api<CentroCusto>(`${base}/${id}`, { method: 'PATCH', body: dados });
