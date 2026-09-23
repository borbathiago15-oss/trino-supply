/** Espelho de `Domain/User.cs` (Roles e AppModules) e das capacidades do legado. */
export type Papel =
  | 'SystemAdministrator' | 'Requester' | 'Approver' | 'PurchasingOfficer'
  | 'WarehouseOperator' | 'WarehouseSupervisor' | 'SupplyManager' | 'Director' | 'Auditor';

export type Modulo =
  | 'SOLICITACOES' | 'APROVACAO' | 'MATERIAL' | 'ESTOQUE' | 'COMPRAS' | 'PRODUTOS'
  | 'FORNECEDORES' | 'CENTROS_CUSTO' | 'USUARIOS' | 'CONTRATOS' | 'COMPLIANCE' | 'INSIGHTS'
  | 'PLANO_ACAO'
  | 'PDCA'
  | 'SUPORTE';

export const ROTULO_MODULO: Record<Modulo, string> = {
  SOLICITACOES: 'Solicitações de Compra', APROVACAO: 'Central de Aprovação',
  MATERIAL: 'Solicitação de Material', ESTOQUE: 'Estoque / Almoxarifado',
  COMPRAS: 'Compras', PRODUTOS: 'Cadastro de Produtos',
  FORNECEDORES: 'Cadastro de Fornecedores', CENTROS_CUSTO: 'Centros de Custo',
  USUARIOS: 'Cadastro de Usuários', CONTRATOS: 'Contratos de Parceria',
  COMPLIANCE: 'Compliance', INSIGHTS: 'Insights & Executivo',
  // o plano de ação não entra em padrão de papel nenhum, de propósito: é ferramenta de
  // trabalho de qualquer área, e quem decide quem a usa é o administrador, no cadastro
  PLANO_ACAO: 'Plano de Ação',
  PDCA: 'Ciclos de Melhoria (PDCA)',
  // atender chamado de suporte. Abrir chamado não pede módulo nenhum: pedir ajuda não pode
  // depender de permissão. Como o plano de ação, fica fora de todo padrão de papel
  SUPORTE: 'Atender chamados de suporte',
};

/** Autorizações sugeridas ao escolher o papel de um usuário novo. */
export const MODULOS_PADRAO: Record<Papel, Modulo[]> = {
  SystemAdministrator: Object.keys(ROTULO_MODULO) as Modulo[],
  Requester: ['SOLICITACOES', 'MATERIAL'],
  Approver: ['SOLICITACOES', 'APROVACAO'],
  PurchasingOfficer: ['COMPRAS', 'FORNECEDORES', 'ESTOQUE'],
  WarehouseOperator: ['ESTOQUE'],
  WarehouseSupervisor: ['ESTOQUE', 'PRODUTOS'],
  SupplyManager: ['SOLICITACOES', 'APROVACAO', 'MATERIAL', 'ESTOQUE', 'COMPRAS', 'PRODUTOS', 'FORNECEDORES', 'CENTROS_CUSTO'],
  Director: ['SOLICITACOES', 'APROVACAO', 'MATERIAL', 'COMPRAS'],
  Auditor: ['SOLICITACOES', 'ESTOQUE', 'COMPRAS'],
};

/**
 * Papéis de almoxarifado saíram do cadastro: quem opera o estoque recebe o
 * módulo "Estoque / Almoxarifado". Gestor de Suprimentos idem.
 */
export const PAPEIS_OCULTOS: Papel[] = ['WarehouseOperator', 'WarehouseSupervisor', 'SupplyManager'];

export const ROTULO_PAPEL: Record<Papel, string> = {
  SystemAdministrator: 'Administrador', Requester: 'Solicitante', Approver: 'Aprovador',
  PurchasingOfficer: 'Comprador', WarehouseOperator: 'Almoxarife', WarehouseSupervisor: 'Supervisor de Almoxarifado',
  SupplyManager: 'Gestor de Suprimentos', Director: 'Diretor', Auditor: 'Auditor',
};

export interface Perfil { role: Papel; modules?: Modulo[] }

