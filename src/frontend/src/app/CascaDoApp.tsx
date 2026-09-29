import { Navigate, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { BarraDeAjuda } from '@/layout/BarraDeAjuda';
import { BotaoDeTema } from '@/componentes/BotaoDeTema';
import { AvisosProvider } from '@/sessao/AvisosProvider';
import { useSessao, useUsuario } from '@/sessao/SessaoProvider';
import { abasDoApp } from './abas';
import { AvisoDeNovaVersao } from '@/versao/AvisoDeNovaVersao';

/**
 * O app de bolso: três telas — aprovar, a visão da diretoria e o dashboard —, com as abas
 * embaixo, onde o polegar alcança. É o mesmo sistema (mesma sessão, mesmas regras, mesmo
 * servidor), instalado na tela do celular pelo manifesto; não há segunda base de código para
 * divergir da primeira. E não tem saída para o resto do sistema: a aba "Sistema" abria o menu
 * inteiro no celular, e `EscopoDoApp` segura os links das três telas que apontam para fora.
 */
export function CascaDoApp() {
  const usuario = useUsuario();
  const { sair } = useSessao();
  const navegar = useNavigate();
  const { pathname } = useLocation();
  const abas = abasDoApp(usuario);
  const atual = abas.find((a) => pathname.startsWith(a.rota));

  return (
    <AvisosProvider>
      <div className="flex min-h-dvh flex-col bg-superficie-suave">
        <header className="sticky top-0 z-20 border-b border-borda bg-superficie/95 px-4 pb-2 pt-[max(0.5rem,env(safe-area-inset-top))] backdrop-blur">
          <div className="flex items-center justify-between gap-2">
            <div className="min-w-0">
              <div className="text-[11px] font-semibold uppercase tracking-wide text-marca">Trino Supply</div>
              <h1 id="titulo-pagina" className="truncate text-lg font-bold text-texto">{atual?.rotulo ?? 'App'}</h1>
            </div>
            <div className="flex shrink-0 items-center gap-1.5">
              <BarraDeAjuda />
              <BotaoDeTema />
              <button type="button" className="botao-secundario !px-2.5 !py-1.5"
                onClick={async () => { await sair(); navegar('/login', { replace: true, state: { de: '/app' } }); }}>
                Sair
              </button>
            </div>
          </div>
        </header>

        <main className="mx-auto w-full max-w-5xl flex-1 px-3 pb-24 pt-3 sm:px-4">
          {/* o app que o Android guardou na memória volta horas depois: é aqui que ele fica sabendo do deploy */}
          <AvisoDeNovaVersao />
          {abas.length === 0
            ? (
              <div className="rounded-xl border border-borda bg-superficie p-5 text-[14px]" data-testid="app-sem-abas">
                O app é para aprovar e acompanhar a compra, e o seu perfil não tem nenhuma dessas telas.
                {/* recarrega a página de propósito: é a única saída do app, e o guarda do escopo nasce zerado */}
                <div className="mt-3"><a className="botao" href="/">Abrir o sistema completo</a></div>
              </div>
            )
            : <Outlet />}
        </main>

        {abas.length > 0 && (
          <nav aria-label="Telas do app" data-testid="abas-do-app"
            className="fixed inset-x-0 bottom-0 z-20 border-t border-borda bg-superficie pb-[env(safe-area-inset-bottom)]">
            <ul className="mx-auto flex max-w-5xl">
              {abas.map((a) => (
                <li key={a.id} className="flex-1">
                  <NavLink to={a.rota}
                    className={({ isActive }) => `block py-3 text-center text-[13px] font-semibold ${isActive ? 'text-marca' : 'text-texto-suave'}`}>
                    {a.rotulo}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
        )}
      </div>
    </AvisosProvider>
  );
}

/** `/app` abre na primeira aba que a pessoa pode ver — para quem aprova, a Central. */
export function InicioDoApp() {
  const abas = abasDoApp(useUsuario());
  return abas.length ? <Navigate to={abas[0].rota} replace /> : null;
}
