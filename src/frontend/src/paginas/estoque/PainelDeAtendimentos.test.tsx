import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { LinhaPainel, PainelAtendimentos } from '@/api/material';
import { ToastProvider } from '@/componentes/Toast';
import { PainelDeAtendimentos, situacaoDaLinha } from './PainelDeAtendimentos';

vi.mock('@/api/material', async (importar) => ({
  ...(await importar<typeof import('@/api/material')>()),
  painelDeAtendimentos: vi.fn(),
}));
vi.mock('@/api/centrosCusto', () => ({ listarCentrosCusto: vi.fn() }));

import { painelDeAtendimentos } from '@/api/material';
import { listarCentrosCusto } from '@/api/centrosCusto';

const linha = (p: Partial<LinhaPainel>): LinhaPainel => ({
  id: 'mr1', number: 'MR-2026-000001', costCenter: 'BAH-001', requesterLabel: 'Ana',
  createdAt: '2026-09-01T12:00:00Z', approvedAt: null, fulfilledAt: null, fulfilledByLabel: null,
  purchaseRequisitionNumber: null, items: 2, pending: 0, summary: '10× Luva · 2× Bota',
  ...p,
});

const painel = (p: Partial<PainelAtendimentos>): PainelAtendimentos => ({
  totals: { aguardandoAprovacao: 1, emAndamento: 2, concluidos: 3, parciais: 1 },
  opcoes: {
    centros: ['BAH-001', 'PER-002'],
    solicitantes: [
      { id: '11111111-1111-1111-1111-111111111111', label: 'Ana Silva' },
      { id: '22222222-2222-2222-2222-222222222222', label: 'Bruno Lima' },
    ],
  },
  aguardandoAprovacao: [linha({ number: 'MR-AGUARDANDO' })],
  emAndamento: [linha({ id: 'mr2', number: 'MR-ANDAMENTO', approvedAt: '2026-09-02T12:00:00Z' })],
  concluidos: [linha({ id: 'mr3', number: 'MR-CONCLUIDO', fulfilledByLabel: 'Zé' })],
  parciais: [linha({ id: 'mr4', number: 'MR-PARCIAL', pending: 6, purchaseRequisitionNumber: 'SC-2026-000030' })],
  porCentro: [{ costCenter: 'BAH-001', total: 4, emAndamento: 2, concluidos: 1, parciais: 1 }],
  porSolicitante: [{
    requesterId: '11111111-1111-1111-1111-111111111111', requesterLabel: 'Ana',
    total: 4, emAndamento: 2, concluidos: 1, parciais: 1,
  }],
  ...p,
});

const abrir = () => render(
  <MemoryRouter><ToastProvider><PainelDeAtendimentos /></ToastProvider></MemoryRouter>,
);

describe('a coluna que muda de bloco para bloco', () => {
  it('no parcial mostra o que falta e a SC que cobre', () => {
    expect(situacaoDaLinha(linha({ pending: 6, purchaseRequisitionNumber: 'SC-30' }))).toBe('falta 6 · SC SC-30');
  });
  it('no concluído mostra quem atendeu; no andamento, desde quando espera', () => {
    expect(situacaoDaLinha(linha({ fulfilledByLabel: 'Zé' }))).toBe('Zé');
    expect(situacaoDaLinha(linha({ approvedAt: '2026-09-02T12:00:00Z' }))).toBe('aprovada em 02/09/2026');
    expect(situacaoDaLinha(linha({}))).toBe('aguardando o Nível 1 do centro');
  });
});

