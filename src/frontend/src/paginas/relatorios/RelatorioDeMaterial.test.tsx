import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RelatorioDeMaterial as Dados, SolicitacaoDoRelatorioDeMaterial } from '@/api/material';
import { ToastProvider } from '@/componentes/Toast';
import { moeda } from '@/util/formato';

// o texto do DOM normaliza o espaço fixo que `moeda` põe entre "R$" e o número
const rs = (v: number) => moeda(v).replace(/\u00a0/g, ' ');
import { prazoDaLinha, RelatorioDeMaterial } from './RelatorioDeMaterial';

vi.mock('@/api/material', async (importar) => ({
  ...(await importar<typeof import('@/api/material')>()),
  relatorioDeMaterial: vi.fn(), pdfDoRelatorioDeMaterial: vi.fn(), planilhaDoRelatorioDeMaterial: vi.fn(),
}));
vi.mock('@/api/cliente', async (importar) => ({
  ...(await importar<typeof import('@/api/cliente')>()), abrirBlob: vi.fn(), salvarBlob: vi.fn(),
}));
import { abrirBlob, salvarBlob } from '@/api/cliente';
import { pdfDoRelatorioDeMaterial, planilhaDoRelatorioDeMaterial, relatorioDeMaterial } from '@/api/material';

const solicitacao = (p: Partial<SolicitacaoDoRelatorioDeMaterial> = {}): SolicitacaoDoRelatorioDeMaterial => ({
  id: 'mr1', number: 'MR-2026-000003', createdAt: '2026-10-03T12:00:00Z', costCenter: 'BAH-001', costCenterName: 'Obra Bahia',
  requester: 'Ana', status: 'AGUARDANDO_ALMOXARIFADO', statusLabel: 'Aguardando almoxarifado',
  approvedAt: '2026-10-03T13:00:00Z', approvedBy: 'Bruno', fulfilledAt: null, fulfilledBy: null, purchaseRequisitionNumber: null,
  items: 1, requestedQty: 2, deliveredQty: 0, requestedValue: 24, approvedValue: 24, deliveredValue: null, itemsWithoutPrice: 0,
  slaStatus: 'ESTOURADO', slaDays: 4, slaMaxDays: 2,
  itemList: [{ code: 'EPI-001', description: 'Luva nitrílica', family: 'EPI', unit: 'PAR', qty: 2, approvedQty: null, deliveredQty: 0,
    unitPrice: 12, requestedValue: 24, approvedValue: 24, deliveredValue: 0, status: 'PENDENTE', statusLabel: 'pendente' }],
  ...p,
});

const dados = (p: Partial<Dados> = {}): Dados => ({
  from: '2026-10-01', to: '2026-10-07',
  kpis: {
    requested: 4, requestedPrev: 2, requestedQty: 20, deliveredQty: 10, awaitingApproval: 1, inWarehouseQueue: 1,
    fulfilled: 1, partial: 0, rejected: 1, cancelled: 0, purchaseRouteItems: 0, avgApprovalHours: 2, avgFulfillDays: 2,
    avgTotalDays: 2.2, slaMetPercent: 100, slaMeasured: 1, slaBreachedOpen: 1,
    requestedValue: 317, approvedValue: 300, deliveredValue: 120, itemsWithoutPrice: 1,
  },
  months: [{ month: '2026-10', requested: 4, fulfilled: 1, rejected: 1, requestedValue: 317, deliveredValue: 120 }],
  byCostCenter: [{ label: 'BAH-001 — Obra Bahia', key: 'BAH-001', count: 2, qty: 12, delivered: 10, requestedValue: 156, deliveredValue: 120 }],
  byFamily: [{ label: 'EPI', key: 'EPI', count: 3, qty: 15, delivered: 10, requestedValue: 180, deliveredValue: 120 }],
  byProduct: [],
  byRequester: [{ label: 'Ana', key: 'u1', count: 3, qty: 15, delivered: 10, requestedValue: 180, deliveredValue: 120 }],
  slaByFamily: [{ family: 'EPI', maxDays: 2, measured: 1, met: 1, avgDays: 2 }],
  filterOptions: {
    costCenters: [{ code: 'BAH-001', name: 'Obra Bahia' }], families: ['EPI'],
    requesters: [{ id: 'u1', label: 'Ana' }, { id: 'u2', label: 'Beto' }],
  },
  requisitions: [
    solicitacao(),
    solicitacao({ id: 'mr2', number: 'MR-2026-000001', status: 'ATENDIDA', statusLabel: 'Atendida', deliveredValue: 120,
      requestedValue: 120, slaStatus: 'OK', slaDays: 2, slaMaxDays: 2 }),
    solicitacao({ id: 'mr3', number: 'MR-2026-000006', requestedValue: null, itemsWithoutPrice: 1, slaStatus: null, slaDays: null, slaMaxDays: null }),
  ],
  ...p,
});

const abrir = () => render(<MemoryRouter><ToastProvider><RelatorioDeMaterial /></ToastProvider></MemoryRouter>);

