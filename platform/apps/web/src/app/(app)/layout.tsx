import type { CentroCusto } from '@trino/contratos';
import { api } from '@/lib/api';
import { contextoAtivo, exigirUsuario } from '@/lib/sessao';
import { Cabecalho } from '@/componentes/cabecalho';
import { Sidebar } from '@/componentes/sidebar';

/**
 * Casca das telas internas: exige sessão, carrega o usuário do token e os
 * centros de custo do tenant para o seletor de contexto.
 */
export default async function LayoutInterno({ children }: { children: React.ReactNode }) {
  const usuario = await exigirUsuario();
  const [centrosCusto, contexto] = await Promise.all([
    api<CentroCusto[]>('/catalogo/centros-custo').catch(() => [] as CentroCusto[]),
    contextoAtivo(),
  ]);

  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <Cabecalho usuario={usuario} centrosCusto={centrosCusto} centroCustoAtivo={contexto.centroCustoId} />
        <main className="flex-1 overflow-x-auto p-6">{children}</main>
      </div>
    </div>
  );
}
