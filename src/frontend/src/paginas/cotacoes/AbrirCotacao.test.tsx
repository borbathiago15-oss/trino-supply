import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { Usuario } from '@/api/auth';
import { agruparPorFamilia, situacaoDaSelecao, type ItemDaFila, type ScNaFila } from '@/api/cotacoes';
import { ToastProvider } from '@/componentes/Toast';
import { AbrirCotacao, linkDoProcesso } from './AbrirCotacao';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  filaDeCotacao: vi.fn(), abrirProcesso: vi.fn(), fecharPorContrato: vi.fn(),
}));
vi.mock('@/api/fornecedores', () => ({ listarFornecedores: vi.fn() }));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import { abrirProcesso, fecharPorContrato, filaDeCotacao } from '@/api/cotacoes';
import { listarFornecedores } from '@/api/fornecedores';

/** Fornecedor da lista, com o contrato no estado que o teste precisa. */
const parceiro = (over: { id: string; nome: string; vigente: boolean; itens?: number }) => ({
  id: over.id, legalName: over.nome, tradeName: over.nome, taxId: '12345678000190',
  email: null, phone: '81 3333-1000', active: true,
  homologationStatus: 'HOMOLOGADO' as const, effectiveHomologation: 'HOMOLOGADO' as const,
  documents: [],
  contract: {
    number: 'CT-2026-001', validFrom: null, validUntil: '2026-12-31', notes: null,
    valueLimit: null, consumed: null, balance: null, current: over.vigente,
    items: Array.from({ length: over.itens ?? 2 }, () => ({
      catalogItemId: null, catalogCode: null, description: 'Bota', unitOfMeasure: 'PAR',
      unitPrice: 45, paymentTerms: null, paymentDays: null, deliveryDays: null, notes: null,
    })),
  },
});

let eu: Usuario = {
  id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'],
};

const item = (p: Partial<ItemDaFila>): ItemDaFila => ({
  id: 'i1', sequence: 1, catalogCode: 'EPI-001', description: 'Luva nitrílica',
  quantity: 10, unitOfMeasure: 'PAR', estimatedUnitPrice: 12, family: 'EPI',
  ...p,
});

const sc = (p: Partial<ScNaFila>): ScNaFila => ({
  id: 'sc1', number: 'SC-2026-000001', requesterLabel: 'Ana', costCenter: 'BAH-001',
  justification: 'Reposição de EPI', totalEstimatedValue: 250, neededBy: null,
  assignedToId: null, assignedToLabel: null, blockReason: null, partial: false,
  families: ['EPI'], items: [item({})],
  ...p,
});

const abrir = () => render(
  <MemoryRouter><ToastProvider><AbrirCotacao /></ToastProvider></MemoryRouter>,
);

describe('destino depois de abrir', () => {
  it('vai para a tela de processos do clássico, já no processo novo', () => {
    expect(linkDoProcesso('q1')).toBe('/cotacoes/q1');
    expect(linkDoProcesso()).toBe('/cotacoes');
  });
});

describe('regras da seleção por item', () => {
  it('nada marcado não abre nada', () => {
    expect(situacaoDaSelecao([])).toMatchObject({ podeJuntar: false, podeSeparar: false });
  });
  it('centros de custo diferentes travam as duas ações e explicam', () => {
    const s = situacaoDaSelecao([
      { id: 'i1', centroCusto: 'BAH-001', familia: 'EPI' },
      { id: 'i2', centroCusto: 'SP-002', familia: 'EPI' },
    ]);
    expect(s).toMatchObject({ misturado: true, podeJuntar: false, podeSeparar: false });
    expect(s.aviso).toMatch(/Centros de custo diferentes/);
  });
  it('o centro de custo é comparado sem diferenciar maiúsculas', () => {
    expect(situacaoDaSelecao([
      { id: 'i1', centroCusto: 'BAH-001', familia: 'EPI' },
      { id: 'i2', centroCusto: 'bah-001', familia: 'EPI' },
    ]).misturado).toBe(false);
  });
  it('separar por família exige duas famílias', () => {
    const uma = situacaoDaSelecao([{ id: 'i1', centroCusto: 'BAH-001', familia: 'EPI' }]);
    expect(uma).toMatchObject({ podeJuntar: true, podeSeparar: false });
    const duas = situacaoDaSelecao([
      { id: 'i1', centroCusto: 'BAH-001', familia: 'EPI' },
      { id: 'i2', centroCusto: 'BAH-001', familia: 'FERRAMENTA' },
    ]);
    expect(duas).toMatchObject({ podeJuntar: true, podeSeparar: true, familias: 2 });
  });
  it('o agrupamento junta os ids de cada família', () => {
    const g = agruparPorFamilia([
      { id: 'i1', familia: 'EPI' }, { id: 'i2', familia: 'EPI' }, { id: 'i3', familia: 'FERRAMENTA' },
    ]);
    expect([...g.keys()]).toEqual(['EPI', 'FERRAMENTA']);
    expect(g.get('EPI')).toEqual(['i1', 'i2']);
  });
});

