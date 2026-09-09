import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { FormaDePagamento } from '@/api/pagamentos';
import { ToastProvider } from '@/componentes/Toast';
import { FormasDePagamento } from './FormasDePagamento';

vi.mock('@/api/pagamentos', () => ({
  listarFormasDePagamento: vi.fn(),
  criarFormaDePagamento: vi.fn(),
  atualizarFormaDePagamento: vi.fn(),
}));

let usuarioAtual: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => usuarioAtual }));

import {
  atualizarFormaDePagamento, criarFormaDePagamento, listarFormasDePagamento,
} from '@/api/pagamentos';

const forma = (p: Partial<FormaDePagamento>): FormaDePagamento => ({
  id: 'f-' + (p.name ?? 'x'), name: 'Boleto Bancário', active: true, ...p,
});

const comprador: Usuario = {
  id: 'u1', email: 'c@t.com', name: 'Comprador', role: 'PurchasingOfficer', modules: ['COMPRAS'],
};
const solicitante: Usuario = { id: 'u2', email: 's@t.com', name: 'Ana', role: 'Requester', modules: [] };

describe('<FormasDePagamento />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    usuarioAtual = comprador;
    vi.mocked(listarFormasDePagamento).mockResolvedValue([
      forma({ name: 'Boleto Bancário' }),
      forma({ name: 'Pix' }),
      forma({ name: 'Cheque', active: false }),
    ]);
    vi.mocked(criarFormaDePagamento).mockResolvedValue(forma({ name: 'Nova' }));
    vi.mocked(atualizarFormaDePagamento).mockResolvedValue(forma({ name: 'Pix' }));
  });

  const montar = () => render(<ToastProvider><FormasDePagamento /></ToastProvider>);

  it('lista as formas com a situação de cada uma', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-formas-pagamento')).toBeInTheDocument());
    expect(screen.getByText('Pix')).toBeInTheDocument();
    expect(screen.getByText('INATIVA')).toBeInTheDocument();
  });

  it('quem compra vê o formulário e a lista traz também as inativas', async () => {
    montar();
    await waitFor(() => expect(screen.getByLabelText('Nome')).toBeInTheDocument());
    expect(vi.mocked(listarFormasDePagamento).mock.calls[0][0]).toBe(true);
  });

  it('quem não compra vê só a lista ativa, sem formulário nem ações', async () => {
    usuarioAtual = solicitante;
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-formas-pagamento')).toBeInTheDocument());
    expect(screen.queryByLabelText('Nome')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(vi.mocked(listarFormasDePagamento).mock.calls[0][0]).toBe(false);
  });

  it('cadastrar manda o nome digitado', async () => {
    montar();
    await waitFor(() => expect(screen.getByLabelText('Nome')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText('Nome'), 'Cartão de Crédito');
    await userEvent.click(screen.getByRole('button', { name: 'Cadastrar forma' }));

    await waitFor(() => expect(criarFormaDePagamento).toHaveBeenCalledWith('Cartão de Crédito'));
  });

  it('editar carrega o nome atual e salva pelo id, sem criar outra', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-formas-pagamento')).toBeInTheDocument());
    const linha = screen.getByText('Pix').closest('tr')!;
    await userEvent.click(within(linha).getByRole('button', { name: 'Editar' }));

    expect(screen.getByLabelText('Nome')).toHaveValue('Pix');
    await userEvent.clear(screen.getByLabelText('Nome'));
    await userEvent.type(screen.getByLabelText('Nome'), 'PIX');
    await userEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }));

    await waitFor(() => expect(atualizarFormaDePagamento).toHaveBeenCalledWith('f-Pix', { name: 'PIX' }));
    expect(criarFormaDePagamento).not.toHaveBeenCalled();
  });

  it('inativar não apaga: manda active false e a forma continua no cadastro', async () => {
    // a proposta antiga que escolheu esta forma tem de continuar legível
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-formas-pagamento')).toBeInTheDocument());
    const linha = screen.getByText('Boleto Bancário').closest('tr')!;
    await userEvent.click(within(linha).getByRole('button', { name: 'Inativar' }));

    await waitFor(() => expect(atualizarFormaDePagamento)
      .toHaveBeenCalledWith('f-Boleto Bancário', { active: false }));
  });
});
