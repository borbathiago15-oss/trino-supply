import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { Usuario } from '@/api/auth';
import {
  acoesDisponiveis, conflitoDeSegregacao, impedimentoDaOferta, melhorPreco, menorTotal, precoDoItem,
} from '@/api/cotacoes';
import { ToastProvider } from '@/componentes/Toast';
import { ProcessoDetalhe } from './ProcessoDetalhe';
import { textoDoConvite } from './PainelDeConvidados';
import { candidatasDaFamilia, impedimentoDaProposta, propostasDaFamilia } from './AcoesDoProcesso';
import { daCondicao, doContrato, montarProposta } from './PainelPropostaManual';
import { lote, oferta, processo, proposta } from '@/test/cotacoes';
import { historicoDoProcesso, mapaDeScore, precosDeContrato, variacaoDoPreco } from '@/api/cotacoes';
import { listarCondicoesDePagamento, listarFormasDePagamento } from '@/api/pagamentos';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  lerProcesso: vi.fn(), convidarFornecedor: vi.fn(), encerrarParaAnalise: vi.fn(),
  escolherVencedor: vi.fn(), decidir: vi.fn(), registrarOc: vi.fn(),
  registrarNegociacao: vi.fn(), cancelarProcesso: vi.fn(), registrarProposta: vi.fn(),
  anexarNaProposta: vi.fn(), mapaDeFamilias: vi.fn(), precosDeContrato: vi.fn(),
  historicoDoProcesso: vi.fn(), mapaDeScore: vi.fn(), definirProdutoDoItem: vi.fn(),
}));
vi.mock('@/api/catalogo', async (importar) => ({
  ...(await importar<typeof import('@/api/catalogo')>()),
  buscarProdutos: vi.fn(),
}));
vi.mock('@/api/familias', async (importar) => ({
  ...(await importar<typeof import('@/api/familias')>()),
  listarFamilias: vi.fn(),
}));
describe('preço vindo do contrato de parceria', () => {
  const cobertura = (over = {}) => ({
    current: true, contractNumber: 'CT-2026-001', validUntil: '2026-12-31',
    items: [
      { quotationItemId: 'i-pvc', description: 'BOTA BIQUEIRA DE PVC', unitPrice: 45,
        deliveryDays: 7, paymentTerms: '30 dias', paymentDays: 30 },
      { quotationItemId: 'i-aco', description: 'BOTA BIQUEIRA DE AÇO', unitPrice: 72,
        deliveryDays: 10, paymentTerms: '30 dias', paymentDays: 30 },
    ],
    ...over,
  });

  it('preenche o preço de cada item coberto, e o prazo e a condição do contrato', () => {
    const { precos, campos } = doContrato(cobertura(), {}, {});
    expect(precos).toEqual({ 'i-pvc': '45', 'i-aco': '72' });
    expect(campos.prazoEntrega).toBe('7');
    expect(campos.condicaoPagamento).toBe('30 dias');
    expect(campos.prazoPagamento).toBe('30');
  });

  it('não escreve por cima do que o comprador já digitou', () => {
    // ele pode ter fechado melhor que o contrato; apagar seria o formulário
    // desfazendo a negociação que acabou de ser feita
    const { precos, campos } = doContrato(cobertura(), { 'i-pvc': '40' }, { prazoEntrega: '3' });
    expect(precos['i-pvc']).toBe('40');
    expect(precos['i-aco']).toBe('72');
    expect(campos.prazoEntrega).toBe('3');
  });

  it('contrato fora da vigência não preenche nada', () => {
    // preço vencido entrando calado é pior que campo vazio: fecharia por valor que não vale
    expect(doContrato(cobertura({ current: false }), {}, {})).toEqual({ precos: {}, campos: {} });
  });

  it('sem contrato, o formulário segue como era', () => {
    expect(doContrato(null, { 'i-pvc': '9' }, {})).toEqual({ precos: { 'i-pvc': '9' }, campos: {} });
    expect(doContrato(cobertura({ items: [] }), {}, {})).toEqual({ precos: {}, campos: {} });
  });

  it('preço zero do contrato ainda é preço e é preenchido', () => {
    const c = cobertura({ items: [{ quotationItemId: 'i-x', description: 'Brinde',
      unitPrice: 0, deliveryDays: null, paymentTerms: null, paymentDays: null }] });
    expect(doContrato(c, {}, {}).precos).toEqual({ 'i-x': '0' });
  });
});

