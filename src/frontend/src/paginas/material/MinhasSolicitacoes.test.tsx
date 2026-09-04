import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Usuario } from '@/api/auth';
import { podeCancelarMaterial, type SolicitacaoMaterial } from '@/api/material';
import { ToastProvider } from '@/componentes/Toast';
import { descricaoDoItem, MinhasSolicitacoes } from './MinhasSolicitacoes';

vi.mock('@/api/material', async (importar) => ({
  ...(await importar<typeof import('@/api/material')>()),
  listarSolicitacoesMaterial: vi.fn(),
  cancelarMaterial: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import { cancelarMaterial, listarSolicitacoesMaterial } from '@/api/material';

const eu: Usuario = { id: 'u1', email: 'ana@t.com', name: 'Ana', role: 'Requester', modules: ['MATERIAL'] };

const mr = (p: Partial<SolicitacaoMaterial>): SolicitacaoMaterial => ({
  id: 'mr-' + (p.number ?? 'MR-1'), number: 'MR-2026-000001', status: 'AGUARDANDO_ALMOXARIFADO',
  costCenter: 'BAH-001', requesterId: 'u1', requesterLabel: 'Ana', notes: null,
  items: [{
    itemId: 'i1', catalogCode: 'EPI-001', description: 'Luva nitrílica', quantity: 10,
    unitOfMeasure: 'PAR', status: 'PENDENTE',
  }],
  ...p,
});

const abrir = () => render(
  <MemoryRouter><ToastProvider><MinhasSolicitacoes /></ToastProvider></MemoryRouter>,
);

describe('regras da solicitação de material', () => {
  it('só o solicitante cancela, e só antes do almoxarifado atender', () => {
    expect(podeCancelarMaterial(mr({ status: 'AGUARDANDO_APROVACAO' }), 'u1')).toBe(true);
    expect(podeCancelarMaterial(mr({ status: 'AGUARDANDO_ALMOXARIFADO' }), 'u1')).toBe(true);
    expect(podeCancelarMaterial(mr({ status: 'ATENDIDA' }), 'u1')).toBe(false);
    expect(podeCancelarMaterial(mr({ status: 'ROTA_DE_COMPRA' }), 'u1')).toBe(false);
    expect(podeCancelarMaterial(mr({ status: 'AGUARDANDO_ALMOXARIFADO' }), 'outro')).toBe(false);
  });
  it('o item aparece com quantidade, código e onde ele está', () => {
    expect(descricaoDoItem({
      itemId: 'i1', catalogCode: 'EPI-001', description: 'Luva', quantity: 10,
      unitOfMeasure: 'PAR', status: 'ENTREGUE',
    })).toBe('10× [EPI-001] Luva (entregue ✓)');
  });
});

describe('tela Minhas Solicitações de Material', () => {
  beforeEach(() => vi.resetAllMocks());

  it('mostra a situação com o nome que o time lê, não o código', async () => {
    vi.mocked(listarSolicitacoesMaterial).mockResolvedValue([
      mr({ number: 'MR-2026-000001', status: 'AGUARDANDO_APROVACAO' }),
      mr({ number: 'MR-2026-000002', status: 'ATENDIDA_PARCIAL', purchaseRequisitionNumber: 'SC-2026-000010' }),
    ]);
    abrir();
    const tabela = await screen.findByTestId('tabela-material');
    expect(within(tabela).getByText('Aguardando aprovação')).toBeInTheDocument();
    expect(within(tabela).getByText('Atendida parcialmente')).toBeInTheDocument();
    expect(within(tabela).getByText(/SC-2026-000010/)).toBeInTheDocument();
  });

  it('cancelar exige o motivo antes de chamar a API', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarSolicitacoesMaterial).mockResolvedValue([mr({})]);
    vi.mocked(cancelarMaterial).mockResolvedValue(undefined);
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Cancelar' }));
    const dialogo = screen.getByRole('dialog');
    const confirmar = within(dialogo).getByRole('button', { name: 'Cancelar solicitação' });
    expect(confirmar).toBeDisabled();
    expect(cancelarMaterial).not.toHaveBeenCalled();

    await usuario.type(within(dialogo).getByLabelText(/Motivo do cancelamento/), 'pedido em duplicidade');
    await usuario.click(confirmar);
    await waitFor(() => expect(cancelarMaterial).toHaveBeenCalledWith('mr-MR-1', 'pedido em duplicidade'));
  });

  it('não oferece cancelar a solicitação de outra pessoa', async () => {
    vi.mocked(listarSolicitacoesMaterial).mockResolvedValue([mr({ requesterId: 'u9', requesterLabel: 'João' })]);
    abrir();
    await screen.findByTestId('tabela-material');
    expect(screen.queryByRole('button', { name: 'Cancelar' })).not.toBeInTheDocument();
  });
});
