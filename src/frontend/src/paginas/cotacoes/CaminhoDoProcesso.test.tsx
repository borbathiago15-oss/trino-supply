import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { EtapaDoCaminho } from '@/api/cotacoes';
import { CaminhoDoProcesso, legenda } from './CaminhoDoProcesso';

const etapa = (p: Partial<EtapaDoCaminho>): EtapaDoCaminho => ({
  chave: 'x', titulo: 'Etapa', situacao: 'pendente', quem: null, em: null, ...p,
});

const caminho: EtapaDoCaminho[] = [
  etapa({ chave: 'solicitacao', titulo: 'Solicitação de compra', situacao: 'feita', quem: 'Ana Solicitante', em: '2026-09-01T12:00:00Z' }),
  etapa({ chave: 'escolha', titulo: 'Escolha do fornecedor vencedor', situacao: 'feita', quem: 'Carla Compradora', em: '2026-09-03T12:00:00Z' }),
  etapa({ chave: 'nivel1', titulo: 'Aprovação de Nível 1', situacao: 'atual', quem: 'Bruno Gerente', em: '2026-09-03T12:00:00Z' }),
  etapa({ chave: 'nivel2', titulo: 'Aprovação de Nível 2', situacao: 'pendente', quem: 'Dora Diretora' }),
  etapa({ chave: 'oc', titulo: 'Registro da O.C. do ERP', situacao: 'pendente' }),
];

describe('caminho do processo', () => {
  it('diz quem pediu, de quem está esperando agora e quem aprova depois', () => {
    render(<CaminhoDoProcesso etapas={caminho} />);

    const bloco = screen.getByTestId('caminho-do-processo');
    expect(bloco).toHaveTextContent('Ana Solicitante');
    // a etapa atual é a única em destaque, e diz o nome e o "desde"
    const atual = bloco.querySelector('[data-situacao="atual"]')!;
    expect(atual).toHaveTextContent('Aprovação de Nível 1');
    expect(atual).toHaveTextContent(/aguardando Bruno Gerente desde/);
    expect(bloco.querySelectorAll('[data-situacao="atual"]')).toHaveLength(1);
    // a pendente já diz quem vai aprovar
    expect(bloco.querySelector('[data-etapa="nivel2"]')).toHaveTextContent('a seguir · Dora Diretora');
  });

  it('centro sem aprovador aparece como defeito, não como espera normal', () => {
    render(<CaminhoDoProcesso etapas={[
      etapa({ chave: 'nivel1', titulo: 'Aprovação de Nível 1', situacao: 'atual', quem: 'sem aprovador cadastrado no centro' }),
    ]} />);
    expect(screen.getByTestId('caminho-do-processo')).toHaveTextContent('aguardando sem aprovador cadastrado no centro');
  });

  it('sem etapas não desenha nada', () => {
    render(<CaminhoDoProcesso etapas={[]} />);
    expect(screen.queryByTestId('caminho-do-processo')).toBeNull();
  });
});

describe('legenda de cada etapa', () => {
  it('feita: quem e quando; sem data, só quem', () => {
    expect(legenda(etapa({ situacao: 'feita', quem: 'Bruno', em: '2026-09-03T12:00:00Z' }))).toMatch(/^Bruno · /);
    expect(legenda(etapa({ situacao: 'feita', quem: 'Bruno' }))).toBe('Bruno');
  });

  it('atual: aguardando quem, e o desde só quando há marca honesta', () => {
    expect(legenda(etapa({ situacao: 'atual', quem: 'Bruno' }))).toBe('aguardando Bruno');
    expect(legenda(etapa({ situacao: 'atual', quem: 'Bruno', em: '2026-09-03T12:00:00Z' }))).toMatch(/^aguardando Bruno desde /);
  });

  it('pendente: o nome quando o sistema o conhece; encerrada: nada a dizer', () => {
    expect(legenda(etapa({ situacao: 'pendente', quem: 'Dora' }))).toBe('a seguir · Dora');
    expect(legenda(etapa({ situacao: 'pendente' }))).toBe('a seguir');
    expect(legenda(etapa({ situacao: 'encerrada', titulo: 'Rejeitado' }))).toBe('');
  });
});
