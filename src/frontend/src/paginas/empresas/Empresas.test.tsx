import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Empresa } from '@/api/empresas';
import { formatarCnpj } from '@/api/empresas';
import { ToastProvider } from '@/componentes/Toast';
import { Empresas } from './Empresas';

vi.mock('@/api/empresas', async (importar) => ({
  ...(await importar<typeof import('@/api/empresas')>()),
  listarEmpresas: vi.fn(),
  criarEmpresa: vi.fn(),
  atualizarEmpresa: vi.fn(),
  perfilDaEmpresa: vi.fn(),
  salvarPerfilDaEmpresa: vi.fn(),
}));
import { atualizarEmpresa, criarEmpresa, listarEmpresas, perfilDaEmpresa, salvarPerfilDaEmpresa } from '@/api/empresas';

const empresa = (p: Partial<Empresa>): Empresa => ({
  id: 'e-' + (p.taxId ?? '11222333000144'), legalName: 'Trino Serviços Industriais LTDA', taxId: '11222333000144',
  stateRegistration: '123456', address: 'Rua A, 100', district: 'Centro', city: 'Vitória', state: 'ES',
  zip: '29000-000', phone: '27 3200-1000', email: 'compras@trino.com.br', active: true, ...p,
});

describe('formatarCnpj', () => {
  it('formata 14 dígitos e devolve o resto como veio', () => {
    expect(formatarCnpj('11222333000144')).toBe('11.222.333/0001-44');
    expect(formatarCnpj('123')).toBe('123');
    expect(formatarCnpj(null)).toBe('');
  });
});

describe('<Empresas />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listarEmpresas).mockResolvedValue([
      empresa({}),
      empresa({ taxId: '99888777000166', legalName: 'Trino Logística LTDA', active: false, city: 'Serra' }),
    ]);
    vi.mocked(perfilDaEmpresa).mockResolvedValue({
      legalName: 'Trino Serviços Industriais LTDA', taxId: '11222333000144', city: 'Vitória', state: 'ES',
      address: 'Rua A, 100', zip: '29000-000', standardClauses: 'Cláusula padrão vigente',
    });
  });

  const montar = () => render(<ToastProvider><Empresas /></ToastProvider>);

  it('lista os CNPJs formatados, com cidade e situação', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-empresas')).toBeInTheDocument());
    expect(screen.getByText('11.222.333/0001-44')).toBeInTheDocument();
    expect(screen.getByText('Vitória/ES')).toBeInTheDocument();
    expect(screen.getByText('INATIVA')).toBeInTheDocument();
    // a lista inclui inativas: é o cadastro, não o picker
    expect(vi.mocked(listarEmpresas).mock.calls[0][0]).toBe(true);
  });

  it('editar trava o CNPJ, que é a identidade da empresa', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-empresas')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: 'Editar' })[0]);
    const formulario = within(document.getElementById('form-empresa')!);
    expect(formulario.getByLabelText('CNPJ')).toBeDisabled();
    expect(formulario.getByLabelText('CNPJ')).toHaveValue('11.222.333/0001-44');
    expect(formulario.getByLabelText('Cidade')).toHaveValue('Vitória');
  });

  it('cadastra um CNPJ novo com os campos obrigatórios', async () => {
    vi.mocked(criarEmpresa).mockResolvedValue(empresa({}));
    montar();
    await waitFor(() => expect(document.getElementById('form-empresa')).toBeInTheDocument());
    const formulario = within(document.getElementById('form-empresa')!);
    await userEvent.type(formulario.getByLabelText('Razão social'), 'Trino Nova LTDA');
    await userEvent.type(formulario.getByLabelText('CNPJ'), '55666777000188');
    await userEvent.type(formulario.getByLabelText('Endereço'), 'Av. B, 200');
    await userEvent.type(formulario.getByLabelText('Cidade'), 'Serra');
    await userEvent.type(formulario.getByLabelText('UF'), 'ES');
    await userEvent.type(formulario.getByLabelText('CEP'), '29160-000');
    await userEvent.click(screen.getByRole('button', { name: 'Cadastrar CNPJ' }));

    await waitFor(() => expect(criarEmpresa).toHaveBeenCalledWith(expect.objectContaining({
      legalName: 'Trino Nova LTDA', taxId: '55666777000188', city: 'Serra', state: 'ES',
    })));
  });

  it('inativar manda apenas a mudança de situação', async () => {
    vi.mocked(atualizarEmpresa).mockResolvedValue(empresa({}));
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-empresas')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Inativar' }));
    await waitFor(() => expect(atualizarEmpresa).toHaveBeenCalledWith('e-11222333000144', { active: false }));
  });

  it('o padrão da O.C. carrega o que já está salvo e devolve o formulário inteiro', async () => {
    montar();
    await waitFor(() => expect(screen.getByLabelText('Cláusulas padrão da O.C.')).toHaveValue('Cláusula padrão vigente'));
    await userEvent.type(screen.getByLabelText(/Política de pagamento/), 'Pagamento em 28 dias.');
    await userEvent.click(screen.getByRole('button', { name: 'Salvar dados da empresa' }));

    await waitFor(() => expect(salvarPerfilDaEmpresa).toHaveBeenCalledWith(expect.objectContaining({
      legalName: 'Trino Serviços Industriais LTDA', paymentPolicy: 'Pagamento em 28 dias.',
      standardClauses: 'Cláusula padrão vigente',
    })));
  });
});
