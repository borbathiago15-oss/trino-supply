import { useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import {
  abrirCiclo, encerrarCiclo, EncerramentoPendente, reabrirCiclo, salvarCiclo,
  type AcaoDoCiclo, type CicloCompleto, type DadosDoCiclo, type Leitura, type Sinal,
} from '@/api/melhoria';
import { Badge, Carregando, Dado, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data as formatarData } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { FerramentaRenderizada, Vital } from './ferramenta';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

const FASES = ['PLAN', 'DO', 'CHECK', 'ACT'] as const;

/** O mínimo que o servidor aceita no motivo — a tela cobra antes de o servidor recusar. */
export const MINIMO_DO_MOTIVO = 20;

export function tomDoSinal(s: Sinal): string {
  if (s.severidade === 'risco') return 'bg-perigo-fundo text-perigo';
  if (s.severidade === 'atencao') return 'bg-aviso-fundo text-aviso';
  return 'bg-slate-100 text-texto-suave';
}

/** A trilha das fases. Encerrado não está nela: ele é veredito, e tem botão próprio. */
function Trilha({ fase, aoTrocar, travada }: {
  fase: string; aoTrocar: (f: string) => void; travada: boolean;
}) {
  return (
    <div className="flex flex-wrap gap-1.5" data-testid="trilha-fases">
      {FASES.map((f) => (
        <button key={f} type="button" disabled={travada}
          className={f === fase ? 'botao' : 'botao-secundario'}
          onClick={() => aoTrocar(f)}>{f}</button>
      ))}
    </div>
  );
}

/** A leitura automática: a mesma que o servidor devolve, sem recalcular nada aqui. */
function LeituraAutomatica({ leitura }: { leitura: Leitura }) {
  return (
    <div data-testid="leitura">
      <p className="text-[13.5px] font-semibold">{leitura.proximoPasso}</p>
      <ul className="mt-2 space-y-0.5 text-[13px] text-texto-suave">
        {leitura.frases.map((f) => <li key={f}>{f}</li>)}
      </ul>
      {leitura.sinais.length > 0 && (
        <div className="mt-3 space-y-1.5">
          {leitura.sinais.map((s) => (
            <div key={s.chave + s.texto} className={'rounded-lg px-3 py-2 text-[12.5px] ' + tomDoSinal(s)}>
              {s.texto}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function ListaDeAcoes({ acoes }: { acoes: AcaoDoCiclo[] }) {
  if (!acoes.length) return <Vazio>Nenhuma ação ligada a este ciclo ainda.</Vazio>;
  return (
    <div className="mt-2 overflow-x-auto">
      <table data-testid="acoes-do-ciclo">
        <thead><tr><th>Ação</th><th>Responsável</th><th>Prazo</th><th>Causa que ataca</th><th>Situação</th></tr></thead>
        <tbody>
          {acoes.map((a) => (
            <tr key={a.id} data-acao={a.number}>
              <td><span className="font-semibold">{a.number}</span><div className="sub">{a.title}</div></td>
              <td className="sub">{a.responsibleLabel}</td>
              <td className="sub whitespace-nowrap">{formatarData(a.dueDate)}</td>
              <td className="sub">{a.rootCauseRef ?? '—'}</td>
              <td>
                {/* suspensa e vencida diz "suspensa", nunca "atrasada": ela está parada
                    por decisão, e cobrá-la seria culpar a equipe pela decisão da gestão */}
                <Badge classe={a.late ? 'bg-perigo-fundo text-perigo'
                  : a.open ? 'bg-slate-100 text-texto-suave' : 'bg-ok-fundo text-ok'}>
                  {a.late ? 'Atrasada' : a.status}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function CicloDeMelhoria() {
  const { id = '' } = useParams();
  const { avisar } = useToast();
  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => abrirCiclo(id, signal), [id]);

  if (erro) return <Painel titulo="Ciclo de melhoria"><Erro>{erro}</Erro></Painel>;
  if (carregando && !dados) return <Painel titulo="Ciclo de melhoria"><Carregando /></Painel>;
  if (!dados) return null;

  return <Conteudo completo={dados} recarregar={recarregar} avisar={avisar} />;
}

function Conteudo({ completo, recarregar, avisar }: {
  completo: CicloCompleto;
  recarregar: () => void;
  avisar: (texto: string, tom?: 'ok' | 'erro') => void;
}) {
  const c = completo.cycle;
  const encerrado = c.phase === 'ENCERRADO';
  const [encerrando, setEncerrando] = useState(false);
  const [metaAtingida, setMetaAtingida] = useState<'' | 'sim' | 'nao'>('');
  const [motivo, setMotivo] = useState('');
  const [pendentes, setPendentes] = useState<AcaoDoCiclo[] | null>(null);
  const [salvando, setSalvando] = useState(false);

  async function salvar(dados: DadosDoCiclo, ok: string) {
    setSalvando(true);
    try {
      await salvarCiclo(c.id, dados);
      avisar(ok);
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar o ciclo.'), 'erro'); }
    finally { setSalvando(false); }
  }

  function fecharDialogo() {
    setEncerrando(false);
    setMetaAtingida('');
    setMotivo('');
    setPendentes(null);
  }

  /**
   * O encerramento em dois passos. O primeiro pede o veredito e o motivo; o segundo só
   * aparece quando sobrou ação em aberto — e mostra <b>quais</b>, porque pedir a confirmação
   * sem dizer de quê seria pedir uma assinatura em branco.
   */
  async function encerrar(ev: FormEvent) {
    ev.preventDefault();
    if (metaAtingida === '') return;
    setSalvando(true);
    try {
      await encerrarCiclo(c.id, {
        goalMet: metaAtingida === 'sim',
        reason: motivo,
        confirmPending: pendentes !== null,
      });
      avisar('Ciclo encerrado, com o veredito e o motivo registrados.');
      fecharDialogo();
      recarregar();
    } catch (e) {
      if (e instanceof EncerramentoPendente) setPendentes(e.detalhe.pending);
      else avisar(mensagem(e, 'Falha ao encerrar o ciclo.'), 'erro');
    } finally { setSalvando(false); }
  }

  async function reabrir() {
    try {
      await reabrirCiclo(c.id);
      avisar('Ciclo reaberto — o veredito anterior foi apagado.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao reabrir o ciclo.'), 'erro'); }
  }

  return (
    <>
      <Painel titulo={`${c.code} — ${c.title}`} acoes={
        encerrado
          ? <button type="button" className="botao-secundario" onClick={reabrir}>Reabrir</button>
          : <button type="button" className="botao" onClick={() => setEncerrando(true)}>Encerrar ciclo</button>
      }>
        <div className="flex flex-wrap items-center gap-2">
          <Badge classe={encerrado ? 'bg-slate-100 text-slate-600' : 'bg-marca/10 text-marca'}>
            {c.phaseLabel}
          </Badge>
          <Badge classe="bg-slate-100 text-texto-suave">{c.scopeLabel}</Badge>
          {completo.sectorName && <Badge classe="bg-slate-100 text-texto-suave">{completo.sectorName}</Badge>}
          {c.goalMet !== null && (
            <Badge classe={c.goalMet ? 'bg-ok-fundo text-ok' : 'bg-perigo-fundo text-perigo'}>
              {c.goalMet ? 'Meta atingida' : 'Meta não atingida'}
            </Badge>
          )}
        </div>
        <Grade2 className="mt-3">
          <Dado rotulo="Dono">{c.ownerLabel ?? '—'}</Dado>
          <Dado rotulo="Prazo da meta">{formatarData(c.goalDeadline)}</Dado>
        </Grade2>
        {!encerrado && (
          <div className="mt-3">
            <Trilha fase={c.phase} travada={salvando}
              aoTrocar={(f) => salvar({ phase: f }, `Ciclo em ${f}.`)} />
            <Nota>Encerrar não é uma fase da trilha: é o veredito, e tem botão próprio.</Nota>
          </div>
        )}
        {encerrado && c.closedReason && (
          <div className="mt-3 rounded-lg bg-superficie-suave p-3 text-[13px]" data-testid="veredito">
            <b>Encerrado por {c.closedByLabel}:</b>
            <p className="mt-1 whitespace-pre-line">{c.closedReason}</p>
          </div>
        )}
      </Painel>

      <Painel titulo="Leitura automática">
        <LeituraAutomatica leitura={completo.reading} />
      </Painel>

      <Painel titulo="Plan — o problema, a causa e a meta">
        <Grade2>
          <Dado rotulo="Problema">{completo.plan.problem ?? '—'}</Dado>
          <Dado rotulo="Situação atual">{completo.plan.currentSituation ?? '—'}</Dado>
        </Grade2>
        <Grade2 className="mt-3">
          <Dado rotulo="Causa raiz">{completo.plan.rootCause ?? '—'}</Dado>
          <Dado rotulo="Meta">{completo.plan.goalDescription ?? '—'}</Dado>
        </Grade2>
        {completo.analysis && (
          <div className="mt-4">
            <h3 className="text-[13.5px] font-bold">{completo.analysis.name}</h3>
            <FerramentaRenderizada analise={completo.analysis} />
            {completo.reading.causas.some((x) => x.vital && x.acoes === 0) && (
              <Nota>
                <Vital /> sem ação apontando para ela — a leitura acima diz qual.
              </Nota>
            )}
          </div>
        )}
      </Painel>

      <Painel titulo="Do — as ações que atacam a causa">
        <ListaDeAcoes acoes={completo.actions} />
        <Nota>
          As ações vivem no Plano de Ação: elas são as mesmas, e apontam para este ciclo.
        </Nota>
      </Painel>

      <Painel titulo="Check e Act">
        <Grade2>
          <Dado rotulo="Medido em">{formatarData(completo.check.checkedOn)}</Dado>
          <Dado rotulo="Resultado">
            {c.resultValue === null ? 'ainda sem medição' : `${c.resultValue} ${c.unit ?? ''}`}
          </Dado>
        </Grade2>
        <Grade2 className="mt-3">
          <Dado rotulo="Padronização">{completo.act.standardization ?? '—'}</Dado>
          <Dado rotulo="Lições">{completo.act.lessons ?? '—'}</Dado>
        </Grade2>
      </Painel>

      {encerrando && (
        <Dialogo titulo="Encerrar o ciclo" aoFechar={fecharDialogo} acoes={
          <>
            <button type="button" className="botao-secundario" onClick={fecharDialogo}>Cancelar</button>
            <button type="submit" form="form-encerrar" className="botao"
              disabled={salvando || metaAtingida === '' || motivo.trim().length < MINIMO_DO_MOTIVO}>
              {pendentes ? 'Encerrar assim mesmo' : 'Encerrar'}
            </button>
          </>
        }>
          <form id="form-encerrar" onSubmit={encerrar}>
            <Nota>
              Prazo vencido não encerra ciclo nenhum — quem encerra é você, e o sistema registra
              o que você decidiu.
            </Nota>
            <Campo id="enc-meta" rotulo="A meta foi atingida?" className="mt-3"
              dica="dito por quem conduziu; não é deduzido do indicador, que pode nem ter sido medido">
              <select id="enc-meta" value={metaAtingida} required
                onChange={(e) => setMetaAtingida(e.target.value as 'sim' | 'nao')}>
                <option value="">Escolha…</option>
                <option value="sim">Sim, a meta foi atingida</option>
                <option value="nao">Não, a meta não foi atingida</option>
              </select>
            </Campo>
            <Campo id="enc-motivo" rotulo="Por quê" className="mt-3"
              dica={`mínimo de ${MINIMO_DO_MOTIVO} caracteres`}>
              <textarea id="enc-motivo" rows={3} value={motivo}
                onChange={(e) => setMotivo(e.target.value)} />
            </Campo>
            {pendentes && (
              <div className="mt-3 rounded-lg bg-perigo-fundo p-3 text-[13px] text-perigo"
                data-testid="pendencias">
                <b>Sobraram {pendentes.length} ação(ões) em aberto.</b>
                <ul className="mt-1 list-disc pl-5">
                  {pendentes.map((a) => <li key={a.id}>{a.number} — {a.title}</li>)}
                </ul>
                <p className="mt-2">
                  O ciclo pode fechar assim; a lista fica anexada ao motivo. Confirme para seguir.
                </p>
              </div>
            )}
          </form>
        </Dialogo>
      )}
    </>
  );
}
