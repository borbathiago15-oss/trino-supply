import { render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { componenteDoScore, type LinhaDeScore, type MapaDeScore } from '@/api/cotacoes';
import { PainelDeScore } from './PainelDeScore';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  mapaDeScore: vi.fn(),
}));

import { mapaDeScore } from '@/api/cotacoes';

const linha = (p: Partial<LinhaDeScore> = {}): LinhaDeScore => ({
  supplierId: 's1', supplierName: 'Pernambuco Distribuidora', score: 88.4,
  pricePct: 100, deliveryPct: 80, paymentPct: 60, otifPct: 92, riskPct: 90, ...p,
});

const mapa = (p: Partial<MapaDeScore> = {}): MapaDeScore => ({
  note: 'Score informativo: a escolha continua sendo do comprador.',
  criteria: [
    { code: 'price', label: 'Preço', weightPct: 40, help: 'O menor total vale 100.' },
    { code: 'delivery', label: 'Prazo de entrega', weightPct: 20, help: 'O prazo mais curto vale 100.' },
    { code: 'payment', label: 'Prazo de pagamento', weightPct: 10, help: 'O prazo mais longo vale 100.' },
    { code: 'otif', label: 'OTIF histórico', weightPct: 20, help: 'Entregas no prazo.' },
    { code: 'risk', label: 'Risco interno', weightPct: 10, help: 'Quanto maior, menos risco.' },
  ],
  items: [linha()],
  ...p,
});

const abrir = () => render(<PainelDeScore processoId="q1" />);

describe('leitura do componente do score', () => {
  it('cada critério lê o seu campo, e o desconhecido é nulo em vez de zero', () => {
    const l = linha({ otifPct: null });
    expect(componenteDoScore(l, 'price')).toBe(100);
    expect(componenteDoScore(l, 'otif')).toBeNull();
    expect(componenteDoScore(l, 'inventado')).toBeNull();
  });
});

describe('painel do score multicritério', () => {
  beforeEach(() => vi.resetAllMocks());

  it('mostra a nota de cada fornecedor com o peso de cada critério à vista', async () => {
    vi.mocked(mapaDeScore).mockResolvedValue(mapa());
    abrir();
    const tabela = await screen.findByTestId('mapa-score');
    expect(within(tabela).getByText('88.4')).toBeInTheDocument();
    expect(within(tabela).getByText('peso 40%')).toBeInTheDocument();
    expect(screen.getByText(/Preço 40%/)).toBeInTheDocument();
  });

  it('o primeiro colocado é apontado, mas só quando há disputa', async () => {
    vi.mocked(mapaDeScore).mockResolvedValue(mapa({
      items: [linha(), linha({ supplierId: 's2', supplierName: 'Norte EPI', score: 71.2 })],
    }));
    abrir();
    const tabela = await screen.findByTestId('mapa-score');
    expect(within(tabela).getByText('melhor score')).toBeInTheDocument();
  });

  it('com um proponente só não há "melhor score" — não houve com quem comparar', async () => {
    vi.mocked(mapaDeScore).mockResolvedValue(mapa());
    abrir();
    await screen.findByTestId('mapa-score');
    expect(screen.queryByText('melhor score')).not.toBeInTheDocument();
  });

  it('critério sem dado sai como traço, não como zero', async () => {
    vi.mocked(mapaDeScore).mockResolvedValue(mapa({ items: [linha({ otifPct: null })] }));
    abrir();
    const tabela = await screen.findByTestId('mapa-score');
    expect(within(tabela).getByTitle(/sem dado/)).toHaveTextContent('—');
    expect(within(tabela).queryByText('0')).not.toBeInTheDocument();
  });

  it('sem proposta com total lançado, diz isso em vez de tabela vazia', async () => {
    vi.mocked(mapaDeScore).mockResolvedValue(mapa({ items: [] }));
    abrir();
    expect(await screen.findByText(/Nenhuma proposta com total lançado/)).toBeInTheDocument();
    expect(screen.queryByTestId('mapa-score')).not.toBeInTheDocument();
  });
});
