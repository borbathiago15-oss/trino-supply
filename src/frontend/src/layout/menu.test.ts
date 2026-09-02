import { describe, expect, it } from 'vitest';
import { ehSubgrupo, enderecoDe, itensVisiveis, type ItemMenu } from './menu';
import type { Perfil } from '@/dominio/papeis';

const folhas = (u: Perfil): ItemMenu[] =>
  itensVisiveis(u).flatMap((g) => g.itens.flatMap((i) => (ehSubgrupo(i) ? i.filhos : [i])));

describe('menu', () => {
  it('administrador vê tudo, com Pedidos de Compra apontando para o React', () => {
    const itens = folhas({ role: 'SystemAdministrator', modules: [] });
    expect(itens.map((i) => i.id)).toContain('users');
    const pedidos = itens.find((i) => i.id === 'buy-orders')!;
    expect(enderecoDe(pedidos)).toBe('/pedidos');
  });

  it('solicitante sem módulo de compras não vê o grupo Compras', () => {
    const grupos = itensVisiveis({ role: 'Requester', modules: ['SOLICITACOES', 'MATERIAL'] });
    expect(grupos.map((g) => g.titulo)).toEqual([null, 'Solicitações de Compra', 'Material']);
    expect(grupos[0].itens.map((i) => (ehSubgrupo(i) ? i.rotulo : i.id))).toEqual(['supply-dash']);
  });

  it('subgrupo com uma tela só vira item simples', () => {
    // Comprador vê "Cadastro de Produtos" mas não "Famílias": o subgrupo Produtos colapsa
    const grupos = itensVisiveis({ role: 'PurchasingOfficer', modules: ['COMPRAS', 'PRODUTOS'] });
    const cadastros = grupos.find((g) => g.titulo === 'Cadastros')!;
    expect(cadastros.itens.some((i) => ehSubgrupo(i) && i.rotulo === 'Produtos')).toBe(false);
    expect(cadastros.itens.some((i) => !ehSubgrupo(i) && i.id === 'products')).toBe(true);
  });

  it('telas do legado viram deep link em /#tela=', () => {
    expect(enderecoDe({ id: 'triage', rotulo: 'x', legado: 'triage' })).toBe('/#tela=triage');
  });
});
