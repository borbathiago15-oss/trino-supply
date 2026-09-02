import {
  ehAdmin, podeAlmoxarifado, podeComprar, podeConduzirCotacao, podeCriarSc, podeDecidirSc, podeManterCatalogo,
  podePedirMaterial, podeTriar, podeVerCompliance, podeVerCotacao, temModulo, type Modulo, type Perfil,
} from '@/dominio/papeis';

/**
 * Uma tela do menu. `rota` aponta para uma tela já migrada (React);
 * `legado` aponta para a view do index.html — o link vai para `/#tela=<id>`.
 */
export interface ItemMenu {
  id: string;
  rotulo: string;
  rota?: string;
  legado?: string;
  modulo?: Modulo;
  mostrar?: (u: Perfil) => boolean;
}
export interface SubgrupoMenu { rotulo: string; filhos: ItemMenu[] }
export interface GrupoMenu { titulo: string | null; modulo?: Modulo; itens: (ItemMenu | SubgrupoMenu)[] }

export const ehSubgrupo = (x: ItemMenu | SubgrupoMenu): x is SubgrupoMenu => 'filhos' in x;

const sempre = () => true;

/** Espelho do `MENU` do legado; só "Pedidos de Compra" já vive no React. */
export const MENU: GrupoMenu[] = [
  { titulo: null, itens: [
    { id: 'supply-dash', rotulo: 'Dashboard de Suprimentos', legado: 'supply-dash', mostrar: sempre },
    { id: 'compliance', rotulo: 'Compliance', legado: 'compliance', modulo: 'COMPLIANCE', mostrar: podeVerCompliance },
    { id: 'insights', rotulo: 'Insights & Executivo', legado: 'insights', modulo: 'INSIGHTS', mostrar: podeVerCompliance },
  ]},
  { titulo: 'Solicitações de Compra', modulo: 'SOLICITACOES', itens: [
    { rotulo: 'Nova Solicitação', filhos: [
      { id: 'pr-new-unit', rotulo: 'Inclusão de SC', legado: 'pr-new-unit', mostrar: podeCriarSc },
      { id: 'pr-new-multi', rotulo: 'Solicitação em Lote', legado: 'pr-new-multi', mostrar: podeCriarSc },
    ]},
    { id: 'pr-mine', rotulo: 'Meus Pedidos', legado: 'pr-mine', mostrar: sempre },
    { id: 'pr-approvals', rotulo: 'Central de Aprovação', legado: 'pr-approvals', modulo: 'APROVACAO', mostrar: podeDecidirSc },
  ]},
  { titulo: 'Material', modulo: 'MATERIAL', itens: [
    { id: 'mr-new', rotulo: 'Solicitar Material', legado: 'mr-new', mostrar: podePedirMaterial },
    { id: 'mr-mine', rotulo: 'Minhas Solicitações', legado: 'mr-mine', mostrar: sempre },
  ]},
  { titulo: 'Estoque', modulo: 'ESTOQUE', itens: [
    { id: 'wh-queue', rotulo: 'Fila de Atendimento', legado: 'wh-queue', mostrar: podeAlmoxarifado },
    { id: 'wh-panel', rotulo: 'Painel de Atendimentos', legado: 'wh-panel', mostrar: podeAlmoxarifado },
  ]},
  { titulo: 'Compras', modulo: 'COMPRAS', itens: [
    { id: 'triage', rotulo: 'Gestão de Solicitações', legado: 'triage',
      mostrar: (u) => podeTriar(u) || podeComprar(u) || podeAlmoxarifado(u) },
    { rotulo: 'Cotações', filhos: [
      { id: 'rfq-queue', rotulo: 'Abrir Cotação', legado: 'rfq-queue', mostrar: podeVerCotacao },
      { id: 'quotations', rotulo: 'Processos de Cotação', legado: 'quotations', mostrar: podeVerCotacao },
    ]},
    { id: 'buy-orders', rotulo: 'Pedidos de Compra', rota: '/pedidos', mostrar: sempre },
    { id: 'contracts', rotulo: 'Contratos', legado: 'contracts', modulo: 'CONTRATOS', mostrar: (u) => podeComprar(u) || ehAdmin(u) },
    { id: 'scorecard', rotulo: 'Scorecard de Fornecedores', legado: 'scorecard', mostrar: (u) => podeComprar(u) || podeVerCompliance(u) },
  ]},
  { titulo: 'Cadastros', itens: [
    { rotulo: 'Produtos', filhos: [
      { id: 'products', rotulo: 'Cadastro de Produtos', legado: 'products', modulo: 'PRODUTOS', mostrar: sempre },
      { id: 'families', rotulo: 'Famílias de Produtos', legado: 'families', modulo: 'PRODUTOS', mostrar: podeManterCatalogo },
    ]},
    { rotulo: 'Estrutura da Empresa', filhos: [
      { id: 'cost-centers', rotulo: 'Centros de Custo', legado: 'cost-centers', modulo: 'CENTROS_CUSTO', mostrar: podeManterCatalogo },
      { id: 'company', rotulo: 'Empresas (CNPJs)', legado: 'company', mostrar: ehAdmin },
    ]},
    { id: 'users', rotulo: 'Usuários', legado: 'users', modulo: 'USUARIOS', mostrar: ehAdmin },
    { id: 'suppliers', rotulo: 'Fornecedores', legado: 'suppliers', modulo: 'FORNECEDORES', mostrar: podeComprar },
  ]},
];

// referência para o lint não reclamar de import sem uso quando o menu evoluir
void podeConduzirCotacao;

/** Mesma regra do `visibleItems()` do legado: módulo do grupo, módulo do item e capacidade. */
export function itensVisiveis(u: Perfil): GrupoMenu[] {
  const permitido = (i: ItemMenu) => (i.modulo ? temModulo(u, i.modulo) : true) && (i.mostrar ?? sempre)(u);
  const saida: GrupoMenu[] = [];
  for (const grupo of MENU) {
    if (grupo.modulo && !temModulo(u, grupo.modulo)) continue;
    const itens: (ItemMenu | SubgrupoMenu)[] = [];
    for (const item of grupo.itens) {
      if (ehSubgrupo(item)) {
        const filhos = item.filhos.filter(permitido);
        // subgrupo com uma tela só vira item simples: menos clique para chegar lá
        if (filhos.length > 1) itens.push({ ...item, filhos });
        else if (filhos.length === 1) itens.push(filhos[0]);
      } else if (permitido(item)) itens.push(item);
    }
    if (itens.length) saida.push({ ...grupo, itens });
  }
  return saida;
}

/** Endereço de um item: rota do React ou deep link na tela do legado. */
export const enderecoDe = (i: ItemMenu) => i.rota ?? `/#tela=${i.legado}`;
