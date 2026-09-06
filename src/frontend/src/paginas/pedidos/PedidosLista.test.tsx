import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PedidoCompra } from '@/api/pedidos';
import { ToastProvider } from '@/componentes/Toast';
import { PedidosLista } from './PedidosLista';

vi.mock('@/api/pedidos', async (importar) => ({
  ...(await importar<typeof import('@/api/pedidos')>()),
  listarPedidos: vi.fn(),
}));
import { listarPedidos } from '@/api/pedidos';

const pedido = (p: Partial<PedidoCompra>): PedidoCompra => ({
  id: 'id-' + p.number, number: 'PO-1', status: 'EMITIDO', supplierId: 's', supplierName: 'Fornecedor A',
  sourcePrNumber: 'SC-2026-000001', quotationNumber: null, paymentTerms: null, deliveryDays: null, freightValue: null,
  families: ['EPI'], notes: null, totalValue: 1500, issuedByLabel: null, receivedByLabel: null, receivedAt: null,
  cancelReason: null, createdAt: '2026-09-01T10:00:00Z', erpNumber: null, noErpReason: null, erpIssuedOn: null, promisedDate: null,
  onTime: null, inFull: null, otif: null, referenceSavingTotal: null, erpDocumentId: null, erpFileName: null,
  deliveryCompletedAt: null, pendingDelivery: true, inactiveCatalogCodes: null, invoices: [],
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

const pagina = (itens: PedidoCompra[], total = itens.length) => ({ itens, total });

describe('<PedidosLista />', () => {
  beforeEach(() => { vi.mocked(listarPedidos).mockResolvedValue(pagina(lista)); });

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

  it('a busca vai para o servidor, e não filtra a lista no navegador', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-pedidos')).toBeInTheDocument());

    vi.mocked(listarPedidos).mockResolvedValue(pagina([lista[1]]));
    await userEvent.type(screen.getByLabelText('Buscar'), 'beta');

    await waitFor(() => expect(listarPedidos).toHaveBeenCalledWith(
      expect.objectContaining({ busca: 'beta' }), expect.anything()));
    await waitFor(() => expect(screen.queryByText('PO-2026-000001')).not.toBeInTheDocument());
    expect(screen.getByText('PO-2026-000002')).toBeInTheDocument();
  });

  it('a situação também é filtrada no servidor', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-pedidos')).toBeInTheDocument());

    await userEvent.selectOptions(screen.getByLabelText('Situação'), 'RECEBIDO');
    await waitFor(() => expect(listarPedidos).toHaveBeenCalledWith(
      expect.objectContaining({ situacao: 'RECEBIDO' }), expect.anything()));
  });

  it('diz quantos existem, e não só quantos vieram', async () => {
    vi.mocked(listarPedidos).mockResolvedValue(pagina(lista, 312));
    montar();
    await waitFor(() => expect(screen.getByTestId('contagem-pedidos')).toHaveTextContent('Mostrando 2 de 312'));

    // e oferece o resto, em vez de fingir que a lista acabou
    await userEvent.click(screen.getByRole('button', { name: 'Carregar mais' }));
    await waitFor(() => expect(listarPedidos).toHaveBeenCalledWith(
      expect.objectContaining({ tamanho: 100 }), expect.anything()));
  });

  it('sem busca, a lista vazia explica que ainda não há pedidos', async () => {
    vi.mocked(listarPedidos).mockResolvedValue(pagina([]));
    montar();
    await waitFor(() => expect(screen.getByText(/Nenhum pedido de compra ainda/)).toBeInTheDocument());
  });

  it('mostra o erro da API sem quebrar a tela', async () => {
    vi.mocked(listarPedidos).mockRejectedValueOnce(new Error('Seu papel não acessa pedidos de compra.'));
    montar();
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Seu papel não acessa pedidos de compra.'));
  });
});
