import { useAuth } from "./store";

export class ApiError extends Error {
  constructor(public status: number, message: string, public code?: string) {
    super(message);
  }
}

/**
 * Cliente da API do Trino Supply (/api/v1). Anexa o Bearer token do estado de auth e normaliza
 * erros de negócio (code/message) em <see cref="ApiError"/>. Same-origin (proxy do Next).
 */
export async function api<T = unknown>(path: string, opts: RequestInit = {}): Promise<T> {
  const token = useAuth.getState().token;
  const res = await fetch(`/api/v1${path}`, {
    ...opts,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(opts.headers ?? {}),
    },
  });

  if (res.status === 204) return undefined as T;

  const body = await res.json().catch(() => null);
  if (!res.ok) {
    const message = (body && (body.message as string)) || res.statusText;
    const code = body && (body.code as string);
    throw new ApiError(res.status, message, code);
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
  decidedBy?: string | null;
  decidedAt?: string | null;
  decisionNote?: string | null;
  lines: { itemCode: string; quantity: number; unit: string }[];
}
export interface OrderView {
  id: string;
  requisitionId: string;
  supplierId: string;
  supplierCode: string;
  status: string;
  issuedBy: string;
  issuedAt: string;
  lines: { itemCode: string; quantity: number; unit: string }[];
}
