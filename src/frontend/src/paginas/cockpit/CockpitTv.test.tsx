import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Cockpit } from '@/api/cockpit';
import { tomDaVariacao, variacao } from '@/api/cockpit';
import { CockpitTv, INTERVALO_DE_ROTACAO } from './CockpitTv';

vi.mock('@/api/cockpit', async (importar) => ({
  ...(await importar<typeof import('@/api/cockpit')>()),
  lerCockpit: vi.fn(),
}));

import { lerCockpit } from '@/api/cockpit';

const leitura = (p: Partial<Cockpit> = {}): Cockpit => ({
  at: '2026-09-16T12:00:00+00:00',
  executivo: {
    valorComprado: { valor: 140000, anterior: 100000 },
    economia: { valor: 12000, anterior: 9000 },
    economiaPercentual: 8.6,
    custosEvitados: { valor: 4000, anterior: 4000 },
    entregasNoPrazoPercent: 94, entregasNoPrazo: 47, entregasConcluidas: 50,
    slaDeComprasPercent: 93, prazoEstourado: 3, emAndamento: 42,
    backlog: 8,
  },
  produtividade: {
    entraramHoje: 18, concluidosHoje: 15, saldo: 3, taxaDeConclusao: 83.3,
    backlog: 8, emCotacao: 12, aguardandoAprovacao: 5, aguardandoOc: 4, urgentes: 3,
    fluxo: [
      { key: 'SOLICITACAO', label: 'Solicitação', quantidade: 18, acimaDoPrazo: 0 },
      { key: 'COTACAO', label: 'Cotação', quantidade: 12, acimaDoPrazo: 3 },
      { key: 'APROVACAO', label: 'Aprovação', quantidade: 5, acimaDoPrazo: 0 },
      { key: 'ORDEM_DE_COMPRA', label: 'Ordem de Compra', quantidade: 4, acimaDoPrazo: 0 },
      { key: 'RECEBIMENTO', label: 'Recebimento', quantidade: 6, acimaDoPrazo: 0 },
    ],
    gargalo: { key: 'COTACAO', label: 'Cotação', quantidade: 12, acimaDoPrazo: 3 },
  },
  alertas: [
    { code: 'ATRASADOS', label: 'atrasados', count: 5, tone: 'alta' },
    { code: 'URGENTES', label: 'urgentes', count: 3, tone: 'media' },
  ],
  ...p,
});

describe('variação e o lado bom dela', () => {
  it('sem base de comparação não inventa percentual', () => {
    // "+100%" vindo de zero parece resultado e não é
    expect(variacao({ valor: 10, anterior: null })).toBeNull();
    expect(variacao({ valor: 10, anterior: 0 })).toBeNull();
    expect(variacao({ valor: 140, anterior: 100 })).toBe(40);
    expect(variacao({ valor: 80, anterior: 100 })).toBe(-20);
  });

  it('o lado bom depende do indicador, não do sinal', () => {
    // pintar tudo de verde quando sobe faria a TV comemorar o próprio atraso
    expect(tomDaVariacao(40, 'maiorMelhor')).toBe('ok');
    expect(tomDaVariacao(40, 'menorMelhor')).toBe('ruim');
    expect(tomDaVariacao(40, 'neutro')).toBe('neutro');
    expect(tomDaVariacao(null, 'maiorMelhor')).toBe('neutro');
    expect(tomDaVariacao(0, 'maiorMelhor')).toBe('neutro');
  });
});

