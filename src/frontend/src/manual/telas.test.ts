import { describe, expect, it } from 'vitest';
import rotasDoApp from '@/App.tsx?raw';
import { MANUAIS } from './conteudo';
import { DETALHES, folhasDoMenu, telaDaRota } from './telas';

/**
 * "Cada menu ou submenu tem o manual daquele menu." Estes testes são o que mantém isso
 * verdade depois de hoje: tela nova no menu, ou rota nova no App, sem manual não passa.
 */
describe('o manual cobre o sistema inteiro', () => {
  it('toda tela do menu tem manual — inclusive as que o papel esconde', () => {
    const sem = folhasDoMenu().filter((i) => !MANUAIS[i.id]).map((i) => `${i.id} (${i.rotulo})`);
    expect(sem).toEqual([]);
  });

  it('toda tela de detalhe tem manual', () => {
    expect(DETALHES.filter((d) => !MANUAIS[d.chave]).map((d) => d.chave)).toEqual([]);
  });

  it('toda rota dentro do app resolve para uma tela com manual', () => {
    // as de fora da casca não têm cabeçalho, e por isso não têm o botão
    const fora = new Set(['/login', '/portal', '/trocar-senha', '/cockpit', '*']);
    const rotas = [...rotasDoApp.matchAll(/<Route path="([^"]+)"/g)].map((m) => m[1]).filter((r) => !fora.has(r));
    expect(rotas.length).toBeGreaterThan(30);
    const sem = rotas.filter((r) => !telaDaRota(r.replace(/:[a-z]+/g, 'abc-123')).manual);
    expect(sem).toEqual([]);
  });

  it('não sobra manual de tela que não existe mais', () => {
    const chaves = new Set([...folhasDoMenu().map((i) => i.id), ...DETALHES.map((d) => d.chave)]);
    expect(Object.keys(MANUAIS).filter((k) => !chaves.has(k))).toEqual([]);
  });

  it('todo manual diz para que serve e tem um caminho de pelo menos dois passos', () => {
    for (const [chave, m] of Object.entries(MANUAIS)) {
      expect(m.paraQueServe.length, chave).toBeGreaterThan(20);
      expect(m.passos.length, chave).toBeGreaterThanOrEqual(2);
    }
  });
});

describe('de qual tela é esta rota', () => {
  it('a rota exata do menu vence o detalhe: /cotacoes/abrir é Abrir Cotação, não um processo', () => {
    expect(telaDaRota('/cotacoes/abrir').chave).toBe('rfq-queue');
    expect(telaDaRota('/cotacoes/3f2a').chave).toBe('quotation-detail');
  });

  it('o detalhe tem manual próprio, e não o da lista', () => {
    expect(telaDaRota('/pedidos/abc').chave).toBe('order-detail');
    expect(telaDaRota('/pedidos').chave).toBe('buy-orders');
  });

  it('usa o nome do menu, que é o que o atendente lê no chamado', () => {
    expect(telaDaRota('/torre')).toMatchObject({ chave: 'control-tower', rotulo: 'Torre de Controle' });
  });

  it('barra no fim não muda a tela', () => {
    expect(telaDaRota('/torre/').chave).toBe('control-tower');
  });

  it('rota desconhecida não inventa manual', () => {
    expect(telaDaRota('/nao-existe')).toEqual({ chave: 'geral', rotulo: 'Trino Supply', manual: null });
  });
});
