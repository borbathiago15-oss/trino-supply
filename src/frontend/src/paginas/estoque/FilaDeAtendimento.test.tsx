import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { ItemMaterial, SolicitacaoMaterial } from '@/api/material';
import { ToastProvider } from '@/componentes/Toast';
import { aprovado, FilaDeAtendimento, validarAtendimento } from './FilaDeAtendimento';

vi.mock('@/api/material', async (importar) => ({
  ...(await importar<typeof import('@/api/material')>()),
  filaDoAlmoxarifado: vi.fn(), atenderMaterial: vi.fn(),
}));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => eu }));

import { atenderMaterial, filaDoAlmoxarifado } from '@/api/material';

const eu: Usuario = { id: 'u1', email: 'zé@t.com', name: 'Zé', role: 'WarehouseOperator', modules: ['ESTOQUE'] };

const item = (p: Partial<ItemMaterial>): ItemMaterial => ({
  itemId: 'i1', catalogCode: 'EPI-001', description: 'Luva nitrílica', quantity: 10,
  unitOfMeasure: 'PAR', effectiveQuantity: 10, status: 'PENDENTE',
  ...p,
});

const mr = (p: Partial<SolicitacaoMaterial>): SolicitacaoMaterial => ({
  id: 'mr1', number: 'MR-2026-000001', status: 'AGUARDANDO_ALMOXARIFADO', costCenter: 'BAH-001',
  requesterId: 'u9', requesterLabel: 'Ana Paula', notes: null, items: [item({})],
  ...p,
});

const abrir = () => render(<ToastProvider><FilaDeAtendimento /></ToastProvider>);

describe('regras do atendimento', () => {
  it('o que vale é o aprovado quando o Nível 1 cortou a quantidade', () => {
    expect(aprovado(item({ quantity: 10, effectiveQuantity: 4 }))).toBe(4);
    expect(aprovado(item({ quantity: 10, effectiveQuantity: undefined, approvedQuantity: 6 }))).toBe(6);
    expect(aprovado(item({ quantity: 10, effectiveQuantity: undefined, approvedQuantity: null }))).toBe(10);
  });
  it('entregar mais do que ainda falta não passa', () => {
    expect(validarAtendimento({ i1: '11' }, [item({ effectiveQuantity: 10 })]).erro)
      .toBe('Não dá para entregar mais do que ainda falta.');
  });
  it('o teto é o que falta, não o aprovado: a parcial já entregue não se entrega de novo', () => {
    // a solicitação que ficou pendente volta com parte do saldo baixada
    const parcial = item({ effectiveQuantity: 10, fulfilledQuantity: 6, pendingQuantity: 4 });
    expect(validarAtendimento({ i1: '4' }, [parcial]).erro).toBeNull();
    expect(validarAtendimento({ i1: '5' }, [parcial]).erro)
      .toBe('Não dá para entregar mais do que ainda falta.');
  });
  it('deixar pendente sem entregar nada não registra nada — o caminho é fechar o painel', () => {
    expect(validarAtendimento({ i1: '0' }, [item({})], false).erro).toMatch(/feche o painel/);
    expect(validarAtendimento({}, [item({})], false).erro).toMatch(/feche o painel/);
  });
  it('concluir sem entregar nada é decisão válida: não tinha nada, tudo segue para compra', () => {
    // antes a tela mandava fechar o painel, e a solicitação ficava na fila para sempre
    expect(validarAtendimento({ i1: '0' }, [item({})], true).erro).toBeNull();
  });
  it('zero num item e quantidade em outro é atendimento parcial válido', () => {
    const { items, erro } = validarAtendimento(
      { i1: '0', i2: '3' }, [item({}), item({ itemId: 'i2', effectiveQuantity: 5 })]);
    expect(erro).toBeNull();
    expect(items).toEqual([{ itemId: 'i1', quantity: 0 }, { itemId: 'i2', quantity: 3 }]);
  });
});

