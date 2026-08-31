import type { Pedido } from '@trino/contratos';
import { api } from '@/lib/api';
import { Conferencia } from './conferencia';

export default async function PaginaRecebimento() {
  const pedidos = await api<Pedido[]>('/pedidos');
  // Só entra na conferência o que ainda pode receber mercadoria.
  const abertos = pedidos.filter((p) => !['RECEBIDO_TOTAL', 'CANCELADO'].includes(p.status));

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-slate-800">Conferência e entrada</h1>
        <p className="mt-1 text-sm text-slate-500">
          {abertos.length} pedido(s) aguardando recebimento · a conciliação 3-way confronta pedido, nota e recebido
        </p>
      </div>

      <Conferencia pedidos={abertos} />
    </div>
  );
}
