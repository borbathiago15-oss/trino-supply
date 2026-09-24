import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ProdutoParaEscolha } from '@/api/catalogo';
import { ToastProvider } from '@/componentes/Toast';
import {
  itensDoFormulario, itensSemCa, novaLinha, NovaSolicitacao, SEM_CADASTRO, totalDaGrade, type LinhaItem,
} from './NovaSolicitacao';

vi.mock('@/api/catalogo', () => ({ produtosParaEscolha: vi.fn(), fichaDoProduto: vi.fn() }));
vi.mock('@/api/familias', () => ({ listarFamilias: vi.fn() }));
vi.mock('@/api/documentos', () => ({ urlDocumento: vi.fn() }));
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
vi.mock('@/api/tiposDeSolicitacao', () => ({ listarTiposDeSolicitacao: vi.fn() }));

import { fichaDoProduto, produtosParaEscolha, type Produto } from '@/api/catalogo';
import { urlDocumento } from '@/api/documentos';
import { listarFamilias } from '@/api/familias';
import { listarCentrosCusto } from '@/api/centrosCusto';
import { listarEmpresas, perfilDaEmpresa } from '@/api/empresas';
import { listarLocaisDeEntrega } from '@/api/locais';
import { criarSolicitacao } from '@/api/solicitacoes';
import { listarTiposDeSolicitacao } from '@/api/tiposDeSolicitacao';

/** Um produto simples do catálogo: sem grade, um "tamanho" de size nulo. */
const cimento: ProdutoParaEscolha = {
  key: '#p1', baseCode: null, description: 'Cimento CP-II', family: 'CIVIL', unitOfMeasure: 'SC',
  productType: null, productTypeLabel: null, hasGrade: false, compliancePending: false,
  sizes: [{ id: 'v-cimento', code: 'MAT-001', size: null, referencePrice: 32, compliancePending: false, imageDocumentId: null }],
};

/** A bota do 38 ao 40: um produto, três tamanhos — cada um com código e C.A. próprios. */
const bota: ProdutoParaEscolha = {
  key: 'EPI|12003', baseCode: '12003', description: 'Bota de segurança', family: 'EPI', unitOfMeasure: 'PAR',
  productType: 'EPI', productTypeLabel: 'EPI', hasGrade: true, compliancePending: false,
  sizes: [
    { id: 'v38', code: '12003-38', size: '38', referencePrice: 89.9, compliancePending: false, imageDocumentId: null },
    { id: 'v39', code: '12003-39', size: '39', referencePrice: 89.9, compliancePending: false, imageDocumentId: null },
    { id: 'v40', code: '12003-40', size: '40', referencePrice: 89.9, compliancePending: true, imageDocumentId: null },
  ],
};

const comProduto = (p: ProdutoParaEscolha, porTamanho: Record<string, string> = {}, quantidade = '1'): LinhaItem =>
  ({ ...novaLinha(), escolhido: p, produto: p.description, unidade: p.unitOfMeasure, familia: p.family, porTamanho, quantidade });

const abrir = () => render(
  <MemoryRouter><ToastProvider><NovaSolicitacao /></ToastProvider></MemoryRouter>,
);

