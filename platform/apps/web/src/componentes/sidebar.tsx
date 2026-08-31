'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useState } from 'react';

interface ItemMenu {
  href: string;
  rotulo: string;
  icone: string;
}

const MENU: ItemMenu[] = [
  { href: '/compras/requisicoes', rotulo: 'Esteira de SCs', icone: '📋' },
  { href: '/compras/aprovacoes', rotulo: 'Aprovações', icone: '✅' },
  { href: '/compras/cotacoes', rotulo: 'Cotações', icone: '💬' },
  { href: '/compras/recebimento', rotulo: 'Recebimento', icone: '📦' },
];

/**
 * Sidebar expansível. O estado fica em localStorage porque é preferência de
 * quem está usando — não vale um round-trip nem um campo no servidor.
 */
export function Sidebar() {
  const caminho = usePathname();
  const [expandida, setExpandida] = useState(true);

  useEffect(() => {
    try {
      const salvo = window.localStorage.getItem('trino:sidebar');
      if (salvo !== null) setExpandida(salvo === '1');
    } catch {
      /* navegador sem storage: fica expandida, que é o padrão útil */
    }
  }, []);

  const alternar = () => {
    setExpandida((atual) => {
      const proxima = !atual;
      try {
        window.localStorage.setItem('trino:sidebar', proxima ? '1' : '0');
      } catch {
        /* sem storage, a preferência vale só nesta navegação */
      }
      return proxima;
    });
  };

  return (
    <aside
      className={`flex flex-col bg-trino-900 text-white transition-[width] duration-200 ${
        expandida ? 'w-60' : 'w-16'
      }`}
    >
      <div className="flex h-14 items-center gap-2 border-b border-white/10 px-4">
        <span className="text-lg">🔷</span>
        {expandida ? <span className="font-semibold tracking-wide">Trino</span> : null}
      </div>

      <nav className="flex-1 space-y-1 p-2">
        {MENU.map((item) => {
          const ativo = caminho.startsWith(item.href);
          return (
            <Link
              key={item.href}
              href={item.href}
              title={item.rotulo}
              aria-current={ativo ? 'page' : undefined}
              className={`flex items-center gap-3 rounded-md px-3 py-2 text-sm transition ${
                ativo ? 'bg-white/15 font-medium' : 'text-white/80 hover:bg-white/10'
              }`}
            >
              <span aria-hidden>{item.icone}</span>
              {expandida ? <span>{item.rotulo}</span> : null}
            </Link>
          );
        })}
      </nav>

      <button
        type="button"
        onClick={alternar}
        aria-expanded={expandida}
        aria-label={expandida ? 'Recolher menu' : 'Expandir menu'}
        className="m-2 rounded-md px-3 py-2 text-left text-sm text-white/70 hover:bg-white/10"
      >
        {expandida ? '« Recolher' : '»'}
      </button>
    </aside>
  );
}
