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

describe('as abas do app são as três telas, para quem o servidor deixa ler', () => {
  it('o diretor tem as três: no sistema o menu troca o painel pela visão da diretoria, mas a permissão é a mesma', () => {
    expect(abasDoApp(perfil('Director')).map((a) => a.rotulo)).toEqual(['Aprovar', 'Diretoria', 'Dashboard']);
  });
  it('o comprador e o gestor têm as três', () => {
    expect(abasDoApp(perfil('PurchasingOfficer')).map((a) => a.rotulo)).toEqual(['Aprovar', 'Diretoria', 'Dashboard']);
    expect(abasDoApp(perfil('SupplyManager')).map((a) => a.rotulo)).toEqual(['Aprovar', 'Diretoria', 'Dashboard']);
  });
  it('o administrador vê as três', () => {
    expect(abasDoApp(perfil('SystemAdministrator')).map((a) => a.rotulo)).toEqual(['Aprovar', 'Diretoria', 'Dashboard']);
  });
  it('o auditor lê e não aprova: só as duas de leitura', () => {
    expect(abasDoApp(perfil('Auditor')).map((a) => a.rotulo)).toEqual(['Diretoria', 'Dashboard']);
  });
  it('sem os módulos das rotas, a aba não abre — o servidor a recusaria', () => {
    expect(abasDoApp(perfil('Director', [])).map((a) => a.rotulo)).toEqual(['Aprovar']);
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
    eu = { id: 'u1', email: 'd@t.com', name: 'Diana', role: 'Director', modules: ['COMPRAS', 'APROVACAO'] };
    abrir('/app');
    expect(await screen.findByText('central')).toBeInTheDocument();
    const abas = screen.getByTestId('abas-do-app');
    expect(abas).toHaveTextContent('Aprovar');
    expect(abas).toHaveTextContent('Diretoria');
    expect(abas).toHaveTextContent('Dashboard');
    // sem saída para o sistema completo: o app é só as três telas
    expect(screen.queryByRole('link', { name: 'Sistema' })).not.toBeInTheDocument();
  });

  it('perfil sem nenhuma das telas recebe o caminho para o sistema, e não uma tela vazia', () => {
    eu = { id: 'u5', email: 'a@t.com', name: 'Ana', role: 'Requester', modules: ['SOLICITACOES'] };
    abrir('/app');
    expect(screen.getByTestId('app-sem-abas')).toHaveTextContent('Abrir o sistema completo');
    expect(screen.queryByTestId('abas-do-app')).not.toBeInTheDocument();
  });
});
