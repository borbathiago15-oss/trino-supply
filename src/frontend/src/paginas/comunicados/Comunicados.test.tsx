import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Comunicado as TipoComunicado } from '@/api/comunicados';
import { ToastProvider } from '@/componentes/Toast';
import { Comunicados, situacaoDoComunicado } from './Comunicados';

vi.mock('@/api/comunicados', async (importar) => ({
  ...(await importar<typeof import('@/api/comunicados')>()),
  listarComunicados: vi.fn(), criarComunicado: vi.fn(), atualizarComunicado: vi.fn(),
  excluirComunicado: vi.fn(), enviarImagemDoComunicado: vi.fn(),
}));

import {
  atualizarComunicado, criarComunicado, enviarImagemDoComunicado, listarComunicados,
} from '@/api/comunicados';

const comunicado = (p: Partial<TipoComunicado> = {}): TipoComunicado => ({
  id: 'c1', title: 'Parada do sistema no sábado', body: 'Das 8h às 12h.',
  imageDocumentId: null, imageFileName: null,
  startsOn: '2026-09-08', endsOn: '2026-09-12', active: true,
  createdByLabel: 'Administrador', createdAt: '2026-09-07T12:00:00Z', dismissedCount: 4,
  ...p,
});

const abrir = () => render(<ToastProvider><Comunicados /></ToastProvider>);

describe('situação do comunicado', () => {
  // a lista não pode obrigar o administrador a comparar datas de cabeça
  const c = comunicado();
  it('antes da vigência está agendado', () => {
    expect(situacaoDoComunicado(c, '2026-09-07').rotulo).toBe('AGENDADO');
  });
  it('dentro da vigência está no ar, incluindo as duas pontas', () => {
    expect(situacaoDoComunicado(c, '2026-09-08').rotulo).toBe('NO AR');
    expect(situacaoDoComunicado(c, '2026-09-12').rotulo).toBe('NO AR');
  });
  it('depois da vigência está encerrado', () => {
    expect(situacaoDoComunicado(c, '2026-09-13').rotulo).toBe('ENCERRADO');
  });
  it('desligado tem precedência sobre a data', () => {
    expect(situacaoDoComunicado(comunicado({ active: false }), '2026-09-10').rotulo).toBe('DESLIGADO');
  });
});

describe('tela de Comunicados', () => {
  beforeEach(() => vi.resetAllMocks());

  it('lista o que existe, com vigência e quantos já fecharam', async () => {
    vi.mocked(listarComunicados).mockResolvedValue([comunicado()]);
    abrir();

    const tabela = within(await screen.findByTestId('tabela-comunicados'));
    expect(tabela.getByText('Parada do sistema no sábado')).toBeInTheDocument();
    expect(tabela.getByText('08/09/2026 a 12/09/2026')).toBeInTheDocument();
    expect(tabela.getByText('4')).toBeInTheDocument();
  });

  it('publica com título e vigência, e sobe a imagem depois de o comunicado existir', async () => {
    // a imagem precisa do id para se prender: sobe na segunda chamada, não na primeira
    const usuario = userEvent.setup();
    vi.mocked(listarComunicados).mockResolvedValue([]);
    vi.mocked(criarComunicado).mockResolvedValue(comunicado({ id: 'novo' }));
    vi.mocked(enviarImagemDoComunicado).mockResolvedValue(
      { documentId: 'd1', fileName: 'cartaz.png', announcement: comunicado() });
    abrir();

    await usuario.type(await screen.findByLabelText('Título'), 'Novo fornecedor de EPI');
    await usuario.clear(screen.getByLabelText('Vigente de'));
    await usuario.type(screen.getByLabelText('Vigente de'), '2026-09-10');
    await usuario.clear(screen.getByLabelText('Até'));
    await usuario.type(screen.getByLabelText('Até'), '2026-09-20');

    const arquivo = new File(['x'], 'cartaz.png', { type: 'image/png' });
    await usuario.upload(screen.getByLabelText(/Imagem/), arquivo);
    await usuario.click(screen.getByRole('button', { name: 'Publicar comunicado' }));

    await waitFor(() => expect(criarComunicado).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Novo fornecedor de EPI', startsOn: '2026-09-10', endsOn: '2026-09-20', active: true,
    })));
    await waitFor(() => expect(enviarImagemDoComunicado).toHaveBeenCalledWith('novo', arquivo));
  });

  it('vigência invertida trava o botão antes de chegar ao servidor', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarComunicados).mockResolvedValue([]);
    abrir();

    await usuario.type(await screen.findByLabelText('Título'), 'Aviso qualquer');
    await usuario.clear(screen.getByLabelText('Vigente de'));
    await usuario.type(screen.getByLabelText('Vigente de'), '2026-09-20');
    await usuario.clear(screen.getByLabelText('Até'));
    await usuario.type(screen.getByLabelText('Até'), '2026-09-10');

    expect(screen.getByRole('button', { name: 'Publicar comunicado' })).toBeDisabled();
    expect(screen.getByText(/não pode ser anterior à inicial/)).toBeInTheDocument();
    expect(criarComunicado).not.toHaveBeenCalled();
  });

  it('tirar do ar não apaga: só desliga', async () => {
    const usuario = userEvent.setup();
    vi.mocked(listarComunicados).mockResolvedValue([comunicado()]);
    vi.mocked(atualizarComunicado).mockResolvedValue(comunicado({ active: false }));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Tirar do ar' }));
    await waitFor(() => expect(atualizarComunicado).toHaveBeenCalledWith('c1', { active: false }));
  });
});
