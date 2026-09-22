import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Acao, PlanoCompleto, Risco } from '@/api/acoes';
import { ToastProvider } from '@/componentes/Toast';
import { PlanoDetalhe, rotuloDaAcao, tomDaSeveridade } from './Plano';

vi.mock('@/api/acoes', async (importar) => ({
  ...(await importar<typeof import('@/api/acoes')>()),
  abrirPlano: vi.fn(), salvarPlano: vi.fn(), encerrarPlano: vi.fn(), reabrirPlano: vi.fn(),
  criarAcao: vi.fn(), mudarStatusDaAcao: vi.fn(),
  salvarRisco: vi.fn(), apagarRisco: vi.fn(), salvarCausaRaiz: vi.fn(), salvarLicoes: vi.fn(),
}));
vi.mock('@/api/usuarios', () => ({ listarUsuariosPicker: vi.fn() }));

import {
  abrirPlano, criarAcao, encerrarPlano, mudarStatusDaAcao, reabrirPlano,
  salvarCausaRaiz, salvarLicoes, salvarRisco,
} from '@/api/acoes';
import { listarUsuariosPicker } from '@/api/usuarios';

const acao = (p: Partial<Acao>): Acao => ({
  id: 'a1', number: 'AC-2026-000001', seq: 1, planId: 'p1',
  title: 'Afixar o limite de empilhamento',
  reason: null, area: null, supportArea: null,
  responsibleId: 'u1', responsibleLabel: 'Ana',
  startDate: null, dueDate: '2026-10-10',
  expectedGain: null, realizedGain: null, expectedResult: null, kpi: null,
  costCenter: 'BAH-001', rootCauseRef: 'Empilhamento acima do limite',
  complexity: 'MEDIA', riskLevel: 'BAIXA', dependencies: null, evidence: null, comments: null,
  status: 'PENDENTE', statusReason: null,
  progress: 0, late: false, daysLate: null, open: true,
  completedAt: null, createdByLabel: 'Ana', createdAt: '2026-09-01T00:00:00Z', ...p,
});

const risco = (p: Partial<Risco>): Risco => ({
  id: 'r1', description: 'A empilhadeira pode parar na alta',
  probability: 'MUITO_ALTA', impact: 'ALTA', score: 12, severity: 'CRITICO',
  mitigation: 'Contrato de reserva', responsibleId: null, responsibleLabel: null,
  status: 'ABERTO', ...p,
});

const completo = (p: Partial<PlanoCompleto> = {}): PlanoCompleto => ({
  plan: {
    id: 'p1', code: 'AP-2026-001', title: 'Reduzir avarias na doca 2', description: null,
    costCenter: 'BAH-001', areas: ['Operações'], otherArea: null,
    priority: 'ALTA', criticality: 'ALTA', complexity: 'MEDIA',
    category: 'Melhoria Contínua', sponsor: null, managerName: null,
    startDate: '2026-09-01', dueDate: '2026-12-31', completion: 0,
    problem: 'Avarias sobem desde julho', businessReason: 'Custa 40 mil por mês',
    operationalImpact: null, financialImpact: null, kpiAffected: null, targetGoal: null,
    roiExpected: 0, savingExpected: 10000, savingRealized: 4000,
    investmentPlanned: 0, investmentActual: 2000,
    cancelled: false, cancelReason: null, life: 'ATIVO',
    closedAt: null, closedByLabel: null, evidenceNote: null, cycleId: null, autoKey: null,
    responsibles: [{ userId: 'u1', label: 'Ana' }],
    status: 'EM_ANDAMENTO', itemsProgress: 0,
    savingTotalExpected: 15000, savingTotalRealized: 4000, roi: 100,
    closedWithPending: false, itemCount: 1, openItems: 1,
    createdByLabel: 'Ana', createdAt: '2026-09-01T00:00:00Z',
    ...(p.plan ?? {}),
  },
  items: p.items ?? [acao({})],
  risks: p.risks ?? [risco({})],
  rootCause: p.rootCause ?? null,
  lessons: p.lessons ?? null,
});

