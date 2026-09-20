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
  erpFileName: null, deliveryCompletedAt: null, pendingDelivery: true, inactiveCatalogCodes: null, invoices: [],
  erpPending: true, erpDocuments: [],
  items: [{
    itemId: 'i1', description: 'Luva nitrílica', unitOfMeasure: 'PAR', quantity: 10, receivedQuantity: 0,
    erpCoveredQuantity: 0, erpPendingQuantity: 10,
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
    vi.mocked(registrarOc).mockResolvedValue(pedido({ erpNumber: '663', erpPending: false }));
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
    vi.mocked(registrarOc).mockResolvedValue(pedido({ noErpReason: 'Compra emergencial de balcão.', erpPending: false }));
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
      erpIssuedOn: '2026-09-01', erpPending: false,
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

describe('O.C. parcial: várias O.C.s do ERP no mesmo pedido', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'] };
    vi.mocked(listarLocais).mockResolvedValue([]);
  });

  const parcial = () => pedido({
    erpNumber: 'OC-100', erpIssuedOn: '2026-09-01', erpPending: true,
    erpDocuments: [{ id: 'd1', number: 'OC-100', issuedOn: '2026-09-01', documentId: null, fileName: null, notes: null,
      createdByLabel: 'Carla', createdAt: '2026-09-01T10:00:00Z', items: [{ itemId: 'i1', quantity: 6 }] }],
    items: [{ ...pedido({}).items[0], erpCoveredQuantity: 6, erpPendingQuantity: 4 }],
  });

  it('mostra o que cada O.C. cobre e quanto ainda falta', async () => {
    vi.mocked(obterPedido).mockResolvedValue(parcial());
    abrir();

    const painel = (await screen.findByTestId('ocs-do-erp')).closest('#oc-erp') as HTMLElement;
    expect(within(painel).getByText('OC-100')).toBeInTheDocument();
    expect(painel).toHaveTextContent('cobre 6 PAR de Luva nitrílica');
    expect(screen.getByTestId('saldo-sem-oc')).toHaveTextContent('4 de 10 unidades ainda sem O.C.');
    // a linha do tempo diz que a O.C. começou e não fechou
    const etapaOc = screen.getByTestId('linha-do-tempo').querySelector('[data-etapa="oc"]');
    expect(etapaOc).toHaveAttribute('data-situacao', 'parcial');
    // o campo do número é da próxima O.C., não da que já existe
    expect(within(painel).getByLabelText('Número da OC no ERP')).toHaveValue('');
  });

  it('a quantidade digitada vai na cobertura, e o que sobra continua em aberto', async () => {
    const usuario = userEvent.setup();
    vi.mocked(obterPedido).mockResolvedValue(parcial());
    vi.mocked(registrarOc).mockResolvedValue(parcial());
    abrir();

    const painel = (await screen.findByTestId('ocs-do-erp')).closest('#oc-erp') as HTMLElement;
    await usuario.type(within(painel).getByLabelText('Número da OC no ERP'), 'OC-101');
    const cobertura = within(painel).getByTestId('cobertura-da-oc');
    expect(cobertura).toHaveTextContent('Deixe como está para a O.C. cobrir tudo o que ainda falta');
    await usuario.type(within(cobertura).getByLabelText('Nesta O.C.: Luva nitrílica'), '3');
    expect(cobertura).toHaveTextContent('O.C. parcial');
    await usuario.click(within(painel).getByRole('button', { name: 'Registrar OC' }));

    await waitFor(() => expect(registrarOc).toHaveBeenCalledWith('po1', {
      erpNumber: 'OC-101', issuedOn: null, noErpReason: null, items: [{ itemId: 'i1', quantity: 3 }],
    }));
  });

  it('cobrir mais do que falta trava o botão antes de o servidor recusar (PO-ERR-059)', async () => {
    const usuario = userEvent.setup();
    vi.mocked(obterPedido).mockResolvedValue(parcial());
    abrir();

    const painel = (await screen.findByTestId('ocs-do-erp')).closest('#oc-erp') as HTMLElement;
    await usuario.type(within(painel).getByLabelText('Número da OC no ERP'), 'OC-101');
    await usuario.type(within(painel).getByLabelText('Nesta O.C.: Luva nitrílica'), '5');
    expect(painel).toHaveTextContent('PO-ERR-059');
    expect(within(painel).getByRole('button', { name: 'Registrar OC' })).toBeDisabled();
  });

  it('o restante fecha sem O.C. com a observação, e as O.C.s registradas ficam', async () => {
    const usuario = userEvent.setup();
    vi.mocked(obterPedido).mockResolvedValue(parcial());
    vi.mocked(registrarOc).mockResolvedValue(parcial());
    abrir();

    const botao = await screen.findByRole('button', { name: 'Fechar o restante sem O.C.' });
    expect(botao).toBeDisabled();
    await usuario.type(screen.getByLabelText(/por que a O.C. não foi gerada/i), 'As 4 restantes vieram de balcão.');
    await usuario.click(botao);
    await waitFor(() => expect(registrarOc).toHaveBeenCalledWith('po1', {
      erpNumber: '', issuedOn: null, noErpReason: 'As 4 restantes vieram de balcão.',
    }));
  });

  it('com o pedido todo coberto não há mais O.C. a registrar', async () => {
    vi.mocked(obterPedido).mockResolvedValue(pedido({
      erpNumber: 'OC-100', erpIssuedOn: '2026-09-01', erpPending: false,
      erpDocuments: [{ id: 'd1', number: 'OC-100', issuedOn: '2026-09-01', documentId: null, fileName: null, notes: null,
        createdByLabel: 'Carla', createdAt: '2026-09-01T10:00:00Z', items: [{ itemId: 'i1', quantity: 10 }] }],
      items: [{ ...pedido({}).items[0], erpCoveredQuantity: 10, erpPendingQuantity: 0 }],
    }));
    abrir();

    const painel = (await screen.findByTestId('ocs-do-erp')).closest('#oc-erp') as HTMLElement;
    expect(painel).toHaveTextContent('OC OC-100 de 01/09/2026');
    expect(painel).toHaveTextContent('cobre o pedido inteiro');
    expect(screen.queryByRole('button', { name: 'Registrar OC' })).not.toBeInTheDocument();
    expect(painel).toHaveTextContent('O próximo passo é o faturamento');
    const linha = screen.getByTestId('linha-do-tempo');
    expect(linha.querySelector('[data-etapa="oc"]')).toHaveAttribute('data-situacao', 'feita');
    expect(linha.querySelector('[data-etapa="faturamento"]')).toHaveAttribute('data-situacao', 'atual');
    expect(linha.querySelector('[data-etapa="entrega"]')).toHaveAttribute('data-situacao', 'pendente');
  });
});

