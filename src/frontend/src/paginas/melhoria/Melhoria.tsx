import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  criarCiclo, listarCiclos, type Ciclo, type PlacarDosCiclos,
} from '@/api/melhoria';
import { Badge, Carregando, Erro, FaixaKpis, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data as formatarData } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { useDebounce } from '@/util/useDebounce';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * A cor da fase. Encerrado é cinza e não verde: ele diz que a decisão foi tomada, não que ela
 * foi boa — quem responde isso é o veredito da meta, que a linha mostra ao lado.
 */
export function tomDaFase(c: Ciclo): string {
  if (c.phase === 'ENCERRADO') return 'bg-slate-100 text-slate-600';
  if (c.phase === 'CHECK') return 'bg-aviso-fundo text-aviso';
  return 'bg-marca/10 text-marca';
}

/**
 * O que a linha diz do veredito. Nulo é "sem veredito" e não "não atingiu": o ciclo em
 * andamento ainda não respondeu a pergunta, e tratá-lo como reprovado seria inventar a resposta.
 */
export function vereditoDaLinha(c: Ciclo): string | null {
  if (c.goalMet === null) return null;
  return c.goalMet ? 'Meta atingida' : 'Meta não atingida';
}

export function Melhoria() {
  const { avisar } = useToast();
  const [busca, setBusca] = useState('');
  const [fase, setFase] = useState('');
  const [escopo, setEscopo] = useState('');
  const [titulo, setTitulo] = useState('');
  const [novoEscopo, setNovoEscopo] = useState('GESTAO');
  const [salvando, setSalvando] = useState(false);

  const termo = useDebounce(busca);
  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarCiclos({ q: termo, phase: fase, scope: escopo }, signal),
    [termo, fase, escopo],
  );

  async function abrir(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      await criarCiclo({ title: titulo, scope: novoEscopo });
      avisar('Ciclo aberto — comece pelo Plan: o problema, a causa e a meta.');
      setTitulo('');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao abrir o ciclo.'), 'erro'); }
    finally { setSalvando(false); }
  }

  const placar: PlacarDosCiclos | undefined = dados?.placar;

  return (
    <>
      {/* porta única: o placar e a lista na mesma tela. Dois menus obrigariam o gestor a
          saber em qual entrar antes de saber o que ele quer fazer */}
      <Painel titulo="Ciclos de melhoria (PDCA)">
        <Nota>
          O ciclo é onde se trata a <b>causa</b>; o plano de ação é onde se trata a <b>tarefa</b>.
          Prazo vencido não encerra ciclo nenhum — quem encerra é uma pessoa, com veredito e motivo.
        </Nota>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {placar && (
          <FaixaKpis>
            <Kpi rotulo="Plan" valor={placar.plan} />
            <Kpi rotulo="Do" valor={placar.do} />
            <Kpi rotulo="Check" valor={placar.check} />
            <Kpi rotulo="Act" valor={placar.act} />
            <Kpi rotulo="Encerrados" valor={placar.encerrados}
              detalhe={placar.encerradosComPendencia > 0
                ? `${placar.encerradosComPendencia} com ação em aberto`
                : undefined} />
          </FaixaKpis>
        )}

        <div className="mt-3 flex flex-wrap items-end gap-2">
          <Campo id="pdca-busca" rotulo="Buscar">
            <input id="pdca-busca" value={busca} placeholder="código ou título"
              onChange={(e) => setBusca(e.target.value)} />
          </Campo>
          <Campo id="pdca-fase" rotulo="Fase">
            <select id="pdca-fase" value={fase} onChange={(e) => setFase(e.target.value)}>
              <option value="">Todas</option>
              {dados?.options.phases.map((f) => <option key={f.key} value={f.key}>{f.label}</option>)}
            </select>
          </Campo>
          <Campo id="pdca-escopo" rotulo="Escopo">
            <select id="pdca-escopo" value={escopo} onChange={(e) => setEscopo(e.target.value)}>
              <option value="">Todos</option>
              {dados?.options.scopes.map((s) => <option key={s.key} value={s.key}>{s.label}</option>)}
            </select>
          </Campo>
          {(busca || fase || escopo) && (
            <button type="button" className="botao-secundario"
              onClick={() => { setBusca(''); setFase(''); setEscopo(''); }}>Limpar</button>
          )}
        </div>

        {dados && !dados.items.length && (
          <Vazio>Nenhum ciclo por aqui — abra o primeiro abaixo, com o problema que você quer atacar.</Vazio>
        )}
        {dados && dados.items.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-ciclos">
              <thead>
                <tr><th>Código</th><th>Título</th><th>Escopo</th><th>Fase</th><th>Meta</th><th>Dono</th></tr>
              </thead>
              <tbody>
                {dados.items.map((c) => (
                  <tr key={c.id} data-ciclo={c.code}>
                    <td className="whitespace-nowrap font-semibold">
                      <Link to={`/melhoria/${c.id}`} className="font-semibold text-marca hover:underline">{c.code}</Link>
                    </td>
                    <td>
                      {c.title}
                      {c.indicator && <div className="sub">{c.indicator}</div>}
                    </td>
                    <td className="sub">{c.scopeLabel}</td>
                    <td><Badge classe={tomDaFase(c)}>{c.phaseLabel}</Badge></td>
                    <td className="sub whitespace-nowrap">
                      {vereditoDaLinha(c) ?? (c.goalDeadline ? `até ${formatarData(c.goalDeadline)}` : '—')}
                    </td>
                    <td className="sub">{c.ownerLabel ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      <Painel titulo="Abrir um ciclo">
        <form onSubmit={abrir}>
          <Grade2>
            <Campo id="pdca-titulo" rotulo="Título"
              dica="uma frase, não um rótulo: &quot;Reduzir avarias na doca 2&quot;, não &quot;Avarias&quot;">
              <input id="pdca-titulo" required minLength={10} value={titulo}
                onChange={(e) => setTitulo(e.target.value)} />
            </Campo>
            <Campo id="pdca-novo-escopo" rotulo="Escopo do ciclo">
              <select id="pdca-novo-escopo" value={novoEscopo}
                onChange={(e) => setNovoEscopo(e.target.value)}>
                {(dados?.options.scopes ?? []).map((s) => (
                  <option key={s.key} value={s.key}>{s.label}</option>
                ))}
              </select>
            </Campo>
          </Grade2>
          <button type="submit" className="botao mt-4" disabled={salvando}>
            {salvando ? 'Abrindo…' : 'Abrir ciclo'}
          </button>
        </form>
      </Painel>
    </>
  );
}
