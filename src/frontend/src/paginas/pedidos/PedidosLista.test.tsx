import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PedidoCompra } from '@/api/pedidos';
import { ToastProvider } from '@/componentes/Toast';
import { filtrarPedidos, PedidosLista } from './PedidosLista';

vi.mock('@/api/pedidos', async (importar) => ({
  ...(await importar<typeof import('@/api/pedidos')>()),
  listarPedidos: vi.fn(),
}));
import { listarPedidos } from '@/api/pedidos';

const pedido = (p: Partial<PedidoCompra>): PedidoCompra => ({
  id: 'id-' + p.number, number: 'PO-1', status: 'EMITIDO', supplierId: 's', supplierName: 'Fornecedor A',
  sourcePrNumber: 'SC-2026-000001', quotationNumber: null, paymentTerms: null, deliveryDays: null, freightValue: null,
  families: ['EPI'], notes: null, totalValue: 1500, issuedByLabel: null, receivedByLabel: null, receivedAt: null,
  cancelReason: null, createdAt: '2026-09-01T10:00:00Z', erpNumber: null, erpIssuedOn: null, promisedDate: null,
  onTime: null, inFull: null, otif: null, referenceSavingTotal: null, erpDocumentId: null, erpFileName: null,
  deliveryCompletedAt: null, pendingDelivery: true, invoices: [],
  items: [{ itemId: 'i1', description: 'Luva nitrílica', unitOfMeasure: 'PAR', quantity: 10, receivedQuantity: 4,
    pendingQuantity: 6, rejectedQuantity: 0, rejectionReason: null, lastPaidUnitPrice: null, referenceSaving: null,
    sourcePrNumber: null, unitPrice: 150, catalogCode: 'EPI-001', catalogItemId: null, family: 'EPI' }],
  ...p,
});

const lista = [
  pedido({ number: 'PO-2026-000001', supplierName: 'Alfa EPIs' }),
  pedido({ number: 'PO-2026-000002', supplierName: 'Beta Química', status: 'RECEBIDO', erpNumber: 'OC-77',
    invoices: [{ id: 'n1', number: '4521', issuedOn: '2026-09-01', value: 1500, documentId: null, fileName: null, createdByLabel: 'Ana', createdAt: '' }] }),
];

describe('filtrarPedidos', () => {
  it('filtra por situação e por texto (pedido, OC, fornecedor, SC, NF)', () => {
    expect(filtrarPedidos(lista, '', 'RECEBIDO').map((o) => o.number)).toEqual(['PO-2026-000002']);
    expect(filtrarPedidos(lista, 'alfa', '').map((o) => o.number)).toEqual(['PO-2026-000001']);
    expect(filtrarPedidos(lista, 'oc-77', '').map((o) => o.number)).toEqual(['PO-2026-000002']);
    expect(filtrarPedidos(lista, '4521', '').map((o) => o.number)).toEqual(['PO-2026-000002']);
    expect(filtrarPedidos(lista, 'SC-2026', '')).toHaveLength(2);
    expect(filtrarPedidos(lista, 'nada', '')).toHaveLength(0);
  });
});

describe('<PedidosLista />', () => {
  beforeEach(() => { vi.mocked(listarPedidos).mockResolvedValue(lista); });

  const montar = () => render(
    <MemoryRouter><ToastProvider><PedidosLista /></ToastProvider></MemoryRouter>,
  );

  it('lista os pedidos com situação, recebido/total e NF', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-pedidos')).toBeInTheDocument());
    const tabela = within(screen.getByTestId('tabela-pedidos'));
    expect(tabela.getByText('PO-2026-000001')).toBeInTheDocument();
    expect(tabela.getByText('OC/Faturamento')).toBeInTheDocument();
    expect(tabela.getByText('Pedido entregue')).toBeInTheDocument();
    expect(tabela.getAllByText(/recebido 4 de 10/)).toHaveLength(2);
    expect(tabela.getByText('NF: 4521')).toBeInTheDocument();
    expect(tabela.getByText('a registrar')).toBeInTheDocument();
  });

  it('filtra pela busca digitada', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-pedidos')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText('Buscar'), 'beta');
    expect(screen.queryByText('PO-2026-000001')).not.toBeInTheDocument();
    expect(screen.getByText('PO-2026-000002')).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText('Buscar'));
    await userEvent.type(screen.getByLabelText('Buscar'), 'zzz');
    expect(screen.getByText('Nenhum pedido corresponde ao filtro.')).toBeInTheDocument();
  });

  it('mostra o erro da API sem quebrar a tela', async () => {
    vi.mocked(listarPedidos).mockRejectedValueOnce(new Error('Seu papel não acessa pedidos de compra.'));
    montar();
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Seu papel não acessa pedidos de compra.'));
  });
});
