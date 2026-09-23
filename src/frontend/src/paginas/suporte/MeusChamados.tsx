import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { listarChamados, resumoDoSuporte, ROTULO_CATEGORIA, type SituacaoDoChamado } from '@/api/suporte';
import { dataHora } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { rotuloDaSituacao, TOM_DA_SITUACAO } from './situacao';

type Aba = 'meus' | 'fila';

/**
 * Os chamados da pessoa e, para quem atende, a fila de todos — na mesma tela, porque o
 * atendente também abre chamado, e duas telas para "chamados" fariam procurar nos dois lugares.
 */
export function MeusChamados() {
  const [aba, setAba] = useState<Aba>('meus');
  const [situacao, setSituacao] = useState<SituacaoDoChamado | ''>('');
  const resumo = useCarregar((s) => resumoDoSuporte(s), []);
  const lista = useCarregar((s) => listarChamados(aba, situacao || undefined, s), [aba, situacao]);
  const atende = !!resumo.dados?.atende;
  const naFila = aba === 'fila';

  return (
    <div className="space-y-4">
      {atende && (
        <div role="tablist" className="flex gap-2">
          {(['meus', 'fila'] as Aba[]).map((a) => (
            <button key={a} type="button" role="tab" aria-selected={aba === a}
              className={aba === a ? 'botao' : 'botao-secundario'} onClick={() => setAba(a)}>
              {a === 'meus' ? 'Meus chamados' : `Fila do suporte${resumo.dados?.fila ? ` (${resumo.dados.fila})` : ''}`}
            </button>
          ))}
        </div>
      )}

      <Painel titulo={naFila ? 'Fila do suporte' : 'Meus chamados'} acoes={
        <label className="flex items-center gap-2 text-[13px]">
          <span className="text-texto-suave">Situação</span>
          <select aria-label="Situação" value={situacao} onChange={(e) => setSituacao(e.target.value as SituacaoDoChamado | '')}>
            <option value="">Todas</option>
            <option value="AGUARDANDO_SUPORTE">{naFila ? 'Aguardando o suporte' : 'Com o suporte'}</option>
            <option value="AGUARDANDO_USUARIO">{naFila ? 'Aguardando quem abriu' : 'Aguardando você'}</option>
            <option value="RESOLVIDO">Resolvido</option>
          </select>
        </label>
      }>
        {lista.erro && <Erro>{lista.erro}</Erro>}
        {lista.carregando && !lista.dados && <Carregando />}
        {lista.dados && lista.dados.length === 0 && (
          <Vazio titulo={naFila ? 'Fila limpa' : 'Nenhum chamado'} icone={naFila ? 'ok' : 'caixa'}>
            {naFila ? 'Nenhum chamado neste recorte.'
              : 'Para pedir ajuda, use o botão "Suporte" no topo de qualquer tela — o chamado já vai com o nome da tela.'}
          </Vazio>
        )}
        {!!lista.dados?.length && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-chamados">
              <thead>
                <tr>
                  <th>Chamado</th><th>Assunto</th><th>Tela</th>
                  {naFila && <th>Quem abriu</th>}
                  <th>Situação</th><th>Atualizado</th>
                </tr>
              </thead>
              <tbody>
                {lista.dados.map((c) => (
                  <tr key={c.id}>
                    <td className="whitespace-nowrap font-semibold"><Link to={`/suporte/${c.id}`}>{c.number}</Link></td>
                    <td>
                      {c.subject}
                      <div className="sub">{ROTULO_CATEGORIA[c.category]}{c.assignedToLabel ? ` · com ${c.assignedToLabel}` : ''}</div>
                    </td>
                    <td className="whitespace-nowrap">{c.screenLabel}</td>
                    {naFila && <td className="whitespace-nowrap">{c.createdByLabel}</td>}
                    <td><Badge classe={TOM_DA_SITUACAO[c.status]}>{rotuloDaSituacao(c, naFila)}</Badge></td>
                    <td className="whitespace-nowrap">{dataHora(c.updatedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>
    </div>
  );
}