const montar = () => render(
  <MemoryRouter initialEntries={['/plano-acao/p1']}>
    <ToastProvider>
      <Routes><Route path="/plano-acao/:id" element={<PlanoDetalhe />} /></Routes>
    </ToastProvider>
  </MemoryRouter>);

describe('as leituras da tela', () => {
  it('a severidade crítica é vermelha, e a baixa é verde', () => {
    expect(tomDaSeveridade('CRITICO')).toContain('perigo');
    expect(tomDaSeveridade('BAIXO')).toContain('ok');
  });

  it('a ação suspensa e vencida diz "suspensa", nunca "atrasada"', () => {
    // ela está parada por decisão, e cobrá-la seria culpar a equipe pela decisão da gestão
    expect(rotuloDaAcao(acao({ status: 'SUSPENSA', late: false }))).toBe('Suspensa');
    expect(rotuloDaAcao(acao({ status: 'PENDENTE', late: true, daysLate: 3 })))
      .toBe('Atrasada há 3d');
  });
});

describe('<PlanoDetalhe />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(abrirPlano).mockResolvedValue(completo());
    vi.mocked(listarUsuariosPicker).mockResolvedValue([
      { id: 'u1', name: 'Ana', role: 'Requester' },
    ]);
    vi.mocked(criarAcao).mockResolvedValue(acao({}));
    vi.mocked(mudarStatusDaAcao).mockResolvedValue(acao({ status: 'CONCLUIDA' }));
    vi.mocked(encerrarPlano).mockResolvedValue(completo().plan);
    vi.mocked(reabrirPlano).mockResolvedValue(completo().plan);
    vi.mocked(salvarRisco).mockResolvedValue(risco({}));
    vi.mocked(salvarCausaRaiz).mockResolvedValue({ method: 'CINCO_PORQUES', contentJson: null, mainCause: 'x' });
    vi.mocked(salvarLicoes).mockResolvedValue({
      whatWorked: null, whatFailed: null, lessons: null,
      bestPractice: null, nextSteps: null, recommendation: null,
    });
  });

  it('o cabeçalho mostra o problema, o porquê e o saving com ROI', async () => {
    montar();
    await screen.findByTestId('abas-do-plano');
    expect(screen.getByText('Avarias sobem desde julho')).toBeInTheDocument();
    expect(screen.getByText('Custa 40 mil por mês')).toBeInTheDocument();
    expect(screen.getByText(/ROI 100%/)).toBeInTheDocument();
  });

  it('as cinco abas do plano estão lá', async () => {
    montar();
    const abas = await screen.findByTestId('abas-do-plano');
    for (const a of ['Ações', 'Estratégico', 'Riscos', 'Causa raiz', 'Lições'])
      expect(within(abas).getByRole('button', { name: a })).toBeInTheDocument();
  });

  it('as ações do plano trazem a causa que cada uma ataca', async () => {
    montar();
    const tabela = await screen.findByTestId('acoes-do-plano');
    expect(within(tabela).getByText('Empilhamento acima do limite')).toBeInTheDocument();
  });

  it('inclui uma ação apontando a causa', async () => {
    montar();
    await screen.findByTestId('acoes-do-plano');
    await userEvent.type(screen.getByLabelText('O quê'), 'Treinar a equipe da doca');
    await userEvent.selectOptions(screen.getByLabelText('Quem'), 'u1');
    await userEvent.type(screen.getByLabelText(/Causa que esta ação ataca/), 'Falta de treinamento');
    await userEvent.click(screen.getByRole('button', { name: 'Incluir ação' }));

    await waitFor(() => expect(criarAcao).toHaveBeenCalledWith('p1', expect.objectContaining({
      title: 'Treinar a equipe da doca',
      responsibleId: 'u1',
      rootCauseRef: 'Falta de treinamento',
    })));
  });

  it('suspender uma ação só confirma com motivo', async () => {
    montar();
    const tabela = await screen.findByTestId('acoes-do-plano');
    await userEvent.click(within(tabela).getByRole('button', { name: 'Suspender' }));

    const confirmar = screen.getByRole('button', { name: 'Confirmar' });
    expect(confirmar).toBeDisabled();

    await userEvent.type(screen.getByLabelText('Por quê'), 'Aguardando verba.');
    expect(confirmar).toBeEnabled();
    await userEvent.click(confirmar);
    await waitFor(() => expect(mudarStatusDaAcao).toHaveBeenCalledWith(
      'a1', 'SUSPENSA', 'Aguardando verba.'));
  });

  it('a severidade do risco vem do servidor, com o ponto à vista', async () => {
    montar();
    await screen.findByTestId('abas-do-plano');
    await userEvent.click(screen.getByRole('button', { name: 'Riscos' }));
    const tabela = await screen.findByTestId('riscos-do-plano');
    expect(within(tabela).getByText('CRITICO · 12')).toBeInTheDocument();
  });

  it('registra um risco com probabilidade e impacto', async () => {
    montar();
    await screen.findByTestId('abas-do-plano');
    await userEvent.click(screen.getByRole('button', { name: 'Riscos' }));
    await userEvent.type(screen.getByLabelText('Qual é o risco'), 'O fornecedor pode atrasar');
    await userEvent.selectOptions(screen.getByLabelText('Probabilidade'), 'ALTA');
    await userEvent.click(screen.getByRole('button', { name: 'Registrar risco' }));

    await waitFor(() => expect(salvarRisco).toHaveBeenCalledWith('p1', expect.objectContaining({
      description: 'O fornecedor pode atrasar', probability: 'ALTA',
    })));
  });

  it('encerrar avisa quantas ações ficam em aberto', async () => {
    montar();
    await screen.findByTestId('abas-do-plano');
    await userEvent.click(screen.getByRole('button', { name: 'Encerrar plano' }));
    expect(screen.getByText(/Sobram 1 ação\(ões\) em aberto/)).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/Evidência/), 'Procedimento afixado.');
    await userEvent.click(screen.getByRole('button', { name: 'Encerrar' }));
    await waitFor(() => expect(encerrarPlano).toHaveBeenCalledWith('p1', 'Procedimento afixado.'));
  });

  it('o plano encerrado mostra quem decidiu e não oferece incluir ação', async () => {
    vi.mocked(abrirPlano).mockResolvedValue(completo({
      plan: {
        ...completo().plan, life: 'ENCERRADO', closedByLabel: 'Ana',
        closedAt: '2026-09-22T12:00:00Z', evidenceNote: 'Procedimento afixado.',
        closedWithPending: true,
      },
    }));
    montar();

    const bloco = await screen.findByTestId('encerramento');
    expect(within(bloco).getByText(/Encerrado por Ana/)).toBeInTheDocument();
    expect(screen.getByText('Encerrado com ação em aberto')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Incluir ação' })).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Reabrir' }));
    await waitFor(() => expect(reabrirPlano).toHaveBeenCalledWith('p1'));
  });

  it('as lições guardam o que não funcionou', async () => {
    montar();
    await screen.findByTestId('abas-do-plano');
    await userEvent.click(screen.getByRole('button', { name: 'Lições' }));
    await userEvent.type(screen.getByLabelText('O que não funcionou'), 'O vídeo não pegou');
    await userEvent.click(screen.getByRole('button', { name: 'Salvar lições' }));

    await waitFor(() => expect(salvarLicoes).toHaveBeenCalledWith('p1', expect.objectContaining({
      whatFailed: 'O vídeo não pegou',
    })));
  });

  it('a causa raiz do plano existe mesmo sem ciclo de melhoria', async () => {
    montar();
    await screen.findByTestId('abas-do-plano');
    await userEvent.click(screen.getByRole('button', { name: 'Causa raiz' }));
    await userEvent.type(screen.getByLabelText('Causa principal'), 'Sem limite afixado');
    await userEvent.click(screen.getByRole('button', { name: 'Salvar' }));

    await waitFor(() => expect(salvarCausaRaiz).toHaveBeenCalledWith('p1', {
      method: 'CINCO_PORQUES', mainCause: 'Sem limite afixado',
    }));
  });
});
