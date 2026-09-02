import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LinhaImportacao, ResultadoImportacao } from '@/api/catalogo';
import { ToastProvider } from '@/componentes/Toast';
import { PainelImportacao, separarLinhas } from './PainelImportacao';

vi.mock('@/api/catalogo', async (importar) => ({
  ...(await importar<typeof import('@/api/catalogo')>()),
  importarPlanilha: vi.fn(),
}));
import { importarPlanilha } from '@/api/catalogo';

const linha = (p: Partial<LinhaImportacao>): LinhaImportacao =>
  ({ line: 2, code: '12003', description: 'Luva', size: null, status: 'NOVO', message: null, ...p });

const resultado = (p: Partial<ResultadoImportacao> = {}): ResultadoImportacao => ({
  fileName: 'produtos.xlsx', totalLines: 5, toCreate: 2, duplicates: 1, errors: 1, committed: false,
  warnings: [], rowsTruncated: false,
  rows: [
    linha({ line: 2 }),
    linha({ line: 3, code: '12004', description: 'Bota' }),
    linha({ line: 4, code: '12005', status: 'DUPLICADO', message: 'já existe no catálogo' }),
    linha({ line: 5, code: '', status: 'ERRO', message: 'sem código' }),
  ],
  ...p,
});

describe('separarLinhas', () => {
  it('separa o que será criado do que precisa de correção', () => {
    const { problemas, amostra } = separarLinhas(resultado().rows);
    expect(problemas.map((r) => r.line)).toEqual([4, 5]);
    expect(amostra.map((r) => r.line)).toEqual([2, 3]);
  });
});

describe('<PainelImportacao />', () => {
  beforeEach(() => vi.clearAllMocks());

  const montar = () => render(
    <ToastProvider>
      <PainelImportacao familias={['EPI', 'LIMPEZA']} tipos={[{ key: 'EPI', label: 'EPI', requiresCa: true }]}
        aoImportar={() => {}} aoFechar={() => {}} />
    </ToastProvider>,
  );

  const planilha = () => new File(['codigo;descricao'], 'produtos.xlsx');

  it('exige planilha e família antes de enviar', async () => {
    montar();
    await userEvent.click(screen.getByRole('button', { name: 'Pré-visualizar' }));
    expect(await screen.findByTestId('toast')).toHaveTextContent('Escolha a planilha primeiro.');

    await userEvent.upload(screen.getByLabelText('Planilha'), planilha());
    await userEvent.click(screen.getByRole('button', { name: 'Pré-visualizar' }));
    await waitFor(() => expect(screen.getAllByTestId('toast').at(-1)).toHaveTextContent('Escolha a família dos produtos.'));
    expect(importarPlanilha).not.toHaveBeenCalled();
  });

  it('pré-visualiza sem gravar e só então oferece confirmar', async () => {
    vi.mocked(importarPlanilha).mockResolvedValue(resultado());
    montar();
    await userEvent.upload(screen.getByLabelText('Planilha'), planilha());
    await userEvent.selectOptions(screen.getByLabelText('Família'), 'EPI');
    await userEvent.click(screen.getByRole('button', { name: 'Pré-visualizar' }));

    await waitFor(() => expect(screen.getByTestId('resultado-importacao')).toBeInTheDocument());
    expect(vi.mocked(importarPlanilha).mock.calls[0][0]).toMatchObject({ family: 'EPI', commit: false });
    expect(screen.getByTestId('resultado-importacao')).toHaveTextContent(/2 produto\(s\) a criar/);
    expect(screen.getByTestId('linhas-com-problema')).toHaveTextContent('já existe no catálogo');
    expect(screen.getByRole('button', { name: 'Confirmar importação' })).toBeInTheDocument();
  });

  it('a grade de tamanhos preenche o campo com a série escolhida', async () => {
    montar();
    await userEvent.click(screen.getByRole('button', { name: 'Letras (PP…XXG)' }));
    expect(screen.getByLabelText(/Grade de tamanhos/)).toHaveValue('PP, P, M, G, GG, XG, XXG');
    await userEvent.click(screen.getByRole('button', { name: 'Sem tamanhos' }));
    expect(screen.getByLabelText(/Grade de tamanhos/)).toHaveValue('');
  });

  it('confirmar grava e avisa quantos produtos entraram', async () => {
    vi.mocked(importarPlanilha).mockResolvedValueOnce(resultado())
      .mockResolvedValueOnce(resultado({ committed: true }));
    montar();
    await userEvent.upload(screen.getByLabelText('Planilha'), planilha());
    await userEvent.selectOptions(screen.getByLabelText('Família'), 'EPI');
    await userEvent.click(screen.getByRole('button', { name: 'Pré-visualizar' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Confirmar importação' })).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar importação' }));

    await waitFor(() => expect(vi.mocked(importarPlanilha).mock.calls[1][0]).toMatchObject({ commit: true }));
    expect(screen.getAllByTestId('toast').at(-1)).toHaveTextContent('2 produto(s) importado(s).');
    expect(screen.getByTestId('resultado-importacao')).toHaveTextContent(/2 produto\(s\) importado\(s\),/);
  });
});
