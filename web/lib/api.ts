import { useAuth } from "./store";

export class ApiError extends Error {
  constructor(public status: number, message: string, public code?: string) {
    super(message);
  }
}

// ---- Ciclo de sessão (SEC-001): access token de 15 min renovado automaticamente ----
// Em 401, tenta UMA renovação via refresh token (single-flight: chamadas concorrentes compartilham a
// mesma renovação) e repete a chamada original. Refresh falhou → sessão encerrada → tela de login.
let refreshing: Promise<boolean> | null = null;

async function tryRefresh(): Promise<boolean> {
  const { refreshToken, companyId, email } = useAuth.getState();
  if (!refreshToken || !companyId) return false;
  refreshing ??= (async () => {
    try {
      const res = await fetch("/api/v1/auth/refresh", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ companyId, refreshToken }),
      });
      if (!res.ok) return false;
      const body = await res.json();
      useAuth.getState().setAuth({
        token: body.accessToken, refreshToken: body.refreshToken, companyId, email: email ?? "",
      });
      return true;
    } catch {
      return false;
    } finally {
      // Libera o single-flight só depois de o estado ser atualizado.
      setTimeout(() => { refreshing = null; }, 0);
    }
  })();
  return refreshing;
}

function endSession(): void {
  useAuth.getState().logout();
  if (typeof window !== "undefined" && !window.location.pathname.startsWith("/login")) {
    window.location.href = "/login";
  }
}

/** fetch autenticado com renovação automática. Endpoints /auth/* não disparam renovação (evita loop). */
async function authFetch(path: string, init: () => RequestInit): Promise<Response> {
  const go = () => {
    const token = useAuth.getState().token;
    const opts = init();
    return fetch(`/api/v1${path}`, {
      ...opts,
      headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(opts.headers ?? {}) },
    });
  };

  let res = await go();
  if (res.status === 401 && !path.startsWith("/auth/")) {
    if (await tryRefresh()) res = await go();
    if (res.status === 401) endSession();
  }
  return res;
}

/**
 * Cliente da API do Trino Supply (/api/v1). Anexa o Bearer token do estado de auth, renova a sessão
 * automaticamente e normaliza erros de negócio (code/message) em <see cref="ApiError"/>.
 */
export async function api<T = unknown>(path: string, opts: RequestInit = {}): Promise<T> {
  const res = await authFetch(path, () => ({
    ...opts,
    headers: { "Content-Type": "application/json", ...(opts.headers ?? {}) },
  }));

  if (res.status === 204) return undefined as T;

  const body = await res.json().catch(() => null);
  if (!res.ok) {
    const message = (body && (body.message as string)) || res.statusText;
    const code = body && (body.code as string);
    throw new ApiError(res.status, message, code);
  }
  return body as T;
}

/** Baixa um arquivo autenticado (blob) e dispara o download no navegador. */
export async function download(path: string, filename: string): Promise<void> {
  const res = await authFetch(path, () => ({}));
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new ApiError(res.status, (body && (body.message as string)) || res.statusText, body?.code);
  }
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/** Envia um arquivo (multipart/form-data) — com campos extras opcionais — e devolve o JSON de resposta. */
export async function upload<T = unknown>(
  path: string, file: File, fields: Record<string, string> = {}, field = "file",
): Promise<T> {
  // FormData recriado a cada tentativa (a renovação de sessão pode repetir a request).
  const makeForm = () => {
    const form = new FormData();
    form.append(field, file);
    for (const [k, v] of Object.entries(fields)) form.append(k, v);
    return form;
  };
  const res = await authFetch(path, () => ({ method: "POST", body: makeForm() }));
  const body = await res.json().catch(() => null);
  if (!res.ok) {
    throw new ApiError(res.status, (body && (body.message as string)) || res.statusText, body?.code);
  }
  return body as T;
}

