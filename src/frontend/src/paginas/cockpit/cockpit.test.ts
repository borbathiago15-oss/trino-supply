import { describe, expect, it } from 'vitest';
import type { AlmoxarifadoDoCockpit, ExcecaoDoCockpit } from '@/api/torre';
import {
  cicloDeParadas, cicloDeUnidades, CLASSE_DA_VAZAO, compacto, fraseDaEmergencia, fraseDaRotaDeCompra,
  fraseDasMedidas, horaDoRelogio, horasNaParede, ordenarRadar, ordenarRadarDoAlmoxarifado, paradaInicial,
  precisaRolar, progressoDaMeta, proximaParada, proximaUnidade, ROTACAO_MS,
  saldoComSinal, sentidoDaVazao, slaAtingido, temAlmoxarifado, tomDoTeto,
} from './cockpit';

const almoxarifado = (p: Partial<AlmoxarifadoDoCockpit> = {}): AlmoxarifadoDoCockpit => ({
  filaSolicitacoes: 0, filaItens: 0, horasDoMaisAntigo: 0, gargalo: 'NORMAL',
  maisAntigaNumero: null, aguardandoAprovacao: 0, atendidasHoje: 0,
  atendidoPeloEstoquePct: null, viraramCompraNoMes: 0, horasMediaAtendimento: null, ...p,
});

const alerta = (p: Partial<ExcecaoDoCockpit>): ExcecaoDoCockpit => ({
  id: 'x', tipoAlerta: 'OC_PENDENTE', codigoReferencia: 'PO-1', descricaoItem: 'Item',
  unidadeCentroCusto: 'CC-01', tempoRestanteOuAtraso: '2h', responsavelNome: 'Carla', ordem: 2, ...p,
});

describe('ordem do radar', () => {
  it('o mais grave assume o topo, mesmo chegando por último', () => {
    // critério de aceite 3: item urgente que entra sobe sozinho
    const radar = ordenarRadar([
      alerta({ id: 'a', tipoAlerta: 'OC_PENDENTE', ordem: 2 }),
      alerta({ id: 'b', tipoAlerta: 'COTACAO_VENCENDO', ordem: 1 }),
      alerta({ id: 'c', tipoAlerta: 'ATRASO_CRITICO', ordem: 0 }),
    ]);
    expect(radar.map((x) => x.id)).toEqual(['c', 'b', 'a']);
  });

  it('empate de gravidade desempata estável, sem a lista dançar a cada 30s', () => {
    const radar = ordenarRadar([
      alerta({ id: '1', codigoReferencia: 'PR-2', ordem: 0 }),
      alerta({ id: '2', codigoReferencia: 'PR-1', ordem: 0 }),
    ]);
    expect(radar.map((x) => x.codigoReferencia)).toEqual(['PR-1', 'PR-2']);
  });

  it('não altera a lista recebida', () => {
    const original = [alerta({ ordem: 2 }), alerta({ ordem: 0 })];
    ordenarRadar(original);
    expect(original[0].ordem).toBe(2);
  });
});

describe('metas', () => {
  it('o progresso vai de 0 a 1 e não passa disso', () => {
    expect(progressoDaMeta(25, 100)).toBe(0.25);
    expect(progressoDaMeta(150, 100)).toBe(1);
    expect(progressoDaMeta(-10, 100)).toBe(0);
  });

  it('sem meta não há barra: barra cheia sem meta seria comemoração de nada', () => {
    expect(progressoDaMeta(500, 0)).toBeNull();
  });

  it('o SLA bate a meta no empate', () => {
    expect(slaAtingido(90, 90)).toBe(true);
    expect(slaAtingido(89.9, 90)).toBe(false);
  });
});

describe('leitura a distância', () => {
  it('lista curta não rola; acima de cinco, rola', () => {
    expect(precisaRolar(5)).toBe(false);
    expect(precisaRolar(6)).toBe(true);
  });

  it('número grande vira compacto', () => {
    expect(compacto(840)).toBe('840');
    expect(compacto(12_400)).toBe('12,4 mil');
    expect(compacto(1_240_000)).toBe('1,2 mi');
  });

  it('o relógio tem dois dígitos em tudo', () => {
    expect(horaDoRelogio(new Date(2026, 8, 22, 7, 5, 3))).toBe('07:05:03');
  });
});

