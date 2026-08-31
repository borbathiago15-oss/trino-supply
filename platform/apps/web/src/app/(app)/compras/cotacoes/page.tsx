import Link from 'next/link';
import type { Cotacao } from '@trino/contratos';
import { api } from '@/lib/api';
import { data } from '@/componentes/etiquetas';

const COR: Record<string, string> = {
  ABERTA: 'bg-blue-100 text-blue-800',
  ENCERRADA: 'bg-green-100 text-green-800',
  CANCELADA: 'bg-slate-200 text-slate-600',
};

export default async function PaginaCotacoes() {
  const cotacoes = await api<Cotacao[]>('/cotacoes');

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-slate-800">Cotações</h1>
        <p className="mt-1 text-sm text-slate-500">{cotacoes.length} processo(s) de cotação</p>
      </div>

      <div className="card overflow-hidden">
        <table className="min-w-full divide-y divide-slate-200">
          <thead className="bg-slate-50">
            <tr>
              <th className="cabecalho-tabela">Número</th>
              <th className="cabecalho-tabela">Situação</th>
              <th className="cabecalho-tabela">Limite de resposta</th>
              <th className="cabecalho-tabela sr-only">Ações</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 bg-white">
            {cotacoes.length === 0 ? (
              <tr>
                <td colSpan={4} className="px-4 py-10 text-center text-sm text-slate-500">
                  Nenhuma cotação aberta.
                </td>
              </tr>
            ) : null}
            {cotacoes.map((cotacao) => (
              <tr key={cotacao.id} className="hover:bg-slate-50">
                <td className="celula font-medium text-slate-800">{cotacao.numero}</td>
                <td className="celula">
                  <span className={`etiqueta ${COR[cotacao.status] ?? 'bg-slate-100'}`}>{cotacao.status}</span>
                </td>
                <td className="celula text-slate-600">{data(cotacao.dataLimiteResposta)}</td>
                <td className="celula text-right">
                  <Link href={`/compras/cotacoes/${cotacao.id}/equalizacao`} className="text-sm text-trino-700 hover:underline">
                    Mapa comparativo
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
