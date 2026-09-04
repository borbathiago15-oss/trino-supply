import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LinhaScorecard } from '@/api/analytics';
import { ToastProvider } from '@/componentes/Toast';
import { Scorecard } from './Scorecard';

vi.mock('@/api/analytics', async (importar) => ({
  ...(await importar<typeof import('@/api/analytics')>()),
  scorecardDeFornecedores: vi.fn(),
}));

import { scorecardDeFornecedores } from '@/api/analytics';

const linha = (p: Partial<LinhaScorecard>): LinhaScorecard => ({
  supplierId: 's1', supplierName: 'Alfa EPIs', score: 92, grade: 'A',
  otifPercent: 95, otifMeasured: 8, qualityPercent: 98, deliveredQuantity: 200, rejectedQuantity: 4,
  winRatePercent: 60, proposals: 5, wins: 3, orders: 8, totalValue: 42000,
  riskScore: 10, riskLevel: 'BAIXO', riskFactors: [],
  ...p,
});

const abrir = () => render(<ToastProvider><Scorecard /></ToastProvider>);

describe('tela Scorecard de Fornecedores', () => {
  beforeEach(() => vi.resetAllMocks());

  it('abre nos últimos 6 meses e refaz a leitura ao mudar a janela', async () => {
    const usuario = userEvent.setup();
    vi.mocked(scorecardDeFornecedores).mockResolvedValue([linha({})]);
    abrir();
    await screen.findByTestId('tabela-scorecard');
    expect(scorecardDeFornecedores).toHaveBeenCalledWith(6, expect.anything());

    await usuario.selectOptions(screen.getByLabelText('Janela de análise'), '12');
    await waitFor(() => expect(scorecardDeFornecedores).toHaveBeenCalledWith(12, expect.anything()));
  });

  it('componente sem medição aparece como tal, não como zero', async () => {
    vi.mocked(scorecardDeFornecedores).mockResolvedValue([
      linha({ score: null, grade: null, otifPercent: null, otifMeasured: 0, qualityPercent: null, winRatePercent: null, proposals: 0, wins: 0 }),
    ]);
    abrir();
    const tabela = await screen.findByTestId('tabela-scorecard');
    expect(within(tabela).getAllByText('sem medição').length).toBe(4);
    expect(within(tabela).queryByText('0%')).not.toBeInTheDocument();
  });

  it('o risco mostra o nível e os motivos que o compuseram', async () => {
    vi.mocked(scorecardDeFornecedores).mockResolvedValue([
      linha({ riskScore: 55, riskLevel: 'ALTO', riskFactors: ['certidão vencida (+30)', 'OTIF baixo (60%) (+20)'] }),
    ]);
    abrir();
    const tabela = await screen.findByTestId('tabela-scorecard');
    expect(within(tabela).getByText('Alto · 55')).toBeInTheDocument();
    expect(within(tabela).getByText(/certidão vencida \(\+30\) · OTIF baixo/)).toBeInTheDocument();
  });

  it('sem fornecedor no período a tela explica em vez de mostrar tabela vazia', async () => {
    vi.mocked(scorecardDeFornecedores).mockResolvedValue([]);
    abrir();
    expect(await screen.findByText('Nenhum fornecedor com atividade no período.')).toBeInTheDocument();
    expect(screen.queryByTestId('tabela-scorecard')).not.toBeInTheDocument();
  });
});
