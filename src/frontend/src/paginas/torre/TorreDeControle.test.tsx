import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LinhaDaTorre, PaginaDaTorre } from '@/api/torre';
import { consultaDaTorre, FILTROS_TORRE_VAZIOS } from '@/api/torre';
import { TorreDeControle } from './TorreDeControle';

vi.mock('@/api/torre', async (importar) => ({
  ...(await importar<typeof import('@/api/torre')>()),
  torreDeControle: vi.fn(),
}));

import { torreDeControle } from '@/api/torre';

const linha = (p: Partial<LinhaDaTorre> = {}): LinhaDaTorre => ({
  itemId: 'i1', requisitionId: 'r1', prNumber: 'PR-2026-000001', sequence: 1,
  catalogCode: 'EPI-001', description: 'Luva de vaqueta', quantity: 10, unitOfMeasure: 'PAR',
  requesterLabel: 'Ana', company: 'Trino Nordeste', costCenter: 'CC-NE-01',
  buyerLabel: 'Carla', supplierName: null,
  stage: 'SOLICITACAO', stageLabel: 'Solicitação',
  statusKey: 'PENDENTE', statusLabel: 'Pendente', statusTone: '',
  priority: 'NORMAL', neededBy: '2026-09-20', promisedDate: null, late: false,
  value: 200, quotationId: null, quotationNumber: null,
  purchaseOrderId: null, purchaseOrderNumber: null, exceptionReason: null,
  ...p,
});

const pagina = (p: Partial<PaginaDaTorre> = {}): PaginaDaTorre => ({
  items: [linha()],
  kpis: {
    total: 12, novos: 4, emCotacao: 3, aguardandoAprovacao: 2, aguardandoOc: 1,
    aguardandoRecebimento: 1, atrasados: 2, urgentes: 1, valor: 24000,
    emFaturamento: 2, excecoes: 3,
  },
  filterOptions: {
    companies: ['Trino Nordeste'], costCenters: [{ code: 'CC-NE-01', name: 'Filial Recife' }],
    families: ['EPI'], requesters: [{ id: 'u1', label: 'Ana' }], buyers: [{ id: 'u2', label: 'Carla' }],
  },
  page: 1, pageSize: 50, total: 12, pages: 1, capped: false, cap: 3000,
  ...p,
});

const abrir = () => render(<MemoryRouter><TorreDeControle /></MemoryRouter>);

