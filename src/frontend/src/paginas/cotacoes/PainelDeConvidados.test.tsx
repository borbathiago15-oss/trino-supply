import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { situacaoDoConvite } from '@/api/cotacoes';
import { ToastProvider } from '@/componentes/Toast';
import { convidado, processo } from '@/test/cotacoes';
import { PainelDeConvidados } from './PainelDeConvidados';

vi.mock('@/api/cotacoes', async (importar) => ({
  ...(await importar<typeof import('@/api/cotacoes')>()),
  convidarFornecedor: vi.fn(),
  prorrogarConvite: vi.fn(),
  dispensarConvite: vi.fn(),
}));
vi.mock('@/api/fornecedores', async (importar) => ({
  ...(await importar<typeof import('@/api/fornecedores')>()),
  listarFornecedores: vi.fn(),
  criarFornecedor: vi.fn(),
}));

import { convidarFornecedor, dispensarConvite, prorrogarConvite } from '@/api/cotacoes';
import { listarFornecedores } from '@/api/fornecedores';

const abrir = (suppliers = [convidado({})], podeConvidar = true) => {
  const aoConvidar = vi.fn();
  render(
    <ToastProvider>
      <PainelDeConvidados processo={processo({ suppliers })} podeConvidar={podeConvidar}
        aoConvidar={aoConvidar} aoAvisar={vi.fn()} />
    </ToastProvider>,
  );
  return { aoConvidar };
};

describe('situação do convite em uma frase', () => {
  it('proposta recebida vence qualquer prazo vencido', () => {
    expect(situacaoDoConvite(convidado({ hasProposal: true, late: true, daysLate: 9 })).rotulo)
      .toBe('RECEBIDA');
  });

  it('atrasado diz há quantos dias, no singular e no plural', () => {
    expect(situacaoDoConvite(convidado({ late: true, daysLate: 1 })).rotulo).toBe('ATRASADA · 1 dia');
    expect(situacaoDoConvite(convidado({ late: true, daysLate: 3 })).rotulo).toBe('ATRASADA · 3 dias');
  });

  it('dispensado não é atraso: a decisão já foi tomada', () => {
    expect(situacaoDoConvite(convidado({ waived: true, late: false })).rotulo).toBe('SEGUIU SEM ELE');
  });

  it('sem prazo e sem proposta é aguardando, não atrasado', () => {
    expect(situacaoDoConvite(convidado({ responseDeadline: null, daysLate: null })).rotulo)
      .toBe('AGUARDANDO');
  });
});

describe('painel de convidados', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(listarFornecedores).mockResolvedValue([]);
    vi.mocked(convidarFornecedor).mockResolvedValue(processo({}));
    vi.mocked(prorrogarConvite).mockResolvedValue(processo({}));
    vi.mocked(dispensarConvite).mockResolvedValue(processo({}));
  });

  it('a linha mostra o prazo de resposta e quantas vezes ele foi esticado', async () => {
    abrir([convidado({ responseDeadline: '2026-10-05', extensions: 2 })]);
    const tabela = await screen.findByTestId('fornecedores-convidados');
    expect(within(tabela).getByText('05/10/2026')).toBeInTheDocument();
    expect(within(tabela).getByText('prorrogado 2×')).toBeInTheDocument();
  });

  it('convite atrasado avisa no topo e diz o que fazer', async () => {
    abrir([convidado({ late: true, daysLate: 4 })]);
    const aviso = await screen.findByTestId('convites-atrasados');
    expect(aviso).toHaveTextContent('Alfa EPIs passou do prazo');
    expect(aviso).toHaveTextContent('novo prazo');
  });

  it('sem ninguém atrasado, não há aviso — alarme que grita sempre para de ser lido', async () => {
    abrir([convidado({})]);
    await screen.findByTestId('fornecedores-convidados');
    expect(screen.queryByTestId('convites-atrasados')).not.toBeInTheDocument();
  });

  it('convidar leva o prazo digitado junto', async () => {
    vi.mocked(listarFornecedores).mockResolvedValue([
      { id: 's2', legalName: 'Beta LTDA', tradeName: 'Beta' },
    ] as never);
    abrir([]);
    await userEvent.selectOptions(await screen.findByLabelText(/Convidar fornecedor/), 's2');
    await userEvent.type(screen.getByLabelText(/Prazo para responder/), '2026-10-15');
    await userEvent.click(screen.getByRole('button', { name: 'Convidar' }));

    await waitFor(() => expect(convidarFornecedor).toHaveBeenCalledWith('q1', ['s2'], '2026-10-15'));
  });

  it('dar novo prazo manda a data nova só para aquele fornecedor', async () => {
    const { aoConvidar } = abrir([convidado({ late: true, daysLate: 4 })]);
    await userEvent.click(await screen.findByTestId('novo-prazo-s1'));
    await userEvent.type(screen.getByLabelText(/Novo prazo para Alfa EPIs/), '2026-10-20');
    await userEvent.click(screen.getByRole('button', { name: 'Dar novo prazo' }));

    await waitFor(() => expect(prorrogarConvite).toHaveBeenCalledWith('q1', 's1', '2026-10-20'));
    await waitFor(() => expect(aoConvidar).toHaveBeenCalled());
  });

  it('seguir sem o fornecedor exige o motivo', async () => {
    abrir([convidado({ late: true, daysLate: 4 })]);
    await userEvent.click(await screen.findByTestId('seguir-sem-s1'));
    // o botão do diálogo nomeia o fornecedor: dois "Seguir sem ele" na mesma tela
    // deixariam quem confirma sem saber de quem se está falando
    const confirmar = screen.getByRole('button', { name: 'Seguir sem Alfa EPIs' });
    expect(confirmar).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/Por que o processo segue sem/), 'Nao respondeu tres cobrancas');
    await userEvent.click(confirmar);

    await waitFor(() => expect(dispensarConvite).toHaveBeenCalledWith(
      'q1', 's1', 'Nao respondeu tres cobrancas'));
  });

  it('quem já respondeu não tem as duas saídas: não há o que cobrar dele', async () => {
    abrir([convidado({ hasProposal: true })]);
    await screen.findByTestId('fornecedores-convidados');
    expect(screen.queryByTestId('novo-prazo-s1')).not.toBeInTheDocument();
    expect(screen.queryByTestId('seguir-sem-s1')).not.toBeInTheDocument();
  });

  it('o dispensado mostra o motivo e sai da cobrança, mas continua na lista', async () => {
    abrir([convidado({ waived: true, waivedReason: 'Avisou que nao vai cotar' })]);
    const tabela = await screen.findByTestId('fornecedores-convidados');
    expect(within(tabela).getByText('Alfa EPIs')).toBeInTheDocument();
    expect(within(tabela).getByText('Avisou que nao vai cotar')).toBeInTheDocument();
    expect(screen.queryByTestId('seguir-sem-s1')).not.toBeInTheDocument();
  });

  it('quem não conduz o processo vê a lista e não vê as ações', async () => {
    abrir([convidado({ late: true, daysLate: 4 })], false);
    await screen.findByTestId('fornecedores-convidados');
    expect(screen.queryByTestId('novo-prazo-s1')).not.toBeInTheDocument();
    expect(screen.getByTestId('convites-atrasados')).toBeInTheDocument();
  });
});
