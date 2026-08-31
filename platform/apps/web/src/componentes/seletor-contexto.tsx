'use client';

import { useRouter } from 'next/navigation';
import { useTransition } from 'react';
import type { CentroCusto } from '@trino/contratos';

/**
 * Seletor do centro de custo ativo. NÃO troca de tenant: o tenant vem do JWT e
 * o backend só aceita o do token — trocar de empresa exige novo login, e é
 * assim que o isolamento se mantém honesto.
 */
export function SeletorContexto({
  centrosCusto,
  centroCustoAtivo,
  aoTrocar,
}: {
  centrosCusto: CentroCusto[];
  centroCustoAtivo: string | null;
  aoTrocar: (id: string | null) => Promise<void>;
}) {
  const router = useRouter();
  const [pendente, iniciarTransicao] = useTransition();

  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-slate-500">Centro de custo</span>
      <select
        className="campo w-56 py-1.5"
        value={centroCustoAtivo ?? ''}
        disabled={pendente}
        onChange={(evento) => {
          const valor = evento.target.value || null;
          iniciarTransicao(async () => {
            await aoTrocar(valor);
            router.refresh();
          });
        }}
      >
        <option value="">Todos</option>
        {centrosCusto.map((cc) => (
          <option key={cc.id} value={cc.id}>
            {cc.codigo} — {cc.nome}
          </option>
        ))}
      </select>
    </label>
  );
}
