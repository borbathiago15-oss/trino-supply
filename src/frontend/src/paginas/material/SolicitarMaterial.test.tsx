import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Produto } from '@/api/catalogo';
import type { CentroCusto } from '@/api/centrosCusto';
import { ToastProvider } from '@/componentes/Toast';
import { casDoProduto, itensEscolhidos, semCaObrigatorio, SolicitarMaterial } from './SolicitarMaterial';

vi.mock('@/api/catalogo', () => ({ buscarProdutos: vi.fn() }));
vi.mock('@/api/familias', () => ({ listarFamilias: vi.fn() }));
vi.mock('@/api/centrosCusto', () => ({ listarCentrosCusto: vi.fn() }));
vi.mock('@/api/material', () => ({ criarSolicitacaoMaterial: vi.fn() }));

import { buscarProdutos } from '@/api/catalogo';
import { listarFamilias } from '@/api/familias';
import { listarCentrosCusto } from '@/api/centrosCusto';
import { criarSolicitacaoMaterial } from '@/api/material';

const cc = { id: 'cc1', code: 'BAH-001', name: 'Obra Bahia' } as CentroCusto;

const produto = (p: Partial<Produto>): Produto => ({
  id: 'p1', code: 'EPI-001', description: 'Luva nitrílica', family: 'EPI', unitOfMeasure: 'PAR',
  referencePrice: null, active: true, stockControlled: true, purchasable: true, minimumQty: null,
  productType: null, productTypeLabel: null, baseCode: null, size: null, imageDocumentId: null,
  imageFileName: null, compliancePending: false, suppliers: [],
  ...p,
});

const abrir = () => render(
  <MemoryRouter><ToastProvider><SolicitarMaterial /></ToastProvider></MemoryRouter>,
);

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
    expect(buscarProdutos).toHaveBeenCalledWith({ familia: 'EPI' }, expect.anything());
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
});
