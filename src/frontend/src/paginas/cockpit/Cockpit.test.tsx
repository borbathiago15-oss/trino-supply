import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CockpitDados } from '@/api/torre';

vi.mock('@/api/torre', async (importar) => ({
  ...(await importar<typeof import('@/api/torre')>()),
  obterCockpit: vi.fn(),
}));

import { obterCockpit } from '@/api/torre';
import { Cockpit } from './Cockpit';

const dados = (p: Partial<CockpitDados> = {}): CockpitDados => ({
  sincronizadoEm: '2026-09-22T12:00:00Z',
  unidade: null,
  unidades: [],
  kpis: {
    itensAtrasados: 4, taxaRiscoPct: 25, backlogTotalItens: 16, backlogTotalValor: 128000,
    slaSemanalPct: 92, metaSlaPct: 90, savingMesTotal: 30000, metaSavingMes: 50000,
    otifGeralPct: 88, otifMedidos: 9,
  },
  pipeline: [
    { etapa: 'SOLICITACAO', rotulo: 'Solicitação', quantidade: 5, horasNaFila: 12, gargalo: 'NORMAL' },
    { etapa: 'COTACAO', rotulo: 'Cotação', quantidade: 7, horasNaFila: 80, gargalo: 'CRITICO' },
  ],
  excecoesCriticas: [
    { id: 'a', tipoAlerta: 'OC_PENDENTE', codigoReferencia: 'PO-9', descricaoItem: 'Bota',
      unidadeCentroCusto: 'CC-01', tempoRestanteOuAtraso: '30h sem O.C.', responsavelNome: 'Carla', ordem: 2 },
    { id: 'b', tipoAlerta: 'ATRASO_CRITICO', codigoReferencia: 'PR-7', descricaoItem: 'Martelete',
      unidadeCentroCusto: 'CC-02', tempoRestanteOuAtraso: '3d de atraso', responsavelNome: 'Caio', ordem: 0 },
  ],
  burndownCompradores: [{ compradorNome: 'Carla', atendidosHoje: 3, totalHoje: 8, pendenciasCriticas: 2 }],
  agendaDocaHoje: [{ numeroNfe: '1234', fornecedorNome: 'Alfa', horarioPrevisto: '22/09', statusEntrega: 'NO_PRAZO' }],
  ...p,
});

describe('<Cockpit />', () => {
  beforeEach(() => { vi.clearAllMocks(); vi.mocked(obterCockpit).mockResolvedValue(dados()); });
  afterEach(() => vi.useRealTimers());

  it('mostra os cinco números de comando', async () => {
    render(<Cockpit />);
    await waitFor(() => expect(screen.getByTestId('cockpit')).toBeInTheDocument());
    expect(screen.getByText('25%')).toBeInTheDocument();       // risco
    expect(screen.getByText('16')).toBeInTheDocument();        // backlog em itens
    expect(screen.getByText('92%')).toBeInTheDocument();       // SLA
    expect(screen.getByText('R$ 30,0 mil')).toBeInTheDocument();
    expect(screen.getByText('88%')).toBeInTheDocument();       // OTIF
  });

  it('o gargalo da esteira aparece no nó, não num aviso solto', async () => {
    render(<Cockpit />);
    await waitFor(() => expect(screen.getByTestId('esteira')).toBeInTheDocument());
    const cotacao = screen.getByTestId('esteira').querySelector('[data-etapa="COTACAO"]');
    expect(cotacao).toHaveAttribute('data-gargalo', 'CRITICO');
  });

  it('o radar põe o mais grave no topo, mesmo vindo depois do servidor', async () => {
    render(<Cockpit />);
    await waitFor(() => expect(screen.getByTestId('radar')).toBeInTheDocument());
    const linhas = screen.getByTestId('radar').querySelectorAll('li');
    expect(linhas[0]).toHaveAttribute('data-alerta', 'ATRASO_CRITICO');
  });

  it('OTIF sem entrega medida mostra traço, e não 0%', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({
      kpis: { ...dados().kpis, otifGeralPct: null, otifMedidos: 0 },
    }));
    render(<Cockpit />);
    await waitFor(() => expect(screen.getByText('nenhuma entrega medida')).toBeInTheDocument());
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('sem exceções, a tela diz isso em vez de ficar vazia', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({ excecoesCriticas: [] }));
    render(<Cockpit />);
    await waitFor(() => expect(screen.getByText('Nenhuma exceção aberta.')).toBeInTheDocument());
  });

  it('a falha de uma atualização mantém os números na parede', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<Cockpit />);
    await waitFor(() => expect(screen.getByTestId('cockpit')).toBeInTheDocument());

    // a próxima leitura falha: a TV não pode apagar o painel por causa de um timeout
    vi.mocked(obterCockpit).mockRejectedValue(new Error('rede'));
    await vi.advanceTimersByTimeAsync(31_000);

    expect(screen.getByTestId('cockpit')).toBeInTheDocument();
    expect(screen.getByText('25%')).toBeInTheDocument();
    expect(screen.getByText('última leitura mantida')).toBeInTheDocument();
  });

  it('a parede diz qual recorte está na tela', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({ unidade: 'Unidade PB', unidades: ['Unidade PB', 'Unidade BA'] }));
    render(<Cockpit />);
    await waitFor(() => expect(screen.getByTestId('unidade-na-tela')).toHaveTextContent('Unidade PB'));
    // 3 = a visão geral mais as duas unidades; é o tamanho da volta que a TV dá
    expect(screen.getByTestId('unidade-na-tela')).toHaveTextContent('3 recortes');
  });

  it('com uma unidade só, a tela não gira e diz visão geral', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({ unidades: ['Única'] }));
    render(<Cockpit />);
    await waitFor(() => expect(screen.getByTestId('unidade-na-tela')).toHaveTextContent('Visão geral'));
    expect(screen.getByTestId('unidade-na-tela')).not.toHaveTextContent('recortes');
  });

  it('enquanto não há dados, diz que está conectando', () => {
    vi.mocked(obterCockpit).mockReturnValue(new Promise(() => {}));
    render(<Cockpit />);
    expect(screen.getByText('Conectando ao cockpit…')).toBeInTheDocument();
  });
});
