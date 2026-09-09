import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogoDePrioridade } from './DialogoDePrioridade';

vi.mock('@/api/triagem', () => ({ alterarPrioridade: vi.fn() }));

import { alterarPrioridade } from '@/api/triagem';

/**
 * A régua da mudança de prioridade, testada onde ela vive.
 *
 * Estes casos vinham da tela de triagem. Quando a triagem de compra passou para a Torre,
 * o diálogo virou compartilhado — e o teste veio com ele, em vez de ficar preso a uma das
 * duas telas. É o que garante que nenhuma delas aceite urgência sem impacto.
 */
describe('<DialogoDePrioridade />', () => {
  const aoAvisar = vi.fn();
  const aoSalvar = vi.fn();
  const aoFechar = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(alterarPrioridade).mockResolvedValue(undefined);
  });

  const abrir = (para: 'URGENT' | 'NORMAL') => render(
    <DialogoDePrioridade pleito={{ id: 'sc1', numero: 'SC-2026-000001', para }}
      aoFechar={aoFechar} aoSalvar={aoSalvar} aoAvisar={aoAvisar} />,
  );

  it('tornar urgente exige motivo e impacto antes de gravar', async () => {
    const usuario = userEvent.setup();
    abrir('URGENT');
    const dialogo = screen.getByRole('dialog');
    const gravar = within(dialogo).getByRole('button', { name: 'Registrar mudança' });
    expect(gravar).toBeDisabled();

    await usuario.type(within(dialogo).getByLabelText(/Por que esta demanda virou urgente/), 'parada de linha');
    expect(gravar).toBeDisabled();   // só o motivo não basta

    await usuario.type(within(dialogo).getByLabelText(/impacto de não comprar/), 'obra parada');
    await usuario.click(gravar);
    await waitFor(() => expect(alterarPrioridade)
      .toHaveBeenCalledWith('sc1', 'URGENT', 'parada de linha', 'obra parada'));
    expect(aoSalvar).toHaveBeenCalled();
  });

  it('voltar a normal pede só o motivo, e o impacto nem aparece', async () => {
    const usuario = userEvent.setup();
    abrir('NORMAL');
    const dialogo = screen.getByRole('dialog');
    expect(within(dialogo).queryByLabelText(/impacto de não comprar/)).not.toBeInTheDocument();

    await usuario.type(within(dialogo).getByLabelText(/volta a Normal/), 'prazo folgou');
    await usuario.click(within(dialogo).getByRole('button', { name: 'Registrar mudança' }));
    // impacto vai nulo, não string vazia: a auditoria distingue "não se aplica" de "em branco"
    await waitFor(() => expect(alterarPrioridade)
      .toHaveBeenCalledWith('sc1', 'NORMAL', 'prazo folgou', null));
  });

  it('falha na API não fecha o diálogo nem descarta o que foi escrito', async () => {
    const usuario = userEvent.setup();
    vi.mocked(alterarPrioridade).mockRejectedValue(new Error('Sem permissão.'));
    abrir('NORMAL');

    await usuario.type(screen.getByLabelText(/volta a Normal/), 'prazo folgou');
    await usuario.click(screen.getByRole('button', { name: 'Registrar mudança' }));

    await waitFor(() => expect(aoAvisar).toHaveBeenCalledWith('Sem permissão.', 'erro'));
    expect(aoFechar).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/volta a Normal/)).toHaveValue('prazo folgou');
  });
});
