import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AcaoDoCiclo, Analise, CicloCompleto } from '@/api/melhoria';
import { EncerramentoPendente } from '@/api/melhoria';
import { ToastProvider } from '@/componentes/Toast';
import { CicloDeMelhoria, MINIMO_DO_MOTIVO, tomDoSinal } from './Ciclo';

vi.mock('@/api/melhoria', async (importar) => ({
  ...(await importar<typeof import('@/api/melhoria')>()),
  abrirCiclo: vi.fn(),
  salvarCiclo: vi.fn(),
  encerrarCiclo: vi.fn(),
  reabrirCiclo: vi.fn(),
}));
import { abrirCiclo, encerrarCiclo, reabrirCiclo, salvarCiclo } from '@/api/melhoria';

const acao = (p: Partial<AcaoDoCiclo>): AcaoDoCiclo => ({
  id: 'a1', number: 'AC-2026-000001', title: 'Afixar o limite de empilhamento',
  responsibleId: 'u1', responsibleLabel: 'Ana', dueDate: '2026-10-10',
  status: 'PENDENTE', rootCauseRef: null, progress: 0, late: false, open: true, ...p,
});

const pareto: Analise = {
  key: 'PARETO', name: 'Pareto', problem: null, effect: null, rootCause: null, warning: null,
  steps: [], groups: [], ideas: [],
  causes: [
    { label: 'Manuseio', value: 50, percent: 50, cumulative: 50, vital: true, detail: null },
    { label: 'Transporte', value: 10, percent: 10, cumulative: 100, vital: false, detail: null },
  ],
};

const completo = (p: Partial<CicloCompleto> = {}): CicloCompleto => ({
  cycle: {
    id: 'c1', code: 'PDCA-2026-001', title: 'Reduzir avarias na doca 2',
    scope: 'CENTRO', scopeLabel: 'Centro de custo', phase: 'DO', phaseLabel: 'Do — executar',
    region: null, sectorId: null, areas: null, priority: 'NORMAL',
    ownerId: 'u1', ownerLabel: 'Ana', indicator: 'Avarias/mês', baseline: 40, goalValue: 10,
    unit: 'un', goalDeadline: '2026-12-31', resultValue: null, goalMet: null,
    startDate: '2026-09-01', endDate: null, closedAt: null, closedByLabel: null,
    closedReason: null, createdByLabel: 'Ana', createdAt: '2026-09-01T00:00:00Z',
    ...(p.cycle ?? {}),
  },
  plan: {
    problem: 'Avarias sobem desde julho', currentSituation: '40 por mês',
    toolName: 'PARETO', toolData: '{}', causeAnalysis: null,
    rootCause: 'Empilhamento acima do limite', goalDescription: 'Cair para 10/mês',
  },
  check: { checkedOn: null, checkAnalysis: null },
  act: { standardization: null, lessons: null, newCycle: false },
  sectorName: 'Tecnologia',
  watchers: [], costCenters: ['BAH-001'],
  analysis: pareto,
  actions: [acao({})],
  reading: {
    indicador: {
      nome: 'Avarias/mês', baseline: 40, meta: 10, atual: null, unidade: 'un',
      sentido: 'reduzir', atingiuNoNumero: null, frase: 'Avarias/mês: de 40 un para 10 un; ainda sem medição.',
    },
    prazo: { prazo: '2026-12-31', diasRestantes: 100, percentualConsumido: 20, vencido: false, frase: 'Faltam 100 dia(s).' },
    acoes: { total: 1, abertas: 1, atrasadas: 0, concluidas: 0, suspensas: 0, frase: '1 ação(ões): 1 em aberto.' },
    causas: [{ rotulo: 'Manuseio', vital: true, acoes: 0, detalhe: null }],
    sinais: [{ chave: 'causa-vital-sem-acao', severidade: 'risco', texto: 'A causa vital "Manuseio" não tem nenhuma ação atacando-a.' }],
    proximoPasso: 'Crie a ação que ataca a causa vital.',
    frases: ['Avarias/mês: de 40 un para 10 un; ainda sem medição.'],
  },
  ...p,
});

const montar = () => render(
  <MemoryRouter initialEntries={['/melhoria/c1']}>
    <ToastProvider>
      <Routes><Route path="/melhoria/:id" element={<CicloDeMelhoria />} /></Routes>
    </ToastProvider>
  </MemoryRouter>);

describe('tomDoSinal', () => {
  it('risco é vermelho, atenção é âmbar', () => {
    expect(tomDoSinal({ chave: 'x', severidade: 'risco', texto: '' })).toContain('perigo');
    expect(tomDoSinal({ chave: 'x', severidade: 'atencao', texto: '' })).toContain('aviso');
  });
});

