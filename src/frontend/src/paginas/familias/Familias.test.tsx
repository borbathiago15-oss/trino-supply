import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { Familia } from '@/api/familias';
import { ToastProvider } from '@/componentes/Toast';
import type { Usuario } from '@/api/auth';
import { Familias, resumoPrazos } from './Familias';

vi.mock('@/api/familias', async (importar) => ({
  ...(await importar<typeof import('@/api/familias')>()),
  listarFamilias: vi.fn(),
  excluirFamilia: vi.fn(),
  criarFamilia: vi.fn(),
}));
vi.mock('@/api/catalogo', () => ({ contarProdutosPorFamilia: vi.fn() }));

let usuarioAtual: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => usuarioAtual }));

import { criarFamilia, excluirFamilia, listarFamilias } from '@/api/familias';
import { contarProdutosPorFamilia } from '@/api/catalogo';

const familia = (p: Partial<Familia>): Familia => ({
  id: 'f-' + p.name, name: 'EPI', notes: null, active: true, category: null,
  leadRequestToQuote: null, leadQuoteToApproval: null, leadApprovalToPo: null, leadPoToDelivery: null,
  leadTotal: null, materialRequestable: true, ...p,
});

const gestor: Usuario = { id: 'u1', email: 'g@t.com', name: 'Gestor', role: 'SupplyManager', modules: ['PRODUTOS'] };
const solicitante: Usuario = { id: 'u2', email: 's@t.com', name: 'Ana', role: 'Requester', modules: [] };

describe('resumoPrazos', () => {
  it('soma as quatro etapas quando há meta', () => {
    expect(resumoPrazos(familia({ leadRequestToQuote: 2, leadQuoteToApproval: 3, leadApprovalToPo: 1, leadPoToDelivery: 15, leadTotal: 21 })))
      .toBe('2 + 3 + 1 + 15 = 21');
  });
  it('avisa quando a família não tem meta', () => {
    expect(resumoPrazos(familia({}))).toBe('sem meta definida');
  });
});

describe('<Familias />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    usuarioAtual = gestor;
    vi.mocked(listarFamilias).mockResolvedValue([
      familia({ name: 'EPI', category: 'SEGURANÇA', leadTotal: 21, leadRequestToQuote: 2, leadQuoteToApproval: 3, leadApprovalToPo: 1, leadPoToDelivery: 15 }),
      familia({ name: 'LIMPEZA', active: false }),
    ]);
    vi.mocked(contarProdutosPorFamilia).mockResolvedValue({ EPI: 12 });
  });

  const montar = () => render(<ToastProvider><Familias /></ToastProvider>);

  it('lista as famílias com categoria, uso, prazos e situação', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-familias')).toBeInTheDocument());
    expect(screen.getByText('SEGURANÇA')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
    expect(screen.getByText('2 + 3 + 1 + 15 = 21')).toBeInTheDocument();
    expect(screen.getByText('INATIVA')).toBeInTheDocument();
  });

  it('quem mantém o catálogo vê o formulário e a lista pede inativas', async () => {
    montar();
    await waitFor(() => expect(screen.getByLabelText('Nome')).toBeInTheDocument());
    expect(vi.mocked(listarFamilias).mock.calls[0][0]).toBe(true);
    expect(screen.getByRole('button', { name: 'Cadastrar família' })).toBeInTheDocument();
  });

  it('quem não mantém vê só a lista, sem formulário nem ações', async () => {
    usuarioAtual = solicitante;
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-familias')).toBeInTheDocument());
    expect(screen.queryByLabelText('Nome')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(vi.mocked(listarFamilias).mock.calls[0][0]).toBe(false);
  });

  it('só a família sem produto pode ser excluída — com produto, o botão diz o que fazer', async () => {
    montar();
    const epi = await screen.findByText('EPI');
    const linhaEpi = epi.closest('tr')!;
    const excluirEpi = within(linhaEpi).getByRole('button', { name: 'Excluir' });
    expect(excluirEpi).toBeDisabled();
    expect(excluirEpi).toHaveAttribute('title', expect.stringMatching(/mova-os para outra família/));
    const linhaLimpeza = screen.getByText('LIMPEZA').closest('tr')!;
    expect(within(linhaLimpeza).getByRole('button', { name: 'Excluir' })).toBeEnabled();
  });

  it('excluir pede confirmação e a recusa do servidor aparece', async () => {
    vi.mocked(excluirFamilia).mockRejectedValue(new Error('1 produto(s) do catálogo estão na família LIMPEZA, contando os inativos.'));
    montar();
    const linha = (await screen.findByText('LIMPEZA')).closest('tr')!;
    await userEvent.click(within(linha).getByRole('button', { name: 'Excluir' }));
    const dialogo = screen.getByRole('dialog', { name: 'Excluir família' });
    await userEvent.click(within(dialogo).getByRole('button', { name: 'Excluir' }));
    expect(excluirFamilia).toHaveBeenCalledWith('f-LIMPEZA');
    expect(await screen.findByText(/contando os inativos/)).toBeInTheDocument();
  });

  it('a família nasce de almoxarifado, e desmarcar vai no corpo', async () => {
    // o campo chegou a uma tela em uso: nascer desmarcado tiraria todo grupo de
    // Solicitar Material sem ninguém ter decidido nada
    const usuario = userEvent.setup();
    montar();
    await screen.findByTestId('fam-material');

    expect(screen.getByTestId('fam-material')).toBeChecked();
    await usuario.type(screen.getByLabelText('Nome'), 'SERVICOS');
    await usuario.click(screen.getByTestId('fam-material'));
    await usuario.click(screen.getByRole('button', { name: 'Cadastrar família' }));

    await waitFor(() => expect(criarFamilia).toHaveBeenCalledWith(
      expect.objectContaining({ materialRequestable: false })));
  });

  it('editar uma família traz a marca que ela tem', async () => {
    vi.mocked(listarFamilias).mockResolvedValue([familia({ name: 'SERVICOS', materialRequestable: false })]);
    const usuario = userEvent.setup();
    montar();
    await usuario.click((await screen.findAllByRole('button', { name: 'Editar' }))[0]);

    expect(screen.getByTestId('fam-material')).not.toBeChecked();
  });
});
