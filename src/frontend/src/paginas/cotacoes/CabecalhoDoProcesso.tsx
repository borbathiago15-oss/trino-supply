import { Link } from 'react-router-dom';
import { ROTULO_RFQ, type Processo } from '@/api/cotacoes';
import { Badge, Dado, Painel } from '@/componentes/basicos';
import { Nota } from '@/componentes/formulario';
import { data, dataHora, quantidade } from '@/util/formato';
import { ProximoPasso } from './ProximoPasso';
import { CaminhoDoProcesso } from './CaminhoDoProcesso';

/**
 * A identificação do processo e os itens que ele cota — a parte da tela que só
 * lê o processo, sem ação nenhuma.
 *
 * Saiu de `ProcessoDetalhe.tsx` no INT-B. O markup é o mesmo; o que muda é
 * poder abrir o arquivo do cabeçalho sem passar pelas ações, pela negociação e
 * pelo mapa.
 */
export function CabecalhoDoProcesso({ processo: q, usuarioId }: { processo: Processo; usuarioId?: string }) {
  const marca = ROTULO_RFQ[q.status] ?? { rotulo: q.status, classe: 'bg-slate-100 text-slate-600' };
  const origem = q.sourcePrNumbers.length ? q.sourcePrNumbers : (q.sourcePrNumber ? [q.sourcePrNumber] : []);
  // "Aguardando Aprovador 01" diz a etapa; o nome ao lado diz de quem — sem rolar até o caminho
  const atual = q.caminho?.find((e) => e.situacao === 'atual');
  const deQuem = !atual?.quem ? null
    : atual.impasse ? 'ninguém da lista pode aprovar'
    : atual.semAprovador ? atual.quem
    : `aguardando ${atual.quem}`;

  return (
    <Painel titulo={
      <span className="flex flex-wrap items-center gap-2">
        {q.number} <Badge classe={marca.classe}>{marca.rotulo}</Badge>
        {/* a marca fica depois de virar compra: é o que diz ao Nível 1 de onde a compra veio */}
        {q.isBudget && (
          <Badge classe="ml-1 bg-teal-50 text-teal-800"
            title={q.budgetConvertedAt ? `Virou compra — ${q.budgetConvertedByLabel ?? ''}` : 'Processo de orçamento'}>
            {q.budgetConvertedAt ? 'Nasceu como orçamento' : 'Orçamento'}
          </Badge>
        )}
        {deQuem && <span data-testid="de-quem" className="text-[13px] font-normal text-texto-suave">· {deQuem}</span>}
      </span>
    } acoes={<Link className="botao-secundario" to="/cotacoes">← Voltar</Link>}>
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <Dado rotulo="Tipo">{q.kind}</Dado>
        <Dado rotulo="Origem">{origem.join(', ') || '—'}</Dado>
        <Dado rotulo="Centro de custo">{q.costCenter}</Dado>
        <Dado rotulo="Prazo">{data(q.deadline)}</Dado>
      </div>
      <Nota>Aberta por {q.createdByLabel ?? '—'} em {dataHora(q.createdAt)}.</Nota>
      <ProximoPasso processo={q} usuarioId={usuarioId} />
      <CaminhoDoProcesso etapas={q.caminho ?? []} centro={q.costCenter} />
      {q.decisionReason && (
        <p className="mt-2 rounded-lg bg-aviso-fundo px-3 py-2 text-[13px] text-aviso">
          Último motivo registrado: <strong>{q.decisionReason}</strong>
        </p>
      )}

      <h3 className="mb-2 mt-4 text-[14px] font-bold">Itens da cotação</h3>
      <div className="overflow-x-auto">
        <table data-testid="itens-cotacao">
          <thead>
            <tr>
              <th>#</th><th>Descrição</th>
              {q.families.length > 1 && <th>Família</th>}
              <th>Qtd</th><th>Unid.</th>
              {origem.length > 1 && <th>SC de origem</th>}
            </tr>
          </thead>
          <tbody>
            {q.items.map((i) => {
              const award = q.awards.find((a) => a.family === i.family);
              return (
                <tr key={i.id}>
                  <td>{i.sequence}</td>
                  <td>
                    {i.description}
                    {i.catalogCode && <div className="sub">{i.catalogCode}</div>}
                  </td>
                  {q.families.length > 1 && (
                    <td>
                      {i.family || '—'}
                      {award && <div className="sub">{award.supplierName}</div>}
                    </td>
                  )}
                  <td className="whitespace-nowrap">{quantidade(i.quantity)}</td>
                  <td>{i.unitOfMeasure}</td>
                  {origem.length > 1 && <td className="sub">{i.sourcePrNumber || '—'}</td>}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Painel>
  );
}
