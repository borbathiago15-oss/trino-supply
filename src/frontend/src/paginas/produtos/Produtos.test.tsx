import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { Produto, ResumoCatalogo, TipoDeProduto } from '@/api/catalogo';
import { ToastProvider } from '@/componentes/Toast';
import { Produtos, resumoEmTexto } from './Produtos';

vi.mock('@/api/catalogo', async (importar) => ({
  ...(await importar<typeof import('@/api/catalogo')>()),
  resumoCatalogo: vi.fn(),
  tiposDeProduto: vi.fn(),
  buscarProdutos: vi.fn(),
  criarProduto: vi.fn(),
  criarGradeDeTamanhos: vi.fn(),
  atualizarProduto: vi.fn(),
  excluirProduto: vi.fn(),
}));
vi.mock('@/api/familias', () => ({ listarFamilias: vi.fn() }));
vi.mock('@/api/fornecedores', () => ({ listarFornecedores: vi.fn() }));
// a miniatura busca o documento com token; aqui só o marcador importa
vi.mock('@/componentes/Miniatura', () => ({
  Miniatura: ({ descricao }: { descricao: string }) => <span data-testid="miniatura">{descricao}</span>,
  Visor: () => null,
}));
let usuarioAtual: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => usuarioAtual }));

import {
  atualizarProduto, buscarProdutos, criarGradeDeTamanhos, excluirProduto, criarProduto, resumoCatalogo, tiposDeProduto,
} from '@/api/catalogo';
import { listarFamilias } from '@/api/familias';
import { listarFornecedores } from '@/api/fornecedores';

const produto = (p: Partial<Produto>): Produto => ({
  id: 'p-' + (p.code ?? '1'), code: '12003', description: 'Luva nitrílica', family: 'EPI',
  unitOfMeasure: 'PAR', referencePrice: 12.5, active: true, stockControlled: true, purchasable: true,
  minimumQty: null, productType: 'EPI', productTypeLabel: 'EPI', baseCode: null, size: null,
  imageDocumentId: null, imageFileName: null, compliancePending: false, suppliers: [], ...p,
});

const resumo: ResumoCatalogo = {
  total: 120, active: 118, inactive: 2, compliancePending: 3,
  families: [{ family: 'EPI', count: 40 }, { family: 'LIMPEZA', count: 78 }],
};

const tipos: TipoDeProduto[] = [
  { key: 'EPI', label: 'EPI', requiresCa: true },
  { key: 'MATERIAL', label: 'Material de consumo', requiresCa: false },
];

const gestor: Usuario = { id: 'g1', email: 'g@t.com', name: 'Gestor', role: 'SupplyManager', modules: ['PRODUTOS'] };
const solicitante: Usuario = { id: 's1', email: 's@t.com', name: 'Ana', role: 'Requester', modules: ['PRODUTOS'] };
const comprador: Usuario = { id: 'c1', email: 'c@t.com', name: 'Carlos', role: 'PurchasingOfficer', modules: ['PRODUTOS'] };

describe('resumoEmTexto', () => {
  it('junta ativos, inativos e pendência de C.A.', () => {
    expect(resumoEmTexto(resumo)).toBe('118 produto(s) ativo(s) · 2 inativo(s) · 3 sem C.A. de fornecedor');
  });
  it('omite o que está zerado', () => {
    expect(resumoEmTexto({ ...resumo, inactive: 0, compliancePending: 0 })).toBe('118 produto(s) ativo(s)');
  });
});

