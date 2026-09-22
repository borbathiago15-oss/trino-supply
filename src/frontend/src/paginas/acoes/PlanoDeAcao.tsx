import { useState, type FormEvent } from 'react';
import {
  atualizarAcao, criarAcao, exigeMotivo, listarAcoes, mudarStatusDaAcao, ROTULO_STATUS,
  type Acao, type StatusDaAcao,
} from '@/api/acoes';
import { listarCentrosCusto, type CentroCusto } from '@/api/centrosCusto';
import { listarUsuariosPicker, type UsuarioPicker } from '@/api/usuarios';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data as formatarData, moeda } from '@/util/formato';
import { useDebounce } from '@/util/useDebounce';
import { useCarregar } from '@/util/useCarregar';

const VAZIO = {
  title: '', responsibleId: '', startDate: '', dueDate: '', reason: '',
  area: '', expectedResult: '', kpi: '', expectedGain: '', costCenter: '',
};
type Formulario = typeof VAZIO;

/** A cor da situação. Atrasada fala mais alto que a situação: é o que precisa de ação hoje. */
export function tomDaLinha(a: Acao): string {
  if (a.late) return 'bg-perigo-fundo text-perigo';
  if (a.status === 'CONCLUIDA') return 'bg-ok-fundo text-ok';
  if (a.status === 'SUSPENSA') return 'bg-slate-100 text-slate-600';
  return 'bg-slate-100 text-texto-suave';
}

/**
 * O rótulo que a linha mostra. A ação suspensa e vencida diz "suspensa", e não "atrasada":
 * ela está parada por decisão, e chamá-la de atrasada cobraria a equipe por isso.
 */
export function rotuloDaLinha(a: Acao): string {
  if (a.late) return `Atrasada há ${a.daysLate}d`;
  return ROTULO_STATUS[a.status];
}

