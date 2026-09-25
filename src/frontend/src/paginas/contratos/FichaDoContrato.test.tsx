import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import { motivoDaCompra, type CompraDoContrato, type FichaDoContrato as Ficha } from '@/api/contratos';
import type { DocumentoFornecedor, Fornecedor } from '@/api/fornecedores';
import { ToastProvider } from '@/componentes/Toast';
import { documentosEmOrdem, FichaDoContrato } from './FichaDoContrato';

vi.mock('@/api/contratos', async (importar) => ({
  ...(await importar<typeof import('@/api/contratos')>()),
  fichaDoContrato: vi.fn(),
}));
vi.mock('@/api/fornecedores', async (importar) => ({
  ...(await importar<typeof import('@/api/fornecedores')>()),
  anexarDocumento: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import { fichaDoContrato } from '@/api/contratos';
import { anexarDocumento } from '@/api/fornecedores';

let eu: Usuario = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['CONTRATOS'] };

const doc = (d: Partial<DocumentoFornecedor>): DocumentoFornecedor => ({
  id: 'd-' + (d.fileName ?? 'x'), type: 'OUTRO', label: null, documentId: 'sd-1', fileName: 'x.pdf',
  validUntil: null, expired: false, expiringDays: null, uploadedByLabel: 'Marina', ...d,
});

const fornecedor = (f: Partial<Fornecedor> = {}): Fornecedor => ({
  id: 's1', legalName: 'Alfa EPIs Ltda', tradeName: 'Alfa', taxId: '12345678000195', email: 'vendas@alfa.com.br', phone: '11 4000-0000',
  active: true, homologationStatus: 'HOMOLOGADO', effectiveHomologation: 'HOMOLOGADO', duplicateCount: 0,
  documents: [
    doc({ type: 'CNDT', fileName: 'cndt.pdf', validUntil: '2026-01-01', expired: true }),
    doc({ type: 'CONTRATO', fileName: 'contrato-ct10.pdf', validUntil: '2026-01-01', expired: true }),
  ],
  contract: {
    number: 'CT-10', validFrom: '2026-01-01', validUntil: '2026-12-31', notes: 'reajuste anual pelo IPCA',
    valueLimit: 10000, consumed: 2500, balance: 7500, current: true,
    items: [{ id: 'i1', catalogItemId: null, catalogCode: 'EPI-001', description: 'Luva nitrílica', unitOfMeasure: 'PAR', unitPrice: 1.95, paymentTerms: '28 DDL', paymentDays: 28, deliveryDays: 7, notes: null }],
  },
  ...f,
});

const compra = (c: Partial<CompraDoContrato>): CompraDoContrato => ({
  id: 'po1', number: 'PO-2026-000001', erpNumber: '45012873', createdAt: '2026-09-20T10:00:00Z', total: 2500,
  status: 'OC/Faturamento', cancelled: false, quotationNumber: 'RFQ-2026-000001', sourcePrNumber: 'PR-2026-000001',
  countsInContract: true, ...c,
});

const ficha = (f: Partial<Ficha> = {}): Ficha => ({
  supplier: fornecedor(),
  purchaseOrders: [
    compra({}),
    compra({ id: 'po0', number: 'PO-2025-000090', createdAt: '2025-11-01T10:00:00Z', countsInContract: false }),
  ],
  timeline: [
    { at: '2026-09-21T10:00:00Z', kind: 'REAJUSTE', text: 'Reajuste pleiteado de 8%, fechado em 3%.', by: 'Marina' },
    { at: '2026-09-20T10:00:00Z', kind: 'ALTERADO', text: 'Contrato alterado: preço de Luva nitrílica R$ 1,80 → R$ 1,95.', by: 'Marina' },
    { at: '2026-01-01T10:00:00Z', kind: 'CRIADO', text: 'Contrato CT-10 cadastrado.', by: 'Marina' },
  ],
  costAvoidanceTotal: 500,
  historyComplete: true,
  ...f,
});

const abrir = () => render(
  <MemoryRouter initialEntries={['/contratos/s1']}>
    <ToastProvider>
      <Routes><Route path="/contratos/:id" element={<FichaDoContrato />} /></Routes>
    </ToastProvider>
  </MemoryRouter>,
);

describe('regras da ficha do contrato', () => {
  it('o papel do contrato vem antes das certidões', () => {
    const ordem = documentosEmOrdem(fornecedor().documents).map((d) => d.type);
    expect(ordem).toEqual(['CONTRATO', 'CNDT']);
  });
  it('a compra diz por que abate ou não o saldo', () => {
    expect(motivoDaCompra(compra({}), true)).toBe('abate o saldo');
    expect(motivoDaCompra(compra({ countsInContract: false, cancelled: true }), true)).toBe('cancelado — não conta');
    expect(motivoDaCompra(compra({ countsInContract: false }), true)).toBe('fora da vigência');
    expect(motivoDaCompra(compra({ countsInContract: false }), false)).toBe('sem contrato');
  });
});

describe('tela Ficha do Contrato', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['CONTRATOS'] };
  });

  it('mostra o contrato, os documentos, as compras e a história', async () => {
    vi.mocked(fichaDoContrato).mockResolvedValue(ficha());
    abrir();
    expect(await screen.findByText('Alfa EPIs Ltda')).toBeInTheDocument();
    expect(screen.getByText('VIGENTE')).toBeInTheDocument();
    expect(screen.getByText('CT-10')).toBeInTheDocument();

    // contrato assinado "vencido" não é certidão vencida: a marca de VENCIDA é só da CNDT
    const docs = screen.getByTestId('documentos-do-contrato');
    const linhaContrato = docs.querySelector('[data-documento="CONTRATO"]') as HTMLElement;
    expect(within(linhaContrato).queryByText('VENCIDA')).toBeNull();
    expect(within(linhaContrato).getByText('do contrato')).toBeInTheDocument();
    expect(within(docs.querySelector('[data-documento="CNDT"]') as HTMLElement).getByText('VENCIDA')).toBeInTheDocument();

    const compras = screen.getByTestId('compras-do-contrato');
    expect(within(compras).getByText('abate o saldo')).toBeInTheDocument();
    expect(within(compras).getByText('fora da vigência')).toBeInTheDocument();
    expect(within(compras).getByRole('link', { name: 'PO-2026-000001' })).toHaveAttribute('href', '/pedidos/po1');

    const linha = screen.getByTestId('linha-do-tempo-contrato');
    expect(within(linha).getAllByRole('listitem').map((li) => li.getAttribute('data-tipo'))).toEqual(['REAJUSTE', 'ALTERADO', 'CRIADO']);
    expect(within(linha).getByText(/R\$ 1,80 → R\$ 1,95/)).toBeInTheDocument();
    expect(screen.queryByTestId('historico-incompleto')).toBeNull();
  });

  it('contrato de antes do registro avisa que a história está incompleta', async () => {
    vi.mocked(fichaDoContrato).mockResolvedValue(ficha({ historyComplete: false, timeline: [] }));
    abrir();
    expect(await screen.findByTestId('historico-incompleto')).toHaveTextContent('antes de o sistema registrar');
  });

  it('anexa o contrato assinado sem validade — a dele é a vigência', async () => {
    vi.mocked(fichaDoContrato).mockResolvedValue(ficha());
    vi.mocked(anexarDocumento).mockResolvedValue(undefined);
    abrir();
    await userEvent.click(await screen.findByRole('button', { name: 'Anexar documento do contrato' }));
    const arquivo = new File(['%PDF-1.4'], 'aditivo-1.pdf', { type: 'application/pdf' });
    await userEvent.selectOptions(screen.getByLabelText('Documento'), 'ADITIVO');
    await userEvent.upload(screen.getByLabelText('Arquivo'), arquivo);
    await userEvent.type(screen.getByLabelText(/Descrição/), '1º aditivo');
    await userEvent.click(screen.getByRole('button', { name: 'Anexar' }));
    await waitFor(() => expect(anexarDocumento).toHaveBeenCalledWith('s1', arquivo, 'ADITIVO', '', '1º aditivo'));
    expect(fichaDoContrato).toHaveBeenCalledTimes(2);
  });

  it('quem não mantém contratos só consulta', async () => {
    eu = { id: 'u2', email: 'rob@t.com', name: 'Roberto', role: 'Director', modules: ['CONTRATOS'] };
    vi.mocked(fichaDoContrato).mockResolvedValue(ficha());
    abrir();
    await screen.findByText('Alfa EPIs Ltda');
    expect(screen.queryByRole('button', { name: 'Anexar documento do contrato' })).toBeNull();
  });

  it('fornecedor sem contrato diz isso em vez de mostrar vigência vazia', async () => {
    vi.mocked(fichaDoContrato).mockResolvedValue(ficha({
      supplier: fornecedor({ contract: { number: null, validFrom: null, validUntil: null, notes: null, valueLimit: null, consumed: null, balance: null, current: false, items: [] } }),
      purchaseOrders: [compra({ countsInContract: false })], timeline: [], historyComplete: false,
    }));
    abrir();
    // no cabeçalho e na compra, que por isso não abate saldo nenhum
    expect(await screen.findAllByText('sem contrato')).toHaveLength(2);
    expect(screen.getByText(/Nenhum produto contratado/)).toBeInTheDocument();
    // sem contrato não há história incompleta para avisar
    expect(screen.queryByTestId('historico-incompleto')).toBeNull();
  });
});
