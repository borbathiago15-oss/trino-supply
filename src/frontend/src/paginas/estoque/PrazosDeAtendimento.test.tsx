import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PrazosDeAtendimento as Dados } from '@/api/prazoDeAtendimento';
import { ToastProvider } from '@/componentes/Toast';
import { PrazosDeAtendimento } from './PrazosDeAtendimento';

vi.mock('@/api/prazoDeAtendimento', async (importar) => ({
  ...(await importar<typeof import('@/api/prazoDeAtendimento')>()),
  lerPrazosDeAtendimento: vi.fn(),
  salvarPrazosDeAtendimento: vi.fn(),
}));

import { lerPrazosDeAtendimento, salvarPrazosDeAtendimento } from '@/api/prazoDeAtendimento';

const dados = (p: Partial<Dados> = {}): Dados => ({
  warnAtPercent: 80, defaultDays: 2, canEdit: true,
  items: [
    { family: null, maxDays: 2, inherited: false, updatedAt: '', updatedByLabel: '' },
    { family: 'EPI', maxDays: 1, inherited: false, updatedAt: '2026-10-01T12:00:00Z', updatedByLabel: 'Admin' },
    { family: 'MATERIAL DE LIMPEZA', maxDays: 2, inherited: true, updatedAt: '', updatedByLabel: '' },
  ],
  ...p,
});

const abrir = () => render(<ToastProvider><PrazosDeAtendimento /></ToastProvider>);

