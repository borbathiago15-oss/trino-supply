import { useEffect, useState } from 'react';

/**
 * Atrasa o valor até ele parar de mudar. Existe para a busca que consulta o
 * servidor: sem isso, cada tecla vira uma requisição.
 */
export function useDebounce<T>(valor: T, ms = 300): T {
  const [atrasado, setAtrasado] = useState(valor);
  useEffect(() => {
    const t = setTimeout(() => setAtrasado(valor), ms);
    return () => clearTimeout(t);
  }, [valor, ms]);
  return atrasado;
}
