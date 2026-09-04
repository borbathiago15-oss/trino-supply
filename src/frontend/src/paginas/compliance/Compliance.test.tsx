import { render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { classeDoScore, type RelatorioCompliance } from '@/api/analytics';
import { ToastProvider } from '@/componentes/Toast';
import { Compliance } from './Compliance';

vi.mock('@/api/analytics', async (importar) => ({
  ...(await importar<typeof import('@/api/analytics')>()),
  relatorioDeCompliance: vi.fn(),
}));

import { relatorioDeCompliance } from '@/api/analytics';

const relatorio = (p: Partial<RelatorioCompliance>): RelatorioCompliance => ({
  evaluated: 4, concluded: 3, averageScore: 82.5, fullCompliance: 2,
  byBuyer: [{ label: 'Carla', count: 3, averageScore: 86.7 }],
  byCostCenter: [{ label: 'BAH-001', count: 4, averageScore: 82.5 }],
  items: [{
    quotationId: 'q1', number: 'RFQ-2026-000001', kind: 'Cotação', status: 'AGUARDANDO_GERENTE',
    costCenter: 'BAH-001', buyerLabel: 'Carla', score: 100, penalties: [],
  }],
  ...p,
});

const abrir = () => render(<ToastProvider><Compliance /></ToastProvider>);

describe('leitura do Compliance Score', () => {
  it('100 é conforme, abaixo de 70 é grave', () => {
    expect(classeDoScore(100)).toContain('text-ok');
    expect(classeDoScore(80)).toContain('text-aviso');
    expect(classeDoScore(55)).toContain('text-perigo');
  });
});

describe('tela Compliance', () => {
  beforeEach(() => vi.resetAllMocks());

  it('o processo sem penalidade aparece como conforme', async () => {
    vi.mocked(relatorioDeCompliance).mockResolvedValue(relatorio({}));
    abrir();
    const tabela = await screen.findByTestId('tabela-compliance');
    expect(within(tabela).getByText('nenhuma ✔')).toBeInTheDocument();
    expect(screen.getByText('4 processo(s) avaliado(s)')).toBeInTheDocument();
  });

  it('cada penalidade mostra os pontos, o motivo e a evidência', async () => {
    vi.mocked(relatorioDeCompliance).mockResolvedValue(relatorio({
      items: [{
        quotationId: 'q2', number: 'RFQ-2026-000002', kind: 'Emergencial', status: 'OC_REGISTRADA',
        costCenter: 'BAH-001', buyerLabel: 'Carla', score: 55,
        penalties: [
          { code: 'CP-01', label: 'sem cotação competitiva', points: 25, evidence: '1 proposta recebida' },
          { code: 'CP-02', label: 'compra emergencial', points: 20, evidence: 'prioridade URGENTE' },
        ],
      }],
    }));
    abrir();
    const tabela = await screen.findByTestId('tabela-compliance');
    expect(within(tabela).getByText('−25')).toBeInTheDocument();
    expect(within(tabela).getByText('1 proposta recebida')).toBeInTheDocument();
    expect(within(tabela).getByText('O.C. registrada')).toBeInTheDocument();
  });

  it('as médias saem por comprador e por centro de custo', async () => {
    vi.mocked(relatorioDeCompliance).mockResolvedValue(relatorio({}));
    abrir();
    const porComprador = await screen.findByTestId('media-por-comprador');
    expect(within(porComprador).getByText('Carla')).toBeInTheDocument();
    expect(within(porComprador).getByText('86.7')).toBeInTheDocument();
    expect(within(screen.getByTestId('media-por-centro')).getByText('82.5')).toBeInTheDocument();
  });
});
