import { podeAprovarAlgo, podeVerAnalises, podeVerRelatorios, type Perfil } from '@/dominio/papeis';

/**
 * As três telas do app, na ordem das abas. Quem vê cada uma é **quem o servidor deixa ler**:
 * a Central é de quem aprova (`podeAprovarAlgo`), a Visão da diretoria é o relatório executivo
 * (`podeVerRelatorios`, a régua de `RelatorioExecutivoService.CanView`) e o Dashboard é
 * `podeVerAnalises` (`AnalyticsService.CanViewSupply`).
 *
 * Não é a regra do menu do sistema, de propósito. O menu esconde o Dashboard do diretor e a
 * Visão da diretoria do comprador por **navegação** — cada papel começa o dia na tela dele —,
 * não por permissão: o servidor entrega os dois a qualquer um deles. O app foi pedido com as
 * três telas para quem analisa e aprova, e a primeira versão, seguindo o menu, mostrava duas
 * abas ao diretor e duas ao comprador. Uma aba que o servidor recusaria continua não existindo:
 * quem não tem nenhuma recebe o caminho para o sistema completo.
 */
export const ABAS_DO_APP = [
  { id: 'pr-approvals', rotulo: 'Aprovar', rota: '/app/aprovacoes', mostrar: podeAprovarAlgo },
  { id: 'director-view', rotulo: 'Diretoria', rota: '/app/diretoria', mostrar: podeVerRelatorios },
  { id: 'supply-dash', rotulo: 'Dashboard', rota: '/app/painel', mostrar: podeVerAnalises },
] as const;

export type AbaDoApp = (typeof ABAS_DO_APP)[number];

export function abasDoApp(u: Perfil): AbaDoApp[] {
  return ABAS_DO_APP.filter((a) => a.mostrar(u));
}