describe('itens da SC: catálogo, grade de tamanhos e item de fora', () => {
  it('a grade vira um item por tamanho pedido, e o tamanho zerado não entra', () => {
    const itens = itensDoFormulario([comProduto(bota, { v38: '2', v39: '', v40: '0' })]);
    expect(itens).toEqual([
      { description: '', catalogItemId: 'v38', unitOfMeasure: 'PAR', quantity: 2, family: null },
    ]);
    // dois tamanhos pedidos, dois itens — é o que a compra precisa: cada tamanho tem código próprio
    expect(itensDoFormulario([comProduto(bota, { v38: '2', v39: '3' })]).map((i) => i.catalogItemId))
      .toEqual(['v38', 'v39']);
  });

  it('produto sem grade usa a quantidade da linha, e sem quantidade não vira item', () => {
    expect(itensDoFormulario([comProduto(cimento, {}, '4')])).toEqual([
      { description: '', catalogItemId: 'v-cimento', unitOfMeasure: 'SC', quantity: 4, family: null },
    ]);
    expect(itensDoFormulario([comProduto(cimento, {}, '0')])).toEqual([]);
    // grade sem nenhuma quantidade também não vira item
    expect(itensDoFormulario([comProduto(bota)])).toEqual([]);
  });

  it('o item de fora do catálogo continua virando descrição livre, com a família de quem pede', () => {
    const livre: LinhaItem = { ...novaLinha(), produto: ' Fita isolante ', unidade: 'RL', quantidade: '3', familia: 'ELETRICA' };
    expect(itensDoFormulario([livre])).toEqual([
      { description: 'Fita isolante', catalogItemId: null, unitOfMeasure: 'RL', quantity: 3, family: 'ELETRICA' },
    ]);
    // "produto não cadastrado" é ausência declarada, não uma família
    expect(itensDoFormulario([{ ...livre, familia: SEM_CADASTRO }])[0].family).toBeNull();
    // linha em branco é descartada
    expect(itensDoFormulario([novaLinha()])).toEqual([]);
  });

  it('a soma da grade aceita vírgula e ignora o campo vazio', () => {
    expect(totalDaGrade(comProduto(bota, { v38: '2', v39: '1,5', v40: '' }))).toBe(3.5);
    expect(totalDaGrade(comProduto(cimento))).toBe(0);
  });

  it('o C.A. é cobrado do tamanho pedido, não do produto inteiro (IC-ERR-023)', () => {
    // o 40 está sem C.A.: só acusa quando alguém pede o 40
    expect(itensSemCa([comProduto(bota, { v38: '2' })])).toEqual([]);
    expect(itensSemCa([comProduto(bota, { v40: '1' })])).toEqual([
      { descricao: 'Bota de segurança', tipo: 'EPI', code: '12003-40' },
    ]);
    // item de fora do catálogo não tem C.A. a cobrar
    expect(itensSemCa([{ ...novaLinha(), produto: 'Fita' }])).toEqual([]);
  });
});

