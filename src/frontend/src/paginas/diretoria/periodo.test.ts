import { describe, expect, it } from 'vitest';
import { intervaloDoPeriodo } from './periodo';

describe('período da visão da diretoria', () => {
  const hoje = new Date('2026-09-20T15:00:00Z');
  it('este mês vai do dia 1 até hoje', () => {
    expect(intervaloDoPeriodo('mes', hoje)).toEqual({ de: '2026-09-01', ate: '2026-09-20' });
  });
  it('os últimos 90 dias contam para trás a partir de hoje', () => {
    expect(intervaloDoPeriodo('trimestre', hoje)).toEqual({ de: '2026-06-22', ate: '2026-09-20' });
  });
  it('este ano vai de 1º de janeiro até hoje', () => {
    expect(intervaloDoPeriodo('ano', hoje)).toEqual({ de: '2026-01-01', ate: '2026-09-20' });
  });
});