describe('<Produtos />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    usuarioAtual = gestor;
    vi.mocked(resumoCatalogo).mockResolvedValue(resumo);
    vi.mocked(tiposDeProduto).mockResolvedValue(tipos);
    vi.mocked(listarFamilias).mockResolvedValue([]);
    vi.mocked(listarFornecedores).mockResolvedValue([]);
    vi.mocked(buscarProdutos).mockResolvedValue([
      produto({ code: '12003', compliancePending: true }),
      produto({ code: '12004', description: 'Bota de segurança', size: 'P', imageDocumentId: 'doc1', active: false,
        suppliers: [{ supplierId: null, supplierName: 'Alfa EPIs', taxId: null, contact: null, supplierItemCode: null, lastPrice: null, caNumber: '4567', notes: null }] }),
    ]);
  });

  const montar = () => render(<ToastProvider><Produtos /></ToastProvider>);

  it('não busca sem critério e avisa o que fazer', async () => {
    montar();
    await waitFor(() => expect(screen.getByLabelText('Buscar produto')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    expect(await screen.findByTestId('toast')).toHaveTextContent('Digite um código ou parte da descrição');
    expect(buscarProdutos).not.toHaveBeenCalled();
    expect(screen.queryByTestId('tabela-produtos')).not.toBeInTheDocument();
  });

  it('busca por texto e mostra conformidade, tamanho e foto', async () => {
    montar();
    await waitFor(() => expect(screen.getByLabelText('Buscar produto')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText('Buscar produto'), 'luva');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(screen.getByTestId('tabela-produtos')).toBeInTheDocument());

    expect(buscarProdutos).toHaveBeenCalledWith({ q: 'luva', familia: '', incluirInativos: true }, expect.anything());
    const tabela = within(screen.getByTestId('tabela-produtos'));
    expect(tabela.getByText('⚠ sem C.A. em nenhum fornecedor')).toBeInTheDocument();
    expect(tabela.getByText('C.A. 4567 · Alfa EPIs')).toBeInTheDocument();
    expect(tabela.getByTestId('miniatura')).toHaveTextContent('Bota de segurança');
    expect(tabela.getByText('INATIVO')).toBeInTheDocument();
  });

  it('excluir pede confirmação; o produto que já circulou é recusado e a mensagem diz onde', async () => {
    vi.mocked(excluirProduto).mockRejectedValue(
      new Error('"Luva" já foi usado (2 itens de solicitação de compra) e não pode ser excluído: apagaria o histórico. Inative o produto.'));
    montar();
    await waitFor(() => expect(screen.getByLabelText('Buscar produto')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText('Buscar produto'), 'luva');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    const tabela = await screen.findByTestId('tabela-produtos');
    await userEvent.click(within(tabela).getAllByRole('button', { name: 'Excluir' })[0]);

    const dialogo = screen.getByRole('dialog', { name: 'Excluir produto' });
    expect(dialogo).toHaveTextContent('Só sai o produto que nunca foi usado');
    await userEvent.click(within(dialogo).getByRole('button', { name: 'Excluir' }));
    expect(excluirProduto).toHaveBeenCalledTimes(1);
    expect(await screen.findByTestId('toast')).toHaveTextContent('2 itens de solicitação de compra');
  });

  it('o resumo do catálogo aparece antes de qualquer busca', async () => {
    montar();
    await waitFor(() => expect(screen.getByText(/118 produto\(s\) ativo\(s\)/)).toBeInTheDocument());
    expect(screen.getByText(/3 sem C.A. de fornecedor/)).toBeInTheDocument();
  });

  it('tipo que exige C.A. mostra o aviso e barra o envio sem o número', async () => {
    montar();
    await waitFor(() => expect(screen.getByRole('button', { name: '+ Novo produto' })).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: '+ Novo produto' }));
    await userEvent.selectOptions(screen.getByLabelText('Tipo de produto'), 'EPI');
    expect(screen.getByTestId('aviso-ca')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Descrição'), 'Bota de segurança');
    await userEvent.selectOptions(screen.getByLabelText('Família'), 'EPI');
    await userEvent.type(screen.getByLabelText('Nome do fornecedor fora do cadastro'), 'Alfa EPIs');
    await userEvent.click(screen.getByRole('button', { name: 'Adicionar produto' }));
    expect(await screen.findByTestId('toast')).toHaveTextContent('EPI e EPC exigem o C.A.');
  });

  it('a grade de tamanhos cadastra um produto por tamanho, de uma vez', async () => {
    // antes, bota do 38 ao 40 eram três cadastros à mão, com três códigos inventados
    vi.mocked(criarGradeDeTamanhos).mockResolvedValue([
      produto({ id: 'a', code: '12003-38' }), produto({ id: 'b', code: '12003-39' }),
      produto({ id: 'c', code: '12003-40' }),
    ]);
    montar();
    await waitFor(() => expect(screen.getByRole('button', { name: '+ Novo produto' })).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: '+ Novo produto' }));

    await userEvent.type(screen.getByLabelText(/^Código/), '12003');
    await userEvent.type(screen.getByLabelText('Descrição'), 'Bota de segurança');
    await userEvent.selectOptions(screen.getByLabelText('Família'), 'EPI');
    await userEvent.type(screen.getByLabelText(/^Tamanhos/), '38, 39, 40');
    // a tela diz o que vai cadastrar antes do clique
    expect(screen.getByText(/12003-38/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Adicionar produto' }));
    await waitFor(() => expect(criarGradeDeTamanhos).toHaveBeenCalledWith(expect.objectContaining({
      baseCode: '12003', description: 'Bota de segurança', family: 'EPI', sizes: ['38, 39, 40'],
    })));
    expect(criarProduto).not.toHaveBeenCalled();
    expect(screen.getAllByTestId('toast').at(-1)).toHaveTextContent('3 tamanho(s) cadastrado(s)');
  });

  it('sem tamanhos, o cadastro continua sendo de um produto só', async () => {
    vi.mocked(criarProduto).mockResolvedValue(produto({ code: 'LMP-001' }));
    montar();
    await waitFor(() => expect(screen.getByRole('button', { name: '+ Novo produto' })).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: '+ Novo produto' }));

    await userEvent.type(screen.getByLabelText('Descrição'), 'Detergente neutro');
    await userEvent.selectOptions(screen.getByLabelText('Família'), 'EPI');
    await userEvent.click(screen.getByRole('button', { name: 'Adicionar produto' }));

    await waitFor(() => expect(criarProduto).toHaveBeenCalled());
    expect(criarGradeDeTamanhos).not.toHaveBeenCalled();
  });

  it('inativar manda apenas a mudança de situação', async () => {
    vi.mocked(atualizarProduto).mockResolvedValue(produto({}));
    montar();
    await waitFor(() => expect(screen.getByLabelText('Buscar produto')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText('Buscar produto'), 'luva');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(screen.getByTestId('tabela-produtos')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: 'Inativar' })[0]);
    await waitFor(() => expect(atualizarProduto).toHaveBeenCalledWith('p-12003', { active: false }));
  });

  it('o comprador cadastra e edita produto, mas importar e excluir continuam com o gestor', async () => {
    usuarioAtual = comprador;
    montar();
    await waitFor(() => expect(screen.getByLabelText('Buscar produto')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: '+ Novo produto' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Importar planilha' })).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Buscar produto'), 'luva');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    const tabela = within(await screen.findByTestId('tabela-produtos'));
    expect(tabela.getAllByRole('button', { name: 'Editar' }).length).toBeGreaterThan(0);
    expect(tabela.queryByRole('button', { name: 'Excluir' })).not.toBeInTheDocument();
  });

  it('quem não mantém o catálogo busca, mas não cadastra nem edita', async () => {
    usuarioAtual = solicitante;
    montar();
    await waitFor(() => expect(screen.getByLabelText('Buscar produto')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: '+ Novo produto' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Importar planilha' })).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Buscar produto'), 'luva');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(screen.getByTestId('tabela-produtos')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(vi.mocked(buscarProdutos).mock.calls[0][0].incluirInativos).toBe(false);
  });
});
