import {
  classeDoScore, concentracaoDeFornecedor, CONCENTRACAO, relatorioDeCompliance,
  type MediaCompliance,
} from '@/api/analytics';
import { ROTULO_RFQ } from '@/api/cotacoes';
import { Badge, Carregando, Erro, FaixaKpis, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Nota } from '@/componentes/formulario';
import { moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const ScoreBadge = ({ score }: { score: number }) => <Badge classe={classeDoScore(score)}>{score}</Badge>;

/** A barra sob o score usa a cor cheia da mesma faixa do badge. */
const corDaBarra = (s: number) => s === 100 ? 'bg-ok-forte' : s >= 70 ? 'bg-aviso-forte' : 'bg-perigo-forte';

/** Score com a barra de progresso embaixo: o número diz quanto, a barra diz quão longe de 100. */
function Score({ score }: { score: number }) {
  return (
    <div className="min-w-[96px]">
      <ScoreBadge score={score} />
      <div className="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-slate-100" aria-hidden>
        <div className={'h-1.5 rounded-full ' + corDaBarra(score)} style={{ width: `${Math.max(0, Math.min(100, score))}%` }} />
      </div>
    </div>
  );
}

/** −20 pts em badge, com o motivo ao lado e a evidência embaixo — em vez do número seco. */
function Penalidade({ pontos, rotulo, evidencia }: { pontos: number; rotulo: string; evidencia: string }) {
  return (
    <div className="mb-1.5 last:mb-0">
      <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
        <span className="inline-flex items-center gap-1 rounded border border-perigo-borda bg-perigo-fundo px-2 py-0.5 text-xs font-semibold text-perigo">
          <strong>−{pontos}</strong> pts
        </span>
        <span className="text-slate-600">{rotulo}</span>
      </div>
      <div className="sub">{evidencia}</div>
    </div>
  );
}

function Medias({ titulo, marca, linhas }: { titulo: string; marca: string; linhas: MediaCompliance[] }) {
  if (!linhas.length) return <Vazio>Sem dados.</Vazio>;
  return (
    <div className="overflow-x-auto">
      <table data-testid={marca}>
        <thead><tr><th>{titulo}</th><th>Processos</th><th>Score médio</th></tr></thead>
        <tbody>
          {linhas.map((g) => (
            <tr key={g.label}>
              <td>{g.label}</td>
              <td>{g.count}</td>
              <td className="whitespace-nowrap">
                <Score score={Math.round(g.averageScore)} />
                <span className="sub">{g.averageScore}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * De quem a empresa depende, por produto.
 *
 * A lista traz **só o que merece ação** — produto com compra bem dividida não aparece,
 * porque encher a tela de linha verde faria a linha vermelha se perder no meio. Pelo mesmo
 * motivo a recomendação vem em cada linha: "95% num fornecedor" sem dizer o que se faz com
 * isso é um número que ninguém aciona.
 */
function RiscoDeConcentracao() {
  const { dados, erro, carregando } = useCarregar(concentracaoDeFornecedor, []);
  const criticos = dados?.items.filter((i) => i.level === 'CRITICO').length ?? 0;

  return (
    <Painel titulo="Risco de concentração por produto">
      <Nota>
        A fatia é sobre o <strong>valor comprado</strong>, não sobre o número de pedidos: dez
        compras pequenas num fornecedor e uma enorme noutro não fazem do primeiro o dono da
        conta. Produto com menos de {dados?.minPurchases ?? 3} compras fica de fora — comprado
        uma vez, ele é 100% concentrado por aritmética, não por dependência.
      </Nota>

      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}

      {dados && !dados.items.length && (
        <Vazio>Nenhum produto com dependência relevante de um fornecedor só.</Vazio>
      )}

      {dados && dados.items.length > 0 && (
        <>
          <FaixaKpis>
            <Kpi rotulo="Produtos em risco" valor={dados.items.length}
              detalhe="com dependência acima do aceitável" />
            <Kpi rotulo="Críticos" valor={criticos} detalhe="fornecedor único ou acima de 90%" />
          </FaixaKpis>

          <div className="overflow-x-auto">
            <table data-testid="tabela-concentracao" className="min-w-[900px]">
              <thead>
                <tr>
                  <th>Produto</th><th>Fornecedor dominante</th><th>Fatia</th>
                  <th>Compras</th><th>Total</th><th>Nível</th><th>O que fazer</th>
                </tr>
              </thead>
              <tbody>
                {dados.items.map((i) => (
                  <tr key={i.catalogItemId} data-produto={i.catalogItemId}>
                    <td className="min-w-[220px]">{i.description}</td>
                    <td className="whitespace-nowrap">{i.topSupplier}</td>
                    <td className="whitespace-nowrap font-semibold">{i.topShare}%</td>
                    <td className="whitespace-nowrap">
                      {i.purchases}
                      <div className="sub">{i.suppliers} fornecedor(es)</div>
                    </td>
                    <td className="whitespace-nowrap">{moeda(i.total)}</td>
                    <td>
                      <Badge classe={CONCENTRACAO[i.level]?.classe ?? ''}>
                        {CONCENTRACAO[i.level]?.rotulo ?? i.level}
                      </Badge>
                    </td>
                    <td className="min-w-[300px] sub">{i.recommendation}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Painel>
  );
}

export function Compliance() {
  const { dados, erro, carregando } = useCarregar(relatorioDeCompliance, []);

  return (
    <>
      <Painel titulo="Compliance Score dos processos">
        <Nota>
          Todo processo recebe <strong>100 − penalizações</strong>, derivado dos fatos já gravados:
          sem cotação competitiva −25 · emergencial −20 · fornecedor não homologado −30 · demanda
          atendida após a data da necessidade −20 · acima do limite de alçada do centro −10. O score{' '}
          <strong>mede e expõe — nunca bloqueia</strong>.
        </Nota>

        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}

        {dados && (
          <>
            <FaixaKpis>
              <Kpi rotulo="Score médio" valor={dados.averageScore ?? '—'}
                detalhe={`${dados.evaluated} processo(s) avaliado(s)`} />
              <Kpi rotulo="100% conformes" valor={dados.fullCompliance} detalhe="sem nenhuma penalidade" />
              <Kpi rotulo="Com penalidade" valor={dados.evaluated - dados.fullCompliance}
                detalhe="detalhadas por processo abaixo" />
              <Kpi rotulo="Concluídos" valor={dados.concluded} detalhe="com O.C. registrada" />
            </FaixaKpis>

            {!dados.items.length && <Vazio>Nenhum processo de cotação para avaliar ainda.</Vazio>}

            {dados.items.length > 0 && (
              <div className="overflow-x-auto">
                <table data-testid="tabela-compliance" className="min-w-[900px]">
                  <thead>
                    <tr>
                      <th>Processo</th><th>CC</th><th>Comprador</th><th>Situação</th>
                      <th>Score</th><th>Penalidades (evidência)</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dados.items.map((i) => (
                      <tr key={i.quotationId} data-processo={i.number}>
                        <td className="whitespace-nowrap">
                          <span className="font-semibold">{i.number}</span>
                          <div className="sub">{i.kind}</div>
                        </td>
                        <td>{i.costCenter}</td>
                        <td>{i.buyerLabel}</td>
                        <td className="sub">{ROTULO_RFQ[i.status]?.rotulo ?? i.status}</td>
                        <td><Score score={i.score} /></td>
                        <td className="min-w-[320px]">
                          {!i.penalties.length && <span className="sub">nenhuma ✔</span>}
                          {i.penalties.map((p) => (
                            <Penalidade key={p.code} pontos={p.points} rotulo={p.label} evidencia={p.evidence} />
                          ))}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </Painel>

      {dados && (
        <Painel titulo="Médias por comprador e por centro de custo">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <Medias titulo="Comprador" marca="media-por-comprador" linhas={dados.byBuyer} />
            <Medias titulo="Centro de custo" marca="media-por-centro" linhas={dados.byCostCenter} />
          </div>
        </Painel>
      )}

      <RiscoDeConcentracao />
    </>
  );
}
