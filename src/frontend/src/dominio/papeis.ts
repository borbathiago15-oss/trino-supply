/** Espelho de `Domain/User.cs` (Roles e AppModules) e das capacidades do legado. */
export type Papel =
  | 'SystemAdministrator' | 'Requester' | 'Approver' | 'PurchasingOfficer'
  | 'WarehouseOperator' | 'WarehouseSupervisor' | 'SupplyManager' | 'Director' | 'Auditor';

export type Modulo =
  | 'SOLICITACOES' | 'APROVACAO' | 'MATERIAL' | 'ESTOQUE' | 'COMPRAS' | 'PRODUTOS'
  | 'FORNECEDORES' | 'CENTROS_CUSTO' | 'USUARIOS' | 'CONTRATOS' | 'COMPLIANCE' | 'INSIGHTS';

export const ROTULO_PAPEL: Record<Papel, string> = {
  SystemAdministrator: 'Administrador', Requester: 'Solicitante', Approver: 'Aprovador',
  PurchasingOfficer: 'Comprador', WarehouseOperator: 'Almoxarife', WarehouseSupervisor: 'Supervisor de Almoxarifado',
  SupplyManager: 'Gestor de Suprimentos', Director: 'Diretor', Auditor: 'Auditor',
};

export interface Perfil { role: Papel; modules?: Modulo[] }

const entre = (u: Perfil, ...papeis: Papel[]) => papeis.includes(u.role);

export const ehAdmin = (u: Perfil) => u.role === 'SystemAdministrator';
export const temModulo = (u: Perfil, m: Modulo) => ehAdmin(u) || (u.modules ?? []).includes(m);

export const podeCriarSc = (u: Perfil) => entre(u, 'Requester', 'SupplyManager', 'SystemAdministrator');
export const podeDecidirSc = (u: Perfil) => entre(u, 'Approver', 'SupplyManager', 'SystemAdministrator');
export const podePedirMaterial = (u: Perfil) => entre(u, 'Requester', 'SupplyManager', 'SystemAdministrator');
export const podeAlmoxarifado = (u: Perfil) => temModulo(u, 'ESTOQUE')
  || entre(u, 'WarehouseOperator', 'WarehouseSupervisor', 'SupplyManager', 'SystemAdministrator');
export const podeComprar = (u: Perfil) => entre(u, 'PurchasingOfficer', 'SupplyManager', 'SystemAdministrator');
export const podeManterCatalogo = (u: Perfil) => entre(u, 'SupplyManager', 'SystemAdministrator');
export const podeTriar = podeComprar;
export const podeConduzirCotacao = podeComprar;
export const podeAprovarGerente = (u: Perfil) => entre(u, 'SupplyManager', 'SystemAdministrator', 'Approver');
export const podeAprovarDiretor = (u: Perfil) => entre(u, 'Director', 'SystemAdministrator');
export const podeVerCotacao = (u: Perfil) =>
  podeConduzirCotacao(u) || podeAprovarGerente(u) || podeAprovarDiretor(u) || u.role === 'Auditor';
export const podeVerCompliance = (u: Perfil) => entre(u, 'Auditor', 'SupplyManager', 'Director', 'SystemAdministrator');

/** Pedidos de compra — mesmo critério de `PurchaseOrderService.CanManage/CanView`. */
export const podeGerirPedidos = (u: Perfil) => entre(u, 'PurchasingOfficer', 'SupplyManager', 'SystemAdministrator');
export const podeVerPedidos = (u: Perfil) => podeGerirPedidos(u) || u.role === 'Auditor';
/** Entrega pode ser confirmada por quem gere o pedido ou por quem opera o estoque. */
export const podeConfirmarEntrega = (u: Perfil) => podeGerirPedidos(u) || podeAlmoxarifado(u);
