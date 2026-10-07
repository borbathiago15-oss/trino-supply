import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  linhaDeContrato, resumoDeContratos, type AcervoDeContratos, type LinhaContrato,
} from '@/api/contratos';
import type { Fornecedor } from '@/api/fornecedores';
import { ToastProvider } from '@/componentes/Toast';
import { Contratos, validarPleito } from './Contratos';

vi.mock('@/api/contratos', async (importar) => ({
  ...(await importar<typeof import('@/api/contratos')>()),
  listarContratos: vi.fn(), registrarReajuste: vi.fn(), historicoDeReajustes: vi.fn(),
}));

import { historicoDeReajustes, listarContratos, registrarReajuste } from '@/api/contratos';

const fornecedor = (nome: string, c: Partial<Fornecedor['contract']>): Fornecedor => ({
  id: 's-' + nome, legalName: nome, tradeName: null, taxId: '00000000000191', email: null, phone: null,
  active: true, homologationStatus: 'HOMOLOGADO', effectiveHomologation: 'HOMOLOGADO', documents: [], duplicateCount: 0,
  contract: {
    number: 'CT-01', validFrom: '2026-01-01', validUntil: '2026-12-31', notes: null,
    valueLimit: 100000, consumed: 20000, balance: 80000, current: true,
    items: [{ catalogItemId: null, catalogCode: 'EPI-001', description: 'Luva', unitOfMeasure: 'PAR', unitPrice: 12, paymentTerms: null, paymentDays: null, deliveryDays: null, notes: null }],
    ...c,
  },
});

const linha = (nome: string, c: Partial<Fornecedor['contract']>): LinhaContrato =>
  linhaDeContrato(fornecedor(nome, c));

/** O retrato que a tela lê: as linhas, quem pode receber contrato e os fornecedores inteiros. */
const acervo = (nomes: [string, Partial<Fornecedor['contract']>][],
  semContrato: string[] = []): AcervoDeContratos => {
  const comContrato = nomes.map(([n, c]) => fornecedor(n, c));
  // sem contrato é **sem nada**: número, vigência, teto e itens vazios. Deixar as datas do
  // fixture faria o formulário "novo" nascer com vigência, e o teste da vigência mentiria
  const livres = semContrato.map((n) => fornecedor(n, {
    number: null, validFrom: null, validUntil: null, valueLimit: null,
    consumed: null, balance: null, current: false, items: [],
  }));
  return {
    linhas: comContrato.map(linhaDeContrato),
    semContrato: livres.map((f) => ({ id: f.id, legalName: f.legalName })),
    fornecedores: [...comContrato, ...livres],
  };
};

const abrir = () => render(<MemoryRouter><ToastProvider><Contratos /></ToastProvider></MemoryRouter>);

describe('contas do contrato de parceria', () => {
  it('saldo abaixo de 20% do teto é crítico', () => {
    expect(linha('Alfa', { valueLimit: 100000, consumed: 85000, balance: 15000 }).saldoCritico).toBe(true);
    expect(linha('Alfa', { valueLimit: 100000, consumed: 50000, balance: 50000 }).saldoCritico).toBe(false);
    expect(linha('Alfa', { valueLimit: null, consumed: null, balance: null }).saldoCritico).toBe(false);
  });
  it('só o contrato vigente entra no teto e no consumo do período', () => {
    const r = resumoDeContratos([
      linha('Alfa', { valueLimit: 100000, consumed: 20000, balance: 80000, current: true }),
      linha('Beta', { valueLimit: 500000, consumed: 400000, balance: 100000, current: false }),
    ]);
    expect(r).toMatchObject({ vigentes: 1, foraDaVigencia: 1, tetoTotal: 100000, consumido: 20000, saldo: 80000, percentualConsumido: 20 });
  });
  it('sem teto não há percentual para mostrar', () => {
    expect(resumoDeContratos([linha('Alfa', { valueLimit: null, consumed: null, balance: null })]).percentualConsumido).toBeNull();
  });
  it('o pleito precisa dos dois percentuais, e o fechado nunca supera o pleiteado', () => {
    expect(validarPleito('', '0')).toBe('Informe o percentual pleiteado pelo fornecedor.');
    expect(validarPleito('8', '')).toBe('Informe o percentual fechado (0 = reajuste totalmente evitado).');
    expect(validarPleito('8', '9')).toBe('O percentual fechado não pode ser maior que o pleiteado.');
    expect(validarPleito('8', '0')).toBeNull();
    expect(validarPleito('8', '3,5')).toBeNull();
  });
});

