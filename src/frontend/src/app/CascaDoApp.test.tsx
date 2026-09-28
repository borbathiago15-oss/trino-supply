import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import { abasDoApp } from './abas';
import { CascaDoApp, InicioDoApp } from './CascaDoApp';

let eu: Usuario = { id: 'u1', email: 'd@t.com', name: 'Diana', role: 'Director', modules: [] };
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu, useSessao: () => ({ sair: vi.fn() }) }));
vi.mock('@/sessao/AvisosProvider', () => ({ AvisosProvider: ({ children }: { children: React.ReactNode }) => children }));
vi.mock('@/layout/BarraDeAjuda', () => ({ BarraDeAjuda: () => null }));

const perfil = (role: Usuario['role'], modules: Usuario['modules'] = ['COMPRAS', 'SOLICITACOES', 'APROVACAO']) =>
  ({ role, modules });

describe('as abas do app seguem o menu do sistema', () => {
  it('o diretor aprova e lê a visão da diretoria; o painel do comprador ele não tem nem no sistema', () => {
    expect(abasDoApp(perfil('Director', [])).map((a) => a.rotulo)).toEqual(['Aprovar', 'Diretoria']);
  });
  it('o administrador vê as três', () => {
    expect(abasDoApp(perfil('SystemAdministrator')).map((a) => a.rotulo)).toEqual(['Aprovar', 'Diretoria', 'Dashboard']);
  });
  it('quem só solicita não tem aba nenhuma', () => {
    expect(abasDoApp(perfil('Requester', ['SOLICITACOES']))).toEqual([]);
  });
});

const abrir = (rota: string) => render(
  <MemoryRouter initialEntries={[rota]}>
    <Routes>
      <Route path="/app" element={<CascaDoApp />}>
        <Route index element={<InicioDoApp />} />
        <Route path="/app/aprovacoes" element={<p>central</p>} />
        <Route path="/app/diretoria" element={<p>diretoria</p>} />
      </Route>
    </Routes>
  </MemoryRouter>);

describe('<CascaDoApp />', () => {
  it('abre na primeira aba, com as abas embaixo e a saída para o sistema completo', async () => {
    eu = { id: 'u1', email: 'd@t.com', name: 'Diana', role: 'Director', modules: [] };
    abrir('/app');
    expect(await screen.findByText('central')).toBeInTheDocument();
    const abas = screen.getByTestId('abas-do-app');
    expect(abas).toHaveTextContent('Aprovar');
    expect(abas).toHaveTextContent('Diretoria');
    expect(abas).not.toHaveTextContent('Dashboard');
    expect(screen.getByRole('link', { name: 'Sistema' })).toHaveAttribute('href', '/');
  });

  it('perfil sem nenhuma das telas recebe o caminho para o sistema, e não uma tela vazia', () => {
    eu = { id: 'u5', email: 'a@t.com', name: 'Ana', role: 'Requester', modules: ['SOLICITACOES'] };
    abrir('/app');
    expect(screen.getByTestId('app-sem-abas')).toHaveTextContent('Abrir o sistema completo');
    expect(screen.queryByTestId('abas-do-app')).not.toBeInTheDocument();
  });
});
