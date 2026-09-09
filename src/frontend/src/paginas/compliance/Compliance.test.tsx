import { render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  classeDoScore, type LinhaConcentracao, type RelatorioCompliance,
} from '@/api/analytics';
import { ToastProvider } from '@/componentes/Toast';
import { Compliance } from './Compliance';

vi.mock('@/api/analytics', async (importar) => ({
  ...(await importar<typeof import('@/api/analytics')>()),
  relatorioDeCompliance: vi.fn(),
  concentracaoDeFornecedor: vi.fn(),
}));

import { concentracaoDeFornecedor, relatorioDeCompliance } from '@/api/analytics';

const risco = (p: Partial<LinhaConcentracao> = {}): LinhaConcentracao => ({
  catalogItemId: 'c1', description: 'BOTA BIQUEIRA DE PVC', total: 12000, purchases: 6,
  suppliers: 2, level: 'CRITICO', topSupplierId: 's1', topSupplier: 'Pernambuco Distribuidora',
  topShare: 95, topValue: 11400,
  recommendation: '95% das compras saem com Pernambuco Distribuidora. '
    + 'Leve o próximo processo a mais fornecedores para reduzir a dependência.',
  ...p,
});

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
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(concentracaoDeFornecedor).mockResolvedValue({ minPurchases: 3, items: [] });
  });

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

describe('risco de concentração', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(relatorioDeCompliance).mockResolvedValue(relatorio({}));
  });

  it('a linha diz a fatia, o fornecedor dominante e o que fazer', async () => {
    vi.mocked(concentracaoDeFornecedor).mockResolvedValue({ minPurchases: 3, items: [risco()] });
    abrir();
    const tabela = await screen.findByTestId('tabela-concentracao');
    expect(within(tabela).getByText('95%')).toBeInTheDocument();
    expect(within(tabela).getByText('Pernambuco Distribuidora')).toBeInTheDocument();
    expect(within(tabela).getByText('Crítico')).toBeInTheDocument();
    expect(within(tabela).getByText(/reduzir a dependência/)).toBeInTheDocument();
  });

  it('conta os críticos à parte, porque não é toda dependência que urge', async () => {
    vi.mocked(concentracaoDeFornecedor).mockResolvedValue({
      minPurchases: 3,
      items: [risco(), risco({ catalogItemId: 'c2', level: 'ATENCAO', topShare: 74 })],
    });
    abrir();
    await screen.findByTestId('tabela-concentracao');
    const criticos = screen.getByText('Críticos').parentElement!;
    expect(within(criticos).getByText('1')).toBeInTheDocument();
    expect(within(screen.getByText('Produtos em risco').parentElement!).getByText('2'))
      .toBeInTheDocument();
    expect(screen.getByText('Atenção')).toBeInTheDocument();
  });

  it('sem produto concentrado a tela diz isso, em vez de tabela vazia', async () => {
    vi.mocked(concentracaoDeFornecedor).mockResolvedValue({ minPurchases: 3, items: [] });
    abrir();
    expect(await screen.findByText(/Nenhum produto com dependência relevante/)).toBeInTheDocument();
    expect(screen.queryByTestId('tabela-concentracao')).not.toBeInTheDocument();
  });
});
