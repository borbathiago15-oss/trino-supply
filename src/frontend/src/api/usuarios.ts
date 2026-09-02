import { api } from './cliente';
import type { Papel } from '@/dominio/papeis';

/** Usuário ativo para pickers de vínculo (id, nome e papel — sem dados sensíveis). */
export interface UsuarioPicker { id: string; name: string; role: Papel }

export const listarUsuariosPicker = async (signal?: AbortSignal) =>
  (await api<{ items: UsuarioPicker[] }>('/api/v1/users/pickers', { signal })).items;
