import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Produto } from '@/api/catalogo';
import { ToastProvider } from '@/componentes/Toast';
import { itensDoFormulario, itensSemCa, NovaSolicitacao, rotuloDoProduto } from './NovaSolicitacao';

vi.mock('@/api/catalogo', () => ({ listarProdutos: vi.fn(), familiasDoCatalogo: vi.fn() }));
vi.mock('@/api/centrosCusto', () => ({ listarCentrosCusto: vi.fn() }));
vi.mock('@/api/empresas', () => ({ listarEmpresas: vi.fn(), perfilDaEmpresa: vi.fn() }));
vi.mock('@/api/locais', async (importar) => ({
  ...(await importar<typeof import('@/api/locais')>()),
  listarLocaisDeEntrega: vi.fn(),
}));
vi.mock('@/api/solicitacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/solicitacoes')>()),
  criarSolicitacao: vi.fn(), anexarNaSolicitacao: vi.fn(),
}));

import { familiasDoCatalogo, listarProdutos } from '@/api/catalogo';
import { listarCentrosCusto } from '@/api/centrosCusto';
import { listarEmpresas, perfilDaEmpresa } from '@/api/empresas';
import { listarLocaisDeEntrega } from '@/api/locais';
import { criarSolicitacao } from '@/api/solicitacoes';

const produto = (p: Partial<Produto>): Produto => ({
  id: 'p1', code: 'MAT-001', description: 'Cimento CP-II', family: 'CIVIL', unitOfMeasure: 'SC',
  referencePrice: null, active: true, stockControlled: true, purchasable: true, minimumQty: null,
  productType: null, productTypeLabel: null, baseCode: null, size: null, imageDocumentId: null,
  imageFileName: null, compliancePending: false, suppliers: [],
  ...p,
});

const epiSemCa = produto({
  id: 'p2', code: 'EPI-002', description: 'Bota de segurança', family: 'EPI', unitOfMeasure: 'PAR',
  productType: 'EPI', productTypeLabel: 'EPI', compliancePending: true,
});

const abrir = () => render(
  <MemoryRouter><ToastProvider><NovaSolicitacao /></ToastProvider></MemoryRouter>,
);

describe('conformidade de EPI na SC (IC-ERR-023)', () => {
  it('só acusa o item do catálogo que é EPI/EPC sem C.A.', () => {
    const catalogo = [produto({}), epiSemCa];
    expect(itensSemCa([{ produto: rotuloDoProduto(produto({})) }], catalogo)).toEqual([]);
    expect(itensSemCa([{ produto: rotuloDoProduto(epiSemCa) }], catalogo)).toEqual([epiSemCa]);
    // item descrito à mão não tem cadastro para checar
    expect(itensSemCa([{ produto: 'bota qualquer' }], catalogo)).toEqual([]);
  });

  it('o item digitado à mão continua virando descrição livre', () => {
    expect(itensDoFormulario(
      [{ chave: 'i1', produto: 'cimento a granel', unidade: 'TN', quantidade: '3' }], [produto({})],
    )).toEqual([{ description: 'cimento a granel', catalogItemId: null, unitOfMeasure: 'TN', quantity: 3 }]);
  });
});

describe('tela Inclusão de SC', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(listarProdutos).mockResolvedValue([produto({}), epiSemCa]);
    vi.mocked(familiasDoCatalogo).mockResolvedValue(['CIVIL', 'EPI']);
    vi.mocked(listarCentrosCusto).mockResolvedValue([
      { id: 'cc1', code: 'BAH-001', name: 'Obra Bahia' } as never,
    ]);
    vi.mocked(listarEmpresas).mockResolvedValue([]);
    vi.mocked(perfilDaEmpresa).mockResolvedValue(null as never);
    vi.mocked(listarLocaisDeEntrega).mockResolvedValue([]);
  });

  it('escolher um EPI sem C.A. avisa na linha e barra o envio', async () => {
    const usuario = userEvent.setup();
    abrir();

    const campo = await screen.findByLabelText('Produto');
    await usuario.type(campo, rotuloDoProduto(epiSemCa));

    const aviso = await screen.findByTestId('linha-sem-ca');
    expect(aviso).toHaveTextContent('IC-ERR-023');
    expect(aviso).toHaveTextContent('Bota de segurança');

    // o resto do formulário fica válido, para o envio chegar até a regra
    await usuario.type(screen.getByLabelText('Quantidade'), '2');
    await usuario.type(screen.getByLabelText('Justificativa da solicitação'), 'obra parada');
    await usuario.selectOptions(screen.getByLabelText('Centro de Custo'), 'BAH-001');

    await usuario.click(screen.getByRole('button', { name: /Criar rascunho da SC/ }));
    expect(await screen.findByTestId('toast')).toHaveTextContent('IC-ERR-023');
    expect(criarSolicitacao).not.toHaveBeenCalled();
  });

  it('item regular do catálogo não mostra aviso e traz a unidade', async () => {
    const usuario = userEvent.setup();
    abrir();

    const campo = await screen.findByLabelText('Produto');
    await usuario.type(campo, rotuloDoProduto(produto({})));

    expect(screen.queryByTestId('linha-sem-ca')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Unidade')).toHaveValue('SC');
  });
});
