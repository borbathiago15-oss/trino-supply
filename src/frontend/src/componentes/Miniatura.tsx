import { useEffect, useState } from 'react';
import { urlDocumento } from '@/api/documentos';

/** Foto do produto guardada como documento: carrega com o token e vira miniatura. */
export function Miniatura({ documentId, descricao, aoAmpliar }:
  { documentId: string; descricao: string; aoAmpliar?: (url: string) => void }) {
  const [url, setUrl] = useState<string | null>(null);
  const [falhou, setFalhou] = useState(false);

  useEffect(() => {
    let vivo = true;
    urlDocumento(documentId)
      .then((u) => { if (vivo) setUrl(u); })
      .catch(() => { if (vivo) setFalhou(true); });
    return () => { vivo = false; };
  }, [documentId]);

  if (falhou) return <span className="sub">—</span>;
  if (!url) return <span className="block h-11 w-11 animate-pulse rounded border border-borda bg-superficie-suave" />;
  return (
    <button type="button" title="Clique para ampliar" onClick={() => aoAmpliar?.(url)} className="block">
      <img src={url} alt={descricao} className="h-11 w-11 rounded border border-borda object-cover" />
    </button>
  );
}

/** Visor da foto ampliada: fecha no clique ou no Esc, como no legado. */
export function Visor({ url, descricao, aoFechar }: { url: string; descricao: string; aoFechar: () => void }) {
  useEffect(() => {
    const tecla = (ev: KeyboardEvent) => { if (ev.key === 'Escape') aoFechar(); };
    document.addEventListener('keydown', tecla);
    return () => document.removeEventListener('keydown', tecla);
  }, [aoFechar]);

  return (
    <div role="dialog" aria-label={descricao} onClick={aoFechar}
      className="fixed inset-0 z-50 flex flex-col items-center justify-center gap-3 bg-black/80 p-6">
      <img src={url} alt={descricao} className="max-h-[80vh] max-w-full rounded-lg object-contain" />
      <p className="text-[13px] text-white">{descricao}</p>
    </div>
  );
}
