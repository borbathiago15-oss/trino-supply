import { create } from "zustand";
import { persist } from "zustand/middleware";

interface AuthState {
  token?: string;
  refreshToken?: string;
  companyId?: string;
  email?: string;
  setAuth: (v: { token: string; refreshToken?: string; companyId: string; email: string }) => void;
  logout: () => void;
}

/**
 * Estado de autenticação (Zustand + persist em localStorage). Guarda o access token e o tenant
 * (companyId) — usados pelo cliente de API. Em produção, considerar httpOnly cookie (SEC-001).
 */
export const useAuth = create<AuthState>()(
  persist(
    (set) => ({
      setAuth: ({ token, refreshToken, companyId, email }) =>
        set({ token, refreshToken, companyId, email }),
      logout: () => set({ token: undefined, refreshToken: undefined, companyId: undefined, email: undefined }),
    }),
    { name: "trino-auth" },
  ),
);
