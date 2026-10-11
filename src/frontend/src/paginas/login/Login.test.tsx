import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import { SessaoProvider } from '@/sessao/SessaoProvider';
import { destinoDe, Login } from './Login';

vi.mock('@/api/auth', async (importar) => ({
  ...(await importar<typeof import('@/api/auth')>()),
  entrar: vi.fn(), confirmarSegundaEtapa: vi.fn(), quemSou: vi.fn(), sair: vi.fn(),
}));

import { confirmarSegundaEtapa, entrar } from '@/api/auth';

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
          <Route path="/trocar-senha" element={<p>Defina a sua senha</p>} />
          <Route path="/autenticador" element={<p>Cadastre o autenticador</p>} />
        </Routes>
      </SessaoProvider>
    </MemoryRouter>,
  );

describe('tela de login', () => {
  beforeEach(() => { sessionStorage.clear(); localStorage.clear(); vi.clearAllMocks(); });

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
    vi.mocked(entrar).mockResolvedValue({ usuario: admin });
    abrir();

    await usuario.type(screen.getByLabelText('E-mail'), 'admin@trinosupply.com.br');
    await usuario.type(document.querySelector('#password')!, 'TrinoSupply@2026!');
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }));

    // no navegador a caixa vem desmarcada: a sessão fica na aba
    expect(entrar).toHaveBeenCalledWith('admin@trinosupply.com.br', 'TrinoSupply@2026!', false);
    await waitFor(() => expect(screen.getByText('Meus pedidos')).toBeInTheDocument());
  });

  it('"Manter conectado" vai junto na entrada, e vem marcada para quem já pediu', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrar).mockResolvedValue({ usuario: admin });
    abrir();
    const caixa = screen.getByRole('checkbox', { name: /Manter conectado neste aparelho/ });
    expect(caixa).not.toBeChecked();
    await usuario.click(caixa);
    await usuario.type(screen.getByLabelText('E-mail'), 'admin@trinosupply.com.br');
    await usuario.type(document.querySelector('#password')!, 'TrinoSupply@2026!');
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }));
    expect(entrar).toHaveBeenCalledWith('admin@trinosupply.com.br', 'TrinoSupply@2026!', true);

    localStorage.setItem('ts.lembrar', '1');      // a escolha da última entrada neste aparelho
    abrir();
    expect(screen.getAllByRole('checkbox', { name: /Manter conectado neste aparelho/ }).at(-1)).toBeChecked();
  });

  it('senha provisória leva à troca, e não ao destino guardado', () => {
    expect(destinoDe({ ...admin, mustChangePassword: true }, '/cotacoes')).toBe('/trocar-senha');
    expect(destinoDe({ ...admin, mustChangePassword: false }, '/cotacoes')).toBe('/cotacoes');
    expect(destinoDe(admin)).toBe('/pedidos');
    // a senha vem antes do autenticador: quem tem as duas pendências troca a senha primeiro
    expect(destinoDe({ ...admin, mustSetupMfa: true }, '/cotacoes')).toBe('/autenticador');
    expect(destinoDe({ ...admin, mustChangePassword: true, mustSetupMfa: true })).toBe('/trocar-senha');
  });

  it('com a segunda etapa ligada, a senha certa pede o código e só ele abre a sessão', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrar).mockResolvedValue({ mfaToken: 'token-pendente' });
    vi.mocked(confirmarSegundaEtapa).mockResolvedValue({ ...admin, usedRecoveryCode: false, recoveryCodesLeft: 8 });
    abrir();

    await usuario.type(screen.getByLabelText('E-mail'), 'admin@trinosupply.com.br');
    await usuario.type(document.querySelector('#password')!, 'TrinoSupply@2026!');
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }));

    const etapa = await screen.findByTestId('segunda-etapa');
    expect(within(etapa).getByRole('button', { name: 'Confirmar' })).toBeDisabled();   // sem código não manda
    await usuario.type(screen.getByLabelText('Código do autenticador'), '123456');
    await usuario.click(screen.getByRole('button', { name: 'Confirmar' }));

    expect(confirmarSegundaEtapa).toHaveBeenCalledWith('token-pendente', '123456', false);
    await waitFor(() => expect(screen.getByText('Meus pedidos')).toBeInTheDocument());
  });

  it('código recusado mostra o motivo e "Voltar" devolve à senha', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrar).mockResolvedValue({ mfaToken: 'token-pendente' });
    vi.mocked(confirmarSegundaEtapa).mockRejectedValue(new Error('Código inválido.'));
    abrir();
    await usuario.type(screen.getByLabelText('E-mail'), 'admin@trinosupply.com.br');
    await usuario.type(document.querySelector('#password')!, 'TrinoSupply@2026!');
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }));
    await usuario.type(await screen.findByLabelText('Código do autenticador'), '000000');
    await usuario.click(screen.getByRole('button', { name: 'Confirmar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Código inválido.');

    await usuario.click(screen.getByRole('button', { name: 'Voltar' }));
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeInTheDocument();
  });

  it('quem entra com senha provisória cai na tela de troca', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrar).mockResolvedValue({ usuario: { ...admin, mustChangePassword: true } });
    abrir();

    await usuario.type(screen.getByLabelText('E-mail'), 'admin@trinosupply.com.br');
    await usuario.type(document.querySelector('#password')!, 'Provisoria#2026');
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }));

    await waitFor(() => expect(screen.getByText('Defina a sua senha')).toBeInTheDocument());
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