describe('tela Prazos do Almoxarifado', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(lerPrazosDeAtendimento).mockResolvedValue(dados());
    vi.mocked(salvarPrazosDeAtendimento).mockResolvedValue({ items: [] });
  });

  it('separa o padrão das exceções por família', async () => {
    abrir();
    expect(await screen.findByLabelText(/Padrão/)).toHaveValue(2);
    expect(screen.getByText('Exceções por família')).toBeInTheDocument();
    expect(screen.getByLabelText(/EPI/)).toHaveValue(1);
  });

  it('diz o que é próprio da família e o que veio herdado do padrão', async () => {
    // sem isso o administrador não sabe se mexer ali muda uma família ou todas
    abrir();
    await screen.findByLabelText(/Padrão/);
    expect(screen.getByTestId('origem-EPI')).toHaveTextContent('prazo próprio desta família');
    expect(screen.getByTestId('origem-MATERIAL DE LIMPEZA')).toHaveTextContent('segue o padrão');
  });

  it('o padrão não oferece "voltar ao padrão": é ele que sobra', async () => {
    abrir();
    await screen.findByLabelText(/Padrão/);
    expect(screen.queryByTestId('origem-')).not.toBeInTheDocument();
  });

  it('a leitura traduz o número: a atenção chega antes do estouro', async () => {
    abrir();
    await screen.findByLabelText(/Padrão/);
    expect(screen.getByTestId('leitura-PADRAO')).toHaveTextContent('atenção a partir de 2 dia(s)');
  });

  it('zero é dito como desligado, e não como "0 dias"', async () => {
    const usuario = userEvent.setup();
    abrir();
    const campo = await screen.findByLabelText(/Padrão/);
    await usuario.clear(campo);
    await usuario.type(campo, '0');
    expect(screen.getByTestId('leitura-PADRAO')).toHaveTextContent('sem cobrança de tempo');
  });

  it('salvar manda o padrão na chave vazia, como o servidor espera', async () => {
    const usuario = userEvent.setup();
    abrir();
    const campo = await screen.findByLabelText(/^EPI/);
    await usuario.clear(campo);
    await usuario.type(campo, '3');
    await usuario.click(screen.getByRole('button', { name: 'Salvar prazos' }));

    await vi.waitFor(() => {
      // o padrão e o EPI (que já era exceção e foi editado) vão; a família **herdada e
      // intocada** fica de fora
      expect(vi.mocked(salvarPrazosDeAtendimento)).toHaveBeenCalledWith({ '': 2, EPI: 3 }, []);
    });
  });

  it('salvar sem mexer em nada não transforma herança em exceção', async () => {
    // mandar a família herdada gravaria o número do padrão como prazo próprio dela, e o
    // padrão deixaria de movê-la: abrir a tela e salvar congelaria todas as heranças
    const usuario = userEvent.setup();
    abrir();
    await screen.findByLabelText(/Padrão/);
    await usuario.click(screen.getByRole('button', { name: 'Salvar prazos' }));

    await vi.waitFor(() => {
      expect(vi.mocked(salvarPrazosDeAtendimento)).toHaveBeenCalledWith({ '': 2, EPI: 1 }, []);
    });
  });

  it('a família herdada que foi editada agora passa a ter prazo próprio', async () => {
    const usuario = userEvent.setup();
    abrir();
    const campo = await screen.findByLabelText(/MATERIAL DE LIMPEZA/);
    await usuario.clear(campo);
    await usuario.type(campo, '8');
    await usuario.click(screen.getByRole('button', { name: 'Salvar prazos' }));

    await vi.waitFor(() => {
      expect(vi.mocked(salvarPrazosDeAtendimento)).toHaveBeenCalledWith(
        { '': 2, EPI: 1, 'MATERIAL DE LIMPEZA': 8 }, []);
    });
  });

  it('"voltar ao padrão" apaga a exceção: a família não vai como número', async () => {
    // mandar o valor congelaria a cópia, e a família deixaria de acompanhar o padrão
    const usuario = userEvent.setup();
    abrir();
    await screen.findByLabelText(/Padrão/);

    await usuario.click(screen.getByRole('button', { name: 'voltar ao padrão' }));
    expect(screen.getByTestId('origem-EPI')).toHaveTextContent('voltará a seguir o padrão');

    await usuario.click(screen.getByRole('button', { name: 'Salvar prazos' }));
    await vi.waitFor(() => {
      expect(vi.mocked(salvarPrazosDeAtendimento)).toHaveBeenCalledWith({ '': 2 }, ['EPI']);
    });
  });

  it('prazo fora da faixa trava o botão em vez de deixar o servidor recusar', async () => {
    const usuario = userEvent.setup();
    abrir();
    const campo = await screen.findByLabelText(/Padrão/);
    await usuario.clear(campo);
    expect(screen.getByRole('button', { name: 'Salvar prazos' })).toBeDisabled();

    await usuario.type(campo, '400');
    expect(screen.getByRole('button', { name: 'Salvar prazos' })).toBeDisabled();

    await usuario.clear(campo);
    await usuario.type(campo, '5');
    expect(screen.getByRole('button', { name: 'Salvar prazos' })).toBeEnabled();
  });

  it('quem não edita vê a régua, sem campo editável nem botão', async () => {
    // quem trabalha na fila precisa saber contra o que a linha ficou vermelha
    vi.mocked(lerPrazosDeAtendimento).mockResolvedValue(dados({ canEdit: false }));
    abrir();
    expect(await screen.findByLabelText(/Padrão/)).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Salvar prazos' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'voltar ao padrão' })).not.toBeInTheDocument();
    expect(screen.getByText(/Só o administrador define estes prazos/)).toBeInTheDocument();
  });

  it('sem família de almoxarifado, explica em vez de mostrar grade vazia', async () => {
    vi.mocked(lerPrazosDeAtendimento).mockResolvedValue(dados({
      items: [{ family: null, maxDays: 2, inherited: false, updatedAt: '', updatedByLabel: '' }],
    }));
    abrir();
    await screen.findByLabelText(/Padrão/);
    expect(screen.queryByText('Exceções por família')).not.toBeInTheDocument();
    expect(screen.getByText(/Nenhuma família está marcada como de almoxarifado/)).toBeInTheDocument();
  });

  it('não se confunde com o prazo-meta da família, que é do processo de compra', async () => {
    abrir();
    await screen.findByLabelText(/Padrão/);
    expect(screen.getByText(/prazos-meta da família/)).toBeInTheDocument();
  });
});
