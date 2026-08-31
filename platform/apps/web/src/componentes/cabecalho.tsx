import type { CentroCusto, UsuarioAutenticado } from '@trino/contratos';
import { definirCentroCusto, sair } from '@/lib/sessao';
import { SeletorContexto } from './seletor-contexto';

export function Cabecalho({
  usuario,
  centrosCusto,
  centroCustoAtivo,
}: {
  usuario: UsuarioAutenticado;
  centrosCusto: CentroCusto[];
  centroCustoAtivo: string | null;
}) {
  const iniciais = usuario.nome
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0]?.toUpperCase())
    .join('');

  return (
    <header className="flex h-14 items-center justify-between border-b border-slate-200 bg-white px-6">
      <SeletorContexto
        centrosCusto={centrosCusto}
        centroCustoAtivo={centroCustoAtivo}
        aoTrocar={definirCentroCusto}
      />

      <div className="flex items-center gap-4">
        <div className="text-right leading-tight">
          <p className="text-sm font-medium text-slate-800">{usuario.nome}</p>
          <p className="text-xs text-slate-500">{usuario.email}</p>
        </div>
        <div
          aria-hidden
          className="flex h-9 w-9 items-center justify-center rounded-full bg-trino-100 text-sm font-semibold text-trino-800"
        >
          {iniciais || '–'}
        </div>
        <form action={sair}>
          <button type="submit" className="text-sm text-slate-500 hover:text-slate-800">
            Sair
          </button>
        </form>
      </div>
    </header>
  );
}
