import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  ICONE_DO_AVISO, listarMeusAvisos, marcarAvisoLido, marcarTodosLidos, quandoChegou,
} from '@/api/meusAvisos';
import { useCarregar } from '@/util/useCarregar';

/**
 * O sino: os avisos endereçados a quem está logado.
 *
 * <p>
 * Fica no topo, ao lado do nome, e não no menu: o menu diz <em>para onde ir</em>, e o aviso
 * diz <em>o que aconteceu</em>. Um recado escondido dentro de uma tela é um recado que
 * depende de a pessoa abrir aquela tela — e o ponto do aviso é justamente ela não precisar.
 * </p>
 *
 * <p>
 * Abrir a caixa <strong>não</strong> marca tudo como lido. Lido é o gesto de quem tratou o
 * recado, não o de quem passou o olho; marcar por abrir esvaziaria a caixa de quem só queria
 * conferir se havia algo novo.
 * </p>
 */
export function CaixaDeAvisos() {
  const [aberta, setAberta] = useState(false);
  const { dados, recarregar } = useCarregar(
    (signal) => listarMeusAvisos(false, signal).catch(() => ({ unread: 0, items: [] })), []);
  const naoLidos = dados?.unread ?? 0;

  async function lerUm(id: string) {
    try { await marcarAvisoLido(id); recarregar(); } catch { /* o recado continua lá */ }
  }
  async function lerTudo() {
    try { await marcarTodosLidos(); recarregar(); } catch { /* idem */ }
  }

  return (
    <div className="relative">
      <button type="button" data-testid="sino-avisos"
        aria-label={naoLidos ? `Avisos (${naoLidos} por ler)` : 'Avisos'}
        aria-expanded={aberta}
        className="botao-secundario relative shrink-0 !px-2.5 !py-1.5 text-[15px] leading-none"
        onClick={() => { setAberta((a) => !a); if (!aberta) recarregar(); }}>
        🔔
        {naoLidos > 0 && (
          <span data-testid="contador-avisos"
            className="absolute -right-1 -top-1 min-w-[17px] rounded-full bg-perigo px-1 text-[10.5px] font-bold leading-[17px] text-white">
            {naoLidos > 9 ? '9+' : naoLidos}
          </span>
        )}
      </button>

      {aberta && (
        <>
          {/* toque fora fecha: uma caixa que só fecha no próprio botão prende quem abriu */}
          <button type="button" aria-label="Fechar os avisos" className="fixed inset-0 z-40"
            onClick={() => setAberta(false)} />
          <div data-testid="caixa-avisos"
            className="absolute right-0 z-50 mt-2 max-h-[70vh] w-[min(360px,90vw)] overflow-y-auto rounded-painel border border-borda bg-superficie shadow-lg">
            <div className="flex items-center justify-between border-b border-borda px-3 py-2">
              <strong className="text-[13px]">Avisos</strong>
              {naoLidos > 0 && (
                <button type="button" className="text-[12px] text-marca underline" onClick={lerTudo}>
                  marcar tudo como lido
                </button>
              )}
            </div>

            {!dados?.items.length && (
              <p className="sub px-3 py-4">Nada por aqui — quando algo passar a ser seu, aparece.</p>
            )}

            {dados?.items.map((a) => (
              <div key={a.id} data-testid={`aviso-${a.id}`}
                className={'border-b border-borda px-3 py-2 last:border-0 '
                  + (a.read ? 'opacity-60' : 'bg-marca/5')}>
                <div className="flex items-start gap-2">
                  <span aria-hidden>{ICONE_DO_AVISO[a.kind] ?? '•'}</span>
                  <div className="min-w-0 flex-1">
                    <div className="text-[13px] font-semibold">
                      {a.link
                        ? <Link to={a.link} className="hover:underline" onClick={() => { setAberta(false); lerUm(a.id); }}>
                            {a.title}
                          </Link>
                        : a.title}
                    </div>
                    <div className="sub">{a.body}</div>
                    <div className="sub mt-0.5 flex items-center gap-2">
                      <span>{quandoChegou(a.createdAt)}</span>
                      {!a.read && (
                        <button type="button" className="underline" onClick={() => lerUm(a.id)}>
                          marcar como lido
                        </button>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
