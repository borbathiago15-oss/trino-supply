import { beforeEach, describe, expect, it } from 'vitest';
import { sessao } from './sessao';

const tokens = (n: number) => ({ accessToken: `a${n}`, refreshToken: `r${n}` });

describe('onde a sessão mora', () => {
  beforeEach(() => { sessionStorage.clear(); localStorage.clear(); });

  it('sem "manter conectado" fica na aba, e fechar a aba a apaga', () => {
    sessao.set(tokens(1), false);
    expect(sessionStorage.getItem('ts.refresh')).toBe('r1');
    expect(localStorage.getItem('ts.refresh')).toBeNull();
    expect(sessao.lembrada).toBe(false);
    sessionStorage.clear();                       // a aba fechou
    expect(sessao.ativa).toBe(false);
  });

  it('com "manter conectado" fica no aparelho e sobrevive ao fechar do app', () => {
    sessao.set(tokens(1), true);
    expect(localStorage.getItem('ts.refresh')).toBe('r1');
    expect(sessionStorage.getItem('ts.refresh')).toBeNull();
    expect(sessao.lembrada).toBe(true);
    sessionStorage.clear();                       // o app fechou e abriu de novo
    expect(sessao.ativa).toBe(true);
    expect(sessao.access).toBe('a1');
  });

  it('a renovação mantém o lugar escolhido na entrada', () => {
    sessao.set(tokens(1), true);
    sessao.set(tokens(2));                        // como a renovação grava
    expect(localStorage.getItem('ts.access')).toBe('a2');
    expect(sessionStorage.getItem('ts.access')).toBeNull();

    sessao.set(tokens(3), false);                 // entrou de novo, sem lembrar
    sessao.set(tokens(4));
    expect(sessionStorage.getItem('ts.access')).toBe('a4');
    expect(localStorage.getItem('ts.access')).toBeNull();
  });

  it('entrar de novo nunca deixa a sessão antiga no outro lugar', () => {
    sessao.set(tokens(1), true);
    sessao.set(tokens(2), false);
    expect(localStorage.getItem('ts.refresh')).toBeNull();
    expect(sessao.refresh).toBe('r2');
  });

  it('sair limpa os dois lugares e guarda só a preferência', () => {
    sessao.set(tokens(1), true);
    sessao.clear();
    expect(sessao.ativa).toBe(false);
    expect(localStorage.getItem('ts.refresh')).toBeNull();
    expect(sessao.lembrada).toBe(true);           // a caixa vem marcada no próximo login
  });
});
