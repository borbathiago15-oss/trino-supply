import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import { SessaoProvider } from '@/sessao/SessaoProvider';
import { Login } from './Login';

vi.mock('@/api/auth', async (importar) => ({
  ...(await importar<typeof import('@/api/auth')>()),
  entrar: vi.fn(), quemSou: vi.fn(), sair: vi.fn(),
}));

import { entrar } from '@/api/auth';

const admin: Usuario = {
  id: 'u1', email: 'admin@trinosupply.com.br', name: 'Admin', role: 'SystemAdministrator', modules: [],
};

const abrir = () =>
  render(
    <MemoryRouter initialEntries={['/login']}>
      <SessaoProvider>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/pedidos" element={<p>Meus pedidos</p>} />
        </Routes>
      </SessaoProvider>
    </MemoryRouter>,
  );

describe('tela de login', () => {
  beforeEach(() => { sessionStorage.clear(); vi.clearAllMocks(); });

  it('mostra a identidade corporativa ao lado do formulário', () => {
    abrir();
    expect(screen.getByAltText(/Trino Supply/)).toBeInTheDocument();
    expect(screen.getByText('Enterprise Supply Management')).toBeInTheDocument();
    expect(screen.getByText(/plataforma integrada para gestão de suprimentos/)).toBeInTheDocument();
    // a assinatura aparece duas vezes: no painel institucional e na linha compacta do celular
    expect(screen.getAllByText('GRUPO TRINO')).toHaveLength(2);
    expect(screen.getByText(/plataforma corporativa de suprimentos do Grupo Trino/)).toBeInTheDocument();
    expect(screen.getByText(/acesso restrito e auditado/)).toBeInTheDocument();
    expect(screen.getByText(/Solicite a redefinição ao administrador/)).toBeInTheDocument();
  });

  it('lista o ciclo de suprimentos do pedido ao resultado', () => {
    abrir();
    const ciclo = screen.getByRole('list', { name: /Ciclo de suprimentos/ });
    expect(within(ciclo).getAllByRole('listitem').map((i) => i.textContent)).toEqual([
      'Necessidade', 'Solicitação', 'Compra', 'Recebimento', 'Estoque', 'Resultado',
    ]);
  });

  it('entra com e-mail e senha e segue para os pedidos', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrar).mockResolvedValue(admin);
    abrir();

    await usuario.type(screen.getByLabelText('E-mail'), 'admin@trinosupply.com.br');
    await usuario.type(document.querySelector('#password')!, 'TrinoSupply@2026!');
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }));

    expect(entrar).toHaveBeenCalledWith('admin@trinosupply.com.br', 'TrinoSupply@2026!');
    await waitFor(() => expect(screen.getByText('Meus pedidos')).toBeInTheDocument());
  });

  it('o botão Mostrar revela a senha digitada', async () => {
    const usuario = userEvent.setup();
    abrir();
    const senha = document.querySelector('#password') as HTMLInputElement;
    expect(senha.type).toBe('password');

    await usuario.click(screen.getByRole('button', { name: 'Mostrar senha' }));
    expect(senha.type).toBe('text');

    await usuario.click(screen.getByRole('button', { name: 'Ocultar senha' }));
    expect(senha.type).toBe('password');
  });

  it('mostra a mensagem do servidor quando a credencial não vale', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrar).mockRejectedValue(new Error('E-mail ou senha inválidos.'));
    abrir();

    await usuario.type(screen.getByLabelText('E-mail'), 'quem@trinosupply.com.br');
    await usuario.type(document.querySelector('#password')!, 'errada');
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('E-mail ou senha inválidos.');
  });
});
