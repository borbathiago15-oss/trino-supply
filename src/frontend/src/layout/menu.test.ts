import { describe, expect, it } from 'vitest';
import { ehSubgrupo, enderecoDe, itensVisiveis, localizar, MENU, type ItemMenu } from './menu';
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

  it('os grupos seguem a sequência do processo, e a aprovação fica no topo', () => {
    const grupos = itensVisiveis({ role: 'SystemAdministrator', modules: [] });
    expect(grupos.map((g) => g.titulo))
      .toEqual([null, 'Solicitações de Compra', 'Compras', 'Material', 'Estoque', 'Cadastros']);
    // a Central de Aprovação decide SC, material e cotação: não mora dentro de um dos três
    expect(grupos[0].itens.map((i) => (ehSubgrupo(i) ? i.rotulo : i.id))).toContain('pr-approvals');
  });

  it('quem não pode ver pedido de compra não recebe o item no menu', () => {
    // antes o item era `mostrar: sempre` e só o módulo COMPRAS segurava a porta:
    // quem tivesse o módulo sem papel de compra via o item e levava 403 da API
    const solicitante = folhas({ role: 'Requester', modules: ['SOLICITACOES', 'COMPRAS'] });
    expect(solicitante.map((i) => i.id)).not.toContain('buy-orders');
    const auditor = folhas({ role: 'Auditor', modules: ['COMPRAS'] });
    expect(auditor.map((i) => i.id)).toContain('buy-orders');
  });

  it('subgrupo com uma tela só vira item simples', () => {
    // Comprador vê "Cadastro de Produtos" mas não "Famílias": o subgrupo Produtos colapsa
    const grupos = itensVisiveis({ role: 'PurchasingOfficer', modules: ['COMPRAS', 'PRODUTOS'] });
    const cadastros = grupos.find((g) => g.titulo === 'Cadastros')!;
    expect(cadastros.itens.some((i) => ehSubgrupo(i) && i.rotulo === 'Produtos')).toBe(false);
    expect(cadastros.itens.some((i) => !ehSubgrupo(i) && i.id === 'products')).toBe(true);
  });

  it('localizar acha o grupo e o subgrupo da rota, inclusive dentro de subgrupo', () => {
    const grupos = itensVisiveis({ role: 'SystemAdministrator', modules: [] });

    const nova = localizar(grupos, '/solicitacoes/nova')!;
    expect(nova.item.id).toBe('pr-new-unit');
    expect(nova.grupo.titulo).toBe('Solicitações de Compra');
    expect(nova.subgrupo?.rotulo).toBe('Nova Solicitação');

    // rota mais longa vence: /solicitacoes/nova casa também com /solicitacoes
    expect(localizar(grupos, '/solicitacoes')!.item.id).toBe('pr-mine');
    // tela filha sem item próprio fica no item que a contém
    expect(localizar(grupos, '/pedidos/abc-123')!.item.id).toBe('buy-orders');
    expect(localizar(grupos, '/tela-que-nao-existe')).toBeNull();
  });

  it('todo item do menu aponta para uma rota do app', () => {
    const folhas = MENU.flatMap((g) => g.itens.flatMap((i) => (ehSubgrupo(i) ? i.filhos : [i])));
    expect(folhas.length).toBeGreaterThan(0);
    for (const f of folhas) expect(enderecoDe(f)).toMatch(/^\/[a-z]/);
  });
});
