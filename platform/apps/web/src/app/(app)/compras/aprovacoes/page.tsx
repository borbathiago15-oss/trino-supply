import type { InstanciaAprovacao, Requisicao } from '@trino/contratos';
import { api } from '@/lib/api';
import { PainelAprovacoes, type ItemAprovacao } from './painel';

interface PendenteApi extends InstanciaAprovacao {
  etapaAtualId: string;
  nivelAtual: number;
  ehNivelFinal: boolean;
  requisicao: Requisicao | null;
}

export default async function PaginaAprovacoes() {
  const pendentes = await api<PendenteApi[]>('/aprovacoes/pendentes');

  const itens: ItemAprovacao[] = pendentes.map((p) => ({
    instancia: p,
    requisicao: p.requisicao,
    etapaAtualId: p.etapaAtualId,
    nivelAtual: p.nivelAtual,
    ehNivelFinal: p.ehNivelFinal,
  }));

  const comEstouro = itens.filter((i) => i.requisicao?.orcamentoEstourado).length;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-slate-800">Portal do aprovador</h1>
        <p className="mt-1 text-sm text-slate-500">
          {itens.length} requisição(ões) aguardando a sua decisão
          {comEstouro > 0 ? ` · ${comEstouro} com orçamento estourado` : ''}
        </p>
      </div>

      <PainelAprovacoes itens={itens} />
    </div>
  );
}