describe('tela Inclusão de SC', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(produtosParaEscolha).mockResolvedValue([cimento, bota]);
    // do cadastro de famílias: só as ativas, e não os nomes que aparecem nos produtos
    vi.mocked(listarFamilias).mockResolvedValue([{ name: 'CIVIL' }, { name: 'EPI' }] as never);
    vi.mocked(fichaDoProduto).mockImplementation(async (id: string) => ficha(id));
    vi.mocked(urlDocumento).mockResolvedValue('blob:foto');
    vi.mocked(listarCentrosCusto).mockResolvedValue([
      { id: 'cc1', code: 'BAH-001', name: 'Obra Bahia' } as never,
    ]);
    vi.mocked(listarEmpresas).mockResolvedValue([]);
    vi.mocked(perfilDaEmpresa).mockResolvedValue(null as never);
    vi.mocked(listarLocaisDeEntrega).mockResolvedValue([]);
    vi.mocked(listarTiposDeSolicitacao).mockResolvedValue({ items: [] } as never);
  });

  /** Abre o seletor, busca, abre a ficha e usa o produto — o caminho que a tela passou a ter. */
  const escolherNoCatalogo = async (usuario: ReturnType<typeof userEvent.setup>, descricao: string) => {
    await usuario.click(await screen.findByRole('button', { name: 'Buscar no catálogo' }));
    const dialogo = within(await screen.findByRole('dialog'));
    await usuario.selectOptions(dialogo.getByLabelText('Família'), 'EPI');
    await usuario.click(await dialogo.findByRole('button', { name: new RegExp(descricao) }));
    await usuario.click(await screen.findByRole('button', { name: 'Usar este produto' }));
  };

  /** "Não achou?": o item fora do catálogo sai da busca, com o termo como descrição. */
  const descreverForaDoCatalogo = async (usuario: ReturnType<typeof userEvent.setup>, termo: string) => {
    await usuario.click(await screen.findByRole('button', { name: 'Buscar no catálogo' }));
    const dialogo = within(await screen.findByRole('dialog'));
    await usuario.type(dialogo.getByLabelText(/Buscar produto/), termo);
    await usuario.click(dialogo.getByRole('button', { name: 'Não achou? Pedir item fora do catálogo' }));
  };

  it('a busca só acontece com família ou termo: o catálogo inteiro não é listado', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.click(await screen.findByRole('button', { name: 'Buscar no catálogo' }));

    const dialogo = within(screen.getByRole('dialog'));
    expect(dialogo.getByText(/Escolha a família ou digite ao menos duas letras/)).toBeInTheDocument();
    expect(produtosParaEscolha).not.toHaveBeenCalled();

    await usuario.selectOptions(dialogo.getByLabelText('Família'), 'EPI');
    await waitFor(() => expect(produtosParaEscolha).toHaveBeenCalledWith(
      expect.objectContaining({ familia: 'EPI' }), expect.anything()));
    expect(await dialogo.findByTestId('produtos-encontrados')).toHaveTextContent('Bota de segurança');
    // o produto com grade diz quantos tamanhos tem, antes do clique
    expect(dialogo.getByText(/3 tamanho\(s\): 38, 39, 40/)).toBeInTheDocument();
  });

  it('a bota escolhida abre a grade, e cada tamanho pedido vira um item da SC', async () => {
    const usuario = userEvent.setup();
    vi.mocked(criarSolicitacao).mockResolvedValue({ id: 'sc1', number: 'PR-2026-000001' } as never);
    abrir();
    await escolherNoCatalogo(usuario, 'Bota de segurança');

    const grade = within(await screen.findByTestId('grade-de-tamanhos'));
    // o 40 está sem C.A.: o campo dele nem aceita quantidade
    expect(grade.getByLabelText('Tamanho 40 de Bota de segurança')).toBeDisabled();
    await usuario.type(grade.getByLabelText('Tamanho 38 de Bota de segurança'), '2');
    await usuario.type(grade.getByLabelText('Tamanho 39 de Bota de segurança'), '3');
    expect(screen.getByTestId('total-da-grade')).toHaveTextContent('Total: 5 PAR');

    await usuario.type(screen.getByLabelText('Justificativa da solicitação'), 'reposição de EPI');
    await usuario.selectOptions(screen.getByLabelText('Centro de Custo'), 'BAH-001');
    await usuario.click(screen.getByRole('button', { name: /Criar rascunho da SC/ }));

    await waitFor(() => expect(criarSolicitacao).toHaveBeenCalledWith(expect.objectContaining({
      items: [
        { description: '', catalogItemId: 'v38', unitOfMeasure: 'PAR', quantity: 2, family: null },
        { description: '', catalogItemId: 'v39', unitOfMeasure: 'PAR', quantity: 3, family: null },
      ],
    })));
  });

  it('produto sem grade não mostra tamanhos, e a família vem do cadastro', async () => {
    const usuario = userEvent.setup();
    abrir();
    await escolherNoCatalogo(usuario, 'Cimento CP-II');

    expect(screen.queryByTestId('grade-de-tamanhos')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Quantidade de Cimento CP-II')).toHaveValue(1);
    expect(screen.getByText(/do cadastro do produto/)).toHaveTextContent('CIVIL');
    // escolhido do catálogo, a linha não pede mais descrição livre
    expect(screen.queryByLabelText('Descrição do item')).not.toBeInTheDocument();
  });

  it('o item fora do catálogo sai da busca, com unidade, quantidade e família', async () => {
    const usuario = userEvent.setup();
    vi.mocked(criarSolicitacao).mockResolvedValue({ id: 'sc1', number: 'PR-2026-000001' } as never);
    abrir();

    await descreverForaDoCatalogo(usuario, 'Fita isolante');
    expect(screen.getByLabelText('Descrição do item')).toHaveValue('Fita isolante');
    await usuario.type(screen.getByLabelText('Unidade'), 'RL');
    await usuario.clear(screen.getByLabelText('Quantidade'));
    await usuario.type(screen.getByLabelText('Quantidade'), '3');
    await usuario.selectOptions(screen.getByLabelText(/Família de Fita isolante/), 'CIVIL');
    await usuario.type(screen.getByLabelText('Justificativa da solicitação'), 'manutenção');
    await usuario.selectOptions(screen.getByLabelText('Centro de Custo'), 'BAH-001');
    await usuario.click(screen.getByRole('button', { name: /Criar rascunho da SC/ }));

    await waitFor(() => expect(criarSolicitacao).toHaveBeenCalledWith(expect.objectContaining({
      items: [{ description: 'Fita isolante', catalogItemId: null, unitOfMeasure: 'RL', quantity: 3, family: 'CIVIL' }],
    })));
  });

  it('pedir o tamanho sem C.A. avisa na linha e barra o envio (IC-ERR-023)', async () => {
    const usuario = userEvent.setup();
    // aqui toda a grade está sem C.A.: o seletor já recusa o produto inteiro
    vi.mocked(produtosParaEscolha).mockResolvedValue([{
      ...bota, compliancePending: true,
      sizes: bota.sizes.map((v) => ({ ...v, compliancePending: true })),
    }]);
    abrir();
    await usuario.click(await screen.findByRole('button', { name: 'Buscar no catálogo' }));
    const dialogo = within(screen.getByRole('dialog'));
    await usuario.selectOptions(dialogo.getByLabelText('Família'), 'EPI');

    const botao = await dialogo.findByRole('button', { name: /Bota de segurança/ });
    expect(botao).toHaveTextContent('IC-ERR-023');
    // a ficha abre para quem quer entender, mas não deixa usar
    await usuario.click(botao);
    expect(await screen.findByRole('button', { name: 'Usar este produto' })).toBeDisabled();
  });

  it('o essencial vem primeiro e o resto fica recolhido em "Mais detalhes"', async () => {
    abrir();
    await screen.findByRole('button', { name: 'Buscar no catálogo' });
    const extras = screen.getByTestId('mais-detalhes');
    expect(extras).not.toHaveAttribute('open');
    // o que decide a SC fica fora da gaveta
    expect(screen.getByLabelText('Centro de Custo').closest('[data-testid="mais-detalhes"]')).toBeNull();
    expect(screen.getByLabelText('Prioridade').closest('[data-testid="mais-detalhes"]')).toBeNull();
    // o opcional, dentro dela
    for (const rotulo of [/Orçamento previsto/, 'Local de Entrega', 'Observação Interna', 'Empresa'])
      expect(screen.getByLabelText(rotulo).closest('[data-testid="mais-detalhes"]')).toBe(extras);
  });

  it('o orçamento informado vai na SC — é a régua do saving (§17)', async () => {
    const usuario = userEvent.setup();
    vi.mocked(criarSolicitacao).mockResolvedValue({ id: 'sc1', number: 'PR-2026-000001' } as never);
    abrir();

    await descreverForaDoCatalogo(usuario, 'Fita isolante');
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

    await descreverForaDoCatalogo(usuario, 'Fita isolante');
    await usuario.type(screen.getByLabelText('Justificativa da solicitação'), 'reposição de obra');
    await usuario.selectOptions(screen.getByLabelText('Centro de Custo'), 'BAH-001');

    await usuario.click(screen.getByRole('button', { name: /Criar rascunho da SC/ }));
    await waitFor(() => expect(criarSolicitacao).toHaveBeenCalledWith(
      expect.objectContaining({ budget: null })));
  });

  it('a linha nova tem uma porta só: a busca do catálogo', async () => {
    abrir();
    const linha = await screen.findByTestId('linha-sem-produto');
    expect(within(linha).getByRole('button', { name: 'Buscar no catálogo' })).toBeInTheDocument();
    // não há mais campo de texto que parecia uma segunda busca
    expect(within(linha).queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('linha sem produto barra o envio e diz o que fazer', async () => {
    const usuario = userEvent.setup();
    abrir();
    await screen.findByTestId('linha-sem-produto');
    await usuario.type(screen.getByLabelText('Justificativa da solicitação'), 'reposição');
    await usuario.selectOptions(screen.getByLabelText('Centro de Custo'), 'BAH-001');
    await usuario.click(screen.getByRole('button', { name: /Criar rascunho da SC/ }));
    expect(await screen.findByTestId('toast')).toHaveTextContent('Há item sem produto');
    expect(criarSolicitacao).not.toHaveBeenCalled();
  });

  it('as famílias da busca são as do cadastro', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.click(await screen.findByRole('button', { name: 'Buscar no catálogo' }));
    const opcoes = within(within(screen.getByRole('dialog')).getByLabelText('Família')).getAllByRole('option');
    expect(opcoes.map((o) => o.textContent)).toEqual(['Todas as famílias', 'CIVIL', 'EPI']);
    expect(listarFamilias).toHaveBeenCalledWith(false, expect.anything());
  });

  it('clicar no produto abre a ficha com foto, código e o cadastro, antes de usar', async () => {
    const usuario = userEvent.setup();
    abrir();
    await usuario.click(await screen.findByRole('button', { name: 'Buscar no catálogo' }));
    const dialogo = within(screen.getByRole('dialog'));
    await usuario.selectOptions(dialogo.getByLabelText('Família'), 'EPI');
    await usuario.click(await dialogo.findByRole('button', { name: /Cimento CP-II/ }));

    const f = within(await screen.findByTestId('ficha-do-produto'));
    expect(await f.findByTestId('foto-do-produto')).toHaveAttribute('src', 'blob:foto');
    expect(f.getByText('MAT-001')).toBeInTheDocument();
    expect(f.getByText('Cimento CP-II')).toBeInTheDocument();
    expect(within(f.getByTestId('fornecedores-da-ficha')).getByText('Votorantim')).toBeInTheDocument();
    // voltar não escolhe nada
    await usuario.click(f.getByRole('button', { name: '← Voltar à busca' }));
    expect(await screen.findByTestId('produtos-encontrados')).toBeInTheDocument();
  });

  it('a empresa é escolhida do cadastro de CNPJs, sem texto livre', async () => {
    vi.mocked(listarEmpresas).mockResolvedValue([
      { legalName: 'TRINO FRIO ARMAZENS GERAIS LTDA' }, { legalName: 'TRINO LOGISTICA INTEGRADA LTDA' },
    ] as never);
    abrir();
    const empresa = await screen.findByLabelText('Empresa');
    expect(empresa.tagName).toBe('SELECT');
    await waitFor(() => expect(within(empresa).getAllByRole('option').map((o) => o.textContent)).toEqual([
      'Selecione a empresa…', 'TRINO FRIO ARMAZENS GERAIS LTDA', 'TRINO LOGISTICA INTEGRADA LTDA',
    ]));
  });
});

/** A ficha que o servidor devolve: o cadastro inteiro do produto. */
function ficha(id: string): Produto {
  return {
    id, code: id === 'v-cimento' ? 'MAT-001' : '12003-38', description: 'Cimento CP-II', family: 'CIVIL',
    unitOfMeasure: 'SC', referencePrice: 32, active: true, stockControlled: true, purchasable: true, minimumQty: 10,
    productType: null, productTypeLabel: null, baseCode: null, size: null,
    imageDocumentId: 'doc-foto', imageFileName: 'cimento.jpg', compliancePending: false,
    suppliers: [{ supplierId: null, supplierName: 'Votorantim', taxId: null, contact: null,
      supplierItemCode: 'CP2-50', lastPrice: 31.5, caNumber: null, notes: null }],
  };
}
