import { beforeEach, describe, expect, it } from 'vitest';
import { sessao } from './sessao';

const tokens = (n: number) => ({ accessToken: `a${n}` });

describe('onde a sessão mora', () => {
  beforeEach(() => { sessionStorage.clear(); localStorage.clear(); });

  it('sem "manter conectado" o access token fica na aba, e fechar a aba o apaga', () => {
    sessao.set(tokens(1), false);
    expect(sessionStorage.getItem('ts.access')).toBe('a1');
    expect(localStorage.getItem('ts.access')).toBeNull();
    expect(sessao.lembrada).toBe(false);
    sessionStorage.clear();                       // a aba fechou
    expect(sessao.ativa).toBe(false);
  });

  it('com "manter conectado" fica no aparelho e sobrevive ao fechar do app', () => {
    sessao.set(tokens(1), true);
    expect(localStorage.getItem('ts.access')).toBe('a1');
    expect(sessionStorage.getItem('ts.access')).toBeNull();
    expect(sessao.lembrada).toBe(true);
    sessionStorage.clear();                       // o app fechou e abriu de novo
    expect(sessao.ativa).toBe(true);
    expect(sessao.access).toBe('a1');
  });

  it('o refresh token nunca chega ao navegador por aqui: ele mora no cookie HttpOnly', () => {
    sessao.set({ accessToken: 'a1', refreshToken: 'r1' }, true);
    expect(localStorage.getItem('ts.refresh')).toBeNull();
    expect(sessionStorage.getItem('ts.refresh')).toBeNull();
    expect(JSON.stringify({ ...localStorage, ...sessionStorage })).not.toContain('r1');
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

  it('entrar de novo nunca deixa a sessão antiga no outro lugar, e apaga o refresh de antes do cookie', () => {
    localStorage.setItem('ts.refresh', 'antigo');  // sobrou de uma versão anterior
    sessao.set(tokens(1), true);
    sessao.set(tokens(2), false);
    expect(localStorage.getItem('ts.access')).toBeNull();
    expect(localStorage.getItem('ts.refresh')).toBeNull();
    expect(sessao.access).toBe('a2');
  });

  it('sair limpa os dois lugares e guarda só a preferência', () => {
    sessao.set(tokens(1), true);
    sessao.clear();
    expect(sessao.ativa).toBe(false);
    expect(localStorage.getItem('ts.access')).toBeNull();
    expect(sessao.lembrada).toBe(true);           // a caixa vem marcada no próximo login
  });
});
