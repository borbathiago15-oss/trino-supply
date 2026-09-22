import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Ciclo } from '@/api/melhoria';
import { ToastProvider } from '@/componentes/Toast';
import { Melhoria, tomDaFase, vereditoDaLinha } from './Melhoria';

vi.mock('@/api/melhoria', async (importar) => ({
  ...(await importar<typeof import('@/api/melhoria')>()),
  listarCiclos: vi.fn(),
  criarCiclo: vi.fn(),
}));
import { criarCiclo, listarCiclos } from '@/api/melhoria';

const ciclo = (p: Partial<Ciclo>): Ciclo => ({
  id: 'c-' + (p.code ?? 'PDCA-2026-001'), code: 'PDCA-2026-001',
  title: 'Reduzir avarias na doca 2', scope: 'CENTRO', scopeLabel: 'Centro de custo',
  phase: 'DO', phaseLabel: 'Do — executar', region: null, sectorId: null, areas: null,
  priority: 'NORMAL', ownerId: 'u1', ownerLabel: 'Ana', indicator: 'Avarias/mês',
  baseline: 40, goalValue: 10, unit: 'un', goalDeadline: '2026-12-31', resultValue: null,
  goalMet: null, startDate: '2026-09-01', endDate: null, closedAt: null, closedByLabel: null,
  closedReason: null, createdByLabel: 'Ana', createdAt: '2026-09-01T00:00:00Z', ...p,
});

const resposta = (itens: Ciclo[]) => ({
  items: itens,
  placar: { total: itens.length, plan: 1, do: 1, check: 0, act: 0, encerrados: 1, encerradosComPendencia: 1 },
  options: {
    phases: [{ key: 'PLAN', label: 'Plan — planejar' }, { key: 'DO', label: 'Do — executar' }],
    scopes: [{ key: 'GESTAO', label: 'Gestão / corporativo' }, { key: 'CENTRO', label: 'Centro de custo' }],
    tools: [{ key: 'PARETO', label: 'Pareto', hint: 'Achar a minoria de causas.' }],
  },
});

const montar = () => render(
  <MemoryRouter><ToastProvider><Melhoria /></ToastProvider></MemoryRouter>);

describe('vereditoDaLinha', () => {
  // nulo é "sem veredito" e não "não atingiu": o ciclo em andamento ainda não respondeu,
  // e tratá-lo como reprovado inventaria a resposta
  it('não transforma ciclo em andamento em meta não atingida', () => {
    expect(vereditoDaLinha(ciclo({ goalMet: null }))).toBeNull();
    expect(vereditoDaLinha(ciclo({ goalMet: false }))).toBe('Meta não atingida');
    expect(vereditoDaLinha(ciclo({ goalMet: true }))).toBe('Meta atingida');
  });

  it('encerrado é cinza: ele diz que a decisão foi tomada, não que foi boa', () => {
    expect(tomDaFase(ciclo({ phase: 'ENCERRADO' }))).toContain('slate');
  });
});

describe('<Melhoria />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listarCiclos).mockResolvedValue(resposta([
      ciclo({}),
      ciclo({ code: 'PDCA-2026-002', phase: 'ENCERRADO', phaseLabel: 'Encerrado', goalMet: false }),
    ]));
    vi.mocked(criarCiclo).mockResolvedValue(ciclo({ code: 'PDCA-2026-003' }));
  });

  it('o placar e a lista ficam na mesma tela — porta única', async () => {
    montar();
    const tabela = await screen.findByTestId('tabela-ciclos');
    expect(within(tabela).getAllByText('Reduzir avarias na doca 2')).toHaveLength(2);
    // o número de encerrados com pendência aparece: a contradição é exposta, não escondida
    expect(screen.getByText(/1 com ação em aberto/)).toBeInTheDocument();
  });

  it('filtra por fase e escopo, e o limpar aparece só quando há filtro', async () => {
    montar();
    await screen.findByTestId('tabela-ciclos');
    expect(screen.queryByRole('button', { name: 'Limpar' })).toBeNull();

    await userEvent.selectOptions(screen.getByLabelText('Fase'), 'PLAN');
    await waitFor(() => expect(listarCiclos).toHaveBeenLastCalledWith(
      { q: '', phase: 'PLAN', scope: '' }, expect.anything()));
    expect(screen.getByRole('button', { name: 'Limpar' })).toBeInTheDocument();
  });

  it('o título é cobrado como frase, não como rótulo', async () => {
    montar();
    await screen.findByTestId('tabela-ciclos');
    expect(screen.getByLabelText(/Título/)).toHaveAttribute('minLength', '10');
  });

  it('abre um ciclo novo com título e escopo', async () => {
    montar();
    await screen.findByTestId('tabela-ciclos');
    await userEvent.type(screen.getByLabelText(/Título/), 'Reduzir retrabalho na conferência');
    await userEvent.selectOptions(screen.getByLabelText('Escopo do ciclo'), 'GESTAO');
    await userEvent.click(screen.getByRole('button', { name: 'Abrir ciclo' }));
    await waitFor(() => expect(criarCiclo).toHaveBeenCalledWith(
      { title: 'Reduzir retrabalho na conferência', scope: 'GESTAO' }));
  });
});
