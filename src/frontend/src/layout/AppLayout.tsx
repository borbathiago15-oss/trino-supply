import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { AvisosProvider } from '@/sessao/AvisosProvider';
import { useSessao, useUsuario } from '@/sessao/SessaoProvider';
import { ROTULO_PAPEL } from '@/dominio/papeis';
import { tituloDaRota } from './titulos';

/** Casca da aplicação: menu lateral escuro + conteúdo, como o legado. */
export function AppLayout() {
  const usuario = useUsuario();
  const { sair } = useSessao();
  const navegar = useNavigate();
  const { pathname } = useLocation();
  const titulo = tituloDaRota(pathname);

  return (
    <AvisosProvider>
      <div className="flex min-h-screen">
        <Sidebar />
        <main className="min-w-0 flex-1 px-7 pb-12 pt-[22px]">
          <div className="mb-[18px] flex flex-wrap items-center justify-between gap-3">
            <h1 className="text-[19px] font-bold" id="titulo-pagina">{titulo}</h1>
            <div className="flex items-center gap-2.5 text-[13px] text-texto-suave">
              <span>Sessão de <strong className="text-texto">{usuario.name || usuario.email}</strong> ({ROTULO_PAPEL[usuario.role] ?? usuario.role})</span>
              <button type="button" className="botao-perigo !py-1.5" onClick={async () => { await sair(); navegar('/login', { replace: true }); }}>Sair</button>
            </div>
          </div>
          <Outlet />
        </main>
      </div>
    </AvisosProvider>
  );
}
