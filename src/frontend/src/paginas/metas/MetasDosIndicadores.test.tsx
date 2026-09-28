import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { MetasDosIndicadores as Dados } from '@/api/metas';
import { ToastProvider } from '@/componentes/Toast';
import { MetasDosIndicadores, valorDaMeta } from './MetasDosIndicadores';

vi.mock('@/api/metas', async (importar) => ({
  ...(await importar<typeof import('@/api/metas')>()),
  lerMetas: vi.fn(), salvarMetas: vi.fn(),
}));
import { lerMetas, salvarMetas } from '@/api/metas';

const dados = (canEdit = true): Dados => ({
  canEdit,
  goals: [
    { indicator: 'saving', label: 'Saving negociado', unit: 'moeda', higherIsBetter: true, accumulates: true,
      monthlyValue: 50000, updatedAt: '2026-09-20T10:00:00Z', updatedByLabel: 'Administrador' },
    { indicator: 'otif', label: 'Entrega no prazo (OTIF)', unit: 'pct', higherIsBetter: true, accumulates: false,
      monthlyValue: null, updatedAt: null, updatedByLabel: null },
  ],
});

const abrir = () => render(<ToastProvider><MetasDosIndicadores /></ToastProvider>);

describe('valor da meta', () => {
  it('vazio é "sem meta", e vírgula decimal vale', () => {
    expect(valorDaMeta('')).toBeNull();
    expect(valorDaMeta('  ')).toBeNull();
    expect(valorDaMeta('92,5')).toBe(92.5);
    expect(valorDaMeta('50.000')).toBe(50000);
    expect(valorDaMeta('abc')).toBeNaN();
  });
});

describe('<MetasDosIndicadores />', () => {
  beforeEach(() => { vi.resetAllMocks(); });

  it('salva o que foi digitado, e o campo vazio vai como "sem meta"', async () => {
    const usuario = userEvent.setup();
    vi.mocked(lerMetas).mockResolvedValue(dados());
    vi.mocked(salvarMetas).mockResolvedValue(dados());
    abrir();
    const otif = await screen.findByLabelText('Entrega no prazo (OTIF)');
    await usuario.type(otif, '92');
    await usuario.clear(screen.getByLabelText('Saving negociado'));
    await usuario.click(screen.getByRole('button', { name: 'Salvar metas' }));
    await waitFor(() => expect(salvarMetas).toHaveBeenCalledWith([
      { indicator: 'saving', monthlyValue: null },
      { indicator: 'otif', monthlyValue: 92 },
    ]));
  });

  it('quem não é administrador lê as metas, mas não altera', async () => {
    vi.mocked(lerMetas).mockResolvedValue(dados(false));
    abrir();
    expect(await screen.findByLabelText('Saving negociado')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Salvar metas' })).not.toBeInTheDocument();
    expect(screen.getByText('Só o administrador altera as metas.')).toBeInTheDocument();
  });
});
