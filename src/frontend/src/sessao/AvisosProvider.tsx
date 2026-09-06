import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { listarAvisos, type Aviso } from '@/api/painel';
import { useCarregar } from '@/util/useCarregar';

interface ContextoAvisos {
  avisos: Aviso[];
  erro: string | null;
  carregando: boolean;
  /** Já respondeu ao menos uma vez — distingue "nada pendente" de "ainda não sei". */
  carregou: boolean;
  recarregar: () => void;
}

const VAZIO: ContextoAvisos = {
  avisos: [], erro: null, carregando: false, carregou: false, recarregar: () => {},
};

const Contexto = createContext<ContextoAvisos>(VAZIO);

/**
 * Os avisos pendentes, carregados uma vez e compartilhados.
 *
 * Três lugares mostram a mesma pendência com leituras diferentes — o contador
 * no menu, a trilha do processo e a Central de Avisos. Buscar em cada um seria
 * três consultas iguais e três respostas que podem divergir na tela.
 */
export function AvisosProvider({ children }: { children: ReactNode }) {
  const { dados, erro, carregando, recarregar } = useCarregar(listarAvisos, []);
  const valor = useMemo<ContextoAvisos>(() => ({
    avisos: dados ?? [], erro, carregando, carregou: dados !== null, recarregar,
  }), [dados, erro, carregando, recarregar]);

  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

/** Fora do provedor devolve a lista vazia: o menu do login não pede avisos. */
export const useAvisos = () => useContext(Contexto);
