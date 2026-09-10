import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { quandoChegou, type MeuAviso } from '@/api/meusAvisos';
import { CaixaDeAvisos } from './CaixaDeAvisos';

vi.mock('@/api/meusAvisos', async (importar) => ({
  ...(await importar<typeof import('@/api/meusAvisos')>()),
  listarMeusAvisos: vi.fn(),
  marcarAvisoLido: vi.fn(),
  marcarTodosLidos: vi.fn(),
}));

import { listarMeusAvisos, marcarAvisoLido, marcarTodosLidos } from '@/api/meusAvisos';

const aviso = (p: Partial<MeuAviso> = {}): MeuAviso => ({
  id: 'a1', kind: 'DEMANDA_ATRIBUIDA', title: 'PR-2026-000001 é sua',
  body: 'Marina atribuiu a você a solicitação.', link: '/torre',
  createdAt: '2026-09-10T12:00:00Z', read: false, ...p,
});

const abrir = () => render(<MemoryRouter><CaixaDeAvisos /></MemoryRouter>);

describe('quando o aviso chegou', () => {
  const agora = new Date('2026-09-10T12:00:00Z');

  it('recado recente conta em minutos e horas', () => {
    expect(quandoChegou('2026-09-10T11:58:00Z', agora)).toBe('há 2 min');
    expect(quandoChegou('2026-09-10T09:00:00Z', agora)).toBe('há 3h');
  });

  it('acabou de chegar não diz "há 0 min"', () => {
    expect(quandoChegou('2026-09-10T11:59:40Z', agora)).toBe('agora');
  });

  it('passada uma semana, a data diz mais que a contagem', () => {
    expect(quandoChegou('2026-09-07T12:00:00Z', agora)).toBe('há 3 dias');
    expect(quandoChegou('2026-08-01T12:00:00Z', agora)).toBe('01/08/2026');
  });

  it('data inválida não vira "NaN" na tela', () => {
    expect(quandoChegou('nao-e-data', agora)).toBe('');
  });
});

describe('o sino', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(marcarAvisoLido).mockResolvedValue({ read: true });
    vi.mocked(marcarTodosLidos).mockResolvedValue({ read: 2 });
  });

  it('mostra o número de não lidos', async () => {
    vi.mocked(listarMeusAvisos).mockResolvedValue({ unread: 3, items: [aviso()] });
    abrir();
    expect(await screen.findByTestId('contador-avisos')).toHaveTextContent('3');
  });

  it('acima de nove, o contador não estica o topo da tela', async () => {
    vi.mocked(listarMeusAvisos).mockResolvedValue({ unread: 42, items: [] });
    abrir();
    expect(await screen.findByTestId('contador-avisos')).toHaveTextContent('9+');
  });

  it('sem nada por ler, não há contador nenhum', async () => {
    vi.mocked(listarMeusAvisos).mockResolvedValue({ unread: 0, items: [aviso({ read: true })] });
    abrir();
    await waitFor(() => expect(screen.getByTestId('sino-avisos')).toBeInTheDocument());
    expect(screen.queryByTestId('contador-avisos')).not.toBeInTheDocument();
  });

  it('a caixa só aparece quando se clica', async () => {
    vi.mocked(listarMeusAvisos).mockResolvedValue({ unread: 1, items: [aviso()] });
    abrir();
    await screen.findByTestId('sino-avisos');
    expect(screen.queryByTestId('caixa-avisos')).not.toBeInTheDocument();

    await userEvent.click(screen.getByTestId('sino-avisos'));

    const caixa = screen.getByTestId('caixa-avisos');
    expect(within(caixa).getByText('PR-2026-000001 é sua')).toBeInTheDocument();
    // o corpo diz o que aconteceu; o "quando" tem teste próprio, com relógio fixo —
    // depender do relógio real aqui faria o teste falhar conforme a hora do dia
    expect(within(caixa).getByText(/Marina atribuiu/)).toBeInTheDocument();
  });

  it('abrir a caixa não marca tudo como lido', async () => {
    // lido é o gesto de quem tratou o recado, não o de quem passou o olho
    vi.mocked(listarMeusAvisos).mockResolvedValue({ unread: 1, items: [aviso()] });
    abrir();
    await userEvent.click(await screen.findByTestId('sino-avisos'));

    expect(marcarTodosLidos).not.toHaveBeenCalled();
    expect(marcarAvisoLido).not.toHaveBeenCalled();
  });

  it('marcar um como lido avisa o servidor e recarrega', async () => {
    vi.mocked(listarMeusAvisos).mockResolvedValue({ unread: 1, items: [aviso()] });
    abrir();
    await userEvent.click(await screen.findByTestId('sino-avisos'));
    await userEvent.click(screen.getByRole('button', { name: 'marcar como lido' }));

    await waitFor(() => expect(marcarAvisoLido).toHaveBeenCalledWith('a1'));
    await waitFor(() => expect(listarMeusAvisos).toHaveBeenCalledTimes(3));   // inicial, abertura e recarga
  });

  it('aviso já lido não oferece marcar de novo', async () => {
    vi.mocked(listarMeusAvisos).mockResolvedValue({ unread: 0, items: [aviso({ read: true })] });
    abrir();
    await userEvent.click(await screen.findByTestId('sino-avisos'));

    expect(screen.queryByRole('button', { name: 'marcar como lido' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'marcar tudo como lido' })).not.toBeInTheDocument();
  });

  it('caixa vazia diz o que isso significa', async () => {
    vi.mocked(listarMeusAvisos).mockResolvedValue({ unread: 0, items: [] });
    abrir();
    await userEvent.click(await screen.findByTestId('sino-avisos'));

    expect(screen.getByText(/quando algo passar a ser seu/)).toBeInTheDocument();
  });

  it('o servidor fora do ar não derruba o topo da tela', async () => {
    // o sino é do cabeçalho: um erro aqui não pode levar junto o nome e o botão de sair
    vi.mocked(listarMeusAvisos).mockRejectedValue(new Error('502'));
    abrir();

    expect(await screen.findByTestId('sino-avisos')).toBeInTheDocument();
    expect(screen.queryByTestId('contador-avisos')).not.toBeInTheDocument();
  });
});
