import { useEffect, useRef, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { contagemPorItem, ehSubgrupo, itensVisiveis, localizar, type ItemMenu, type SubgrupoMenu } from './menu';
import { useAvisos } from '@/sessao/AvisosProvider';
import { useUsuario } from '@/sessao/SessaoProvider';

const classeItem = (ativo: boolean, recuo: number) =>
  `flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-[13.5px] font-medium transition-colors ` +
  `${ativo ? 'bg-marca text-white' : 'text-slate-300 hover:bg-slate-800 hover:text-white'} ` +
  (recuo === 1 ? 'pl-5' : recuo === 2 ? 'pl-8 text-[13px]' : '');

function Folha({ item, recuo, ativo, pendente }: {
  item: ItemMenu; recuo: number; ativo: boolean; pendente?: number;
}) {
  // a tela que vive fora do AppLayout abre em aba nova: dentro da mesma, o usuário
  // cairia numa tela sem menu e sem caminho de volta
  const Alvo = item.novaAba
    ? ({ children, className, ...resto }: React.ComponentProps<'a'>) =>
        <a href={item.rota} target="_blank" rel="noopener" className={className} {...resto}>{children}</a>
    : ({ children, ...resto }: React.ComponentProps<'a'>) =>
        <Link to={item.rota} {...resto}>{children}</Link>;

  return (
    <Alvo className={classeItem(ativo, recuo)} aria-current={ativo ? 'page' : undefined}>
      <span>{item.rotulo}{item.novaAba && ' ↗'}</span>
      {!!pendente && (
        <span data-testid={`pendencia-${item.id}`} aria-label={`${pendente} pendente(s)`}
          className={'ml-2 shrink-0 rounded-full px-2 py-0.5 text-[11px] font-bold '
            + (ativo ? 'bg-white/25 text-white' : 'bg-aviso-fundo text-aviso')}>
          {pendente > 99 ? '99+' : pendente}
        </span>
      )}
    </Alvo>
  );
}

/** Total pendente de um punhado de telas — o que um grupo fechado precisa mostrar. */
const somar = (itens: (ItemMenu | SubgrupoMenu)[], pendencias: Record<string, number>): number =>
  itens.reduce((t, i) => t + (ehSubgrupo(i) ? somar(i.filhos, pendencias) : (pendencias[i.id] ?? 0)), 0);

/** Contador de um cabeçalho fechado: com o grupo aberto, o número está nas folhas. */
function Fechado({ total }: { total: number }) {
  if (!total) return null;
  return (
    <span className="ml-auto mr-2 rounded-full bg-aviso-fundo px-2 py-0.5 text-[11px] font-bold text-aviso">
      {total > 99 ? '99+' : total}
    </span>
  );
}

/**
 * `recuo` é o nível do cabeçalho do subgrupo: 1 quando ele mora dentro de um
 * grupo (Cotações, dentro de Compras) e 0 quando é de topo (Dashboard, que não
 * tem grupo acima). As folhas entram sempre um nível abaixo do cabeçalho.
 */
function Subgrupo({ sub, aberto, alternar, itemAtual, pendencias, recuo = 1 }: {
  sub: SubgrupoMenu; aberto: boolean; alternar: () => void; itemAtual: ItemMenu | null;
  pendencias: Record<string, number>; recuo?: number;
}) {
  return (
    <>
      <button type="button" onClick={alternar}
        className={`flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-[13.5px] font-semibold `
          + (recuo === 1 ? 'pl-5 ' : '')
          + (aberto ? 'text-white' : 'text-slate-300 hover:bg-slate-800 hover:text-white')}>
        <span>{sub.rotulo}</span>
        {!aberto && <Fechado total={somar(sub.filhos, pendencias)} />}
        <span className="text-[10px] opacity-80">{aberto ? '▾' : '▸'}</span>
      </button>
      {aberto && (
        <div className="flex flex-col gap-0.5">
          {sub.filhos.map((f) => (
            <Folha key={f.id} item={f} recuo={recuo + 1} ativo={f === itemAtual} pendente={pendencias[f.id]} />
          ))}
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
export function Sidebar({ aberto = false, aoFechar }: { aberto?: boolean; aoFechar?: () => void } = {}) {
  const usuario = useUsuario();
  const { pathname } = useLocation();
  const { avisos } = useAvisos();
  const grupos = itensVisiveis(usuario);
  const atual = localizar(grupos, pathname);
  // o contador diz onde há trabalho parado; grupo fechado carrega a soma do que esconde
  const pendencias = contagemPorItem(avisos);

  const [escolha, setEscolha] = useState<{ grupo?: string | null; sub?: string | null }>({});
  // trocar de tela devolve o menu à rota e fecha a gaveta do celular: quem tocou
  // num item quer ver a tela, não o menu por cima dela
  const fechar = useRef(aoFechar);
  fechar.current = aoFechar;
  useEffect(() => { setEscolha({}); fechar.current?.(); }, [pathname]);

  const grupoAberto = escolha.grupo !== undefined ? escolha.grupo : (atual?.grupo.titulo ?? null);
  const subAberto = escolha.sub !== undefined ? escolha.sub : (atual?.subgrupo?.rotulo ?? null);

  return (
    <aside
      className={'flex w-[252px] shrink-0 flex-col gap-0.5 overflow-y-auto bg-fundo px-3 pb-7 pt-5 text-slate-300 '
        // no notebook o menu é a coluna de sempre; no celular ele sai do fluxo e
        // vira gaveta, senão sobram 138px de conteúdo numa tela de 390px
        + 'fixed inset-y-0 left-0 z-40 transition-transform lg:static lg:z-auto lg:translate-x-0 '
        + (aberto ? 'translate-x-0 shadow-2xl' : '-translate-x-full')}
      aria-label="Menu">
      <Link to="/painel" className="block px-2 pb-4 pt-1">
        <img src="/assets/brand/trino-supply-mark.png" width={420} height={108} alt="Trino Supply" className="h-auto w-[196px] max-w-full" />
      </Link>
      <nav className="flex flex-1 flex-col gap-0.5">
        {grupos.map((g) => {
          // o topo não tem cabeçalho de grupo, mas tem subgrupo (Dashboard) — e até
          // aqui ele era descartado no `null`, então o item simplesmente sumia do menu
          if (!g.titulo)
            return g.itens.map((i) => (ehSubgrupo(i)
              ? <Subgrupo key={i.rotulo} sub={i} recuo={0} aberto={subAberto === i.rotulo}
                  itemAtual={atual?.item ?? null} pendencias={pendencias}
                  // só o subgrupo muda: mexer no grupo aberto daqui fecharia,
                  // sem motivo, o grupo em que a pessoa estava trabalhando
                  alternar={() => setEscolha((e) => ({ ...e, sub: subAberto === i.rotulo ? null : i.rotulo }))} />
              : <Folha key={i.id} item={i} recuo={0} ativo={i === atual?.item} pendente={pendencias[i.id]} />));
          const aberto = grupoAberto === g.titulo;
          return (
            <div key={g.titulo}>
              <button type="button" onClick={() => setEscolha({ grupo: aberto ? null : g.titulo, sub: null })}
                className={`mt-2 flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-[11.5px] font-bold uppercase tracking-wide ` +
                  (aberto ? 'text-white' : 'text-slate-400 hover:bg-slate-800 hover:text-white')}>
                <span>{g.titulo}</span>
                {!aberto && <Fechado total={somar(g.itens, pendencias)} />}
                <span className="text-[10px] opacity-80">{aberto ? '▾' : '▸'}</span>
              </button>
              {aberto && (
                <div className="flex flex-col gap-0.5">
                  {g.itens.map((i) => ehSubgrupo(i)
                    ? <Subgrupo key={i.rotulo} sub={i} aberto={subAberto === i.rotulo} itemAtual={atual?.item ?? null}
                        pendencias={pendencias}
                        alternar={() => setEscolha({ grupo: grupoAberto, sub: subAberto === i.rotulo ? null : i.rotulo })} />
                    : <Folha key={i.id} item={i} recuo={1} ativo={i === atual?.item} pendente={pendencias[i.id]} />)}
                </div>
              )}
            </div>
          );
        })}
      </nav>
    </aside>
  );
}
