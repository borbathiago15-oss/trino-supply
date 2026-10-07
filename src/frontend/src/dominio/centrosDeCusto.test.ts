import { describe, expect, it } from 'vitest';
import type { CentroCusto } from '@/api/centrosCusto';
import { mapaDeNomes, rotuloDoCentro } from './centrosDeCusto';

const centro = (code: string, name: string) => ({ code, name } as CentroCusto);

describe('o centro de custo na tela', () => {
  const nomes = mapaDeNomes([centro('PER-001', 'Suprimentos Pernambuco'), centro('BAH-003', 'Obra Bahia')]);

  it('mostra o nome quando o cadastro conhece o centro', () => {
    // "PER-001" não diz se a compra é da obra ou do administrativo
    expect(rotuloDoCentro(nomes, 'PER-001')).toBe('Suprimentos Pernambuco');
  });

  it('o código é identidade, então a comparação não se perde em caixa nem espaço', () => {
    expect(rotuloDoCentro(nomes, ' per-001 ')).toBe('Suprimentos Pernambuco');
  });

  it('centro fora do cadastro continua aparecendo pelo código, não como traço', () => {
    // ele some do cadastro, mas a solicitação dele continua existindo — esconder a que
    // centro ela pertence seria pior que mostrar o código cru
    expect(rotuloDoCentro(nomes, 'SEM-001')).toBe('SEM-001');
    expect(rotuloDoCentro({}, 'BAH-003')).toBe('BAH-003');
  });

  it('sem centro nenhum, aí sim é traço', () => {
    expect(rotuloDoCentro(nomes, null)).toBe('—');
    expect(rotuloDoCentro(nomes, '')).toBe('—');
  });
});
