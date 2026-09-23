import { describe, expect, it, vi, afterEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LinkAbaNova } from './LinkAbaNova';

// O cockpit abria em aba nova e pedia login de novo: a sessão mora no sessionStorage,
// que é por aba, e o navegador só a copia quando a aba nova mantém o opener. Estes
// testes seguram as duas metades da correção.
describe('LinkAbaNova', () => {
  afterEach(() => vi.restoreAllMocks());

  const abrir = () => vi.spyOn(window, 'open').mockReturnValue({} as Window);

  it('o clique simples abre pelo window.open, que preserva o opener e leva a sessão', async () => {
    const open = abrir();
    render(<LinkAbaNova href="/cockpit">Modo TV</LinkAbaNova>);

    await userEvent.click(screen.getByRole('link', { name: 'Modo TV' }));

    expect(open).toHaveBeenCalledWith('/cockpit', '_blank');
  });

  it('declara rel="opener": ctrl+clique e botão do meio não passam por handler nenhum', () => {
    render(<LinkAbaNova href="/cockpit">Modo TV</LinkAbaNova>);
    const link = screen.getByRole('link', { name: 'Modo TV' });

    expect(link).toHaveAttribute('rel', 'opener');
    expect(link).toHaveAttribute('target', '_blank');
    // o href fica: é ele que faz os atalhos do navegador existirem
    expect(link).toHaveAttribute('href', '/cockpit');
  });

  it('atalho do navegador segue sendo do navegador', () => {
    const open = abrir();
    render(<LinkAbaNova href="/cockpit">Modo TV</LinkAbaNova>);

    fireEvent.click(screen.getByRole('link', { name: 'Modo TV' }), { ctrlKey: true });

    expect(open).not.toHaveBeenCalled();
  });

  it('pop-up bloqueado não engole o clique: o target="_blank" ainda tem a sua chance', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    const cliques: MouseEvent[] = [];
    render(<LinkAbaNova href="/cockpit" onClick={(e) => cliques.push(e.nativeEvent)}>Modo TV</LinkAbaNova>);

    await userEvent.click(screen.getByRole('link', { name: 'Modo TV' }));

    expect(open).toHaveBeenCalled();
    expect(cliques[0].defaultPrevented).toBe(false);
  });
});
