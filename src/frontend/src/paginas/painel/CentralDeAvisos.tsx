import type { JSX } from 'react';
import { Link } from 'react-router-dom';
import { CLASSE_AVISO, type Aviso } from '@/api/painel';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { enderecoDoId } from '@/layout/menu';
import { useAvisos } from '@/sessao/AvisosProvider';

const ICONE: Record<Aviso['severity'], JSX.Element> = {
  alta: (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
      <path d="M12 9v4" /><path d="M12 17h.01" />
    </svg>
  ),
  media: (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <circle cx="12" cy="12" r="10" /><polyline points="12 6 12 12 16 14" />
    </svg>
  ),
  info: (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <circle cx="12" cy="12" r="10" /><path d="M12 16v-4" /><path d="M12 8h.01" />
    </svg>
  ),
};

/**
 * Cartão de ação: fundo branco, a severidade na borda esquerda e no ícone, o texto limpo,
 * a contagem à direita e o convite para ir até lá. A faixa colorida de ponta a ponta
 * dizia "urgente" gritando; aqui a cor marca e o texto fala.
 */
function Cartao({ aviso }: { aviso: Aviso }) {
  const sev = CLASSE_AVISO[aviso.severity] ? aviso.severity : 'info';
  const classe = 'flex items-center gap-3 rounded-lg border border-l-4 border-slate-200/80 bg-white px-4 py-3 shadow-sm '
    + 'transition-colors hover:bg-slate-50 ' + CLASSE_AVISO[sev];

  return (
    <Link to={enderecoDoId(aviso.view)} className={classe} data-aviso={aviso.kind}>
      <span className="shrink-0">{ICONE[sev]}</span>
      <span className="min-w-0 flex-1 text-[13.5px] font-medium text-slate-700">{aviso.text}</span>
      <span className="shrink-0 rounded-full bg-slate-100 px-2.5 py-0.5 text-[12.5px] font-bold text-slate-700">{aviso.count}</span>
      <span className="hidden shrink-0 text-[12.5px] font-semibold text-marca sm:inline">Ver itens →</span>
    </Link>
  );
}

export function CentralDeAvisos() {
  // os avisos vêm do provedor: o menu e a trilha leem a mesma consulta
  const { avisos, erro, carregando, carregou } = useAvisos();

  return (
    <Painel titulo="Central de Avisos">
      {erro && <Erro>{erro}</Erro>}
      {carregando && !carregou && <Carregando texto="Carregando avisos…" />}
      {carregou && !avisos.length && (
        <Vazio icone="ok" titulo="Tudo em dia por aqui">Nenhum aviso pendente: nada esperando por você agora.</Vazio>
      )}
      {avisos.length > 0 && (
        <div className="flex flex-col gap-2" data-testid="lista-avisos">
          {avisos.map((a) => <Cartao key={a.kind} aviso={a} />)}
        </div>
      )}
    </Painel>
  );
}
