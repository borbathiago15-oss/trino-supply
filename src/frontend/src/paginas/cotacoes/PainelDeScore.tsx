import { componenteDoScore, mapaDeScore } from '@/api/cotacoes';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Nota } from '@/componentes/formulario';
import { useCarregar } from '@/util/useCarregar';

/** Verde no primeiro colocado; o resto fica neutro para não parecer semáforo de aprovação. */
const classeDoScore = (lider: boolean) =>
  lider ? 'bg-ok-fundo text-ok' : 'bg-slate-100 text-slate-600';

/**
 * Comparação multicritério das propostas.
 *
 * <p>
 * O score já era calculado desde a fase P4 e nenhuma tela o mostrava: a conta existia, o
 * comprador escolhia sem ela. Este painel é ela aparecendo — e aparecendo <b>com os pesos
 * à vista</b>, porque nota sem critério é palpite com cara de medida.
 * </p>
 *
 * <p>
 * Ele <b>não decide nem bloqueia</b> (decisão C5). O menor preço continua destacado no mapa,
 * a escolha continua exigindo justificativa, e o score serve para uma coisa só: mostrar
 * quando o mais barato não é o melhor negócio — e o quanto.
 * </p>
 *
 * <p>
 * Quem monta a tela é que decide se ele existe: sem proposta não há disputa a comparar, e a
 * guarda mora lá em cima para o painel não ir buscar no servidor o que já se sabe vazio.
 * </p>
 */
export function PainelDeScore({ processoId }: { processoId: string }) {
  const { dados, erro, carregando } = useCarregar(
    (signal) => mapaDeScore(processoId, signal), [processoId]);

  return (
    <Painel titulo="Comparação multicritério">
      <Nota>
        {dados?.note ?? 'Score informativo: a escolha continua sendo do comprador, com justificativa.'}
        {dados && dados.criteria.length > 0 && (
          <>
            {' '}Pesos:{' '}
            {dados.criteria.map((c, i) => (
              <span key={c.code}>
                {i > 0 && ' · '}<strong>{c.label} {c.weightPct}%</strong>
              </span>
            ))}.
          </>
        )}
      </Nota>

      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}

      {dados && !dados.items.length && (
        <Vazio>Nenhuma proposta com total lançado para comparar.</Vazio>
      )}

      {dados && dados.items.length > 0 && (
        <div className="overflow-x-auto">
          <table data-testid="mapa-score" className="min-w-[720px]">
            <thead>
              <tr>
                <th>Fornecedor</th>
                <th>Score</th>
                {dados.criteria.map((c) => (
                  <th key={c.code} title={c.help}>
                    {c.label}
                    <div className="sub">peso {c.weightPct}%</div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {dados.items.map((linha, posicao) => (
                <tr key={linha.supplierId} data-fornecedor={linha.supplierId}>
                  <td className="whitespace-nowrap">
                    {linha.supplierName}
                    {posicao === 0 && dados.items.length > 1 && (
                      <div className="sub">melhor score</div>
                    )}
                  </td>
                  <td>
                    <Badge classe={classeDoScore(posicao === 0 && dados.items.length > 1)}>
                      {linha.score}
                    </Badge>
                  </td>
                  {dados.criteria.map((c) => {
                    const v = componenteDoScore(linha, c.code);
                    return (
                      <td key={c.code} className="whitespace-nowrap">
                        {v == null
                          ? <span className="sub" title="sem dado — este critério sai da conta deste fornecedor">—</span>
                          : v}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Painel>
  );
}
