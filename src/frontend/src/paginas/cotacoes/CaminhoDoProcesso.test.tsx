import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { EtapaDoCaminho } from '@/api/cotacoes';
import { CaminhoDoProcesso, legenda } from './CaminhoDoProcesso';

const etapa = (p: Partial<EtapaDoCaminho>): EtapaDoCaminho => ({
  chave: 'x', titulo: 'Etapa', situacao: 'pendente', quem: null, em: null, ...p,
});

const montar = (etapas: EtapaDoCaminho[], centro?: string) => render(
  <MemoryRouter><CaminhoDoProcesso etapas={etapas} centro={centro} /></MemoryRouter>,
);

const caminho: EtapaDoCaminho[] = [
  etapa({ chave: 'solicitacao', titulo: 'Solicitação de compra', situacao: 'feita', quem: 'Ana Solicitante', em: '2026-09-01T12:00:00Z' }),
  etapa({ chave: 'escolha', titulo: 'Escolha do fornecedor vencedor', situacao: 'feita', quem: 'Carla Compradora', em: '2026-09-03T12:00:00Z' }),
  etapa({ chave: 'nivel1', titulo: 'Aprovação de Nível 1', situacao: 'atual', quem: 'Bruno Gerente', em: '2026-09-03T12:00:00Z' }),
  etapa({ chave: 'nivel2', titulo: 'Aprovação de Nível 2', situacao: 'pendente', quem: 'Dora Diretora' }),
  etapa({ chave: 'oc', titulo: 'Registro da O.C. do ERP', situacao: 'pendente' }),
];

describe('Nível 2 dispensado (compra do Gestor de Suprimentos)', () => {
  const dispensado = etapa({
    chave: 'nivel2', titulo: 'Aprovação de Nível 2', situacao: 'dispensada',
    quem: 'dispensado — compra do próprio Gestor de Suprimentos', em: '2026-09-04T12:00:00Z',
  });

  it('a etapa diz que não se aplica, em vez de ficar aguardando alguém', () => {
    expect(legenda(dispensado)).toBe('dispensado — compra do próprio Gestor de Suprimentos');
    expect(legenda(dispensado)).not.toContain('aguardando');
  });

  it('a etapa continua na lista, com marca própria', () => {
    montar([...caminho.slice(0, 3), dispensado, caminho[4]]);
    const linha = screen.getByText('Aprovação de Nível 2').closest('li');
    expect(linha).toHaveAttribute('data-situacao', 'dispensada');
  });

  it('dispensada não é o mesmo que pendente sem ninguém', () => {
    expect(legenda(etapa({ situacao: 'pendente', quem: null }))).toBe('a seguir');
  });
});

describe('caminho do processo', () => {
  it('diz quem pediu, de quem está esperando agora e quem aprova depois', () => {
    montar(caminho);

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

  it('centro sem aprovador aparece como defeito, e a tela aponta onde consertar', () => {
    montar([
      etapa({ chave: 'nivel1', titulo: 'Aprovação de Nível 1', situacao: 'atual', quem: 'sem aprovador cadastrado no centro', semAprovador: true }),
      etapa({ chave: 'nivel2', titulo: 'Aprovação de Nível 2', situacao: 'pendente', quem: 'sem aprovador cadastrado no centro', semAprovador: true }),
    ], 'BAH-002');
    const bloco = screen.getByTestId('caminho-do-processo');
    expect(bloco).toHaveTextContent('aguardando sem aprovador cadastrado no centro');
    // um link só, mesmo com os dois níveis vazios — o conserto é o mesmo cadastro
    const links = screen.getAllByRole('link', { name: /Cadastrar aprovadores do BAH-002/ });
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAttribute('href', '/centros-custo');
  });

  it('com aprovador cadastrado não há link de conserto', () => {
    montar(caminho);
    expect(screen.queryByRole('link', { name: /Cadastrar aprovadores/ })).toBeNull();
    expect(screen.queryByTestId('impasse-da-alcada')).toBeNull();
  });

  it('impasse no Nível 2: diz que o aprovador está impedido, por quê, e quem destrava', () => {
    // o caso real: o administrador é o único do Nível 2 e escolheu o fornecedor (ou deu o Nível 1)
    montar([
      etapa({ chave: 'nivel1', titulo: 'Aprovação de Nível 1', situacao: 'feita', quem: 'Gerson' }),
      etapa({ chave: 'nivel2', titulo: 'Aprovação de Nível 2', situacao: 'atual', quem: 'Gerson', impasse: true }),
    ], 'BAH-002');

    const bloco = screen.getByTestId('caminho-do-processo');
    // a linha não diz "aguardando Gerson" — ele não pode agir
    expect(bloco.querySelector('[data-etapa="nivel2"]')).toHaveTextContent('impasse · Gerson não pode aprovar');
    expect(bloco).not.toHaveTextContent('aguardando Gerson');

    const aviso = screen.getByTestId('impasse-da-alcada');
    expect(aviso).toHaveTextContent('Ninguém da lista pode dar o Nível 2');
    expect(aviso).toHaveTextContent('Gerson escolheu o fornecedor ou deu o Nível 1');
    expect(aviso).toHaveTextContent('RFQ-ERR-030');
    expect(aviso).toHaveTextContent('outro administrador pode aprovar');
    const link = screen.getByRole('link', { name: /cadastre outra pessoa no Nível 2 do BAH-002/ });
    expect(link).toHaveAttribute('href', '/centros-custo');
    // não é caso de "sem aprovador": há gente cadastrada, o problema é outro
    expect(screen.queryByRole('link', { name: /Cadastrar aprovadores/ })).toBeNull();
  });

  it('sem etapas não desenha nada', () => {
    montar([]);
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

  it('atual em impasse: diz que quem está na lista não pode aprovar', () => {
    expect(legenda(etapa({ situacao: 'atual', quem: 'Bruno', impasse: true }))).toBe('impasse · Bruno não pode aprovar');
    expect(legenda(etapa({ situacao: 'atual', quem: 'Bruno', impasse: true, em: '2026-09-03T12:00:00Z' })))
      .toMatch(/^impasse · Bruno não pode aprovar desde /);
  });

  it('pendente: o nome quando o sistema o conhece; encerrada: nada a dizer', () => {
    expect(legenda(etapa({ situacao: 'pendente', quem: 'Dora' }))).toBe('a seguir · Dora');
    expect(legenda(etapa({ situacao: 'pendente' }))).toBe('a seguir');
    expect(legenda(etapa({ situacao: 'encerrada', titulo: 'Rejeitado' }))).toBe('');
  });
});
