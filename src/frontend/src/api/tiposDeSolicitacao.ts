import { api } from './cliente';

/**
 * Um tipo de solicitação — a classificação da solicitação (Reposição, Emergencial, Projeto…)
 * que decide, entre outras coisas, qual conjunto de prazos vale para ela.
 *
 * O `code` é a identidade: é ele que fica gravado na SC e é por ele que o prazo por tipo é
 * procurado. Por isso ele não muda depois de criado — o nome, sim.
 */
export interface TipoDeSolicitacao {
  id: string;
  code: string;
  name: string;
  description: string | null;
  active: boolean;
}

export interface CadastroDeTipos {
  canMaintain: boolean;
  items: TipoDeSolicitacao[];
}

export const listarTiposDeSolicitacao = async (incluirInativos = false, signal?: AbortSignal) => {
  const r = await api<CadastroDeTipos>(
    `/api/v1/request-types${incluirInativos ? '?all=true' : ''}`, { signal });
  return { canMaintain: r.canMaintain, items: r.items ?? [] };
};

export const criarTipoDeSolicitacao = (dados: { code: string; name: string; description: string | null }) =>
  api<TipoDeSolicitacao>('/api/v1/request-types', { method: 'POST', body: dados });

export const atualizarTipoDeSolicitacao = (
  id: string, dados: { name?: string; description?: string | null; active?: boolean },
) => api<TipoDeSolicitacao>(`/api/v1/request-types/${id}`, { method: 'PATCH', body: dados });

/**
 * Como o código é aceito: caixa alta, sem espaço nas pontas. A tela normaliza enquanto se
 * digita para o usuário ver a identidade que vai ficar gravada, em vez de descobrir depois
 * que "emergencial " virou outra coisa.
 */
export const codigoDoTipo = (v: string) => v.trim().toUpperCase().slice(0, 60);

/**
 * O rótulo do tipo de uma SC. SC antiga pode carregar um texto que não está no cadastro —
 * ela continua legível, mostrando o próprio texto, porque é o que ela de fato guardou.
 */
export const rotuloDoTipo = (codigo: string | null, tipos: TipoDeSolicitacao[]): string =>
  !codigo ? '—' : tipos.find((t) => t.code === codigo)?.name ?? codigo;