describe('rotação de unidades', () => {
  it('o ciclo abre pela visão geral e passa por cada unidade', () => {
    expect(cicloDeUnidades(['PB', 'BA'])).toEqual([null, 'PB', 'BA']);
  });

  it('com uma unidade só não há o que girar', () => {
    // alternar "geral" com a própria unidade mostraria o mesmo número duas vezes
    expect(cicloDeUnidades(['PB'])).toEqual([null]);
    expect(cicloDeUnidades([])).toEqual([null]);
  });

  it('a rotação dá a volta e recomeça na geral', () => {
    const ciclo = cicloDeUnidades(['PB', 'BA']);
    expect(proximaUnidade(ciclo, null)).toBe('PB');
    expect(proximaUnidade(ciclo, 'PB')).toBe('BA');
    expect(proximaUnidade(ciclo, 'BA')).toBeNull();
  });

  it('unidade que saiu do cadastro volta para a geral, em vez de travar a volta', () => {
    expect(proximaUnidade(cicloDeUnidades(['PB']), 'SUMIU')).toBeNull();
  });
});

describe('o rodízio das duas telas', () => {
  it('a tela do material entra como mais um passo do mesmo ciclo, depois da compra de cada recorte', () => {
    expect(cicloDeParadas(['PB', 'BA'], true)).toEqual([
      { tela: 'compras', unidade: null }, { tela: 'material', unidade: null },
      { tela: 'compras', unidade: 'PB' }, { tela: 'material', unidade: 'PB' },
      { tela: 'compras', unidade: 'BA' }, { tela: 'material', unidade: 'BA' },
    ]);
  });

  it('sem material para contar, o ciclo é o de sempre', () => {
    expect(cicloDeParadas(['PB', 'BA'], false)).toEqual([
      { tela: 'compras', unidade: null }, { tela: 'compras', unidade: 'PB' }, { tela: 'compras', unidade: 'BA' },
    ]);
  });

  it('com uma unidade só e material, a parede ainda alterna as duas telas da geral', () => {
    expect(cicloDeParadas(['Única'], true)).toEqual([
      { tela: 'compras', unidade: null }, { tela: 'material', unidade: null },
    ]);
    expect(cicloDeParadas(['Única'], false)).toEqual([{ tela: 'compras', unidade: null }]);
  });

  it('a volta passa pela compra e pelo material de cada recorte e recomeça na compra geral', () => {
    const ciclo = cicloDeParadas(['PB', 'BA'], true);
    expect(proximaParada(ciclo, { tela: 'compras', unidade: null })).toEqual({ tela: 'material', unidade: null });
    expect(proximaParada(ciclo, { tela: 'material', unidade: null })).toEqual({ tela: 'compras', unidade: 'PB' });
    expect(proximaParada(ciclo, { tela: 'compras', unidade: 'PB' })).toEqual({ tela: 'material', unidade: 'PB' });
    expect(proximaParada(ciclo, { tela: 'material', unidade: 'BA' })).toEqual({ tela: 'compras', unidade: null });
  });

  it('parada que saiu do ciclo volta ao início, em vez de travar a volta', () => {
    expect(proximaParada(cicloDeParadas(['PB', 'BA'], true), { tela: 'material', unidade: 'SUMIU' }))
      .toEqual({ tela: 'compras', unidade: null });
    expect(proximaParada([], { tela: 'material', unidade: null })).toEqual({ tela: 'compras', unidade: null });
  });

  it('cada parada fica um minuto: a volta com o material não pode dobrar de tamanho', () => {
    expect(ROTACAO_MS).toBe(60_000);
  });

  it('a parede começa pela compra geral, e ?tela=material abre direto na segunda tela', () => {
    expect(paradaInicial('')).toEqual({ tela: 'compras', unidade: null });
    expect(paradaInicial('?tela=material')).toEqual({ tela: 'material', unidade: null });
    expect(paradaInicial('?tela=outra')).toEqual({ tela: 'compras', unidade: null });
  });
});

describe('a tela do material', () => {
  it('o radar do almoxarifado põe o prazo estourado no topo, estável no desempate', () => {
    const linha = (numero: string, tipo: 'PRAZO_ESTOURADO' | 'ROTA_DE_COMPRA' | 'AGUARDANDO_CENTRO', ordem: number) => ({
      id: numero, tipo, numero, descricao: '', centroCusto: '', solicitante: '', tempo: '', ordem,
    });
    const ordenado = ordenarRadarDoAlmoxarifado([
      linha('MR-9', 'ROTA_DE_COMPRA', 3), linha('MR-2', 'PRAZO_ESTOURADO', 0),
      linha('MR-5', 'AGUARDANDO_CENTRO', 2), linha('MR-1', 'PRAZO_ESTOURADO', 0),
    ]);
    expect(ordenado.map((l) => l.numero)).toEqual(['MR-1', 'MR-2', 'MR-5', 'MR-9']);
  });

  it('a frase das medidas não deixa o traço sem explicação', () => {
    expect(fraseDasMedidas(0)).toBe('nenhum atendimento medido no mês');
    expect(fraseDasMedidas(1)).toBe('1 atendimento medido no mês');
    expect(fraseDasMedidas(7)).toBe('7 atendimentos medidos no mês');
  });
});

