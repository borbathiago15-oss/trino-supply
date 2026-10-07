import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Produto } from '@/api/catalogo';
import type { Fornecedor } from '@/api/fornecedores';
import { ToastProvider } from '@/componentes/Toast';
import { daLinha, itensDoFormulario, novaLinha, PainelContrato } from './PainelContrato';

vi.mock('@/api/catalogo', async (importar) => ({
  ...(await importar<typeof import('@/api/catalogo')>()),
  buscarProdutos: vi.fn(),
}));
vi.mock('@/api/familias', () => ({ listarFamilias: vi.fn() }));
vi.mock('@/api/fornecedores', async (importar) => ({
  ...(await importar<typeof import('@/api/fornecedores')>()),
  salvarContrato: vi.fn(),
}));

import { buscarProdutos } from '@/api/catalogo';
import { listarFamilias } from '@/api/familias';
import { salvarContrato } from '@/api/fornecedores';

const produto = (p: Partial<Produto>): Produto => ({
  id: 'p1', code: '02090081', description: 'BALDE 20 LITROS', family: 'MATERIAL DE LIMPEZA',
  unitOfMeasure: 'UN', referencePrice: 19.9, active: true, stockControlled: true,
  purchasable: true, minimumQty: null, productType: null, productTypeLabel: null,
  baseCode: null, size: null, imageDocumentId: null, imageFileName: null,
  compliancePending: false, materialRequestable: 'FAMILIA',
  ...p,
} as Produto);

const CONTRATO_VAZIO: Fornecedor['contract'] = {
  number: null, validFrom: null, validUntil: null, notes: null,
  valueLimit: null, consumed: null, balance: null, current: false, items: [],
};

const fornecedor = (c: Partial<Fornecedor['contract']> = {}): Fornecedor => ({
  id: 's1', legalName: 'Alfa EPIs LTDA', tradeName: null, taxId: '00000000000191',
  email: null, phone: null, active: true, homologationStatus: 'HOMOLOGADO',
  effectiveHomologation: 'HOMOLOGADO', documents: [], duplicateCount: 0,
  contract: { ...CONTRATO_VAZIO, ...c },
});

const abrir = (f = fornecedor()) => render(
  <ToastProvider>
    <PainelContrato fornecedor={f} aoSalvar={() => {}} aoFechar={() => {}} />
  </ToastProvider>,
);

describe('as linhas do contrato viram itens da API', () => {
  it('a linha carrega o produto escolhido, sem consultar cópia do catálogo', () => {
    // era a cópia do catálogo inteiro na tela que obrigava a baixar milhares de itens
    const l = { ...novaLinha(), catalogItemId: 'p1', catalogCode: '02090081',
      descricao: 'BALDE 20 LITROS', unidade: 'UN', preco: '19.90', diasEntrega: '7' };
    expect(itensDoFormulario([l])).toEqual([{
      catalogItemId: 'p1', description: 'BALDE 20 LITROS', catalogCode: '02090081',
      unitOfMeasure: 'UN', unitPrice: 19.9, paymentTerms: null, paymentDays: null,
      deliveryDays: 7, notes: null,
    }]);
  });

  it('linha sem produto nenhum é descartada', () => {
    // é o que permite "+ Adicionar produto" deixar uma linha em branco sem sujar o contrato
    expect(itensDoFormulario([novaLinha()])).toEqual([]);
  });

  it('o produto fora do catálogo, vindo de contrato antigo, continua valendo', () => {
    // ele não tem id: sumir com ele apagaria uma linha que o fornecedor já entrega
    const antigo = daLinha({
      catalogItemId: null, catalogCode: null, description: 'FRETE ESPECIAL',
      unitOfMeasure: null, unitPrice: 100, paymentTerms: null, paymentDays: null,
      deliveryDays: null, notes: null,
    });
    expect(itensDoFormulario([antigo])).toHaveLength(1);
    expect(itensDoFormulario([antigo])[0].description).toBe('FRETE ESPECIAL');
  });

  it('o item salvo volta ao formulário com código, descrição e unidade', () => {
    const l = daLinha({
      catalogItemId: 'p1', catalogCode: '02090081', description: 'BALDE 20 LITROS',
      unitOfMeasure: 'UN', unitPrice: 19.9, paymentTerms: '28 DDL', paymentDays: 28,
      deliveryDays: 7, notes: 'obs',
    });
    expect(l).toMatchObject({
      catalogItemId: 'p1', catalogCode: '02090081', descricao: 'BALDE 20 LITROS',
      unidade: 'UN', preco: '19.9', condicao: '28 DDL', diasPagamento: '28', diasEntrega: '7',
    });
  });
});

