import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { resumoDoSuporte, type ResumoDoSuporte } from '@/api/suporte';
import { PainelDoManual } from '@/manual/PainelDoManual';
import { telaDaRota } from '@/manual/telas';
import { DialogoDeSuporte } from '@/paginas/suporte/DialogoDeSuporte';

/**
 * Manual e Suporte, no topo de toda tela da casca. Moram no cabeçalho, e não em cada página,
 * para nenhuma tela nova nascer sem eles: a tela é descoberta pela rota (`telaDaRota`), e o
 * que se abre é sempre o manual e o chamado **daquela** tela.
 *
 * O terceiro botão leva aos chamados. O número nele é o que é a sua vez: chamado seu que o
 * suporte respondeu e, para quem atende, o que espera na fila. Contar "abertos" diria quanto
 * existe, não o que precisa de você. O ícone é o balão, desenhado aqui e não emoji (que depende da fonte e saía apagado), e não as três linhas: no celular elas
 * já são o botão do menu, e dois botões iguais lado a lado fariam adivinhar qual é qual.
 */
export function BarraDeAjuda() {
  const { pathname } = useLocation();
  const tela = telaDaRota(pathname);
  const [aberto, setAberto] = useState<'manual' | 'suporte' | null>(null);
  const [resumo, setResumo] = useState<ResumoDoSuporte | null>(null);

  // relido a cada troca de tela: é barato, e é quando a pessoa olha o cabeçalho de novo
  useEffect(() => {
    const ctl = new AbortController();
    resumoDoSuporte(ctl.signal).then(setResumo).catch(() => { /* o número some; o botão fica */ });
    return () => ctl.abort();
  }, [pathname, aberto]);

  const pendentes = (resumo?.paraMim ?? 0) + (resumo?.fila ?? 0);
  const dicaDosChamados = !resumo ? 'Chamados de suporte'
    : [resumo.paraMim ? `${resumo.paraMim} resposta(s) esperando você` : null,
      resumo.fila ? `${resumo.fila} na fila do suporte` : null].filter(Boolean).join(' · ') || 'Chamados de suporte';

  return (
    <>
      <div className="flex items-center gap-1.5">
        <button type="button" className="botao-secundario !px-2.5 !py-1.5" onClick={() => setAberto('manual')}
          title={`Manual — ${tela.rotulo}`}>
          <span aria-hidden="true">?</span><span className="ml-1 hidden sm:inline">Manual</span>
          <span className="sr-only sm:hidden">Manual</span>
        </button>
        <button type="button" className="botao-secundario !px-2.5 !py-1.5" onClick={() => setAberto('suporte')}
          title={`Abrir chamado sobre ${tela.rotulo}`}>
          <span aria-hidden="true">✉</span><span className="ml-1 hidden sm:inline">Suporte</span>
          <span className="sr-only sm:hidden">Suporte</span>
        </button>
        <Link to="/suporte" className="botao-secundario relative !px-2.5 !py-1.5" title={dicaDosChamados}
          aria-label={pendentes ? `Meus chamados — ${dicaDosChamados}` : 'Meus chamados'}>
          <svg aria-hidden="true" viewBox="0 0 20 20" className="h-[15px] w-[15px]" fill="none" stroke="currentColor" strokeWidth="1.8">
            <path strokeLinejoin="round" d="M3.5 4.5h13v8.5h-7l-4 3v-3h-2z" />
          </svg>
          {pendentes > 0 && (
            <span data-testid="chamados-pendentes"
              className="absolute -right-1.5 -top-1.5 rounded-full bg-perigo px-1.5 text-[10.5px] font-bold leading-[18px] text-white">
              {pendentes > 99 ? '99+' : pendentes}
            </span>
          )}
        </Link>
      </div>

      {aberto === 'manual' && (
        <PainelDoManual tela={tela} aoFechar={() => setAberto(null)} aoPedirSuporte={() => setAberto('suporte')} />
      )}
      {aberto === 'suporte' && (
        <DialogoDeSuporte tela={tela} rota={pathname} aoFechar={() => setAberto(null)} />
      )}
    </>
  );
}
