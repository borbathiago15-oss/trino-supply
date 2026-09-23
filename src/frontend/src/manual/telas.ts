import { ehSubgrupo, MENU, type ItemMenu } from '@/layout/menu';
import { MANUAIS, type Manual } from './conteudo';

/**
 * As telas que não estão no menu mas têm manual próprio: o detalhe de um processo não é a
 * lista dele, e é no detalhe que moram as regras mais difíceis (adjudicação, O.C. do ERP,
 * encerramento do ciclo).
 */
export const DETALHES: { chave: string; rotulo: string; padrao: RegExp }[] = [
  // /cotacoes/abrir é item do menu e resolve antes, pela rota exata
  { chave: 'quotation-detail', rotulo: 'Processo de Cotação', padrao: /^\/cotacoes\/[^/]+$/ },
  { chave: 'order-detail', rotulo: 'Pedido de Compra', padrao: /^\/pedidos\/[^/]+$/ },
  { chave: 'pdca-detail', rotulo: 'Ciclo de Melhoria', padrao: /^\/melhoria\/[^/]+$/ },
  { chave: 'plan-detail', rotulo: 'Plano de Ação', padrao: /^\/plano-acao\/[^/]+$/ },
  { chave: 'support-detail', rotulo: 'Chamado de Suporte', padrao: /^\/suporte\/[^/]+$/ },
];

/** Toda tela do menu, inclusive as que o papel de quem olha esconde. */
export const folhasDoMenu = (): ItemMenu[] =>
  MENU.flatMap((g) => g.itens.flatMap((i) => (ehSubgrupo(i) ? i.filhos : [i])));

export interface TelaAtual {
  /** `id` do item do menu ou chave do detalhe — é o que identifica a tela no chamado. */
  chave: string;
  /** O nome da tela como o menu o chama: é o que o atendente lê no chamado. */
  rotulo: string;
  manual: Manual | null;
}

/**
 * De qual tela é esta rota. A rota exata do menu vence; depois o detalhe; e por último a raiz
 * (`/torre?x=1` e `/torre/qualquer` caem na Torre), como o título do cabeçalho já faz.
 */
export function telaDaRota(pathname: string): TelaAtual {
  const caminho = pathname.replace(/\/+$/, '') || '/';
  const folhas = folhasDoMenu();
  const achar = (item: ItemMenu | undefined): TelaAtual | null =>
    item ? { chave: item.id, rotulo: item.rotulo, manual: MANUAIS[item.id] ?? null } : null;

  const exata = achar(folhas.find((i) => i.rota === caminho));
  if (exata) return exata;
  const detalhe = DETALHES.find((d) => d.padrao.test(caminho));
  if (detalhe) return { chave: detalhe.chave, rotulo: detalhe.rotulo, manual: MANUAIS[detalhe.chave] ?? null };
  const raiz = '/' + (caminho.split('/')[1] ?? '');
  return achar(folhas.find((i) => i.rota === raiz)) ?? { chave: 'geral', rotulo: 'Trino Supply', manual: null };
}