export function PlanoDeAcaoTela() {
  const { avisar } = useToast();
  const [busca, setBusca] = useState('');
  const [status, setStatus] = useState('');
  const [responsavel, setResponsavel] = useState('');
  const [centroCusto, setCentroCusto] = useState('');
  const [atrasadas, setAtrasadas] = useState(false);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [editando, setEditando] = useState<Acao | null>(null);
  const [salvando, setSalvando] = useState(false);
  const [mudando, setMudando] = useState<{ acao: Acao; status: StatusDaAcao } | null>(null);
  const [motivo, setMotivo] = useState('');

  const termo = useDebounce(busca);
  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarAcoes({ busca: termo, status, responsavel, centroCusto, atrasadas }, signal),
    [termo, status, responsavel, centroCusto, atrasadas],
  );
  const apoio = useCarregar(async (signal) => ({
    usuarios: await listarUsuariosPicker(signal).catch(() => [] as UsuarioPicker[]),
    centros: await listarCentrosCusto(false, signal).catch(() => [] as CentroCusto[]),
  }), []);

  const lista = dados?.itens ?? [];
  const placar = dados?.placar;
  const campo = (k: keyof Formulario) => ({
    value: form[k],
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  function editar(a: Acao) {
    setEditando(a);
    setForm({
      title: a.title, responsibleId: a.responsibleId, startDate: a.startDate ?? '',
      dueDate: a.dueDate ?? '', reason: a.reason ?? '', area: a.area ?? '',
      expectedResult: a.expectedResult ?? '', kpi: a.kpi ?? '',
      expectedGain: a.expectedGain?.toString() ?? '', costCenter: a.costCenter ?? '',
    });
  }
  const cancelar = () => { setEditando(null); setForm(VAZIO); };

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      const corpo = {
        title: form.title, responsibleId: form.responsibleId,
        startDate: form.startDate || null, dueDate: form.dueDate || null,
        reason: form.reason || null, area: form.area || null,
        expectedResult: form.expectedResult || null, kpi: form.kpi || null,
        expectedGain: form.expectedGain ? parseFloat(form.expectedGain) : null,
        costCenter: form.costCenter || null,
      };
      if (editando) { await atualizarAcao(editando.id, corpo); avisar('Ação atualizada.'); }
      else { await criarAcao(corpo); avisar('Ação criada.'); }
      cancelar();
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar a ação.', 'erro'); }
    finally { setSalvando(false); }
  }

  async function confirmarMudanca() {
    if (!mudando) return;
    try {
      await mudarStatusDaAcao(mudando.acao.id, mudando.status, motivo || null);
      avisar('Situação atualizada.');
      setMudando(null); setMotivo('');
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao mudar a situação.', 'erro'); }
  }

  return (
    <>
      <Painel titulo="Plano de ação" acoes={
        <div className="flex flex-wrap items-center gap-2">
          <input aria-label="Buscar" placeholder="Buscar por número, o quê ou responsável"
            className="!w-[280px]" value={busca} onChange={(e) => setBusca(e.target.value)} />
          <select aria-label="Situação" className="!w-[160px]" value={status}
            onChange={(e) => setStatus(e.target.value)}>
            <option value="">Todas</option>
            {Object.entries(ROTULO_STATUS).map(([v, r]) => <option key={v} value={v}>{r}</option>)}
          </select>
          <select aria-label="Responsável" className="!w-[180px]" value={responsavel}
            onChange={(e) => setResponsavel(e.target.value)}>
            <option value="">Todos</option>
            {(dados?.opcoes.responsibles ?? []).map((r) => <option key={r.id} value={r.id}>{r.label}</option>)}
          </select>
          <select aria-label="Centro de custo" className="!w-[160px]" value={centroCusto}
            onChange={(e) => setCentroCusto(e.target.value)}>
            <option value="">Todos os centros</option>
            {(dados?.opcoes.costCenters ?? []).map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
          <label className="!mb-0 flex items-center gap-2 !text-[13px] !font-normal">
            <input type="checkbox" className="!w-auto" checked={atrasadas}
              onChange={(e) => setAtrasadas(e.target.checked)} />
            Só atrasadas
          </label>
        </div>
      }>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}

        {placar && (
          <div className="mb-3 grid grid-cols-2 gap-2 md:grid-cols-6" data-testid="placar-das-acoes">
            {([
              ['Total', placar.total, ''],
              ['Pendentes', placar.pendentes, ''],
              ['Em andamento', placar.emAndamento, ''],
              ['Concluídas', placar.concluidas, 'text-ok'],
              ['Suspensas', placar.suspensas, 'text-slate-500'],
              ['Atrasadas', placar.atrasadas, 'text-perigo'],
            ] as const).map(([rotulo, valor, tom]) => (
              <div key={rotulo} className="rounded-lg border border-borda p-2">
                <div className="sub">{rotulo}</div>
                <div className={`text-[20px] font-bold ${tom}`}>{valor}</div>
              </div>
            ))}
          </div>
        )}
        {placar && (placar.ganhoEsperado > 0 || placar.ganhoRealizado > 0) && (
          <p className="sub mb-3" data-testid="ganho-das-acoes">
            Ganho esperado {moeda(placar.ganhoEsperado)} · realizado {moeda(placar.ganhoRealizado)}
          </p>
        )}

        {dados && !lista.length && <Vazio>Nenhuma ação neste recorte.</Vazio>}
        {lista.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-acoes" className="min-w-[1000px]">
              <thead>
                <tr>
                  <th>Nº</th><th>O quê</th><th>Responsável</th><th>Prazo</th>
                  <th>Progresso</th><th>Situação</th><th>Ações</th>
                </tr>
              </thead>
              <tbody>
                {lista.map((a) => (
                  <tr key={a.id} data-acao={a.number}>
                    <td className="font-semibold">{a.number}</td>
                    <td>
                      {a.title}
                      {a.statusReason && <div className="sub">{a.statusReason}</div>}
                    </td>
                    <td>{a.responsibleLabel}</td>
                    <td className="whitespace-nowrap">{a.dueDate ? formatarData(a.dueDate) : '—'}</td>
                    <td className="whitespace-nowrap">{a.progress}%</td>
                    <td><Badge classe={tomDaLinha(a)}>{rotuloDaLinha(a)}</Badge></td>
                    <td className="whitespace-nowrap">
                      <div className="flex gap-1.5">
                        <button type="button" className="botao-secundario !py-1.5" onClick={() => editar(a)}>Editar</button>
                        {a.open && (
                          <select aria-label={`Mudar situação de ${a.number}`} className="!w-[130px] !py-1.5"
                            value="" onChange={(e) => {
                              const novo = e.target.value as StatusDaAcao;
                              if (!novo) return;
                              if (exigeMotivo(novo)) { setMudando({ acao: a, status: novo }); setMotivo(''); }
                              else void mudarStatusDaAcao(a.id, novo).then(() => { avisar('Situação atualizada.'); recarregar(); });
                            }}>
                            <option value="">Mudar para…</option>
                            {(Object.keys(ROTULO_STATUS) as StatusDaAcao[])
                              .filter((s) => s !== a.status)
                              .map((s) => <option key={s} value={s}>{ROTULO_STATUS[s]}</option>)}
                          </select>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      <Painel id="form-acao" titulo={editando ? `Editar ação — ${editando.number}` : 'Nova ação'}>
        <form onSubmit={enviar}>
          <Campo id="ac-titulo" rotulo="O quê" dica="(o que precisa ser feito)">
            <input id="ac-titulo" required minLength={5} placeholder="ex.: Refazer o layout da doca 2" {...campo('title')} />
          </Campo>
          <Grade2 className="mt-3">
            <Campo id="ac-responsavel" rotulo="Quem" dica="(dono da ação)">
              <select id="ac-responsavel" required {...campo('responsibleId')}>
                <option value="">Selecione o responsável…</option>
                {(apoio.dados?.usuarios ?? []).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
              </select>
            </Campo>
            <Campo id="ac-centro" rotulo="Centro de custo">
              <select id="ac-centro" {...campo('costCenter')}>
                <option value="">Sem centro — ação da casa</option>
                {(apoio.dados?.centros ?? []).map((c) => (
                  <option key={c.id} value={c.code}>{c.code} — {c.name}</option>
                ))}
              </select>
            </Campo>
          </Grade2>
          <Grade2 className="mt-3">
            <Campo id="ac-inicio" rotulo="Início">
              <input id="ac-inicio" type="date" {...campo('startDate')} />
            </Campo>
            <Campo id="ac-prazo" rotulo="Prazo">
              <input id="ac-prazo" type="date" {...campo('dueDate')} />
            </Campo>
          </Grade2>
          <details className="mt-3">
            <summary className="cursor-pointer text-[13px] font-semibold text-texto-suave">Mais detalhes</summary>
            <Campo id="ac-porque" rotulo="Por quê" className="mt-3">
              <textarea id="ac-porque" rows={2} {...campo('reason')} />
            </Campo>
            <Grade2 className="mt-3">
              <Campo id="ac-onde" rotulo="Onde">
                <input id="ac-onde" {...campo('area')} />
              </Campo>
              <Campo id="ac-kpi" rotulo="Indicador relacionado">
                <input id="ac-kpi" {...campo('kpi')} />
              </Campo>
            </Grade2>
            <Grade2 className="mt-3">
              <Campo id="ac-resultado" rotulo="Resultado esperado">
                <input id="ac-resultado" {...campo('expectedResult')} />
              </Campo>
              <Campo id="ac-ganho" rotulo="Ganho esperado (R$)">
                <input id="ac-ganho" type="number" min={0} step="0.01" {...campo('expectedGain')} />
              </Campo>
            </Grade2>
          </details>
          <Nota>
            Suspender ou cancelar pede o motivo. Ação suspensa fica parada por decisão e
            <strong> não conta como atrasada</strong> — o prazo não corre contra quem foi mandado parar.
          </Nota>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="submit" className="botao" disabled={salvando}>
              {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Criar ação'}
            </button>
            {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
          </div>
        </form>
      </Painel>

      {mudando && (
        <Dialogo titulo={`${ROTULO_STATUS[mudando.status]} — ${mudando.acao.number}`}
          aoFechar={() => setMudando(null)}>
          <p className="text-[13.5px]">
            {mudando.status === 'SUSPENSA'
              ? 'A ação fica parada por decisão e deixa de contar como atrasada. Diga por quê.'
              : 'A ação sai do plano. Diga por quê.'}
          </p>
          <Campo id="ac-motivo" rotulo="Motivo" className="mt-3">
            <textarea id="ac-motivo" rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </Campo>
          <div className="mt-4 flex gap-2">
            <button type="button" className="botao" disabled={!motivo.trim()} onClick={() => void confirmarMudanca()}>
              Confirmar
            </button>
            <button type="button" className="botao-secundario" onClick={() => setMudando(null)}>Cancelar</button>
          </div>
        </Dialogo>
      )}
    </>
  );
}
