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
  vazao: { entraramHoje: 18, concluidosHoje: 15, saldo: 3, taxaConclusaoPct: 83.3 },
  compra: {
    valorComprado: 412500, pedidos: 18, emergenciais: 3,
    tetoAteHoje: 500000, pctDoTeto: 82.5, faixaDoTeto: 'ok',
  },
  almoxarifado: {
    filaSolicitacoes: 6, filaItens: 21, horasDoMaisAntigo: 52, gargalo: 'ATENCAO',
    maisAntigaNumero: 'MR-2026-000012', aguardandoAprovacao: 3, atendidasHoje: 4,
    atendidoPeloEstoquePct: 78.5, viraramCompraNoMes: 2, horasMediaAtendimento: 9.4,
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

    // a falha chega numa microtarefa depois do relógio: esperar por ela é o que torna o
    // teste determinístico — a máquina lenta do CI errava aqui, e a rápida daqui não
    await waitFor(() => expect(screen.getByText('última leitura mantida')).toBeInTheDocument());
    // e o que estava na parede continua lá, que é o ponto
    expect(screen.getByTestId('cockpit')).toBeInTheDocument();
    expect(screen.getByText('25%')).toBeInTheDocument();
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

  // quem entra na sala tem de saber de quem é o painel antes de ler qualquer número —
  // e o cockpit ficou sem a marca até alguém notar da porta
  it('a parede mostra a marca, e não só o nome escrito', async () => {
    render(<Cockpit />);
    const marca = await screen.findByAltText('Trino Supply');
    expect(marca.tagName).toBe('IMG');
    expect(marca.closest('h1')).not.toBeNull();
  });

  it('enquanto não há dados, diz que está conectando', () => {
    vi.mocked(obterCockpit).mockReturnValue(new Promise(() => {}));
    render(<Cockpit />);
    expect(screen.getByText('Conectando ao cockpit…')).toBeInTheDocument();
  });

  it('a vazão do dia mostra os dois extremos do cano e o lado para onde o dia andou', async () => {
    // o backlog do cartão diz quanto há parado; ele não diz se o time ganhou ou perdeu terreno
    render(<Cockpit />);
    await screen.findByTestId('vazao-do-dia');

    expect(screen.getByTestId('vazao-entraram')).toHaveTextContent('18');
    expect(screen.getByTestId('vazao-concluidos')).toHaveTextContent('15');
    expect(screen.getByTestId('vazao-saldo')).toHaveTextContent('+3');
    expect(screen.getByTestId('vazao-saldo')).toHaveTextContent('backlog aumentando');
    expect(screen.getByTestId('vazao-taxa')).toHaveTextContent('83,3%');
  });

  it('backlog encolhendo é verde, e o saldo negativo já carrega o sinal', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({
      vazao: { entraramHoje: 12, concluidosHoje: 17, saldo: -5, taxaConclusaoPct: 141.7 },
    }));
    render(<Cockpit />);
    await screen.findByTestId('vazao-do-dia');

    const saldo = screen.getByTestId('vazao-saldo');
    expect(saldo).toHaveTextContent('-5');
    expect(saldo).toHaveTextContent('backlog reduzindo');
  });

  it('dia sem entrada mostra traço na taxa, não 0%', async () => {
    // "nada entrou" e "não demos conta de nada" são notícias diferentes, e só uma cobra alguém
    vi.mocked(obterCockpit).mockResolvedValue(dados({
      vazao: { entraramHoje: 0, concluidosHoje: 0, saldo: 0, taxaConclusaoPct: null },
    }));
    render(<Cockpit />);
    await screen.findByTestId('vazao-do-dia');

    expect(screen.getByTestId('vazao-taxa')).toHaveTextContent('—');
    expect(screen.getByTestId('vazao-saldo')).toHaveTextContent('backlog estável');
  });

  it('a compra do mês mostra o valor, o que foi emergencial e quanto do teto já se usou', async () => {
    render(<Cockpit />);
    await screen.findByTestId('compra-do-mes');

    expect(screen.getByTestId('compra-valor')).toHaveTextContent('R$ 412,5 mil');
    expect(screen.getByTestId('compra-emergenciais')).toHaveTextContent('3 emergenciais de 18 pedidos');
    expect(screen.getByTestId('compra-teto')).toHaveTextContent('82,5%');
  });

  it('a cor do valor comprado vem da faixa do servidor, porque a meta é teto e não alvo', async () => {
    // 82,5% de um alvo seria "quase lá"; de um teto é "dentro". Quem sabe a diferença é a
    // régua do catálogo, no servidor — a parede só pinta o que ela disse
    vi.mocked(obterCockpit).mockResolvedValue(dados({
      compra: { ...dados().compra, pctDoTeto: 134, faixaDoTeto: 'fora' },
    }));
    render(<Cockpit />);
    await screen.findByTestId('compra-valor');

    expect(screen.getByTestId('compra-valor').querySelector('[data-testid="numero-vivo"]')?.className)
      .toContain('rose');
  });

  it('sem teto cadastrado não há comparação nem cor', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({
      compra: { ...dados().compra, tetoAteHoje: null, pctDoTeto: null, faixaDoTeto: null },
    }));
    render(<Cockpit />);
    await screen.findByTestId('compra-teto');

    expect(screen.getByTestId('compra-teto')).toHaveTextContent('sem teto definido');
    const numero = screen.getByTestId('compra-valor').querySelector('[data-testid="numero-vivo"]');
    expect(numero?.className).not.toContain('rose');
    expect(numero?.className).not.toContain('emerald');
  });

  it('o bloco do almoxarifado separa a fila do estoque da fila do centro de custo', async () => {
    // somar as duas cobraria do almoxarife trabalho que ainda está com o Nível 1
    render(<Cockpit />);
    await screen.findByTestId('almoxarifado');

    expect(screen.getByTestId('almox-fila')).toHaveTextContent('6');
    expect(screen.getByTestId('almox-fila')).toHaveTextContent('21 itens');
    expect(screen.getByTestId('almox-aprovacao')).toHaveTextContent('3');
    expect(screen.getByTestId('almox-atendidas')).toHaveTextContent('4');
    expect(screen.getByTestId('almox-estoque')).toHaveTextContent('78,5%');
    expect(screen.getByTestId('almox-estoque')).toHaveTextContent('2 viraram compra no mês');
  });

  it('uma solicitação sozinha não vira "1 viraram"', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({
      almoxarifado: { ...dados().almoxarifado, viraramCompraNoMes: 1 },
    }));
    render(<Cockpit />);
    await screen.findByTestId('almox-estoque');

    expect(screen.getByTestId('almox-estoque')).toHaveTextContent('1 virou compra no mês');
  });

  it('a fila do almoxarifado acende pela mesma régua de gargalo da esteira', async () => {
    render(<Cockpit />);
    await screen.findByTestId('almox-fila');

    // 52h com a solicitação mais antiga: atenção, e o número dela fica na tela
    expect(screen.getByTestId('almox-fila').className).toContain('amber');
    expect(screen.getByTestId('almox-fila')).toHaveTextContent('mais antiga 2d 4h');
    expect(screen.getByTestId('almox-fila')).toHaveTextContent('MR-2026-000012');
  });

  it('atendimento sem medição mostra traço, não zero hora', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({
      almoxarifado: {
        ...dados().almoxarifado, atendidasHoje: 0,
        atendidoPeloEstoquePct: null, horasMediaAtendimento: null, viraramCompraNoMes: 0,
      },
    }));
    render(<Cockpit />);
    await screen.findByTestId('almox-tempo');

    expect(screen.getByTestId('almox-tempo')).toHaveTextContent('—');
    expect(screen.getByTestId('almox-estoque')).toHaveTextContent('—');
    expect(screen.getByTestId('almox-estoque')).toHaveTextContent('nenhuma virou compra no mês');
  });

  it('operação que não pede material ao almoxarifado não perde altura com o bloco', async () => {
    vi.mocked(obterCockpit).mockResolvedValue(dados({
      almoxarifado: {
        filaSolicitacoes: 0, filaItens: 0, horasDoMaisAntigo: 0, gargalo: 'NORMAL',
        maisAntigaNumero: null, aguardandoAprovacao: 0, atendidasHoje: 0,
        atendidoPeloEstoquePct: null, viraramCompraNoMes: 0, horasMediaAtendimento: null,
      },
    }));
    render(<Cockpit />);
    await screen.findByTestId('esteira');

    expect(screen.queryByTestId('almoxarifado')).not.toBeInTheDocument();
  });
});
