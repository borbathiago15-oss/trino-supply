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
  aguardandoAprovacao: [linha({ number: 'MR-AGUARDANDO' })],
  emAndamento: [linha({ id: 'mr2', number: 'MR-ANDAMENTO', approvedAt: '2026-09-02T12:00:00Z' })],
  concluidos: [linha({ id: 'mr3', number: 'MR-CONCLUIDO', fulfilledByLabel: 'Zé' })],
  parciais: [linha({ id: 'mr4', number: 'MR-PARCIAL', pending: 6, purchaseRequisitionNumber: 'SC-2026-000030' })],
  porCentro: [{ costCenter: 'BAH-001', total: 4, emAndamento: 2, concluidos: 1, parciais: 1 }],
  porSolicitante: [{ requesterLabel: 'Ana', total: 4, emAndamento: 2, concluidos: 1, parciais: 1 }],
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
