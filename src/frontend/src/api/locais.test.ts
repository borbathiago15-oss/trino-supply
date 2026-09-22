import { describe, expect, it } from 'vitest';
import { locaisPorTipo, rotuloDoLocal, type LocalEntrega } from './locais';

const alm: LocalEntrega = { id: 'l1', code: 'ALM-01', name: 'Almoxarifado Sede', kind: 'ALMOXARIFADO' };
const cc: LocalEntrega = { id: 'c1', code: 'PBA-002', name: 'Whirlpool PB', kind: 'CENTRO_DE_CUSTO' };

describe('locaisPorTipo', () => {
  it('separa o almoxarifado do centro de custo, nessa ordem', () => {
    const grupos = locaisPorTipo([cc, alm]);
    expect(grupos.map((g) => g.rotulo)).toEqual(['Almoxarifado', 'Centro de custo']);
    expect(grupos[1].locais.map((l) => l.code)).toEqual(['PBA-002']);
  });

  it('grupo sem nenhum local não entra: título sem opção não leva a lugar nenhum', () => {
    expect(locaisPorTipo([alm]).map((g) => g.kind)).toEqual(['ALMOXARIFADO']);
    expect(locaisPorTipo([])).toEqual([]);
  });

  it('o rótulo gravado na SC continua sendo "CÓDIGO — Nome" nos dois tipos', () => {
    expect(rotuloDoLocal(alm)).toBe('ALM-01 — Almoxarifado Sede');
    expect(rotuloDoLocal(cc)).toBe('PBA-002 — Whirlpool PB');
  });
});
