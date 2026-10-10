import type { CockpitDados, CockpitDoMaterial, RankingDoMaterial } from '@/api/torre';
import {
  CLASSE_DO_ALERTA_DO_ALMOXARIFADO, CLASSE_DO_GARGALO, compacto, fraseDasMedidas, fraseDaRotaDeCompra,
  horasNaParede, ordenarRadarDoAlmoxarifado, ROTULO_DO_ALERTA_DO_ALMOXARIFADO, SEM_RANKING, slaAtingido,
} from './cockpit';
import { CartaoVital, ListaRolante, NumeroVivo, ParDaFaixa } from './pecas';

/** A cor do número grande pela régua de gargalo — a mesma da esteira da compra. */
const TOM_DO_GARGALO = { NORMAL: 'text-white', ATENCAO: 'text-amber-400', CRITICO: 'text-rose-400' } as const;

/**
 * A segunda tela da parede: o material do almoxarifado, no rodízio com a da compra.
 *
 * <p>
 * Ela tem a mesma gramática da tela da compra — cartões de comando, esteira, radar e um quadro
 * de produtividade — porque quem lê de cinco metros aprende a parede uma vez só. E ela leva no
 * topo uma faixa fina da compra: o comprador que levanta a cabeça durante o minuto do material
 * não pode perder o número que importa para ele. Nenhum número nasce aqui: o bloco do
 * almoxarifado é o da tela da compra, o mês é o do Dashboard e o prazo de cada linha do radar
 * é o da fila do almoxarife.
 * </p>
 */
