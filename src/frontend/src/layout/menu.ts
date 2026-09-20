import {
  ehAdmin, podeAlmoxarifado, podeAprovarDiretor, podeAprovarGerente, podeComprar, podeConduzirCotacao, podeCriarSc, podeDecidirSc,
  podeManterCatalogo,
  podePedirMaterial, podeTriar, podeVerCompliance, podeVerCotacao, podeVerPedidos, podeVerRelatorios, temModulo,
  type Modulo, type Perfil,
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
export interface GrupoMenu {
  titulo: string | null;
  modulo?: Modulo;
  /** Grupo inteiro fora do menu de um papel, mesmo com o módulo marcado. */
  mostrar?: (u: Perfil) => boolean;
  itens: (ItemMenu | SubgrupoMenu)[];
}

export const ehSubgrupo = (x: ItemMenu | SubgrupoMenu): x is SubgrupoMenu => 'filhos' in x;

const sempre = () => true;

/**
 * O diretor é a segunda alçada e mais nada: o menu dele é o Dashboard (com os relatórios),
 * a Central de Aprovação e as solicitações. Torre, cotações, material, estoque e cadastros
 * são ferramenta de quem opera a compra — para quem só aprova, era ruído que confundia,
 * mesmo com os módulos marcados no cadastro. O que ele precisa ler de um processo chega
 * pelo link da própria Central.
 */
const naoDiretor = (u: Perfil) => u.role !== 'Director';

/**
 * O menu do sistema: cada item aponta para a sua rota.
 *
 * Os grupos seguem a sequência do processo — Solicitações de Compra, Compras,
 * Material, Estoque — e não a ordem em que os módulos foram escritos. Quem
 * enxerga o sistema inteiro lê o menu de cima para baixo como lê o ciclo.
 *
 * A Central de Aprovação fica **fora** dos grupos, no topo: ela decide três
 * fluxos (SC, material e cotação) e é o passo 2 e o passo 6 do ciclo. Enquanto
 * morava dentro de "Solicitações de Compra", quem acabava de escolher o
 * fornecedor em "Compras" tinha de voltar dois grupos para aprovar.
 *
 * ## O vocabulário (D7)
 *
 * Duas palavras significavam duas coisas cada uma, e era o que mais confundia
 * quem enxerga o sistema inteiro. A regra agora é: **cada nome carrega o seu
 * objeto**, e o rótulo do menu é o mesmo que o título da tela e o texto do
 * aviso — dizer "pedido" no menu e "solicitação" na tela é o mesmo defeito,
 * só que espalhado.
 *
 * | Palavra | Significa | Onde |
 * |---|---|---|
 * | **Solicitação (SC)** | o pedido interno de compra | Minhas Solicitações (SC) |
 * | **Pedido (O.C.)** | a ordem fechada no ERP, para o fornecedor | Pedidos de Compra (O.C.) |
 * | **Solicitação de Material** | a retirada do almoxarifado | Minhas Solicitações de Material |
 * | **Demanda** | o que chega para o comprador triar | Triagem de Demandas |
 *
 * "Pedido" sozinho passa a querer dizer O.C., em todo o sistema.
 */
export const MENU: GrupoMenu[] = [
  { titulo: null, itens: [
    // As quatro telas de leitura moram juntas: são o mesmo gesto — olhar o que
    // aconteceu —, e soltas no topo empurravam a Central de Aprovação para baixo
    // de coisa que ninguém abre todo dia. Quem só enxerga o painel não paga por
    // isso: subgrupo com uma tela só vira item simples (`itensVisiveis`).
    { rotulo: 'Dashboard', filhos: [
      // o painel é do comprador e de quem lê tudo; o solicitante começa nas próprias SCs
      { id: 'supply-dash', rotulo: 'Dashboard de Suprimentos', rota: '/painel', mostrar: (u) => u.role !== 'Requester' },
      { id: 'insights', rotulo: 'Insights & Executivo', rota: '/insights', modulo: 'INSIGHTS', mostrar: podeVerCompliance },
      { id: 'compliance', rotulo: 'Compliance', rota: '/compliance', modulo: 'COMPLIANCE', mostrar: podeVerCompliance },
      { id: 'reports', rotulo: 'Relatórios', rota: '/relatorios', mostrar: podeVerRelatorios },
    ]},
    // A Central é de quem aprova alguma coisa — SC, Nível 1 ou Nível 2 — e o direito vem do
    // papel, como no servidor (que não pede módulo para decidir). O diretor chegava lá só pelo
    // atalho da Torre, porque o item pedia o módulo APROVACAO e um papel que não era o dele.
    { id: 'pr-approvals', rotulo: 'Central de Aprovação', rota: '/aprovacoes',
      mostrar: (u) => podeDecidirSc(u) || podeAprovarGerente(u) || podeAprovarDiretor(u) },
  ]},
  { titulo: 'Solicitações de Compra', modulo: 'SOLICITACOES', itens: [
    { rotulo: 'Nova Solicitação', filhos: [
      { id: 'pr-new-unit', rotulo: 'Inclusão de SC', rota: '/solicitacoes/nova', mostrar: podeCriarSc },
      { id: 'pr-new-multi', rotulo: 'Solicitação em Lote', rota: '/solicitacoes/lote', mostrar: podeCriarSc },
    ]},
    { id: 'pr-mine', rotulo: 'Minhas Solicitações (SC)', rota: '/solicitacoes', mostrar: sempre },
  ]},
  { titulo: 'Compras', modulo: 'COMPRAS', mostrar: naoDiretor, itens: [
    // Primeira do grupo, e uma tela só: é a que o comprador abre e deixa aberta.
    //
    // Já foi um subgrupo, com a Torre e a triagem lado a lado, e era um erro: as duas
    // listavam a mesma demanda de compra por caminhos diferentes, e o comprador tinha de
    // escolher em qual acreditar. A triagem de compra vive dentro da Torre — atribuir e
    // liberar responsável se faz na linha —, e o tempo de fila por faixa veio junto, que
    // era a única coisa que a outra tela mostrava e esta não.
    //
    // A triagem de **material** não veio: é outro ciclo, com outras etapas e outro
    // atendente. Ela tem tela própria, no grupo Material.
    { id: 'control-tower', rotulo: 'Torre de Controle', rota: '/torre',
      mostrar: (u) => podeComprar(u) || podeVerCompliance(u) || u.role === 'Auditor' },
    { rotulo: 'Cotações', filhos: [
      { id: 'rfq-queue', rotulo: 'Abrir Cotação', rota: '/cotacoes/abrir', mostrar: podeVerCotacao },
      { id: 'quotations', rotulo: 'Processos de Cotação', rota: '/cotacoes', mostrar: podeVerCotacao },
    ]},
    { id: 'buy-orders', rotulo: 'Pedidos de Compra (O.C.)', rota: '/pedidos', mostrar: podeVerPedidos },
    { id: 'contracts', rotulo: 'Contratos', rota: '/contratos', modulo: 'CONTRATOS', mostrar: (u) => podeComprar(u) || ehAdmin(u) },
    { id: 'scorecard', rotulo: 'Scorecard de Fornecedores', rota: '/scorecard', mostrar: (u) => podeComprar(u) || podeVerCompliance(u) },
  ]},
  { titulo: 'Material', modulo: 'MATERIAL', mostrar: naoDiretor, itens: [
    { id: 'mr-new', rotulo: 'Solicitar Material', rota: '/material/nova', mostrar: podePedirMaterial },
    { id: 'mr-mine', rotulo: 'Minhas Solicitações de Material', rota: '/material', mostrar: sempre },
    // saiu do grupo Compras: o que ela tria é pedido ao almoxarifado, não compra —
    // e era ficar ao lado da Torre que fazia as duas parecerem a mesma fila
    { id: 'triage', rotulo: 'Triagem de Material', rota: '/gestao-solicitacoes',
      mostrar: (u) => podeTriar(u) || podeAlmoxarifado(u) },
  ]},
  { titulo: 'Estoque', modulo: 'ESTOQUE', mostrar: naoDiretor, itens: [
    { id: 'wh-queue', rotulo: 'Fila de Atendimento', rota: '/estoque/fila', mostrar: podeAlmoxarifado },
    { id: 'wh-panel', rotulo: 'Painel de Atendimentos', rota: '/estoque/atendimentos', mostrar: podeAlmoxarifado },
  ]},
  { titulo: 'Cadastros', mostrar: naoDiretor, itens: [
    { rotulo: 'Produtos', filhos: [
      { id: 'products', rotulo: 'Cadastro de Produtos', rota: '/produtos', modulo: 'PRODUTOS', mostrar: sempre },
      { id: 'families', rotulo: 'Famílias de Produtos', rota: '/familias', modulo: 'PRODUTOS', mostrar: podeManterCatalogo },
    ]},
    { rotulo: 'Estrutura da Empresa', filhos: [
      { id: 'cost-centers', rotulo: 'Centros de Custo', rota: '/centros-custo', modulo: 'CENTROS_CUSTO', mostrar: podeManterCatalogo },
      { id: 'company', rotulo: 'Empresas (CNPJs)', rota: '/empresas', mostrar: ehAdmin },
    ]},
    { id: 'users', rotulo: 'Usuários', rota: '/usuarios', modulo: 'USUARIOS', mostrar: ehAdmin },
    { id: 'announcements', rotulo: 'Comunicados', rota: '/comunicados', mostrar: ehAdmin },
    { id: 'suppliers', rotulo: 'Fornecedores', rota: '/fornecedores', modulo: 'FORNECEDORES', mostrar: podeComprar },
    // a régua com que o sistema compara proposta é cadastro como outro qualquer — e fica
    // aqui, e não na tela da cotação, porque vale para toda cotação e não para uma
    { id: 'score-weights', rotulo: 'Pesos do Score', rota: '/pesos-score', modulo: 'COMPRAS', mostrar: ehAdmin },
    // o prazo de cada etapa é a régua com que a Torre marca o estouro — cadastro, como os
    // pesos do score, e pelo mesmo motivo: vale para toda a operação, não para uma tela
    // os dois andam juntos: o tipo classifica o pedido, e o prazo é escolhido pelo tipo
    { rotulo: 'Atendimento', filhos: [
      { id: 'request-types', rotulo: 'Tipos de Solicitação', rota: '/tipos-solicitacao', modulo: 'COMPRAS',
        mostrar: (u) => ehAdmin(u) || u.role === 'SupplyManager' },
      { id: 'stage-sla', rotulo: 'Prazos por Etapa', rota: '/prazos-etapas', modulo: 'COMPRAS', mostrar: ehAdmin },
    ]},
    { rotulo: 'Pagamento', filhos: [
      { id: 'payment-methods', rotulo: 'Formas de Pagamento', rota: '/formas-pagamento', modulo: 'COMPRAS', mostrar: podeComprar },
      { id: 'payment-terms', rotulo: 'Condições de Pagamento', rota: '/condicoes-pagamento', modulo: 'COMPRAS', mostrar: podeComprar },
    ]},
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
    if (grupo.mostrar && !grupo.mostrar(u)) continue;
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

/** A URL casa com o item quando é a rota dele ou uma tela abaixo dela. */
const casa = (caminho: string, rota: string) => caminho === rota || caminho.startsWith(rota + '/');

export interface Localizacao { item: ItemMenu; grupo: GrupoMenu; subgrupo: SubgrupoMenu | null }

/**
 * Onde a URL atual mora no menu — o que permite abrir o grupo certo e marcar
 * o item certo. Vence a rota mais longa: `/solicitacoes/nova` casa também com
 * `/solicitacoes`, e quem manda é a tela mais específica.
 */
export function localizar(grupos: GrupoMenu[], caminho: string): Localizacao | null {
  let achado: Localizacao | null = null;
  for (const grupo of grupos)
    for (const entrada of grupo.itens) {
      const subgrupo = ehSubgrupo(entrada) ? entrada : null;
      for (const item of subgrupo ? subgrupo.filhos : [entrada as ItemMenu])
        if (casa(caminho, item.rota) && (!achado || item.rota.length > achado.item.rota.length))
          achado = { item, grupo, subgrupo };
    }
  return achado;
}

/**
 * Views que a Central de Avisos manda e que não são itens de menu.
 * `buy-demands` é o caso vivo: a tela dele deixou de existir há tempos e o
 * aviso ficava sem destino — quem cuida dessas demandas é a Gestão de
 * Solicitações.
 */
const VIEW_PARA_ITEM: Record<string, string> = { 'buy-demands': 'triage' };

/** O item de menu que atende a view de um aviso. */
export const itemDaView = (view: string) => VIEW_PARA_ITEM[view] ?? view;

/**
 * Quanto há pendente em cada item de menu, somado a partir dos avisos.
 *
 * Só entra o que pede ação e está na fila. Aviso de severidade `info` é
 * acompanhamento — "40 pedidos seus em andamento" não é trabalho parado com
 * você. E aviso com `counts: false` é constatação, não fila: é o caso do achado
 * de insight, que aparece na Central de Avisos mas não infla o menu.
 */
export function contagemPorItem(
  avisos: { view: string; count: number; severity?: string; counts?: boolean }[],
): Record<string, number> {
  const total: Record<string, number> = {};
  for (const a of avisos) {
    if (a.severity === 'info' || a.counts === false) continue;
    const id = itemDaView(a.view);
    total[id] = (total[id] ?? 0) + a.count;
  }
  return total;
}

/**
 * Endereço a partir do id da tela — a Central de Avisos manda o id da view
 * (`pr-mine`, `triage`…), não a rota. Id que não é item de menu nem tem
 * destino conhecido cai no painel, onde o próprio aviso está.
 */
export function enderecoDoId(id: string): string {
  const alvo = itemDaView(id);
  for (const grupo of MENU)
    for (const item of grupo.itens) {
      const candidatos = ehSubgrupo(item) ? item.filhos : [item];
      const achado = candidatos.find((i) => i.id === alvo);
      if (achado) return achado.rota;
    }
  return '/painel';
}
