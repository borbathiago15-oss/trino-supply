import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { listarProcessos, propostasVigentes, ROTULO_RFQ, type Processo } from '@/api/cotacoes';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

/** As situações oferecidas no filtro, na ordem do fluxo. */
export const SITUACOES_FILTRO = [
  { valor: 'COTACAO_ABERTA', rotulo: 'Cotações em aberto / aguardando propostas' },
  { valor: 'EM_ANALISE', rotulo: 'Propostas recebidas / em análise' },
  { valor: 'AGUARDANDO_GERENTE', rotulo: 'Aguardando Aprovador 01 (Nível 1)' },
  { valor: 'AGUARDANDO_DIRETOR', rotulo: 'Aguardando Aprovador 02 (Nível 2)' },
  { valor: 'APROVADO_PARA_EMISSAO', rotulo: 'Aprovados — aguardando registro da O.C.' },
  { valor: 'OC_REGISTRADA', rotulo: 'O.C. registrada (SENIOR)' },
  { valor: 'REJEITADO', rotulo: 'Rejeitados' },
  { valor: 'CANCELADA', rotulo: 'Cancelados' },
];

/** O fornecedor que venceu, quando já houve escolha. */
export function vencedorDe(q: Processo): string | null {
  if (!q.selection) return null;
  return q.proposals.find((p) => p.id === q.selection!.winnerProposalId)?.supplierName ?? null;
}

/** Origem do processo: uma SC, ou o agrupamento de várias. */
export const origemDe = (q: Processo) =>
  q.sourcePrNumbers.length ? q.sourcePrNumbers : (q.sourcePrNumber ? [q.sourcePrNumber] : []);

export function ProcessosDeCotacao() {
  const [situacao, setSituacao] = useState('');
  const { dados, erro, carregando } = useCarregar(listarProcessos, []);

  const lista = useMemo(
    () => (situacao ? (dados ?? []).filter((q) => q.status === situacao) : (dados ?? [])),
    [dados, situacao],
  );

  return (
    <Painel titulo="Processos de cotação" acoes={
      <Campo id="rfq-situacao" rotulo="">
        <select id="rfq-situacao" aria-label="Situação do processo" className="w-auto"
          value={situacao} onChange={(e) => setSituacao(e.target.value)}>
          <option value="">Todos</option>
          {SITUACOES_FILTRO.map((s) => <option key={s.valor} value={s.valor}>{s.rotulo}</option>)}
        </select>
      </Campo>
    }>
      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}
      {dados && !lista.length && <Vazio>Nenhum processo de cotação neste filtro.</Vazio>}

      {lista.length > 0 && (
        <div className="overflow-x-auto">
          <table data-testid="tabela-processos" className="min-w-[860px]">
            <thead>
              <tr><th>Número</th><th>Origem</th><th>Fornecedores</th><th>Situação</th><th>Ações</th></tr>
            </thead>
            <tbody>
              {lista.map((q) => {
                const marca = ROTULO_RFQ[q.status] ?? { rotulo: q.status, classe: 'bg-slate-100 text-slate-600' };
                const origem = origemDe(q);
                const vencedor = vencedorDe(q);
                return (
                  <tr key={q.id} data-processo={q.number}>
                    <td className="whitespace-nowrap">
                      <span className="font-semibold">{q.number}</span>
                      <div className="sub">{q.kind}</div>
                    </td>
                    <td className="min-w-[180px]">
                      {origem.join(', ') || '—'}
                      <div className="sub">
                        CC: {q.costCenter}
                        {origem.length > 1 && ` · agrupamento de ${origem.length} SCs`}
                      </div>
                    </td>
                    <td className="min-w-[200px]">
                      {q.suppliers.length} convidado(s) · {propostasVigentes(q).length} proposta(s)
                      {vencedor && <div className="sub">Vencedor: {vencedor}</div>}
                      {q.splitAward && <div className="sub">compra dividida em {q.awards.length} família(s)</div>}
                    </td>
                    <td>
                      <Badge classe={marca.classe}>{marca.rotulo}</Badge>
                      {q.saving && q.saving.value > 0 && (
                        <div className="sub">ganho de {moeda(q.saving.value)}</div>
                      )}
                    </td>
                    <td>
                      <Link className="botao" to={`/cotacoes/${q.id}`}>Abrir processo</Link>
                    </td>
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
