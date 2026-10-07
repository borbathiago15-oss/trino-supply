import { describe, expect, it } from 'vitest';
import {
  chaveDoPrazo, leituraDoAtendimento, textoDoSla,
  type PrazoDeAtendimento, type SlaDoAtendimento,
} from './prazoDeAtendimento';

const linha = (p: Partial<PrazoDeAtendimento>): PrazoDeAtendimento => ({
  family: 'EPI', maxDays: 2, inherited: false, updatedAt: '', updatedByLabel: '', ...p,
});

describe('a leitura do prazo de atendimento', () => {
  it('diz de quando é a atenção e quando estoura', () => {
    expect(leituraDoAtendimento(10, 80)).toBe('atenção a partir de 8 dia(s), estouro depois de 10');
  });

  it('arredonda a atenção para cima, e nunca para menos de um dia', () => {
    // 80% de 2 é 1,6: avisar "a partir de 1" é o único aviso que chega antes do estouro
    expect(leituraDoAtendimento(2, 80)).toContain('a partir de 2 dia(s)');
    expect(leituraDoAtendimento(1, 80)).toContain('a partir de 1 dia(s)');
  });

  it('zero é dito como desligado, não como "0 dias"', () => {
    // "0 dias" se leria como "tem de sair hoje", o oposto do que zero significa
    expect(leituraDoAtendimento(0, 80)).toBe('sem cobrança de tempo nesta família');
    expect(leituraDoAtendimento(-1, 80)).toBe('sem cobrança de tempo nesta família');
  });
});

describe('a chave com que a tela e o servidor falam da mesma linha', () => {
  it('o padrão é a string vazia; a família é o próprio nome', () => {
    expect(chaveDoPrazo(linha({ family: null }))).toBe('');
    expect(chaveDoPrazo(linha({ family: 'EPI' }))).toBe('EPI');
  });
});

describe('o prazo na linha da fila', () => {
  const sla = (p: Partial<SlaDoAtendimento>): SlaDoAtendimento =>
    ({ maxDays: 2, days: 3, status: 'ESTOURADO', family: 'EPI', ...p });

  it('diz qual família impôs o prazo: a solicitação responde pela mais curta', () => {
    // "2 dias" sem a família é um número que o almoxarife não tem como conferir
    expect(textoDoSla(sla({}))).toBe('3 de 2 dia(s) (EPI)');
  });

  it('sem espera a medir, mostra só o prazo', () => {
    // antes da liberação do Nível 1 a vez é do centro, e não há relógio do almoxarifado
    expect(textoDoSla(sla({ days: null, status: null }))).toBe('prazo de 2 dia(s) (EPI)');
  });

  it('família do padrão não inventa nome', () => {
    expect(textoDoSla(sla({ family: null }))).toBe('3 de 2 dia(s)');
  });

  it('sem prazo nenhum não há frase', () => {
    expect(textoDoSla(null)).toBeNull();
    expect(textoDoSla(undefined)).toBeNull();
  });
});
