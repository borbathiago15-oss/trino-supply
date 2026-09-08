import { useEffect, useState } from 'react';
import {
  comunicadosDoMomento, fecharComunicado, urlDaImagem, type Comunicado,
} from '@/api/comunicados';
import { data } from '@/util/formato';

/** A imagem do comunicado, buscada com o token da sessão e trocada por um endereço local. */
function Cartaz({ comunicado }: { comunicado: Comunicado }) {
  const [url, setUrl] = useState<string | null>(null);
  const [falhou, setFalhou] = useState(false);

  useEffect(() => {
    let vivo = true;
    urlDaImagem(comunicado.id)
      .then((u) => { if (vivo) setUrl(u); })
      .catch(() => { if (vivo) setFalhou(true); });
    return () => { vivo = false; };
  }, [comunicado.id]);

  // a imagem falhar não pode esconder o recado: o texto continua de pé
  if (falhou) return null;
  if (!url) return <div className="h-40 animate-pulse rounded-lg bg-superficie-suave" />;
  return (
    <img src={url} alt={comunicado.title}
      className="max-h-[52vh] w-full rounded-lg border border-borda object-contain" />
  );
}

/**
 * Comunicados do administrador, ao abrir o sistema.
 *
 * Aparece um de cada vez, na ordem em que entraram em vigência: uma pilha de
 * recados sobrepostos não é lida, é dispensada. Fechar grava no servidor —
 * então não volta quando a pessoa entrar de outro computador.
 *
 * Falhar aqui não pode impedir ninguém de trabalhar: se a leitura der erro, a
 * tela simplesmente não mostra nada.
 */
export function ModalDeComunicados() {
  const [fila, setFila] = useState<Comunicado[]>([]);
  const [fechando, setFechando] = useState(false);

  useEffect(() => {
    let vivo = true;
    comunicadosDoMomento()
      .then((itens) => { if (vivo) setFila(itens); })
      .catch(() => { /* comunicado não é bloqueio: sem ele o sistema abre igual */ });
    return () => { vivo = false; };
  }, []);

  const atual = fila[0];
  if (!atual) return null;

  async function fechar() {
    if (!atual) return;
    setFechando(true);
    try { await fecharComunicado(atual.id); } catch { /* fechar de novo depois não custa nada */ }
    setFila((f) => f.slice(1));
    setFechando(false);
  }

  return (
    <div role="dialog" aria-modal="true" aria-labelledby="comunicado-titulo"
      data-testid="comunicado"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-4">
      <div className="max-h-[90vh] w-full max-w-[640px] overflow-y-auto rounded-painel bg-superficie p-5 shadow-2xl">
        <div className="mb-1 text-[11.5px] font-bold uppercase tracking-wide text-marca">Comunicado</div>
        <h2 id="comunicado-titulo" className="text-[19px] font-bold">{atual.title}</h2>
        <p className="sub mt-1">
          {atual.createdByLabel} · vigente de {data(atual.startsOn)} a {data(atual.endsOn)}
        </p>

        {atual.imageDocumentId && <div className="mt-3"><Cartaz comunicado={atual} /></div>}
        {atual.body && <p className="mt-3 whitespace-pre-wrap text-[14px]">{atual.body}</p>}

        <div className="mt-5 flex items-center justify-between gap-3">
          {/* quantos faltam: quem tem três recados na fila merece saber antes de fechar o primeiro */}
          <span className="sub">
            {fila.length > 1 ? `Mais ${fila.length - 1} comunicado(s) depois deste.` : ''}
          </span>
          <button type="button" className="botao" disabled={fechando} onClick={fechar}>
            {fechando ? 'Fechando…' : 'Entendi, fechar'}
          </button>
        </div>
      </div>
    </div>
  );
}
