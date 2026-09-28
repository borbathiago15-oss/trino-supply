import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BotaoDeTema } from './BotaoDeTema';

describe('<BotaoDeTema />', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
    vi.stubGlobal('matchMedia', () => ({ matches: false }));
  });

  it('o rótulo diz o que o toque faz, e o toque troca o tema e o guarda', async () => {
    const usuario = userEvent.setup();
    render(<BotaoDeTema />);
    const botao = screen.getByRole('button', { name: 'Tema escuro' });
    expect(botao).toHaveAttribute('aria-pressed', 'false');
    await usuario.click(botao);
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(localStorage.getItem('ts.tema')).toBe('escuro');
    expect(screen.getByRole('button', { name: 'Tema claro' })).toHaveAttribute('aria-pressed', 'true');
  });
});
