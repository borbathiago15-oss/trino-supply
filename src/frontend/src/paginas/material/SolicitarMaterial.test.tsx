import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Produto } from '@/api/catalogo';
import type { CentroCusto } from '@/api/centrosCusto';
import { ToastProvider } from '@/componentes/Toast';
import { casDoProduto, itensEscolhidos, paraEscolha, semCaObrigatorio, SolicitarMaterial, valorEstimado } from './SolicitarMaterial';

vi.mock('@/api/catalogo', () => ({ buscarProdutos: vi.fn(), fichaDoProduto: vi.fn() }));
vi.mock('@/api/documentos', () => ({ urlDocumento: vi.fn() }));
vi.mock('@/api/familias', () => ({ listarFamilias: vi.fn() }));
vi.mock('@/api/centrosCusto', () => ({ listarCentrosCusto: vi.fn() }));
vi.mock('@/api/material', async (importar) => ({
  ...(await importar<typeof import('@/api/material')>()), criarSolicitacaoMaterial: vi.fn(),
}));

import { buscarProdutos, fichaDoProduto } from '@/api/catalogo';
import { urlDocumento } from '@/api/documentos';
import { listarFamilias } from '@/api/familias';
import { listarCentrosCusto } from '@/api/centrosCusto';
import { criarSolicitacaoMaterial } from '@/api/material';

const cc = { id: 'cc1', code: 'BAH-001', name: 'Obra Bahia' } as CentroCusto;

const produto = (p: Partial<Produto>): Produto => ({
  id: 'p1', code: 'EPI-001', description: 'Luva nitrílica', family: 'EPI', unitOfMeasure: 'PAR',
  referencePrice: null, active: true, stockControlled: true, purchasable: true, minimumQty: null,
  productType: null, productTypeLabel: null, baseCode: null, size: null, imageDocumentId: null,
  imageFileName: null, compliancePending: false, materialRequestable: 'FAMILIA', suppliers: [],
  ...p,
});

const abrir = () => render(
  <MemoryRouter><ToastProvider><SolicitarMaterial /></ToastProvider></MemoryRouter>,
);

describe('o valor estimado do pedido', () => {
  it('é custo × quantidade dos marcados, nulo sem custo, e o marcado sem custo é contado à parte', () => {
    const lista = [produto({ id: 'a', referencePrice: 12.5 }), produto({ id: 'b', referencePrice: null }), produto({ id: 'c', referencePrice: 3 })];
    expect(valorEstimado({ a: { marcado: true, quantidade: '2' }, c: { marcado: false, quantidade: '9' } }, lista))
      .toEqual({ valor: 25, semCusto: 0 });
    // a vírgula do teclado brasileiro conta, e o sem custo fica fora da soma mas não da contagem
    expect(valorEstimado({ a: { marcado: true, quantidade: '1,5' }, b: { marcado: true, quantidade: '4' } }, lista))
      .toEqual({ valor: 18.75, semCusto: 1 });
    expect(valorEstimado({ b: { marcado: true, quantidade: '4' } }, lista)).toEqual({ valor: null, semCusto: 1 });
    expect(valorEstimado({}, lista)).toEqual({ valor: null, semCusto: 0 });
  });
});

describe('regras da grade de material', () => {
  it('marcar sem quantidade não passa, e nada marcado também não', () => {
    expect(itensEscolhidos({}).erro).toBe('Marque ao menos um produto da lista.');
    expect(itensEscolhidos({ p1: { marcado: true, quantidade: '' } }).erro)
      .toBe('Informe a quantidade dos itens marcados.');
    expect(itensEscolhidos({ p1: { marcado: true, quantidade: '0' } }).erro)
      .toBe('Informe a quantidade dos itens marcados.');
  });
  it('a quantidade aceita vírgula, e o item não marcado fica de fora', () => {
    const { items, erro } = itensEscolhidos({
      p1: { marcado: true, quantidade: '2,5' },
      p2: { marcado: false, quantidade: '9' },
    });
    expect(erro).toBeNull();
    expect(items).toEqual([{ catalogItemId: 'p1', quantity: 2.5 }]);
  });
  it('o C.A. sai de cada fornecedor que tiver um', () => {
    expect(casDoProduto(produto({
      suppliers: [
        { supplierId: 's1', supplierName: 'Alfa', taxId: null, contact: null, supplierItemCode: null, lastPrice: null, caNumber: '12345', notes: null },
        { supplierId: 's2', supplierName: 'Beta', taxId: null, contact: null, supplierItemCode: null, lastPrice: null, caNumber: null, notes: null },
      ],
    }))).toBe('12345 (Alfa)');
  });
});