describe('tela Fila de Atendimento', () => {
  beforeEach(() => vi.resetAllMocks());

  it('o filtro de escopo troca a consulta e explica a fila vazia', async () => {
    const usuario = userEvent.setup();
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([]);
    abrir();
    expect(await screen.findByText('Nenhuma solicitação aprovada aguardando atendimento.')).toBeInTheDocument();

    await usuario.selectOptions(screen.getByLabelText('Escopo da fila'), 'MINHAS');
    await waitFor(() => expect(filaDoAlmoxarifado).toHaveBeenCalledWith(true, expect.anything()));
    expect(await screen.findByText(/Nenhuma solicitação designada a você/)).toBeInTheDocument();
  });

  it('marca a solicitação designada a quem está olhando', async () => {
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([
      mr({ assignedToId: 'u1', assignedToLabel: 'Zé' }),
      mr({ id: 'mr2', number: 'MR-2026-000002', assignedToId: 'u7', assignedToLabel: 'Outro' }),
    ]);
    abrir();
    const tabela = await screen.findByTestId('fila-almoxarifado');
    expect(within(tabela).getByText('designada a você')).toBeInTheDocument();
  });

  it('a fila mostra o prazo de atendimento, com o número e a família que o impôs', async () => {
    // o prazo é a mesma marca do Painel de Atendimentos (MarcaDoSla): duas telas que
    // mostrassem o mesmo prazo de jeitos diferentes deixariam o almoxarife sem saber em
    // qual acreditar
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([
      mr({ sla: { maxDays: 2, days: 3, status: 'ESTOURADO', family: 'EPI' } }),
    ]);
    abrir();
    const celula = await screen.findByTestId('sla-MR-2026-000001');
    expect(celula).toHaveTextContent('prazo estourado');
    expect(celula).toHaveTextContent('3 de 2 dia(s) (EPI)');
  });

  it('solicitação sem prazo mostra traço, e não "no prazo"', async () => {
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([mr({ sla: null })]);
    abrir();
    expect(await screen.findByTestId('sla-MR-2026-000001')).toHaveTextContent('—');
  });

  it('atender abre com o aprovado preenchido e envia o que ficou na grade', async () => {
    const usuario = userEvent.setup();
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([
      mr({ items: [item({}), item({ itemId: 'i2', description: 'Bota', effectiveQuantity: 2 })] }),
    ]);
    vi.mocked(atenderMaterial).mockResolvedValue(mr({ purchaseRequisitionNumber: 'SC-2026-000030' }));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Atender' }));
    const grade = screen.getByTestId('itens-atendimento');
    const luva = within(grade).getByLabelText('Entregue de Luva nitrílica');
    expect(luva).toHaveValue(10);

    await usuario.clear(luva);
    await usuario.type(luva, '4');
    await usuario.click(screen.getByRole('button', { name: 'Confirmar atendimento' }));

    await waitFor(() => expect(atenderMaterial).toHaveBeenCalledWith('mr1', [
      { itemId: 'i1', quantity: 4 }, { itemId: 'i2', quantity: 2 },
    ], { concluir: true, gerarCompra: true }));
    // o faltante vira SC, e a tela diz qual
    expect(await screen.findByText(/SC-2026-000030/)).toBeInTheDocument();
  });

  it('“Atender tudo” devolve as quantidades aprovadas', async () => {
    const usuario = userEvent.setup();
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([mr({ items: [item({ effectiveQuantity: 7 })] })]);
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Atender' }));
    const campo = within(screen.getByTestId('itens-atendimento')).getByLabelText('Entregue de Luva nitrílica');
    await usuario.clear(campo);
    expect(campo).toHaveValue(null);

    await usuario.click(screen.getByRole('button', { name: 'Atender tudo' }));
    expect(campo).toHaveValue(7);
  });

  it('desmarcar "concluir" registra a entrega e mantém a solicitação na fila', async () => {
    // o caso que a tela perdia: entregou 3 das 10 porque o resto chega na quinta
    const usuario = userEvent.setup();
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([mr({ items: [item({ effectiveQuantity: 10 })] })]);
    vi.mocked(atenderMaterial).mockResolvedValue(mr({}));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Atender' }));
    const luva = within(screen.getByTestId('itens-atendimento')).getByLabelText('Entregue de Luva nitrílica');
    await usuario.clear(luva);
    await usuario.type(luva, '3');
    await usuario.click(screen.getByTestId('atend-concluir'));

    // o botão diz o que o clique faz, e não "confirmar atendimento"
    await usuario.click(screen.getByRole('button', { name: 'Registrar entrega parcial' }));

    await waitFor(() => expect(atenderMaterial).toHaveBeenCalledWith(
      'mr1', [{ itemId: 'i1', quantity: 3 }], { concluir: false, gerarCompra: true }));
    expect(await screen.findByText(/continua na fila do estoque/)).toBeInTheDocument();
  });

  it('desmarcar "comprar o que faltou" não abre a SC do faltante', async () => {
    // o material já está a caminho: comprar de novo duplicaria a carga
    const usuario = userEvent.setup();
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([mr({ items: [item({ effectiveQuantity: 10 })] })]);
    vi.mocked(atenderMaterial).mockResolvedValue(mr({}));
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Atender' }));
    await usuario.click(screen.getByTestId('atend-compra'));
    await usuario.click(screen.getByRole('button', { name: 'Confirmar atendimento' }));

    await waitFor(() => expect(atenderMaterial).toHaveBeenCalledWith(
      'mr1', expect.anything(), { concluir: true, gerarCompra: false }));
  });

  it('a solicitação que voltou parcial mostra o que falta, e o que já saiu', async () => {
    const usuario = userEvent.setup();
    vi.mocked(filaDoAlmoxarifado).mockResolvedValue([mr({
      items: [item({ effectiveQuantity: 10, fulfilledQuantity: 6, pendingQuantity: 4 })],
    })]);
    abrir();

    await usuario.click(await screen.findByRole('button', { name: 'Atender' }));
    const grade = screen.getByTestId('itens-atendimento');

    expect(within(grade).getByText('já entregue 6')).toBeInTheDocument();
    // "Atender tudo" preenche o que falta, não o aprovado
    expect(within(grade).getByLabelText('Entregue de Luva nitrílica')).toHaveValue(4);
  });
});
