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
  salvarContrato: vi.fn(), anexarDocumento: vi.fn(),
}));
vi.mock('@/api/documentos', () => ({ baixarDocumento: vi.fn() }));

import { buscarProdutos } from '@/api/catalogo';
import { listarFamilias } from '@/api/familias';
import { anexarDocumento, salvarContrato } from '@/api/fornecedores';

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

const papel = (p: Partial<Fornecedor['documents'][number]> = {}) => ({
  id: 'd1', type: 'CONTRATO' as const, fileName: 'contrato.pdf', documentId: 'doc1',
  label: null, validUntil: null, uploadedByLabel: 'Ana', uploadedAt: '2026-10-01T12:00:00Z',
  ...p,
} as Fornecedor['documents'][number]);

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


describe('os papéis do contrato se anexam no próprio formulário', () => {
  // decisão da empresa (2026-10): o contrato assinado se anexa onde o contrato é fechado — é
  // quando o comprador tem o PDF na mão. O formulário é o **mesmo** da ficha, para as duas
  // telas não gravarem de jeitos diferentes
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(listarFamilias).mockResolvedValue([] as never);
    vi.mocked(anexarDocumento).mockResolvedValue(undefined as never);
  });

  it('o formulário oferece anexar, mesmo num contrato que ainda não foi salvo', async () => {
    // o documento é do fornecedor no modelo, e o fornecedor já está escolhido
    const usuario = userEvent.setup();
    abrir();
    expect(screen.getByText(/Nenhum papel do contrato anexado/)).toBeInTheDocument();

    await usuario.click(screen.getByRole('button', { name: 'Anexar documento do contrato' }));
    expect(screen.getByTestId('anexar-documento-contrato')).toBeInTheDocument();
  });

  it('anexa sem validade: a do papel do contrato é a vigência', async () => {
    const usuario = userEvent.setup();
    const aoSalvar = vi.fn();
    render(
      <ToastProvider>
        <PainelContrato fornecedor={fornecedor()} aoSalvar={aoSalvar} aoFechar={() => {}} />
      </ToastProvider>,
    );
    await usuario.click(screen.getByRole('button', { name: 'Anexar documento do contrato' }));

    const arquivo = new File(['%PDF-1.4'], 'contrato.pdf', { type: 'application/pdf' });
    await usuario.upload(screen.getByLabelText('Arquivo'), arquivo);
    await usuario.type(screen.getByLabelText(/Descrição/), 'renovação 2027');
    await usuario.click(screen.getByRole('button', { name: 'Anexar' }));

    await vi.waitFor(() => expect(anexarDocumento).toHaveBeenCalledWith(
      's1', arquivo, 'CONTRATO', '', 'renovação 2027'));
    // a lista do contrato recarrega, senão o papel recém-anexado não apareceria
    await vi.waitFor(() => expect(aoSalvar).toHaveBeenCalled());
  });

  it('sem arquivo não chama a API', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.click(screen.getByRole('button', { name: 'Anexar documento do contrato' }));
    await usuario.click(screen.getByRole('button', { name: 'Anexar' }));

    expect(await screen.findByText('Escolha o arquivo.')).toBeInTheDocument();
    expect(anexarDocumento).not.toHaveBeenCalled();
  });

  it('lista os papéis já anexados, com o nome do arquivo', async () => {
    abrir({ ...fornecedor(), documents: [papel({}), papel({ id: 'd2', type: 'ADITIVO', fileName: 'aditivo-1.pdf', label: '1º aditivo' })] });
    const lista = await screen.findByTestId('papeis-do-contrato');
    expect(lista).toHaveTextContent('contrato.pdf');
    expect(lista).toHaveTextContent('aditivo-1.pdf');
    expect(lista).toHaveTextContent('1º aditivo');
  });

  it('a certidão do fornecedor não entra: ela é da homologação, não do contrato', async () => {
    // misturá-las faria a lista do contrato parecer ter documentos que não são dele
    abrir({ ...fornecedor(), documents: [papel({}), papel({ id: 'd3', type: 'FGTS', fileName: 'fgts.pdf' })] });
    const lista = await screen.findByTestId('papeis-do-contrato');
    expect(lista).toHaveTextContent('contrato.pdf');
    expect(lista).not.toHaveTextContent('fgts.pdf');
  });
});