describe('tela Solicitar Material', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(listarCentrosCusto).mockResolvedValue([cc]);
    vi.mocked(listarFamilias).mockResolvedValue([{ name: 'EPI' }, { name: 'LIMPEZA' }] as never);
    vi.mocked(buscarProdutos).mockResolvedValue([produto({})]);
  });

  it('EPI sem C.A. não pode ser marcado e a tela diz o porquê (IC-ERR-023)', async () => {
    const usuario = userEvent.setup();
    const luva = produto({});
    const bota = produto({
      id: 'p2', code: 'EPI-002', description: 'Bota de segurança',
      productType: 'EPI', productTypeLabel: 'EPI', compliancePending: true,
    });
    expect(semCaObrigatorio(bota)).toBe(true);
    expect(semCaObrigatorio(luva)).toBe(false);
    vi.mocked(buscarProdutos).mockResolvedValue([luva, bota]);
    abrir();

    await usuario.selectOptions(await screen.findByLabelText('Família de produtos'), 'EPI');
    const grade = await screen.findByTestId('grade-produtos');

    expect(within(grade).getByLabelText('Selecionar Bota de segurança')).toBeDisabled();
    expect(within(grade).getByLabelText('Quantidade de Bota de segurança')).toBeDisabled();
    expect(screen.getByTestId('epi-sem-ca')).toHaveTextContent('IC-ERR-023');
    // o item regular da mesma família continua disponível
    expect(within(grade).getByLabelText('Selecionar Luva nitrílica')).toBeEnabled();
  });

  it('família sem pendência de C.A. não mostra o aviso', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.selectOptions(await screen.findByLabelText('Família de produtos'), 'EPI');
    await screen.findByTestId('grade-produtos');
    expect(screen.queryByTestId('epi-sem-ca')).not.toBeInTheDocument();
  });

  it('a grade só carrega depois de escolher a família', async () => {
    const usuario = userEvent.setup();
    abrir();
    await screen.findByLabelText('Família de produtos');
    expect(buscarProdutos).not.toHaveBeenCalled();

    await usuario.selectOptions(screen.getByLabelText('Família de produtos'), 'EPI');
    await screen.findByTestId('grade-produtos');
    // a tela pede o recorte do almoxarifado, não o catálogo inteiro: era por mostrar tudo
    // que ela trazia oitocentos produtos de toda família
    expect(buscarProdutos).toHaveBeenCalledWith({ familia: 'EPI', material: true }, expect.anything());
  });

  it('envia o centro de custo, as observações e só os itens marcados com quantidade', async () => {
    const usuario = userEvent.setup();
    vi.mocked(buscarProdutos).mockResolvedValue([
      produto({}), produto({ id: 'p2', code: 'EPI-002', description: 'Bota de segurança' }),
    ]);
    vi.mocked(criarSolicitacaoMaterial).mockResolvedValue({} as never);
    abrir();

    await usuario.selectOptions(await screen.findByLabelText('Centro de custo'), 'BAH-001');
    await usuario.type(screen.getByLabelText('Observações'), 'para a obra');
    await usuario.selectOptions(screen.getByLabelText('Família de produtos'), 'EPI');

    const grade = await screen.findByTestId('grade-produtos');
    await usuario.click(within(grade).getByLabelText('Selecionar Luva nitrílica'));
    await usuario.type(within(grade).getByLabelText('Quantidade de Luva nitrílica'), '10');
    await usuario.click(screen.getByRole('button', { name: 'Enviar ao almoxarifado' }));

    await waitFor(() => expect(criarSolicitacaoMaterial).toHaveBeenCalledWith({
      costCenter: 'BAH-001', notes: 'para a obra', items: [{ catalogItemId: 'p1', quantity: 10 }],
    }));
  });

  it('marcar sem quantidade avisa e não chama a API', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.selectOptions(await screen.findByLabelText('Centro de custo'), 'BAH-001');
    await usuario.selectOptions(screen.getByLabelText('Família de produtos'), 'EPI');

    const grade = await screen.findByTestId('grade-produtos');
    await usuario.click(within(grade).getByLabelText('Selecionar Luva nitrílica'));
    await usuario.click(screen.getByRole('button', { name: 'Enviar ao almoxarifado' }));

    expect(await screen.findByText('Informe a quantidade dos itens marcados.')).toBeInTheDocument();
    expect(criarSolicitacaoMaterial).not.toHaveBeenCalled();
  });

  it('a lista de famílias é a do almoxarifado, não o cadastro inteiro', async () => {
    // a família manda nesta tela: pedir todas traria grupo que não se retira do estoque
    abrir();
    await screen.findByLabelText('Família de produtos');

    expect(listarFamilias).toHaveBeenCalledWith(false, expect.anything(), true);
  });

  it('sem família e sem termo, a tela não lista nada — e diz as duas portas', async () => {
    // listar o acervo inteiro é o que fazia o solicitante rolar a tela atrás da bota
    abrir();
    await screen.findByLabelText('Família de produtos');

    expect(screen.getByText(/Escolha uma família ou busque o produto/)).toBeInTheDocument();
    expect(buscarProdutos).not.toHaveBeenCalled();
  });

  it('a busca acha o produto sem passar pela família, dentro do recorte do almoxarifado', async () => {
    // é a única porta que alcança o produto marcado "sempre entra" numa família que não é
    // de almoxarifado: essa família não entra no seletor
    const usuario = userEvent.setup();
    abrir();
    await usuario.type(await screen.findByLabelText(/Buscar produto/), 'bota');

    await waitFor(() => expect(buscarProdutos).toHaveBeenCalledWith(
      { familia: undefined, q: 'bota', material: true }, expect.anything()));
  });

  it('uma letra só não busca: acharia quase tudo e devolveria a mesma rolagem', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.type(await screen.findByLabelText(/Buscar produto/), 'b');

    await waitFor(() => expect(screen.getByText(/Escolha uma família ou busque/)).toBeInTheDocument());
    expect(buscarProdutos).not.toHaveBeenCalled();
  });
});

