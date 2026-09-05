import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { PedidoCompra } from '@/api/pedidos';
import { ToastProvider } from '@/componentes/Toast';
import { PedidoDetalhe } from './PedidoDetalhe';

vi.mock('@/api/pedidos', async (importar) => ({
  ...(await importar<typeof import('@/api/pedidos')>()),
  obterPedido: vi.fn(), registrarOc: vi.fn(), anexarOc: vi.fn(),
  lancarNota: vi.fn(), anexarNota: vi.fn(), registrarEntrega: vi.fn(), pdfPedido: vi.fn(),
}));
vi.mock('@/api/estoque', async (importar) => ({
  ...(await importar<typeof import('@/api/estoque')>()),
  listarLocais: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import { obterPedido, registrarOc } from '@/api/pedidos';
import { listarLocais } from '@/api/estoque';

let eu: Usuario = {
  id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'],
};

const pedido = (p: Partial<PedidoCompra>): PedidoCompra => ({
  id: 'po1', number: 'PO-2026-000001', status: 'EMITIDO', supplierId: 's1', supplierName: 'Alfa',
  sourcePrNumber: 'SC-2026-000001', quotationNumber: null, paymentTerms: null, deliveryDays: null,
  freightValue: null, families: ['EPI'], notes: null, totalValue: 1500, issuedByLabel: 'Carla',
  receivedByLabel: null, receivedAt: null, cancelReason: null, createdAt: '2026-09-01T10:00:00Z',
  erpNumber: null, noErpReason: null, erpIssuedOn: null, promisedDate: null,
  onTime: null, inFull: null, otif: null, referenceSavingTotal: null, erpDocumentId: null,
  erpFileName: null, deliveryCompletedAt: null, pendingDelivery: true, invoices: [],
  items: [{
    itemId: 'i1', description: 'Luva nitrílica', unitOfMeasure: 'PAR', quantity: 10, receivedQuantity: 0,
    pendingQuantity: 10, rejectedQuantity: 0, rejectionReason: null, lastPaidUnitPrice: null,
    referenceSaving: null, sourcePrNumber: null, unitPrice: 150, catalogCode: 'EPI-001',
    catalogItemId: null, family: 'EPI',
  }],
  ...p,
});

const abrir = () => render(
  <MemoryRouter initialEntries={['/pedidos/po1']}>
    <ToastProvider>
      <Routes><Route path="/pedidos/:id" element={<PedidoDetalhe />} /></Routes>
    </ToastProvider>
  </MemoryRouter>,
);

describe('O.C. do ERP no pedido', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'] };
    vi.mocked(listarLocais).mockResolvedValue([]);
  });

  it('com o número preenchido registra a O.C. e não pede motivo', async () => {
    const usuario = userEvent.setup();
    vi.mocked(obterPedido).mockResolvedValue(pedido({}));
    vi.mocked(registrarOc).mockResolvedValue(pedido({ erpNumber: '663' }));
    abrir();

    const painel = await screen.findByRole('region', { name: 'OC do ERP' }).catch(() => null)
      ?? document.querySelector('#oc-erp')!;
    await usuario.type(within(painel as HTMLElement).getByLabelText('Número da OC no ERP'), '663');
    expect(within(painel as HTMLElement).queryByLabelText(/por que a O.C. não foi gerada/i)).not.toBeInTheDocument();

    await usuario.click(within(painel as HTMLElement).getByRole('button', { name: 'Registrar OC' }));
    await waitFor(() => expect(registrarOc).toHaveBeenCalledWith('po1', {
      erpNumber: '663', issuedOn: null, noErpReason: null,
    }));
  });

  it('sem o número, a observação é obrigatória e libera o fechamento', async () => {
    const usuario = userEvent.setup();
    vi.mocked(obterPedido).mockResolvedValue(pedido({}));
    vi.mocked(registrarOc).mockResolvedValue(pedido({ noErpReason: 'Compra emergencial de balcão.' }));
    abrir();

    const motivo = await screen.findByLabelText(/por que a O.C. não foi gerada/i);
    const botao = screen.getByRole('button', { name: 'Fechar sem O.C.' });
    expect(botao).toBeDisabled();

    await usuario.type(motivo, 'curto');
    expect(botao).toBeDisabled();

    await usuario.clear(motivo);
    await usuario.type(motivo, 'Compra emergencial de balcão.');
    expect(botao).toBeEnabled();
    await usuario.click(botao);

    await waitFor(() => expect(registrarOc).toHaveBeenCalledWith('po1', {
      erpNumber: '', issuedOn: null, noErpReason: 'Compra emergencial de balcão.',
    }));
  });

  it('a compra sem O.C. mostra a referência interna e o motivo registrado', async () => {
    vi.mocked(obterPedido).mockResolvedValue(pedido({
      erpIssuedOn: '2026-09-01',
      noErpReason: 'Fornecedor entregou antes de a O.C. sair do SENIOR.',
    }));
    abrir();

    expect(await screen.findByTestId('motivo-sem-oc')).toHaveTextContent('Fornecedor entregou antes');
    const painel = document.querySelector('#oc-erp') as HTMLElement;
    expect(within(painel).getByText('sem O.C. do ERP')).toBeInTheDocument();
    // o pedido segue com a própria numeração, nada que pareça O.C. do SENIOR
    expect(within(painel).getByText('PO-2026-000001')).toBeInTheDocument();
    // sem O.C. de verdade, o campo do número segue vazio e o motivo continua exigido
    expect(within(painel).getByLabelText('Número da OC no ERP')).toHaveValue('');
  });
});
