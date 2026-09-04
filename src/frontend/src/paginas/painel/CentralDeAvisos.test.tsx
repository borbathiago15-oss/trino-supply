import { render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Aviso } from '@/api/painel';
import { ehRotaInterna, enderecoDoId } from '@/layout/menu';
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
  it('tela migrada vira rota do React', () => {
    expect(enderecoDoId('pr-mine')).toBe('/solicitacoes');
    expect(enderecoDoId('triage')).toBe('/gestao-solicitacoes');
    expect(ehRotaInterna(enderecoDoId('pr-mine'))).toBe(true);
  });
  it('id desconhecido pelo menu cai no clássico, que sabe lidar com ele', () => {
    expect(enderecoDoId('quotations')).toBe('/cotacoes');
    expect(enderecoDoId('buy-demands')).toBe('/#tela=buy-demands');
    expect(ehRotaInterna(enderecoDoId('buy-demands'))).toBe(false);
  });
});

describe('Central de Avisos', () => {
  beforeEach(() => vi.resetAllMocks());

  it('cada aviso leva à tela dele, por rota interna ou pelo clássico', async () => {
    vi.mocked(listarAvisos).mockResolvedValue([
      aviso({ kind: 'DEVOLVIDO', view: 'pr-mine' }),
      aviso({ kind: 'DEMANDA', view: 'buy-demands', severity: 'media', count: 4, text: '4 demandas aguardando pedido.' }),
    ]);
    abrir();
    const lista = await screen.findByTestId('lista-avisos');
    expect(within(lista).getByText(/devolvido\(s\) para ajuste/)).toBeInTheDocument();
    expect(lista.querySelector('[data-aviso="DEVOLVIDO"]')).toHaveAttribute('href', '/solicitacoes');
    expect(lista.querySelector('[data-aviso="DEMANDA"]')).toHaveAttribute('href', '/#tela=buy-demands');
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