// ---- Tipos das respostas da API ----
export interface LoginResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}
export interface ItemView {
  id: string;
  code: string;
  name: string;
  baseUnitId: string;
  status: string;
  group: string;
  ca?: string | null;
}
export interface CollaboratorView {
  id: string;
  name: string;
  registration?: string | null;
  costCenterCode?: string | null;
  companyCode?: string | null;
  admissionDate?: string | null;
  status: string;
}
export interface ConsumptionView {
  id: string;
  companyCode: string;
  costCenterCode: string;
  collaboratorId: string;
  collaboratorName: string;
  reason: string;
  issuedBy: string;
  issuedAt: string;
  lines: { itemCode: string; quantity: number }[];
}
export interface StockRequestView {
  id: string;
  requesterSubject: string;
  companyCode: string;
  costCenterCode: string;
  managerSubject: string;
  reason: string;
  status: string;
  createdAt: string;
  decisionBy?: string | null;
  decisionAt?: string | null;
  decisionNote?: string | null;
  linkedRequisitionId?: string | null;
  lines: { itemCode: string; quantity: number; balance: number }[];
}
export interface CostCenterView {
  id: string;
  code: string;
  name: string;
  status: string;
  payingCompanyCode?: string | null;
  payingCompanyName?: string | null;
}
export interface UserView {
  id: string;
  subject: string;
  email: string;
  displayName: string;
  status: string;
  roleIds: string[];
  costCenterCodes: string[];
}
export interface RoleView {
  id: string;
  name: string;
  permissions: string[];
}
export interface ApprovalsView {
  requisitions: RequisitionView[];
  stockRequests: StockRequestView[];
}
export interface BalanceView {
  itemId: string;
  itemCode: string;
  quantity: number;
  version: number;
}
export interface Suggestion {
  itemId: string;
  itemCode: string;
  itemName: string;
  balance: number;
  minLevel: number;
  maxLevel: number;
  suggestedQuantity: number;
}
export interface RequisitionView {
  id: string;
  requester: string;
  status: string;
  createdAt: string;
  payingCompanyCode: string;
  payingCompanyName: string;
  costCenterCode: string;
  costCenterName: string;
  priority: string;
  justification: string;
  approverLevel1: string;
  approverLevel2: string;
  level1DecidedBy?: string | null;
  level2DecidedBy?: string | null;
  rejectedBy?: string | null;
  decisionNote?: string | null;
  lines: { itemCode: string; quantity: number; unit: string }[];
}
export interface OrderLineView {
  itemCode: string;
  description: string;
  quantity: number;
  unit: string;
  unitPrice: number;
  irrfPercent: number;
  issPercent: number;
  serviceValue: number;
  irrfValue: number;
  issValue: number;
  deliveryDate?: string | null;
}
export interface OrderView {
  id: string;
  number: number;
  requisitionId: string;
  payingCompanyId: string;
  payingCompanyName: string;
  supplierId: string;
  supplierCode: string;
  supplierName: string;
  status: string;
  issuedBy: string;
  issuedAt: string;
  paymentTerms: string;
  paymentMethod: string;
  productsValue: number;
  ipiValue: number;
  icmsValue: number;
  discountValue: number;
  otherExpenses: number;
  freightTerms: string;
  netValue: number;
  cancelledBy?: string | null;
  cancelledAt?: string | null;
  cancelReason?: string | null;
  lines: OrderLineView[];
}

export interface AuditView {
  id: string;
  occurredAt: string;
  actor: string;
  action: string;
  targetType?: string | null;
  targetId?: string | null;
  metadata?: string | null;
}
export interface SupplierStatsView {
  supplierId: string;
  code: string;
  name: string;
  ordersCount: number;
  totalValue: number;
  lastOrderAt?: string | null;
}
export interface PayingCompanyView {
  id: string;
  code: string;
  legalName: string;
  taxId: string;
  stateRegistration: string;
  address: string;
  district: string;
  city: string;
  state: string;
  zipCode: string;
  phone: string;
  email: string;
  status: string;
}
export interface SupplierFullView {
  id: string;
  code: string;
  name: string;
  taxId: string;
  stateRegistration: string;
  address: string;
  district: string;
  city: string;
  state: string;
  zipCode: string;
  phone: string;
  email: string;
  paymentTerms: string;
  paymentMethod: string;
  status: string;
}
export interface CenterConsumptionRow {
  costCenterCode: string;
  itemCode: string;
  itemName: string;
  totalQuantity: number;
  movements: number;
}
export interface CollaboratorConsumptionRow {
  collaboratorName: string;
  registration: string | null;
  costCenterCode: string;
  deliveries: number;
  totalItems: number;
}
export interface CycleStats {
  total: number;
  approved: number;
  rejected: number;
  fulfilledFromStock: number;
  pending: number;
  avgHoursToLevel1: number | null;
  avgHoursToLevel2: number | null;
}
