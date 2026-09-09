import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { CondicaoDePagamento } from '@/api/pagamentos';
import { ToastProvider } from '@/componentes/Toast';
import { CondicoesDePagamento, resumoCondicao } from './CondicoesDePagamento';

vi.mock('@/api/pagamentos', () => ({
  listarCondicoesDePagamento: vi.fn(),
  criarCondicaoDePagamento: vi.fn(),
  atualizarCondicaoDePagamento: vi.fn(),
}));

let usuarioAtual: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => usuarioAtual }));

import {
  atualizarCondicaoDePagamento, criarCondicaoDePagamento, listarCondicoesDePagamento,
} from '@/api/pagamentos';

const condicao = (p: Partial<CondicaoDePagamento>): CondicaoDePagamento => ({
  id: 'c-' + (p.name ?? 'x'), name: 'À Vista', installments: 1, firstDueDays: 0,
  isDefault: false, active: true, ...p,
});

const comprador: Usuario = {
  id: 'u1', email: 'c@t.com', name: 'Comprador', role: 'PurchasingOfficer', modules: ['COMPRAS'],
};
const solicitante: Usuario = { id: 'u2', email: 's@t.com', name: 'Ana', role: 'Requester', modules: [] };

describe('resumoCondicao', () => {
  it('diz em quantas parcelas e quando vence a primeira', () => {
    expect(resumoCondicao(condicao({ name: 'Parcelado 30/60/90', installments: 3, firstDueDays: 30 })))
      .toBe('3 parcelas · 1ª em 30 dias');
  });

  it('zero dia é à vista, não "em 0 dias"', () => {
    expect(resumoCondicao(condicao({ installments: 1, firstDueDays: 0 }))).toBe('1 parcela · 1ª à vista');
  });

  it('condição sem prazo definido diz só as parcelas', () => {
    expect(resumoCondicao(condicao({ installments: 2, firstDueDays: null }))).toBe('2 parcelas');
  });
});

describe('<CondicoesDePagamento />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    usuarioAtual = comprador;
    vi.mocked(listarCondicoesDePagamento).mockResolvedValue([
      condicao({ name: 'À Vista' }),
      condicao({ name: 'Parcelado 30/60/90', installments: 3, firstDueDays: 30, isDefault: true }),
      condicao({ name: 'Parcelado 14/28', installments: 2, firstDueDays: 14, active: false }),
    ]);
    vi.mocked(criarCondicaoDePagamento).mockResolvedValue(condicao({ name: 'Nova' }));
    vi.mocked(atualizarCondicaoDePagamento).mockResolvedValue(condicao({ name: 'À Vista' }));
  });

  const montar = () => render(<ToastProvider><CondicoesDePagamento /></ToastProvider>);

  it('lista as condições com parcelas, prazo e situação, e marca a sugerida', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-condicoes-pagamento')).toBeInTheDocument());
    expect(screen.getByText('3 parcelas · 1ª em 30 dias')).toBeInTheDocument();
    expect(screen.getByText('(sugerida)')).toBeInTheDocument();
    expect(screen.getByText('INATIVA')).toBeInTheDocument();
  });

  it('quem compra vê o formulário e a lista traz também as inativas', async () => {
    montar();
    await waitFor(() => expect(screen.getByLabelText('Nome')).toBeInTheDocument());
    expect(vi.mocked(listarCondicoesDePagamento).mock.calls[0][0]).toBe(true);
  });

  it('quem não compra vê só a lista ativa, sem formulário nem ações', async () => {
    usuarioAtual = solicitante;
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-condicoes-pagamento')).toBeInTheDocument());
    expect(screen.queryByLabelText('Nome')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(vi.mocked(listarCondicoesDePagamento).mock.calls[0][0]).toBe(false);
  });

  it('cadastrar envia parcelas como número e o prazo em branco como nulo', async () => {
    // "" virando 0 diria "vence à vista" numa condição que apenas não define prazo
    montar();
    await waitFor(() => expect(screen.getByLabelText('Nome')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText('Nome'), 'Parcelado 15/30');
    await userEvent.clear(screen.getByLabelText('Nº de parcelas'));
    await userEvent.type(screen.getByLabelText('Nº de parcelas'), '2');
    await userEvent.click(screen.getByRole('button', { name: 'Cadastrar condição' }));

    await waitFor(() => expect(criarCondicaoDePagamento).toHaveBeenCalledWith({
      name: 'Parcelado 15/30', installments: 2, firstDueDays: null, isDefault: false,
    }));
  });

  it('inativar não apaga: manda active false e a linha continua no cadastro', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-condicoes-pagamento')).toBeInTheDocument());
    const linha = screen.getByText('À Vista').closest('tr')!;
    await userEvent.click(within(linha).getByRole('button', { name: 'Inativar' }));

    await waitFor(() => expect(atualizarCondicaoDePagamento)
      .toHaveBeenCalledWith('c-À Vista', { active: false }));
  });
});
