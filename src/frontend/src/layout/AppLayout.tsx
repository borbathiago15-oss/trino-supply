import { useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { LimiteErro } from '@/componentes/LimiteErro';
import { Sidebar } from './Sidebar';
import { ModalDeComunicados } from '@/componentes/Comunicados';
import { AvisosProvider } from '@/sessao/AvisosProvider';
import { CaixaDeAvisos } from './CaixaDeAvisos';
import { useSessao, useUsuario } from '@/sessao/SessaoProvider';
import { ROTULO_PAPEL } from '@/dominio/papeis';
import { tituloDaRota } from './titulos';

/**
 * Casca da aplicação: menu lateral escuro + conteúdo.
 *
 * No notebook — que é onde o trabalho acontece — o menu é a coluna fixa de
 * sempre. No celular ele vira gaveta, atrás de um botão: com a coluna de 252px
 * no fluxo, sobravam **138px** de conteúdo numa tela de 390px, e a página ainda
 * rolava de lado. Quem abre o sistema no telefone vai aprovar ou acompanhar um
 * pedido, e nenhuma das duas coisas cabia ali.
 */
export function AppLayout() {
  const usuario = useUsuario();
  const { sair } = useSessao();
  const navegar = useNavigate();
  const { pathname } = useLocation();
  const titulo = tituloDaRota(pathname);
  const [menuAberto, setMenuAberto] = useState(false);

  return (
    <AvisosProvider>
      {/* o recado do administrador, ao abrir o sistema — por cima de qualquer tela */}
      <ModalDeComunicados />
      <div className="flex min-h-screen">
        <Sidebar aberto={menuAberto} aoFechar={() => setMenuAberto(false)} />

        {/* toque fora fecha a gaveta; no notebook a sobreposição não existe */}
        {menuAberto && (
          <button type="button" aria-label="Fechar o menu" onClick={() => setMenuAberto(false)}
            className="fixed inset-0 z-30 bg-black/50 lg:hidden" />
        )}

        <main className="min-w-0 flex-1 px-4 pb-12 pt-[18px] sm:px-7 sm:pt-[22px]">
          <div className="mb-[18px] flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
            <div className="flex min-w-0 items-center gap-2">
              <button type="button" aria-label="Abrir o menu" aria-expanded={menuAberto}
                onClick={() => setMenuAberto(true)}
                className="botao-secundario shrink-0 !px-2.5 !py-1.5 text-[16px] leading-none lg:hidden">
                ☰
              </button>
              <h1 className="truncate text-xl font-bold tracking-tight text-slate-900 sm:text-2xl" id="titulo-pagina">{titulo}</h1>
            </div>
            <div className="flex items-center gap-2.5 text-[13px] text-texto-suave">
              {/* o sino vem antes do nome: é o que muda, e o nome é o que fica */}
              <CaixaDeAvisos />
              {/* no celular o nome sozinho basta: o papel ocupa a linha inteira sem dizer muito */}
              <span className="hidden sm:inline">
                Sessão de <strong className="text-texto">{usuario.name || usuario.email}</strong> ({ROTULO_PAPEL[usuario.role] ?? usuario.role})
              </span>
              <span className="max-w-[46vw] truncate sm:hidden">
                <strong className="text-texto">{usuario.name || usuario.email}</strong>
              </span>
              <button type="button" className="botao-perigo !py-1.5" onClick={async () => { await sair(); navegar('/login', { replace: true }); }}>Sair</button>
            </div>
          </div>
          {/*
            O erro de uma tela para naquela tela. Antes o limite vivia só na raiz do App e
            levava junto o menu e o cabeçalho: quem caía em /pedidos ficava numa página
            branca com uma faixa vermelha, sem como navegar para lugar nenhum a não ser
            recarregando. A `key` é a rota porque limite de erro do React não se recupera
            sozinho — sem ela, o erro de uma tela continuaria na tela seguinte.
          */}
          <LimiteErro key={pathname}>
            <Outlet />
          </LimiteErro>
        </main>
      </div>
    </AvisosProvider>
  );
}
