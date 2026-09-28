import { useEffect, useState } from 'react';

export type Tema = 'claro' | 'escuro';
const CHAVE = 'ts.tema';

/**
 * O tema é escolha de cada pessoa, guardada no navegador dela (não no cadastro: é preferência
 * de leitura, não dado da empresa, e vale por aparelho — a mesma pessoa pode querer claro no
 * monitor e escuro no celular). Sem escolha, segue o sistema operacional.
 */
export function temaPreferido(): Tema {
  try {
    const guardado = localStorage.getItem(CHAVE);
    if (guardado === 'claro' || guardado === 'escuro') return guardado;
  } catch { /* navegador sem armazenamento: cai no sistema */ }
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'escuro' : 'claro';
}

/** Aplica no <html>: a classe `dark` é o que o Tailwind e o index.css leem. */
export function aplicarTema(tema: Tema) {
  document.documentElement.classList.toggle('dark', tema === 'escuro');
}

export function guardarTema(tema: Tema) {
  try { localStorage.setItem(CHAVE, tema); } catch { /* sem armazenamento a escolha vale até fechar */ }
  aplicarTema(tema);
}

export const outroTema = (t: Tema): Tema => (t === 'claro' ? 'escuro' : 'claro');

/** O tema atual e o alternador, para o botão do cabeçalho. */
export function useTema(): [Tema, () => void] {
  const [tema, setTema] = useState<Tema>(temaPreferido);
  useEffect(() => { aplicarTema(tema); }, [tema]);
  return [tema, () => { const t = outroTema(tema); guardarTema(t); setTema(t); }];
}
