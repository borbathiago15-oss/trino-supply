import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { somaDosPesos, type ReguaDoScore } from '@/api/pesosDoScore';
import { ToastProvider } from '@/componentes/Toast';
import { PesosDoScore } from './PesosDoScore';

vi.mock('@/api/pesosDoScore', async (importar) => ({
  ...(await importar<typeof import('@/api/pesosDoScore')>()),
  lerPesosDoScore: vi.fn(),
  salvarPesosDoScore: vi.fn(),
}));

import { lerPesosDoScore, salvarPesosDoScore } from '@/api/pesosDoScore';

const regua = (p: Partial<ReguaDoScore['weights']> = {}, canEdit = true): ReguaDoScore => ({
  canEdit,
  weights: {
    price: 40, delivery: 20, payment: 10, otif: 20, risk: 10,
    updatedAt: '2026-09-01T12:00:00Z', updatedByLabel: 'Thiago',
    criteria: [
      { code: 'price', label: 'Preço', weightPct: 40, help: 'O menor total vale 100.' },
      { code: 'delivery', label: 'Prazo de entrega', weightPct: 20, help: 'O mais curto vale 100.' },
      { code: 'payment', label: 'Prazo de pagamento', weightPct: 10, help: 'O mais longo vale 100.' },
      { code: 'otif', label: 'OTIF histórico', weightPct: 20, help: 'Entregas no prazo.' },
      { code: 'risk', label: 'Risco interno', weightPct: 10, help: 'Quanto maior, menos risco.' },
    ],
    ...p,
  },
});

const abrir = () => render(<ToastProvider><PesosDoScore /></ToastProvider>);

describe('soma dos pesos', () => {
  it('soma o que está digitado, aceitando vírgula e ignorando campo vazio', () => {
    expect(somaDosPesos({ price: '40', delivery: '20', payment: '10', otif: '20', risk: '10' })).toBe(100);
    expect(somaDosPesos({ price: '40', delivery: '', payment: '10', otif: '20', risk: '10' })).toBe(80);
    expect(somaDosPesos({ price: '2,5', delivery: '0', payment: '0', otif: '0', risk: '0' })).toBe(2.5);
  });

  it('texto que não é número não vira NaN e contamina a soma', () => {
    expect(somaDosPesos({ price: 'abc', delivery: '20', payment: '0', otif: '0', risk: '0' })).toBe(20);
  });
});

describe('tela dos pesos do score', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(salvarPesosDoScore).mockResolvedValue(regua().weights);
  });

  it('carrega a régua vigente nos campos, com o rótulo e a explicação do servidor', async () => {
    vi.mocked(lerPesosDoScore).mockResolvedValue(regua());
    abrir();
    expect(await screen.findByLabelText(/Preço/)).toHaveValue(40);
    expect(screen.getByLabelText(/OTIF histórico/)).toHaveValue(20);
    expect(screen.getByText(/O menor total vale 100/)).toBeInTheDocument();
  });

  it('a soma aparece enquanto se digita, e fora de 100 o botão não libera', async () => {
    vi.mocked(lerPesosDoScore).mockResolvedValue(regua());
    abrir();
    const preco = await screen.findByLabelText(/Preço/);

    await userEvent.clear(preco);
    await userEvent.type(preco, '50');

    expect(screen.getByTestId('soma-dos-pesos')).toHaveTextContent('110');
    expect(screen.getByRole('button', { name: /Salvar pesos/ })).toBeDisabled();
    expect(salvarPesosDoScore).not.toHaveBeenCalled();
  });

  it('fechando 100 o salvamento vai com os cinco pesos', async () => {
    vi.mocked(lerPesosDoScore).mockResolvedValue(regua());
    abrir();
    const preco = await screen.findByLabelText(/Preço/);
    const entrega = screen.getByLabelText(/Prazo de entrega/);

    await userEvent.clear(preco);
    await userEvent.type(preco, '50');
    await userEvent.clear(entrega);
    await userEvent.type(entrega, '10');

    expect(screen.getByTestId('soma-dos-pesos')).toHaveTextContent('100');
    await userEvent.click(screen.getByRole('button', { name: /Salvar pesos/ }));

    await waitFor(() => expect(salvarPesosDoScore).toHaveBeenCalledWith(
      { price: 50, delivery: 10, payment: 10, otif: 20, risk: 10 }));
  });

  it('quem não pode alterar vê a régua e não vê o botão', async () => {
    vi.mocked(lerPesosDoScore).mockResolvedValue(regua({}, false));
    abrir();
    expect(await screen.findByLabelText(/Preço/)).toBeDisabled();
    expect(screen.queryByRole('button', { name: /Salvar pesos/ })).not.toBeInTheDocument();
    expect(screen.getByText(/Só o administrador altera esta régua/)).toBeInTheDocument();
  });

  it('a tela diz quem mexeu por último — régua trocada sem dono é auditoria pela metade', async () => {
    vi.mocked(lerPesosDoScore).mockResolvedValue(regua());
    abrir();
    expect(await screen.findByText(/Última alteração por/)).toHaveTextContent('Thiago');
  });
});
