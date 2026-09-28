import { useEffect, useRef } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useToast } from '@/componentes/Toast';

/** As três telas do app, pela rota do sistema → pela rota do app. */
const TELAS_DO_APP: Record<string, string> = {
  '/aprovacoes': '/app/aprovacoes',
  '/diretoria': '/app/diretoria',
  '/painel': '/app/painel',
};

/** A casca do app e as três abas — e só elas: `/app/fornecedores` de um favorito antigo não é o app. */
export const ehTelaDoApp = (pathname: string) => pathname === '/app' || Object.values(TELAS_DO_APP).includes(pathname);

/** A rota do app que corresponde a uma rota do sistema, ou nulo quando a tela não existe no app. */
export const rotaNoApp = (pathname: string): string | null => TELAS_DO_APP[pathname] ?? null;

export const AVISO_FORA_DO_APP = 'O app de bolso tem só Aprovar, Diretoria e Dashboard. Para esta tela, use o sistema completo.';

/**
 * O app de bolso não tem saída para o sistema completo. As três telas são as mesmas páginas
 * React do sistema, e os links delas apontam para as rotas do sistema ("Decidir →" vai a
 * `/aprovacoes`, "Ver processo completo" a `/cotacoes/:id`): sem este guarda, um toque levava a
 * pessoa para fora da casca, e o app inteiro virava o sistema com menu, cadastros e tudo. O
 * guarda mora no roteador, acima das rotas, porque a casca desmonta no instante em que a rota
 * sai de `/app` — de dentro dela não há como segurar a saída.
 *
 * A régua: navegação que **vinha** de uma tela do app e **sairia** dela é reescrita para a rota
 * do app quando a tela existe lá, e devolvida à tela de origem, com o aviso, quando não existe.
 * Sair da sessão continua livre. Um endereço digitado ou um favorito recarrega a página, e a
 * memória do guarda nasce zerada — por isso ele não prende quem abriu o sistema de propósito.
 */
export function EscopoDoApp() {
  const { pathname, search } = useLocation();
  const navegar = useNavigate();
  const { avisar } = useToast();
  const anterior = useRef(pathname);

  useEffect(() => {
    const origem = anterior.current;
    if (ehTelaDoApp(origem) && !ehTelaDoApp(pathname) && pathname !== '/login') {
      const dentro = rotaNoApp(pathname);
      if (dentro) { navegar(dentro + search, { replace: true }); return; }
      navegar(origem, { replace: true });
      avisar(AVISO_FORA_DO_APP, 'erro');
      return;
    }
    anterior.current = pathname;
  }, [pathname, search, navegar, avisar]);

  return null;
}
