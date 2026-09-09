import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { leituraDoPrazo, type PrazosDasEtapas as Regua } from '@/api/prazosDasEtapas';
import { ToastProvider } from '@/componentes/Toast';
import { PrazosDasEtapas } from './PrazosDasEtapas';

vi.mock('@/api/prazosDasEtapas', async (importar) => ({
  ...(await importar<typeof import('@/api/prazosDasEtapas')>()),
  lerPrazosDasEtapas: vi.fn(),
  salvarPrazosDasEtapas: vi.fn(),
}));

import { lerPrazosDasEtapas, salvarPrazosDasEtapas } from '@/api/prazosDasEtapas';

const regua = (canEdit = true): Regua => ({
  warnAtPercent: 80,
  canEdit,
  items: [
    { stage: 'SOLICITACAO', label: 'Solicitação', maxDays: 2, updatedAt: '', updatedByLabel: '' },
    { stage: 'COTACAO', label: 'Cotação', maxDays: 7, updatedAt: '2026-09-01T12:00:00Z', updatedByLabel: 'Thiago' },
    { stage: 'APROVACAO', label: 'Aprovação', maxDays: 3, updatedAt: '', updatedByLabel: '' },
  ],
});

const abrir = () => render(<ToastProvider><PrazosDasEtapas /></ToastProvider>);

describe('leitura do prazo', () => {
  it('diz a partir de quando avisa e quando estoura', () => {
    expect(leituraDoPrazo(10, 80)).toBe('atenção a partir de 8 dia(s), estouro depois de 10');
  });

  it('zero é "sem cobrança", e não "resolver hoje"', () => {
    // "0 dias" se leria como o prazo mais apertado possível — o oposto do que zero significa
    expect(leituraDoPrazo(0, 80)).toBe('sem cobrança de tempo nesta etapa');
  });

  it('prazo curto ainda avisa um dia antes, em vez de arredondar para zero', () => {
    expect(leituraDoPrazo(1, 80)).toContain('a partir de 1 dia');
  });
});

describe('tela dos prazos por etapa', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(salvarPrazosDasEtapas).mockResolvedValue({ items: [] });
  });

  it('carrega o prazo de cada etapa com o rótulo do servidor', async () => {
    vi.mocked(lerPrazosDasEtapas).mockResolvedValue(regua());
    abrir();
    expect(await screen.findByLabelText(/Cotação/)).toHaveValue(7);
    expect(screen.getByLabelText(/Aprovação/)).toHaveValue(3);
    expect(screen.getByTestId('leitura-COTACAO')).toHaveTextContent('estouro depois de 7');
  });

  it('salvar manda os prazos das etapas todas', async () => {
    vi.mocked(lerPrazosDasEtapas).mockResolvedValue(regua());
    abrir();
    const cotacao = await screen.findByLabelText(/Cotação/);
    await userEvent.clear(cotacao);
    await userEvent.type(cotacao, '10');
    await userEvent.click(screen.getByRole('button', { name: /Salvar prazos/ }));

    await waitFor(() => expect(salvarPrazosDasEtapas).toHaveBeenCalledWith(
      { SOLICITACAO: 2, COTACAO: 10, APROVACAO: 3 }));
  });

  it('prazo vazio ou fora da faixa não sai da tela', async () => {
    vi.mocked(lerPrazosDasEtapas).mockResolvedValue(regua());
    abrir();
    const cotacao = await screen.findByLabelText(/Cotação/);
    await userEvent.clear(cotacao);

    expect(screen.getByRole('button', { name: /Salvar prazos/ })).toBeDisabled();
    expect(salvarPrazosDasEtapas).not.toHaveBeenCalled();
  });

  it('zero é aceito: desligar a cobrança é uma decisão, não um erro', async () => {
    vi.mocked(lerPrazosDasEtapas).mockResolvedValue(regua());
    abrir();
    const cotacao = await screen.findByLabelText(/Cotação/);
    await userEvent.clear(cotacao);
    await userEvent.type(cotacao, '0');

    expect(screen.getByTestId('leitura-COTACAO')).toHaveTextContent('sem cobrança de tempo');
    expect(screen.getByRole('button', { name: /Salvar prazos/ })).toBeEnabled();
  });

  it('quem não é administrador vê a régua e não a edita', async () => {
    vi.mocked(lerPrazosDasEtapas).mockResolvedValue(regua(false));
    abrir();
    expect(await screen.findByLabelText(/Cotação/)).toBeDisabled();
    expect(screen.queryByRole('button', { name: /Salvar prazos/ })).not.toBeInTheDocument();
    expect(screen.getByText(/Só o administrador define os prazos/)).toBeInTheDocument();
  });

  it('a etapa alterada diz por quem — régua trocada sem dono é auditoria pela metade', async () => {
    vi.mocked(lerPrazosDasEtapas).mockResolvedValue(regua());
    abrir();
    expect(await screen.findByText(/alterado por/)).toHaveTextContent('Thiago');
  });
});
