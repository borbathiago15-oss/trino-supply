import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { Processo } from '@/api/cotacoes';
import { proximoPasso, ProximoPasso } from './ProximoPasso';

const processo = (p: Partial<Processo>): Processo => ({
  id: 'q1', number: 'RFQ-2026-000001', kind: 'COTACAO', status: 'EM_ANALISE', costCenter: 'BAH-001',
  deadline: null, createdAt: '2026-09-01T10:00:00Z', createdByLabel: 'Carla', decisionReason: null,
  families: ['EPI'], items: [], suppliers: [], proposals: [], awards: [], sourcePrNumbers: [],
  sourcePrNumber: null, selection: null, managerApproval: null, directorApproval: null,
  pendingPoSuppliers: [], purchaseOrders: [],
  ...p,
} as unknown as Processo);

const montar = (p: Partial<Processo>) => render(
  <MemoryRouter><ProximoPasso processo={processo(p)} /></MemoryRouter>,
);

describe('proximoPasso', () => {
  it('quem escolheu o fornecedor segue para a Central e dá o Nível 1 do próprio processo', () => {
    // o comprador cota, escolhe e fecha a primeira alçada: a segregação fica no Nível 2
    const q = processo({
      status: 'AGUARDANDO_GERENTE',
      selection: { winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: null, justification: 'x', by: 'eu', byLabel: 'Eu' },
    });
    const passo = proximoPasso(q, 'eu')!;
    expect(passo.titulo).toBe('Aprovação de Nível 1');
    expect(passo.rota).toBe('/aprovacoes');
    expect(passo.detalhe).not.toMatch(/RFQ-ERR-030/);
  });

  it('quem escolheu o fornecedor lê que o Nível 2 é de outra pessoa, sem link para a Central', () => {
    const q = processo({
      status: 'AGUARDANDO_DIRETOR',
      selection: { winnerSupplierId: 's1', winnerProposalId: 'p1', criteria: null, justification: 'x', by: 'eu', byLabel: 'Eu' },
    });
    const passo = proximoPasso(q, 'eu')!;
    expect(passo.titulo).toBe('Aprovação de Nível 2');
    expect(passo.detalhe).toMatch(/Você escolheu o fornecedor.*Nível 2/);
    expect(passo.rota).toBeUndefined();
    // outra pessoa continua vendo o caminho normal
    expect(proximoPasso(q, 'outro')!.rota).toBe('/aprovacoes');
  });

  it('quem deu o Nível 1 lê que o Nível 2 é de outra pessoa', () => {
    const q = processo({ status: 'AGUARDANDO_DIRETOR', managerApproval: { by: 'eu', byLabel: 'Eu', at: '2026-09-02T10:00:00Z' } });
    expect(proximoPasso(q, 'eu')!.detalhe).toMatch(/Nível 2 é de outra pessoa/);
  });

  it('a aprovação sai desta tela: manda para a Central de Aprovação', () => {
    // é o salto que confundia — a aprovação não fica no grupo Compras
    for (const status of ['AGUARDANDO_GERENTE', 'AGUARDANDO_DIRETOR'])
      expect(proximoPasso(processo({ status }))!.rota).toBe('/aprovacoes');
    // só o Nível 2 tem segregação de funções a avisar
    expect(proximoPasso(processo({ status: 'AGUARDANDO_DIRETOR' }))!.detalhe).toMatch(/RFQ-ERR-030/);
    expect(proximoPasso(processo({ status: 'AGUARDANDO_GERENTE' }))!.detalhe).not.toMatch(/RFQ-ERR-030/);
  });

  it('com a O.C. registrada, aponta o pedido em que a compra continua', () => {
    const passo = proximoPasso(processo({
      status: 'OC_REGISTRADA',
      purchaseOrders: [{ id: 'po1', number: 'PO-2026-000007', supplierName: 'Alfa', families: ['EPI'], totalValue: 10 }],
    }))!;
    expect(passo.rota).toBe('/pedidos/po1');
    expect(passo.rotulo).toContain('PO-2026-000007');
  });

  it('aprovado com o pedido criado na alçada, aponta a tela do pedido para a O.C.', () => {
    const oc = (id: string, erpPending = true) =>
      ({ id, number: `PO-${id}`, supplierName: 'A', families: [], totalValue: 1, erpNumber: null, noErpReason: null, erpPending });
    const passo = proximoPasso(processo({ status: 'APROVADO_PARA_EMISSAO', purchaseOrders: [oc('a')] }))!;
    expect(passo.titulo).toMatch(/na tela do pedido/);
    expect(passo.rota).toBe('/pedidos/a');
    expect(passo.rotulo).toContain('PO-a');
    // dois pedidos e só um sem O.C.: leva direto ao que falta
    const falta = proximoPasso(processo({
      status: 'APROVADO_PARA_EMISSAO',
      purchaseOrders: [{ ...oc('a', false), erpNumber: 'OC-1' }, oc('b')],
    }))!;
    expect(falta.rota).toBe('/pedidos/b');
    // fornecedor ainda sem pedido: o formulário antigo continua aqui, sem link
    expect(proximoPasso(processo({
      status: 'APROVADO_PARA_EMISSAO', purchaseOrders: [oc('a')],
      pendingPoSuppliers: [{ supplierId: 's2', supplierName: 'B', families: ['X'], totalValue: 1 }],
    }))!.rota).toBeUndefined();
  });

  it('compra dividida em várias O.C. cai na lista, não num pedido só', () => {
    const oc = (id: string) => ({ id, number: id, supplierName: 'A', families: [], totalValue: 1 });
    const passo = proximoPasso(processo({ status: 'OC_REGISTRADA', purchaseOrders: [oc('a'), oc('b')] }))!;
    expect(passo.rota).toBe('/pedidos');
  });

  it('os passos que se fazem aqui mesmo não oferecem link', () => {
    for (const status of ['COTACAO_ABERTA', 'EM_ANALISE', 'APROVADO_PARA_EMISSAO'])
      expect(proximoPasso(processo({ status }))?.rota).toBeUndefined();
  });

  it('processo encerrado não tem passo seguinte a apontar', () => {
    expect(proximoPasso(processo({ status: 'REJEITADO' }))).toBeNull();
    expect(proximoPasso(processo({ status: 'CANCELADA' }))).toBeNull();
  });
});

describe('<ProximoPasso />', () => {
  it('mostra o passo e o caminho até ele', () => {
    montar({ status: 'AGUARDANDO_GERENTE' });
    expect(screen.getByTestId('proximo-passo')).toHaveTextContent('Aprovação de Nível 1');
    expect(screen.getByRole('link', { name: /Central de Aprovação/ })).toHaveAttribute('href', '/aprovacoes');
  });

  it('some quando não há passo seguinte', () => {
    montar({ status: 'CANCELADA' });
    expect(screen.queryByTestId('proximo-passo')).not.toBeInTheDocument();
  });
});