describe('recebimento de item inativado no catálogo (IV-ERR-010)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    eu = {
      id: 'u2', email: 'ana@t.com', name: 'Ana', role: 'WarehouseOperator',
      modules: ['COMPRAS', 'ESTOQUE'],
    };
    vi.mocked(listarLocais).mockResolvedValue([{ id: 'l1', code: 'ALM-01', name: 'Almoxarifado' }]);
  });

  it('o item inativo não recebe entrada, mas a devolução dele continua aberta', async () => {
    // antes o almoxarife digitava a quantidade de tudo e o servidor recusava a
    // entrega inteira; agora a linha diz o que houve, e o resto do pedido segue
    vi.mocked(obterPedido).mockResolvedValue(pedido({
      status: 'FATURADO',
      inactiveCatalogCodes: ['EPI-001'],
      items: [
        pedido({}).items[0],
        { ...pedido({}).items[0], itemId: 'i2', description: 'Bota', catalogCode: 'EPI-002' },
      ],
    }));
    abrir();

    const tabela = await screen.findByTestId('tabela-entrega');
    expect(within(tabela).getByText(/Inativo no catálogo/)).toHaveTextContent('IV-ERR-010');
    expect(within(tabela).getByLabelText('Chegou agora: Luva nitrílica')).toBeDisabled();
    // devolver ao fornecedor não dá entrada em estoque: segue permitido
    expect(within(tabela).getByLabelText('Devolvido agora: Luva nitrílica')).toBeEnabled();
    // o outro item do mesmo pedido não é afetado
    expect(within(tabela).getByLabelText('Chegou agora: Bota')).toBeEnabled();
  });

  it('sem item inativo, nada é bloqueado', async () => {
    vi.mocked(obterPedido).mockResolvedValue(pedido({ status: 'FATURADO', inactiveCatalogCodes: [] }));
    abrir();

    const tabela = await screen.findByTestId('tabela-entrega');
    expect(within(tabela).queryByText(/Inativo no catálogo/)).not.toBeInTheDocument();
    expect(within(tabela).getByLabelText('Chegou agora: Luva nitrílica')).toBeEnabled();
  });
});