describe('<CockpitTv />', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(lerCockpit).mockResolvedValue(leitura());
  });
  afterEach(() => vi.useRealTimers());

  it('abre no resultado de Suprimentos, com os números do mês', async () => {
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');

    expect(screen.getByTestId('tela-atual')).toHaveTextContent('Resultado de Suprimentos');
    expect(screen.getByTestId('card-Valor comprado')).toHaveTextContent('140.000,00');
    expect(screen.getByTestId('card-Entregas no prazo')).toHaveTextContent('47 de 50 entregas');
  });

  it('cada número carrega a comparação, e o verde não é do sinal', async () => {
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');

    // comprado subiu 40%, mas subir o gasto não é boa nem má notícia
    const comprado = screen.getByTestId('card-Valor comprado');
    expect(comprado).toHaveTextContent('↑ 40% vs. mês anterior');
    expect(within(comprado).getByText(/40%/)).not.toHaveClass('text-emerald-400');

    // economia subiu 33,3% e isso é boa notícia
    const economia = screen.getByTestId('card-Economia');
    expect(within(economia).getByText(/33,3%/)).toHaveClass('text-emerald-400');
  });

  it('o que não tem base de comparação mostra só o número', async () => {
    vi.mocked(lerCockpit).mockResolvedValue(leitura({
      executivo: { ...leitura().executivo, economia: { valor: 5000, anterior: null } },
    }));
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');

    expect(screen.getByTestId('card-Economia')).not.toHaveTextContent('vs. mês anterior');
  });

  it('sem entrega concluída, o percentual não vira 0% — vira travessão', async () => {
    // 0% diria "erramos todas"; o travessão diz "ainda não há o que medir"
    vi.mocked(lerCockpit).mockResolvedValue(leitura({
      executivo: {
        ...leitura().executivo,
        entregasNoPrazoPercent: null, entregasNoPrazo: 0, entregasConcluidas: 0,
      },
    }));
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');

    const card = screen.getByTestId('card-Entregas no prazo');
    expect(card).toHaveTextContent('—');
    expect(card).toHaveTextContent('nenhuma entrega concluída no mês');
  });

  it('a tela gira sozinha, porque ninguém clica numa parede', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');

    await vi.advanceTimersByTimeAsync(INTERVALO_DE_ROTACAO + 100);

    await waitFor(() => expect(screen.getByTestId('tela-produtividade')).toBeInTheDocument());
    expect(screen.getByTestId('tela-atual')).toHaveTextContent('Operação de Compras');
  });

  it('escolher uma tela à mão trava a rotação, para quem está conferindo', async () => {
    const usuario = userEvent.setup();
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');

    await usuario.click(screen.getByTestId('ir-para-PRODUTIVIDADE'));
    expect(screen.getByTestId('tela-produtividade')).toBeInTheDocument();

    vi.useFakeTimers({ shouldAdvanceTime: true });
    await vi.advanceTimersByTimeAsync(INTERVALO_DE_ROTACAO * 2);
    expect(screen.getByTestId('tela-produtividade')).toBeInTheDocument();
  });

  it('o saldo do dia diz para que lado se anda, e não só o número', async () => {
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');
    await userEvent.click(screen.getByTestId('ir-para-PRODUTIVIDADE'));

    const saldo = screen.getByTestId('card-Saldo do dia');
    expect(saldo).toHaveTextContent('+3');
    expect(saldo).toHaveTextContent('backlog aumentando');
  });

  it('o gargalo é apontado na etapa, em vez de virar mais um número', async () => {
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');
    await userEvent.click(screen.getByTestId('ir-para-PRODUTIVIDADE'));

    expect(screen.getByTestId('etapa-COTACAO')).toHaveTextContent('gargalo');
    expect(screen.getByTestId('etapa-SOLICITACAO')).not.toHaveTextContent('gargalo');
  });

  it('a faixa de alertas mostra o que pede ação', async () => {
    render(<CockpitTv />);
    await screen.findByTestId('faixa-alertas');

    expect(screen.getByTestId('alerta-ATRASADOS')).toHaveTextContent('5 atrasados');
    expect(screen.getByTestId('alerta-URGENTES')).toHaveTextContent('3 urgentes');
  });

  it('sem nada a fazer, a faixa some — alarme que grita sempre para de ser lido', async () => {
    vi.mocked(lerCockpit).mockResolvedValue(leitura({ alertas: [] }));
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');

    expect(screen.queryByTestId('faixa-alertas')).not.toBeInTheDocument();
  });

  it('a falha de rede não apaga a parede: ela avisa e mantém o último número bom', async () => {
    // uma TV vazia por causa de um timeout é pior que uma TV com números de um minuto atrás
    vi.mocked(lerCockpit)
      .mockResolvedValueOnce(leitura())
      .mockRejectedValue(new Error('Rede indisponível'));
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<CockpitTv />);
    await screen.findByTestId('tela-executivo');

    await vi.advanceTimersByTimeAsync(31_000);

    // o aviso fica no cabeçalho, que não muda com a rotação
    await waitFor(() => expect(screen.getByTestId('cockpit-erro')).toHaveTextContent('Rede indisponível'));
    // e o último número bom continua na parede
    await userEvent.click(screen.getByTestId('ir-para-EXECUTIVO'));
    expect(screen.getByTestId('card-Valor comprado')).toHaveTextContent('140.000,00');
  });
});
