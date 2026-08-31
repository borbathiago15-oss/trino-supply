import type { Prioridade, StatusRequisicao } from '@trino/contratos';
import { ROTULO_PRIORIDADE, ROTULO_STATUS_REQUISICAO } from '@trino/contratos';

const COR_STATUS: Record<StatusRequisicao, string> = {
  RASCUNHO: 'bg-slate-100 text-slate-700',
  SUBMETIDA: 'bg-blue-100 text-blue-800',
  EM_TRIAGEM: 'bg-indigo-100 text-indigo-800',
  DEVOLVIDA_AJUSTE: 'bg-amber-100 text-amber-800',
  EM_COTACAO: 'bg-cyan-100 text-cyan-800',
  COTADA: 'bg-teal-100 text-teal-800',
  APROVACAO_ALCADA: 'bg-purple-100 text-purple-800',
  PEDIDO_GERADO: 'bg-emerald-100 text-emerald-800',
  RECEBIDA_PARCIAL: 'bg-lime-100 text-lime-800',
  RECEBIDA_TOTAL: 'bg-green-100 text-green-800',
  REJEITADA: 'bg-red-100 text-red-800',
  CANCELADA: 'bg-slate-200 text-slate-600',
};

const COR_PRIORIDADE: Record<Prioridade, string> = {
  BAIXA: 'bg-slate-100 text-slate-600',
  NORMAL: 'bg-slate-100 text-slate-700',
  ALTA: 'bg-orange-100 text-orange-800',
  EMERGENCIAL: 'bg-red-100 text-red-800',
};

export const EtiquetaStatus = ({ status }: { status: StatusRequisicao }) => (
  <span className={`etiqueta ${COR_STATUS[status]}`}>{ROTULO_STATUS_REQUISICAO[status]}</span>
);

export const EtiquetaPrioridade = ({ prioridade }: { prioridade: Prioridade }) => (
  <span className={`etiqueta ${COR_PRIORIDADE[prioridade]}`}>{ROTULO_PRIORIDADE[prioridade]}</span>
);

export const dinheiro = (valor: string | number | null | undefined): string =>
  new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(Number(valor ?? 0));

export const dataHora = (valor: string | Date | null | undefined): string =>
  valor ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(valor)) : '—';

export const data = (valor: string | Date | null | undefined): string =>
  valor ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short' }).format(new Date(valor)) : '—';
