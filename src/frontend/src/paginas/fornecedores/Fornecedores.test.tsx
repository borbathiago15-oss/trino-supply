import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { Fornecedor } from '@/api/fornecedores';
import { ToastProvider } from '@/componentes/Toast';
import { Fornecedores } from './Fornecedores';
import { situacaoDocumento } from './PainelHomologacao';

vi.mock('@/api/fornecedores', async (importar) => ({
  ...(await importar<typeof import('@/api/fornecedores')>()),
  buscarFornecedores: vi.fn(),
  gerarChavePortal: vi.fn(),
  atualizarFornecedor: vi.fn(),
}));
let usuarioAtual: Usuario;
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => usuarioAtual }));

import { atualizarFornecedor, buscarFornecedores, gerarChavePortal } from '@/api/fornecedores';

const fornecedor = (p: Partial<Fornecedor>): Fornecedor => ({
  id: 'id-' + (p.taxId ?? '1'), legalName: 'Alfa Equipamentos LTDA', tradeName: 'Alfa EPIs',
  taxId: '12345678000199', email: 'vendas@alfa.com.br', phone: '11 4000-0000', active: true,
  homologationStatus: 'HOMOLOGADO', effectiveHomologation: 'HOMOLOGADO', documents: [],
  contract: { number: null, validFrom: null, validUntil: null, notes: null, valueLimit: null, consumed: null, balance: null, current: false, items: [] },
  ...p,
});

const lista = [
  fornecedor({ taxId: '12345678000199' }),
  fornecedor({
    taxId: '98765432000155', legalName: 'Beta Química S.A.', tradeName: 'Beta', active: false,
    homologationStatus: 'HOMOLOGADO', effectiveHomologation: 'RESTRITO',
  }),
];

