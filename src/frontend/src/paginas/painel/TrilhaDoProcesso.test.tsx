import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { Aviso } from '@/api/painel';
import type { Perfil } from '@/dominio/papeis';
import { ETAPAS, montarTrilha, telasVisiveis, TrilhaDoProcesso } from './TrilhaDoProcesso';

const aviso = (kind: string, count: number, view: string): Aviso =>
  ({ kind, count, view, severity: 'media', text: `${count} coisa(s)` });

const admin: Perfil = { role: 'SystemAdministrator', modules: [] };
const solicitante: Perfil = { role: 'Requester', modules: ['SOLICITACOES', 'MATERIAL'] };

const montar = (avisos: Aviso[], usuario: Perfil = admin) => render(
  <MemoryRouter><TrilhaDoProcesso avisos={avisos} usuario={usuario} /></MemoryRouter>,
);

describe('montarTrilha', () => {
  it('soma os avisos de cada etapa e resolve a rota onde se age', () => {
    const trilha = montarTrilha([
      aviso('APROVACAO_GERENTE', 2, 'pr-approvals'),
      aviso('APROVACAO_DIRETOR', 1, 'pr-approvals'),
      aviso('OC_EMITIR', 5, 'quotations'),
    ], telasVisiveis(admin));

    const aprovar = trilha.find((e) => e.rotulo === 'Aprovar Níveis 1 e 2')!;
    expect(aprovar.pendente).toBe(3);
    expect(aprovar.rota).toBe('/aprovacoes');
    expect(trilha.find((e) => e.rotulo === 'Registrar a O.C.')!.pendente).toBe(5);
    // etapa sem aviso não some: a sequência é o que se quer enxergar
    expect(trilha).toHaveLength(ETAPAS.length);
    expect(trilha.find((e) => e.rotulo === 'Entregar do estoque')!.pendente).toBe(0);
  });

  it('etapa fora do alcance do usuário fica no mapa, mas sem link', () => {
    const trilha = montarTrilha([], telasVisiveis(solicitante));
    expect(trilha.find((e) => e.rotulo === 'Solicitar')!.alcancavel).toBe(true);
    expect(trilha.find((e) => e.rotulo === 'Cotar e escolher')!.alcancavel).toBe(false);
  });

  it('todas as etapas apontam para uma tela de verdade', () => {
    for (const e of montarTrilha([], telasVisiveis(admin)))
      expect(e.rota).not.toBe('/painel');
  });
});

describe('<TrilhaDoProcesso />', () => {
  it('mostra os nove passos em ordem, com o que está parado em cada um', () => {
    montar([aviso('APROVACAO', 4, 'pr-approvals')]);
    const trilha = within(screen.getByTestId('trilha-processo'));
    expect(screen.getByTestId('trilha-processo').children).toHaveLength(9);

    const aprovarSc = trilha.getByText('Aprovar a SC').closest('a')!;
    expect(aprovarSc).toHaveAttribute('href', '/aprovacoes');
    expect(trilha.getByTestId('parado-Aprovar a SC')).toHaveTextContent('4');
  });

  it('passo sem pendência não ganha contador', () => {
    montar([aviso('APROVACAO', 4, 'pr-approvals')]);
    const trilha = within(screen.getByTestId('trilha-processo'));
    expect(trilha.queryByTestId('parado-Cotar e escolher')).toBeNull();
    expect(trilha.getByTestId('parado-Aprovar a SC')).toBeInTheDocument();
  });

  it('quem não alcança a etapa não recebe link para ela', () => {
    montar([], solicitante);
    const trilha = within(screen.getByTestId('trilha-processo'));
    expect(trilha.getByText('Cotar e escolher').closest('a')).toBeNull();
    expect(trilha.getByText('Solicitar').closest('a')).toHaveAttribute('href', '/solicitacoes');
  });
});
