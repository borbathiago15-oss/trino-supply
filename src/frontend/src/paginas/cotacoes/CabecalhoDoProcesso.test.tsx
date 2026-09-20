import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { EtapaDoCaminho, Processo } from '@/api/cotacoes';
import { processo } from '@/test/cotacoes';
import { CabecalhoDoProcesso } from './CabecalhoDoProcesso';

const etapa = (p: Partial<EtapaDoCaminho>): EtapaDoCaminho => ({
  chave: 'x', titulo: 'Etapa', situacao: 'pendente', quem: null, em: null, ...p,
});

const montar = (q: Processo) => render(
  <MemoryRouter><CabecalhoDoProcesso processo={q} /></MemoryRouter>,
);

describe('cabeçalho do processo', () => {
  it('ao lado da situação diz de quem se espera, sem rolar até o caminho', () => {
    montar(processo({
      status: 'AGUARDANDO_GERENTE',
      caminho: [etapa({ chave: 'nivel1', situacao: 'atual', quem: 'Bruno Gerente' })],
    }));
    expect(screen.getByTestId('de-quem')).toHaveTextContent('aguardando Bruno Gerente');
  });

  it('no impasse não aponta para quem não pode agir', () => {
    montar(processo({
      status: 'AGUARDANDO_GERENTE',
      caminho: [etapa({ chave: 'nivel1', situacao: 'atual', quem: 'Administrador', impasse: true })],
    }));
    expect(screen.getByTestId('de-quem')).toHaveTextContent('ninguém da lista pode aprovar');
    expect(screen.getByTestId('de-quem')).not.toHaveTextContent('aguardando');
  });

  it('sem etapa atual com nome, não inventa ninguém', () => {
    montar(processo({ status: 'OC_REGISTRADA', caminho: [etapa({ chave: 'entrega', situacao: 'atual' })] }));
    expect(screen.queryByTestId('de-quem')).toBeNull();
  });
});
