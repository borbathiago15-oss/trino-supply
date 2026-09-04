import { melhorPreco, menorTotal, precoDoItem, propostasVigentes, type Processo } from '@/api/cotacoes';
import { Vazio } from '@/componentes/basicos';
import { data, moeda, quantidade } from '@/util/formato';

/** Célula que ganhou: menor preço do item, ou menor total da coluna. */
const DESTAQUE = 'bg-ok-fundo';

interface LinhaResumo {
  rotulo: string;
  valor: (p: Processo['proposals'][number]) => React.ReactNode;
  forte?: boolean;
}

/**
 * Mapa de cotação: itens nas linhas, fornecedores nas colunas. Só a última
 * versão de cada fornecedor entra — é o que o comprador compara. O melhor
 * preço de cada item e o menor total ficam destacados.
 */
export function MapaDeCotacao({ processo, aoBaixarAnexo }: {
  processo: Processo;
  aoBaixarAnexo: (documentId: string, nome: string) => void;
}) {
  const vigentes = propostasVigentes(processo);
  if (!vigentes.length)
    return <Vazio>Nenhuma proposta lançada ainda. Registre abaixo o que cada fornecedor respondeu.</Vazio>;

  const menor = menorTotal(processo);
  const varias = processo.sourcePrNumbers.length > 1;

  const resumo: LinhaResumo[] = [
    { rotulo: 'Frete', valor: (p) => (p.freightValue != null ? moeda(p.freightValue) : '—') },
    { rotulo: 'Impostos', valor: (p) => (p.taxValue != null ? moeda(p.taxValue) : '—') },
    { rotulo: 'Outros custos', valor: (p) => (p.otherCosts != null ? moeda(p.otherCosts) : '—') },
    { rotulo: 'Desconto', valor: (p) => (p.discountValue != null ? `− ${moeda(p.discountValue)}` : '—') },
  ];

  const condicoes: LinhaResumo[] = [
    { rotulo: 'Validade da proposta', valor: (p) => data(p.validUntil) },
    { rotulo: 'Moeda', valor: (p) => p.currency || 'BRL' },
    { rotulo: 'Condição de pagamento', valor: (p) => p.paymentTerms || '—' },
    { rotulo: 'Prazo para pagamento', valor: (p) => (p.paymentDays != null ? `${p.paymentDays} dias` : '—') },
    { rotulo: 'Prazo de entrega', valor: (p) => (p.deliveryDays != null ? `${p.deliveryDays} dias` : '—') },
    { rotulo: 'Observação', valor: (p) => p.notes || '—' },
    {
      rotulo: 'Anexo',
      valor: (p) => (p.attachmentDocumentId
        ? <button type="button" className="text-marca underline"
            onClick={() => aoBaixarAnexo(p.attachmentDocumentId!, p.attachmentFileName ?? 'anexo')}>
            {p.attachmentFileName ?? 'anexo'}
          </button>
        : '—'),
    },
  ];

  const Linhas = ({ linhas }: { linhas: LinhaResumo[] }) => (
    <>
      {linhas.map((l) => (
        <tr key={l.rotulo}>
          <td className={l.forte ? '' : 'sub'}>{l.forte ? <strong>{l.rotulo}</strong> : l.rotulo}</td>
          <td></td>
          {vigentes.map((p) => (
            <td key={p.id} className={l.forte ? '' : 'sub'}>{l.valor(p)}</td>
          ))}
        </tr>
      ))}
    </>
  );

  return (
    <div className="overflow-x-auto">
      <table data-testid="mapa-cotacao" className="min-w-[720px]">
        <thead>
          <tr>
            <th>Produto</th><th>Qtde</th>
            {vigentes.map((p) => (
              <th key={p.id}>
                {p.supplierName}{p.isWinner && ' 🏆'}
                <div className="sub">
                  v{p.version} · {p.submittedVia === 'PORTAL' ? 'portal' : `lançada por ${p.submittedByLabel ?? 'compras'}`}
                </div>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {processo.items.map((i) => {
            const melhor = melhorPreco(processo, i.id);
            return (
              <tr key={i.id} data-item={i.id}>
                <td className="min-w-[200px]">
                  {i.description}
                  {varias && i.sourcePrNumber && <div className="sub">{i.sourcePrNumber}</div>}
                </td>
                <td className="sub whitespace-nowrap">{quantidade(i.quantity)} {i.unitOfMeasure}</td>
                {vigentes.map((p) => {
                  const v = precoDoItem(p, i.id);
                  if (v == null) return <td key={p.id} className="sub">—</td>;
                  const ganhou = melhor != null && v === melhor && vigentes.length > 1;
                  return (
                    <td key={p.id} className={ganhou ? DESTAQUE : undefined}>
                      {moeda(v)}
                      <div className="sub">
                        total {moeda(v * i.quantity)}{ganhou && ' · melhor preço'}
                      </div>
                    </td>
                  );
                })}
              </tr>
            );
          })}

          <Linhas linhas={resumo} />

          <tr>
            <td><strong>Total da cotação</strong></td>
            <td></td>
            {vigentes.map((p) => {
              const ganhou = menor != null && p.totalValue === menor;
              return (
                <td key={p.id} className={ganhou ? DESTAQUE : undefined}>
                  <strong>{moeda(p.totalValue)}</strong>
                  {ganhou && <div className="sub">menor total</div>}
                </td>
              );
            })}
          </tr>

          <Linhas linhas={condicoes} />
        </tbody>
      </table>
    </div>
  );
}
