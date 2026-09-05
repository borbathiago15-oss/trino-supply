import {
  ehAdmin, podeAlmoxarifado, podeComprar, podeConduzirCotacao, podeCriarSc, podeDecidirSc, podeManterCatalogo,
  podePedirMaterial, podeTriar, podeVerCompliance, podeVerCotacao, temModulo, type Modulo, type Perfil,
} from '@/dominio/papeis';

/** Uma tela do menu. Todas vivem no React desde a remoção do sistema clássico. */
export interface ItemMenu {
  id: string;
  rotulo: string;
  rota: string;
  modulo?: Modulo;
  mostrar?: (u: Perfil) => boolean;
}
export interface SubgrupoMenu { rotulo: string; filhos: ItemMenu[] }
export interface GrupoMenu { titulo: string | null; modulo?: Modulo; itens: (ItemMenu | SubgrupoMenu)[] }

export const ehSubgrupo = (x: ItemMenu | SubgrupoMenu): x is SubgrupoMenu => 'filhos' in x;

const sempre = () => true;

/** O menu do sistema: cada item aponta para a sua rota. */
export const MENU: GrupoMenu[] = [
  { titulo: null, itens: [
    { id: 'supply-dash', rotulo: 'Dashboard de Suprimentos', rota: '/painel', mostrar: sempre },
    { id: 'compliance', rotulo: 'Compliance', rota: '/compliance', modulo: 'COMPLIANCE', mostrar: podeVerCompliance },
    { id: 'insights', rotulo: 'Insights & Executivo', rota: '/insights', modulo: 'INSIGHTS', mostrar: podeVerCompliance },
  ]},
  { titulo: 'Solicitações de Compra', modulo: 'SOLICITACOES', itens: [
    { rotulo: 'Nova Solicitação', filhos: [
      { id: 'pr-new-unit', rotulo: 'Inclusão de SC', rota: '/solicitacoes/nova', mostrar: podeCriarSc },
      { id: 'pr-new-multi', rotulo: 'Solicitação em Lote', rota: '/solicitacoes/lote', mostrar: podeCriarSc },
    ]},
    { id: 'pr-mine', rotulo: 'Meus Pedidos', rota: '/solicitacoes', mostrar: sempre },
    { id: 'pr-approvals', rotulo: 'Central de Aprovação', rota: '/aprovacoes', modulo: 'APROVACAO', mostrar: podeDecidirSc },
  ]},
  { titulo: 'Material', modulo: 'MATERIAL', itens: [
    { id: 'mr-new', rotulo: 'Solicitar Material', rota: '/material/nova', mostrar: podePedirMaterial },
    { id: 'mr-mine', rotulo: 'Minhas Solicitações', rota: '/material', mostrar: sempre },
  ]},
  { titulo: 'Estoque', modulo: 'ESTOQUE', itens: [
    { id: 'wh-queue', rotulo: 'Fila de Atendimento', rota: '/estoque/fila', mostrar: podeAlmoxarifado },
    { id: 'wh-panel', rotulo: 'Painel de Atendimentos', rota: '/estoque/atendimentos', mostrar: podeAlmoxarifado },
  ]},
  { titulo: 'Compras', modulo: 'COMPRAS', itens: [
    { id: 'triage', rotulo: 'Gestão de Solicitações', rota: '/gestao-solicitacoes',
      mostrar: (u) => podeTriar(u) || podeComprar(u) || podeAlmoxarifado(u) },
    { rotulo: 'Cotações', filhos: [
      { id: 'rfq-queue', rotulo: 'Abrir Cotação', rota: '/cotacoes/abrir', mostrar: podeVerCotacao },
      { id: 'quotations', rotulo: 'Processos de Cotação', rota: '/cotacoes', mostrar: podeVerCotacao },
    ]},
    { id: 'buy-orders', rotulo: 'Pedidos de Compra', rota: '/pedidos', mostrar: sempre },
    { id: 'contracts', rotulo: 'Contratos', rota: '/contratos', modulo: 'CONTRATOS', mostrar: (u) => podeComprar(u) || ehAdmin(u) },
    { id: 'scorecard', rotulo: 'Scorecard de Fornecedores', rota: '/scorecard', mostrar: (u) => podeComprar(u) || podeVerCompliance(u) },
  ]},
  { titulo: 'Cadastros', itens: [
    { rotulo: 'Produtos', filhos: [
      { id: 'products', rotulo: 'Cadastro de Produtos', rota: '/produtos', modulo: 'PRODUTOS', mostrar: sempre },
      { id: 'families', rotulo: 'Famílias de Produtos', rota: '/familias', modulo: 'PRODUTOS', mostrar: podeManterCatalogo },
    ]},
    { rotulo: 'Estrutura da Empresa', filhos: [
      { id: 'cost-centers', rotulo: 'Centros de Custo', rota: '/centros-custo', modulo: 'CENTROS_CUSTO', mostrar: podeManterCatalogo },
      { id: 'company', rotulo: 'Empresas (CNPJs)', rota: '/empresas', mostrar: ehAdmin },
    ]},
    { id: 'users', rotulo: 'Usuários', rota: '/usuarios', modulo: 'USUARIOS', mostrar: ehAdmin },
    { id: 'suppliers', rotulo: 'Fornecedores', rota: '/fornecedores', modulo: 'FORNECEDORES', mostrar: podeComprar },
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

export const enderecoDe = (i: ItemMenu) => i.rota;

/**
 * Destinos que a Central de Avisos manda e que não são itens de menu.
 * `buy-demands` é o caso vivo: a tela dele deixou de existir há tempos e o
 * aviso ficava sem destino — quem cuida dessas demandas é a Gestão de
 * Solicitações.
 */
const FORA_DO_MENU: Record<string, string> = { 'buy-demands': '/gestao-solicitacoes' };

/**
 * Endereço a partir do id da tela — a Central de Avisos manda o id da view
 * (`pr-mine`, `triage`…), não a rota. Id que não é item de menu nem tem
 * destino conhecido cai no painel, onde o próprio aviso está.
 */
export function enderecoDoId(id: string): string {
  for (const grupo of MENU)
    for (const item of grupo.itens) {
      const candidatos = ehSubgrupo(item) ? item.filhos : [item];
      const achado = candidatos.find((i) => i.id === id);
      if (achado) return achado.rota;
    }
  return FORA_DO_MENU[id] ?? '/painel';
}
