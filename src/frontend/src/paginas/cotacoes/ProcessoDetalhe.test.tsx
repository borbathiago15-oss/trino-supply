import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { Usuario } from '@/api/auth';
import { acoesDisponiveis, melhorPreco, menorTotal, precoDoItem } from '@/api/cotacoes';
import { ToastProvider } from '@/componentes/Toast';
import { ProcessoDetalhe, textoDoConvite } from './ProcessoDetalhe';
import { propostasDaFamilia } from './AcoesDoProcesso';
import { montarProposta } from './PainelPropostaManual';
import { processo, proposta } from '@/test/cotacoes';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  lerProcesso: vi.fn(), convidarFornecedor: vi.fn(), encerrarParaAnalise: vi.fn(),
  escolherVencedor: vi.fn(), decidir: vi.fn(), registrarOc: vi.fn(),
  registrarNegociacao: vi.fn(), cancelarProcesso: vi.fn(), registrarProposta: vi.fn(),
}));
vi.mock('@/api/fornecedores', async (importar) => ({
  ...(await importar<typeof import('@/api/fornecedores')>()),
  listarFornecedores: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import {
  cancelarProcesso, decidir, encerrarParaAnalise, escolherVencedor, lerProcesso,
  registrarNegociacao, registrarOc,
} from '@/api/cotacoes';
import { listarFornecedores } from '@/api/fornecedores';

let eu: Usuario = {
  id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'],
};

const abrir = () => render(
  <MemoryRouter initialEntries={['/cotacoes/q1']}>
    <ToastProvider>
      <Routes><Route path="/cotacoes/:id" element={<ProcessoDetalhe />} /></Routes>
    </ToastProvider>
  </MemoryRouter>,
);

describe('leituras do mapa de cotação', () => {
  const q = processo({
    items: [
      { id: 'i1', sequence: 1, catalogCode: null, description: 'Luva', quantity: 10, unitOfMeasure: 'PAR', sourcePrNumber: null, family: 'EPI' },
      { id: 'i2', sequence: 2, catalogCode: null, description: 'Bota', quantity: 5, unitOfMeasure: 'PAR', sourcePrNumber: null, family: 'EPI' },
    ],
    proposals: [
      proposta({ id: 'p1', supplierName: 'Alfa', totalValue: 1000, items: [{ quotationItemId: 'i1', unitPrice: 12, quantity: 10 }] }),
      proposta({ id: 'p2', supplierId: 's2', supplierName: 'Beta', totalValue: 900, items: [{ quotationItemId: 'i1', unitPrice: 10, quantity: 10 }, { quotationItemId: 'i2', unitPrice: 40, quantity: 5 }] }),
      proposta({ id: 'p0', supplierName: 'Alfa', version: 0, isLatest: false, totalValue: 1500, items: [] }),
    ],
  });

  it('só a última versão de cada fornecedor entra na comparação', () => {
    expect(menorTotal(q)).toBe(900);
  });
  it('o melhor preço de cada item sai das propostas vigentes', () => {
    expect(melhorPreco(q, 'i1')).toBe(10);
    expect(melhorPreco(q, 'i2')).toBe(40);
  });
  it('item não cotado por um fornecedor devolve null, não zero', () => {
    expect(precoDoItem(q.proposals[0], 'i2')).toBeNull();
  });
  it('com uma proposta só não há menor total a destacar', () => {
    expect(menorTotal(processo({ proposals: [proposta({})] }))).toBeNull();
  });
});

describe('regras das ações por etapa', () => {
  const conduz = { conduz: true, aprovaNivel1: false, aprovaNivel2: false };
  it('em aberto: convida, registra proposta e encerra; não escolhe vencedor ainda', () => {
    const a = acoesDisponiveis(processo({ status: 'COTACAO_ABERTA' }), conduz);
    expect(a).toMatchObject({ convidar: true, encerrar: true, escolherVencedor: false, registrarOc: false });
  });
  it('em análise: escolhe o vencedor e já não encerra de novo', () => {
    const a = acoesDisponiveis(processo({ status: 'EM_ANALISE' }), conduz);
    expect(a).toMatchObject({ escolherVencedor: true, encerrar: false });
  });
  it('a decisão é de quem tem a alçada da etapa', () => {
    expect(acoesDisponiveis(processo({ status: 'AGUARDANDO_GERENTE' }),
      { conduz: false, aprovaNivel1: true, aprovaNivel2: false }).decidirNivel1).toBe(true);
    expect(acoesDisponiveis(processo({ status: 'AGUARDANDO_GERENTE' }),
      { conduz: false, aprovaNivel1: false, aprovaNivel2: true }).decidirNivel1).toBe(false);
    expect(acoesDisponiveis(processo({ status: 'AGUARDANDO_DIRETOR' }),
      { conduz: false, aprovaNivel1: false, aprovaNivel2: true }).decidirNivel2).toBe(true);
  });
  it('processo encerrado não aceita mais cancelamento', () => {
    for (const status of ['OC_REGISTRADA', 'REJEITADO', 'CANCELADA'])
      expect(acoesDisponiveis(processo({ status }), conduz).cancelar).toBe(false);
    expect(acoesDisponiveis(processo({ status: 'EM_ANALISE' }), conduz).cancelar).toBe(true);
  });
  it('mais de uma família manda escolher fornecedor por família', () => {
    expect(acoesDisponiveis(processo({ families: ['EPI', 'FERRAMENTA'] }), conduz).porFamilia).toBe(true);
  });
  it('só concorre a uma família quem cotou algum item dela', () => {
    const q = processo({
      families: ['EPI', 'FERRAMENTA'],
      items: [
        { id: 'i1', sequence: 1, catalogCode: null, description: 'Luva', quantity: 10, unitOfMeasure: 'PAR', sourcePrNumber: null, family: 'EPI' },
        { id: 'i2', sequence: 2, catalogCode: null, description: 'Furadeira', quantity: 1, unitOfMeasure: 'UN', sourcePrNumber: null, family: 'FERRAMENTA' },
      ],
      proposals: [
        proposta({ id: 'p1', supplierName: 'Alfa', items: [{ quotationItemId: 'i1', unitPrice: 12, quantity: 10 }] }),
        proposta({ id: 'p2', supplierId: 's2', supplierName: 'Beta', items: [{ quotationItemId: 'i2', unitPrice: 300, quantity: 1 }] }),
      ],
    });
    expect(propostasDaFamilia(q, 'EPI').map((p) => p.supplierName)).toEqual(['Alfa']);
    expect(propostasDaFamilia(q, 'FERRAMENTA').map((p) => p.supplierName)).toEqual(['Beta']);
  });
});

describe('proposta lançada à mão', () => {
  it('item sem preço fica de fora — o fornecedor pode não ter cotado tudo', () => {
    const { proposta: p, erro } = montarProposta('s1', { moeda: 'BRL' }, { i1: '12', i2: '' });
    expect(erro).toBeNull();
    expect(p?.items).toEqual([{ quotationItemId: 'i1', unitPrice: 12 }]);
  });
  it('sem nenhum preço não vira chamada', () => {
    expect(montarProposta('s1', {}, { i1: '', i2: '0' }).erro).toBe('Informe o preço de ao menos um item.');
  });
  it('sem fornecedor também não', () => {
    expect(montarProposta('', {}, { i1: '12' }).erro).toBe('Escolha o fornecedor da proposta.');
  });
});

describe('convite ao fornecedor', () => {
  it('traz o número, o prazo e o endereço do portal', () => {
    const texto = textoDoConvite(processo({}), 'https://exemplo.com');
    expect(texto).toContain('RFQ-2026-000001');
    expect(texto).toContain('https://exemplo.com/portal');
    expect(texto).toContain('2026-10-01');
  });
  it('sem prazo definido, diz "a combinar" em vez de deixar em branco', () => {
    expect(textoDoConvite(processo({ deadline: null }), 'https://exemplo.com')).toContain('a combinar');
  });
});

describe('tela do processo', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'] };
    vi.mocked(listarFornecedores).mockResolvedValue([]);
  });

  it('o mapa destaca o melhor preço e o menor total', async () => {
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      proposals: [
        proposta({ id: 'p1', supplierName: 'Alfa', totalValue: 1200, items: [{ quotationItemId: 'i1', unitPrice: 12, quantity: 100 }] }),
        proposta({ id: 'p2', supplierId: 's2', supplierName: 'Beta', totalValue: 1000, items: [{ quotationItemId: 'i1', unitPrice: 10, quantity: 100 }] }),
      ],
    }));
    abrir();
    const mapa = await screen.findByTestId('mapa-cotacao');
    expect(within(mapa).getByText('melhor preço', { exact: false })).toBeInTheDocument();
    expect(within(mapa).getByText('menor total')).toBeInTheDocument();
  });

  it('encerrar para análise pede confirmação antes', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'COTACAO_ABERTA' }));
    vi.mocked(encerrarParaAnalise).mockResolvedValue(processo({ status: 'EM_ANALISE' }));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Encerrar para análise' }));
    const dialogo = screen.getByRole('dialog');
    expect(within(dialogo).getByText(/deixa de aceitar propostas novas/)).toBeInTheDocument();
    await usuario.click(within(dialogo).getByRole('button', { name: 'Encerrar' }));
    await waitFor(() => expect(encerrarParaAnalise).toHaveBeenCalledWith('q1'));
  });

  it('a escolha do vencedor exige proposta marcada e justificativa', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'EM_ANALISE' }));
    vi.mocked(escolherVencedor).mockResolvedValue(processo({}));
    abrir();

    const form = await screen.findByTestId('form-vencedor');
    const confirmar = within(form).getByRole('button', { name: /Confirmar escolha/ });
    expect(confirmar).toBeDisabled();

    await usuario.click(within(form).getByLabelText('Escolher Alfa EPIs'));
    expect(confirmar).toBeDisabled();

    await usuario.type(within(form).getByLabelText(/Justificativa da escolha/), 'menor preço e prazo');
    await usuario.click(within(form).getByLabelText('Preço'));
    await usuario.click(confirmar);

    await waitFor(() => expect(escolherVencedor).toHaveBeenCalledWith('q1', {
      proposalId: 'p1', criteria: ['Preço'], justification: 'menor preço e prazo',
    }));
  });

  it('rejeitar exige motivo; aprovar não', async () => {
    const usuario = userEvent.setup();
    eu = { id: 'u2', email: 'gestor@t.com', name: 'Gestor', role: 'SupplyManager', modules: ['COMPRAS', 'APROVACAO'] };
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'AGUARDANDO_GERENTE' }));
    vi.mocked(decidir).mockResolvedValue(processo({}));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Rejeitar' }));
    let dialogo = screen.getByRole('dialog');
    expect(within(dialogo).getByRole('button', { name: 'Rejeitar' })).toBeDisabled();
    await usuario.click(within(dialogo).getByRole('button', { name: 'Cancelar' }));

    await usuario.click(screen.getByRole('button', { name: 'Aprovar' }));
    dialogo = screen.getByRole('dialog');
    await usuario.click(within(dialogo).getByRole('button', { name: 'Aprovar' }));
    await waitFor(() => expect(decidir).toHaveBeenCalledWith('q1', 'manager', 'APROVAR', null));
  });

  it('a negociação aceita valor fechado ou desconto, mas não nenhum dos dois', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'EM_ANALISE' }));
    vi.mocked(registrarNegociacao).mockResolvedValue(processo({}));
    abrir();

    const form = await screen.findByTestId('form-negociacao');
    await usuario.selectOptions(within(form).getByLabelText('Fornecedor negociado'), 's1');
    await usuario.click(within(form).getByRole('button', { name: 'Registrar negociação' }));
    expect(await screen.findByText(/Informe o valor fechado ou o desconto/)).toBeInTheDocument();
    expect(registrarNegociacao).not.toHaveBeenCalled();

    await usuario.type(within(form).getByLabelText('ou desconto (%)'), '5');
    await usuario.click(within(form).getByRole('button', { name: 'Registrar negociação' }));
    await waitFor(() => expect(registrarNegociacao).toHaveBeenCalledWith('q1', {
      supplierId: 's1', closedValue: null, discountPercent: 5, notes: null,
    }));
  });

  it('compra dividida: uma O.C. por fornecedor, escolhida na hora do registro', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'APROVADO_PARA_EMISSAO', splitAward: true,
      pendingPoSuppliers: [
        { supplierId: 's1', supplierName: 'Alfa', families: ['EPI'], totalValue: 1200 },
        { supplierId: 's2', supplierName: 'Beta', families: ['FERRAMENTA'], totalValue: 800 },
      ],
    }));
    vi.mocked(registrarOc).mockResolvedValue(processo({}));
    abrir();

    const form = await screen.findByTestId('form-oc');
    expect(within(form).getByText(/uma O.C. por fornecedor/)).toBeInTheDocument();
    await usuario.selectOptions(within(form).getByLabelText('Fornecedor desta O.C.'), 's2');
    await usuario.type(within(form).getByLabelText('Número da O.C. (ERP)'), '663');
    await usuario.click(within(form).getByRole('button', { name: /Registrar O.C./ }));

    await waitFor(() => expect(registrarOc).toHaveBeenCalledWith('q1', {
      erpNumber: '663', issuedOn: null, notes: null, supplierId: 's2',
    }));
  });

  it('cancelar o processo exige o motivo', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'EM_ANALISE' }));
    vi.mocked(cancelarProcesso).mockResolvedValue(processo({}));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Cancelar processo' }));
    const dialogo = screen.getByRole('dialog');
    const confirmar = within(dialogo).getByRole('button', { name: 'Cancelar processo' });
    expect(confirmar).toBeDisabled();
    await usuario.type(within(dialogo).getByLabelText(/Motivo do cancelamento/), 'demanda suspensa');
    await usuario.click(confirmar);
    await waitFor(() => expect(cancelarProcesso).toHaveBeenCalledWith('q1', 'demanda suspensa'));
  });

  it('quem não conduz nem aprova vê o processo sem ações', async () => {
    eu = { id: 'u9', email: 'auditor@t.com', name: 'Auditor', role: 'Auditor', modules: ['COMPRAS'] };
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'EM_ANALISE' }));
    abrir();
    expect(await screen.findByText('Nenhuma ação disponível para o seu papel nesta etapa.')).toBeInTheDocument();
    expect(screen.queryByTestId('form-vencedor')).not.toBeInTheDocument();
    expect(screen.queryByTestId('form-negociacao')).not.toBeInTheDocument();
  });

  it('a O.C. registrada leva ao pedido, onde ficam faturamento e entrega', async () => {
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'OC_REGISTRADA',
      purchaseOrders: [{ id: 'po1', number: 'PO-2026-000001', supplierName: 'Alfa', families: ['EPI'], totalValue: 1200 }],
    }));
    abrir();
    const tabela = await screen.findByTestId('ocs-do-processo');
    expect(within(tabela).getByRole('link', { name: 'Faturamento e entrega' })).toHaveAttribute('href', '/pedidos/po1');
  });
});
