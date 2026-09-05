import { render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Aviso } from '@/api/painel';
import { enderecoDoId } from '@/layout/menu';
import { CentralDeAvisos } from './CentralDeAvisos';

vi.mock('@/api/painel', async (importar) => ({
  ...(await importar<typeof import('@/api/painel')>()),
  listarAvisos: vi.fn(),
}));

import { listarAvisos } from '@/api/painel';

const aviso = (p: Partial<Aviso>): Aviso => ({
  kind: 'DEVOLVIDO', severity: 'alta', count: 2, view: 'pr-mine',
  text: '2 pedido(s) devolvido(s) para ajuste — revise e reenvie.',
  ...p,
});

const abrir = () => render(<MemoryRouter><CentralDeAvisos /></MemoryRouter>);

describe('endereço da tela pelo id', () => {
  it('o id do menu vira a rota daquela tela', () => {
    expect(enderecoDoId('pr-mine')).toBe('/solicitacoes');
    expect(enderecoDoId('triage')).toBe('/gestao-solicitacoes');
    expect(enderecoDoId('quotations')).toBe('/cotacoes');
  });
  it('buy-demands não é item de menu e cai na Gestão de Solicitações', () => {
    // a tela dele deixou de existir; quem cuida dessas demandas é a triagem
    expect(enderecoDoId('buy-demands')).toBe('/gestao-solicitacoes');
  });
  it('id que ninguém conhece cai no painel, onde o aviso está', () => {
    expect(enderecoDoId('tela-que-nao-existe')).toBe('/painel');
  });
  it('todo destino que o backend emite tem para onde ir', () => {
    const destinos = ['buy-demands', 'buy-orders', 'contracts', 'pr-approvals', 'pr-mine',
      'quotations', 'suppliers', 'triage', 'wh-queue'];
    for (const d of destinos) expect(enderecoDoId(d)).toMatch(/^\/[a-z]/);
  });
});

describe('Central de Avisos', () => {
  beforeEach(() => vi.resetAllMocks());

  it('cada aviso leva à tela dele', async () => {
    vi.mocked(listarAvisos).mockResolvedValue([
      aviso({ kind: 'DEVOLVIDO', view: 'pr-mine' }),
      aviso({ kind: 'DEMANDA', view: 'buy-demands', severity: 'media', count: 4, text: '4 demandas aguardando pedido.' }),
    ]);
    abrir();
    const lista = await screen.findByTestId('lista-avisos');
    expect(within(lista).getByText(/devolvido\(s\) para ajuste/)).toBeInTheDocument();
    expect(lista.querySelector('[data-aviso="DEVOLVIDO"]')).toHaveAttribute('href', '/solicitacoes');
    expect(lista.querySelector('[data-aviso="DEMANDA"]')).toHaveAttribute('href', '/gestao-solicitacoes');
  });

  it('sem aviso, diz que está tudo em dia', async () => {
    vi.mocked(listarAvisos).mockResolvedValue([]);
    abrir();
    expect(await screen.findByText(/Tudo em dia por aqui/)).toBeInTheDocument();
    expect(screen.queryByTestId('lista-avisos')).not.toBeInTheDocument();
  });

  it('a severidade escolhe a cor do cartão', async () => {
    vi.mocked(listarAvisos).mockResolvedValue([
      aviso({ kind: 'ALTA', severity: 'alta' }),
      aviso({ kind: 'INFO', severity: 'info' }),
    ]);
    abrir();
    const lista = await screen.findByTestId('lista-avisos');
    expect(lista.querySelector('[data-aviso="ALTA"]')?.className).toContain('text-perigo');
    expect(lista.querySelector('[data-aviso="INFO"]')?.className).toContain('bg-superficie-suave');
  });
});
