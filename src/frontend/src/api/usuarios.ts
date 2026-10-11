import { api } from './cliente';
import type { Modulo, Papel } from '@/dominio/papeis';

/** Usuário ativo para pickers de vínculo (id, nome e papel — sem dados sensíveis). */
export interface UsuarioPicker { id: string; name: string; role: Papel }

export const listarUsuariosPicker = async (signal?: AbortSignal) =>
  (await api<{ items: UsuarioPicker[] }>('/api/v1/users/pickers', { signal })).items;

/** Usuário do cadastro. Só o administrador enxerga esta lista. */
export interface UsuarioCadastro {
  id: string;
  email: string;
  name: string;
  role: Papel;
  active: boolean;
  modules: Modulo[];
  /** true quando as autorizações foram escolhidas à mão, e não herdadas do papel. */
  customModules: boolean;
  costCenters: string[];
  directorId: string | null;
  /** Gestor de Suprimentos que dá a 2ª alçada das compras deste comprador. */
  supplyManagerId: string | null;
  /** Setor em que a pessoa trabalha — quem tem setor não escolhe o setor de um ciclo. */
  sectorId: string | null;
  /** Ainda usa a senha de cadastro: troca obrigatória no próximo acesso (SEC-004). */
  mustChangePassword: boolean;
  passwordChangedAt: string | null;
  /** Senhas erradas seguidas; cinco fecham a conta por um tempo (IAM-ERR-003). */
  failedLogins?: number;
  /** Até quando a conta está fechada por tentativas erradas. Nulo = livre. */
  lockedUntil?: string | null;
  createdAt: string;
  updatedAt: string | null;
}

const base = '/api/v1/users';

export const listarUsuarios = (signal?: AbortSignal) =>
  api<{ items: UsuarioCadastro[]; roles: Papel[]; availableModules: Modulo[] }>(`${base}/`, { signal });

export interface DadosUsuario {
  name: string;
  role: Papel;
  modules: Modulo[];
  costCenters: string[];
  directorId: string | null;
  supplyManagerId: string | null;
  sectorId: string | null;
}

export const criarUsuario = (dados: DadosUsuario & { email: string; password: string }) =>
  api<UsuarioCadastro>(`${base}/`, { method: 'POST', body: dados });

/** E-mail e senha não mudam por aqui: a senha tem rota própria. */
export const atualizarUsuario = (id: string, dados: Partial<DadosUsuario> & { active?: boolean; clearDirector?: boolean; clearSupplyManager?: boolean; clearSector?: boolean }) =>
  api<UsuarioCadastro>(`${base}/${id}`, { method: 'PATCH', body: dados });

export const redefinirSenha = (id: string, novaSenha: string) =>
  api<unknown>(`${base}/${id}/reset-password`, { method: 'POST', body: { newPassword: novaSenha } });

/** Abre a conta fechada por senhas erradas, sem mexer na senha. */
export const desbloquearUsuario = (id: string) =>
  api<unknown>(`${base}/${id}/unlock`, { method: 'POST' });

/** A conta está fechada por tentativas erradas neste instante. */
export const contaBloqueada = (u: Pick<UsuarioCadastro, 'lockedUntil'>, agora = Date.now()) =>
  !!u.lockedUntil && new Date(u.lockedUntil).getTime() > agora;

/** Senha mínima aceita pela API. */
export const TAMANHO_MINIMO_SENHA = 12;