describe('a ficha do produto na tela de material', () => {
  const bota38 = produto({ id: 'b38', code: '24001-38', description: 'Bota biqueira de aço — Tam. 38', baseCode: '24001', size: '38', productType: 'EPI', productTypeLabel: 'EPI' });
  const bota40 = produto({ id: 'b40', code: '24001-40', description: 'Bota biqueira de aço — Tam. 40', baseCode: '24001', size: '40', productType: 'EPI', productTypeLabel: 'EPI', imageDocumentId: 'doc-bota' });
  const luva = produto({});

  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(listarCentrosCusto).mockResolvedValue([cc]);
    vi.mocked(listarFamilias).mockResolvedValue([{ name: 'EPI' }] as never);
    vi.mocked(buscarProdutos).mockResolvedValue([bota38, bota40, luva]);
    vi.mocked(fichaDoProduto).mockImplementation(async (id: string) => produto({
      ...(id === 'b40' ? bota40 : id === 'b38' ? bota38 : luva),
      suppliers: [{ id: 'f1', supplierId: 's1', supplierName: 'Alfa EPIs', taxId: null, contact: null,
        supplierItemCode: 'ALF-24001', lastPrice: 89.9, caNumber: '31469', notes: null }],
    }));
    vi.mocked(urlDocumento).mockResolvedValue('blob:foto');
  });

  it('a linha da grade vira o produto com os tamanhos irmãos; o produto sem grade fica só', () => {
    const lista = [bota38, bota40, luva];
    const bota = paraEscolha(bota40, lista);
    expect(bota.hasGrade).toBe(true);
    expect(bota.sizes.map((v) => v.size)).toEqual(['38', '40']);
    expect(bota.key).toBe('24001');
    const solta = paraEscolha(luva, lista);
    expect(solta.hasGrade).toBe(false);
    expect(solta.sizes).toHaveLength(1);
    expect(solta.key).toBe('p1');
  });

  it('clicar no nome abre a ficha com a foto, os tamanhos e o C.A. do fornecedor', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.selectOptions(await screen.findByLabelText('Família de produtos'), 'EPI');
    const grade = await screen.findByTestId('grade-produtos');
    await usuario.click(within(grade).getByRole('button', { name: 'Bota biqueira de aço — Tam. 40' }));

    const ficha = await screen.findByTestId('ficha-do-produto');
    // a ficha é a do tamanho clicado, não a do primeiro da grade
    await waitFor(() => expect(fichaDoProduto).toHaveBeenCalledWith('b40', expect.anything()));
    expect(await within(ficha).findByTestId('foto-do-produto')).toHaveAttribute('src', 'blob:foto');
    expect(within(ficha).getByTestId('tamanhos-da-ficha')).toHaveTextContent('38');
    expect(within(ficha).getByTestId('fornecedores-da-ficha')).toHaveTextContent('Alfa EPIs');
    expect(within(ficha).getByTestId('fornecedores-da-ficha')).toHaveTextContent('31469');
  });

  it('"Usar este produto" marca a linha da grade e leva o cursor à quantidade', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.selectOptions(await screen.findByLabelText('Família de produtos'), 'EPI');
    const grade = await screen.findByTestId('grade-produtos');
    await usuario.click(within(grade).getByRole('button', { name: 'Bota biqueira de aço — Tam. 38' }));
    await screen.findByTestId('ficha-do-produto');
    await usuario.click(await screen.findByRole('button', { name: 'Usar este produto' }));

    expect(screen.queryByTestId('ficha-do-produto')).not.toBeInTheDocument();
    expect(within(grade).getByLabelText('Selecionar Bota biqueira de aço — Tam. 38')).toBeChecked();
    expect(within(grade).getByLabelText('Selecionar Bota biqueira de aço — Tam. 40')).not.toBeChecked();
    await waitFor(() => expect(within(grade).getByLabelText('Quantidade de Bota biqueira de aço — Tam. 38')).toHaveFocus());
  });

  it('"Voltar à lista" fecha a ficha sem marcar nada', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.selectOptions(await screen.findByLabelText('Família de produtos'), 'EPI');
    const grade = await screen.findByTestId('grade-produtos');
    await usuario.click(within(grade).getByRole('button', { name: 'Luva nitrílica' }));
    await screen.findByTestId('ficha-do-produto');
    await usuario.click(await screen.findByRole('button', { name: '← Voltar à lista' }));
    expect(screen.queryByTestId('ficha-do-produto')).not.toBeInTheDocument();
    expect(within(grade).getByLabelText('Selecionar Luva nitrílica')).not.toBeChecked();
  });
});
