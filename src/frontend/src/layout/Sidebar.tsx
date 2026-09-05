import { useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import { ehSubgrupo, itensVisiveis, type ItemMenu, type SubgrupoMenu } from './menu';
import { useUsuario } from '@/sessao/SessaoProvider';

const classeItem = (ativo: boolean, recuo: number) =>
  `flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-[13.5px] font-medium transition-colors ` +
  `${ativo ? 'bg-marca text-white' : 'text-slate-300 hover:bg-slate-800 hover:text-white'} ` +
  (recuo === 1 ? 'pl-5' : recuo === 2 ? 'pl-8 text-[13px]' : '');

function Folha({ item, recuo }: { item: ItemMenu; recuo: number }) {
  return (
    <NavLink to={item.rota} className={({ isActive }) => classeItem(isActive, recuo)} end={false}>
      <span>{item.rotulo}</span>
    </NavLink>
  );
}

function Subgrupo({ sub, aberto, alternar }: { sub: SubgrupoMenu; aberto: boolean; alternar: () => void }) {
  return (
    <>
      <button type="button" onClick={alternar}
        className={`flex w-full items-center justify-between rounded-lg px-3 py-2 pl-5 text-left text-[13.5px] font-semibold ` +
          (aberto ? 'text-white' : 'text-slate-300 hover:bg-slate-800 hover:text-white')}>
        <span>{sub.rotulo}</span><span className="text-[10px] opacity-80">{aberto ? '▾' : '▸'}</span>
      </button>
      {aberto && <div className="flex flex-col gap-0.5">{sub.filhos.map((f) => <Folha key={f.id} item={f} recuo={2} />)}</div>}
    </>
  );
}

export function Sidebar() {
  const usuario = useUsuario();
  const { pathname } = useLocation();
  const grupos = itensVisiveis(usuario);
  const grupoAtual = grupos.find((g) => g.titulo && g.itens.some((i) => !ehSubgrupo(i) && i.rota && pathname.startsWith(i.rota)));
  const [grupoAberto, setGrupoAberto] = useState<string | null>(grupoAtual?.titulo ?? null);
  const [subAberto, setSubAberto] = useState<string | null>(null);

  return (
    <aside className="flex w-[252px] shrink-0 flex-col gap-0.5 bg-fundo px-3 pb-7 pt-5 text-slate-300" aria-label="Menu">
      <NavLink to="/painel" className="block px-2 pb-4 pt-1">
        <img src="/assets/brand/trino-supply-mark.png" width={420} height={108} alt="Trino Supply" className="h-auto w-[196px] max-w-full" />
      </NavLink>
      <nav className="flex flex-1 flex-col gap-0.5">
        {grupos.map((g) => {
          if (!g.titulo)
            return g.itens.map((i) => (ehSubgrupo(i) ? null : <Folha key={i.id} item={i} recuo={0} />));
          const aberto = grupoAberto === g.titulo;
          return (
            <div key={g.titulo}>
              <button type="button" onClick={() => { setGrupoAberto(aberto ? null : g.titulo); setSubAberto(null); }}
                className={`mt-2 flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-[11.5px] font-bold uppercase tracking-wide ` +
                  (aberto ? 'text-white' : 'text-slate-400 hover:bg-slate-800 hover:text-white')}>
                <span>{g.titulo}</span><span className="text-[10px] opacity-80">{aberto ? '▾' : '▸'}</span>
              </button>
              {aberto && (
                <div className="flex flex-col gap-0.5">
                  {g.itens.map((i) => ehSubgrupo(i)
                    ? <Subgrupo key={i.rotulo} sub={i} aberto={subAberto === i.rotulo}
                        alternar={() => setSubAberto(subAberto === i.rotulo ? null : i.rotulo)} />
                    : <Folha key={i.id} item={i} recuo={1} />)}
                </div>
              )}
            </div>
          );
        })}
      </nav>
    </aside>
  );
}
