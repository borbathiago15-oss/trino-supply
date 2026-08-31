import type { CentroCusto, Requisicao, StatusRequisicao } from '@trino/contratos';
import { calcularFarol } from '@trino/contratos';
import { api } from '@/lib/api';
import { configuracaoSla } from '@/lib/sla';
import { contextoAtivo } from '@/lib/sessao';
import { Paginacao } from '@/componentes/paginacao';
import { Filtros } from './filtros';
import { TabelaEsteira, type LinhaEsteira } from './tabela';

const TAMANHO_PAGINA = 20;

/** Ordenações da esteira — a de SLA é a que o comprador usa no dia a dia. */
function ordenar(linhas: LinhaEsteira[], ordem: string): LinhaEsteira[] {
  const copia = [...linhas];
  switch (ordem) {
    case 'antigas':
      return copia.sort((a, b) => +new Date(a.requisicao.criadoEm) - +new Date(b.requisicao.criadoEm));
    case 'sla':
      // Mais crítico primeiro: quem tem menos tempo útil restante.
      return copia.sort((a, b) => a.farol.segundosRestantes - b.farol.segundosRestantes);
    case 'valor':
      return copia.sort((a, b) => Number(b.requisicao.valorEstimado) - Number(a.requisicao.valorEstimado));
    default:
      return copia.sort((a, b) => +new Date(b.requisicao.criadoEm) - +new Date(a.requisicao.criadoEm));
  }
}

export default async function PaginaEsteira({
  searchParams,
}: {
  searchParams: { status?: string; centroCustoId?: string; ordem?: string; pagina?: string };
}) {
  const contexto = await contextoAtivo();
  // O filtro explícito da tela vence o contexto do cabeçalho.
  const centroCustoId = searchParams.centroCustoId ?? contexto.centroCustoId ?? '';
  const status = (searchParams.status ?? '') as StatusRequisicao | '';
  const ordem = searchParams.ordem ?? 'recentes';
  const pagina = Math.max(1, Number(searchParams.pagina ?? 1));

  const consulta = new URLSearchParams();
  if (status) consulta.set('status', status);
  if (centroCustoId) consulta.set('centroCustoId', centroCustoId);

  const [requisicoes, centrosCusto, config] = await Promise.all([
    api<Requisicao[]>(`/requisicoes${consulta.size ? `?${consulta}` : ''}`),
    api<CentroCusto[]>('/catalogo/centros-custo', { revalidate: 300 }),
    configuracaoSla(),
  ]);

  const nomePorCentro = new Map(centrosCusto.map((cc) => [cc.id, `${cc.codigo} — ${cc.nome}`]));
  const agora = new Date();

  const todas: LinhaEsteira[] = requisicoes.map((requisicao) => ({
    requisicao,
    centroCusto: nomePorCentro.get(requisicao.centroCustoId) ?? '—',
    farol: calcularFarol(
      {
        status: requisicao.status,
        prioridade: requisicao.prioridade,
        submetidaEm: requisicao.submetidaEm,
        concluidaEm: requisicao.concluidaEm,
        slaPausadoEm: requisicao.slaPausadoEm,
        slaSegundosPausados: requisicao.slaSegundosPausados,
      },
      agora,
      config,
    ),
  }));

  const ordenadas = ordenar(todas, ordem);
  const daPagina = ordenadas.slice((pagina - 1) * TAMANHO_PAGINA, pagina * TAMANHO_PAGINA);

  const estouradas = todas.filter((l) => l.farol.cor === 'PRETO').length;
  const criticas = todas.filter((l) => l.farol.cor === 'VERMELHO').length;

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Esteira de solicitações</h1>
          <p className="mt-1 text-sm text-slate-500">
            {todas.length} requisição(ões) · {criticas} em situação crítica · {estouradas} com SLA estourado
          </p>
        </div>
      </div>

      <Filtros centrosCusto={centrosCusto} status={status} centroCustoId={centroCustoId} ordem={ordem} />

      <div>
        <TabelaEsteira linhas={daPagina} />
        <div className="card mt-0 rounded-t-none border-t-0">
          <Paginacao pagina={pagina} tamanho={TAMANHO_PAGINA} total={ordenadas.length} />
        </div>
      </div>
    </div>
  );
}