describe('tela Abrir Cotação', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    eu = { id: 'u1', email: 'carla@t.com', name: 'Carla', role: 'PurchasingOfficer', modules: ['COMPRAS'] };
    // o resetAllMocks apaga a implementação da fábrica: sem repor, a tela chamaria
    // `.then` em undefined ao carregar os parceiros
    vi.mocked(listarFornecedores).mockResolvedValue([]);
  });

  it('a SC retida não é selecionável e mostra de quem é a aprovação', async () => {
    vi.mocked(filaDeCotacao).mockResolvedValue([
      sc({ blockReason: 'Aguardando aprovação de: Diretor Financeiro', items: [] }),
    ]);
    abrir();
    const tabela = await screen.findByTestId('fila-cotacao');
    expect(within(tabela).getByText('RETIDA NA APROVAÇÃO')).toBeInTheDocument();
    expect(within(tabela).getByText(/Diretor Financeiro/)).toBeInTheDocument();
    expect(within(tabela).queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('o cabeçalho da SC marca e desmarca todos os itens dela', async () => {
    const usuario = userEvent.setup();
    vi.mocked(filaDeCotacao).mockResolvedValue([
      sc({ items: [item({}), item({ id: 'i2', sequence: 2, description: 'Bota' })] }),
    ]);
    abrir();

    await usuario.click(await screen.findByLabelText('Marcar todos os itens de SC-2026-000001'));
    expect(screen.getByRole('button', { name: /Um processo com os itens marcados \(2\)/ })).toBeEnabled();

    await usuario.click(screen.getByLabelText('Marcar todos os itens de SC-2026-000001'));
    expect(screen.getByRole('button', { name: /Um processo com os itens marcados \(0\)/ })).toBeDisabled();
  });

  it('itens de centros diferentes travam a abertura e avisam', async () => {
    const usuario = userEvent.setup();
    vi.mocked(filaDeCotacao).mockResolvedValue([
      sc({}),
      sc({ id: 'sc2', number: 'SC-2026-000002', costCenter: 'SP-002', items: [item({ id: 'i9' })] }),
    ]);
    abrir();

    await usuario.click(await screen.findByLabelText('Marcar Luva nitrílica de SC-2026-000001'));
    await usuario.click(screen.getByLabelText('Marcar Luva nitrílica de SC-2026-000002'));

    expect(screen.getByText(/Centros de custo diferentes/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Um processo com os itens marcados/ })).toBeDisabled();
    expect(abrirProcesso).not.toHaveBeenCalled();
  });

  it('junta os itens marcados num processo, com tipo e prazo', async () => {
    const usuario = userEvent.setup();
    vi.mocked(filaDeCotacao).mockResolvedValue([sc({})]);
    vi.mocked(abrirProcesso).mockResolvedValue({ id: 'q1', number: 'RFQ-2026-000001' });
    abrir();

    await usuario.click(await screen.findByLabelText('Marcar Luva nitrílica de SC-2026-000001'));
    await usuario.selectOptions(screen.getByLabelText('Tipo de cotação'), 'BID');
    await usuario.type(screen.getByLabelText('Prazo para respostas'), '2026-10-15');
    await usuario.click(screen.getByRole('button', { name: /Um processo com os itens marcados/ }));

    await waitFor(() => expect(abrirProcesso).toHaveBeenCalledWith({
      prItemIds: ['i1'], kind: 'BID', deadline: '2026-10-15',
    }));
  });

  it('separar por família confirma antes e abre um processo para cada uma', async () => {
    const usuario = userEvent.setup();
    vi.mocked(filaDeCotacao).mockResolvedValue([
      sc({ families: ['EPI', 'FERRAMENTA'], items: [item({}), item({ id: 'i2', sequence: 2, description: 'Furadeira', family: 'FERRAMENTA' })] }),
    ]);
    vi.mocked(abrirProcesso)
      .mockResolvedValueOnce({ id: 'q1', number: 'RFQ-1' })
      .mockResolvedValueOnce({ id: 'q2', number: 'RFQ-2' });
    abrir();

    await usuario.click(await screen.findByLabelText('Marcar todos os itens de SC-2026-000001'));
    await usuario.click(screen.getByRole('button', { name: /Um processo por família \(2\)/ }));

    const dialogo = screen.getByRole('dialog');
    expect(within(dialogo).getByText(/EPI, FERRAMENTA/)).toBeInTheDocument();
    await usuario.click(within(dialogo).getByRole('button', { name: 'Abrir 2 processos' }));

    await waitFor(() => expect(abrirProcesso).toHaveBeenCalledTimes(2));
    expect(vi.mocked(abrirProcesso).mock.calls[0][0]).toMatchObject({ prItemIds: ['i1'] });
    expect(vi.mocked(abrirProcesso).mock.calls[1][0]).toMatchObject({ prItemIds: ['i2'] });
  });

  it('quem não conduz cotação vê a fila sem as caixas de seleção', async () => {
    eu = { id: 'u5', email: 'ana@t.com', name: 'Ana', role: 'Approver', modules: ['APROVACAO'] };
    vi.mocked(filaDeCotacao).mockResolvedValue([sc({})]);
    abrir();
    await screen.findByTestId('fila-cotacao');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Um processo com os itens/ })).not.toBeInTheDocument();
  });

  describe('fechar pelo contrato de parceria', () => {
    const comFila = async () => {
      vi.mocked(filaDeCotacao).mockResolvedValue([sc({})]);
      abrir();
      await waitFor(() => expect(screen.getByTestId('fila-cotacao')).toBeInTheDocument());
    };

    it('só aparece quando existe fornecedor com contrato vigente', async () => {
      // contrato vencido não é opção: fechar por ele seria fechar por um preço
      // que já não vale, e é o mesmo CT-ERR-020 que o servidor recusaria
      vi.mocked(listarFornecedores).mockResolvedValue([
        parceiro({ id: 'f1', nome: 'Vencido', vigente: false }),
      ] as never);
      await comFila();
      expect(screen.queryByTestId('fechar-por-contrato')).not.toBeInTheDocument();
    });

    it('contrato vigente mas sem item cadastrado também não conta', async () => {
      // contrato sem produto não fixa preço de nada — oferecer o caminho aqui
      // levaria direto ao CT-ERR-021 para todos os itens
      vi.mocked(listarFornecedores).mockResolvedValue([
        parceiro({ id: 'f1', nome: 'Sem itens', vigente: true, itens: 0 }),
      ] as never);
      await comFila();
      expect(screen.queryByTestId('fechar-por-contrato')).not.toBeInTheDocument();
    });

    it('com parceiro vigente, fecha os itens marcados pelo contrato escolhido', async () => {
      vi.mocked(listarFornecedores).mockResolvedValue([
        parceiro({ id: 'f1', nome: 'Pernambuco', vigente: true }),
      ] as never);
      vi.mocked(fecharPorContrato).mockResolvedValue({ id: 'q9', number: 'RFQ-2026-000009' });
      await comFila();

      const painel = screen.getByTestId('fechar-por-contrato');
      const botao = within(painel).getByRole('button', { name: /Fechar pelo contrato/ });
      // sem fornecedor escolhido o botão não libera, mesmo com item marcado
      await userEvent.click(screen.getAllByRole('checkbox')[0]);
      expect(botao).toBeDisabled();

      await userEvent.selectOptions(
        within(painel).getByLabelText('Fornecedor parceiro'), 'f1');
      expect(botao).toBeEnabled();

      await userEvent.click(botao);
      await waitFor(() => expect(fecharPorContrato)
        .toHaveBeenCalledWith(['i1'], 'f1'));
    });

    it('o número do contrato aparece na opção, para não escolher o parceiro errado', async () => {
      vi.mocked(listarFornecedores).mockResolvedValue([
        parceiro({ id: 'f1', nome: 'Pernambuco', vigente: true }),
      ] as never);
      await comFila();
      expect(within(screen.getByTestId('fechar-por-contrato'))
        .getByRole('option', { name: /Pernambuco — contrato CT-2026-001/ })).toBeInTheDocument();
    });
  });
});