describe('o produto do contrato se escolhe buscando', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(listarFamilias).mockResolvedValue(
      [{ name: 'MATERIAL DE LIMPEZA' }, { name: 'EPI' }] as never);
    vi.mocked(buscarProdutos).mockResolvedValue([produto({})]);
  });

  it('a linha abre a busca, em vez de uma lista com o catálogo inteiro', async () => {
    const usuario = userEvent.setup();
    abrir();
    // o select com milhares de itens não existe mais
    expect(screen.queryByRole('combobox', { name: 'Produto do contrato' })).not.toBeInTheDocument();

    await usuario.click(screen.getByRole('button', { name: 'Buscar produto do contrato' }));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  it('não consulta nada antes de família ou duas letras — a mesma régua da SC', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.click(screen.getByRole('button', { name: 'Buscar produto do contrato' }));

    expect(await screen.findByText(/digite ao menos duas letras/)).toBeInTheDocument();
    expect(buscarProdutos).not.toHaveBeenCalled();

    await usuario.type(screen.getByRole('textbox', { name: /Buscar produto/ }), 'b');
    await new Promise((r) => setTimeout(r, 400));
    expect(buscarProdutos).not.toHaveBeenCalled();

    await usuario.type(screen.getByRole('textbox', { name: /Buscar produto/ }), 'alde');
    await vi.waitFor(() => expect(buscarProdutos).toHaveBeenCalledWith(
      { q: 'balde', familia: '' }, expect.anything()));
  });

  it('a família sozinha já busca, sem precisar de termo', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.click(screen.getByRole('button', { name: 'Buscar produto do contrato' }));
    await usuario.selectOptions(screen.getByLabelText('Família'), 'EPI');

    await vi.waitFor(() => expect(buscarProdutos).toHaveBeenCalledWith(
      { q: '', familia: 'EPI' }, expect.anything()));
  });

  it('escolher traz o produto para a linha, com código e unidade', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.click(screen.getByRole('button', { name: 'Buscar produto do contrato' }));
    await usuario.type(screen.getByRole('textbox', { name: /Buscar produto/ }), 'balde');

    const lista = await screen.findByTestId('produtos-do-contrato');
    await usuario.click(within(lista).getByText('BALDE 20 LITROS'));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    const linha = screen.getByText('BALDE 20 LITROS').closest('span')!;
    expect(linha).toHaveTextContent('[02090081]');
    expect(linha).toHaveTextContent('UN');
  });

  it('o produto escolhido vai para a gravação como código, descrição e unidade', async () => {
    const usuario = userEvent.setup();
    vi.mocked(salvarContrato).mockResolvedValue(undefined as never);
    abrir();
    await usuario.click(screen.getByRole('button', { name: 'Buscar produto do contrato' }));
    await usuario.type(screen.getByRole('textbox', { name: /Buscar produto/ }), 'balde');
    await usuario.click(within(await screen.findByTestId('produtos-do-contrato')).getByText('BALDE 20 LITROS'));

    await usuario.type(screen.getByRole('spinbutton', { name: 'Preço fixo' }), '19.90');
    await usuario.click(screen.getByRole('button', { name: 'Salvar contrato' }));

    await vi.waitFor(() => expect(salvarContrato).toHaveBeenCalledWith('s1', expect.objectContaining({
      items: [expect.objectContaining({
        catalogItemId: 'p1', catalogCode: '02090081', description: 'BALDE 20 LITROS',
        unitOfMeasure: 'UN', unitPrice: 19.9,
      })],
    })));
  });

  it('"Trocar" reabre a busca para quem já escolheu', async () => {
    const usuario = userEvent.setup();
    abrir(fornecedor({
      items: [{
        catalogItemId: 'p1', catalogCode: '02090081', description: 'BALDE 20 LITROS',
        unitOfMeasure: 'UN', unitPrice: 19.9, paymentTerms: null, paymentDays: null,
        deliveryDays: null, notes: null,
      }],
    }));
    await usuario.click(screen.getByRole('button', { name: 'Trocar' }));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  it('o produto fora do catálogo continua só de leitura, sem botão de busca', async () => {
    // ele veio de um contrato antigo e não tem id: oferecer "trocar" apagaria a linha
    abrir(fornecedor({
      items: [{
        catalogItemId: null, catalogCode: null, description: 'FRETE ESPECIAL',
        unitOfMeasure: null, unitPrice: 100, paymentTerms: null, paymentDays: null,
        deliveryDays: null, notes: null,
      }],
    }));
    expect(screen.getByDisplayValue('FRETE ESPECIAL')).toHaveAttribute('readonly');
    expect(screen.queryByRole('button', { name: 'Trocar' })).not.toBeInTheDocument();
  });
});
