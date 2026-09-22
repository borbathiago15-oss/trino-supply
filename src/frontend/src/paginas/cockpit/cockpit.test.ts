import { describe, expect, it } from 'vitest';
import type { ExcecaoDoCockpit } from '@/api/torre';
import {
  cicloDeUnidades, compacto, horaDoRelogio, ordenarRadar, precisaRolar, progressoDaMeta,
  proximaUnidade, slaAtingido,
} from './cockpit';

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
