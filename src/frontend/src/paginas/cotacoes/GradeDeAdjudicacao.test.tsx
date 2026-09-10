import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ItemDoProcesso } from '@/api/cotacoes';
import { processo, proposta } from '@/test/cotacoes';
import { GradeDeAdjudicacao } from './GradeDeAdjudicacao';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  escolherVencedor: vi.fn(),
}));

import { escolherVencedor } from '@/api/cotacoes';

/**
 * O caso da divisão de quantidade: mil botas, dois fornecedores. Antes a escolha era
 * "quem leva a bota", e o lote inteiro ia para um só.
 */
const bota: ItemDoProcesso = {
  id: 'i-bota', sequence: 1, catalogItemId: null, catalogCode: null,
  description: 'Bota de segurança', quantity: 1000, unitOfMeasure: 'PAR',
  sourcePrNumber: null, family: 'EPI',
};

const comBota = () => processo({
  families: ['EPI'],
  items: [bota],
  proposals: [
    proposta({ id: 'p-alfa', supplierId: 's-alfa', supplierName: 'Alfa', totalValue: 10000,
      items: [{ quotationItemId: bota.id, unitPrice: 10, quantity: 1000 }] }),
    proposta({ id: 'p-beta', supplierId: 's-beta', supplierName: 'Beta', totalValue: 12000,
      items: [{ quotationItemId: bota.id, unitPrice: 12, quantity: 1000 }] }),
  ],
});

const abrir = () => {
  const aoConcluir = vi.fn();
  render(<GradeDeAdjudicacao processo={comBota()} lotes={null}
    aoConcluir={aoConcluir} aoAvisar={vi.fn()} />);
  return { aoConcluir };
};

describe('grade: dividir a quantidade de um item', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(escolherVencedor).mockResolvedValue(comBota());
  });

  it('sem dividir, a linha escolhe um vencedor só', async () => {
    abrir();
    expect(screen.getByLabelText('Alfa para Bota de segurança')).toBeInTheDocument();
    expect(screen.queryByLabelText(/Quantidade de Bota/)).not.toBeInTheDocument();
  });

  it('dividir troca os botões de escolha por campos de quantidade', async () => {
    abrir();
    await userEvent.click(screen.getByTestId('dividir-i-bota'));

    expect(screen.getByLabelText('Quantidade de Bota de segurança com Alfa')).toBeInTheDocument();
    expect(screen.getByLabelText('Quantidade de Bota de segurança com Beta')).toBeInTheDocument();
    expect(screen.queryByLabelText('Alfa para Bota de segurança')).not.toBeInTheDocument();
  });

  it('a soma que não fecha aparece na linha, antes de tentar enviar', async () => {
    // descobrir no erro da API que a conta não fecha é o caminho longo para somar dois números
    abrir();
    await userEvent.click(screen.getByTestId('dividir-i-bota'));
    await userEvent.type(screen.getByLabelText(/com Alfa/), '700');

    expect(screen.getByTestId('erro-divisao-i-bota')).toHaveTextContent('faltam 300');
    expect(screen.getByRole('button', { name: /Confirmar escolha/ })).toBeDisabled();
  });

  it('fechando a soma, o envio leva uma linha por fornecedor com a quantidade', async () => {
    abrir();
    await userEvent.click(screen.getByTestId('dividir-i-bota'));
    await userEvent.type(screen.getByLabelText(/com Alfa/), '700');
    await userEvent.type(screen.getByLabelText(/com Beta/), '300');
    await userEvent.type(screen.getByLabelText(/Justificativa/), 'Alfa não entrega tudo no prazo');
    await userEvent.click(screen.getByRole('button', { name: /Confirmar escolha/ }));

    await waitFor(() => expect(escolherVencedor).toHaveBeenCalled());
    const enviado = vi.mocked(escolherVencedor).mock.calls[0][1];
    expect(enviado.awards).toHaveLength(2);
    expect(enviado.awards!.map((a) => a.quantity)).toEqual([700, 300]);
  });

  it('o rodapé mostra o que cada um leva, e não o item inteiro duas vezes', async () => {
    abrir();
    await userEvent.click(screen.getByTestId('dividir-i-bota'));
    await userEvent.type(screen.getByLabelText(/com Alfa/), '700');
    await userEvent.type(screen.getByLabelText(/com Beta/), '300');

    // 700 × 10 = 7.000 e 300 × 12 = 3.600 — e não 10.000 + 12.000
    expect(screen.getByTestId('resumo-grade')).toHaveTextContent('10.600');
  });

  it('desistir da divisão devolve a linha à escolha simples', async () => {
    abrir();
    await userEvent.click(screen.getByTestId('dividir-i-bota'));
    await userEvent.type(screen.getByLabelText(/com Alfa/), '700');
    await userEvent.click(screen.getByTestId('dividir-i-bota'));

    expect(screen.getByLabelText('Alfa para Bota de segurança')).toBeInTheDocument();
    expect(screen.queryByTestId('erro-divisao-i-bota')).not.toBeInTheDocument();
  });
});
