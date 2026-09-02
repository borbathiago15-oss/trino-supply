import { useCallback, useEffect, useRef, useState } from 'react';

interface Estado<T> { dados: T | null; erro: string | null; carregando: boolean }

/**
 * Carrega dados de uma função assíncrona, com cancelamento ao desmontar e
 * `recarregar()` para depois de uma ação. Simples de propósito: quando a
 * migração tiver várias telas, a troca por uma lib de cache é local.
 */
export function useCarregar<T>(carregar: (signal: AbortSignal) => Promise<T>, deps: unknown[] = []) {
  const [estado, setEstado] = useState<Estado<T>>({ dados: null, erro: null, carregando: true });
  const [rodada, setRodada] = useState(0);
  const fn = useRef(carregar);
  fn.current = carregar;

  const recarregar = useCallback(() => setRodada((r) => r + 1), []);

  useEffect(() => {
    const ctl = new AbortController();
    let vivo = true;
    setEstado((e) => ({ ...e, carregando: true, erro: null }));
    fn.current(ctl.signal)
      .then((dados) => { if (vivo) setEstado({ dados, erro: null, carregando: false }); })
      .catch((e: unknown) => {
        if (vivo) setEstado((atual) => ({ dados: atual.dados, erro: e instanceof Error ? e.message : String(e), carregando: false }));
      });
    return () => { vivo = false; ctl.abort(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rodada, ...deps]);

  return { ...estado, recarregar };
}
