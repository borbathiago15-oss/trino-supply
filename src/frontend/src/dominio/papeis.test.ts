import { describe, expect, it } from 'vitest';
import {
  paginaInicial, podeAprovarDiretor, podeAprovarGerente, podeConfirmarEntrega, podeCriarSc, podeGerirPedidos, podePedirMaterial, podeVerPedidos, temModulo,
} from './papeis';

describe('papéis', () => {
  it('cada papel começa o dia onde o trabalho dele está', () => {
    // quem aprova abre a fila de decisão; quem pede abre as próprias SCs; o resto, o painel
    expect(paginaInicial({ role: 'Director' })).toBe('/diretoria');
    expect(paginaInicial({ role: 'Approver' })).toBe('/aprovacoes');
    expect(paginaInicial({ role: 'Requester' })).toBe('/solicitacoes');
    expect(paginaInicial({ role: 'PurchasingOfficer' })).toBe('/painel');
    expect(paginaInicial({ role: 'SystemAdministrator' })).toBe('/painel');
  });

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

  it('o comprador solicita e dá o Nível 1; o Nível 2 continua da diretoria', () => {
    // decisão da empresa (2026-09): quem cota abre a SC em qualquer centro e fecha a primeira alçada
    expect(podeCriarSc({ role: 'PurchasingOfficer' })).toBe(true);
    expect(podeCriarSc({ role: 'Director' })).toBe(true);   // o diretor também solicita (2026-09)
    expect(podePedirMaterial({ role: 'Director' })).toBe(true);
    expect(podePedirMaterial({ role: 'Approver' })).toBe(false);
    expect(podeCriarSc({ role: 'Auditor' })).toBe(false);
    expect(podeAprovarGerente({ role: 'PurchasingOfficer' })).toBe(true);
    expect(podeAprovarDiretor({ role: 'PurchasingOfficer' })).toBe(false);
  });
});
