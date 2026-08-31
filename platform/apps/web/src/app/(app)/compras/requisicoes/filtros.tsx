'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import type { CentroCusto, StatusRequisicao } from '@trino/contratos';
import { ROTULO_STATUS_REQUISICAO, STATUS_REQUISICAO } from '@trino/contratos';

/**
 * Filtros e ordenação vivem na URL: recarregar, compartilhar o link ou voltar
 * no histórico devolve exatamente a mesma lista.
 */
export function Filtros({
  centrosCusto,
  status,
  centroCustoId,
  ordem,
}: {
  centrosCusto: CentroCusto[];
  status: StatusRequisicao | '';
  centroCustoId: string;
  ordem: string;
}) {
  const router = useRouter();
  const caminho = usePathname();
  const parametros = useSearchParams();

  const atualizar = (chave: string, valor: string) => {
    const novos = new URLSearchParams(parametros.toString());
    if (valor) novos.set(chave, valor);
    else novos.delete(chave);
    novos.delete('pagina'); // filtro novo recomeça na primeira página
    router.push(`${caminho}?${novos.toString()}`);
  };

  return (
    <div className="flex flex-wrap items-end gap-3">
      <label className="text-sm">
        <span className="rotulo">Situação</span>
        <select className="campo mt-1 w-56" value={status} onChange={(e) => atualizar('status', e.target.value)}>
          <option value="">Todas</option>
          {STATUS_REQUISICAO.map((s) => (
            <option key={s} value={s}>
              {ROTULO_STATUS_REQUISICAO[s]}
            </option>
          ))}
        </select>
      </label>

      <label className="text-sm">
        <span className="rotulo">Centro de custo</span>
        <select
          className="campo mt-1 w-56"
          value={centroCustoId}
          onChange={(e) => atualizar('centroCustoId', e.target.value)}
        >
          <option value="">Todos</option>
          {centrosCusto.map((cc) => (
            <option key={cc.id} value={cc.id}>
              {cc.codigo} — {cc.nome}
            </option>
          ))}
        </select>
      </label>

      <label className="text-sm">
        <span className="rotulo">Ordenar por</span>
        <select className="campo mt-1 w-56" value={ordem} onChange={(e) => atualizar('ordem', e.target.value)}>
          <option value="recentes">Mais recentes</option>
          <option value="antigas">Mais antigas</option>
          <option value="sla">SLA mais crítico</option>
          <option value="valor">Maior valor</option>
        </select>
      </label>
    </div>
  );
}
