import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { CentroCusto } from '@/api/centrosCusto';
import type { UsuarioPicker } from '@/api/usuarios';
import { ToastProvider } from '@/componentes/Toast';
import { candidatosDoNivel, CentrosCusto } from './CentrosCusto';

vi.mock('@/api/centrosCusto', async (importar) => ({
  ...(await importar<typeof import('@/api/centrosCusto')>()),
  listarCentrosCusto: vi.fn(),
  atualizarCentroCusto: vi.fn(),
}));
vi.mock('@/api/usuarios', () => ({ listarUsuariosPicker: vi.fn() }));
vi.mock('@/api/empresas', () => ({ listarEmpresas: vi.fn() }));
let usuarioAtual: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => usuarioAtual }));

import { atualizarCentroCusto, listarCentrosCusto } from '@/api/centrosCusto';
import { listarUsuariosPicker } from '@/api/usuarios';
import { listarEmpresas } from '@/api/empresas';

const usuarios: UsuarioPicker[] = [
  { id: 'ap1', name: 'Ana Aprovadora', role: 'Approver' },
  { id: 'di1', name: 'Davi Diretor', role: 'Director' },
  { id: 'so1', name: 'Sofia Solicitante', role: 'Requester' },
];

const centro: CentroCusto = {
  id: 'cc1', code: 'BAH-001', name: 'PepsiCo Simões Filho', region: 'BAHIA',
  companyId: 'emp1', managerUserId: 'ap1', managerName: 'Ana Aprovadora', clientName: 'PepsiCo',
  active: true, level1ValueLimit: 50000, level2ValueLimit: null,
  level1: [{ userId: 'ap1', name: 'Ana Aprovadora' }], level2: [],
};

const gestor: Usuario = { id: 'g1', email: 'g@t.com', name: 'Gestor', role: 'SupplyManager', modules: ['CENTROS_CUSTO'] };

describe('candidatosDoNivel', () => {
  it('nível 1 lista aprovadores e gestores; nível 2, diretoria', () => {
    expect(candidatosDoNivel(usuarios, 'level1', []).map((u) => u.id)).toEqual(['ap1']);
    expect(candidatosDoNivel(usuarios, 'level2', []).map((u) => u.id)).toEqual(['di1']);
  });
  it('quem já está marcado continua na lista mesmo com papel de outro nível', () => {
    expect(candidatosDoNivel(usuarios, 'level2', ['so1']).map((u) => u.id)).toEqual(['di1', 'so1']);
  });
});

describe('<CentrosCusto />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    usuarioAtual = gestor;
    vi.mocked(listarCentrosCusto).mockResolvedValue([centro]);
    vi.mocked(listarUsuariosPicker).mockResolvedValue(usuarios);
    vi.mocked(listarEmpresas).mockResolvedValue([{ id: 'emp1', legalName: 'Trino Serviços LTDA', taxId: '11222333000144' }]);
  });

  const montar = () => render(<ToastProvider><CentrosCusto /></ToastProvider>);

  it('lista com alçadas, limites e CNPJ resolvido pelo nome', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-centros-custo')).toBeInTheDocument());
    expect(screen.getByText('BAH-001')).toBeInTheDocument();
    expect(screen.getByText('diretor vinculado')).toBeInTheDocument();
    expect(screen.getByText(/N1 R\$/)).toBeInTheDocument();
    expect(screen.getByText('Trino Serviços LTDA')).toBeInTheDocument();
  });

  it('editar carrega as alçadas marcadas do centro', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-centros-custo')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Editar' }));
    expect(screen.getByRole('heading', { name: /Editar centro de custo — BAH-001/ })).toBeInTheDocument();
    expect(screen.getByLabelText(/Ana Aprovadora/)).toBeChecked();
    expect(screen.getByLabelText(/Davi Diretor/)).not.toBeChecked();
    expect(screen.getByLabelText('Nível 1 (R$)')).toHaveValue(50000);
  });

  it('inativar manda apenas a mudança de situação', async () => {
    vi.mocked(atualizarCentroCusto).mockResolvedValue(centro);
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-centros-custo')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Inativar' }));
    await waitFor(() => expect(atualizarCentroCusto).toHaveBeenCalledWith('cc1', { active: false }));
  });
});
