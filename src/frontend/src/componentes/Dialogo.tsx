import { useEffect, useRef, type ReactNode } from 'react';

/**
 * Caixa modal simples: fecha no Esc e no clique fora, e devolve o foco ao
 * primeiro elemento. Substitui os `confirm()`/`prompt()` do legado.
 */
export function Dialogo({ titulo, children, acoes, aoFechar }:
  { titulo: string; children: ReactNode; acoes?: ReactNode; aoFechar: () => void }) {
  const caixa = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const tecla = (ev: KeyboardEvent) => { if (ev.key === 'Escape') aoFechar(); };
    document.addEventListener('keydown', tecla);
    caixa.current?.querySelector<HTMLElement>('button, input, select, textarea')?.focus();
    return () => document.removeEventListener('keydown', tecla);
  }, [aoFechar]);

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40 px-4"
      onMouseDown={(ev) => { if (ev.target === ev.currentTarget) aoFechar(); }}>
      <div ref={caixa} role="dialog" aria-modal="true" aria-label={titulo}
        className="w-full max-w-[520px] rounded-painel bg-white p-6 shadow-xl">
        <h2 className="mb-3 text-[16px] font-bold">{titulo}</h2>
        <div className="text-[13.5px]">{children}</div>
        <div className="mt-5 flex flex-wrap justify-end gap-2">
          {acoes ?? <button type="button" className="botao" onClick={aoFechar}>Fechar</button>}
        </div>
      </div>
    </div>
  );
}

/** Confirmação de uma ação, com o texto do botão que confirma. */
export function Confirmacao({ titulo, mensagem, rotuloConfirmar = 'Confirmar', perigo, aoConfirmar, aoFechar }:
  { titulo: string; mensagem: ReactNode; rotuloConfirmar?: string; perigo?: boolean; aoConfirmar: () => void; aoFechar: () => void }) {
  return (
    <Dialogo titulo={titulo} aoFechar={aoFechar} acoes={
      <>
        <button type="button" className="botao-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className={perigo ? 'botao-perigo' : 'botao'} onClick={aoConfirmar}>{rotuloConfirmar}</button>
      </>
    }>
      {mensagem}
    </Dialogo>
  );
}
