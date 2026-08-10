import { useQuery } from "@tanstack/react-query";
import { api } from "./api";

export interface Me {
  subject: string | null;
  companyId: string | null;
  permissions: string[];
}

/** Permissões conhecidas (espelham o catálogo do backend). */
export const Perm = {
  UsersRead: "users.read",
  UsersManage: "users.manage",
  RolesManage: "roles.manage",
  AuditRead: "audit.read",
  MaterialsRead: "materials.read",
  MaterialsManage: "materials.manage",
  PurchasesRead: "purchases.read",
  PurchasesRequest: "purchases.request",
  PurchasesApprove: "purchases.approve",
  PurchasesOrder: "purchases.order",
} as const;

/** Identidade + permissões efetivas do usuário corrente (cacheado). */
export function useMe() {
  return useQuery({ queryKey: ["me"], queryFn: () => api<Me>("/me"), staleTime: 60_000 });
}

/** Predicado de permissão: <code>const has = useHas(); has(Perm.MaterialsManage)</code>. */
export function useHas() {
  const { data } = useMe();
  const set = new Set(data?.permissions ?? []);
  return (permission: string) => set.has(permission);
}