export function TelaDoMaterial({ compras, material }: { compras: CockpitDados; material: CockpitDoMaterial }) {
  const { kpis } = compras;
  const { almoxarifado: a } = material;
  const radar = ordenarRadarDoAlmoxarifado(material.radar);
  const noPrazoOk = material.atendidasNoPrazoPct !== null && slaAtingido(material.atendidasNoPrazoPct, 90);

  return (
    <>
      {/* a compra, em uma linha: a parede não esconde o alarme de uma tela enquanto mostra a outra */}
      <div className="flex items-center gap-6 rounded-xl border border-white/10 bg-fundo-card/90 px-5 py-2.5"
        data-testid="compras-na-tela-do-material">
        <span className="text-[12px] uppercase tracking-wider text-slate-400">A compra agora</span>
        <ParDaFaixa testid="mat-compras-risco" rotulo="de risco operacional"
          valor={`${kpis.taxaRiscoPct.toLocaleString('pt-BR')}%`}
          tom={kpis.taxaRiscoPct > 20 ? 'text-rose-400' : ''} />
        <ParDaFaixa testid="mat-compras-atrasados" rotulo="em atraso crítico"
          valor={String(kpis.itensAtrasados)} tom={kpis.itensAtrasados > 0 ? 'text-rose-400' : ''} />
        <ParDaFaixa testid="mat-compras-backlog" rotulo="itens no backlog" valor={compacto(kpis.backlogTotalItens)} />
        <div className="ml-auto">
          <ParDaFaixa testid="mat-compras-sla" rotulo="SLA da semana"
            valor={`${kpis.slaSemanalPct.toLocaleString('pt-BR')}%`}
            tom={slaAtingido(kpis.slaSemanalPct, kpis.metaSlaPct) ? 'text-emerald-400' : 'text-amber-400'} />
        </div>
      </div>

      {/* NÍVEL 2 — quatro cartões de comando do almoxarifado */}
      <div className="mt-3 grid grid-cols-4 gap-4">
        <CartaoVital icone="📦" titulo="Fila do almoxarifado" tom={TOM_DO_GARGALO[a.gargalo]}
          rodape={a.filaSolicitacoes === 0 ? 'nada a separar' : (
            <>{a.filaItens} itens · mais antiga {horasNaParede(a.horasDoMaisAntigo)}
              {a.maisAntigaNumero && ` · ${a.maisAntigaNumero}`}</>
          )}>
          <NumeroVivo valor={String(a.filaSolicitacoes)} />
        </CartaoVital>

        <CartaoVital icone="✋" titulo="Aguardando o centro" tom={TOM_DO_GARGALO[material.gargaloAguardando]}
          rodape={a.aguardandoAprovacao === 0 ? 'nada com o Nível 1' : (
            <>mais antiga {horasNaParede(material.horasDoMaisAntigoAguardando)}
              {material.maisAntigaAguardandoNumero && ` · ${material.maisAntigaAguardandoNumero}`}</>
          )}>
          <NumeroVivo valor={String(a.aguardandoAprovacao)} />
        </CartaoVital>

        <CartaoVital icone="⏰" titulo="Fora do prazo" tom={material.foraDoPrazo > 0 ? 'text-rose-400' : 'text-white'}
          rodape={`${material.emAtencao} a vencer · prazo de atendimento da família`}>
          <NumeroVivo valor={String(material.foraDoPrazo)} />
        </CartaoVital>

        {/* nulo é traço: 0% diria que todas atrasaram, e "nenhuma medida" é outra notícia */}
        <CartaoVital icone="✅" titulo="Atendidas no prazo"
          tom={material.atendidasNoPrazoPct === null ? 'text-white' : noPrazoOk ? 'text-emerald-400' : 'text-amber-400'}
          rodape={fraseDasMedidas(material.atendidasMedidas)}>
          <NumeroVivo valor={material.atendidasNoPrazoPct === null
            ? '—' : `${material.atendidasNoPrazoPct.toLocaleString('pt-BR')}%`} />
        </CartaoVital>
      </div>

      {/* NÍVEL 3 — a esteira do atendimento: de quem é a vez, em cada etapa */}
      <div className="mt-4 flex items-stretch gap-2" data-testid="esteira-do-material">
        <No rotulo="Aguardando o centro" quantidade={a.aguardandoAprovacao}
          horas={material.horasDoMaisAntigoAguardando} gargalo={material.gargaloAguardando} etapa="CENTRO" />
        <span aria-hidden className="self-center text-slate-700">──▶</span>
        <No rotulo="Fila do estoque" quantidade={a.filaSolicitacoes} horas={a.horasDoMaisAntigo} gargalo={a.gargalo}
          etapa="ESTOQUE" />
        <span aria-hidden className="self-center text-slate-700">──▶</span>
        <div className="flex-1 rounded-xl border border-slate-800 bg-fundo-card/90 px-4 py-3 text-slate-300"
          data-etapa="ATENDIDAS">
          <div className="text-[12px] uppercase tracking-wider text-slate-400">Atendidas hoje</div>
          <div className="flex items-baseline gap-3">
            <NumeroVivo valor={String(a.atendidasHoje)} className="font-mono text-3xl font-bold" />
            <span className="text-[12px]" data-testid="mat-mes">
              no mês: {material.atendidasNoMes} de {material.solicitadasNoMes} solicitadas
              {a.atendidoPeloEstoquePct !== null && ` · estoque atendeu ${a.atendidoPeloEstoquePct.toLocaleString('pt-BR')}%`}
              {' · '}{fraseDaRotaDeCompra(a.viraramCompraNoMes)}
            </span>
          </div>
        </div>
      </div>

      {/* NÍVEL 4 — radar (60%) e os rankings do mês (40%) */}
      <div className="mt-4 grid min-h-0 flex-1 grid-cols-5 gap-4">
        <section className="col-span-3 flex min-h-0 flex-col rounded-2xl border border-white/10 bg-fundo-card/90 p-4">
          <h2 className="pb-2 text-[13px] font-semibold uppercase tracking-wider text-slate-400">
            Radar do almoxarifado — ação imediata
          </h2>
          {radar.length === 0 ? (
            <p className="py-6 text-center text-emerald-300">Nenhuma solicitação fora do prazo.</p>
          ) : (
            <ListaRolante itens={radar.length}>
              <ul data-testid="radar-do-material">
                {radar.map((x) => (
                  <li key={`${x.tipo}-${x.id}`} data-alerta={x.tipo}
                    className={`mb-1.5 flex items-center gap-3 border-l-4 px-3 py-2 ${CLASSE_DO_ALERTA_DO_ALMOXARIFADO[x.tipo]}`}>
                    <span className="w-32 shrink-0 text-[12px] font-bold uppercase">{ROTULO_DO_ALERTA_DO_ALMOXARIFADO[x.tipo]}</span>
                    <span className="w-36 shrink-0 font-mono text-[15px]">{x.numero}</span>
                    <span className="min-w-0 flex-1 truncate text-[16px]">{x.descricao}</span>
                    <span className="w-36 shrink-0 truncate text-[13px] text-slate-400">{x.centroCusto}</span>
                    <span className="w-56 shrink-0 text-right font-semibold">{x.tempo}</span>
                    <span className="w-32 shrink-0 truncate text-right text-[13px] text-slate-400">{x.solicitante}</span>
                  </li>
                ))}
              </ul>
            </ListaRolante>
          )}
        </section>

        <section className="col-span-2 flex min-h-0 flex-col gap-3">
          <Ranking titulo="Centros que mais pedem no mês" linhas={material.porCentro} testid="ranking-centros" />
          <Ranking titulo="Famílias mais pedidas" linhas={material.porFamilia} testid="ranking-familias" />
          <Ranking titulo="Produtos mais pedidos" linhas={material.porProduto} testid="ranking-produtos" />
        </section>
      </div>
    </>
  );
}

