import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { linhaDeContrato, resumoDeContratos, type LinhaContrato } from '@/api/contratos';
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

const abrir = () => render(<ToastProvider><Contratos /></ToastProvider>);

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
    vi.mocked(listarContratos).mockResolvedValue([
      linha('Alfa EPIs', { valueLimit: 100000, consumed: 20000, balance: 80000, current: true }),
      linha('Beta Química', { current: false }),
    ]);
    abrir();
    const tabela = await screen.findByTestId('tabela-contratos');
    expect(within(tabela).getByText('VIGENTE')).toBeInTheDocument();
    expect(within(tabela).getByText('FORA DA VIGÊNCIA')).toBeInTheDocument();
    expect(screen.getByText('1 fora da vigência')).toBeInTheDocument();
    expect(screen.getByText('20% consumido')).toBeInTheDocument();
  });

  it('o reajuste fechado acima do pleiteado não chega à API', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarContratos).mockResolvedValue([linha('Alfa EPIs', {})]);
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
    vi.mocked(listarContratos).mockResolvedValue([linha('Alfa EPIs', {})]);
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
    vi.mocked(listarContratos).mockResolvedValue([linha('Alfa EPIs', {})]);
    vi.mocked(historicoDeReajustes).mockResolvedValue({
      costAvoidanceTotal: 6000,
      items: [{
        id: 'a1', requestedPercent: 8, agreedPercent: 3, baseValue: 120000, costAvoidance: 6000,
        appliedToPrices: false, notes: 'negociado na renovação', createdByLabel: 'Ana',
        createdAt: '2026-09-01T12:00:00Z',
      }],
    });
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Mais ações' }));
    await usuario.click(screen.getByRole('menuitem', { name: 'Histórico de reajustes' }));

    const dialogo = await screen.findByRole('dialog');
    expect(within(dialogo).getByText(/Custo evitado total/)).toBeInTheDocument();
    expect(within(dialogo).getByText(/Pleiteado 8% → fechado 3%/)).toBeInTheDocument();
  });
});
