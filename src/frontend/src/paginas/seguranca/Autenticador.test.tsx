import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import { Autenticador } from './Autenticador';

vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn(async () => 'data:image/png;base64,QR') } }));
vi.mock('@/api/auth', async (importar) => ({
  ...(await importar<typeof import('@/api/auth')>()),
  estadoDoMfa: vi.fn(), iniciarMfa: vi.fn(), ativarMfa: vi.fn(), desativarMfa: vi.fn(),
}));
const entrou = vi.fn();
let usuario: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useSessao: () => ({ usuario, entrou }) }));

import { ativarMfa, desativarMfa, estadoDoMfa, iniciarMfa } from '@/api/auth';

const diretora: Usuario = { id: 'u1', email: 'dora@t.com', name: 'Dora', role: 'Director', modules: [], mfaRequired: true, mustSetupMfa: true };
const comprador: Usuario = { id: 'u2', email: 'caio@t.com', name: 'Caio', role: 'PurchasingOfficer', modules: [], mfaRequired: false };

const abrir = () => render(
  <MemoryRouter initialEntries={['/autenticador']}>
    <Routes>
      <Route path="/autenticador" element={<Autenticador />} />
      <Route path="/diretoria" element={<p>Visão da diretoria</p>} />
      <Route path="/painel" element={<p>Painel</p>} />
    </Routes>
  </MemoryRouter>,
);

describe('tela do autenticador', () => {
  beforeEach(() => vi.clearAllMocks());

  it('diretor sem autenticador cadastra, confirma o código, guarda os códigos e segue', async () => {
    usuario = diretora;
    vi.mocked(estadoDoMfa).mockResolvedValue({ enabled: false, required: true, enabledAt: null, recoveryCodesLeft: 0 });
    vi.mocked(iniciarMfa).mockResolvedValue({ secret: 'JBSWY3DPEHPK3PXP', otpauthUrl: 'otpauth://totp/Trino%20Supply:dora%40t.com?secret=JBSWY3DPEHPK3PXP' });
    vi.mocked(ativarMfa).mockResolvedValue({ usuario: { ...diretora, mfaEnabled: true, mustSetupMfa: false }, recoveryCodes: ['abcde-fghjk', 'mnpqr-stuvw'] });
    abrir();

    expect(await screen.findByTestId('mfa-obrigatorio')).toHaveTextContent('exige a verificação em duas etapas');
    await userEvent.click(screen.getByTestId('comecar-mfa'));
    expect(await screen.findByRole('img', { name: 'QR code do autenticador' })).toHaveAttribute('src', 'data:image/png;base64,QR');
    expect(screen.getByTestId('segredo-mfa')).toHaveTextContent('JBSWY3DPEHPK3PXP');

    const confirmar = screen.getByRole('button', { name: 'Confirmar código' });
    expect(confirmar).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Código que o aplicativo mostra/), '123456');
    await userEvent.click(confirmar);

    await waitFor(() => expect(ativarMfa).toHaveBeenCalledWith('123456'));
    expect(entrou).toHaveBeenCalledWith(expect.objectContaining({ mfaEnabled: true }));
    const codigos = await screen.findByTestId('codigos-recuperacao');
    expect(codigos).toHaveTextContent('abcde-fghjk');
    expect(codigos).toHaveTextContent('mnpqr-stuvw');

    await userEvent.click(screen.getByRole('button', { name: 'Já guardei os códigos' }));
    expect(await screen.findByText('Visão da diretoria')).toBeInTheDocument();
  });

  it('código recusado mostra o motivo do servidor', async () => {
    usuario = diretora;
    vi.mocked(estadoDoMfa).mockResolvedValue({ enabled: false, required: true, enabledAt: null, recoveryCodesLeft: 0 });
    vi.mocked(iniciarMfa).mockResolvedValue({ secret: 'X', otpauthUrl: 'otpauth://x' });
    vi.mocked(ativarMfa).mockRejectedValue(new Error('Código inválido. Confira o relógio do telefone.'));
    abrir();
    await userEvent.click(await screen.findByTestId('comecar-mfa'));
    await userEvent.type(await screen.findByLabelText(/Código que o aplicativo mostra/), '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar código' }));
    expect(await screen.findByText(/Código inválido/)).toBeInTheDocument();
  });

  it('quem o papel obriga vê que não pode desligar; quem é opcional desliga com o código', async () => {
    usuario = { ...diretora, mfaEnabled: true, mustSetupMfa: false };
    vi.mocked(estadoDoMfa).mockResolvedValue({ enabled: true, required: true, enabledAt: '2026-10-11T12:00:00Z', recoveryCodesLeft: 7 });
    const { unmount } = abrir();
    expect(await screen.findByTestId('mfa-ativa')).toHaveTextContent('restantes: 7');
    expect(screen.queryByRole('button', { name: 'Desligar' })).not.toBeInTheDocument();
    expect(screen.getByText(/não pode ser desligada aqui/)).toBeInTheDocument();
    unmount();

    usuario = { ...comprador, mfaEnabled: true };
    vi.mocked(estadoDoMfa).mockResolvedValue({ enabled: true, required: false, enabledAt: '2026-10-11T12:00:00Z', recoveryCodesLeft: 8 });
    vi.mocked(desativarMfa).mockResolvedValue({});
    abrir();
    await userEvent.type(await screen.findByLabelText(/Para desligar/), '654321');
    await userEvent.click(screen.getByRole('button', { name: 'Desligar' }));
    await waitFor(() => expect(desativarMfa).toHaveBeenCalledWith('654321'));
    expect(entrou).toHaveBeenCalledWith(expect.objectContaining({ mfaEnabled: false }));
  });
});
