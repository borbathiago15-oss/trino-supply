import { notFound } from 'next/navigation';
import type { Cotacao, Fornecedor, PesosEqualizacao } from '@trino/contratos';
import { equalizar as calcularMatriz, prazoPagamentoDeCondicao } from '@trino/contratos';
import { api, apiOuNulo } from '@/lib/api';
import { data, dinheiro } from '@/componentes/etiquetas';
import { MapaComparativo } from './mapa';

export default async function PaginaEqualizacao({ params }: { params: { id: string } }) {
  const cotacao = await apiOuNulo<Cotacao>(`/cotacoes/${params.id}`);
  if (!cotacao) notFound();

  const fornecedores = await api<Fornecedor[]>('/fornecedores', { revalidate: 120 }).catch(() => [] as Fornecedor[]);
  const porId = new Map(fornecedores.map((f) => [f.id, f]));

  const encerrada = cotacao.equalizacao !== null;

  // Já equalizada: mostra a matriz GRAVADA (é o que foi decidido na época).
  // Ainda aberta: calcula a prévia com a mesma função do servidor, para o
  // comprador ver as notas antes de escolher.
  const pesos = cotacao.criterioEqualizacao as unknown as PesosEqualizacao;
  const matriz = encerrada
    ? cotacao.equalizacao!.notas.matriz
    : cotacao.propostas.length > 0
      ? calcularMatriz(
          cotacao.propostas.map((p) => ({
            id: p.id,
            fornecedorId: p.fornecedorId,
            valorTotal: Number(p.valorTotal ?? 0),
            frete: Number(p.frete),
            prazoEntregaDias: p.prazoEntregaDias,
            prazoPagamentoDias: prazoPagamentoDeCondicao(p.condicaoPagamento),
          })),
          pesos,
        ).matriz
      : [];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-slate-800">Mapa comparativo · {cotacao.numero}</h1>
        <p className="mt-1 text-sm text-slate-500">
          {cotacao.propostas.length} proposta(s) · limite de resposta {data(cotacao.dataLimiteResposta)} ·{' '}
          {encerrada ? 'cotação encerrada' : 'cotação aberta'}
        </p>
      </div>

      {cotacao.propostas.length === 0 ? (
        <div className="card p-10 text-center text-sm text-slate-500">
          Nenhuma proposta recebida ainda — a equalização fica disponível quando os fornecedores responderem.
        </div>
      ) : (
        <MapaComparativo
          cotacaoId={cotacao.id}
          propostas={cotacao.propostas}
          fornecedores={porId}
          matriz={matriz}
          pesos={pesos as unknown as Record<string, number>}
          encerrada={encerrada}
          vencedoraRegistrada={cotacao.equalizacao?.propostaVencedoraId ?? null}
          justificativaRegistrada={cotacao.equalizacao?.justificativaDesvio ?? null}
        />
      )}

      <section className="card p-5">
        <h2 className="text-sm font-semibold text-slate-700">Propostas recebidas</h2>
        <table className="mt-3 min-w-full divide-y divide-slate-200 text-sm">
          <thead>
            <tr>
              <th className="cabecalho-tabela">Fornecedor</th>
              <th className="cabecalho-tabela">Itens</th>
              <th className="cabecalho-tabela">Frete</th>
              <th className="cabecalho-tabela">Desconto</th>
              <th className="cabecalho-tabela">Total</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {cotacao.propostas.map((p) => (
              <tr key={p.id}>
                <td className="celula">{porId.get(p.fornecedorId)?.razaoSocial ?? p.fornecedorId.slice(0, 8)}</td>
                <td className="celula">{dinheiro(p.valorItens)}</td>
                <td className="celula">{dinheiro(p.frete)}</td>
                <td className="celula">{dinheiro(p.desconto)}</td>
                <td className="celula font-medium">{dinheiro(p.valorTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
