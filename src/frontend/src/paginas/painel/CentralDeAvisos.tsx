import { Link } from 'react-router-dom';
import { CLASSE_AVISO, type Aviso } from '@/api/painel';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { enderecoDoId } from '@/layout/menu';
import { useAvisos } from '@/sessao/AvisosProvider';

function Cartao({ aviso }: { aviso: Aviso }) {
  const conteudo = (
    <>
      <span>{aviso.text}</span>
      <span className="ml-3 shrink-0 rounded-full bg-white/70 px-2.5 py-0.5 text-[13px] font-bold">{aviso.count}</span>
    </>
  );
  const classe = 'flex items-center justify-between rounded-lg border px-4 py-3 text-[13.5px] '
    + 'transition-opacity hover:opacity-80 ' + (CLASSE_AVISO[aviso.severity] ?? CLASSE_AVISO.info);

  return (
    <Link to={enderecoDoId(aviso.view)} className={classe} data-aviso={aviso.kind}>{conteudo}</Link>
  );
}

export function CentralDeAvisos() {
  // os avisos vêm do provedor: o menu e a trilha leem a mesma consulta
  const { avisos, erro, carregando, carregou } = useAvisos();

  return (
    <Painel titulo="Central de Avisos">
      {erro && <Erro>{erro}</Erro>}
      {carregando && !carregou && <Carregando texto="Carregando avisos…" />}
      {carregou && !avisos.length && <Vazio>Tudo em dia por aqui: nenhum aviso pendente. ✔</Vazio>}
      {avisos.length > 0 && (
        <div className="flex flex-col gap-2" data-testid="lista-avisos">
          {avisos.map((a) => <Cartao key={a.kind} aviso={a} />)}
        </div>
      )}
    </Painel>
  );
}