describe('tela Contratos de Parceria', () => {
  beforeEach(() => vi.resetAllMocks());

  it('mostra os KPIs do período e marca a vigência de cada contrato', async () => {
    vi.mocked(listarContratos).mockResolvedValue(acervo([
      ['Alfa EPIs', { valueLimit: 100000, consumed: 20000, balance: 80000, current: true }],
      ['Beta Química', { current: false }],
    ]));
    abrir();
    const tabela = await screen.findByTestId('tabela-contratos');
    expect(within(tabela).getByText('VIGENTE')).toBeInTheDocument();
    expect(within(tabela).getByText('FORA DA VIGÊNCIA')).toBeInTheDocument();
    expect(screen.getByText('1 fora da vigência')).toBeInTheDocument();
    expect(screen.getByText('20% consumido')).toBeInTheDocument();
  });

  it('o reajuste fechado acima do pleiteado não chega à API', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(acervo([['Alfa EPIs', {}]]));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Registrar reajuste' }));
    const dialogo = screen.getByRole('dialog');
    await usuario.type(within(dialogo).getByLabelText(/Percentual pleiteado/), '8');
    await usuario.type(within(dialogo).getByLabelText(/Percentual fechado/), '9');
    await usuario.click(within(dialogo).getByRole('button', { name: 'Registrar reajuste' }));

    expect(await screen.findByText('O percentual fechado não pode ser maior que o pleiteado.')).toBeInTheDocument();
    expect(registrarReajuste).not.toHaveBeenCalled();
  });

  it('registra o pleito com o que foi digitado e recarrega a lista', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(acervo([['Alfa EPIs', {}]]));
    vi.mocked(registrarReajuste).mockResolvedValue({
      id: 'a1', requestedPercent: 8, agreedPercent: 3, baseValue: 120000, costAvoidance: 6000,
      appliedToPrices: true, notes: null, createdByLabel: 'Ana', createdAt: '2026-09-01T12:00:00Z',
    });
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Registrar reajuste' }));
    const dialogo = screen.getByRole('dialog');
    await usuario.type(within(dialogo).getByLabelText(/Percentual pleiteado/), '8');
    await usuario.type(within(dialogo).getByLabelText(/Percentual fechado/), '3');
    await usuario.click(within(dialogo).getByLabelText(/Aplicar o % fechado/));
    await usuario.click(within(dialogo).getByRole('button', { name: 'Registrar reajuste' }));

    await waitFor(() => expect(registrarReajuste).toHaveBeenCalledWith('s-Alfa EPIs', {
      requestedPercent: 8, agreedPercent: 3, notes: null, applyToPrices: true,
    }));
    await waitFor(() => expect(listarContratos).toHaveBeenCalledTimes(2));
  });

  it('o histórico abre com o custo evitado total', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(acervo([['Alfa EPIs', {}]]));
    vi.mocked(historicoDeReajustes).mockResolvedValue({
      costAvoidanceTotal: 6000,
      items: [{
        id: 'a1', requestedPercent: 8, agreedPercent: 3, baseValue: 120000, costAvoidance: 6000,
        appliedToPrices: false, notes: 'negociado na renovação', createdByLabel: 'Ana',
        createdAt: '2026-09-01T12:00:00Z',
      }],
    });
    abrir();

    await usuario.click(await screen.findByRole('button', { name: /Mais ações/ }));
    await usuario.click(screen.getByRole('menuitem', { name: 'Histórico de reajustes' }));

    const dialogo = await screen.findByRole('dialog');
    expect(within(dialogo).getByText(/Custo evitado total/)).toBeInTheDocument();
    expect(within(dialogo).getByText(/Pleiteado 8% → fechado 3%/)).toBeInTheDocument();
  });
});

