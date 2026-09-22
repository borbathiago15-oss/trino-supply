import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Usuario } from '@/api/auth';
import type { CentroCusto } from '@/api/centrosCusto';
import type { UsuarioCadastro } from '@/api/usuarios';
import { ToastProvider } from '@/componentes/Toast';
import { diretoresPossiveis, gestoresPossiveis, impedimentoDeInativar, pedeGestorResponsavel, papeisOferecidos, Usuarios } from './Usuarios';

vi.mock('@/api/usuarios', async (importar) => ({
  ...(await importar<typeof import('@/api/usuarios')>()),
  listarUsuarios: vi.fn(),
  criarUsuario: vi.fn(),
  atualizarUsuario: vi.fn(),
  redefinirSenha: vi.fn(),
}));
vi.mock('@/api/centrosCusto', () => ({ listarCentrosCusto: vi.fn() }));
vi.mock('@/api/setores', () => ({ listarSetores: vi.fn() }));
vi.mock('@/sessao/SessaoProvider', () => ({ useUsuario: () => admin }));

import { atualizarUsuario, criarUsuario, listarUsuarios, redefinirSenha } from '@/api/usuarios';
import { listarCentrosCusto } from '@/api/centrosCusto';
import { listarSetores } from '@/api/setores';

const admin: Usuario = { id: 'u-admin', email: 'admin@t.com', name: 'Administrador', role: 'SystemAdministrator', modules: [] };

const usuario = (p: Partial<UsuarioCadastro>): UsuarioCadastro => {
  const email = p.email ?? 'ana@t.com';
  return {
    id: 'u-' + email, email, name: 'Ana Solicitante', role: 'Requester',
    active: true, modules: ['SOLICITACOES', 'MATERIAL'], customModules: false, costCenters: ['BAH-001'],
    directorId: null, supplyManagerId: null, sectorId: null, mustChangePassword: false, passwordChangedAt: '2026-01-02T00:00:00Z',
    createdAt: '2026-01-01T00:00:00Z', updatedAt: null, ...p,
  };
};

const diretor = usuario({ email: 'davi@t.com', name: 'Davi Diretor', role: 'Director', modules: ['APROVACAO'], costCenters: [] });
const inativo = usuario({ email: 'joao@t.com', name: 'João Inativo', active: false, costCenters: [] });

const centros: CentroCusto[] = [{
  id: 'cc1', code: 'BAH-001', name: 'PepsiCo Simões Filho', region: 'BAHIA', companyId: null,
  managerUserId: null, managerName: null, clientName: null, active: true,
  receivesMaterial: false, level1ValueLimit: null, level2ValueLimit: null, level1: [], level2: [],
}];

describe('regras da tela de usuários', () => {
  it('só diretoria e administração podem ser diretor responsável', () => {
    const nomes = diretoresPossiveis([usuario({}), diretor, inativo]).map((u) => u.name);
    expect(nomes).toEqual(['Davi Diretor']);
  });
  it('papéis resolvidos por módulo saem da lista de escolha', () => {
    expect(papeisOferecidos(['Requester', 'WarehouseOperator', 'SupplyManager', 'Director']))
      .toEqual(['Requester', 'Director']);
  });

    it('só o Gestor de Suprimentos ativo pode ser gestor responsável', () => {
    const gestor = usuario({ email: 'gu@t.com', name: 'Gustavo Gestor', role: 'SupplyManager' });
    const inativoGestor = usuario({ email: 'gi@t.com', name: 'Gina Inativa', role: 'SupplyManager', active: false });
    const nomes = gestoresPossiveis([usuario({}), diretor, gestor, inativoGestor]).map((u) => u.name);
    // diretor não entra: o servidor recusaria (IAM-ERR-020) e a tela não propõe o que não grava
    expect(nomes).toEqual(['Gustavo Gestor']);
  });

  it('o vínculo do gestor só é pedido ao comprador', () => {
    expect(pedeGestorResponsavel('PurchasingOfficer')).toBe(true);
    expect(pedeGestorResponsavel('Requester')).toBe(false);
    expect(pedeGestorResponsavel('SupplyManager')).toBe(false);
    expect(pedeGestorResponsavel('')).toBe(false);
  });

  describe('quem não pode ser inativado', () => {
    const ana = usuario({});
    const adm = usuario({ email: 'adm@t.com', name: 'Adm', role: 'SystemAdministrator' });
    const adm2 = usuario({ email: 'adm2@t.com', name: 'Outro Adm', role: 'SystemAdministrator' });

    it('ninguém inativa o próprio usuário (IAM-ERR-016)', () => {
      expect(impedimentoDeInativar(ana, ana.id, [ana, adm])).toContain('IAM-ERR-016');
    });
    it('o único administrador ativo fica (IAM-ERR-015)', () => {
      expect(impedimentoDeInativar(adm, 'u-outro', [ana, adm])).toContain('IAM-ERR-015');
    });
    it('com outro administrador ativo, a trava sai', () => {
      expect(impedimentoDeInativar(adm, 'u-outro', [ana, adm, adm2])).toBeNull();
    });
    it('administrador já inativo não conta como o último ativo', () => {
      const inativoAdm = usuario({ email: 'x@t.com', role: 'SystemAdministrator', active: false });
      expect(impedimentoDeInativar(adm, 'u-outro', [adm, inativoAdm])).toContain('IAM-ERR-015');
    });
    it('usuário comum não é barrado', () => {
      expect(impedimentoDeInativar(ana, 'u-outro', [ana, adm])).toBeNull();
    });
  });
});

