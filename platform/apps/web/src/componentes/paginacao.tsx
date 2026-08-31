'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';

/**
 * Paginação REMOTA: o número da página vai na URL, então o servidor refaz a
 * consulta. Nada de fatiar no cliente uma lista que veio inteira.
 */
export function Paginacao({ pagina, tamanho, total }: { pagina: number; tamanho: number; total: number }) {
  const router = useRouter();
  const caminho = usePathname();
  const parametros = useSearchParams();
  const ultimaPagina = Math.max(1, Math.ceil(total / tamanho));

  const irPara = (destino: number) => {
    const novos = new URLSearchParams(parametros.toString());
    novos.set('pagina', String(destino));
    router.push(`${caminho}?${novos.toString()}`);
  };

  const inicio = total === 0 ? 0 : (pagina - 1) * tamanho + 1;
  const fim = Math.min(pagina * tamanho, total);

  return (
    <div className="flex items-center justify-between border-t border-slate-200 px-4 py-3 text-sm text-slate-600">
      <span>
        {inicio}–{fim} de {total}
      </span>
      <div className="flex gap-2">
        <button type="button" className="botao-secundario py-1" disabled={pagina <= 1} onClick={() => irPara(pagina - 1)}>
          Anterior
        </button>
        <span className="px-2 py-1">
          Página {pagina} de {ultimaPagina}
        </span>
        <button
          type="button"
          className="botao-secundario py-1"
          disabled={pagina >= ultimaPagina}
          onClick={() => irPara(pagina + 1)}
        >
          Próxima
        </button>
      </div>
    </div>
  );
}
