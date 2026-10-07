import { api } from './cliente';

/** O prazo de atendimento de uma família. `family` nulo é o **padrão**. */
export interface PrazoDeAtendimento {
  family: string | null;
  maxDays: number;
  /** A família não definiu o seu e segue o padrão — mudar o padrão move esta junto. */
  inherited: boolean;
  updatedAt: string;
  updatedByLabel: string;
}

export interface PrazosDeAtendimento {
  /** A partir de que percentual do prazo a linha entra em atenção. Quem define é o servidor. */
  warnAtPercent: number;
  /** O prazo de fábrica, para a tela dizer de onde veio o número de quem nunca configurou. */
  defaultDays: number;
  canEdit: boolean;
  items: PrazoDeAtendimento[];
}

export const lerPrazosDeAtendimento = async (signal?: AbortSignal) => {
  const r = await api<PrazosDeAtendimento>('/api/v1/material-sla', { signal });
  return { ...r, items: r.items ?? [] };
};

/**
 * Grava os prazos. `herdar` lista as famílias que voltam a seguir o padrão — é como se desfaz
 * uma exceção sem copiar o número do padrão para cá, o que a congelaria.
 *
 * <p>
 * A chave vazia é o padrão, igual ao servidor: um campo separado para ele faria a tela ter
 * dois caminhos de gravação para a mesma coisa.
 * </p>
 */
export const salvarPrazosDeAtendimento = (
  dias: Record<string, number>, herdar: string[] = [],
) => api<{ items: { family: string | null; maxDays: number }[] }>('/api/v1/material-sla', {
  method: 'PUT', body: { days: dias, inherit: herdar },
});

/** A chave com que a tela e o servidor falam da mesma linha. O padrão é a string vazia. */
export const chaveDoPrazo = (p: PrazoDeAtendimento) => p.family ?? '';

/**
 * Como o prazo será cobrado na fila. Zero é desligado, e a tela diz isso em vez de mostrar
 * "0 dias" — que se leria como "tem de sair hoje", o oposto do que zero significa.
 */
export const leituraDoAtendimento = (dias: number, avisoEmPct: number): string =>
  dias <= 0
    ? 'sem cobrança de tempo nesta família'
    : `atenção a partir de ${Math.max(1, Math.ceil(dias * avisoEmPct / 100))} dia(s), `
      + `estouro depois de ${dias}`;

/** Como a fila mostra o prazo de uma solicitação. Vem junto da linha, derivado no servidor. */
export interface SlaDoAtendimento {
  maxDays: number;
  days: number | null;
  status: 'OK' | 'ATENCAO' | 'ESTOURADO' | null;
  /** Qual família impôs o prazo — a solicitação tem itens de várias, e vale a mais curta. */
  family: string | null;
}

export const ROTULO_DO_SLA: Record<'OK' | 'ATENCAO' | 'ESTOURADO', { rotulo: string; classe: string }> = {
  OK: { rotulo: 'no prazo', classe: 'bg-ok-fundo text-ok' },
  ATENCAO: { rotulo: 'no limite', classe: 'bg-aviso-fundo text-aviso' },
  ESTOURADO: { rotulo: 'prazo estourado', classe: 'bg-perigo-fundo text-perigo' },
};

/**
 * A frase do prazo numa linha da fila. Diz **de onde saiu o número**: a solicitação tem itens
 * de várias famílias e vale a mais curta, então "2 dias" sem a família seria um número que o
 * almoxarife não tem como conferir.
 */
export function textoDoSla(sla: SlaDoAtendimento | null | undefined): string | null {
  if (!sla) return null;
  const familia = sla.family ? ` (${sla.family})` : '';
  if (sla.days == null) return `prazo de ${sla.maxDays} dia(s)${familia}`;
  return `${sla.days} de ${sla.maxDays} dia(s)${familia}`;
}
