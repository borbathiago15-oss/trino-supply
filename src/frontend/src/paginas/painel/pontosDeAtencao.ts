import type { DashboardSuprimentos as Dados } from '@/api/painel';
import { quantidade } from '@/util/formato';

export interface PontoDeAtencao {
  chave: string;
  texto: string;
  /** Para onde ir para agir; sem destino, a resposta está no próprio painel. */
  destino: string | null;
  tom: 'perigo' | 'atencao';
}

/**
 * O que, no período filtrado, pede uma ação — derivado do <b>mesmo</b> `dados` que os cards e
 * as tabelas mostram, sem consulta própria: um ponto de atenção que contasse por outra regra
 * discordaria do card ao lado, e o primeiro a notar pararia de confiar nos dois.
 *
 * A ordem é a da urgência: o que já está fora (atraso, meta longe, OTIF abaixo de 70%) antes do
 * que está perto de sair. Não é lista de tudo o que existe: a coleção que chama atenção com
 * qualquer coisa é a que ninguém mais lê.
 */
export function pontosDeAtencao(dados: Dados): PontoDeAtencao[] {
  const k = dados.kpis;
  const pontos: PontoDeAtencao[] = [];

  if (k.overdue > 0) pontos.push({
    chave: 'sc-atrasadas', tom: 'perigo', destino: '/torre',
    texto: `${quantidade(k.overdue)} SC(s) com a data de necessidade vencida e a compra ainda aberta.`,
  });

  for (const [chave, g] of Object.entries(dados.goals ?? {})) {
    if (g.faixa !== 'fora') continue;
    pontos.push({
      chave: `meta-${chave}`, tom: 'perigo', destino: null,
      texto: `${ROTULO_META[chave] ?? chave} longe da meta: ${g.atingimento.toLocaleString('pt-BR')}% da meta do período.`,
    });
  }

  const otifBaixo = (dados.supplierTable ?? []).filter((s) => s.otifPercent != null && s.otifPercent < 70);
  if (otifBaixo.length) pontos.push({
    chave: 'otif-baixo', tom: 'perigo', destino: '/scorecard',
    texto: `OTIF abaixo de 70%: ${otifBaixo.slice(0, 3).map((s) => `${s.supplier} (${s.otifPercent}%)`).join(', ')}`
      + (otifBaixo.length > 3 ? ` e mais ${otifBaixo.length - 3}.` : '.'),
  });

  if (k.poLate > 0) pontos.push({
    chave: 'pedidos-parados', tom: 'atencao', destino: '/pedidos',
    texto: `${quantidade(k.poLate)} pedido(s) em aberto há mais de 7 dias sem recebimento.`,
  });

  for (const f of dados.leadTimes ?? [])
    for (const e of f.stages)
      if (e.late && e.target != null && e.actual != null) pontos.push({
        chave: `prazo-${f.family}-${e.stage}`, tom: 'atencao', destino: '/torre',
        texto: `${f.family}: ${e.stage.toLowerCase()} leva ${e.actual}d contra a meta de ${e.target}d.`,
      });

  return pontos.slice(0, 8);
}

const ROTULO_META: Record<string, string> = {
  poTotalValue: 'Valor comprado', saving: 'Saving', otif: 'OTIF',
  avgApprovalDays: 'Tempo de aprovação', avgReceiveDays: 'Tempo de entrega', overdue: 'SCs em atraso',
};
