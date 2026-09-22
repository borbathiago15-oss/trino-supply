import { useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import {
  abrirPlano, apagarRisco, criarAcao, encerrarPlano, exigeMotivo, mudarStatusDaAcao,
  reabrirPlano, ROTULO_STATUS, ROTULO_SITUACAO, salvarCausaRaiz, salvarLicoes, salvarPlano,
  salvarRisco,
  type Acao, type PlanoCompleto, type Risco, type SituacaoDoPlano, type StatusDaAcao,
} from '@/api/acoes';
import { listarUsuariosPicker, type UsuarioPicker } from '@/api/usuarios';
import { Badge, Carregando, Dado, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data as formatarData, moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

const ABAS = ['Ações', 'Estratégico', 'Riscos', 'Causa raiz', 'Lições'] as const;
type Aba = (typeof ABAS)[number];

/**
 * A cor da severidade. Ela vem do servidor, que a calcula de probabilidade × impacto — a tela
 * só pinta: recalcular aqui daria dois números para o mesmo risco.
 */
export function tomDaSeveridade(s: Risco['severity']): string {
  if (s === 'CRITICO') return 'bg-perigo-fundo text-perigo';
  if (s === 'ALTO') return 'bg-aviso-fundo text-aviso';
  if (s === 'MEDIO') return 'bg-slate-100 text-texto-suave';
  return 'bg-ok-fundo text-ok';
}

/** A ação suspensa e vencida diz "suspensa", nunca "atrasada": ela está parada por decisão. */
export function rotuloDaAcao(a: Acao): string {
  if (a.late) return `Atrasada há ${a.daysLate}d`;
  return ROTULO_STATUS[a.status];
}

export function PlanoDetalhe() {
  const { id = '' } = useParams();
  const { avisar } = useToast();
  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => abrirPlano(id, signal), [id]);

  if (erro) return <Painel titulo="Plano de ação"><Erro>{erro}</Erro></Painel>;
  if (carregando && !dados) return <Painel titulo="Plano de ação"><Carregando /></Painel>;
  if (!dados) return null;

  return <Conteudo completo={dados} recarregar={recarregar} avisar={avisar} />;
}

function Conteudo({ completo, recarregar, avisar }: {
  completo: PlanoCompleto;
  recarregar: () => void;
  avisar: (texto: string, tom?: 'ok' | 'erro') => void;
}) {
  const p = completo.plan;
  const encerrado = p.life === 'ENCERRADO';
  const [aba, setAba] = useState<Aba>('Ações');
  const [salvando, setSalvando] = useState(false);
  const [encerrando, setEncerrando] = useState(false);
  const [evidencia, setEvidencia] = useState('');

  const apoio = useCarregar(
    (signal) => listarUsuariosPicker(signal).catch(() => [] as UsuarioPicker[]), []);
  const usuarios = apoio.dados ?? [];

  async function comAviso(acao: () => Promise<unknown>, ok: string, padrao: string) {
    setSalvando(true);
    try { await acao(); avisar(ok); recarregar(); }
    catch (e) { avisar(mensagem(e, padrao), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <>
      <Painel titulo={`${p.code} — ${p.title}`} acoes={
        encerrado
          ? <button type="button" className="botao-secundario" disabled={salvando}
              onClick={() => void comAviso(() => reabrirPlano(p.id),
                'Plano reaberto — quem o encerrou deixou de valer.', 'Falha ao reabrir.')}>
              Reabrir
            </button>
          : <button type="button" className="botao" onClick={() => setEncerrando(true)}>
              Encerrar plano
            </button>
      }>
        <div className="flex flex-wrap items-center gap-2">
          <Badge classe={encerrado ? 'bg-slate-100 text-slate-600' : 'bg-marca/10 text-marca'}>
            {encerrado ? 'Encerrado' : ROTULO_SITUACAO[p.status as SituacaoDoPlano]}
          </Badge>
          <Badge classe="bg-slate-100 text-texto-suave">Prioridade {p.priority}</Badge>
          {p.areas.map((a) => <Badge key={a} classe="bg-slate-100 text-texto-suave">{a}</Badge>)}
          {/* a contradição aparece: encerrar com pendência pode ser a decisão certa, mas
              fica à vista de quem abrir o plano depois */}
          {p.closedWithPending && (
            <Badge classe="bg-perigo-fundo text-perigo">Encerrado com ação em aberto</Badge>
          )}
        </div>
        <Grade2 className="mt-3">
          <Dado rotulo="Problema">{p.problem ?? '—'}</Dado>
          <Dado rotulo="Por quê">{p.businessReason ?? '—'}</Dado>
        </Grade2>
        <Grade2 className="mt-3">
          <Dado rotulo="Responsáveis">
            {p.responsibles.map((r) => r.label).join(' · ') || '—'}
          </Dado>
          <Dado rotulo="Prazo">{formatarData(p.dueDate)}</Dado>
        </Grade2>
        <Grade2 className="mt-3">
          <Dado rotulo="Avanço das ações">{p.itemsProgress}% ({p.itemCount - p.openItems}/{p.itemCount})</Dado>
          <Dado rotulo="Saving realizado">
            {moeda(p.savingTotalRealized)}
            <span className="sub"> de {moeda(p.savingTotalExpected)} esperado</span>
            {p.roi !== null && <span className="sub"> · ROI {p.roi}%</span>}
          </Dado>
        </Grade2>
        {encerrado && (
          <div className="mt-3 rounded-lg bg-superficie-suave p-3 text-[13px]" data-testid="encerramento">
            <b>Encerrado por {p.closedByLabel} em {formatarData(p.closedAt)}.</b>
            {p.evidenceNote && <p className="mt-1 whitespace-pre-line">{p.evidenceNote}</p>}
            <Nota>Plano encerrado não se edita. Reabra para mexer — a reabertura fica registrada.</Nota>
          </div>
        )}
      </Painel>

      <Painel titulo={
        <div className="flex flex-wrap gap-1.5" data-testid="abas-do-plano">
          {ABAS.map((a) => (
            <button key={a} type="button" className={a === aba ? 'botao' : 'botao-secundario'}
              aria-pressed={a === aba} onClick={() => setAba(a)}>{a}</button>
          ))}
        </div>
      }>
        {aba === 'Ações' && (
          <Acoes completo={completo} encerrado={encerrado} usuarios={usuarios}
            salvando={salvando} comAviso={comAviso} />
        )}
        {aba === 'Estratégico' && (
          <Estrategico completo={completo} encerrado={encerrado}
            salvando={salvando} comAviso={comAviso} />
        )}
        {aba === 'Riscos' && (
          <Riscos completo={completo} encerrado={encerrado}
            salvando={salvando} comAviso={comAviso} />
        )}
        {aba === 'Causa raiz' && (
          <CausaRaiz completo={completo} encerrado={encerrado}
            salvando={salvando} comAviso={comAviso} />
        )}
        {aba === 'Lições' && (
          <Licoes completo={completo} salvando={salvando} comAviso={comAviso} />
        )}
      </Painel>

      {encerrando && (
        <Dialogo titulo="Encerrar o plano" aoFechar={() => setEncerrando(false)} acoes={
          <>
            <button type="button" className="botao-secundario"
              onClick={() => setEncerrando(false)}>Cancelar</button>
            <button type="button" className="botao" disabled={salvando}
              onClick={() => void comAviso(async () => {
                await encerrarPlano(p.id, evidencia || null);
                setEncerrando(false);
              }, 'Plano encerrado, com quem decidiu e quando.', 'Falha ao encerrar.')}>
              Encerrar
            </button>
          </>
        }>
          <Nota>
            Encerrar é decisão, e não consequência do progresso: um plano a 100% continua ativo
            até alguém dizer que acabou.
          </Nota>
          {p.openItems > 0 && (
            <p className="mt-2 rounded-lg bg-aviso-fundo p-2 text-[13px] text-aviso">
              Sobram {p.openItems} ação(ões) em aberto. O plano pode fechar assim; ele fica
              marcado como encerrado com pendência.
            </p>
          )}
          <Campo id="ap-evidencia" rotulo="Evidência" className="mt-3"
            dica="o que foi feito — é o que sobra quando o plano fecha">
            <textarea id="ap-evidencia" rows={3} value={evidencia}
              onChange={(e) => setEvidencia(e.target.value)} />
          </Campo>
        </Dialogo>
      )}
    </>
  );
}

// ---- aba: ações -----------------------------------------------------------

interface AbaProps {
  completo: PlanoCompleto;
  encerrado: boolean;
  salvando: boolean;
  comAviso: (acao: () => Promise<unknown>, ok: string, padrao: string) => Promise<void>;
}

function Acoes({ completo, encerrado, usuarios, salvando, comAviso }:
  AbaProps & { usuarios: UsuarioPicker[] }) {
  const p = completo.plan;
  const [titulo, setTitulo] = useState('');
  const [responsavel, setResponsavel] = useState('');
  const [prazo, setPrazo] = useState('');
  const [causa, setCausa] = useState('');
  const [mudando, setMudando] = useState<{ acao: Acao; status: StatusDaAcao } | null>(null);
  const [motivo, setMotivo] = useState('');

  async function incluir(ev: FormEvent) {
    ev.preventDefault();
    await comAviso(async () => {
      await criarAcao(p.id, {
        title: titulo, responsibleId: responsavel,
        dueDate: prazo || null, rootCauseRef: causa || null,
      });
      setTitulo(''); setPrazo(''); setCausa('');
    }, 'Ação incluída no plano.', 'Falha ao incluir a ação.');
  }

  return (
    <>
      {completo.items.length === 0
        ? <Vazio>Nenhuma ação ainda — o plano ainda não diz como vai ser feito.</Vazio>
        : (
          <div className="overflow-x-auto">
            <table data-testid="acoes-do-plano">
              <thead>
                <tr>
                  <th>#</th><th>Ação</th><th>Responsável</th><th>Prazo</th>
                  <th>Causa que ataca</th><th>Avanço</th><th>Situação</th><th />
                </tr>
              </thead>
              <tbody>
                {completo.items.map((a) => (
                  <tr key={a.id} data-acao={a.number}>
                    <td className="tabular-nums">{a.seq}</td>
                    <td><span className="font-semibold">{a.number}</span><div className="sub">{a.title}</div></td>
                    <td className="sub">{a.responsibleLabel}</td>
                    <td className="sub whitespace-nowrap">{formatarData(a.dueDate)}</td>
                    <td className="sub">{a.rootCauseRef ?? '—'}</td>
                    <td className="tabular-nums">{a.progress}%</td>
                    <td>
                      <Badge classe={a.late ? 'bg-perigo-fundo text-perigo'
                        : a.status === 'CONCLUIDA' ? 'bg-ok-fundo text-ok'
                        : 'bg-slate-100 text-texto-suave'}>
                        {rotuloDaAcao(a)}
                      </Badge>
                    </td>
                    <td className="whitespace-nowrap">
                      {!encerrado && a.open && (
                        <div className="flex gap-1.5">
                          <button type="button" className="botao-secundario"
                            onClick={() => void comAviso(
                              () => mudarStatusDaAcao(a.id, 'CONCLUIDA'),
                              'Ação concluída.', 'Falha ao concluir.')}>Concluir</button>
                          <button type="button" className="botao-secundario"
                            onClick={() => { setMudando({ acao: a, status: 'SUSPENSA' }); setMotivo(''); }}>
                            Suspender
                          </button>
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

      {!encerrado && (
        <form onSubmit={incluir} className="mt-4">
          <Grade2>
            <Campo id="ac-titulo" rotulo="O quê">
              <input id="ac-titulo" required minLength={5} value={titulo}
                onChange={(e) => setTitulo(e.target.value)} />
            </Campo>
            <Campo id="ac-responsavel" rotulo="Quem">
              <select id="ac-responsavel" required value={responsavel}
                onChange={(e) => setResponsavel(e.target.value)}>
                <option value="">Escolha…</option>
                {usuarios.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
              </select>
            </Campo>
          </Grade2>
          <Grade2 className="mt-3">
            <Campo id="ac-prazo" rotulo="Quando">
              <input id="ac-prazo" type="date" value={prazo} onChange={(e) => setPrazo(e.target.value)} />
            </Campo>
            {/* o elo com a causa é o que permite ver a causa que ficou sem ação */}
            <Campo id="ac-causa" rotulo="Causa que esta ação ataca">
              <input id="ac-causa" value={causa} onChange={(e) => setCausa(e.target.value)} />
            </Campo>
          </Grade2>
          <button type="submit" className="botao mt-3" disabled={salvando}>Incluir ação</button>
        </form>
      )}

      {mudando && (
        <Dialogo titulo={`${ROTULO_STATUS[mudando.status]} — ${mudando.acao.number}`}
          aoFechar={() => setMudando(null)} acoes={
            <>
              <button type="button" className="botao-secundario"
                onClick={() => setMudando(null)}>Cancelar</button>
              <button type="button" className="botao"
                disabled={salvando || (exigeMotivo(mudando.status) && motivo.trim().length < 3)}
                onClick={() => void comAviso(async () => {
                  await mudarStatusDaAcao(mudando.acao.id, mudando.status, motivo);
                  setMudando(null);
                }, 'Situação da ação atualizada.', 'Falha ao mudar a situação.')}>
                Confirmar
              </button>
            </>
          }>
          <Nota>
            Suspender para o trabalho de alguém. O motivo fica registrado — e a ação suspensa
            nunca conta como atrasada.
          </Nota>
          <Campo id="ac-motivo" rotulo="Por quê" className="mt-3">
            <textarea id="ac-motivo" rows={3} value={motivo}
              onChange={(e) => setMotivo(e.target.value)} />
          </Campo>
        </Dialogo>
      )}
    </>
  );
}

// ---- aba: estratégico -----------------------------------------------------

function Estrategico({ completo, encerrado, salvando, comAviso }: AbaProps) {
  const p = completo.plan;
  const [form, setForm] = useState({
    category: p.category ?? '', sponsor: p.sponsor ?? '', managerName: p.managerName ?? '',
    operationalImpact: p.operationalImpact ?? '', financialImpact: p.financialImpact ?? '',
    kpiAffected: p.kpiAffected ?? '', targetGoal: p.targetGoal ?? '',
    criticality: p.criticality, complexity: p.complexity,
    savingExpected: String(p.savingExpected), savingRealized: String(p.savingRealized),
    investmentPlanned: String(p.investmentPlanned), investmentActual: String(p.investmentActual),
  });
  const campo = (k: keyof typeof form) => ({
    value: form[k],
    onChange: (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value })),
  });
  const numero = (v: string) => (v.trim() === '' ? 0 : Number(v.replace(',', '.')) || 0);

  return (
    <form onSubmit={(ev) => {
      ev.preventDefault();
      void comAviso(() => salvarPlano(p.id, {
        category: form.category || null, sponsor: form.sponsor || null,
        managerName: form.managerName || null,
        operationalImpact: form.operationalImpact || null,
        financialImpact: form.financialImpact || null,
        kpiAffected: form.kpiAffected || null, targetGoal: form.targetGoal || null,
        criticality: form.criticality, complexity: form.complexity,
        savingExpected: numero(form.savingExpected), savingRealized: numero(form.savingRealized),
        investmentPlanned: numero(form.investmentPlanned),
        investmentActual: numero(form.investmentActual),
      }), 'Plano atualizado.', 'Falha ao salvar o plano.');
    }}>
      <Grade2>
        <Campo id="es-categoria" rotulo="Categoria"><input id="es-categoria" {...campo('category')} /></Campo>
        <Campo id="es-sponsor" rotulo="Patrocinador"><input id="es-sponsor" {...campo('sponsor')} /></Campo>
      </Grade2>
      <Grade2 className="mt-3">
        <Campo id="es-gerente" rotulo="Gerente"><input id="es-gerente" {...campo('managerName')} /></Campo>
        <Campo id="es-kpi" rotulo="KPI afetado"><input id="es-kpi" {...campo('kpiAffected')} /></Campo>
      </Grade2>
      <Grade2 className="mt-3">
        <Campo id="es-impacto-op" rotulo="Impacto operacional">
          <textarea id="es-impacto-op" rows={2} {...campo('operationalImpact')} />
        </Campo>
        <Campo id="es-impacto-fin" rotulo="Impacto financeiro">
          <textarea id="es-impacto-fin" rows={2} {...campo('financialImpact')} />
        </Campo>
      </Grade2>
      <Campo id="es-meta" rotulo="Meta" className="mt-3">
        <textarea id="es-meta" rows={2} {...campo('targetGoal')} />
      </Campo>
      <Grade2 className="mt-3">
        <Campo id="es-criticidade" rotulo="Criticidade">
          <select id="es-criticidade" {...campo('criticality')}>
            {['BAIXA', 'MEDIA', 'ALTA'].map((g) => <option key={g} value={g}>{g}</option>)}
          </select>
        </Campo>
        <Campo id="es-complexidade" rotulo="Complexidade">
          <select id="es-complexidade" {...campo('complexity')}>
            {['BAIXA', 'MEDIA', 'ALTA'].map((g) => <option key={g} value={g}>{g}</option>)}
          </select>
        </Campo>
      </Grade2>
      <Grade2 className="mt-3">
        <Campo id="es-saving-esp" rotulo="Saving esperado">
          <input id="es-saving-esp" inputMode="decimal" {...campo('savingExpected')} />
        </Campo>
        <Campo id="es-saving-real" rotulo="Saving realizado">
          <input id="es-saving-real" inputMode="decimal" {...campo('savingRealized')} />
        </Campo>
      </Grade2>
      <Grade2 className="mt-3">
        <Campo id="es-inv-plan" rotulo="Investimento previsto">
          <input id="es-inv-plan" inputMode="decimal" {...campo('investmentPlanned')} />
        </Campo>
        <Campo id="es-inv-real" rotulo="Investimento realizado"
          dica="sem investimento, o ROI não é zero: é nulo">
          <input id="es-inv-real" inputMode="decimal" {...campo('investmentActual')} />
        </Campo>
      </Grade2>
      <button type="submit" className="botao mt-4" disabled={salvando || encerrado}>
        Salvar
      </button>
    </form>
  );
}

// ---- aba: riscos ----------------------------------------------------------

function Riscos({ completo, encerrado, salvando, comAviso }: AbaProps) {
  const p = completo.plan;
  const [descricao, setDescricao] = useState('');
  const [probabilidade, setProbabilidade] = useState('MEDIA');
  const [impacto, setImpacto] = useState('MEDIA');
  const [mitigacao, setMitigacao] = useState('');
  const graus = ['BAIXA', 'MEDIA', 'ALTA', 'MUITO_ALTA'];

  return (
    <>
      <Nota>
        A severidade é <b>probabilidade × impacto</b>, calculada no servidor. Dois riscos
        marcados "alto" que significam coisas diferentes é o que faz a matriz deixar de servir.
      </Nota>
      {completo.risks.length === 0
        ? <Vazio>Nenhum risco mapeado.</Vazio>
        : (
          <div className="mt-2 overflow-x-auto">
            <table data-testid="riscos-do-plano">
              <thead>
                <tr><th>Risco</th><th>Prob.</th><th>Impacto</th><th>Severidade</th><th>Mitigação</th><th /></tr>
              </thead>
              <tbody>
                {completo.risks.map((r) => (
                  <tr key={r.id} data-risco={r.id}>
                    <td>{r.description}</td>
                    <td className="sub">{r.probability}</td>
                    <td className="sub">{r.impact}</td>
                    <td>
                      <Badge classe={tomDaSeveridade(r.severity)}>{r.severity} · {r.score}</Badge>
                    </td>
                    <td className="sub">{r.mitigation ?? '—'}</td>
                    <td>
                      {!encerrado && (
                        <button type="button" className="botao-perigo"
                          onClick={() => void comAviso(() => apagarRisco(p.id, r.id),
                            'Risco removido.', 'Falha ao remover o risco.')}>Remover</button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

      {!encerrado && (
        <form className="mt-4" onSubmit={(ev) => {
          ev.preventDefault();
          void comAviso(async () => {
            await salvarRisco(p.id, {
              description: descricao, probability: probabilidade,
              impact: impacto, mitigation: mitigacao || null,
            });
            setDescricao(''); setMitigacao('');
          }, 'Risco registrado.', 'Falha ao registrar o risco.');
        }}>
          <Campo id="ri-descricao" rotulo="Qual é o risco">
            <input id="ri-descricao" required minLength={5} value={descricao}
              onChange={(e) => setDescricao(e.target.value)} />
          </Campo>
          <Grade2 className="mt-3">
            <Campo id="ri-prob" rotulo="Probabilidade">
              <select id="ri-prob" value={probabilidade} onChange={(e) => setProbabilidade(e.target.value)}>
                {graus.map((g) => <option key={g} value={g}>{g}</option>)}
              </select>
            </Campo>
            <Campo id="ri-impacto" rotulo="Impacto">
              <select id="ri-impacto" value={impacto} onChange={(e) => setImpacto(e.target.value)}>
                {graus.map((g) => <option key={g} value={g}>{g}</option>)}
              </select>
            </Campo>
          </Grade2>
          <Campo id="ri-mitigacao" rotulo="Como mitigar" className="mt-3">
            <textarea id="ri-mitigacao" rows={2} value={mitigacao}
              onChange={(e) => setMitigacao(e.target.value)} />
          </Campo>
          <button type="submit" className="botao mt-3" disabled={salvando}>Registrar risco</button>
        </form>
      )}
    </>
  );
}

// ---- aba: causa raiz ------------------------------------------------------

function CausaRaiz({ completo, encerrado, salvando, comAviso }: AbaProps) {
  const p = completo.plan;
  const [metodo, setMetodo] = useState(completo.rootCause?.method ?? 'CINCO_PORQUES');
  const [causa, setCausa] = useState(completo.rootCause?.mainCause ?? '');

  return (
    <form onSubmit={(ev) => {
      ev.preventDefault();
      void comAviso(() => salvarCausaRaiz(p.id, { method: metodo, mainCause: causa }),
        'Causa raiz do plano salva.', 'Falha ao salvar a causa raiz.');
    }}>
      <Nota>
        A causa raiz do plano convive com a do ciclo de melhoria e não a substitui: nem todo
        plano nasce de um ciclo, e o que nasce de uma reunião também precisa dizer o que ataca.
      </Nota>
      <Grade2 className="mt-3">
        <Campo id="cr-metodo" rotulo="Método">
          <select id="cr-metodo" value={metodo} onChange={(e) => setMetodo(e.target.value)}>
            <option value="CINCO_PORQUES">5 Porquês</option>
            <option value="ISHIKAWA">Ishikawa (6M)</option>
          </select>
        </Campo>
        <div />
      </Grade2>
      <Campo id="cr-causa" rotulo="Causa principal" className="mt-3">
        <textarea id="cr-causa" rows={3} value={causa} onChange={(e) => setCausa(e.target.value)} />
      </Campo>
      <button type="submit" className="botao mt-3" disabled={salvando || encerrado}>Salvar</button>
    </form>
  );
}

// ---- aba: lições ----------------------------------------------------------

function Licoes({ completo, salvando, comAviso }: Omit<AbaProps, 'encerrado'>) {
  const p = completo.plan;
  const l = completo.lessons;
  const [form, setForm] = useState({
    whatWorked: l?.whatWorked ?? '', whatFailed: l?.whatFailed ?? '',
    lessons: l?.lessons ?? '', bestPractice: l?.bestPractice ?? '',
    nextSteps: l?.nextSteps ?? '', recommendation: l?.recommendation ?? '',
  });
  const campo = (k: keyof typeof form) => ({
    value: form[k],
    onChange: (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value })),
  });

  return (
    <form onSubmit={(ev) => {
      ev.preventDefault();
      void comAviso(() => salvarLicoes(p.id, form), 'Lições salvas.', 'Falha ao salvar as lições.');
    }}>
      {/* as lições ficam mesmo com o plano encerrado: é depois de fechar que se aprende */}
      <Nota>
        O que <b>não</b> funcionou vale tanto quanto o que funcionou — é o que faz o próximo
        plano não repetir este.
      </Nota>
      <Grade2 className="mt-3">
        <Campo id="li-funcionou" rotulo="O que funcionou">
          <textarea id="li-funcionou" rows={2} {...campo('whatWorked')} />
        </Campo>
        <Campo id="li-falhou" rotulo="O que não funcionou">
          <textarea id="li-falhou" rows={2} {...campo('whatFailed')} />
        </Campo>
      </Grade2>
      <Grade2 className="mt-3">
        <Campo id="li-licoes" rotulo="Lições">
          <textarea id="li-licoes" rows={2} {...campo('lessons')} />
        </Campo>
        <Campo id="li-pratica" rotulo="Boa prática a padronizar">
          <textarea id="li-pratica" rows={2} {...campo('bestPractice')} />
        </Campo>
      </Grade2>
      <Grade2 className="mt-3">
        <Campo id="li-proximos" rotulo="Próximos passos">
          <textarea id="li-proximos" rows={2} {...campo('nextSteps')} />
        </Campo>
        <Campo id="li-recomendacao" rotulo="Recomendação">
          <textarea id="li-recomendacao" rows={2} {...campo('recommendation')} />
        </Campo>
      </Grade2>
      <button type="submit" className="botao mt-4" disabled={salvando}>Salvar lições</button>
    </form>
  );
}
