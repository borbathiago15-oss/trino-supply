import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CotacaoDoPortal, ItemDaCotacao } from '@/api/portal';
import { montarPropostaDoPortal } from '@/api/portal';
import { ToastProvider } from '@/componentes/Toast';
import { Portal } from './Portal';

vi.mock('@/api/portal', async (importar) => ({
  ...(await importar<typeof import('@/api/portal')>()),
  entrarNoPortal: vi.fn(), minhasCotacoes: vi.fn(), lerCotacao: vi.fn(),
  enviarProposta: vi.fn(), anexarNaProposta: vi.fn(),
}));

import {
  anexarNaProposta, entrarNoPortal, enviarProposta, lerCotacao, minhasCotacoes, sessaoPortal,
} from '@/api/portal';

const item = (p: Partial<ItemDaCotacao>): ItemDaCotacao => ({
  id: 'i1', sequence: 1, description: 'Luva nitrílica', quantity: 100, unitOfMeasure: 'PAR',
  ...p,
});

const cotacao = (p: Partial<CotacaoDoPortal>): CotacaoDoPortal => ({
  id: 'q1', number: 'RFQ-2026-000001', kind: 'COMPRA', open: true, status: 'ABERTA',
  deadline: '2026-10-01', notes: null, createdAt: '2026-09-01T10:00:00Z',
  items: [item({})], myProposals: [],
  ...p,
});

const abrir = () => render(<ToastProvider><Portal /></ToastProvider>);

const entrar = async (usuario: ReturnType<typeof userEvent.setup>) => {
  await usuario.type(screen.getByLabelText('CNPJ / CPF'), '12345678000199');
  await usuario.type(screen.getByLabelText('Chave de acesso'), 'chave-secreta');
  await usuario.click(screen.getByRole('button', { name: 'Entrar no portal' }));
};

describe('proposta do portal', () => {
  const itens = [item({}), item({ id: 'i2', sequence: 2, description: 'Bota' })];

  it('o portal exige preço de todos os itens — proposta parcial não dá para comparar', () => {
    expect(montarPropostaDoPortal(itens, { i1: '12' }, {
      prazoEntrega: '', condicaoPagamento: '', frete: '', validade: '', observacao: '',
    }).erro).toBe('Informe o preço de todos os itens.');
  });
  it('preço zero ou negativo também não passa', () => {
    expect(montarPropostaDoPortal([item({})], { i1: '0' }, {
      prazoEntrega: '', condicaoPagamento: '', frete: '', validade: '', observacao: '',
    }).erro).toBe('Informe o preço de todos os itens.');
  });
  it('com tudo preenchido, monta a proposta e aceita vírgula decimal', () => {
    const { proposta, erro } = montarPropostaDoPortal(itens, { i1: '12,50', i2: '30' }, {
      prazoEntrega: '15', condicaoPagamento: '28 dias', frete: '100', validade: '2026-11-01', observacao: 'ok',
    });
    expect(erro).toBeNull();
    expect(proposta).toMatchObject({
      deliveryDays: 15, paymentTerms: '28 dias', freightValue: 100, validUntil: '2026-11-01', notes: 'ok',
    });
    expect(proposta?.items).toEqual([
      { quotationItemId: 'i1', unitPrice: 12.5, quantity: null },
      { quotationItemId: 'i2', unitPrice: 30, quantity: null },
    ]);
  });
  it('campos opcionais em branco viram null, não zero', () => {
    const { proposta } = montarPropostaDoPortal([item({})], { i1: '10' }, {
      prazoEntrega: '', condicaoPagamento: '', frete: '', validade: '', observacao: '',
    });
    expect(proposta).toMatchObject({ deliveryDays: null, paymentTerms: null, freightValue: null, validUntil: null });
  });
});

