import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  BADGE_ACHADO, CLASSE_ACHADO, JANELAS, relatorioDeInsights, tcoPorProduto,
  type Achado, type Backlog, type Janela, type LinhaTco, type VisaoExecutiva,
} from '@/api/analytics';
import { tratarAchado } from '@/api/melhoria';
import { Badge, Carregando, Erro, Kpi, Painel, SeletorJanela, Vazio } from '@/componentes/basicos';
import { Dialogo } from '@/componentes/Dialogo';
import { Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { moedaCurta } from '@/componentes/graficos';
import { enderecoDoId } from '@/layout/menu';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

/**
 * Tratar a causa de um achado. O Insights aponta o problema com a evidência junto e parava
 * aí — quem lia abria um ciclo na mão e redigitava o que a tela já dizia.
 *
 * <p>
 * O diálogo pergunta uma coisa só: abrir junto o plano da contramedida. É a decisão que o
 * usuário de fato tem — o resto o achado já respondeu.
 * </p>
 */
export function TratarACausa({ achado }: { achado: Achado }) {
  const { avisar } = useToast();
  const navegar = useNavigate();
  const [aberto, setAberto] = useState(false);
  const [comPlano, setComPlano] = useState(true);
  const [indo, setIndo] = useState(false);

  async function tratar() {
    setIndo(true);
    try {
      const r = await tratarAchado({
        code: achado.code, title: achado.title,
        evidence: achado.evidence, action: achado.action, createPlan: comPlano,
      });
      // "já existia" abre o ciclo em vez de anunciar um novo: dizer "criado" seria mentir,
      // e o usuário procuraria um segundo ciclo que não existe
      avisar(r.alreadyExisted
        ? `Este achado já tinha o ciclo ${r.cycle.code} — abrindo ele.`
        : `Ciclo ${r.cycle.code} aberto${r.planCode ? `, com o plano ${r.planCode}` : ''}.`);
      navegar(`/melhoria/${r.cycle.id}`);
    } catch (e) {
      avisar(e instanceof Error ? e.message : 'Falha ao abrir o ciclo.', 'erro');
      setIndo(false);
    }
  }

  return (
    <>
      <button type="button" className="botao-secundario" onClick={() => setAberto(true)}>
        Tratar a causa
      </button>
      {aberto && (
        <Dialogo titulo="Tratar a causa deste achado" aoFechar={() => setAberto(false)} acoes={
          <>
            <button type="button" className="botao-secundario"
              onClick={() => setAberto(false)}>Cancelar</button>
            <button type="button" className="botao" disabled={indo}
              onClick={() => void tratar()}>{indo ? 'Abrindo…' : 'Abrir ciclo'}</button>
          </>
        }>
          <p className="text-[13.5px] font-semibold">{achado.title}</p>
          <p className="sub mt-1">{achado.evidence}</p>
          <Nota>
            O ciclo nasce em Plan, com o problema e a evidência já escritos e os 5 Porquês
            começados. O mesmo achado não abre dois ciclos.
          </Nota>
          <label className="mt-3 flex items-center gap-2 text-[13px]">
            <input type="checkbox" checked={comPlano}
              onChange={(e) => setComPlano(e.target.checked)} />
            Abrir também o plano da contramedida
          </label>
        </Dialogo>
      )}
    </>
  );
}

/** As quatro faixas do backlog, da mais nova para a mais velha. */
const CORES_AGING = ['bg-ok-fundo text-ok', 'bg-teal-50 text-teal-800', 'bg-aviso-fundo text-aviso', 'bg-perigo-fundo text-perigo'];

function Executivo({ e }: { e: VisaoExecutiva }) {
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4 xl:grid-cols-7">
      <Kpi rotulo="Spend (O.C.s)" valor={moedaCurta(e.spend)} detalhe={`${quantidade(e.orders)} pedido(s)`} />
      <Kpi rotulo="Processos" valor={quantidade(e.processes)} detalhe={`${quantidade(e.closedProcesses)} com O.C. registrada`} />
      <Kpi rotulo="Saving negociado" valor={moedaCurta(e.savingTotal)} detalhe="ganho de negociação" />
      <Kpi rotulo="Saving de referência" valor={moedaCurta(e.referenceSavingTotal)} detalhe="vs último preço pago" />
      <Kpi rotulo="Cost avoidance" valor={moedaCurta(e.costAvoidanceTotal)} detalhe="reajustes evitados no contrato" />
      <Kpi rotulo="OTIF médio" valor={e.otifPercent != null ? `${e.otifPercent}%` : '—'} detalhe="entregas medidas" />
      <Kpi rotulo="Compliance médio" valor={e.complianceAverage ?? '—'} detalhe="score dos processos" />
    </div>
  );
}