describe('<Usuarios />', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listarUsuarios).mockResolvedValue({
      items: [usuario({ directorId: 'u-davi@t.com' }), diretor, inativo],
      roles: ['Requester', 'Approver', 'Director', 'SystemAdministrator', 'WarehouseOperator'],
      availableModules: ['SOLICITACOES', 'MATERIAL'],
    });
    vi.mocked(listarCentrosCusto).mockResolvedValue(centros);
    vi.mocked(listarSetores).mockResolvedValue([
      { id: 's-rh', code: 'RH', name: 'Recursos Humanos', active: true },
      { id: 's-ti', code: 'TI', name: 'Tecnologia', active: true },
    ]);
  });

  const montar = () => render(<ToastProvider><Usuarios /></ToastProvider>);

  // o setor é de qualquer papel: diz onde a pessoa trabalha, não o que ela aprova —
  // por isso o campo aparece sem depender do papel escolhido, ao contrário do gestor
  it('vincula o setor em que a pessoa trabalha, seja qual for o papel', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-usuarios')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText(/^Nome/), 'Rita Requisitante');
    await userEvent.type(screen.getByLabelText(/E-mail/), 'rita@t.com');
    await userEvent.selectOptions(screen.getByLabelText(/Papel/), 'Requester');
    await userEvent.type(screen.getByLabelText(/Senha/), 'Bandeira#Azul47');
    await userEvent.selectOptions(screen.getByLabelText(/^Setor/), 's-ti');
    await userEvent.click(screen.getByRole('button', { name: 'Criar usuário' }));

    await waitFor(() => expect(criarUsuario).toHaveBeenCalledWith(
      expect.objectContaining({ sectorId: 's-ti' })));
  });

  it('lista com autorizações legíveis, vínculos e diretor pelo nome', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-usuarios')).toBeInTheDocument());
    const tabela = within(screen.getByTestId('tabela-usuarios'));
    expect(tabela.getAllByText('Solicitações de Compra · Solicitação de Material')).toHaveLength(2);
    expect(tabela.getByText('Diretor: Davi Diretor')).toBeInTheDocument();
    expect(tabela.getByText('INATIVO')).toBeInTheDocument();
  });

  it('escolher o papel sugere as autorizações daquele papel', async () => {
    montar();
    await waitFor(() => expect(screen.getByLabelText('Papel')).toBeInTheDocument());
    await userEvent.selectOptions(screen.getByLabelText('Papel'), 'Approver');
    expect(screen.getByLabelText('Central de Aprovação')).toBeChecked();
    expect(screen.getByLabelText('Compras')).not.toBeChecked();
  });

  it('editar trava e-mail e senha, que têm caminho próprio', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-usuarios')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: 'Editar' })[0]);
    expect(screen.getByLabelText('E-mail')).toBeDisabled();
    expect(screen.getByLabelText(/Senha provisória/)).toBeDisabled();
    expect(screen.getByLabelText('BAH-001 — PepsiCo Simões Filho')).toBeChecked();
  });

  it('cria usuário com autorizações e vínculos marcados', async () => {
    vi.mocked(criarUsuario).mockResolvedValue(usuario({}));
    montar();
    await waitFor(() => expect(screen.getByLabelText('Papel')).toBeInTheDocument());
    await userEvent.type(screen.getByLabelText('Nome'), 'Nova Pessoa');
    await userEvent.type(screen.getByLabelText('E-mail'), 'nova@t.com');
    await userEvent.selectOptions(screen.getByLabelText('Papel'), 'Requester');
    await userEvent.type(screen.getByLabelText(/Senha provisória/), 'senhaSegura123');
    await userEvent.click(screen.getByLabelText('BAH-001 — PepsiCo Simões Filho'));
    await userEvent.click(screen.getByRole('button', { name: 'Criar usuário' }));

    await waitFor(() => expect(criarUsuario).toHaveBeenCalledWith(expect.objectContaining({
      name: 'Nova Pessoa', email: 'nova@t.com', role: 'Requester', password: 'senhaSegura123',
      costCenters: ['BAH-001'], modules: ['SOLICITACOES', 'MATERIAL'], directorId: null,
    })));
  });

  it('inativar pede confirmação, avisando que as sessões caem', async () => {
    vi.mocked(atualizarUsuario).mockResolvedValue(usuario({}));
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-usuarios')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: /Mais ações de/ })[0]);
    await userEvent.click(screen.getByRole('menuitem', { name: 'Inativar' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('As sessões dele serão encerradas.');
    expect(atualizarUsuario).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Inativar' }));
    await waitFor(() => expect(atualizarUsuario).toHaveBeenCalledWith('u-ana@t.com', { active: false }));
  });

  it('nova senha exige o tamanho mínimo antes de chamar a API', async () => {
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-usuarios')).toBeInTheDocument());
    await userEvent.click(screen.getAllByRole('button', { name: /Mais ações de/ })[0]);
    await userEvent.click(screen.getByRole('menuitem', { name: 'Nova senha' }));
    const campoSenha = document.getElementById('usu-nova-senha')!;
    await userEvent.type(campoSenha, 'curta');
    await userEvent.click(screen.getByRole('button', { name: 'Redefinir senha' }));
    expect(await screen.findByTestId('toast')).toHaveTextContent('pelo menos 12 caracteres');
    expect(redefinirSenha).not.toHaveBeenCalled();

    await userEvent.type(campoSenha, 'MaisQueDozeCaracteres1');
    await userEvent.click(screen.getByRole('button', { name: 'Redefinir senha' }));
    await waitFor(() => expect(redefinirSenha).toHaveBeenCalledWith('u-ana@t.com', 'curtaMaisQueDozeCaracteres1'));
  });

  it('inativar a si mesmo e o único administrador aparece barrado, com o motivo', async () => {
    // as duas regras existiam só no servidor: o administrador clicava e tomava o erro
    vi.mocked(listarUsuarios).mockResolvedValue({
      items: [usuario({ id: 'u-admin', email: 'admin@t.com', name: 'Administrador', role: 'SystemAdministrator' })],
      roles: ['Requester', 'SystemAdministrator'],
      availableModules: [],
    });
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-usuarios')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: /Mais ações de/ }));

    const inativar = screen.getByRole('menuitem', { name: /Inativar/ });
    expect(inativar).toBeDisabled();
    // é o próprio usuário logado: a regra que aparece primeiro é a do IAM-ERR-016
    expect(inativar).toHaveTextContent('IAM-ERR-016');
    await userEvent.click(inativar);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(atualizarUsuario).not.toHaveBeenCalled();
  });

  it('editar o único administrador avisa que o papel dele não muda', async () => {
    vi.mocked(listarUsuarios).mockResolvedValue({
      items: [usuario({ id: 'u-adm', email: 'adm@t.com', name: 'Adm', role: 'SystemAdministrator' })],
      roles: ['Requester', 'SystemAdministrator'],
      availableModules: [],
    });
    montar();
    await waitFor(() => expect(screen.getByTestId('tabela-usuarios')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Editar' }));
    expect(screen.getByText(/trocar o papel dele é recusado/)).toHaveTextContent('IAM-ERR-015');
  });

  it('lista vazia diz que não há usuário, em vez de mostrar um painel mudo', async () => {
    // era a única tela sem o estado vazio: quem chegava numa lista sem resultado
    // não sabia se estava carregando, quebrado ou realmente vazio
    vi.mocked(listarUsuarios).mockResolvedValue({ items: [], roles: [], availableModules: [] });
    montar();
    await waitFor(() => expect(screen.getByText(/Nenhum usuário cadastrado ainda/)).toBeInTheDocument());
  });
});
