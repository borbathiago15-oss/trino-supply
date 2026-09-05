import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import { ToastProvider } from '@/componentes/Toast';
import { pendenciasDaSenha, TrocarSenha } from './TrocarSenha';

vi.mock('@/api/auth', async (importar) => ({
  ...(await importar<typeof import('@/api/auth')>()),
  trocarSenha: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({
  useSessao: () => ({ usuario: eu, carregando: false, entrou, sair: vi.fn() }),
}));

import { trocarSenha } from '@/api/auth';

const entrou = vi.fn();
let eu: Usuario | null = null;

const ana: Usuario = {
  id: 'u1', email: 'ana.souza@trino.com.br', name: 'Ana Souza',
  role: 'Requester', modules: ['SOLICITACOES'], mustChangePassword: true,
};

const abrir = () => render(
  <MemoryRouter initialEntries={['/trocar-senha']}>
    <ToastProvider>
      <Routes>
        <Route path="/trocar-senha" element={<TrocarSenha />} />
        <Route path="/painel" element={<p>Painel de Suprimentos</p>} />
      </Routes>
    </ToastProvider>
  </MemoryRouter>,
);

const preencher = async (usuario: ReturnType<typeof userEvent.setup>, atual: string, nova: string, confirma = nova) => {
  await usuario.type(screen.getByLabelText(/Senha provisória/), atual);
  await usuario.type(screen.getByLabelText('Nova senha'), nova);
  await usuario.type(screen.getByLabelText('Repita a nova senha'), confirma);
};

describe('exigências da senha', () => {
  const checar = (nova: string, atual = 'Provisoria#2026') =>
    pendenciasDaSenha(nova, atual, 'Ana Souza', 'ana.souza@trino.com.br');

  it('aceita uma senha forte e sem relação com o dono', () => {
    expect(checar('Kx7$paralelo!vento')).toEqual([]);
  });
  it('recusa curta, repetida, com o nome do dono e com palavra previsível', () => {
    expect(checar('Kx7$curta')).toContain('Ter pelo menos 12 caracteres');
    expect(checar('Provisoria#2026')).toContain('Ser diferente da senha atual');
    expect(checar('Kx7$anaSouzaOk!')).toContain('Não conter o seu nome nem o seu e-mail');
    expect(checar('Kx7$trinosupply!')).toContain('Não usar palavras previsíveis como "senha", "123456" ou o nome do sistema');
    expect(checar('apenasminusculas')).toContain('Combinar ao menos três entre minúscula, maiúscula, número e símbolo');
  });
  it('campo vazio acusa só o que dá para saber, sem citar nome nem senha atual', () => {
    // a tela nem mostra a lista com o campo vazio; aqui é o contrato da função
    expect(checar('')).toEqual([
      'Ter pelo menos 12 caracteres',
      'Combinar ao menos três entre minúscula, maiúscula, número e símbolo',
    ]);
  });
});

describe('tela de troca de senha', () => {
  beforeEach(() => { vi.clearAllMocks(); eu = { ...ana }; });

  it('explica por que a troca é obrigatória no primeiro acesso', () => {
    abrir();
    expect(screen.getByTestId('senha-provisoria')).toHaveTextContent(/definida por quem cadastrou/);
  });

  it('mantém o botão travado até a senha passar nas exigências e conferir', async () => {
    const usuario = userEvent.setup();
    abrir();
    const botao = screen.getByRole('button', { name: 'Salvar nova senha' });
    expect(botao).toBeDisabled();

    await preencher(usuario, 'Provisoria#2026', 'Kx7$curta', 'Kx7$curta');
    expect(screen.getByTestId('pendencias-senha')).toHaveTextContent('Ter pelo menos 12 caracteres');
    expect(botao).toBeDisabled();

    await usuario.clear(screen.getByLabelText('Nova senha'));
    await usuario.type(screen.getByLabelText('Nova senha'), 'Kx7$paralelo!vento');
    expect(screen.getByText('As duas senhas não são iguais.')).toBeInTheDocument();
    expect(botao).toBeDisabled();

    await usuario.clear(screen.getByLabelText('Repita a nova senha'));
    await usuario.type(screen.getByLabelText('Repita a nova senha'), 'Kx7$paralelo!vento');
    expect(botao).toBeEnabled();
  });

  it('troca a senha e segue para o painel', async () => {
    const usuario = userEvent.setup();
    vi.mocked(trocarSenha).mockResolvedValue({ ...ana, mustChangePassword: false });
    abrir();

    await preencher(usuario, 'Provisoria#2026', 'Kx7$paralelo!vento');
    await usuario.click(screen.getByRole('button', { name: 'Salvar nova senha' }));

    expect(trocarSenha).toHaveBeenCalledWith('Provisoria#2026', 'Kx7$paralelo!vento');
    await waitFor(() => expect(screen.getByText('Painel de Suprimentos')).toBeInTheDocument());
    expect(entrou).toHaveBeenCalledWith(expect.objectContaining({ mustChangePassword: false }));
  });

  it('mostra a recusa do servidor e mantém a pessoa na tela', async () => {
    const usuario = userEvent.setup();
    vi.mocked(trocarSenha).mockRejectedValue(new Error('A senha atual não confere.'));
    abrir();

    await preencher(usuario, 'Chutei#2026aqui', 'Kx7$paralelo!vento');
    await usuario.click(screen.getByRole('button', { name: 'Salvar nova senha' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('A senha atual não confere.');
    expect(screen.queryByText('Painel de Suprimentos')).not.toBeInTheDocument();
  });

  it('quem já definiu a senha vê a tela como alteração comum', () => {
    eu = { ...ana, mustChangePassword: false };
    abrir();
    expect(screen.queryByTestId('senha-provisoria')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Senha atual')).toBeInTheDocument();
  });
});
