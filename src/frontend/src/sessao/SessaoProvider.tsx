import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { quemSou, sair, type Usuario } from '@/api/auth';
import { EVENTO_SESSAO_EXPIRADA, sessao } from '@/api/sessao';

interface ContextoSessao {
  usuario: Usuario | null;
  carregando: boolean;
  entrou: (u: Usuario) => void;
  sair: () => Promise<void>;
}

const Contexto = createContext<ContextoSessao | null>(null);

/**
 * Descobre quem está logado a partir dos tokens já guardados (a mesma sessão
 * do legado) e mantém o usuário à mão para as telas e o menu.
 */
export function SessaoProvider({ children }: { children: ReactNode }) {
  const [usuario, setUsuario] = useState<Usuario | null>(null);
  const [carregando, setCarregando] = useState(sessao.ativa);

  useEffect(() => {
    let vivo = true;
    if (sessao.ativa) {
      quemSou()
        .then((u) => { if (vivo) setUsuario(u); })
        .catch(() => { if (vivo) { sessao.clear(); setUsuario(null); } })
        .finally(() => { if (vivo) setCarregando(false); });
    }
    const expirou = () => { if (vivo) setUsuario(null); };
    globalThis.addEventListener(EVENTO_SESSAO_EXPIRADA, expirou);
    return () => { vivo = false; globalThis.removeEventListener(EVENTO_SESSAO_EXPIRADA, expirou); };
  }, []);

  const entrou = useCallback((u: Usuario) => setUsuario(u), []);
  const encerrar = useCallback(async () => { await sair(); setUsuario(null); }, []);

  const valor = useMemo(() => ({ usuario, carregando, entrou, sair: encerrar }), [usuario, carregando, entrou, encerrar]);
  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

export function useSessao(): ContextoSessao {
  const ctx = useContext(Contexto);
  if (!ctx) throw new Error('useSessao precisa estar dentro de SessaoProvider.');
  return ctx;
}

/** Para telas que exigem usuário logado (o guard de rota já garante isso). */
export function useUsuario(): Usuario {
  const { usuario } = useSessao();
  if (!usuario) throw new Error('Tela protegida renderizada sem usuário.');
  return usuario;
}
