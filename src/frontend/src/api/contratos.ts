import { api } from './cliente';
import { listarFornecedores, type ContratoFornecedor, type Fornecedor } from './fornecedores';

/** Fornecedor com contrato de parceria — a linha da tela de Contratos. */
export interface LinhaContrato {
  supplierId: string;
  supplierName: string;
  contrato: ContratoFornecedor;
  /** Quanto do teto já foi consumido, em %; `null` quando o contrato não tem teto. */
  percentualConsumido: number | null;
  /** Saldo abaixo de 20% do teto: o contrato está no fim. */
  saldoCritico: boolean;
}

export interface ResumoContratos {
  vigentes: number;
  foraDaVigencia: number;
  tetoTotal: number;
  consumido: number;
  saldo: number;
  percentualConsumido: number | null;
}

const percentual = (parte: number, total: number | null | undefined) =>
  total ? Math.min(100, Math.round((parte * 100) / total)) : null;

export function linhaDeContrato(f: Fornecedor): LinhaContrato {
  const c = f.contract;
  const consumido = c.consumed ?? 0;
  return {
    supplierId: f.id,
    supplierName: f.legalName,
    contrato: c,
    percentualConsumido: percentual(consumido, c.valueLimit),
    saldoCritico: c.balance != null && c.valueLimit != null && c.balance < c.valueLimit * 0.2,
  };
}

/** Só os KPIs vigentes entram na conta: contrato vencido não compõe o teto do período. */
export function resumoDeContratos(linhas: LinhaContrato[]): ResumoContratos {
  const vigentes = linhas.filter((l) => l.contrato.current);
  const tetoTotal = vigentes.reduce((t, l) => t + (l.contrato.valueLimit ?? 0), 0);
  const consumido = vigentes.reduce((t, l) => t + (l.contrato.consumed ?? 0), 0);
  return {
    vigentes: vigentes.length,
    foraDaVigencia: linhas.length - vigentes.length,
    tetoTotal,
    consumido,
    saldo: tetoTotal - consumido,
    percentualConsumido: percentual(consumido, tetoTotal),
  };
}

/**
 * A tela de contratos lê os fornecedores (é lá que o contrato é mantido) e
 * mostra só quem tem produtos contratados — o mesmo filtro do legado.
 */
export async function listarContratos(signal?: AbortSignal): Promise<LinhaContrato[]> {
  const fornecedores = await listarFornecedores(true, signal);
  return fornecedores
    .filter((f) => f.contract?.items?.length)
    .map(linhaDeContrato)
    .sort((a, b) => Number(b.contrato.current) - Number(a.contrato.current)
      || a.supplierName.localeCompare(b.supplierName, 'pt-BR'));
}

export interface Reajuste {
  id: string;
  requestedPercent: number;
  agreedPercent: number;
  baseValue: number;
  costAvoidance: number;
  appliedToPrices: boolean;
  notes: string | null;
  createdByLabel: string | null;
  createdAt: string;
}

export interface PleitoDeReajuste {
  requestedPercent: number;
  agreedPercent: number;
  notes: string | null;
  /** Reajusta os preços dos produtos do contrato pelo % fechado. */
  applyToPrices: boolean;
}

const contrato = (fornecedorId: string) => `/api/v1/suppliers/${fornecedorId}/contract/adjustments`;

export const registrarReajuste = (fornecedorId: string, pleito: PleitoDeReajuste) =>
  api<Reajuste>(contrato(fornecedorId), { method: 'POST', body: pleito });

export const historicoDeReajustes = (fornecedorId: string, signal?: AbortSignal) =>
  api<{ items: Reajuste[]; costAvoidanceTotal: number }>(contrato(fornecedorId), { signal });