describe('Portal do Fornecedor', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    sessaoPortal.token = null;
  });
  afterEach(() => { sessaoPortal.token = null; });

  it('sem sessão, abre no login e não busca cotações', async () => {
    abrir();
    expect(await screen.findByLabelText('CNPJ / CPF')).toBeInTheDocument();
    expect(minhasCotacoes).not.toHaveBeenCalled();
  });

  it('o login errado avisa e mantém o fornecedor na tela de acesso', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrarNoPortal).mockRejectedValue(new Error('CNPJ/CPF ou chave de acesso inválidos'));
    abrir();
    await entrar(usuario);
    expect(await screen.findByText(/chave de acesso inválidos/)).toBeInTheDocument();
    expect(screen.getByLabelText('CNPJ / CPF')).toBeInTheDocument();
  });

  it('entrou: lista as cotações e mostra quem está logado', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrarNoPortal).mockResolvedValue({ id: 's1', name: 'Alfa EPIs', taxId: '12345678000199' });
    vi.mocked(minhasCotacoes).mockResolvedValue([cotacao({}), cotacao({ id: 'q2', number: 'RFQ-2', open: false, status: 'ENCERRADA' })]);
    abrir();
    await entrar(usuario);

    const tabela = await screen.findByTestId('cotacoes-do-portal');
    expect(within(tabela).getByText('ABERTA')).toBeInTheDocument();
    expect(within(tabela).getByText('ENCERRADA')).toBeInTheDocument();
    expect(screen.getByText('Alfa EPIs')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sair' })).toBeInTheDocument();
  });

  it('a cotação encerrada não oferece o formulário de proposta', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrarNoPortal).mockResolvedValue({ id: 's1', name: 'Alfa', taxId: '1' });
    vi.mocked(minhasCotacoes).mockResolvedValue([cotacao({ open: false, status: 'ENCERRADA' })]);
    vi.mocked(lerCotacao).mockResolvedValue(cotacao({ open: false, status: 'ENCERRADA' }));
    abrir();
    await entrar(usuario);
    await usuario.click(await screen.findByRole('button', { name: 'Abrir' }));

    expect(await screen.findByTestId('itens-solicitados')).toBeInTheDocument();
    expect(screen.queryByTestId('form-precos')).not.toBeInTheDocument();
    expect(screen.getByText('Nenhuma proposta enviada ainda.')).toBeInTheDocument();
  });

  it('envia a proposta e relê a cotação para mostrar a nova versão', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrarNoPortal).mockResolvedValue({ id: 's1', name: 'Alfa', taxId: '1' });
    vi.mocked(minhasCotacoes).mockResolvedValue([cotacao({})]);
    vi.mocked(lerCotacao).mockResolvedValue(cotacao({}));
    vi.mocked(enviarProposta).mockResolvedValue({ id: 'p1', version: 1, totalValue: 1250 });
    abrir();
    await entrar(usuario);
    await usuario.click(await screen.findByRole('button', { name: 'Abrir' }));

    await usuario.type(await screen.findByLabelText('Preço unitário de Luva nitrílica'), '12.50');
    await usuario.type(screen.getByLabelText('Prazo de entrega (dias)'), '15');
    await usuario.click(screen.getByRole('button', { name: 'Enviar proposta' }));

    await waitFor(() => expect(enviarProposta).toHaveBeenCalledWith('q1', expect.objectContaining({
      deliveryDays: 15, items: [{ quotationItemId: 'i1', unitPrice: 12.5, quantity: null }],
    })));
    expect(await screen.findByText(/Proposta v1 enviada/)).toBeInTheDocument();
    // a releitura mantém a tela em dia sem exigir um "atualizar" do fornecedor
    await waitFor(() => expect(lerCotacao).toHaveBeenCalledTimes(2));
  });

  it('preço faltando avisa e não chega à API', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrarNoPortal).mockResolvedValue({ id: 's1', name: 'Alfa', taxId: '1' });
    vi.mocked(minhasCotacoes).mockResolvedValue([cotacao({})]);
    vi.mocked(lerCotacao).mockResolvedValue(cotacao({ items: [item({}), item({ id: 'i2', sequence: 2, description: 'Bota' })] }));
    abrir();
    await entrar(usuario);
    await usuario.click(await screen.findByRole('button', { name: 'Abrir' }));

    await usuario.type(await screen.findByLabelText('Preço unitário de Luva nitrílica'), '12');
    await usuario.click(screen.getByRole('button', { name: 'Enviar proposta' }));

    expect(await screen.findByText('Informe o preço de todos os itens.')).toBeInTheDocument();
    expect(enviarProposta).not.toHaveBeenCalled();
  });

  it('anexo que falha não apaga a proposta já enviada', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrarNoPortal).mockResolvedValue({ id: 's1', name: 'Alfa', taxId: '1' });
    vi.mocked(minhasCotacoes).mockResolvedValue([cotacao({})]);
    vi.mocked(lerCotacao).mockResolvedValue(cotacao({}));
    vi.mocked(enviarProposta).mockResolvedValue({ id: 'p1', version: 2, totalValue: 1250 });
    vi.mocked(anexarNaProposta).mockRejectedValue(new Error('Arquivo acima de 10 MB.'));
    abrir();
    await entrar(usuario);
    await usuario.click(await screen.findByRole('button', { name: 'Abrir' }));

    await usuario.type(await screen.findByLabelText('Preço unitário de Luva nitrílica'), '12.50');
    await usuario.upload(screen.getByLabelText(/Anexo da proposta comercial/),
      new File(['x'], 'orcamento.pdf', { type: 'application/pdf' }));
    await usuario.click(screen.getByRole('button', { name: 'Enviar proposta' }));

    expect(await screen.findByText(/Proposta v2 enviada, mas o anexo falhou/)).toBeInTheDocument();
    expect(enviarProposta).toHaveBeenCalledTimes(1);
  });

  it('sair limpa a sessão e volta ao login', async () => {
    const usuario = userEvent.setup();
    vi.mocked(entrarNoPortal).mockResolvedValue({ id: 's1', name: 'Alfa', taxId: '1' });
    vi.mocked(minhasCotacoes).mockResolvedValue([]);
    abrir();
    await entrar(usuario);
    await screen.findByRole('button', { name: 'Sair' });

    await usuario.click(screen.getByRole('button', { name: 'Sair' }));
    expect(screen.getByLabelText('CNPJ / CPF')).toBeInTheDocument();
    expect(sessaoPortal.ativa).toBe(false);
  });
});
