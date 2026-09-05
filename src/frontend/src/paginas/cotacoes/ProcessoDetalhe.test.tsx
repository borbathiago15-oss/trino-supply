import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { Usuario } from '@/api/auth';
import { acoesDisponiveis, conflitoDeSegregacao, melhorPreco, menorTotal, precoDoItem } from '@/api/cotacoes';
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
  anexarNaProposta: vi.fn(),
}));
vi.mock('@/api/pedidos', async (importar) => ({
  ...(await importar<typeof import('@/api/pedidos')>()),
  anexarOc: vi.fn(),
}));
vi.mock('@/api/fornecedores', async (importar) => ({
  ...(await importar<typeof import('@/api/fornecedores')>()),
  listarFornecedores: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import {
  anexarNaProposta, cancelarProcesso, decidir, encerrarParaAnalise, escolherVencedor,
  lerProcesso, registrarNegociacao, registrarOc, registrarProposta,
} from '@/api/cotacoes';
import { anexarOc } from '@/api/pedidos';
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
  describe('segregação de funções (RFQ-ERR-030)', () => {
    const nivel1 = { conduz: false, aprovaNivel1: true, aprovaNivel2: false };
    const nivel2 = { conduz: false, aprovaNivel1: false, aprovaNivel2: true };
    const escolhidoPor = (by: string) => ({
      winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: null, justification: 'menor preço',
      by, byLabel: 'Carla',
    });

    it('quem escolheu o fornecedor não aprova o Nível 1 da própria escolha', () => {
      const q = processo({ status: 'AGUARDANDO_GERENTE', selection: escolhidoPor('u1') });
      const a = acoesDisponiveis(q, { ...nivel1, de: 'u1' });
      expect(a.decidirNivel1).toBe(false);
      expect(a.conflitoSegregacao).toMatch(/escolheu o fornecedor.*RFQ-ERR-030/);
    });

    it('outra pessoa da mesma alçada aprova normalmente', () => {
      const q = processo({ status: 'AGUARDANDO_GERENTE', selection: escolhidoPor('u1') });
      const a = acoesDisponiveis(q, { ...nivel1, de: 'u2' });
      expect(a.decidirNivel1).toBe(true);
      expect(a.conflitoSegregacao).toBeNull();
    });

    it('quem deu o Nível 1 não dá também o Nível 2', () => {
      const q = processo({
        status: 'AGUARDANDO_DIRETOR', selection: escolhidoPor('u1'),
        managerApproval: { by: 'u2', byLabel: 'Bruno', at: '2026-02-01T10:00:00Z' },
      });
      const a = acoesDisponiveis(q, { ...nivel2, de: 'u2' });
      expect(a.decidirNivel2).toBe(false);
      expect(a.conflitoSegregacao).toMatch(/Nível 1.*RFQ-ERR-030/);
      // e o comprador que escolheu também segue barrado no Nível 2
      expect(acoesDisponiveis(q, { ...nivel2, de: 'u1' }).decidirNivel2).toBe(false);
      // um terceiro decide
      expect(acoesDisponiveis(q, { ...nivel2, de: 'u3' }).decidirNivel2).toBe(true);
    });

    it('sem saber quem está logado a tela não esconde a ação', () => {
      const q = processo({ status: 'AGUARDANDO_GERENTE', selection: escolhidoPor('u1') });
      expect(conflitoDeSegregacao(q, undefined, 'manager')).toBeNull();
      expect(acoesDisponiveis(q, nivel1).decidirNivel1).toBe(true);
    });
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

  it('quem escolheu o fornecedor vê o motivo no lugar dos botões de aprovar', async () => {
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'Approver', modules: ['COMPRAS', 'APROVACAO'] };
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'AGUARDANDO_GERENTE',
      selection: {
        winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: null,
        justification: 'menor preço', by: 'u1', byLabel: 'Carla',
      },
    }));
    abrir();

    expect(await screen.findByTestId('conflito-segregacao')).toHaveTextContent('RFQ-ERR-030');
    expect(screen.queryByTestId('acoes-aprovacao')).not.toBeInTheDocument();
    expect(decidir).not.toHaveBeenCalled();
  });

  it('outro aprovador do mesmo nível decide o processo normalmente', async () => {
    eu = { id: 'u7', email: 'bruno@t.com', name: 'Bruno', role: 'Approver', modules: ['APROVACAO'] };
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'AGUARDANDO_GERENTE',
      selection: {
        winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: null,
        justification: 'menor preço', by: 'u1', byLabel: 'Carla',
      },
    }));
    abrir();

    expect(await screen.findByTestId('acoes-aprovacao')).toBeInTheDocument();
    expect(screen.queryByTestId('conflito-segregacao')).not.toBeInTheDocument();
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

  it('o orçamento recebido por e-mail é anexado à proposta registrada', async () => {
    const usuario = userEvent.setup();
    const comProposta = processo({
      status: 'EM_ANALISE',
      proposals: [proposta({ id: 'p9', supplierId: 's1', supplierName: 'Alfa EPIs' })],
    });
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'COTACAO_ABERTA', proposals: [] }));
    vi.mocked(registrarProposta).mockResolvedValue(comProposta);
    vi.mocked(anexarNaProposta).mockResolvedValue({ documentId: 'd1', fileName: 'orcamento.pdf' });
    abrir();

    const form = await screen.findByTestId('form-proposta');
    await usuario.type(within(form).getByLabelText('Preço unitário de Luva nitrílica'), '12');
    await usuario.upload(within(form).getByLabelText(/Cotação recebida/),
      new File(['x'], 'orcamento.pdf', { type: 'application/pdf' }));
    await usuario.click(within(form).getByRole('button', { name: 'Registrar proposta' }));

    // o anexo vai na proposta que acabou de entrar, achada pelo fornecedor
    await waitFor(() => expect(anexarNaProposta).toHaveBeenCalledWith('q1', 'p9', expect.any(File)));
  });

  it('anexo que falha não desfaz a proposta já registrada', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'COTACAO_ABERTA', proposals: [] }));
    vi.mocked(registrarProposta).mockResolvedValue(processo({
      proposals: [proposta({ id: 'p9', supplierId: 's1' })],
    }));
    vi.mocked(anexarNaProposta).mockRejectedValue(new Error('Arquivo acima de 10 MB.'));
    abrir();

    const form = await screen.findByTestId('form-proposta');
    await usuario.type(within(form).getByLabelText('Preço unitário de Luva nitrílica'), '12');
    await usuario.upload(within(form).getByLabelText(/Cotação recebida/),
      new File(['x'], 'grande.pdf', { type: 'application/pdf' }));
    await usuario.click(within(form).getByRole('button', { name: 'Registrar proposta' }));

    expect(await screen.findByText(/Proposta registrada, mas o anexo falhou/)).toBeInTheDocument();
    expect(registrarProposta).toHaveBeenCalledTimes(1);
  });

  it('o anexo da O.C. entra no pedido que o registro acabou de criar', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'APROVADO_PARA_EMISSAO',
      pendingPoSuppliers: [{ supplierId: 's1', supplierName: 'Alfa', families: ['EPI'], totalValue: 1200 }],
    }));
    vi.mocked(registrarOc).mockResolvedValue(processo({
      purchaseOrders: [{ id: 'po-novo', number: 'PO-1', supplierName: 'Alfa', families: ['EPI'], totalValue: 1200 }],
    }));
    vi.mocked(anexarOc).mockResolvedValue(undefined);
    abrir();

    const form = await screen.findByTestId('form-oc');
    await usuario.type(within(form).getByLabelText('Número da O.C. (ERP)'), '663');
    await usuario.upload(within(form).getByLabelText(/Anexo da O.C./),
      new File(['x'], 'oc.pdf', { type: 'application/pdf' }));
    await usuario.click(within(form).getByRole('button', { name: /Registrar O.C./ }));

    await waitFor(() => expect(anexarOc).toHaveBeenCalledWith('po-novo', expect.any(File)));
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
