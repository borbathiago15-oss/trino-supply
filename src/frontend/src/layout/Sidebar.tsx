import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ehSubgrupo, itensVisiveis, localizar, type ItemMenu, type SubgrupoMenu } from './menu';
import { useUsuario } from '@/sessao/SessaoProvider';

const classeItem = (ativo: boolean, recuo: number) =>
  `flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-[13.5px] font-medium transition-colors ` +
  `${ativo ? 'bg-marca text-white' : 'text-slate-300 hover:bg-slate-800 hover:text-white'} ` +
  (recuo === 1 ? 'pl-5' : recuo === 2 ? 'pl-8 text-[13px]' : '');

function Folha({ item, recuo, ativo }: { item: ItemMenu; recuo: number; ativo: boolean }) {
  return (
    <Link to={item.rota} className={classeItem(ativo, recuo)} aria-current={ativo ? 'page' : undefined}>
      <span>{item.rotulo}</span>
    </Link>
  );
}

function Subgrupo({ sub, aberto, alternar, itemAtual }: {
  sub: SubgrupoMenu; aberto: boolean; alternar: () => void; itemAtual: ItemMenu | null;
}) {
  return (
    <>
      <button type="button" onClick={alternar}
        className={`flex w-full items-center justify-between rounded-lg px-3 py-2 pl-5 text-left text-[13.5px] font-semibold ` +
          (aberto ? 'text-white' : 'text-slate-300 hover:bg-slate-800 hover:text-white')}>
        <span>{sub.rotulo}</span><span className="text-[10px] opacity-80">{aberto ? '▾' : '▸'}</span>
      </button>
      {aberto && (
        <div className="flex flex-col gap-0.5">
          {sub.filhos.map((f) => <Folha key={f.id} item={f} recuo={2} ativo={f === itemAtual} />)}
        </div>
      )}
    </>
  );
}

/**
 * O menu lateral. Abre sozinho o grupo — e o subgrupo — da tela em que se está,
 * e volta a acompanhar a rota a cada navegação: antes o grupo aberto era
 * escolhido só na montagem, então chegar por link (o caso da Central de Avisos)
 * deixava o menu apontando para outro lugar. `escolha` guarda o que a pessoa
 * abriu ou fechou com a mão; `undefined` significa "ainda estou seguindo a rota".
 */
export function Sidebar() {
  const usuario = useUsuario();
  const { pathname } = useLocation();
  const grupos = itensVisiveis(usuario);
  const atual = localizar(grupos, pathname);

  const [escolha, setEscolha] = useState<{ grupo?: string | null; sub?: string | null }>({});
  useEffect(() => { setEscolha({}); }, [pathname]);

  const grupoAberto = escolha.grupo !== undefined ? escolha.grupo : (atual?.grupo.titulo ?? null);
  const subAberto = escolha.sub !== undefined ? escolha.sub : (atual?.subgrupo?.rotulo ?? null);

  return (
    <aside className="flex w-[252px] shrink-0 flex-col gap-0.5 bg-fundo px-3 pb-7 pt-5 text-slate-300" aria-label="Menu">
      <Link to="/painel" className="block px-2 pb-4 pt-1">
        <img src="/assets/brand/trino-supply-mark.png" width={420} height={108} alt="Trino Supply" className="h-auto w-[196px] max-w-full" />
      </Link>
      <nav className="flex flex-1 flex-col gap-0.5">
        {grupos.map((g) => {
          if (!g.titulo)
            return g.itens.map((i) => (ehSubgrupo(i) ? null
              : <Folha key={i.id} item={i} recuo={0} ativo={i === atual?.item} />));
          const aberto = grupoAberto === g.titulo;
          return (
            <div key={g.titulo}>
              <button type="button" onClick={() => setEscolha({ grupo: aberto ? null : g.titulo, sub: null })}
                className={`mt-2 flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-[11.5px] font-bold uppercase tracking-wide ` +
                  (aberto ? 'text-white' : 'text-slate-400 hover:bg-slate-800 hover:text-white')}>
                <span>{g.titulo}</span><span className="text-[10px] opacity-80">{aberto ? '▾' : '▸'}</span>
              </button>
              {aberto && (
                <div className="flex flex-col gap-0.5">
                  {g.itens.map((i) => ehSubgrupo(i)
                    ? <Subgrupo key={i.rotulo} sub={i} aberto={subAberto === i.rotulo} itemAtual={atual?.item ?? null}
                        alternar={() => setEscolha({ grupo: grupoAberto, sub: subAberto === i.rotulo ? null : i.rotulo })} />
                    : <Folha key={i.id} item={i} recuo={1} ativo={i === atual?.item} />)}
                </div>
              )}
            </div>
          );
        })}
      </nav>
    </aside>
  );
}
