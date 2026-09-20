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
      // no impasse, "aguardando fulano" seria mentira: fulano está impedido de agir
      return `${e.impasse ? `impasse · ${e.quem ?? '—'} não pode aprovar` : `aguardando ${e.quem ?? '—'}`}${
        e.em ? ` desde ${dataHora(e.em)}` : ''}`;
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
  // O caso real que motivou isto: o administrador é o único aprovador do centro E escolheu o
  // fornecedor. "Aguardando Administrador" apontava para alguém que não pode agir, e a tela não
  // dizia quem pode — outro administrador, ou outra pessoa cadastrada no nível.
  const impasse = etapas.find((e) => e.situacao === 'atual' && e.impasse);

  return (
    <div data-testid="caminho-do-processo" className="mt-3 rounded-lg border border-marca/25 px-4 py-3">
      <p className="rotulo">Caminho do processo</p>
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
      {impasse && (
        <p data-testid="impasse-da-alcada" className="mt-2 rounded-lg bg-aviso-fundo px-3 py-2 text-[12.5px] text-aviso">
          <strong>Ninguém da lista pode dar {impasse.chave === 'nivel2' ? 'o Nível 2' : 'o Nível 1'}.</strong>{' '}
          {impasse.quem} {impasse.chave === 'nivel2'
            ? 'escolheu o fornecedor ou deu o Nível 1, e quem já agiu no processo não aprova a etapa seguinte'
            : 'escolheu o fornecedor, e quem escolhe não aprova a própria escolha'} (RFQ-ERR-030).
          Quem destrava: outro administrador pode aprovar na Central de Aprovação, ou{' '}
          <Link to="/centros-custo" className="font-semibold underline">
            cadastre outra pessoa {impasse.chave === 'nivel2' ? 'no Nível 2' : 'no Nível 1'}{centro ? ` do ${centro}` : ''} →
          </Link>
        </p>
      )}
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
