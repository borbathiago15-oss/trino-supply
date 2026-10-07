import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./cliente', () => ({ api: vi.fn() }));

import { api } from './cliente';
import { FILTRO_VAZIO, filtrosAtivos, painelDeAtendimentos } from './material';

const rota = () => vi.mocked(api).mock.calls.at(-1)?.[0] as string;

describe('o recorte do Painel de Atendimentos vai na própria rota', () => {
  beforeEach(() => vi.resetAllMocks());

  it('sem filtro nenhum a rota fica limpa, como era antes de o filtro existir', () => {
    painelDeAtendimentos();
    expect(rota()).toBe('/api/v1/material-requisitions/panel');
    painelDeAtendimentos(FILTRO_VAZIO);
    expect(rota()).toBe('/api/v1/material-requisitions/panel');
  });

  it('cada recorte entra como parâmetro, e o solicitante vai pelo id', () => {
    painelDeAtendimentos({ costCenter: 'BAH-001', requesterId: 'u-1', from: '2026-09-01', to: '2026-09-30' });
    expect(rota()).toBe('/api/v1/material-requisitions/panel'
      + '?costCenter=BAH-001&requesterId=u-1&from=2026-09-01&to=2026-09-30');
  });

  it('o filtro que o usuário tirou não viaja como vazio', () => {
    // `undefined` é como a tela desfaz um recorte; mandá-lo em branco faria o servidor
    // filtrar por centro nenhum e devolver lista vazia
    painelDeAtendimentos({ costCenter: undefined, requesterId: 'u-1' });
    expect(rota()).toBe('/api/v1/material-requisitions/panel?requesterId=u-1');
  });
});

describe('quantos recortes estão valendo', () => {
  it('conta só o que tem valor — é o número que a tela mostra ao lado do "limpar"', () => {
    expect(filtrosAtivos(FILTRO_VAZIO)).toBe(0);
    expect(filtrosAtivos({ costCenter: 'BAH-001', requesterId: undefined })).toBe(1);
    expect(filtrosAtivos({ from: '2026-09-01', to: '2026-09-30' })).toBe(2);
  });
});
