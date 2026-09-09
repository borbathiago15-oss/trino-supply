import { describe, expect, it } from 'vitest';
import { contagemPorItem, ehSubgrupo, enderecoDe, itensVisiveis, localizar, MENU, type ItemMenu, type SubgrupoMenu } from './menu';
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

  it('as telas de leitura moram juntas no subgrupo Dashboard, nesta ordem', () => {
    const grupos = itensVisiveis({ role: 'SystemAdministrator', modules: [] });
    const topo = grupos[0].itens;
    const dashboard = topo.find((i) => ehSubgrupo(i) && i.rotulo === 'Dashboard');
    expect(dashboard).toBeDefined();
    expect((dashboard as SubgrupoMenu).filhos.map((f) => f.id))
      .toEqual(['supply-dash', 'insights', 'compliance', 'reports']);
    // a Central de Aprovação continua fora: ela é passo do ciclo, não leitura
    expect(topo.some((i) => !ehSubgrupo(i) && i.id === 'pr-approvals')).toBe(true);
  });

  it('quem só enxerga o painel não paga clique: o subgrupo de uma tela vira item simples', () => {
    // sem Insights, Compliance nem Relatórios, "Dashboard" viraria uma gaveta com
    // uma coisa dentro — dois cliques para a tela que a pessoa mais abre
    const topo = itensVisiveis({ role: 'Requester', modules: ['SOLICITACOES'] })[0].itens;
    expect(topo.map((i) => (ehSubgrupo(i) ? i.rotulo : i.id))).toEqual(['supply-dash']);
  });

  it('Relatórios segue o mesmo critério da rota: papel de análise e módulo Compras ou Insights', () => {
    // o menu não pode abrir uma tela que a API vai recusar com 403, nem esconder
    // uma que ela aceitaria — os dois lados leem o mesmo par (papel, módulo)
    expect(folhas({ role: 'Director', modules: ['INSIGHTS'] }).map((i) => i.id)).toContain('reports');
    expect(folhas({ role: 'Auditor', modules: ['COMPRAS'] }).map((i) => i.id)).toContain('reports');
    // papel certo, módulo nenhum
    expect(folhas({ role: 'Director', modules: ['SOLICITACOES'] }).map((i) => i.id)).not.toContain('reports');
    // módulo certo, papel que não analisa
    expect(folhas({ role: 'Requester', modules: ['COMPRAS'] }).map((i) => i.id)).not.toContain('reports');
  });

  it('a Torre de Controle abre o grupo Compras, e é uma tela só', () => {
    const compras = itensVisiveis({ role: 'PurchasingOfficer', modules: ['COMPRAS'] })
      .find((g) => g.titulo === 'Compras')!;
    const primeiro = compras.itens[0];
    // já foi subgrupo, com a Torre e a triagem lado a lado, e era um erro: as duas
    // listavam a mesma demanda de compra e o comprador tinha de escolher em qual acreditar
    expect(ehSubgrupo(primeiro)).toBe(false);
    expect(ehSubgrupo(primeiro) ? '' : primeiro.id).toBe('control-tower');
    // o auditor enxerga a fila para auditar; quem só solicita, não
    expect(folhas({ role: 'Auditor', modules: ['COMPRAS'] }).map((i) => i.id)).toContain('control-tower');
    expect(folhas({ role: 'Requester', modules: ['COMPRAS'] }).map((i) => i.id)).not.toContain('control-tower');
  });

  it('a triagem de material saiu de Compras e agora mora no grupo Material', () => {
    // ela tria pedido ao almoxarifado, que é outro ciclo — ficar ao lado da Torre
    // era o que fazia as duas parecerem a mesma fila
    const grupos = itensVisiveis({ role: 'SupplyManager', modules: ['COMPRAS', 'MATERIAL'] });
    const noGrupo = (titulo: string) => grupos.find((g) => g.titulo === titulo)!.itens
      .flatMap((i) => (ehSubgrupo(i) ? i.filhos : [i])).map((i) => i.id);
    expect(noGrupo('Material')).toContain('triage');
    expect(noGrupo('Compras')).not.toContain('triage');
  });

  it('Comunicados é do administrador: quem escreve o recado não é qualquer um', () => {
    expect(folhas({ role: 'SystemAdministrator', modules: [] }).map((i) => i.id)).toContain('announcements');
    for (const papel of ['SupplyManager', 'Director', 'PurchasingOfficer', 'Requester'] as const)
      expect(folhas({ role: papel, modules: [] }).map((i) => i.id)).not.toContain('announcements');
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

  it('contagemPorItem soma as views que a mesma tela atende', () => {
    const total = contagemPorItem([
      { view: 'triage', count: 3, severity: 'media' },
      { view: 'buy-demands', count: 4, severity: 'media' },
      { view: 'pr-approvals', count: 2, severity: 'alta' },
    ]);
    // buy-demands não é item de menu: quem cuida dessas demandas é a Gestão de Solicitações
    expect(total).toEqual({ triage: 7, 'pr-approvals': 2 });
  });

  it('achado de insight aparece no aviso, mas não infla o contador do menu', () => {
    // é constatação, não fila: contar faria o menu dizer "Pedidos 3" com a lista vazia
    const total = contagemPorItem([
      { view: 'buy-orders', count: 3, severity: 'alta', counts: false },
      { view: 'buy-orders', count: 2, severity: 'alta' },
    ]);
    expect(total).toEqual({ 'buy-orders': 2 });
  });

  it('aviso de acompanhamento não vira contador: número no menu quer dizer ação', () => {
    const total = contagemPorItem([
      { view: 'pr-mine', count: 40, severity: 'info' },
      { view: 'pr-mine', count: 2, severity: 'alta' },
    ]);
    expect(total).toEqual({ 'pr-mine': 2 });
  });

  it('todo item do menu aponta para uma rota do app', () => {
    const folhas = MENU.flatMap((g) => g.itens.flatMap((i) => (ehSubgrupo(i) ? i.filhos : [i])));
    expect(folhas.length).toBeGreaterThan(0);
    for (const f of folhas) expect(enderecoDe(f)).toMatch(/^\/[a-z]/);
  });

  it('cada nome do menu carrega o seu objeto: “pedido” quer dizer O.C. (D7)', () => {
    // duas palavras significavam duas coisas cada uma — era o que mais confundia
    // quem enxerga o sistema inteiro. Este teste é o que impede a volta.
    const folhas = MENU.flatMap((g) => g.itens.flatMap((i) => (ehSubgrupo(i) ? i.filhos : [i])));
    const rotulo = (id: string) => folhas.find((f) => f.id === id)!.rotulo;

    expect(rotulo('pr-mine')).toBe('Minhas Solicitações (SC)');
    expect(rotulo('buy-orders')).toBe('Pedidos de Compra (O.C.)');
    expect(rotulo('mr-mine')).toBe('Minhas Solicitações de Material');
    expect(rotulo('triage')).toBe('Triagem de Material');

    // nenhuma outra tela chama de "pedido" o que não é O.C.
    expect(folhas.filter((f) => /pedido/i.test(f.rotulo)).map((f) => f.id)).toEqual(['buy-orders']);
  });
});