describe('o veredito da vazão do dia', () => {
  it('o sentido vem do sinal, e o empate é estável — não é boa notícia', () => {
    // pintar o empate de verde faria a parede comemorar um dia em que o time apenas
    // não perdeu terreno
    expect(sentidoDaVazao(3)).toBe('CRESCENDO');
    expect(sentidoDaVazao(-5)).toBe('REDUZINDO');
    expect(sentidoDaVazao(0)).toBe('ESTAVEL');
    expect(CLASSE_DA_VAZAO.ESTAVEL).not.toContain('emerald');
    expect(CLASSE_DA_VAZAO.CRESCENDO).toContain('rose');
    expect(CLASSE_DA_VAZAO.REDUZINDO).toContain('emerald');
  });

  it('o saldo leva o sinal, porque "+3" conta o que "3" não conta', () => {
    expect(saldoComSinal(3)).toBe('+3');
    expect(saldoComSinal(-5)).toBe('-5');
    expect(saldoComSinal(0)).toBe('0');
  });
});

describe('a compra do mês', () => {
  it('a cor sai da faixa do servidor, e sem meta não há cor nenhuma', () => {
    // a meta de valor comprado é teto ("menor é melhor"): 130% é ruim, o oposto do saving.
    // Quem sabe o sentido é a régua do catálogo, no servidor
    expect(tomDoTeto('ok')).toContain('emerald');
    expect(tomDoTeto('atencao')).toContain('amber');
    expect(tomDoTeto('fora')).toContain('rose');
    // pintar de vermelho um valor sem teto seria cobrar de uma meta que ninguém definiu
    expect(tomDoTeto(null)).toBe('');
  });

  it('zero emergencial é dito, e não deixado em branco', () => {
    // espaço vazio numa parede só significa que ninguém olhou
    expect(fraseDaEmergencia(0, 18)).toBe('nenhuma emergencial em 18 pedidos');
    expect(fraseDaEmergencia(3, 18)).toBe('3 emergenciais de 18 pedidos');
    expect(fraseDaEmergencia(1, 1)).toBe('1 emergencial de 1 pedido');
    expect(fraseDaEmergencia(0, 0)).toBe('nenhum pedido no mês');
  });
});

describe('o bloco do almoxarifado', () => {
  it('o tempo vira dias depois de dois dias, para não se dividir de cabeça', () => {
    expect(horasNaParede(9.4)).toBe('9,4h');
    expect(horasNaParede(47)).toBe('47h');
    expect(horasNaParede(52)).toBe('2d 4h');
    expect(horasNaParede(72)).toBe('3d');
  });

  it('tempo não medido é traço, e não zero hora', () => {
    // "ninguém mediu" e "levou zero hora" são notícias diferentes, e só uma é elogio
    expect(horasNaParede(null)).toBe('—');
    expect(horasNaParede(0)).toBe('0h');
  });

  it('o plural é da frase, não do número', () => {
    // "1 viraram compra" numa parede lida de longe parece defeito da tela
    expect(fraseDaRotaDeCompra(0)).toBe('nenhuma virou compra no mês');
    expect(fraseDaRotaDeCompra(1)).toBe('1 virou compra no mês');
    expect(fraseDaRotaDeCompra(4)).toBe('4 viraram compra no mês');
  });

  it('bloco sem nada a dizer não entra na parede', () => {
    // cinco zeros tirariam altura da esteira só para anunciar que o módulo não é usado
    expect(temAlmoxarifado(almoxarifado())).toBe(false);
  });

  it('basta uma das cinco perguntas ter resposta para o bloco existir', () => {
    expect(temAlmoxarifado(almoxarifado({ filaSolicitacoes: 1 }))).toBe(true);
    expect(temAlmoxarifado(almoxarifado({ aguardandoAprovacao: 1 }))).toBe(true);
    expect(temAlmoxarifado(almoxarifado({ atendidasHoje: 1 }))).toBe(true);
    expect(temAlmoxarifado(almoxarifado({ viraramCompraNoMes: 1 }))).toBe(true);
    // o estoque que não tinha nada mediu 0%, e medir é ter o que dizer
    expect(temAlmoxarifado(almoxarifado({ atendidoPeloEstoquePct: 0 }))).toBe(true);
  });
});