describe('Torre de Controle', () => {
  beforeEach(() => vi.resetAllMocks());

  it('mostra uma linha por item, com etapa e situação', async () => {
    vi.mocked(torreDeControle).mockResolvedValue(pagina({
      items: [
        linha({ itemId: 'i1', description: 'Luva de vaqueta', stage: 'COTACAO', stageLabel: 'Cotação',
          statusLabel: 'Em Cotação', quotationId: 'q1', quotationNumber: 'RFQ-2026-000001' }),
        linha({ itemId: 'i2', sequence: 2, description: 'Bota de segurança' }),
      ],
    }));
    abrir();

    const tabela = within(await screen.findByTestId('tabela-torre'));
    // dois itens da mesma SC, em etapas diferentes — é a razão de a Torre existir
    expect(tabela.getByText('Luva de vaqueta', { exact: false })).toBeInTheDocument();
    expect(tabela.getByText('Bota de segurança', { exact: false })).toBeInTheDocument();
    expect(tabela.getByText('Cotação')).toBeInTheDocument();
    expect(tabela.getByText('Solicitação')).toBeInTheDocument();
    // o link leva para onde a ação está
    expect(tabela.getByRole('link', { name: 'RFQ-2026-000001' }))
      .toHaveAttribute('href', '/cotacoes/q1');
  });

  it('o KPI filtra: clicar em "Em cotação" recarrega a Torre naquela etapa', async () => {
    const usuario = userEvent.setup();
    vi.mocked(torreDeControle).mockResolvedValue(pagina());
    abrir();
    await screen.findByTestId('tabela-torre');

    await usuario.click(screen.getByRole('button', { name: /Em cotação/ }));

    await waitFor(() => expect(torreDeControle).toHaveBeenLastCalledWith(
      expect.objectContaining({ etapa: 'COTACAO', pagina: 1 }), expect.anything()));
  });

  it('clicar de novo no mesmo KPI desfaz o filtro', async () => {
    const usuario = userEvent.setup();
    vi.mocked(torreDeControle).mockResolvedValue(pagina());
    abrir();
    await screen.findByTestId('tabela-torre');

    await usuario.click(screen.getByRole('button', { name: /Em cotação/ }));
    await waitFor(() => expect(torreDeControle).toHaveBeenLastCalledWith(
      expect.objectContaining({ etapa: 'COTACAO' }), expect.anything()));

    await usuario.click(screen.getByRole('button', { name: /Em cotação/ }));
    await waitFor(() => expect(torreDeControle).toHaveBeenLastCalledWith(
      expect.objectContaining({ etapa: '' }), expect.anything()));
  });

  it('trocar de página mantém o filtro e só muda a página', async () => {
    const usuario = userEvent.setup();
    vi.mocked(torreDeControle).mockResolvedValue(pagina({ page: 1, pages: 3, total: 120 }));
    abrir();
    await screen.findByTestId('tabela-torre');
    expect(screen.getByTestId('torre-contagem')).toHaveTextContent('120 item(ns) · página 1 de 3');

    await usuario.click(screen.getByRole('button', { name: 'Próxima' }));
    await waitFor(() => expect(torreDeControle).toHaveBeenLastCalledWith(
      expect.objectContaining({ pagina: 2 }), expect.anything()));
  });

  it('aplicar filtro estando na página 3 volta para a primeira', async () => {
    // filtro novo, contagem nova: continuar na página 3 mostraria uma página que
    // talvez nem exista no recorte recém-aplicado
    const usuario = userEvent.setup();
    vi.mocked(torreDeControle).mockResolvedValue(pagina({ page: 1, pages: 5, total: 200 }));
    abrir();
    await screen.findByTestId('tabela-torre');

    await usuario.click(screen.getByRole('button', { name: 'Próxima' }));
    await waitFor(() => expect(torreDeControle).toHaveBeenLastCalledWith(
      expect.objectContaining({ pagina: 2 }), expect.anything()));

    await usuario.click(screen.getByRole('button', { name: /Atrasados/ }));
    await waitFor(() => expect(torreDeControle).toHaveBeenLastCalledWith(
      expect.objectContaining({ atrasados: true, pagina: 1 }), expect.anything()));
  });

  it('na primeira página, "Anterior" fica travado', async () => {
    vi.mocked(torreDeControle).mockResolvedValue(pagina({ page: 1, pages: 3 }));
    abrir();
    await screen.findByTestId('tabela-torre');
    expect(screen.getByRole('button', { name: 'Anterior' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Próxima' })).toBeEnabled();
  });

  it('item atrasado é marcado na linha, não só contado no topo', async () => {
    vi.mocked(torreDeControle).mockResolvedValue(pagina({
      items: [linha({ late: true, promisedDate: '2026-09-01' })],
    }));
    abrir();
    const tabela = within(await screen.findByTestId('tabela-torre'));
    expect(tabela.getByText('atrasado')).toBeInTheDocument();
  });

  it('quando o filtro derivado bate no teto, a tela avisa em vez de calar', async () => {
    vi.mocked(torreDeControle).mockResolvedValue(pagina({ capped: true, cap: 3000 }));
    abrir();
    const aviso = await screen.findByTestId('torre-teto');
    expect(aviso).toHaveTextContent(/teto de 3.000 itens/);
  });

  it('os filtros do §5.1 chegam ao servidor com o nome que a API espera', async () => {
    const usuario = userEvent.setup();
    vi.mocked(torreDeControle).mockResolvedValue(pagina());
    abrir();
    await screen.findByTestId('tabela-torre');

    await usuario.type(screen.getByLabelText('Fornecedor'), 'Alfa');
    await usuario.type(screen.getByLabelText(/Número da O\.C\./), '4521');
    await usuario.type(screen.getByLabelText('Valor de (R$)'), '1000');
    await usuario.click(screen.getByRole('button', { name: 'Aplicar filtros' }));

    await waitFor(() => expect(torreDeControle).toHaveBeenLastCalledWith(
      expect.objectContaining({
        fornecedor: 'Alfa', numeroOc: '4521', valorDe: '1000', pagina: 1,
      }), expect.anything()));
  });

  it('a consulta monta os parâmetros de faixa e omite o campo vazio', () => {
    // zero é valor legítimo numa faixa; o que descarta é o campo em branco
    const cheia = consultaDaTorre({
      ...FILTROS_TORRE_VAZIOS, fornecedor: ' Alfa ', numeroOc: 'PO-1',
      prazoDe: '2026-09-01', prazoAte: '2026-09-30', valorDe: '0', valorAte: '5000',
      de: '2026-08-01', ate: '2026-08-31',
    });
    expect(cheia).toContain('supplier=Alfa');          // aparado
    expect(cheia).toContain('orderNumber=PO-1');
    expect(cheia).toContain('dueFrom=2026-09-01');
    expect(cheia).toContain('dueTo=2026-09-30');
    expect(cheia).toContain('minValue=0');             // zero vai, não é "vazio"
    expect(cheia).toContain('maxValue=5000');
    expect(cheia).toContain('from=2026-08-01');
    expect(cheia).toContain('to=2026-08-31');

    const vazia = consultaDaTorre(FILTROS_TORRE_VAZIOS);
    for (const p of ['supplier', 'orderNumber', 'dueFrom', 'dueTo', 'minValue', 'maxValue', 'from=', 'to='])
      expect(vazia).not.toContain(p);
  });

  it('em faturamento e aguardando recebimento aparecem como dois números (§5)', async () => {
    vi.mocked(torreDeControle).mockResolvedValue(pagina());
    abrir();
    await screen.findByTestId('tabela-torre');

    // duas filas, dois donos: sem NF a bola está com o fornecedor
    expect(screen.getByRole('button', { name: /Em faturamento/ })).toHaveTextContent('2');
    expect(screen.getByRole('button', { name: /Aguardando recebimento/ })).toHaveTextContent('1');
  });

  it('o KPI de exceções filtra e a linha diz o motivo', async () => {
    const usuario = userEvent.setup();
    vi.mocked(torreDeControle).mockResolvedValue(pagina({
      items: [linha({ exceptionReason: 'Fechado sem O.C. do ERP' })],
    }));
    abrir();

    const tabela = within(await screen.findByTestId('tabela-torre'));
    expect(tabela.getByText(/Fechado sem O\.C\. do ERP/)).toBeInTheDocument();

    await usuario.click(screen.getByRole('button', { name: /Exceções/ }));
    await waitFor(() => expect(torreDeControle).toHaveBeenLastCalledWith(
      expect.objectContaining({ excecoes: true, etapa: '', pagina: 1 }), expect.anything()));
  });

  it('linha sem exceção não ganha marca nenhuma', async () => {
    vi.mocked(torreDeControle).mockResolvedValue(pagina({ items: [linha()] }));
    abrir();
    const tabela = within(await screen.findByTestId('tabela-torre'));
    expect(tabela.queryByText(/⚠/)).not.toBeInTheDocument();
  });

  it('sem item no recorte, diz isso em vez de mostrar tabela vazia', async () => {
    vi.mocked(torreDeControle).mockResolvedValue(pagina({ items: [], total: 0, pages: 0 }));
    abrir();
    expect(await screen.findByText('Nenhum item de compra neste recorte.')).toBeInTheDocument();
    expect(screen.queryByTestId('tabela-torre')).not.toBeInTheDocument();
  });
});