vi.mock('@/api/pagamentos', () => ({
  listarFormasDePagamento: vi.fn().mockResolvedValue([]),
  listarCondicoesDePagamento: vi.fn().mockResolvedValue([]),
}));
vi.mock('@/api/pedidos', async (importar) => ({
  ...(await importar<typeof import('@/api/pedidos')>()),
  anexarOc: vi.fn(),
}));
vi.mock('@/api/fornecedores', async (importar) => ({
  ...(await importar<typeof import('@/api/fornecedores')>()),
  listarFornecedores: vi.fn(), criarFornecedor: vi.fn(), acharFornecedor: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import {
  anexarNaProposta, cancelarProcesso, decidir, encerrarParaAnalise, escolherVencedor,
  convidarFornecedor, definirProdutoDoItem, lerProcesso, mapaDeFamilias, registrarNegociacao, registrarOc, registrarProposta,
} from '@/api/cotacoes';
import { buscarProdutos } from '@/api/catalogo';
import { listarFamilias } from '@/api/familias';
import { anexarOc } from '@/api/pedidos';
import { acharFornecedor, criarFornecedor, listarFornecedores } from '@/api/fornecedores';

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
      { id: 'i1', sequence: 1, catalogItemId: null, catalogCode: null, description: 'Luva', quantity: 10, unitOfMeasure: 'PAR', sourcePrNumber: null, family: 'EPI' },
      { id: 'i2', sequence: 2, catalogItemId: null, catalogCode: null, description: 'Bota', quantity: 5, unitOfMeasure: 'PAR', sourcePrNumber: null, family: 'EPI' },
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

    it('quem escolheu o fornecedor dá o Nível 1 da própria escolha: a segregação fica no Nível 2', () => {
      const q = processo({ status: 'AGUARDANDO_GERENTE', selection: escolhidoPor('u1') });
      const a = acoesDisponiveis(q, { ...nivel1, de: 'u1' });
      expect(a.decidirNivel1).toBe(true);
      expect(a.conflitoSegregacao).toBeNull();
    });

    it('quem escolheu o fornecedor não dá o Nível 2', () => {
      const q = processo({
        status: 'AGUARDANDO_DIRETOR', selection: escolhidoPor('u1'),
        managerApproval: { by: 'u1', byLabel: 'Carla', at: '2026-02-01T10:00:00Z' },
      });
      const a = acoesDisponiveis(q, { ...nivel2, de: 'u1' });
      expect(a.decidirNivel2).toBe(false);
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
  it('o item sem produto trava a escolha da compra, mas não a do orçamento, que é cobrado ao converter', () => {
    const digitado = (over = {}) => {
      const q = processo({ status: 'EM_ANALISE', ...over });
      q.items = q.items.map((i) => ({ ...i, catalogItemId: null }));
      return q;
    };
    expect(acoesDisponiveis(digitado(), conduz).produtoPendente).toHaveLength(1);
    expect(acoesDisponiveis(digitado({ isBudget: true, budgetConvertedAt: null }), conduz).produtoPendente).toEqual([]);
    expect(acoesDisponiveis(digitado({ isBudget: true, status: 'ORCAMENTO_APRESENTADO' }), conduz).produtoPendente).toHaveLength(1);
    // com produto, nada pendente
    expect(acoesDisponiveis(processo({ status: 'EM_ANALISE' }), conduz).produtoPendente).toEqual([]);
    // define o produto até a escolha; depois dela, não
    expect(acoesDisponiveis(digitado(), conduz).definirProduto).toBe(true);
    expect(acoesDisponiveis(digitado({ status: 'AGUARDANDO_GERENTE' }), conduz).definirProduto).toBe(false);
  });

  it('mais de um item manda escolher na grade item × fornecedor', () => {
    // o critério deixou de ser a família: a divisão passou a poder acontecer DENTRO de
    // uma família só (o papel com um fornecedor, a caneta com outro)
    const item = (id: string, description: string) => ({
      id, sequence: 1, catalogItemId: null, catalogCode: null, description, quantity: 10,
      unitOfMeasure: 'UN', sourcePrNumber: null, family: 'MATERIAL DE ESCRITORIO',
    });
    const doisItens = processo({
      families: ['MATERIAL DE ESCRITORIO'],
      items: [item('i1', 'Papel ofício A4'), item('i2', 'Caixa de caneta')],
    });
    expect(acoesDisponiveis(doisItens, conduz).porItem).toBe(true);
    // um item só não vira grade: a escolha simples diz mais, com prazo e condição
    expect(acoesDisponiveis(processo({ items: [item('i1', 'Papel')] }), conduz).porItem).toBe(false);
  });
  it('só concorre a uma família quem cotou a família inteira', () => {
    const q = processo({
      families: ['EPI', 'FERRAMENTA'],
      items: [
        { id: 'i1', sequence: 1, catalogItemId: null, catalogCode: null, description: 'Luva', quantity: 10, unitOfMeasure: 'PAR', sourcePrNumber: null, family: 'EPI' },
        { id: 'i2', sequence: 2, catalogItemId: null, catalogCode: null, description: 'Furadeira', quantity: 1, unitOfMeasure: 'UN', sourcePrNumber: null, family: 'FERRAMENTA' },
      ],
      proposals: [
        proposta({ id: 'p1', supplierName: 'Alfa', items: [{ quotationItemId: 'i1', unitPrice: 12, quantity: 10 }] }),
        proposta({ id: 'p2', supplierId: 's2', supplierName: 'Beta', items: [{ quotationItemId: 'i2', unitPrice: 300, quantity: 1 }] }),
      ],
    });
    expect(propostasDaFamilia(q, 'EPI').map((p) => p.supplierName)).toEqual(['Alfa']);
    expect(propostasDaFamilia(q, 'FERRAMENTA').map((p) => p.supplierName)).toEqual(['Beta']);
  });

  describe('quem pode levar a família', () => {
    // a mesma régua do servidor, na ordem em que ele verifica
    it('meia cotação não leva o lote (RFQ-ERR-024)', () => {
      expect(impedimentoDaOferta(oferta({ complete: false, canWin: false })))
        .toContain('não cotou a família inteira');
    });
    it('fornecedor inativo não leva o lote (RFQ-ERR-040)', () => {
      expect(impedimentoDaOferta(oferta({ active: false, canWin: false }))).toContain('inativo');
    });
    it('fornecedor sem homologação não leva o lote (SUP-ERR-030)', () => {
      const barrado = impedimentoDaOferta(oferta({ homologation: 'EM_HOMOLOGACAO', canWin: false }))!;
      expect(barrado).toContain('Em homologação');
      expect(barrado).toContain('SUP-ERR-030');
    });
    it('homologado e com a família inteira cotada pode vencer', () => {
      expect(impedimentoDaOferta(oferta({}))).toBeNull();
    });

    it('a adjudicação só oferece quem pode vencer, e diz por que o outro ficou de fora', () => {
      const q = processo({ families: ['EPI'] });
      const candidatas = candidatasDaFamilia(q, 'EPI', lote({
        offers: [
          oferta({ proposalId: 'p1', supplierName: 'Alfa' }),
          oferta({ proposalId: 'p2', supplierName: 'Beta', homologation: 'PROSPECT', canWin: false }),
        ],
      }));
      expect(candidatas.filter((c) => !c.impedimento).map((c) => c.supplierName)).toEqual(['Alfa']);
      expect(candidatas.find((c) => c.supplierName === 'Beta')!.impedimento).toContain('SUP-ERR-030');
    });

    it('sem o mapa carregado a tela não esconde ninguém — quem barra é a API', () => {
      // leitura do mapa pode falhar; esconder a opção por engano é pior do que
      // deixar o servidor recusar com a mensagem dele
      const q = processo({ families: ['EPI'] });
      expect(candidatasDaFamilia(q, 'EPI', null).map((c) => c.impedimento)).toEqual([null]);
    });
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

  it('a forma escolhida vai junto da condição — são o "como" e o "quando"', () => {
    const { proposta: p } = montarProposta(
      's1',
      { condicaoPagamento: 'Parcelado 30/60/90', formaPagamento: 'Boleto Bancário', prazoPagamento: '30' },
      { i1: '12' },
    );
    expect(p?.paymentTerms).toBe('Parcelado 30/60/90');
    expect(p?.paymentMethodName).toBe('Boleto Bancário');
    expect(p?.paymentDays).toBe(30);
  });

  it('condição sem forma não inventa forma: o campo vazio vira nulo, não texto', () => {
    const { proposta: p } = montarProposta('s1', { condicaoPagamento: 'À Vista' }, { i1: '12' });
    expect(p?.paymentMethodName).toBeNull();
  });
});

describe('condição escolhida preenche o prazo', () => {
  const condicao = (parcelas: number, primeira: number | null) => ({
    id: 'c1', name: 'Parcelado 30/60/90', installments: parcelas,
    firstDueDays: primeira, isDefault: false, active: true,
  });

  it('traz o prazo da primeira parcela — é o número que se redigitava a cada cotação', () => {
    expect(daCondicao(condicao(3, 30))).toEqual({ condicaoPagamento: 'Parcelado 30/60/90', prazoPagamento: '30' });
    // à vista é zero dia, e zero é um prazo: não pode virar "sem prazo"
    expect(daCondicao(condicao(1, 0)).prazoPagamento).toBe('0');
  });

  it('condição que não define prazo não apaga o que o comprador já digitou', () => {
    // devolver prazoPagamento: '' aqui limparia o campo ao trocar a condição
    expect(daCondicao(condicao(2, null))).toEqual({ condicaoPagamento: 'Parcelado 30/60/90' });
  });
});

describe('impedimento da proposta', () => {
  // Foi o bug que a negociação expunha: registrar a negociação cria uma versão NOVA
  // da proposta, o mapa de famílias continuava sendo o da versão anterior, e a tela
  // dizia que o fornecedor recém-negociado "não cotou nenhum item desta compra" —
  // travando o rádio de quem acabara de fechar o melhor preço.
  const loteCom = (offers: Parameters<typeof oferta>[0][]) =>
    lote({ offers: offers.map(oferta) });

  it('sem mapa carregado nada é barrado: quem decide é a API', () => {
    expect(impedimentoDaProposta(null, proposta({}))).toBeNull();
  });

  it('proposta mais nova que o mapa não é barrada por "não cotou nada"', () => {
    // o mapa nem conhece este fornecedor: é mapa atrasado, não ausência de oferta
    const mapa = loteCom([{ supplierId: 'outro', proposalId: 'p-outro' }]);
    expect(impedimentoDaProposta(mapa, proposta({ id: 'p-nova', supplierId: 's1' }))).toBeNull();
  });

  it('fornecedor no mapa sem esta proposta continua sendo "não cotou nada"', () => {
    const mapa = loteCom([{ supplierId: 's1', proposalId: 'p-antiga' }]);
    expect(impedimentoDaProposta(mapa, proposta({ id: 'p1', supplierId: 's1' })))
      .toBe('não cotou nenhum item desta compra');
  });

  it('a homologação pendente segue barrando, que é a regra de verdade (SUP-ERR-030)', () => {
    const mapa = loteCom([{ supplierId: 's1', proposalId: 'p1', homologation: 'PROSPECT', canWin: false }]);
    expect(impedimentoDaProposta(mapa, proposta({ id: 'p1', supplierId: 's1' })))
      .toContain('SUP-ERR-030');
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
    // ninguém no cadastro com este nome: o pré-cadastro segue criando
    vi.mocked(acharFornecedor).mockResolvedValue(null);
    vi.mocked(mapaDeFamilias).mockResolvedValue([lote({})]);
    // o resetAllMocks acima apaga a implementação vinda da fábrica do vi.mock:
    // sem repor aqui, o painel de proposta chamaria `.then` em undefined
    vi.mocked(listarFormasDePagamento).mockResolvedValue([]);
    vi.mocked(listarCondicoesDePagamento).mockResolvedValue([]);
    vi.mocked(precosDeContrato).mockResolvedValue(
      { current: false, contractNumber: null, validUntil: null, items: [] });
    vi.mocked(historicoDoProcesso).mockResolvedValue({ warnAbovePct: 10, items: [] });
    vi.mocked(mapaDeScore).mockResolvedValue({ note: '', criteria: [], items: [] });
  });

  it('fornecedor fora do cadastro entra na cotação só com razão social e telefone', async () => {
    // §7: o comprador pede preço por telefone antes de existir cadastro, e nessa hora
    // o CNPJ ele não tem. O pré-cadastro nasce PROSPECT e concorre; o cadastro completo
    // é cobrado de quem ganhar o BID
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({}));
    vi.mocked(criarFornecedor).mockResolvedValue({ id: 's9' } as Awaited<ReturnType<typeof criarFornecedor>>);
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Fornecedor fora do cadastro' }));
    await usuario.type(screen.getByLabelText('Razão social'), 'Gama Distribuidora LTDA');
    await usuario.type(screen.getByLabelText('Telefone'), '(81) 98888-1234');
    await usuario.click(screen.getByRole('button', { name: 'Incluir na cotação' }));

    // sem CNPJ o cadastro vai com `taxId` nulo, e o convite sai no mesmo gesto
    await waitFor(() => expect(criarFornecedor).toHaveBeenCalledWith({
      legalName: 'Gama Distribuidora LTDA', tradeName: null, taxId: null,
      email: null, phone: '(81) 98888-1234',
    }));
    // sem prazo digitado, o convite vai sem prazo próprio e vale o do processo
    await waitFor(() => expect(convidarFornecedor).toHaveBeenCalledWith('q1', ['s9'], null));
  });

  it('com CNPJ informado, o pré-cadastro já vai com o documento em dígitos', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({}));
    vi.mocked(criarFornecedor).mockResolvedValue({ id: 's9' } as Awaited<ReturnType<typeof criarFornecedor>>);
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Fornecedor fora do cadastro' }));
    await usuario.type(screen.getByLabelText('Razão social'), 'Gama Distribuidora LTDA');
    await usuario.type(screen.getByLabelText('Telefone'), '81 3333-1000');
    await usuario.type(screen.getByLabelText('CNPJ (opcional)'), '11.222.333/0001-81');
    await usuario.click(screen.getByRole('button', { name: 'Incluir na cotação' }));

    await waitFor(() => expect(criarFornecedor).toHaveBeenCalledWith({
      legalName: 'Gama Distribuidora LTDA', tradeName: null, taxId: '11222333000181',
      email: null, phone: '81 3333-1000',
    }));
  });

  it('pré-cadastro sem telefone não chega a criar fornecedor nenhum', async () => {
    // o telefone é o mínimo do §7: sem ele o comprador não tem como voltar a falar
    // com quem cotou, e a API recusaria com SUP-ERR-014
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({}));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Fornecedor fora do cadastro' }));
    await usuario.type(screen.getByLabelText('Razão social'), 'Gama Distribuidora LTDA');
    await usuario.click(screen.getByRole('button', { name: 'Incluir na cotação' }));

    expect(criarFornecedor).not.toHaveBeenCalled();
    expect(convidarFornecedor).not.toHaveBeenCalled();
  });

  it('CNPJ informado errado é recusado na tela, sem ida ao servidor', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({}));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Fornecedor fora do cadastro' }));
    await usuario.type(screen.getByLabelText('Razão social'), 'Gama Distribuidora LTDA');
    await usuario.type(screen.getByLabelText('Telefone'), '81 3333-1000');
    await usuario.type(screen.getByLabelText('CNPJ (opcional)'), '123');
    await usuario.click(screen.getByRole('button', { name: 'Incluir na cotação' }));

    expect(await screen.findByText(/CPF\/CNPJ inválido/)).toBeInTheDocument();
    expect(criarFornecedor).not.toHaveBeenCalled();
  });

  it('as três réguas do saving aparecem quando existem, e só quando existem (§17)', async () => {
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'AGUARDANDO_GERENTE',
      saving: {
        baselineValue: 1000, closedValue: 900, value: 100, percent: 10,
        competitionBaselineValue: 1200, competitionValue: 300,
        budgetBaselineValue: null, budgetValue: null,
        notes: null, byLabel: 'Carla', at: '2026-08-24T12:00:00Z',
      },
    }));
    abrir();

    const reguas = await screen.findByTestId('reguas-de-saving');
    expect(reguas).toHaveTextContent('Concorrência do BID');
    // a SC não informou orçamento: a régua some em vez de exibir ganho zero
    expect(reguas).not.toHaveTextContent('Contra o orçamento');
  });

  it('sem concorrência nem orçamento, só o ganho de negociação é mostrado', async () => {
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'AGUARDANDO_GERENTE',
      saving: {
        baselineValue: 1000, closedValue: 900, value: 100, percent: 10,
        competitionBaselineValue: null, competitionValue: null,
        budgetBaselineValue: null, budgetValue: null,
        notes: null, byLabel: 'Carla', at: '2026-08-24T12:00:00Z',
      },
    }));
    abrir();

    expect(await screen.findByText(/Ganho de negociação/)).toBeInTheDocument();
    expect(screen.queryByTestId('reguas-de-saving')).not.toBeInTheDocument();
  });

  it('proposta de fornecedor sem homologação não pode ser escolhida (SUP-ERR-030)', async () => {
    // antes a tela deixava marcar, escrever a justificativa e só então o
    // servidor recusava — a régua agora é a mesma dos dois lados
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      proposals: [
        proposta({ id: 'p1', supplierName: 'Alfa' }),
        proposta({ id: 'p2', supplierId: 's2', supplierName: 'Beta', totalValue: 900 }),
      ],
    }));
    vi.mocked(mapaDeFamilias).mockResolvedValue([lote({
      offers: [
        oferta({ proposalId: 'p1', supplierName: 'Alfa' }),
        oferta({ proposalId: 'p2', supplierId: 's2', supplierName: 'Beta', homologation: 'RESTRITO', canWin: false }),
      ],
    })]);
    abrir();

    const forma = await screen.findByTestId('form-vencedor');
    // o mapa por família chega depois do processo: é ele que sabe quem pode vencer
    expect(await within(forma).findByText(/Não pode vencer/)).toHaveTextContent('SUP-ERR-030');
    expect(within(forma).getByRole('radio', { name: 'Escolher Alfa' })).toBeEnabled();
    expect(within(forma).getByRole('radio', { name: 'Escolher Beta' })).toBeDisabled();
    expect(escolherVencedor).not.toHaveBeenCalled();
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

  it('havendo proposta, o score multicritério aparece junto do mapa', async () => {
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      proposals: [proposta({ id: 'p1', supplierName: 'Alfa', totalValue: 1200 })],
    }));
    vi.mocked(mapaDeScore).mockResolvedValue({
      note: 'Score informativo.',
      criteria: [{ code: 'price', label: 'Preço', weightPct: 40, help: 'menor total vale 100' }],
      items: [{
        supplierId: 's1', supplierName: 'Alfa', score: 91.5, pricePct: 100,
        deliveryPct: null, paymentPct: null, otifPct: null, riskPct: null,
      }],
    });
    abrir();
    expect(await screen.findByTestId('mapa-score')).toHaveTextContent('91.5');
  });

  it('sem nenhuma proposta o painel do score nem é montado — não há o que comparar', async () => {
    vi.mocked(lerProcesso).mockResolvedValue(processo({ proposals: [] }));
    abrir();
    expect(await screen.findByText(/Nenhuma proposta lançada ainda/)).toBeInTheDocument();
    expect(screen.queryByText('Comparação multicritério')).not.toBeInTheDocument();
    expect(mapaDeScore).not.toHaveBeenCalled();
  });

  it('a compradora que escolheu o fornecedor vê os botões do Nível 1 do próprio processo', async () => {
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS', 'APROVACAO'] };
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

  it('quem escolheu o fornecedor vê o motivo no lugar dos botões do Nível 2', async () => {
    eu = { id: 'u1', email: 'dora@t.com', name: 'Dora', role: 'Director', modules: ['COMPRAS', 'APROVACAO'] };
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'AGUARDANDO_DIRETOR',
      selection: {
        winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: null,
        justification: 'menor preço', by: 'u1', byLabel: 'Dora',
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

  it('item fora do catálogo: a escolha espera o cadastro, e o comprador cadastra ali mesmo (RFQ-ERR-026)', async () => {
    const usuario = userEvent.setup();
    const semProduto = processo({ status: 'EM_ANALISE' });
    semProduto.items = semProduto.items.map((i) => ({ ...i, catalogItemId: null, catalogCode: null, description: 'Suporte de monitor' }));
    vi.mocked(lerProcesso).mockResolvedValue(semProduto);
    vi.mocked(buscarProdutos).mockResolvedValue([]);
    vi.mocked(listarFamilias).mockResolvedValue([
      { id: 'f1', name: 'MOBILIARIO', active: true } as Awaited<ReturnType<typeof listarFamilias>>[number],
    ]);
    vi.mocked(definirProdutoDoItem).mockResolvedValue(processo({ status: 'EM_ANALISE' }));
    abrir();

    // a tela diz qual item falta, e não mostra a escolha que o servidor recusaria
    expect(await screen.findByTestId('produto-pendente')).toHaveTextContent('Suporte de monitor');
    expect(screen.queryByTestId('form-vencedor')).toBeNull();
    const itens = screen.getByTestId('itens-cotacao');
    expect(within(itens).getByText('fora do catálogo')).toBeInTheDocument();

    await usuario.click(within(itens).getByRole('button', { name: 'Cadastrar produto' }));
    const dialogo = await screen.findByRole('dialog');
    expect(await within(dialogo).findByText(/Nada encontrado/)).toBeInTheDocument();
    await usuario.click(within(dialogo).getByRole('tab', { name: 'Cadastrar produto novo' }));
    // o cadastro nasce com a descrição e a unidade do item
    expect(within(dialogo).getByLabelText('Descrição do produto')).toHaveValue('Suporte de monitor');
    await usuario.selectOptions(within(dialogo).getByLabelText('Família'), 'MOBILIARIO');
    await usuario.click(within(dialogo).getByRole('button', { name: 'Cadastrar e usar no item' }));

    await waitFor(() => expect(definirProdutoDoItem).toHaveBeenCalledWith('q1', 'i1', {
      newProduct: { code: null, description: 'Suporte de monitor', family: 'MOBILIARIO', unitOfMeasure: 'PAR', referencePrice: null },
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
      erpNumber: '663', issuedOn: null, notes: null, supplierId: 's2', noErpReason: null,
    }));
  });

  it('sem o número do ERP a observação é obrigatória, e é ela que libera o fechamento', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'APROVADO_PARA_EMISSAO' }));
    vi.mocked(registrarOc).mockResolvedValue(processo({}));
    abrir();

    const form = await screen.findByTestId('form-oc');
    const botao = within(form).getByRole('button', { name: /Fechar sem O.C.|Registrar O.C./ });

    // com o campo da O.C. vazio o botão vira "sem O.C." e trava até o motivo
    expect(botao).toHaveTextContent('Fechar sem O.C., com a observação');
    expect(botao).toBeDisabled();
    expect(within(form).getByText(/sem ela o processo/)).toBeInTheDocument();

    await usuario.type(within(form).getByLabelText(/por que a O.C. não foi gerada/i), 'curto');
    expect(botao).toBeDisabled();

    await usuario.clear(within(form).getByLabelText(/por que a O.C. não foi gerada/i));
    await usuario.type(within(form).getByLabelText(/por que a O.C. não foi gerada/i),
      'Compra emergencial de balcão, sem tempo de abrir O.C.');
    expect(botao).toBeEnabled();
    await usuario.click(botao);

    await waitFor(() => expect(registrarOc).toHaveBeenCalledWith('q1', {
      erpNumber: '', issuedOn: null, notes: null, supplierId: null,
      noErpReason: 'Compra emergencial de balcão, sem tempo de abrir O.C.',
    }));
  });

  it('digitar o número do ERP faz o campo de observação sumir', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerProcesso).mockResolvedValue(processo({ status: 'APROVADO_PARA_EMISSAO' }));
    abrir();

    const form = await screen.findByTestId('form-oc');
    expect(within(form).getByLabelText(/por que a O.C. não foi gerada/i)).toBeInTheDocument();

    await usuario.type(within(form).getByLabelText('Número da O.C. (ERP)'), '663');
    expect(within(form).queryByLabelText(/por que a O.C. não foi gerada/i)).not.toBeInTheDocument();
    expect(within(form).getByRole('button', { name: /Registrar O.C./ })).toBeEnabled();
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

  it('aprovado com o pedido já criado: a O.C. se registra na tela do pedido, não aqui', async () => {
    // o pedido nasce na aprovação do Nível 2; o formulário antigo fica só para o processo
    // aprovado antes disso (sem pedido nenhum) ou com fornecedor ainda sem pedido
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'APROVADO_PARA_EMISSAO',
      purchaseOrders: [{ id: 'po1', number: 'PO-2026-000001', supplierName: 'Alfa', families: ['EPI'], totalValue: 1200,
        erpNumber: null, noErpReason: null, erpPending: true, status: 'EMITIDO' }],
    }));
    abrir();

    const tabela = await screen.findByTestId('ocs-do-processo');
    expect(screen.queryByTestId('form-oc')).not.toBeInTheDocument();
    expect(within(tabela).getByText('a registrar')).toBeInTheDocument();
    expect(within(tabela).getByRole('link', { name: 'Registrar O.C., faturamento e entrega' }))
      .toHaveAttribute('href', '/pedidos/po1');
    expect(screen.getByTestId('proximo-passo')).toHaveTextContent('na tela do pedido');
    expect(within(screen.getByTestId('proximo-passo')).getByRole('link')).toHaveAttribute('href', '/pedidos/po1');
  });

  it('a O.C. parcial aparece no processo com o número e o aviso do saldo', async () => {
    vi.mocked(lerProcesso).mockResolvedValue(processo({
      status: 'APROVADO_PARA_EMISSAO',
      purchaseOrders: [{ id: 'po1', number: 'PO-2026-000001', supplierName: 'Alfa', families: ['EPI'], totalValue: 1200,
        erpNumber: 'OC-100', noErpReason: null, erpPending: true, status: 'EMITIDO' }],
    }));
    abrir();

    const tabela = await screen.findByTestId('ocs-do-processo');
    expect(within(tabela).getByText('OC-100')).toBeInTheDocument();
    expect(within(tabela).getByText(/parcial: falta O.C./)).toBeInTheDocument();
    expect(within(tabela).getByRole('link', { name: 'Faturamento e entrega' })).toHaveAttribute('href', '/pedidos/po1');
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

describe('memória de preço na proposta', () => {
  const historico = (over = {}) => ({
    quotationItemId: 'i1', average: 100, last: 105, min: 90, max: 110,
    purchases: 4, lastSupplier: 'Alfa', lastAt: '2026-08-01T12:00:00Z', ...over,
  });

  it('o exemplo do documento: média 100, preço 140 → 40%', () => {
    expect(variacaoDoPreco(historico(), '140')).toBe(40);
  });

  it('aceita vírgula, porque é assim que se digita preço aqui', () => {
    expect(variacaoDoPreco(historico(), '110,5')).toBe(10.5);
  });

  it('sem histórico não há variação — nulo diz "não sei", zero afirmaria', () => {
    expect(variacaoDoPreco(undefined, '140')).toBeNull();
    // média zero também não serve de base: a conta daria infinito
    expect(variacaoDoPreco(historico({ average: 0 }), '140')).toBeNull();
  });

  it('campo vazio ou inválido não vira aviso', () => {
    expect(variacaoDoPreco(historico(), '')).toBeNull();
    expect(variacaoDoPreco(historico(), '   ')).toBeNull();
    expect(variacaoDoPreco(historico(), 'abc')).toBeNull();
    expect(variacaoDoPreco(historico(), '0')).toBeNull();
  });

  it('pagar menos aparece como variação negativa, sem alarme', () => {
    expect(variacaoDoPreco(historico(), '80')).toBe(-20);
  });
});
});
