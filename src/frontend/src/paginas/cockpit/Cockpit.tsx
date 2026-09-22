import { useCallback, useEffect, useState } from 'react';
import { INTERVALO_DO_COCKPIT, obterCockpit, type CockpitDados } from '@/api/torre';
import { moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import {
  CLASSE_DA_DESCARGA, CLASSE_DO_ALERTA, CLASSE_DO_GARGALO, cicloDeUnidades, compacto,
  horaDoRelogio, ordenarRadar, progressoDaMeta, proximaUnidade, ROTACAO_MS,
  ROTULO_DA_DESCARGA, ROTULO_DO_ALERTA, slaAtingido,
} from './cockpit';
import { BarraDeMeta, CartaoVital, ListaRolante, NumeroVivo, PulsoAoVivo } from './pecas';

/** O relógio do cabeçalho, de segundo em segundo. */
function useRelogio() {
  const [agora, setAgora] = useState(() => new Date());
  useEffect(() => {
    const id = setInterval(() => setAgora(new Date()), 1000);
    return () => clearInterval(id);
  }, []);
  return agora;
}

/**
 * War Room Cockpit — a TV da sala de suprimentos.
 *
 * <p>
 * Três decisões sustentam a tela. Ela **deriva da Torre**, e não de uma segunda conta: a TV
 * fica na sala onde o comprador trabalha, e duas telas com números diferentes perdem a
 * autoridade juntas. Ela **troca sem piscar**: o valor anterior fica até o novo chegar, porque
 * número que some e volta parece defeito. E ela **não tem barra de rolagem**: ninguém toca
 * nessa tela, então lista longa rola sozinha.
 * </p>
 */
export function Cockpit() {
  const agora = useRelogio();
  const [telaCheia, setTelaCheia] = useState(false);
  const [unidade, setUnidade] = useState<string | null>(null);
  const { dados, erro, recarregar } = useCarregar<CockpitDados>(
    (signal) => obterCockpit(unidade, signal), [unidade]);

  // o ciclo silencioso: `recarregar` mantém os dados atuais na tela enquanto os novos vêm
  useEffect(() => {
    const id = setInterval(recarregar, INTERVALO_DO_COCKPIT);
    return () => clearInterval(id);
  }, [recarregar]);

  // a rotação da parede: a cada dois minutos a TV passa para a próxima unidade sozinha
  const ciclo = cicloDeUnidades(dados?.unidades ?? []);
  const chaveDoCiclo = ciclo.join('|');
  useEffect(() => {
    if (ciclo.length <= 1) return;
    const id = setInterval(() => setUnidade((atual) => proximaUnidade(ciclo, atual)), ROTACAO_MS);
    return () => clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chaveDoCiclo]);

  const alternarTelaCheia = useCallback(async () => {
    try {
      if (document.fullscreenElement) { await document.exitFullscreen(); setTelaCheia(false); }
      else { await document.documentElement.requestFullscreen(); setTelaCheia(true); }
    } catch { /* navegador sem permissão: a tela continua servindo do mesmo jeito */ }
  }, []);

  if (!dados) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-950 text-slate-400">
        {erro ? `Sem sinal — ${erro}` : 'Conectando ao cockpit…'}
      </div>
    );
  }

  const { kpis, pipeline, excecoesCriticas, burndownCompradores, agendaDocaHoje } = dados;
  const radar = ordenarRadar(excecoesCriticas);
  const slaOk = slaAtingido(kpis.slaSemanalPct, kpis.metaSlaPct);

  return (
    <div className="flex h-screen flex-col overflow-hidden bg-slate-950 p-5 text-white" data-testid="cockpit">
      {/* NÍVEL 1 — barra de estado */}
      <header className="flex items-center justify-between pb-4">
        <div className="flex items-center gap-4">
          {/* a marca na parede: quem entra na sala tem de saber de quem é o painel antes
              de ler qualquer número. É o mesmo arquivo do menu, que já nasceu para fundo
              escuro — e o alt mantém o título da tela para quem lê por leitor de tela */}
          <h1 className="m-0">
            <img src="/assets/brand/trino-supply-mark.png" width={420} height={108}
              alt="Trino Supply" className="h-9 w-auto" />
          </h1>
          <PulsoAoVivo vivo={!erro} />
          {/* qual recorte está na parede agora — sem isso, quem chega lê o número
              da unidade da vez achando que é o da empresa inteira */}
          <span data-testid="unidade-na-tela"
            className="rounded-full border border-slate-700 px-3 py-1 text-[13px] text-slate-300">
            {dados.unidade ?? 'Visão geral'}
            {ciclo.length > 1 && <span className="ml-2 text-slate-500">{ciclo.length} recortes</span>}
          </span>
          {erro && <span className="text-[13px] text-rose-300">última leitura mantida</span>}
        </div>
        <div className="flex items-center gap-5">
          <span className="font-mono text-3xl font-bold tabular-nums">{horaDoRelogio(agora)}</span>
          <button type="button" onClick={() => void alternarTelaCheia()}
            className="rounded-lg border border-slate-700 px-3 py-1.5 text-[12px] text-slate-400 hover:text-white"
            aria-label="Alternar tela cheia">
            {telaCheia ? '◱' : '⛶'}
          </button>
        </div>
      </header>

      {/* NÍVEL 2 — cinco cartões de comando */}
      <div className="grid grid-cols-5 gap-4">
        <CartaoVital icone="🚨" titulo="Risco operacional"
          tom={kpis.taxaRiscoPct > 20 ? 'text-rose-400' : 'text-white'}
          rodape={<><NumeroVivo valor={String(kpis.itensAtrasados)} /> em atraso crítico</>}>
          <NumeroVivo valor={`${kpis.taxaRiscoPct.toLocaleString('pt-BR')}%`} />
        </CartaoVital>

        <CartaoVital icone="⏳" titulo="Backlog de suprimentos"
          rodape={<>{moeda(kpis.backlogTotalValor)} em trâmite</>}>
          <NumeroVivo valor={compacto(kpis.backlogTotalItens)} />
        </CartaoVital>

        <CartaoVital icone="⏱️" titulo="SLA da semana"
          tom={slaOk ? 'text-emerald-400' : 'text-amber-400'}
          rodape={<BarraDeMeta progresso={progressoDaMeta(kpis.slaSemanalPct, kpis.metaSlaPct)} atingiu={slaOk} />}>
          <NumeroVivo valor={`${kpis.slaSemanalPct.toLocaleString('pt-BR')}%`} />
        </CartaoVital>

        <CartaoVital icone="💰" titulo="Saving do mês" tom="text-emerald-400"
          rodape={<BarraDeMeta
            progresso={progressoDaMeta(kpis.savingMesTotal, kpis.metaSavingMes)}
            atingiu={kpis.savingMesTotal >= kpis.metaSavingMes} />}>
          <NumeroVivo valor={`R$ ${compacto(kpis.savingMesTotal)}`} />
        </CartaoVital>

        <CartaoVital icone="🚚" titulo="OTIF 30 dias"
          tom={kpis.otifGeralPct !== null && kpis.otifGeralPct < 85 ? 'text-amber-400' : 'text-white'}
          rodape={kpis.otifMedidos > 0 ? `${kpis.otifMedidos} entrega(s) medida(s)` : 'nenhuma entrega medida'}>
          <NumeroVivo valor={kpis.otifGeralPct === null ? '—' : `${kpis.otifGeralPct.toLocaleString('pt-BR')}%`} />
        </CartaoVital>
      </div>

      {/* NÍVEL 3 — a esteira */}
      <div className="mt-4 flex items-stretch gap-2" data-testid="esteira">
        {pipeline.map((no, i) => (
          <div key={no.etapa} className="flex flex-1 items-center gap-2">
            <div className={`flex-1 rounded-xl border bg-slate-900/90 px-4 py-3 ${CLASSE_DO_GARGALO[no.gargalo]}`}
              data-etapa={no.etapa} data-gargalo={no.gargalo}>
              <div className="text-[12px] uppercase tracking-wider text-slate-400">{no.rotulo}</div>
              <div className="flex items-baseline gap-2">
                <NumeroVivo valor={String(no.quantidade)} className="font-mono text-3xl font-bold" />
                {no.quantidade > 0 && (
                  <span className="text-[12px]">mais antigo: {no.horasNaFila}h</span>
                )}
              </div>
            </div>
            {i < pipeline.length - 1 && <span aria-hidden className="text-slate-700">──▶</span>}
          </div>
        ))}
      </div>

      {/* NÍVEL 4 — radar (60%) e produtividade (40%) */}
      <div className="mt-4 grid min-h-0 flex-1 grid-cols-5 gap-4">
        <section className="col-span-3 flex min-h-0 flex-col rounded-2xl border border-slate-800 bg-slate-900/90 p-4">
          <h2 className="pb-2 text-[13px] font-semibold uppercase tracking-wider text-slate-400">
            Radar de exceções — ação imediata
          </h2>
          {radar.length === 0 ? (
            <p className="py-6 text-center text-emerald-300">Nenhuma exceção aberta.</p>
          ) : (
            <ListaRolante itens={radar.length}>
              <ul data-testid="radar">
                {radar.map((x) => (
                  <li key={`${x.tipoAlerta}-${x.id}`} data-alerta={x.tipoAlerta}
                    className={`mb-1.5 flex items-center gap-3 border-l-4 px-3 py-2 ${CLASSE_DO_ALERTA[x.tipoAlerta]}`}>
                    <span className="w-28 shrink-0 text-[12px] font-bold uppercase">{ROTULO_DO_ALERTA[x.tipoAlerta]}</span>
                    <span className="w-32 shrink-0 font-mono text-[15px]">{x.codigoReferencia}</span>
                    <span className="min-w-0 flex-1 truncate text-[16px]">{x.descricaoItem}</span>
                    <span className="w-28 shrink-0 text-[13px] text-slate-400">{x.unidadeCentroCusto}</span>
                    <span className="w-44 shrink-0 text-right font-semibold">{x.tempoRestanteOuAtraso}</span>
                    <span className="w-40 shrink-0 truncate text-right text-[13px] text-slate-400">{x.responsavelNome}</span>
                  </li>
                ))}
              </ul>
            </ListaRolante>
          )}
        </section>

        <section className="col-span-2 flex min-h-0 flex-col gap-4">
          <div className="flex min-h-0 flex-1 flex-col rounded-2xl border border-slate-800 bg-slate-900/90 p-4">
            <h2 className="pb-2 text-[13px] font-semibold uppercase tracking-wider text-slate-400">
              Produtividade do dia
            </h2>
            {burndownCompradores.length === 0 ? (
              <p className="py-4 text-center text-slate-500">Sem fila atribuída.</p>
            ) : (
              <ListaRolante itens={burndownCompradores.length}>
                <ul data-testid="burndown">
                  {burndownCompradores.map((b) => (
                    <li key={b.compradorNome} className="mb-2">
                      <div className="flex items-baseline justify-between text-[15px]">
                        <span className="truncate">{b.compradorNome}</span>
                        <span className="font-mono tabular-nums">
                          {b.atendidosHoje}/{b.totalHoje}
                          {b.pendenciasCriticas > 0 && (
                            <span className="ml-2 text-rose-400">⚠ {b.pendenciasCriticas}</span>
                          )}
                        </span>
                      </div>
                      <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-slate-800">
                        <div className="h-full rounded-full bg-sky-400 transition-all duration-700"
                          style={{ width: `${b.totalHoje === 0 ? 0 : Math.round(b.atendidosHoje * 100 / b.totalHoje)}%` }} />
                      </div>
                    </li>
                  ))}
                </ul>
              </ListaRolante>
            )}
          </div>

          <div className="flex min-h-0 flex-1 flex-col rounded-2xl border border-slate-800 bg-slate-900/90 p-4">
            <h2 className="pb-2 text-[13px] font-semibold uppercase tracking-wider text-slate-400">
              Descargas previstas
            </h2>
            {agendaDocaHoje.length === 0 ? (
              <p className="py-4 text-center text-slate-500">Nenhuma descarga prevista.</p>
            ) : (
              <ListaRolante itens={agendaDocaHoje.length}>
                <ul data-testid="doca">
                  {agendaDocaHoje.map((d) => (
                    <li key={`${d.numeroNfe}-${d.fornecedorNome}`}
                      className="mb-1.5 flex items-center gap-3 text-[15px]">
                      <span className="w-14 shrink-0 font-mono text-slate-400">{d.horarioPrevisto}</span>
                      <span className="w-24 shrink-0 font-mono">NF {d.numeroNfe}</span>
                      <span className="min-w-0 flex-1 truncate">{d.fornecedorNome}</span>
                      <span className={`shrink-0 text-[13px] font-semibold ${CLASSE_DA_DESCARGA[d.statusEntrega]}`}>
                        {ROTULO_DA_DESCARGA[d.statusEntrega]}
                      </span>
                    </li>
                  ))}
                </ul>
              </ListaRolante>
            )}
          </div>
        </section>
      </div>
    </div>
  );
}
