import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { GraficoColunas, ListaBarras, moedaCurta, rotuloDoMes, type Serie } from './graficos';

const series: Serie[] = [
  { nome: 'Aprovadas', cor: '#16a34a', valores: [3, 0, 5] },
  { nome: 'Rejeitadas', cor: '#dc2626', valores: [1, 2, 0] },
];

describe('formatos do gráfico', () => {
  it('o mês vira rótulo curto; o que não é mês passa direto', () => {
    expect(rotuloDoMes('2026-09')).toBe('set/26');
    expect(rotuloDoMes('2026-01')).toBe('jan/26');
    expect(rotuloDoMes('Total')).toBe('Total');
  });
  it('valores grandes ficam curtos para caber no eixo', () => {
    expect(moedaCurta(1_250_000)).toBe('1,3 mi');
    expect(moedaCurta(340_000)).toBe('340 mil');
    expect(moedaCurta(820)).toBe('820');
    expect(moedaCurta(-1_500_000)).toBe('-1,5 mi');
  });
});

describe('GraficoColunas', () => {
  it('desenha uma barra por série e mês, com o valor no tooltip', async () => {
    render(<GraficoColunas rotulos={['2026-08', '2026-09']} series={series} titulo="Por mês" />);
    const grafico = screen.getByRole('img', { name: 'Por mês' });
    // 2 meses × 2 séries; a barra de valor zero continua no DOM, com altura mínima
    // (o rect do clipPath não é barra: fica em <defs>)
    const barras = grafico.querySelectorAll('g[clip-path] rect');
    expect(barras).toHaveLength(4);
    expect(within(grafico).getByText('set/26')).toBeInTheDocument();
    expect(barras[0]).toHaveAttribute('aria-label', 'ago/26 — Aprovadas: 3');
    // o tooltip escuro aparece ao passar o ponteiro, e some ao sair do gráfico
    expect(screen.queryByRole('tooltip')).toBeNull();
    await userEvent.hover(barras[0]);
    expect(screen.getByRole('tooltip')).toHaveTextContent('ago/26 — Aprovadas: 3');
    await userEvent.unhover(grafico);
    expect(screen.queryByRole('tooltip')).toBeNull();
  });

  it('empilhado omite a fatia zerada em vez de desenhar uma barra invisível', () => {
    render(<GraficoColunas rotulos={['2026-08', '2026-09']} series={series} empilhado titulo="Empilhado" />);
    const grafico = screen.getByRole('img', { name: 'Empilhado' });
    // ago tem 3 e 1; set tem 5 e 0 → 3 fatias
    expect(grafico.querySelectorAll('g[clip-path] rect')).toHaveLength(3);
  });

  it('sem meses no período, explica em vez de desenhar um eixo vazio', () => {
    render(<GraficoColunas rotulos={[]} series={series} titulo="Vazio" />);
    expect(screen.getByText('Sem dados no período.')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });
});

describe('ListaBarras', () => {
  it('mostra rótulo e valor de cada linha', () => {
    render(<ListaBarras linhas={[{ label: 'Alfa EPIs', value: 12000 }, { label: 'Beta', value: 3000 }]} />);
    expect(screen.getByText('Alfa EPIs')).toBeInTheDocument();
    expect(screen.getByText('R$ 12.000,00')).toBeInTheDocument();
  });
  it('lista vazia explica o período', () => {
    render(<ListaBarras linhas={[]} />);
    expect(screen.getByText('Sem dados no período.')).toBeInTheDocument();
  });
});
