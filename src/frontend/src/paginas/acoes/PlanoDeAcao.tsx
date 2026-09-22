import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  criarPlano, listarPlanos, ROTULO_SITUACAO,
  type Plano, type SituacaoDoPlano,
} from '@/api/acoes';
import { Badge, Carregando, Erro, FaixaKpis, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data as formatarData, moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { useDebounce } from '@/util/useDebounce';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * A cor da linha. Atrasado fala mais alto que a situação: é o que precisa de decisão hoje.
 * Encerrado é cinza — ele diz que alguém parou de acompanhar, não que deu certo.
 */
export function tomDaLinha(p: Plano): string {
  if (p.life === 'ENCERRADO') return 'bg-slate-100 text-slate-600';
  if (p.status === 'ATRASADO') return 'bg-perigo-fundo text-perigo';
  if (p.status === 'CONCLUIDO') return 'bg-ok-fundo text-ok';
  if (p.status === 'CANCELADO') return 'bg-slate-100 text-slate-500';
  return 'bg-slate-100 text-texto-suave';
}

/**
 * O rótulo da linha. O plano encerrado diz "encerrado", e não a situação do trabalho: a
 * pergunta que interessa nele é outra — alguém decidiu parar, e quando.
 */
export function rotuloDaLinha(p: Plano): string {
  if (p.life === 'ENCERRADO') return p.closedWithPending ? 'Encerrado com pendência' : 'Encerrado';
  return ROTULO_SITUACAO[p.status as SituacaoDoPlano] ?? p.status;
}

export function PlanoDeAcaoTela() {
  const { avisar } = useToast();
  const [busca, setBusca] = useState('');
  const [status, setStatus] = useState('');
  const [prioridade, setPrioridade] = useState('');
  const [centroCusto, setCentroCusto] = useState('');
  const [responsavel, setResponsavel] = useState('');
  const [encerrados, setEncerrados] = useState(false);
  const [titulo, setTitulo] = useState('');
  const [problema, setProblema] = useState('');
  const [porque, setPorque] = useState('');
  const [salvando, setSalvando] = useState(false);

  const termo = useDebounce(busca);
  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarPlanos(
      { busca: termo, status, prioridade, centroCusto, responsavel, encerrados: encerrados || undefined },
      signal),
    [termo, status, prioridade, centroCusto, responsavel, encerrados],
  );

  async function abrir(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      await criarPlano({ title: titulo, problem: problema || null, businessReason: porque || null });
      avisar('Plano aberto — agora inclua as ações que o realizam.');
      setTitulo(''); setProblema(''); setPorque('');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao abrir o plano.'), 'erro'); }
    finally { setSalvando(false); }
  }

  const limpar = () => {
    setBusca(''); setStatus(''); setPrioridade(''); setCentroCusto('');
    setResponsavel(''); setEncerrados(false);
  };
  const temFiltro = !!(busca || status || prioridade || centroCusto || responsavel || encerrados);

  return (
    <>
      <Painel titulo="Planos de ação">
        <Nota>
          O plano é o <b>projeto</b>; as ações são o como. Cada plano diz qual problema ataca,
          por quê, quanto custa e o que se aprendeu — e é isso que o separa de uma lista de tarefas.
        </Nota>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && (
          <FaixaKpis>
            <Kpi rotulo="Pendentes" valor={dados.placar.pendentes} />
            <Kpi rotulo="Em andamento" valor={dados.placar.emAndamento} />
            <Kpi rotulo="Atrasados" valor={dados.placar.atrasados} />
            <Kpi rotulo="Concluídos" valor={dados.placar.concluidos} />
            <Kpi rotulo="Encerrados" valor={dados.placar.encerrados}
              detalhe={dados.placar.encerradosComPendencia > 0
                ? `${dados.placar.encerradosComPendencia} com ação em aberto`
                : undefined} />
            <Kpi rotulo="Saving realizado" valor={moeda(dados.placar.savingRealizado)}
              detalhe={`de ${moeda(dados.placar.savingEsperado)} esperado`} />
          </FaixaKpis>
        )}

        <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Campo id="ap-busca" rotulo="Buscar">
            <input id="ap-busca" value={busca} placeholder="código ou título"
              onChange={(e) => setBusca(e.target.value)} />
          </Campo>
          <Campo id="ap-status" rotulo="Situação">
            <select id="ap-status" value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">Todas</option>
              {(dados?.filterOptions.statuses ?? []).map((s) => (
                <option key={s} value={s}>{ROTULO_SITUACAO[s as SituacaoDoPlano] ?? s}</option>
              ))}
            </select>
          </Campo>
          <Campo id="ap-prioridade" rotulo="Prioridade">
            <select id="ap-prioridade" value={prioridade} onChange={(e) => setPrioridade(e.target.value)}>
              <option value="">Todas</option>
              {(dados?.filterOptions.priorities ?? []).map((p) => <option key={p} value={p}>{p}</option>)}
            </select>
          </Campo>
          <Campo id="ap-responsavel" rotulo="Responsável">
            <select id="ap-responsavel" value={responsavel} onChange={(e) => setResponsavel(e.target.value)}>
              <option value="">Todos</option>
              {(dados?.filterOptions.responsibles ?? []).map((r) => (
                <option key={r.id} value={r.id}>{r.label}</option>
              ))}
            </select>
          </Campo>
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <label className="flex items-center gap-2 text-[13px]">
            <input type="checkbox" checked={encerrados}
              onChange={(e) => setEncerrados(e.target.checked)} />
            Mostrar só os encerrados
          </label>
          {temFiltro && (
            <button type="button" className="botao-secundario" onClick={limpar}>Limpar</button>
          )}
        </div>

        {dados && !dados.items.length && (
          <Vazio>Nenhum plano por aqui — abra o primeiro abaixo, com o problema que ele ataca.</Vazio>
        )}
        {dados && dados.items.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-planos">
              <thead>
                <tr>
                  <th>Código</th><th>Plano</th><th>Responsáveis</th><th>Prazo</th>
                  <th>Ações</th><th>Avanço</th><th>Situação</th>
                </tr>
              </thead>
              <tbody>
                {dados.items.map((p) => (
                  <tr key={p.id} data-plano={p.code}>
                    <td className="whitespace-nowrap font-semibold">
                      <Link to={`/plano-acao/${p.id}`} className="font-semibold text-marca hover:underline">
                        {p.code}
                      </Link>
                    </td>
                    <td>
                      {p.title}
                      {p.problem && <div className="sub">{p.problem}</div>}
                    </td>
                    <td className="sub">
                      {p.responsibles.map((r) => r.label).join(' · ') || '—'}
                    </td>
                    <td className="sub whitespace-nowrap">{formatarData(p.dueDate)}</td>
                    <td className="sub whitespace-nowrap">
                      {p.itemCount === 0 ? '—' : `${p.itemCount - p.openItems}/${p.itemCount}`}
                    </td>
                    <td className="tabular-nums">{p.itemsProgress}%</td>
                    <td><Badge classe={tomDaLinha(p)}>{rotuloDaLinha(p)}</Badge></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      <Painel titulo="Abrir um plano">
        <form onSubmit={abrir}>
          <Campo id="ap-titulo" rotulo="O que o plano vai resolver">
            <input id="ap-titulo" required minLength={5} value={titulo}
              onChange={(e) => setTitulo(e.target.value)} />
          </Campo>
          {/* o problema e o porquê já entram aqui: plano sem os dois vira lista de tarefas,
              e é exatamente o que este módulo existe para não ser */}
          <Grade2 className="mt-3">
            <Campo id="ap-problema" rotulo="Problema encontrado">
              <textarea id="ap-problema" rows={2} value={problema}
                onChange={(e) => setProblema(e.target.value)} />
            </Campo>
            <Campo id="ap-porque" rotulo="Por quê" dica="(a justificativa de negócio)">
              <textarea id="ap-porque" rows={2} value={porque}
                onChange={(e) => setPorque(e.target.value)} />
            </Campo>
          </Grade2>
          <button type="submit" className="botao mt-4" disabled={salvando}>
            {salvando ? 'Abrindo…' : 'Abrir plano'}
          </button>
        </form>
      </Painel>
    </>
  );
}
