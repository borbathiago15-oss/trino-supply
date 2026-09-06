import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import { Sidebar } from './Sidebar';

let usuarioAtual: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => usuarioAtual }));

const admin: Usuario = {
  id: 'u1', email: 'admin@trinosupply.com.br', name: 'Admin', role: 'SystemAdministrator', modules: [],
};

const montar = (rota: string) => render(
  <MemoryRouter initialEntries={[rota]}>
    <Sidebar />
    <Routes>
      {/* o painel é quem manda o usuário para outra tela: é o caminho da Central de Avisos */}
      <Route path="/painel" element={<Link to="/cotacoes">ver processos de cotação</Link>} />
      <Route path="*" element={null} />
    </Routes>
  </MemoryRouter>,
);

const menu = () => within(screen.getByRole('navigation'));

describe('<Sidebar />', () => {
  beforeEach(() => { usuarioAtual = admin; });

  it('abre sozinho o grupo da tela em que se está', () => {
    montar('/pedidos');
    expect(menu().getByRole('link', { name: 'Pedidos de Compra' })).toHaveAttribute('aria-current', 'page');
  });

  it('abre também o subgrupo, que antes nunca era encontrado', () => {
    // `grupoAtual` ignorava subgrupos: quem entrava direto em /cotacoes/abrir
    // chegava com "Compras" fechado, sem nada dizendo onde estava
    montar('/cotacoes/abrir');
    expect(menu().getByRole('link', { name: 'Abrir Cotação' })).toHaveAttribute('aria-current', 'page');
    expect(menu().getByRole('button', { name: /^Cotações/ })).toBeInTheDocument();
  });

  it('a rota mais longa vence: /solicitacoes/nova não marca Meus Pedidos', () => {
    montar('/solicitacoes/nova');
    expect(menu().getByRole('link', { name: 'Inclusão de SC' })).toHaveAttribute('aria-current', 'page');
    expect(menu().getByRole('link', { name: 'Meus Pedidos' })).not.toHaveAttribute('aria-current');
  });

  it('navegar por link fora do menu reabre o grupo do destino', async () => {
    montar('/painel');
    expect(menu().queryByRole('link', { name: 'Processos de Cotação' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('link', { name: 'ver processos de cotação' }));

    await waitFor(() => expect(menu().getByRole('link', { name: 'Processos de Cotação' }))
      .toHaveAttribute('aria-current', 'page'));
  });

  it('quem abre e fecha um grupo com a mão continua mandando', async () => {
    montar('/pedidos');
    await userEvent.click(menu().getByRole('button', { name: /^Compras/ }));
    expect(menu().queryByRole('link', { name: 'Pedidos de Compra' })).not.toBeInTheDocument();

    await userEvent.click(menu().getByRole('button', { name: /^Cadastros/ }));
    expect(menu().getByRole('link', { name: 'Usuários' })).toBeInTheDocument();
  });

  it('a Central de Aprovação fica no topo, fora dos grupos', () => {
    montar('/painel');
    // sem abrir grupo nenhum, ela já está à vista: é o passo 2 e o passo 6 do ciclo
    expect(menu().getByRole('link', { name: 'Central de Aprovação' })).toBeInTheDocument();
  });
});
