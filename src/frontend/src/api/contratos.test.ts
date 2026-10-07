import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Fornecedor } from './fornecedores';

vi.mock('./fornecedores', async (importar) => ({
  ...(await importar<typeof import('./fornecedores')>()),
  listarFornecedores: vi.fn(),
}));

import { listarFornecedores } from './fornecedores';
import { listarContratos, motivoDaCompra, type CompraDoContrato } from './contratos';

const CONTRATO_VAZIO = {
  number: null, validFrom: null, validUntil: null, notes: null,
  valueLimit: null, consumed: null, balance: null, current: false,
  items: [] as Fornecedor['contract']['items'],
};

const ITEM = {
  catalogItemId: null, catalogCode: 'EPI-001', description: 'Luva', unitOfMeasure: 'PAR',
  unitPrice: 12, paymentTerms: null, paymentDays: null, deliveryDays: null, notes: null,
};

const fornecedor = (nome: string, p: Partial<Fornecedor> = {}): Fornecedor => ({
  id: 's-' + nome, legalName: nome, tradeName: null, taxId: '00000000000191',
  email: null, phone: null, active: true, homologationStatus: 'HOMOLOGADO',
  effectiveHomologation: 'HOMOLOGADO', documents: [], duplicateCount: 0,
  contract: CONTRATO_VAZIO,
  ...p,
});

const comContrato = (nome: string, c: Partial<Fornecedor['contract']> = {}) =>
  fornecedor(nome, { contract: { ...CONTRATO_VAZIO, items: [ITEM], ...c } });

describe('o acervo que a tela de Contratos lê', () => {
  beforeEach(() => vi.resetAllMocks());

  it('separa quem tem contrato de quem pode receber um', async () => {
    vi.mocked(listarFornecedores).mockResolvedValue([
      comContrato('Alfa'),
      fornecedor('Gama'),
    ]);
    const a = await listarContratos();

    expect(a.linhas.map((l) => l.supplierName)).toEqual(['Alfa']);
    expect(a.semContrato.map((f) => f.legalName)).toEqual(['Gama']);
  });

  it('quem já tem contrato não entra em "novo contrato"', async () => {
    // dois caminhos para a mesma coisa é justamente o que a unificação desfez: quem tem
    // contrato se edita pela linha da tabela, e o formulário de lá nasce preenchido
    vi.mocked(listarFornecedores).mockResolvedValue([comContrato('Alfa')]);
    expect((await listarContratos()).semContrato).toEqual([]);
  });

  it('fornecedor inativo não entra: reativar é decisão do cadastro de fornecedores', async () => {
    vi.mocked(listarFornecedores).mockResolvedValue([fornecedor('Gama', { active: false })]);
    expect((await listarContratos()).semContrato).toEqual([]);
  });

  it('contrato com número mas sem produto nenhum conta como sem contrato', async () => {
    // salvar sem produto encerra o contrato — encerrado, o fornecedor volta a poder receber
    // um novo, e a tabela de contratos não o lista mais
    vi.mocked(listarFornecedores).mockResolvedValue([
      fornecedor('Delta', { contract: { ...CONTRATO_VAZIO, number: 'CT-ANTIGO' } }),
    ]);
    const a = await listarContratos();
    expect(a.linhas).toEqual([]);
    expect(a.semContrato.map((f) => f.legalName)).toEqual(['Delta']);
  });

  it('o vigente vem antes, e depois em ordem alfabética', async () => {
    vi.mocked(listarFornecedores).mockResolvedValue([
      comContrato('Zeta', { current: false }),
      comContrato('Beta', { current: true }),
      comContrato('Alfa', { current: true }),
    ]);
    expect((await listarContratos()).linhas.map((l) => l.supplierName))
      .toEqual(['Alfa', 'Beta', 'Zeta']);
  });

  it('a lista de "novo contrato" sai ordenada pelo nome', async () => {
    vi.mocked(listarFornecedores).mockResolvedValue([fornecedor('Zeta'), fornecedor('Alfa')]);
    expect((await listarContratos()).semContrato.map((f) => f.legalName)).toEqual(['Alfa', 'Zeta']);
  });

  it('é uma consulta só: as duas listas saem do mesmo retrato do cadastro', async () => {
    // duas chamadas ao mesmo endpoint dariam dois retratos, e o fornecedor poderia aparecer
    // nas duas listas ou em nenhuma
    vi.mocked(listarFornecedores).mockResolvedValue([comContrato('Alfa'), fornecedor('Gama')]);
    await listarContratos();
    expect(listarFornecedores).toHaveBeenCalledTimes(1);
  });
});

describe('por que a compra conta ou não no saldo', () => {
  const compra = (p: Partial<CompraDoContrato>): CompraDoContrato => ({
    id: 'p1', number: 'PO-1', erpNumber: null, createdAt: '2026-05-01T12:00:00Z',
    total: 100, status: 'OPEN', cancelled: false, quotationNumber: null,
    sourcePrNumber: null, countsInContract: false, ...p,
  });

  it('a linha diz o motivo, em vez de só marcar', () => {
    expect(motivoDaCompra(compra({ countsInContract: true }), true)).toBe('abate o saldo');
    expect(motivoDaCompra(compra({}), false)).toBe('sem contrato');
    expect(motivoDaCompra(compra({ cancelled: true }), true)).toBe('cancelado — não conta');
    expect(motivoDaCompra(compra({}), true)).toBe('fora da vigência');
  });
});