describe('<CicloDeMelhoria />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(abrirCiclo).mockResolvedValue(completo());
    vi.mocked(salvarCiclo).mockResolvedValue(completo().cycle);
    vi.mocked(reabrirCiclo).mockResolvedValue(completo().cycle);
  });

  it('mostra a leitura automática vinda do servidor, sem recalcular nada', async () => {
    montar();
    const leitura = await screen.findByTestId('leitura');
    expect(within(leitura).getByText('Crie a ação que ataca a causa vital.')).toBeInTheDocument();
    expect(within(leitura).getByText(/não tem nenhuma ação atacando-a/)).toBeInTheDocument();
  });

  it('desenha o Pareto com a faixa vital marcada', async () => {
    montar();
    const grade = await screen.findByTestId('pareto');
    expect(within(grade).getByText('Manuseio')).toBeInTheDocument();
    expect(within(grade).getAllByText('VITAL')).toHaveLength(1);
  });

  // a trilha não oferece "encerrado": era o clique nela que fechava o ciclo sem
  // registrar quem decidiu, e é daí que vinha o encerrado com ação atrasada
  it('a trilha de fases não oferece o encerramento', async () => {
    montar();
    const trilha = await screen.findByTestId('trilha-fases');
    expect(within(trilha).queryByRole('button', { name: /ENCERRADO/i })).toBeNull();
    expect(within(trilha).getAllByRole('button')).toHaveLength(4);
  });

  it('a trilha muda a fase pela edição comum', async () => {
    montar();
    const trilha = await screen.findByTestId('trilha-fases');
    await userEvent.click(within(trilha).getByRole('button', { name: 'CHECK' }));
    await waitFor(() => expect(salvarCiclo).toHaveBeenCalledWith('c1', { phase: 'CHECK' }));
  });

  it('encerrar só habilita com veredito e motivo', async () => {
    montar();
    await screen.findByTestId('leitura');
    await userEvent.click(screen.getByRole('button', { name: 'Encerrar ciclo' }));

    const botao = screen.getByRole('button', { name: 'Encerrar' });
    expect(botao).toBeDisabled();

    await userEvent.selectOptions(screen.getByLabelText(/A meta foi atingida/), 'nao');
    expect(botao).toBeDisabled();  // ainda falta o motivo

    await userEvent.type(screen.getByLabelText(/Por quê/), 'x'.repeat(MINIMO_DO_MOTIVO - 1));
    expect(botao).toBeDisabled();  // motivo curto é campo vazio com mais passos

    await userEvent.type(screen.getByLabelText(/Por quê/), 'x');
    expect(botao).toBeEnabled();
  });

  it('a recusa por pendência mostra o que sobrou e só então confirma', async () => {
    // pedir a confirmação sem dizer de quê seria pedir uma assinatura em branco
    vi.mocked(encerrarCiclo)
      .mockRejectedValueOnce(new EncerramentoPendente({
        code: 'PDCA-ERR-041', message: 'Sobraram 1 ação(ões) em aberto.',
        pending: [acao({ number: 'AC-2026-000009', title: 'Treinar a equipe da doca' })],
      }))
      .mockResolvedValueOnce(completo().cycle);

    montar();
    await screen.findByTestId('leitura');
    await userEvent.click(screen.getByRole('button', { name: 'Encerrar ciclo' }));
    await userEvent.selectOptions(screen.getByLabelText(/A meta foi atingida/), 'sim');
    await userEvent.type(screen.getByLabelText(/Por quê/), 'A meta foi batida e virou rotina da área.');
    await userEvent.click(screen.getByRole('button', { name: 'Encerrar' }));

    const pendencias = await screen.findByTestId('pendencias');
    expect(within(pendencias).getByText(/AC-2026-000009/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Encerrar assim mesmo' }));
    await waitFor(() => expect(encerrarCiclo).toHaveBeenLastCalledWith('c1', {
      goalMet: true,
      reason: 'A meta foi batida e virou rotina da área.',
      confirmPending: true,
    }));
  });

  it('o ciclo encerrado mostra o veredito e oferece reabrir', async () => {
    vi.mocked(abrirCiclo).mockResolvedValue(completo({
      cycle: {
        ...completo().cycle, phase: 'ENCERRADO', phaseLabel: 'Encerrado', goalMet: false,
        closedByLabel: 'Ana', closedReason: 'O ganho não veio e a equipe foi realocada.',
      },
    }));
    montar();

    const veredito = await screen.findByTestId('veredito');
    expect(within(veredito).getByText(/Encerrado por Ana/)).toBeInTheDocument();
    expect(screen.getByText('Meta não atingida')).toBeInTheDocument();
    expect(screen.queryByTestId('trilha-fases')).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: 'Reabrir' }));
    await waitFor(() => expect(reabrirCiclo).toHaveBeenCalledWith('c1'));
  });
});
