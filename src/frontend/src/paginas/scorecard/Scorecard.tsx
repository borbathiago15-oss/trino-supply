import { useState } from 'react';
import {
  CLASSE_FORNECEDOR, JANELAS, RISCO, scorecardDeFornecedores, type Janela, type LinhaScorecard,
} from '@/api/analytics';
import { Badge, Carregando, Erro, Painel, SeletorJanela, Vazio } from '@/componentes/basicos';
import { Nota } from '@/componentes/formulario';
import { moeda, quantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

/** Componente sem medição sai da conta do score — a tela diz isso em vez de mostrar 0%. */
const Percentual = ({ valor }: { valor: number | null }) =>
  valor == null ? <span className="sub">sem medição</span> : <>{valor}%</>;

export function Scorecard() {
  const [meses, setMeses] = useState<Janela>(6);
  const { dados, erro, carregando } = useCarregar(
    (signal) => scorecardDeFornecedores(meses, signal), [meses]);
  const linhas: LinhaScorecard[] = dados ?? [];

  return (
    <Painel titulo="Scorecard de Fornecedores"
      acoes={<SeletorJanela id="sc-meses" valor={meses} opcoes={JANELAS}
        aoMudar={(v) => setMeses(v as Janela)} />}>
      <Nota>
        Classe derivada dos fatos do período: <strong>OTIF (peso 50)</strong> ·{' '}
        <strong>qualidade = 1 − devolvido/entregue (peso 30)</strong> ·{' '}
        <strong>competitividade = vitórias/participações (peso 20)</strong>. Componente sem medição
        sai da conta. A ≥ 90 · B ≥ 75 · C ≥ 60 · D &lt; 60.
      </Nota>

      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}
      {dados && !linhas.length && <Vazio>Nenhum fornecedor com atividade no período.</Vazio>}

      {linhas.length > 0 && (
        <div className="mt-3 overflow-x-auto">
          <table data-testid="tabela-scorecard" className="min-w-[1040px]">
            <thead>
              <tr>
                <th>Fornecedor</th><th>Classe</th><th>Score</th><th>Risco</th><th>OTIF</th>
                <th>Qualidade</th><th>Competitividade</th><th>O.C.s</th><th>Valor comprado</th>
              </tr>
            </thead>
            <tbody>
              {linhas.map((s) => {
                const risco = RISCO[s.riskLevel] ?? { rotulo: s.riskLevel, classe: 'bg-slate-100 text-slate-600' };
                return (
                  <tr key={s.supplierId} data-fornecedor={s.supplierName}>
                    <td className="min-w-[180px]">{s.supplierName}</td>
                    <td>
                      {s.grade
                        ? <Badge classe={CLASSE_FORNECEDOR[s.grade].classe} title={CLASSE_FORNECEDOR[s.grade].faixa}>
                            {s.grade}
                          </Badge>
                        : <span className="sub">—</span>}
                    </td>
                    <td className="whitespace-nowrap">
                      {s.score != null ? s.score : <span className="sub">sem medição</span>}
                    </td>
                    <td className="min-w-[200px]">
                      <Badge classe={risco.classe}>{risco.rotulo} · {s.riskScore}</Badge>
                      {s.riskFactors.length > 0 && (
                        <div className="sub mt-1 whitespace-normal">{s.riskFactors.join(' · ')}</div>
                      )}
                    </td>
                    <td className="whitespace-nowrap">
                      <Percentual valor={s.otifPercent} />
                      {s.otifMeasured > 0 && <div className="sub">{s.otifMeasured} entrega(s) medida(s)</div>}
                    </td>
                    <td className="whitespace-nowrap">
                      <Percentual valor={s.qualityPercent} />
                      {s.rejectedQuantity > 0 && (
                        <div className="sub">
                          {quantidade(s.rejectedQuantity)} devolvido(s) de {quantidade(s.deliveredQuantity)}
                        </div>
                      )}
                    </td>
                    <td className="whitespace-nowrap">
                      <Percentual valor={s.winRatePercent} />
                      <div className="sub">{s.wins} vitória(s) em {s.proposals} cotação(ões)</div>
                    </td>
                    <td>{s.orders}</td>
                    <td className="whitespace-nowrap">{moeda(s.totalValue)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </Painel>
  );
}
