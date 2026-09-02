import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { Familia } from '@/api/familias';
import { ToastProvider } from '@/componentes/Toast';
import type { Usuario } from '@/api/auth';
import { Familias, resumoPrazos } from './Familias';

vi.mock('@/api/familias', async (importar) => ({
  ...(await importar<typeof import('@/api/familias')>()),
  listarFamilias: vi.fn(),
}));
vi.mock('@/api/catalogo', () => ({ contarProdutosPorFamilia: vi.fn() }));

let usuarioAtual: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => usuarioAtual }));

import { listarFamilias } from '@/api/familias';
import { contarProdutosPorFamilia } from '@/api/catalogo';

const familia = (p: Partial<Familia>): Familia => ({
  id: 'f-' + p.name, name: 'EPI', notes: null, active: true, category: null,
  leadRequestToQuote: null, leadQuoteToApproval: null, leadApprovalToPo: null, leadPoToDelivery: null,
  leadTotal: null, ...p,
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
});
