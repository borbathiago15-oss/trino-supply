import {
  classeDoScore, relatorioDeCompliance, type MediaCompliance,
} from '@/api/analytics';
import { ROTULO_RFQ } from '@/api/cotacoes';
import { Badge, Carregando, Erro, FaixaKpis, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Nota } from '@/componentes/formulario';
import { useCarregar } from '@/util/useCarregar';

const ScoreBadge = ({ score }: { score: number }) => <Badge classe={classeDoScore(score)}>{score}</Badge>;

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
                <ScoreBadge score={Math.round(g.averageScore)} /> <span className="sub">{g.averageScore}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
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
                        <td><ScoreBadge score={i.score} /></td>
                        <td className="min-w-[320px]">
                          {!i.penalties.length && <span className="sub">nenhuma ✔</span>}
                          {i.penalties.map((p) => (
                            <div key={p.code} className="mb-1 last:mb-0">
                              <strong className="text-perigo">−{p.points}</strong> {p.label}
                              <div className="sub">{p.evidence}</div>
                            </div>
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
    </>
  );
}
