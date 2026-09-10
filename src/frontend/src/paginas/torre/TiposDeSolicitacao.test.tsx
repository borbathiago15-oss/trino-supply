import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { codigoDoTipo, rotuloDoTipo, type TipoDeSolicitacao } from '@/api/tiposDeSolicitacao';
import { ToastProvider } from '@/componentes/Toast';
import { TiposDeSolicitacao } from './TiposDeSolicitacao';

vi.mock('@/api/tiposDeSolicitacao', async (importar) => ({
  ...(await importar<typeof import('@/api/tiposDeSolicitacao')>()),
  listarTiposDeSolicitacao: vi.fn(),
  criarTipoDeSolicitacao: vi.fn(),
  atualizarTipoDeSolicitacao: vi.fn(),
}));

import {
  atualizarTipoDeSolicitacao, criarTipoDeSolicitacao, listarTiposDeSolicitacao,
} from '@/api/tiposDeSolicitacao';

const tipo = (p: Partial<TipoDeSolicitacao> = {}): TipoDeSolicitacao => ({
  id: 't1', code: 'EMERGENCIAL', name: 'Emergencial',
  description: 'parada de linha', active: true, ...p,
});

const abrir = () => render(<ToastProvider><TiposDeSolicitacao /></ToastProvider>);

describe('código do tipo', () => {
  it('vira caixa alta sem espaço nas pontas — é a identidade que fica na SC', () => {
    expect(codigoDoTipo('  emergencial ')).toBe('EMERGENCIAL');
  });

  it('não passa do tamanho que o banco aceita', () => {
    expect(codigoDoTipo('x'.repeat(80))).toHaveLength(60);
  });
});

describe('rótulo do tipo de uma SC', () => {
  it('mostra o nome cadastrado', () => {
    expect(rotuloDoTipo('EMERGENCIAL', [tipo()])).toBe('Emergencial');
  });

  it('SC antiga com texto fora do cadastro mostra o próprio texto', () => {
    // é o que ela de fato guardou; trocar por "—" esconderia a informação que existe
    expect(rotuloDoTipo('LEGADO', [tipo()])).toBe('LEGADO');
  });

  it('sem tipo é traço, e não o primeiro da lista', () => {
    expect(rotuloDoTipo(null, [tipo()])).toBe('—');
  });
});

describe('tela dos tipos de solicitação', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(criarTipoDeSolicitacao).mockResolvedValue(tipo());
    vi.mocked(atualizarTipoDeSolicitacao).mockResolvedValue(tipo());
  });

  it('lista os tipos com o código, o nome e quando usar', async () => {
    vi.mocked(listarTiposDeSolicitacao).mockResolvedValue({ canMaintain: true, items: [tipo()] });
    abrir();
    const tabela = await screen.findByTestId('tabela-tipos-solicitacao');
    expect(within(tabela).getByText('EMERGENCIAL')).toBeInTheDocument();
    expect(within(tabela).getByText('parada de linha')).toBeInTheDocument();
  });

  it('cadastrar manda o código já normalizado', async () => {
    vi.mocked(listarTiposDeSolicitacao).mockResolvedValue({ canMaintain: true, items: [] });
    abrir();
    await userEvent.type(await screen.findByLabelText(/Código/), 'projeto');
    await userEvent.type(screen.getByLabelText(/^Nome/), 'Projeto');
    await userEvent.click(screen.getByRole('button', { name: 'Cadastrar tipo' }));

    await waitFor(() => expect(criarTipoDeSolicitacao).toHaveBeenCalledWith(
      { code: 'PROJETO', name: 'Projeto', description: null }));
  });

  it('editar não deixa mexer no código — ele é a identidade gravada na SC', async () => {
    vi.mocked(listarTiposDeSolicitacao).mockResolvedValue({ canMaintain: true, items: [tipo()] });
    abrir();
    await userEvent.click(await screen.findByRole('button', { name: 'Editar' }));

    expect(screen.getByLabelText(/Código/)).toBeDisabled();
    expect(screen.getByLabelText(/Código/)).toHaveValue('EMERGENCIAL');
  });

  it('inativar mantém o tipo — apagar quebraria as SCs que já o escolheram', async () => {
    vi.mocked(listarTiposDeSolicitacao).mockResolvedValue({ canMaintain: true, items: [tipo()] });
    abrir();
    await userEvent.click(await screen.findByRole('button', { name: 'Inativar' }));

    await waitFor(() => expect(atualizarTipoDeSolicitacao).toHaveBeenCalledWith('t1', { active: false }));
  });

  it('sem tipo cadastrado, a tela diz o que isso significa', async () => {
    vi.mocked(listarTiposDeSolicitacao).mockResolvedValue({ canMaintain: true, items: [] });
    abrir();
    expect(await screen.findByText(/toda SC usa o conjunto de prazos padrão/)).toBeInTheDocument();
  });

  it('quem não mantém vê a lista e não vê o formulário', async () => {
    vi.mocked(listarTiposDeSolicitacao).mockResolvedValue({ canMaintain: false, items: [tipo()] });
    abrir();
    await screen.findByTestId('tabela-tipos-solicitacao');
    expect(screen.queryByTestId('form-tipo-solicitacao')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
  });
});
