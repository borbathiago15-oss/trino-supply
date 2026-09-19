import { Link } from 'react-router-dom';
import type { EtapaDoCaminho } from '@/api/cotacoes';
import { dataHora } from '@/util/formato';

const MARCA: Record<EtapaDoCaminho['situacao'], string> = {
  feita: '✓', atual: '●', pendente: '○', encerrada: '■',
};

/**
 * A linha de baixo de cada etapa: quem e quando, dito conforme a situação.
 *
 * Na etapa atual o "desde" é o que transforma espera em cobrança; na pendente,
 * o nome é o que permite avisar o aprovador antes de a bola chegar.
 */
export function legenda(e: EtapaDoCaminho): string {
  switch (e.situacao) {
    case 'feita':
      return [e.quem ?? '—', e.em ? dataHora(e.em) : null].filter(Boolean).join(' · ');
    case 'atual':
      return `aguardando ${e.quem ?? '—'}${e.em ? ` desde ${dataHora(e.em)}` : ''}`;
    case 'pendente':
      return e.quem ? `a seguir · ${e.quem}` : 'a seguir';
    case 'encerrada':
      return '';
  }
}

/**
 * O caminho do processo inteiro: quem pediu, o que já aconteceu, de quem se
 * espera agora e o que ainda falta.
 *
 * O "Próximo passo" acima diz o que fazer e leva até lá; este bloco responde
 * o que ficava sem resposta — "aguardando aprovador" de **quem**? O sistema
 * já sabia (os aprovadores estão no centro de custo, o solicitante na SC), só
 * não dizia nesta tela.
 */
export function CaminhoDoProcesso({ etapas, centro }: { etapas: EtapaDoCaminho[]; centro?: string }) {
  if (etapas.length === 0) return null;
  // um link só, mesmo que os dois níveis estejam vazios: o conserto é o mesmo cadastro
  const semAprovador = etapas.some((e) => e.semAprovador);

  return (
    <div data-testid="caminho-do-processo" className="mt-3 rounded-lg border border-marca/25 px-4 py-3">
      <p className="text-[11.5px] font-bold uppercase tracking-wide text-texto-suave">Caminho do processo</p>
      <ol className="mt-2 grid gap-x-6 gap-y-1.5 md:grid-cols-2">
        {etapas.map((e) => (
          <li key={e.chave} data-etapa={e.chave} data-situacao={e.situacao}
            className={`flex items-start gap-2 text-[13px] ${
              e.situacao === 'atual' ? 'font-bold' : e.situacao === 'pendente' ? 'text-texto-suave' : ''}`}>
            <span aria-hidden className="w-4 shrink-0 text-center">{MARCA[e.situacao]}</span>
            <div className="min-w-0">
              <div>{e.titulo}</div>
              {legenda(e) && <div className="sub font-normal">{legenda(e)}</div>}
            </div>
          </li>
        ))}
      </ol>
      {semAprovador && (
        <p className="mt-2 text-[12.5px]">
          Este processo não anda enquanto o centro não tiver aprovador.{' '}
          <Link to="/centros-custo" className="font-semibold underline">
            Cadastrar aprovadores{centro ? ` do ${centro}` : ''} →
          </Link>
        </p>
      )}
    </div>
  );
}
