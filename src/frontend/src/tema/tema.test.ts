import { beforeEach, describe, expect, it, vi } from 'vitest';
import { aplicarTema, guardarTema, outroTema, temaPreferido } from './tema';

describe('o tema é escolha da pessoa, guardada no navegador dela', () => {
  beforeEach(() => { localStorage.clear(); document.documentElement.classList.remove('dark'); });

  it('sem escolha, segue o sistema operacional', () => {
    vi.stubGlobal('matchMedia', (q: string) => ({ matches: q.includes('dark') }));
    expect(temaPreferido()).toBe('escuro');
    vi.stubGlobal('matchMedia', () => ({ matches: false }));
    expect(temaPreferido()).toBe('claro');
  });

  it('a escolha guardada vence o sistema, e aplicar põe a classe no <html>', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }));
    guardarTema('claro');
    expect(temaPreferido()).toBe('claro');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
    aplicarTema('escuro');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(outroTema('escuro')).toBe('claro');
  });

  it('valor estranho no armazenamento é ignorado', () => {
    localStorage.setItem('ts.tema', 'roxo');
    vi.stubGlobal('matchMedia', () => ({ matches: false }));
    expect(temaPreferido()).toBe('claro');
  });
});