describe('o contrato de parceria é mantido aqui', () => {
  // decisão da empresa (2026-10): o cadastro do fornecedor continua em Cadastros; o contrato
  // dele — número, vigência, teto e produtos — vive neste menu. Nenhuma regra mudou de
  // conteúdo: é a mesma chamada, a mesma vigência e o mesmo encerramento por lista vazia
  beforeEach(() => vi.resetAllMocks());

  it('"Novo contrato" oferece só fornecedor ativo e sem contrato', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(
      acervo([['Alfa EPIs', {}]], ['Gama Ferramentas']));
    abrir();
    await screen.findByTestId('tabela-contratos');

    await usuario.click(screen.getByRole('button', { name: 'Novo contrato de parceria' }));
    const seletor = screen.getByLabelText(/Fornecedor/);
    expect([...seletor.querySelectorAll('option')].map((o) => o.textContent))
      .toEqual(['Selecione o fornecedor…', 'Gama Ferramentas']);
    // quem já tem contrato se edita pela linha: dois caminhos para a mesma coisa é o que se desfez
    expect(within(seletor).queryByText('Alfa EPIs')).not.toBeInTheDocument();
  });

  it('abrir o contrato de quem não tem começa o formulário em branco', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(
      acervo([['Alfa EPIs', {}]], ['Gama Ferramentas']));
    abrir();
    await screen.findByTestId('tabela-contratos');

    await usuario.click(screen.getByRole('button', { name: 'Novo contrato de parceria' }));
    await usuario.selectOptions(screen.getByLabelText(/Fornecedor/), 's-Gama Ferramentas');
    await usuario.click(screen.getByRole('button', { name: 'Abrir contrato' }));

    expect(await screen.findByText(/Contrato de parceria — Gama Ferramentas/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Número do contrato/)).toHaveValue('');
  });

  it('"Editar contrato" abre o contrato daquela linha, já preenchido', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(acervo([['Alfa EPIs', { number: 'CT-2026-001' }]]));
    abrir();
    await screen.findByTestId('tabela-contratos');

    await usuario.click(screen.getByRole('button', { name: /Mais ações do contrato de Alfa EPIs/ }));
    await usuario.click(screen.getByRole('menuitem', { name: 'Editar contrato' }));

    expect(await screen.findByText(/Contrato de parceria — Alfa EPIs/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Número do contrato/)).toHaveValue('CT-2026-001');
  });

  it('trocar de fornecedor não carrega o contrato do anterior', async () => {
    // a consequência era pior que um rótulo errado: salvar copiava número, teto, vigência e
    // itens de um fornecedor para outro. A `key` é o que impede, e a regra veio junto na mudança
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(acervo([
      ['Alfa EPIs', { number: 'CT-2026-001' }],
      ['Beta Química', { number: 'CT-2026-002' }],
    ]));
    abrir();
    await screen.findByTestId('tabela-contratos');

    await usuario.click(screen.getByRole('button', { name: /Mais ações do contrato de Alfa EPIs/ }));
    await usuario.click(screen.getByRole('menuitem', { name: 'Editar contrato' }));
    expect(await screen.findByLabelText(/Número do contrato/)).toHaveValue('CT-2026-001');

    await usuario.click(screen.getByRole('button', { name: /Mais ações do contrato de Beta Química/ }));
    await usuario.click(screen.getByRole('menuitem', { name: 'Editar contrato' }));
    await waitFor(() => expect(screen.getByLabelText(/Número do contrato/)).toHaveValue('CT-2026-002'));
  });

  it('a vigência não aceita fim antes do início (SUP-ERR-020)', async () => {
    // o servidor recusa; o campo já não deixa escolher, em vez de avisar no salvar
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(acervo([], ['Gama Ferramentas']));
    abrir();
    await usuario.click(await screen.findByRole('button', { name: 'Novo contrato de parceria' }));
    await usuario.selectOptions(screen.getByLabelText(/Fornecedor/), 's-Gama Ferramentas');
    await usuario.click(screen.getByRole('button', { name: 'Abrir contrato' }));

    const fim = await screen.findByLabelText(/Fim da vigência/);
    expect(fim).not.toHaveAttribute('min');          // sem início, nada a limitar
    await usuario.type(screen.getByLabelText('Início da vigência'), '2026-03-01');
    expect(fim).toHaveAttribute('min', '2026-03-01');
  });

  it('salvar sem produto nenhum encerra o contrato, e a tela diz isso', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue(acervo([], ['Gama Ferramentas']));
    abrir();
    await usuario.click(await screen.findByRole('button', { name: 'Novo contrato de parceria' }));
    await usuario.selectOptions(screen.getByLabelText(/Fornecedor/), 's-Gama Ferramentas');
    await usuario.click(screen.getByRole('button', { name: 'Abrir contrato' }));

    expect(await screen.findByText(/Salvar sem nenhum produto encerra o contrato/)).toBeInTheDocument();
  });
});