describe('tela Painel de Atendimentos', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // o cadastro de centros é consultado pelo rótulo da linha; sem resposta a tela cai no código
    vi.mocked(listarCentrosCusto).mockResolvedValue([]);
  });

  it('abre com os quatro blocos visíveis', async () => {
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({}));
    abrir();
    expect(await screen.findByTestId('painel-aguardando')).toBeInTheDocument();
    expect(screen.getByTestId('painel-andamento')).toBeInTheDocument();
    expect(screen.getByTestId('painel-parcial')).toBeInTheDocument();
    expect(screen.getByTestId('painel-concluido')).toBeInTheDocument();
  });

  it('clicar num cartão isola o bloco, e clicar de novo devolve todos', async () => {
    const usuario = userEvent.setup();
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({}));
    abrir();
    await screen.findByTestId('painel-aguardando');

    const cartao = screen.getByRole('button', { name: /Em andamento/ });
    await usuario.click(cartao);
    expect(cartao).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('painel-andamento')).toBeInTheDocument();
    expect(screen.queryByTestId('painel-aguardando')).not.toBeInTheDocument();

    await usuario.click(cartao);
    expect(screen.getByTestId('painel-aguardando')).toBeInTheDocument();
  });

  it('bloco vazio diz que não há nada, em vez de tabela vazia', async () => {
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({
      concluidos: [], totals: { aguardandoAprovacao: 1, emAndamento: 1, concluidos: 0, parciais: 1 },
    }));
    abrir();
    await screen.findByTestId('painel-aguardando');
    expect(screen.queryByTestId('painel-concluido')).not.toBeInTheDocument();
    expect(screen.getAllByText('Nada por aqui. ✔').length).toBe(1);
  });

  it('agrupa por centro de custo e por solicitante', async () => {
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({}));
    abrir();
    const porCentro = await screen.findByTestId('painel-por-centro');
    expect(within(porCentro).getByText('BAH-001')).toBeInTheDocument();
    expect(within(screen.getByTestId('painel-por-solicitante')).getByText('Ana')).toBeInTheDocument();
  });

  it('a linha mostra o nome do centro, e o código fica na dica', async () => {
    // "BAH-001" não diz se a compra é da obra da Bahia ou do administrativo — era isso que
    // a lista mostrava em toda linha
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({}));
    vi.mocked(listarCentrosCusto).mockResolvedValue(
      [{ code: 'BAH-001', name: 'Obra Bahia' }] as never);
    abrir();

    // a mesma linha aparece em mais de um bloco da tela: basta a primeira
    const celulas = await screen.findAllByTitle('BAH-001');
    expect(celulas[0]).toHaveTextContent('Obra Bahia');
  });

  it('centro fora do cadastro continua aparecendo pelo código, não como traço', async () => {
    // some do cadastro depois, mas a solicitação dele continua existindo
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({}));
    vi.mocked(listarCentrosCusto).mockResolvedValue([] as never);
    abrir();

    expect((await screen.findAllByTitle('BAH-001'))[0]).toHaveTextContent('BAH-001');
  });
});

