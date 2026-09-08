import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Comunicado } from '@/api/comunicados';
import { ModalDeComunicados } from './Comunicados';

vi.mock('@/api/comunicados', async (importar) => ({
  ...(await importar<typeof import('@/api/comunicados')>()),
  comunicadosDoMomento: vi.fn(), fecharComunicado: vi.fn(), urlDaImagem: vi.fn(),
}));

import { comunicadosDoMomento, fecharComunicado, urlDaImagem } from '@/api/comunicados';

const comunicado = (p: Partial<Comunicado> = {}): Comunicado => ({
  id: 'c1', title: 'Parada do sistema no sábado',
  body: 'O sistema fica fora do ar das 8h às 12h.',
  imageDocumentId: null, imageFileName: null,
  startsOn: '2026-09-08', endsOn: '2026-09-12', active: true,
  createdByLabel: 'Administrador', createdAt: '2026-09-07T12:00:00Z', dismissedCount: 0,
  ...p,
});

describe('comunicado ao abrir o sistema', () => {
  beforeEach(() => vi.resetAllMocks());

  it('mostra o recado com a vigência e some ao ser fechado', async () => {
    const usuario = userEvent.setup();
    vi.mocked(comunicadosDoMomento).mockResolvedValue([comunicado()]);
    vi.mocked(fecharComunicado).mockResolvedValue({ dismissed: true });
    render(<ModalDeComunicados />);

    expect(await screen.findByText('Parada do sistema no sábado')).toBeInTheDocument();
    expect(screen.getByText(/vigente de 08\/09\/2026 a 12\/09\/2026/)).toBeInTheDocument();

    await usuario.click(screen.getByRole('button', { name: 'Entendi, fechar' }));

    await waitFor(() => expect(fecharComunicado).toHaveBeenCalledWith('c1'));
    await waitFor(() => expect(screen.queryByTestId('comunicado')).not.toBeInTheDocument());
  });

  it('com mais de um, mostra um de cada vez e diz quantos faltam', async () => {
    const usuario = userEvent.setup();
    vi.mocked(comunicadosDoMomento).mockResolvedValue([
      comunicado(), comunicado({ id: 'c2', title: 'Novo fornecedor de EPI' }),
    ]);
    vi.mocked(fecharComunicado).mockResolvedValue({ dismissed: true });
    render(<ModalDeComunicados />);

    await screen.findByText('Parada do sistema no sábado');
    expect(screen.getByText('Mais 1 comunicado(s) depois deste.')).toBeInTheDocument();
    // uma pilha de recados sobrepostos não é lida: o segundo só aparece depois
    expect(screen.queryByText('Novo fornecedor de EPI')).not.toBeInTheDocument();

    await usuario.click(screen.getByRole('button', { name: 'Entendi, fechar' }));
    expect(await screen.findByText('Novo fornecedor de EPI')).toBeInTheDocument();
  });

  it('o cartaz é buscado com a sessão, e falhar nele não esconde o recado', async () => {
    vi.mocked(comunicadosDoMomento).mockResolvedValue([
      comunicado({ imageDocumentId: 'doc1', imageFileName: 'cartaz.png' }),
    ]);
    vi.mocked(urlDaImagem).mockRejectedValue(new Error('sem acesso'));
    render(<ModalDeComunicados />);

    expect(await screen.findByText('Parada do sistema no sábado')).toBeInTheDocument();
    await waitFor(() => expect(urlDaImagem).toHaveBeenCalledWith('c1'));
    expect(screen.getByText('O sistema fica fora do ar das 8h às 12h.')).toBeInTheDocument();
  });

  it('sem comunicado no ar, não ocupa a tela com nada', async () => {
    vi.mocked(comunicadosDoMomento).mockResolvedValue([]);
    render(<ModalDeComunicados />);
    await waitFor(() => expect(comunicadosDoMomento).toHaveBeenCalled());
    expect(screen.queryByTestId('comunicado')).not.toBeInTheDocument();
  });

  it('falha ao ler comunicado não impede o sistema de abrir', async () => {
    vi.mocked(comunicadosDoMomento).mockRejectedValue(new Error('servidor fora'));
    render(<ModalDeComunicados />);
    await waitFor(() => expect(comunicadosDoMomento).toHaveBeenCalled());
    expect(screen.queryByTestId('comunicado')).not.toBeInTheDocument();
  });
});
