import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Produto } from '@/api/catalogo';
import { ToastProvider } from '@/componentes/Toast';
import { familiaDaLinha, itensDoFormulario, itensSemCa, NovaSolicitacao, rotuloDoProduto, SEM_CADASTRO } from './NovaSolicitacao';

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

  it('o item digitado à mão continua virando descrição livre, agora com família', () => {
    expect(itensDoFormulario(
      [{ chave: 'i1', produto: 'cimento a granel', unidade: 'TN', quantidade: '3', familia: 'CIVIL' }],
      [produto({})],
    )).toEqual([{
      description: 'cimento a granel', catalogItemId: null, unitOfMeasure: 'TN',
      quantity: 3, family: 'CIVIL',
    }]);
  });

  it('"produto não cadastrado" vira nulo — é ausência declarada, não uma família', () => {
    // o servidor resolve nulo como DIVERSOS; mandar a marca da tela criaria uma
    // família chamada "__SEM_CADASTRO__" no relatório de spend
    const [item] = itensDoFormulario(
      [{ chave: 'i1', produto: 'peça sob medida', unidade: 'UN', quantidade: '1', familia: SEM_CADASTRO }],
      [],
    );
    expect(item.family).toBeNull();
  });

  it('produto do catálogo não leva família da tela: a dele é a do cadastro', () => {
    // aceitar deixaria o mesmo produto em duas famílias conforme quem digitou
    const p = produto({});
    const [item] = itensDoFormulario(
      [{ chave: 'i1', produto: rotuloDoProduto(p), unidade: '', quantidade: '2', familia: 'LIMPEZA' }],
      [p],
    );
    expect(item.catalogItemId).toBe(p.id);
    expect(item.family).toBeNull();
  });

  it('a linha mostra a família do catálogo, travada, e a do solicitante quando é livre', () => {
    const p = produto({});
    expect(familiaDaLinha(
      { chave: 'i1', produto: rotuloDoProduto(p), unidade: '', quantidade: '1', familia: 'LIMPEZA' }, [p],
    )).toEqual({ valor: p.family, travada: true });

    expect(familiaDaLinha(
      { chave: 'i2', produto: 'peça sob medida', unidade: '', quantidade: '1', familia: 'CIVIL' }, [p],
    )).toEqual({ valor: 'CIVIL', travada: false });
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

  it('o orçamento informado vai na SC — é a régua do saving (§17)', async () => {
    const usuario = userEvent.setup();
    vi.mocked(criarSolicitacao).mockResolvedValue({ id: 'sc1', number: 'PR-2026-000001' } as never);
    abrir();

    await usuario.type(await screen.findByLabelText('Produto'), rotuloDoProduto(produto({})));
    await usuario.type(screen.getByLabelText('Quantidade'), '2');
    await usuario.type(screen.getByLabelText('Justificativa da solicitação'), 'reposição de obra');
    await usuario.selectOptions(screen.getByLabelText('Centro de Custo'), 'BAH-001');
    await usuario.type(screen.getByLabelText(/Orçamento previsto/), '1200');

    await usuario.click(screen.getByRole('button', { name: /Criar rascunho da SC/ }));
    await waitFor(() => expect(criarSolicitacao).toHaveBeenCalledWith(
      expect.objectContaining({ budget: 1200 })));
  });

  it('sem orçamento, a SC não inventa um — vai nula', async () => {
    // orçamento zero viraria uma meta que o comprador sempre bate; nulo diz a verdade
    const usuario = userEvent.setup();
    vi.mocked(criarSolicitacao).mockResolvedValue({ id: 'sc1', number: 'PR-2026-000001' } as never);
    abrir();

    await usuario.type(await screen.findByLabelText('Produto'), rotuloDoProduto(produto({})));
    await usuario.type(screen.getByLabelText('Quantidade'), '2');
    await usuario.type(screen.getByLabelText('Justificativa da solicitação'), 'reposição de obra');
    await usuario.selectOptions(screen.getByLabelText('Centro de Custo'), 'BAH-001');

    await usuario.click(screen.getByRole('button', { name: /Criar rascunho da SC/ }));
    await waitFor(() => expect(criarSolicitacao).toHaveBeenCalledWith(
      expect.objectContaining({ budget: null })));
  });
});
