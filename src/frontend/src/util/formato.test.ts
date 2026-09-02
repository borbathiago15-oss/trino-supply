import { describe, expect, it } from 'vitest';
import { data, moeda, quantidade } from './formato';

describe('formato', () => {
  it('moeda em pt-BR', () => {
    expect(moeda(1234.5).replace(/\u00a0/g, ' ')).toBe('R$ 1.234,50');
    expect(moeda(null).replace(/\u00a0/g, ' ')).toBe('R$ 0,00');
  });
  it('quantidade com no máximo uma casa, como o legado', () => {
    expect(quantidade(10)).toBe('10');
    expect(quantidade(2.55)).toBe('2,6');
    expect(quantidade('x')).toBe('0');
  });
  it('data DateOnly vira dd/MM/yyyy', () => {
    expect(data('2026-09-02')).toBe('02/09/2026');
    expect(data(null)).toBe('—');
    expect(data('ontem')).toBe('ontem');
  });
});