const comprador: Usuario = { id: 'u1', email: 'c@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['FORNECEDORES'] };
const auditor: Usuario = { id: 'u2', email: 'a@t.com', name: 'Auditor', role: 'Auditor', modules: ['FORNECEDORES'] };

const pagina = (itens: Fornecedor[], total = itens.length) => ({ itens, total });

describe('situacaoDocumento', () => {
  it('distingue vencida, vencendo, válida e sem prazo', () => {
    expect(situacaoDocumento({ expired: true, expiringDays: -3, validUntil: '2026-01-01' })?.rotulo).toBe('VENCIDA');
    expect(situacaoDocumento({ expired: false, expiringDays: 10, validUntil: '2026-09-12' })?.rotulo).toBe('vence em 10d');
    expect(situacaoDocumento({ expired: false, expiringDays: 200, validUntil: '2027-03-01' })?.rotulo).toBe('VÁLIDA');
    expect(situacaoDocumento({ expired: false, expiringDays: null, validUntil: null })).toBeNull();
  });
});

describe('<Fornecedores />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    usuarioAtual = comprador;
    vi.mocked(buscarFornecedores).mockResolvedValue(pagina(lista));
  });

  const montar = () => render(<ToastProvider><Fornecedores /></ToastProvider>);

  it('mostra a homologação efetiva e explica a restrição por certidão', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-fornecedores')).toBeInTheDocument());
    const tabela = within(screen.getByTestId('tabela-fornecedores'));
    expect(tabela.getByText('Homologado')).toBeInTheDocument();
    expect(tabela.getByText('Restrito')).toBeInTheDocument();
    expect(tabela.getByText('certidão vencida')).toBeInTheDocument();
    expect(tabela.getAllByText('sem contrato')).toHaveLength(2);
    // contato aparece junto da identificação, sem coluna própria
    expect(tabela.getAllByText(/vendas@alfa\.com\.br · 11 4000-0000/)).toHaveLength(2);
  });

  it('editar trava razão social e CNPJ já gravado, que a API não altera', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-fornecedores')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: 'Editar' })[0]);
    expect(screen.getByLabelText('Razão social')).toBeDisabled();
    expect(screen.getByLabelText(/CNPJ\/CPF/)).toBeDisabled();
    expect(screen.getByLabelText('Nome fantasia')).toHaveValue('Alfa EPIs');
    expect(screen.getByRole('button', { name: 'Salvar alterações' })).toBeInTheDocument();
  });

  it('pré-cadastro sem CNPJ aparece como tal e a edição completa o documento (§7)', async () => {
    // o fornecedor que entrou pela cotação chega aqui sem documento; é nesta tela que
    // ele vira cadastro completo — e só então pode ser homologado (SUP-ERR-013)
    vi.mocked(buscarFornecedores).mockResolvedValue({
      itens: [fornecedor({ taxId: null, legalName: 'Gama Distribuidora LTDA', tradeName: null,
        homologationStatus: 'PROSPECT', effectiveHomologation: 'PROSPECT' })],
      total: 1,
    });
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-fornecedores')).toBeInTheDocument());
    expect(within(screen.getByTestId('tabela-fornecedores')).getByText('pré-cadastro')).toBeInTheDocument();

    await userEvent.click(screen.getAllByRole('button', { name: 'Editar' })[0]);
    const cnpj = screen.getByLabelText(/CNPJ\/CPF/);
    expect(cnpj).toBeEnabled();          // falta o documento: aqui ele entra
    await userEvent.type(cnpj, '11222333000181');
    await userEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }));

    await waitFor(() => expect(atualizarFornecedor).toHaveBeenCalledWith(
      expect.any(String), expect.objectContaining({ taxId: '11222333000181' })));
  });

  it('a chave do portal só é gerada depois de confirmar, e aparece uma vez', async () => {
    vi.mocked(gerarChavePortal).mockResolvedValue({ accessKey: 'CHAVE-SECRETA-123' });
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-fornecedores')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: /Mais ações de/ })[0]);
    await userEvent.click(screen.getByRole('menuitem', { name: 'Chave do portal' }));
    expect(gerarChavePortal).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Gerar nova chave' }));
    await waitFor(() => expect(screen.getByTestId('chave-portal')).toHaveTextContent('CHAVE-SECRETA-123'));
  });

  it('a busca vai para o servidor, e a tela diz quantos existem', async () => {
    vi.mocked(buscarFornecedores).mockResolvedValue(pagina(lista, 480));
    usuarioAtual = comprador;
    montar();
    await waitFor(() => expect(screen.getByTestId('contagem-fornecedores'))
      .toHaveTextContent('Mostrando 2 de 480'));

    await userEvent.type(screen.getByLabelText('Buscar'), '12.345');
    await waitFor(() => expect(buscarFornecedores).toHaveBeenCalledWith(
      expect.objectContaining({ busca: '12.345' }), expect.anything()));

    // e oferece o resto em vez de fingir que a lista acabou
    await userEvent.click(screen.getByRole('button', { name: 'Carregar mais' }));
    await waitFor(() => expect(buscarFornecedores).toHaveBeenCalledWith(
      expect.objectContaining({ tamanho: 100 }), expect.anything()));
  });

  it('auditor vê a lista sem ações nem formulário', async () => {
    usuarioAtual = auditor;
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-fornecedores')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Homologação' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Mais ações de/ })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Razão social')).not.toBeInTheDocument();
    // auditor não mantém cadastro: a consulta não pede os inativos
    expect(vi.mocked(buscarFornecedores).mock.calls[0][0]).toMatchObject({ incluirInativos: false });
  });
  it('trocar de fornecedor troca o painel: o homologado não aparece como prospect', async () => {
    // o painel nasce com o estado lido da prop uma vez só, e a lista continua na tela
    // acima dele. Sem `key`, abrir a homologação de outro fornecedor trocava a prop sem
    // desmontar o painel: o select seguia na situação do anterior, e "Salvar situação"
    // gravava isso no fornecedor errado
    vi.mocked(buscarFornecedores).mockResolvedValue(pagina([
      fornecedor({ taxId: null, legalName: 'Pontes Tour', tradeName: null,
        homologationStatus: 'PROSPECT', effectiveHomologation: 'PROSPECT' }),
      fornecedor({ taxId: '98765432000155', legalName: 'Beta Química S.A.', tradeName: 'Beta' }),
    ]));
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-fornecedores')).toBeInTheDocument());

    await userEvent.click(screen.getAllByRole('button', { name: 'Homologação' })[0]);
    expect(screen.getByLabelText(/Situação da homologação/)).toHaveValue('PROSPECT');

    await userEvent.click(screen.getAllByRole('button', { name: 'Homologação' })[1]);
    expect(screen.getByText(/Homologação — Beta Química S\.A\./)).toBeInTheDocument();
    expect(screen.getByLabelText(/Situação da homologação/)).toHaveValue('HOMOLOGADO');
  });

  it('trocar de fornecedor não carrega o contrato do anterior', async () => {
    // aqui a consequência era pior que um rótulo errado: salvar copiava número, teto,
    // vigência e itens de um fornecedor para outro
    const comContrato = fornecedor({ taxId: '11111111000111', legalName: 'Alfa Equipamentos LTDA' });
    comContrato.contract = { ...comContrato.contract, number: 'CT-2026-001', valueLimit: 50000 };
    vi.mocked(buscarFornecedores).mockResolvedValue(pagina([
      comContrato,
      fornecedor({ taxId: '98765432000155', legalName: 'Beta Química S.A.', tradeName: 'Beta' }),
    ]));
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-fornecedores')).toBeInTheDocument());

    await userEvent.click(screen.getAllByRole('button', { name: /Mais ações de/ })[0]);
    await userEvent.click(screen.getByRole('menuitem', { name: 'Contrato de parceria' }));
    expect(screen.getByLabelText(/Número do contrato/)).toHaveValue('CT-2026-001');

    await userEvent.click(screen.getAllByRole('button', { name: /Mais ações de/ })[1]);
    await userEvent.click(screen.getByRole('menuitem', { name: 'Contrato de parceria' }));
    expect(screen.getByLabelText(/Número do contrato/)).toHaveValue('');
  });

  it('a vigência do contrato não aceita fim antes do início (SUP-ERR-020)', async () => {
    // o servidor recusa; o campo já não deixa escolher, em vez de avisar no salvar
    usuarioAtual = comprador;
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-fornecedores')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: /Mais ações de/ })[0]);
    await userEvent.click(screen.getByRole('menuitem', { name: 'Contrato de parceria' }));

    const fim = screen.getByLabelText(/Fim da vigência/);
    expect(fim).not.toHaveAttribute('min');          // sem início, nada a limitar
    await userEvent.type(screen.getByLabelText('Início da vigência'), '2026-03-01');
    expect(fim).toHaveAttribute('min', '2026-03-01');
  });
});
