import { describe, expect, it } from 'vitest';
import { podeConfirmarEntrega, podeGerirPedidos, podeVerPedidos, temModulo } from './papeis';

describe('papéis', () => {
  it('gestão de pedidos segue PurchaseOrderService.CanManage/CanView', () => {
    expect(podeGerirPedidos({ role: 'PurchasingOfficer' })).toBe(true);
    expect(podeGerirPedidos({ role: 'Auditor' })).toBe(false);
    expect(podeVerPedidos({ role: 'Auditor' })).toBe(true);
    expect(podeVerPedidos({ role: 'Requester' })).toBe(false);
  });
  it('quem tem o módulo ESTOQUE confirma entrega mesmo sem ser comprador', () => {
    expect(podeConfirmarEntrega({ role: 'Requester', modules: ['ESTOQUE'] })).toBe(true);
    expect(podeConfirmarEntrega({ role: 'Requester', modules: [] })).toBe(false);
  });
  it('administrador tem todos os módulos', () => {
    expect(temModulo({ role: 'SystemAdministrator', modules: [] }, 'COMPRAS')).toBe(true);
    expect(temModulo({ role: 'Approver', modules: ['APROVACAO'] }, 'COMPRAS')).toBe(false);
  });
});
