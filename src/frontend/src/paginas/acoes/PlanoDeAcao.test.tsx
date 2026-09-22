import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Acao, PaginaDeAcoes } from '@/api/acoes';
import { exigeMotivo } from '@/api/acoes';
import { ToastProvider } from '@/componentes/Toast';
import { PlanoDeAcaoTela, rotuloDaLinha, tomDaLinha } from './PlanoDeAcao';

vi.mock('@/api/acoes', async (importar) => ({
  ...(await importar<typeof import('@/api/acoes')>()),
  listarAcoes: vi.fn(),
  mudarStatusDaAcao: vi.fn(),
  criarAcao: vi.fn(),
}));
vi.mock('@/api/usuarios', () => ({ listarUsuariosPicker: vi.fn().mockResolvedValue([]) }));
vi.mock('@/api/centrosCusto', () => ({ listarCentrosCusto: vi.fn().mockResolvedValue([]) }));

import { listarAcoes, mudarStatusDaAcao } from '@/api/acoes';

const acao = (p: Partial<Acao>): Acao => ({
  id: 'a1', number: 'AC-2026-000001', title: 'Refazer o layout da doca',
  reason: null, area: null, responsibleId: 'u1', responsibleLabel: 'Carla Compradora',
  startDate: null, dueDate: '2026-09-01', expectedGain: null, realizedGain: null,
  expectedResult: null, kpi: null, costCenter: 'CC-01', status: 'PENDENTE',
  statusReason: null, progress: 0, late: false, daysLate: null, open: true,
  completedAt: null, createdByLabel: 'Carla', createdAt: '2026-08-01T00:00:00Z', ...p,
});

const pagina = (itens: Acao[], placar?: Partial<PaginaDeAcoes['placar']>): PaginaDeAcoes => ({
  itens,
  placar: {
    total: itens.length, pendentes: 0, emAndamento: 0, concluidas: 0, suspensas: 0,
    canceladas: 0, atrasadas: 0, ganhoEsperado: 0, ganhoRealizado: 0, ...placar,
  },
  opcoes: { responsibles: [{ id: 'u1', label: 'Carla Compradora' }], costCenters: ['CC-01'] },
});

const abrir = () => render(<ToastProvider><PlanoDeAcaoTela /></ToastProvider>);

describe('leitura da linha', () => {
  it('a suspensa e vencida diz "suspensa", e não "atrasada"', () => {
    // é a regra do módulo: ela está parada por decisão, e chamá-la de atrasada
    // cobraria a equipe por uma decisão da gestão
    const suspensa = acao({ status: 'SUSPENSA', dueDate: '2026-01-01', late: false });
    expect(rotuloDaLinha(suspensa)).toBe('Suspensa');
    expect(tomDaLinha(suspensa)).not.toContain('perigo');
  });

  it('a atrasada diz há quantos dias', () => {
    expect(rotuloDaLinha(acao({ late: true, daysLate: 5 }))).toBe('Atrasada há 5d');
    expect(tomDaLinha(acao({ late: true, daysLate: 5 }))).toContain('perigo');
  });

  it('suspender e cancelar exigem motivo; as outras não', () => {
    expect(exigeMotivo('SUSPENSA')).toBe(true);
    expect(exigeMotivo('CANCELADA')).toBe(true);
    expect(exigeMotivo('EM_ANDAMENTO')).toBe(false);
    expect(exigeMotivo('CONCLUIDA')).toBe(false);
  });
});

describe('<PlanoDeAcaoTela />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listarAcoes).mockResolvedValue(pagina([acao({})]));
  });

  it('o placar separa suspensa de atrasada', async () => {
    vi.mocked(listarAcoes).mockResolvedValue(pagina(
      [acao({}), acao({ id: 'a2', number: 'AC-2026-000002', status: 'SUSPENSA' })],
      { suspensas: 1, atrasadas: 1 }));
    abrir();
    const placar = await screen.findByTestId('placar-das-acoes');
    expect(within(placar).getByText('Suspensas')).toBeInTheDocument();
    expect(within(placar).getByText('Atrasadas')).toBeInTheDocument();
  });

  it('suspender abre o diálogo e só confirma com motivo', async () => {
    const usuario = userEvent.setup();
    abrir();
    await screen.findByTestId('tabela-acoes');

    await usuario.selectOptions(screen.getByLabelText('Mudar situação de AC-2026-000001'), 'SUSPENSA');
    const confirmar = await screen.findByRole('button', { name: 'Confirmar' });
    expect(confirmar).toBeDisabled();
    // sem motivo não passa: parar o trabalho de alguém sem dizer por quê é o que faz
    // a ação ficar meses parada sem dono da decisão
    expect(mudarStatusDaAcao).not.toHaveBeenCalled();

    await usuario.type(screen.getByLabelText('Motivo'), 'Obra parada pela diretoria.');
    await usuario.click(screen.getByRole('button', { name: 'Confirmar' }));
    await waitFor(() => expect(mudarStatusDaAcao).toHaveBeenCalledWith(
      'a1', 'SUSPENSA', 'Obra parada pela diretoria.'));
  });

  it('concluir não pede motivo: vai direto', async () => {
    const usuario = userEvent.setup();
    vi.mocked(mudarStatusDaAcao).mockResolvedValue(acao({ status: 'CONCLUIDA' }));
    abrir();
    await screen.findByTestId('tabela-acoes');

    await usuario.selectOptions(screen.getByLabelText('Mudar situação de AC-2026-000001'), 'CONCLUIDA');
    await waitFor(() => expect(mudarStatusDaAcao).toHaveBeenCalledWith('a1', 'CONCLUIDA'));
    expect(screen.queryByRole('button', { name: 'Confirmar' })).not.toBeInTheDocument();
  });

  it('ação encerrada não oferece mudar situação', async () => {
    vi.mocked(listarAcoes).mockResolvedValue(pagina([acao({ status: 'CONCLUIDA', open: false })]));
    abrir();
    await screen.findByTestId('tabela-acoes');
    expect(screen.queryByLabelText(/Mudar situação/)).not.toBeInTheDocument();
  });

  it('o filtro de atrasadas vai para o servidor', async () => {
    const usuario = userEvent.setup();
    abrir();
    await screen.findByTestId('tabela-acoes');
    await usuario.click(screen.getByLabelText('Só atrasadas'));
    await waitFor(() => expect(listarAcoes).toHaveBeenCalledWith(
      expect.objectContaining({ atrasadas: true }), expect.anything()));
  });
});
