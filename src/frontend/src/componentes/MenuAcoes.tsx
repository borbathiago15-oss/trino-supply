import { useEffect, useRef, useState, type ReactNode } from 'react';

export interface AcaoMenu {
  rotulo: string;
  aoEscolher: () => void;
  perigo?: boolean;
}

/**
 * Ações secundárias de uma linha, atrás de um botão. Mantém a tabela legível
 * quando a linha tem mais ações do que cabe na largura da tela.
 */
export function MenuAcoes({ acoes, rotulo = 'Mais ações' }: { acoes: AcaoMenu[]; rotulo?: string }) {
  const [aberto, setAberto] = useState(false);
  const caixa = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!aberto) return;
    const fora = (ev: MouseEvent) => { if (!caixa.current?.contains(ev.target as Node)) setAberto(false); };
    const tecla = (ev: KeyboardEvent) => { if (ev.key === 'Escape') setAberto(false); };
    document.addEventListener('mousedown', fora);
    document.addEventListener('keydown', tecla);
    return () => { document.removeEventListener('mousedown', fora); document.removeEventListener('keydown', tecla); };
  }, [aberto]);

  return (
    <div className="relative inline-block" ref={caixa}>
      <button type="button" className="botao-secundario" aria-haspopup="menu" aria-expanded={aberto}
        aria-label={rotulo} onClick={() => setAberto((a) => !a)}>
        ⋯
      </button>
      {aberto && (
        <div role="menu" className="absolute right-0 z-30 mt-1 min-w-[190px] overflow-hidden rounded-lg border border-borda bg-white py-1 shadow-lg">
          {acoes.map((a) => (
            <button key={a.rotulo} type="button" role="menuitem"
              className={'block w-full px-3 py-2 text-left text-[13px] hover:bg-superficie-suave ' + (a.perigo ? 'text-perigo' : 'text-texto')}
              onClick={() => { setAberto(false); a.aoEscolher(); }}>
              {a.rotulo}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

/** Célula de ações: as principais em botões, o resto no menu. */
export const CelulaAcoes = ({ children }: { children: ReactNode }) =>
  <div className="flex items-start gap-1.5">{children}</div>;