const entre = (u: Perfil, ...papeis: Papel[]) => papeis.includes(u.role);

export const ehAdmin = (u: Perfil) => u.role === 'SystemAdministrator';
export const temModulo = (u: Perfil, m: Modulo) => ehAdmin(u) || (u.modules ?? []).includes(m);

/**
 * O comprador também solicita, em qualquer centro — decisão da empresa (2026-09). O diretor
 * também: o Nível 1 da SC dele é do comprador ou da lista do centro, e o Nível 2 ele mesmo dá.
 */
export const podeCriarSc = (u: Perfil) => entre(u, 'Requester', 'SupplyManager', 'SystemAdministrator', 'PurchasingOfficer', 'Director');
export const podeDecidirSc = (u: Perfil) => entre(u, 'Approver', 'SupplyManager', 'SystemAdministrator');
/** O diretor também pede material ao almoxarifado (2026-09), como na SC. */
export const podePedirMaterial = (u: Perfil) => entre(u, 'Requester', 'SupplyManager', 'SystemAdministrator', 'Director');
export const podeAlmoxarifado = (u: Perfil) => temModulo(u, 'ESTOQUE')
  || entre(u, 'WarehouseOperator', 'WarehouseSupervisor', 'SupplyManager', 'SystemAdministrator');
export const podeComprar = (u: Perfil) => entre(u, 'PurchasingOfficer', 'SupplyManager', 'SystemAdministrator');
export const podeManterCatalogo = (u: Perfil) => entre(u, 'SupplyManager', 'SystemAdministrator');
export const podeTriar = podeComprar;
export const podeConduzirCotacao = podeComprar;
/** O comprador dá o Nível 1, inclusive do processo que conduziu; a segregação fica no Nível 2. */
export const podeAprovarGerente = (u: Perfil) => entre(u, 'SupplyManager', 'SystemAdministrator', 'Approver', 'PurchasingOfficer');
export const podeAprovarDiretor = (u: Perfil) => entre(u, 'Director', 'SystemAdministrator');
export const podeVerCotacao = (u: Perfil) =>
  podeConduzirCotacao(u) || podeAprovarGerente(u) || podeAprovarDiretor(u) || u.role === 'Auditor';
export const podeVerCompliance = (u: Perfil) => entre(u, 'Auditor', 'SupplyManager', 'Director', 'SystemAdministrator');

/**
 * Relatórios de compras — mesmo critério de `RelatorioExecutivoService.CanView`
 * (papel) somado ao dos módulos da rota: Compras **ou** Insights. O menu não pode
 * abrir uma tela que o servidor vai recusar, nem esconder uma que ele aceitaria.
 */
export const podeVerRelatorios = (u: Perfil) =>
  entre(u, 'Approver', 'PurchasingOfficer', 'SupplyManager', 'Director', 'Auditor', 'SystemAdministrator')
  && (temModulo(u, 'COMPRAS') || temModulo(u, 'INSIGHTS'));

/** Pedidos de compra — mesmo critério de `PurchaseOrderService.CanManage/CanView`. */
export const podeGerirPedidos = (u: Perfil) => entre(u, 'PurchasingOfficer', 'SupplyManager', 'SystemAdministrator');
export const podeVerPedidos = (u: Perfil) => podeGerirPedidos(u) || u.role === 'Auditor';
/** Entrega pode ser confirmada por quem gere o pedido ou por quem opera o estoque. */
export const podeConfirmarEntrega = (u: Perfil) => podeGerirPedidos(u) || podeAlmoxarifado(u);

/**
 * Onde cada papel começa o dia. Quem aprova abre a Central; quem pede abre as próprias
 * solicitações; quem compra, e quem lê tudo, abre o painel. Mandar o diretor para o
 * dashboard do comprador era pedir que ele achasse a fila dele num menu.
 */
export function paginaInicial(u: Perfil): string {
  switch (u.role) {
    case 'Director':
      return '/diretoria';
    case 'Approver':
      return '/aprovacoes';
    case 'Requester':
      return '/solicitacoes';
    default:
      return '/painel';
  }
}
