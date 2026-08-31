'use client';

import { useEffect, useRef } from 'react';

/**
 * Modal/drawer acessível: fecha no Esc, devolve o foco e trava o scroll do
 * fundo. `lado` decide se é diálogo central (confirmação) ou gaveta lateral
 * (visualização rápida).
 */
export function Modal({
  aberto,
  titulo,
  aoFechar,
  children,
  rodape,
  lado = false,
}: {
  aberto: boolean;
  titulo: string;
  aoFechar: () => void;
  children: React.ReactNode;
  rodape?: React.ReactNode;
  lado?: boolean;
}) {
  const conteudo = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!aberto) return undefined;
    const aoTeclar = (evento: KeyboardEvent) => {
      if (evento.key === 'Escape') aoFechar();
    };
    document.addEventListener('keydown', aoTeclar);
    const anterior = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    conteudo.current?.focus();
    return () => {
      document.removeEventListener('keydown', aoTeclar);
      document.body.style.overflow = anterior;
    };
  }, [aberto, aoFechar]);

  if (!aberto) return null;

  return (
    <div className="fixed inset-0 z-50 flex" role="dialog" aria-modal="true" aria-label={titulo}>
      <button type="button" aria-label="Fechar" className="absolute inset-0 bg-slate-900/40" onClick={aoFechar} />
      <div
        ref={conteudo}
        tabIndex={-1}
        className={
          lado
            ? 'relative ml-auto flex h-full w-full max-w-xl flex-col bg-white shadow-xl outline-none'
            : 'relative m-auto flex max-h-[85vh] w-full max-w-lg flex-col rounded-lg bg-white shadow-xl outline-none'
        }
      >
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <h2 className="text-lg font-semibold text-slate-800">{titulo}</h2>
          <button type="button" onClick={aoFechar} className="text-slate-400 hover:text-slate-700" aria-label="Fechar">
            ✕
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
        {rodape ? <div className="border-t border-slate-200 px-5 py-4">{rodape}</div> : null}
      </div>
    </div>
  );
}