function PainelBacklog({ b }: { b: Backlog }) {
  return (
    <>
      <p className="mb-3">
        <strong>{quantidade(b.total)}</strong> demanda(s) em aberto ·{' '}
        <strong>{quantidade(b.unassigned)}</strong> sem responsável
      </p>
      <div className="mb-3 flex flex-wrap gap-1.5">
        {b.aging.map((f, i) => (
          <Badge key={f.label} classe={CORES_AGING[i] ?? CORES_AGING[3]}>{f.label}: {quantidade(f.count)}</Badge>
        ))}
      </div>
      {b.byAssignee.length > 0 && (
        <div className="overflow-x-auto">
          <table data-testid="backlog-por-responsavel">
            <thead><tr><th>Responsável</th><th>Demandas abertas</th></tr></thead>
            <tbody>
              {b.byAssignee.map((a) => (
                <tr key={a.label}><td>{a.label}</td><td>{quantidade(a.count)}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

function Tco({ linhas }: { linhas: LinhaTco[] }) {
  if (!linhas.length) return <Vazio>Nenhuma O.C. com produto de catálogo no período.</Vazio>;
  return (
    <div className="overflow-x-auto">
      <table data-testid="tabela-tco" className="min-w-[880px]">
        <thead>
          <tr>
            <th>Produto</th><th>Categoria</th><th>Qtde</th><th>Preço unit. médio</th>
            <th>TCO unit. médio</th><th>Extras (frete/impostos)</th><th>TCO total</th>
          </tr>
        </thead>
        <tbody>
          {linhas.map((i) => (
            <tr key={i.catalogItemId}>
              <td className="min-w-[220px]">
                {i.code ? `[${i.code}] ` : ''}{i.description}
                {i.family && <div className="sub">{i.family}</div>}
              </td>
              <td>{i.category ?? <span className="sub">—</span>}</td>
              <td className="whitespace-nowrap">{quantidade(i.quantity)} {i.unitOfMeasure ?? ''}</td>
              <td className="whitespace-nowrap">{moeda(i.unitPriceAvg)}</td>
              <td className="whitespace-nowrap"><strong>{moeda(i.tcoUnitAvg)}</strong></td>
              <td className="whitespace-nowrap">
                {moeda(i.extrasValue)} <span className="sub">({i.extrasPercent}%)</span>
              </td>
              <td className="whitespace-nowrap">{moeda(i.tcoTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Insights() {
  const [meses, setMeses] = useState<Janela>(6);
  const { dados, erro, carregando } = useCarregar((signal) => relatorioDeInsights(meses, signal), [meses]);
  // o TCO é uma leitura à parte: falhar nele não derruba o resto da tela
  const tco = useCarregar((signal) => tcoPorProduto(meses, signal).catch(() => [] as LinhaTco[]), [meses]);

  return (
    <>
      <Painel titulo="Visão executiva"
        acoes={<SeletorJanela id="ins-meses" valor={meses} opcoes={JANELAS} aoMudar={(v) => setMeses(v as Janela)} />}>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando texto="Apurando o período…" />}
        {dados && <Executivo e={dados.executive} />}
      </Painel>

      {dados && (
        <>
          <Painel titulo="Insights de compras">
            <p className="sub mb-3">
              Achados <strong>100% determinísticos</strong> — regra fixa sobre os dados reais, sempre
              com a evidência: sobrepreço vs último preço pago (+20%), possível fracionamento (SCs
              pequenas somando acima do limite de alçada em 30 dias), urgências recorrentes (3+ no
              período) e concentração de fornecedor (≥40% do spend da categoria).
            </p>
            {!dados.insights.length && (
              <Vazio>
                Nenhum achado no período — nada de sobrepreço, fracionamento, urgência recorrente ou concentração. ✔
              </Vazio>
            )}
            {dados.insights.length > 0 && (
              <div className="flex flex-col gap-2" data-testid="lista-insights">
                {dados.insights.map((i, idx) => (
                  <div key={`${i.code}-${idx}`} data-achado={i.code}
                    className={'rounded-lg border border-l-4 border-slate-200/80 bg-white px-4 py-3 shadow-sm '
                      + (CLASSE_ACHADO[i.severity] ?? CLASSE_ACHADO.info)}>
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge classe={BADGE_ACHADO[i.severity] ?? BADGE_ACHADO.info}>{i.code}</Badge>
                      <strong>{i.title}</strong>
                    </div>
                    <div className="sub mt-1">{i.evidence}</div>
                    <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
                      {i.action && <span className="text-[13px] font-semibold">{i.action}</span>}
                      <div className="flex shrink-0 flex-wrap gap-2">
                        {/* o achado aponta o problema com a evidência junto, e parava aí:
                            quem lia redigitava na mão o que a tela já dizia */}
                        <TratarACausa achado={i} />
                        {i.view && (
                          <Link to={enderecoDoId(i.view)} className="botao-secundario">
                            Ir para a tela →
                          </Link>
                        )}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Painel>

          <Painel titulo="TCO por produto">
            <p className="sub mb-3">
              Custo total de aquisição no período: o extra de cada O.C. (frete + impostos + outros −
              desconto) é rateado entre os itens proporcionalmente ao valor. Compare o{' '}
              <strong>TCO unitário</strong> com o preço unitário puro.
            </p>
            {tco.carregando && !tco.dados && <Carregando />}
            {tco.dados && <Tco linhas={tco.dados} />}
          </Painel>

          <Painel titulo="Backlog de demandas em aberto">
            <PainelBacklog b={dados.backlog} />
          </Painel>
        </>
      )}
    </>
  );
}