/** Um nó da esteira do material, com a espera do mais antigo e a cor pela régua da compra. */
function No({ rotulo, quantidade, horas, gargalo, etapa }: {
  rotulo: string; quantidade: number; horas: number; gargalo: keyof typeof TOM_DO_GARGALO; etapa: string;
}) {
  return (
    <div className={`flex-1 rounded-xl border bg-fundo-card/90 px-4 py-3 ${CLASSE_DO_GARGALO[gargalo]}`}
      data-etapa={etapa} data-gargalo={gargalo}>
      <div className="text-[12px] uppercase tracking-wider text-slate-400">{rotulo}</div>
      <div className="flex items-baseline gap-2">
        <NumeroVivo valor={String(quantidade)} className="font-mono text-3xl font-bold" />
        {quantidade > 0 && <span className="text-[12px]">mais antiga: {horasNaParede(horas)}</span>}
      </div>
    </div>
  );
}

/**
 * Um ranking do mês, cinco linhas, lido de longe: o nome, quantas solicitações e a barra
 * relativa à primeira. A quantidade fica na dica porque famílias somam unidades diferentes.
 */
function Ranking({ titulo, linhas, testid }: { titulo: string; linhas: RankingDoMaterial[]; testid: string }) {
  const maior = Math.max(1, ...linhas.map((l) => l.solicitacoes));
  return (
    <div className="flex min-h-0 flex-1 flex-col rounded-2xl border border-white/10 bg-fundo-card/90 px-4 py-3"
      data-testid={testid}>
      <h2 className="pb-1.5 text-[13px] font-semibold uppercase tracking-wider text-slate-400">{titulo}</h2>
      {linhas.length === 0 ? (
        <p className="py-2 text-center text-slate-500">{SEM_RANKING}</p>
      ) : (
        <ul>
          {linhas.map((l) => (
            <li key={l.rotulo} className="mb-1" title={`${l.quantidade.toLocaleString('pt-BR')} un. pedidas`}>
              <div className="flex items-baseline justify-between gap-3 text-[14px]">
                <span className="min-w-0 truncate">{l.rotulo}</span>
                <span className="shrink-0 font-mono tabular-nums">{l.solicitacoes}</span>
              </div>
              <div className="mt-0.5 h-1 w-full overflow-hidden rounded-full bg-slate-800">
                <div className="h-full rounded-full bg-sky-400 transition-all duration-700"
                  style={{ width: `${Math.round(l.solicitacoes * 100 / maior)}%` }} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