describe('o filtro do painel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(listarCentrosCusto).mockResolvedValue([]);
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({}));
  });

  it('abre sem recorte nenhum: o painel inteiro', async () => {
    abrir();
    await screen.findByTestId('painel-aguardando');
    expect(vi.mocked(painelDeAtendimentos).mock.calls[0][0]).toEqual({});
    expect(screen.queryByTestId('limpar-filtro')).not.toBeInTheDocument();
  });

  it('o recorte vai ao servidor, para o cartão continuar contando a lista que ele abre', async () => {
    const usuario = userEvent.setup();
    abrir();
    await screen.findByTestId('painel-aguardando');

    await usuario.selectOptions(screen.getByLabelText('Centro de custo'), 'BAH-001');
    await vi.waitFor(() => {
      const ultima = vi.mocked(painelDeAtendimentos).mock.calls.at(-1)?.[0];
      expect(ultima).toEqual({ costCenter: 'BAH-001' });
    });
  });

  it('a data de criação também recorta, e as duas pontas somam no contador', async () => {
    const usuario = userEvent.setup();
    abrir();
    await screen.findByTestId('painel-aguardando');

    await usuario.type(screen.getByLabelText('Criadas de'), '2026-09-01');
    await usuario.type(screen.getByLabelText('até'), '2026-09-30');
    await vi.waitFor(() => {
      expect(vi.mocked(painelDeAtendimentos).mock.calls.at(-1)?.[0])
        .toEqual({ from: '2026-09-01', to: '2026-09-30' });
    });
    expect(screen.getByTestId('limpar-filtro').parentElement).toHaveTextContent('2 filtro(s) valendo');
  });

  it('clicar na quebra por centro aplica o recorte daquela linha, e clicar de novo o tira', async () => {
    // o número da linha já promete um recorte: obrigar a repetir o centro no seletor ao lado
    // seria pedir duas vezes a mesma coisa
    const usuario = userEvent.setup();
    abrir();
    const porCentro = await screen.findByTestId('painel-por-centro');

    // é botão, não linha com onClick: chega pelo teclado e o aria-pressed diz o que está valendo
    const alvo = within(porCentro).getByRole('button', { name: 'Filtrar por BAH-001' });
    expect(alvo).toHaveAttribute('aria-pressed', 'false');
    await usuario.click(alvo);
    await vi.waitFor(() => {
      expect(vi.mocked(painelDeAtendimentos).mock.calls.at(-1)?.[0]).toEqual({ costCenter: 'BAH-001' });
    });
    expect(within(screen.getByTestId('painel-por-centro'))
      .getByRole('button', { name: 'Tirar o filtro de BAH-001' })).toHaveAttribute('aria-pressed', 'true');

    const chamadas = vi.mocked(painelDeAtendimentos).mock.calls.length;
    await usuario.click(within(screen.getByTestId('painel-por-centro'))
      .getByRole('button', { name: 'Tirar o filtro de BAH-001' }));
    await vi.waitFor(() => {
      // tirar o recorte consulta de novo, e não só repinta: os cartões precisam voltar a contar
      // o painel inteiro, senão o número do topo fica preso no recorte que já saiu
      expect(vi.mocked(painelDeAtendimentos).mock.calls.length).toBeGreaterThan(chamadas);
      expect(vi.mocked(painelDeAtendimentos).mock.calls.at(-1)?.[0]).toEqual({});
    });
  });

  it('a quebra por solicitante recorta pelo id, não pelo nome', async () => {
    // dois "João Silva" são duas pessoas, e o painel não pode somá-las numa só
    const usuario = userEvent.setup();
    abrir();
    const porSolicitante = await screen.findByTestId('painel-por-solicitante');

    await usuario.click(within(porSolicitante).getByRole('button', { name: 'Filtrar por Ana' }));
    await vi.waitFor(() => {
      expect(vi.mocked(painelDeAtendimentos).mock.calls.at(-1)?.[0])
        .toEqual({ requesterId: '11111111-1111-1111-1111-111111111111' });
    });
  });

  it('limpar devolve o painel inteiro de uma vez', async () => {
    const usuario = userEvent.setup();
    abrir();
    await screen.findByTestId('painel-aguardando');

    await usuario.selectOptions(screen.getByLabelText('Centro de custo'), 'BAH-001');
    await vi.waitFor(() => {
      expect(vi.mocked(painelDeAtendimentos).mock.calls.at(-1)?.[0]).toEqual({ costCenter: 'BAH-001' });
    });

    await usuario.click(screen.getByTestId('limpar-filtro'));
    await vi.waitFor(() => {
      expect(vi.mocked(painelDeAtendimentos).mock.calls.at(-1)?.[0]).toEqual({});
    });
    expect(screen.queryByTestId('limpar-filtro')).not.toBeInTheDocument();
  });

  it('o seletor oferece o cadastro inteiro, e não só o que sobrou do recorte', async () => {
    // a quebra conta o recorte; o seletor responde "para onde eu posso ir agora". Se ele viesse
    // do recorte, filtrar o BAH-001 esconderia o PER-002 e trocar de centro exigiria limpar tudo
    const usuario = userEvent.setup();
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({
      // o recorte já devolveu uma quebra de um centro só — é o que acontece ao filtrar
      porCentro: [{ costCenter: 'BAH-001', total: 2, emAndamento: 1, concluidos: 1, parciais: 0 }],
    }));
    abrir();
    await screen.findByTestId('painel-aguardando');

    const centro = screen.getByLabelText('Centro de custo');
    expect([...centro.querySelectorAll('option')].map((o) => o.textContent))
      .toEqual(['Todos', 'BAH-001', 'PER-002']);

    await usuario.selectOptions(centro, 'PER-002');
    await vi.waitFor(() => {
      expect(vi.mocked(painelDeAtendimentos).mock.calls.at(-1)?.[0]).toEqual({ costCenter: 'PER-002' });
    });
  });

  it('o seletor de solicitante mostra os homônimos como duas linhas, porque o valor é o id', async () => {
    vi.mocked(painelDeAtendimentos).mockResolvedValue(painel({
      opcoes: {
        centros: ['BAH-001'],
        solicitantes: [
          { id: '11111111-1111-1111-1111-111111111111', label: 'Ana Silva' },
          { id: '22222222-2222-2222-2222-222222222222', label: 'Ana Silva' },
        ],
      },
    }));
    abrir();
    await screen.findByTestId('painel-aguardando');

    const opcoes = [...screen.getByLabelText('Solicitante').querySelectorAll('option')];
    expect(opcoes.map((o) => o.textContent)).toEqual(['Todos', 'Ana Silva', 'Ana Silva']);
    expect(opcoes.map((o) => o.getAttribute('value'))).toEqual([
      '', '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222',
    ]);
  });

  it('o filtro continua na tela quando a consulta falha, para dar como desfazer o recorte', async () => {
    // painel que troca a barra por uma mensagem de erro prende o usuário no filtro que quebrou
    vi.mocked(painelDeAtendimentos).mockRejectedValue(new Error('MR-ERR-001'));
    abrir();
    expect(await screen.findByTestId('filtro-painel')).toBeInTheDocument();
  });
});
