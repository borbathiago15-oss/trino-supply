import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Setor } from '@/api/setores';
import { ToastProvider } from '@/componentes/Toast';
import { codigoSugerido, Setores } from './Setores';

vi.mock('@/api/setores', async (importar) => ({
  ...(await importar<typeof import('@/api/setores')>()),
  listarSetores: vi.fn(),
  criarSetor: vi.fn(),
  atualizarSetor: vi.fn(),
}));
import { atualizarSetor, criarSetor, listarSetores } from '@/api/setores';

const setor = (p: Partial<Setor>): Setor => ({
  id: 's-' + (p.code ?? 'RH'), code: 'RH', name: 'Recursos Humanos', active: true, ...p,
});

const montar = () => render(<ToastProvider><Setores /></ToastProvider>);

describe('codigoSugerido', () => {
  // a regra é a mesma do servidor (SectorService.DoNome): divergir faria a tela
  // prometer um código e a gravação criar outro
  it('tira acento e espaço, sobe para caixa alta e corta em seis', () => {
    expect(codigoSugerido('Manutenção Predial')).toBe('MANUTE');
    expect(codigoSugerido('RH')).toBe('RH');
    expect(codigoSugerido('T.I.')).toBe('TI');
    expect(codigoSugerido('')).toBe('');
  });
});

describe('<Setores />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listarSetores).mockResolvedValue([
      setor({}),
      setor({ code: 'TI', name: 'Tecnologia', active: false }),
    ]);
    vi.mocked(criarSetor).mockResolvedValue(setor({ code: 'MANUTE', name: 'Manutenção' }));
    vi.mocked(atualizarSetor).mockResolvedValue(setor({}));
  });

  it('lista os setores com a situação de cada um', async () => {
    montar();
    const tabela = await screen.findByTestId('tabela-setores');
    expect(within(tabela).getByText('Recursos Humanos')).toBeInTheDocument();
    const inativo = tabela.querySelector('[data-setor="TI"]')!;
    expect(within(inativo as HTMLElement).getByText('INATIVO')).toBeInTheDocument();
  });

  it('cadastra sem código e deixa o servidor derivá-lo do nome', async () => {
    montar();
    await screen.findByTestId('tabela-setores');
    await userEvent.type(screen.getByLabelText(/Nome do setor/), 'Manutenção');
    await userEvent.click(screen.getByRole('button', { name: 'Cadastrar setor' }));
    await waitFor(() => expect(criarSetor).toHaveBeenCalledWith({ name: 'Manutenção', code: undefined }));
  });

  it('mostra, antes de salvar, o código que vai ser gerado', async () => {
    montar();
    await screen.findByTestId('tabela-setores');
    await userEvent.type(screen.getByLabelText(/Nome do setor/), 'Manutenção Predial');
    expect(screen.getByText(/Se ficar vazio, será MANUTE/)).toBeInTheDocument();
  });

  it('editar não oferece o código: ele é identidade e não muda', async () => {
    montar();
    const tabela = await screen.findByTestId('tabela-setores');
    const linha = tabela.querySelector('[data-setor="RH"]')!;
    await userEvent.click(within(linha as HTMLElement).getByRole('button', { name: 'Editar' }));

    expect(screen.getByLabelText(/Código/)).toBeDisabled();
    await userEvent.clear(screen.getByLabelText(/Nome do setor/));
    await userEvent.type(screen.getByLabelText(/Nome do setor/), 'RH e DP');
    await userEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }));
    await waitFor(() => expect(atualizarSetor).toHaveBeenCalledWith('s-RH', { name: 'RH e DP' }));
  });

  it('inativar é o que tira o setor de uso — nunca apagar', async () => {
    montar();
    const tabela = await screen.findByTestId('tabela-setores');
    const linha = tabela.querySelector('[data-setor="RH"]')!;
    await userEvent.click(within(linha as HTMLElement).getByRole('button', { name: 'Inativar' }));
    await waitFor(() => expect(atualizarSetor).toHaveBeenCalledWith('s-RH', { active: false }));
    expect(within(tabela).queryByRole('button', { name: /Excluir/ })).toBeNull();
  });
});