describe('o prazo da linha', () => {
  it('diz o veredito com os dias, e sem prazo é traço', () => {
    expect(prazoDaLinha({ slaStatus: 'ESTOURADO', slaDays: 4, slaMaxDays: 2 }).texto).toBe('estourado (4d de 2d)');
    expect(prazoDaLinha({ slaStatus: 'ATENCAO', slaDays: 2, slaMaxDays: 2 }).texto).toBe('a vencer (2d de 2d)');
    expect(prazoDaLinha({ slaStatus: 'OK', slaDays: 1, slaMaxDays: 2 }).texto).toBe('no prazo (1d de 2d)');
    expect(prazoDaLinha({ slaStatus: null, slaDays: null, slaMaxDays: null }).texto).toBe('—');
  });
});

describe('<RelatorioDeMaterial />', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(relatorioDeMaterial).mockResolvedValue(dados());
  });

  it('abre com os números, os valores e os sete blocos', async () => {
    abrir();
    const kpis = await screen.findByTestId('kpis-relatorio-material');
    expect(within(kpis).getByText('Valor pedido').parentElement).toHaveTextContent(rs(317));
    expect(within(kpis).getByText('Valor entregue').parentElement).toHaveTextContent(rs(120));
    expect(within(kpis).getByText('Itens sem custo').parentElement).toHaveTextContent('1');
    for (const titulo of ['1. Por centro de custo', '2. Por família', '3. Por produto', '4. Por solicitante',
      '5. Mês a mês', '6. Prazo de atendimento por família', '7. Todas as solicitações do período — 3'])
      expect(screen.getByRole('heading', { name: titulo })).toBeInTheDocument();
    // bloco vazio é dito, não tabela em branco
    expect(screen.getAllByText('Nenhuma solicitação no período.').length).toBeGreaterThan(0);
  });

  it('a lista completa diz a situação, o valor com o que falta e o prazo de cada solicitação', async () => {
    abrir();
    const tabela = await screen.findByTestId('rm-solicitacoes');
    const linha = within(tabela).getAllByText('MR-2026-000003')[0].closest('tr')!;
    expect(linha).toHaveTextContent('Aguardando almoxarifado');
    expect(linha).toHaveTextContent('Obra Bahia');
    expect(linha).toHaveTextContent('estourado (4d de 2d)');
    expect(linha).toHaveTextContent(rs(24));
    // a sem custo diz isso, em vez de R$ 0,00
    const semCusto = within(tabela).getAllByText('MR-2026-000006')[0].closest('tr')!;
    expect(semCusto).toHaveTextContent('sem custo cadastrado');
    // os itens abrem na linha, com o custo congelado
    expect(linha).toHaveTextContent('[EPI-001] Luva nitrílica');
    expect(linha).toHaveTextContent(`${rs(12)}/PAR`);
  });

  it('os filtros só consultam ao aplicar, e o solicitante vai pelo id', async () => {
    abrir();
    await screen.findByTestId('kpis-relatorio-material');
    expect(relatorioDeMaterial).toHaveBeenCalledTimes(1);
    await userEvent.selectOptions(screen.getByLabelText('Solicitante'), 'u2');
    await userEvent.selectOptions(screen.getByLabelText('Família'), 'EPI');
    expect(relatorioDeMaterial).toHaveBeenCalledTimes(1);
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }));
    await waitFor(() => expect(relatorioDeMaterial).toHaveBeenCalledTimes(2));
    expect(vi.mocked(relatorioDeMaterial).mock.calls[1][0]).toMatchObject({ solicitante: 'u2', familia: 'EPI' });
  });

  it('o PDF abre numa aba e a planilha baixa com o nome do período, os dois com o recorte aplicado', async () => {
    vi.mocked(pdfDoRelatorioDeMaterial).mockResolvedValue(new Blob(['%PDF']));
    vi.mocked(planilhaDoRelatorioDeMaterial).mockResolvedValue(new Blob(['PK']));
    abrir();
    await screen.findByTestId('kpis-relatorio-material');
    await userEvent.selectOptions(screen.getByLabelText('Centro de custo'), 'BAH-001');
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }));
    await waitFor(() => expect(relatorioDeMaterial).toHaveBeenCalledTimes(2));

    await userEvent.click(screen.getByRole('button', { name: 'Exportar em PDF' }));
    await waitFor(() => expect(abrirBlob).toHaveBeenCalled());
    expect(vi.mocked(pdfDoRelatorioDeMaterial).mock.calls[0][0]).toMatchObject({ centroCusto: 'BAH-001' });

    await userEvent.click(screen.getByRole('button', { name: 'Baixar planilha' }));
    await waitFor(() => expect(salvarBlob).toHaveBeenCalledWith(expect.any(Blob), 'solicitacoes-de-material-2026-10-01_a_2026-10-07.xlsx'));
    expect(vi.mocked(planilhaDoRelatorioDeMaterial).mock.calls[0][0]).toMatchObject({ centroCusto: 'BAH-001' });
  });

  it('a falha da consulta aparece, e os botões de exportar ficam desligados sem dados', async () => {
    vi.mocked(relatorioDeMaterial).mockRejectedValue(new Error('403'));
    abrir();
    await waitFor(() => expect(screen.getByText(/403/)).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Exportar em PDF' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Baixar planilha' })).toBeDisabled();
  });
});
