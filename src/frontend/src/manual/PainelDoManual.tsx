import { useEffect, useRef } from 'react';
import type { TelaAtual } from './telas';

/**
 * O manual da tela, numa gaveta à direita. Gaveta, e não página: quem abre o manual está no
 * meio de uma tarefa e quer ler sem perder o que já digitou no formulário atrás dela.
 *
 * O fim da gaveta leva ao chamado: quem leu e não achou a resposta está exatamente no ponto
 * em que o suporte deve entrar — e com a tela já identificada.
 */
export function PainelDoManual({ tela, aoFechar, aoPedirSuporte }:
  { tela: TelaAtual; aoFechar: () => void; aoPedirSuporte: () => void }) {
  const caixa = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const tecla = (ev: KeyboardEvent) => { if (ev.key === 'Escape') aoFechar(); };
    document.addEventListener('keydown', tecla);
    return () => document.removeEventListener('keydown', tecla);
  }, [aoFechar]);

  useEffect(() => { caixa.current?.querySelector<HTMLElement>('button')?.focus(); }, []);

  const m = tela.manual;
  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-black/30"
      onMouseDown={(ev) => { if (ev.target === ev.currentTarget) aoFechar(); }}>
      <div ref={caixa} role="dialog" aria-modal="true" aria-label={`Manual — ${tela.rotulo}`}
        className="flex h-full w-full max-w-[440px] flex-col bg-white shadow-xl">
        <div className="flex items-start justify-between gap-3 border-b border-slate-200 px-5 py-4">
          <div className="min-w-0">
            <p className="text-[11.5px] font-bold uppercase tracking-wide text-texto-suave">Manual</p>
            <h2 className="text-[17px] font-bold text-slate-900">{tela.rotulo}</h2>
          </div>
          <button type="button" className="botao-secundario !px-2.5 !py-1" onClick={aoFechar} aria-label="Fechar o manual">✕</button>
        </div>

        <div className="flex-1 space-y-5 overflow-y-auto px-5 py-4 text-[13.5px] leading-relaxed text-slate-700">
          {!m ? (
            <p>Esta tela ainda não tem manual. Se tiver dúvida, abra um chamado — ele já vai com o nome da tela.</p>
          ) : (
            <>
              <section>
                <h3 className="mb-1 text-[12.5px] font-bold uppercase tracking-wide text-texto-suave">Para que serve</h3>
                <p>{m.paraQueServe}</p>
              </section>

              <section>
                <h3 className="mb-1 text-[12.5px] font-bold uppercase tracking-wide text-texto-suave">Passo a passo</h3>
                <ol className="list-decimal space-y-1.5 pl-5">
                  {m.passos.map((p) => <li key={p}>{p}</li>)}
                </ol>
              </section>

              {!!m.regras?.length && (
                <section>
                  <h3 className="mb-1 text-[12.5px] font-bold uppercase tracking-wide text-texto-suave">O que o sistema cobra</h3>
                  <ul className="space-y-2">
                    {m.regras.map((r) => (
                      <li key={r.texto} className="rounded-lg bg-slate-50 px-3 py-2">
                        {r.codigo && (
                          <code className="mr-1.5 rounded bg-slate-200 px-1.5 py-0.5 text-[11.5px] font-semibold text-slate-700">{r.codigo}</code>
                        )}
                        {r.texto}
                      </li>
                    ))}
                  </ul>
                </section>
              )}

              {!!m.duvidas?.length && (
                <section>
                  <h3 className="mb-1 text-[12.5px] font-bold uppercase tracking-wide text-texto-suave">Dúvidas comuns</h3>
                  {m.duvidas.map((d) => (
                    <details key={d.pergunta} className="border-b border-slate-100 py-2">
                      <summary className="cursor-pointer font-semibold text-slate-800">{d.pergunta}</summary>
                      <p className="mt-1">{d.resposta}</p>
                    </details>
                  ))}
                </section>
              )}
            </>
          )}
        </div>

        <div className="border-t border-slate-200 bg-slate-50 px-5 py-4">
          <p className="mb-2 text-[13px] text-texto-suave">Não achou a resposta?</p>
          <button type="button" className="botao w-full" onClick={aoPedirSuporte}>Abrir chamado sobre esta tela</button>
        </div>
      </div>
    </div>
  );
}
