import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Chamado as ChamadoApi, ChamadoCompleto, MensagemDoChamado } from '@/api/suporte';
import { ToastProvider } from '@/componentes/Toast';
import { BarraDeAjuda } from '@/layout/BarraDeAjuda';
import { telaDaRota } from '@/manual/telas';
import { Chamado } from './Chamado';
import { DialogoDeSuporte } from './DialogoDeSuporte';
import { MeusChamados } from './MeusChamados';

vi.mock('@/api/suporte', async (importar) => ({
  ...(await importar<typeof import('@/api/suporte')>()),
  resumoDoSuporte: vi.fn(),
  listarChamados: vi.fn(),
  lerChamado: vi.fn(),
  abrirChamado: vi.fn(),
  responderChamado: vi.fn(),
  anexarAoChamado: vi.fn(),
}));
import {
  abrirChamado, anexarAoChamado, lerChamado, listarChamados, responderChamado, resumoDoSuporte,
} from '@/api/suporte';

const chamado = (p: Partial<ChamadoApi> = {}): ChamadoApi => ({
  id: 'ch-1', number: 'CH-2026-000001', status: 'AGUARDANDO_SUPORTE', category: 'DUVIDA',
  subject: 'Não consigo aprovar', screen: '/aprovacoes', screenLabel: 'Central de Aprovação',
  createdByLabel: 'Caio', createdAt: '2026-09-23T09:00:00Z', updatedAt: '2026-09-23T09:00:00Z',
  assignedToLabel: null, resolvedAt: null, resolvedByLabel: null, ...p,
});

const mensagem = (p: Partial<MensagemDoChamado> = {}): MensagemDoChamado => ({
  id: 'm-1', authorLabel: 'Caio', fromSupport: false, text: 'O botão de aprovar não aparece.',
  attachmentId: null, attachmentName: null, createdAt: '2026-09-23T09:00:00Z', ...p,
});

const completo = (p: Partial<ChamadoCompleto> = {}): ChamadoCompleto => ({
  ticket: chamado(), clientInfo: 'Chrome · janela 1920x1080', messages: [mensagem()], souDoSuporte: false, ...p,
});

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(resumoDoSuporte).mockResolvedValue({ paraMim: 0, fila: null, atende: false });
  vi.mocked(listarChamados).mockResolvedValue([]);
});

describe('<BarraDeAjuda /> — o manual e o suporte de cada tela', () => {
  const montar = (rota: string) => render(
    <ToastProvider><MemoryRouter initialEntries={[rota]}><BarraDeAjuda /></MemoryRouter></ToastProvider>);

  it('o Manual abre o manual da tela aberta, e não um genérico', async () => {
    montar('/torre');
    await userEvent.click(screen.getByRole('button', { name: /Manual/ }));
    const gaveta = screen.getByRole('dialog', { name: 'Manual — Torre de Controle' });
    expect(within(gaveta).getByText(/abrem exatamente a lista que contaram/)).toBeInTheDocument();
  });

  it('o detalhe tem o manual dele: no pedido, a linha do tempo da O.C.', async () => {
    montar('/pedidos/abc-123');
    await userEvent.click(screen.getByRole('button', { name: /Manual/ }));
    const gaveta = screen.getByRole('dialog', { name: 'Manual — Pedido de Compra' });
    expect(within(gaveta).getByText('PO-ERR-054')).toBeInTheDocument();
  });

  it('do manual, "não achou a resposta" abre o chamado já com a tela', async () => {
    montar('/cotacoes/abrir');
    await userEvent.click(screen.getByRole('button', { name: /Manual/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Abrir chamado sobre esta tela' }));
    expect(screen.getByTestId('tela-do-chamado')).toHaveTextContent('Abrir Cotação');
  });

  it('o Suporte abre o chamado da tela aberta', async () => {
    montar('/aprovacoes');
    await userEvent.click(screen.getByRole('button', { name: /Suporte/ }));
    expect(screen.getByRole('dialog', { name: 'Abrir chamado de suporte' })).toBeInTheDocument();
    expect(screen.getByTestId('tela-do-chamado')).toHaveTextContent('Central de Aprovação');
  });

  it('o número dos chamados é o que é a sua vez: resposta esperando você e fila de quem atende', async () => {
    vi.mocked(resumoDoSuporte).mockResolvedValue({ paraMim: 1, fila: 3, atende: true });
    montar('/painel');
    expect(await screen.findByTestId('chamados-pendentes')).toHaveTextContent('4');
  });

  it('sem nada esperando, não há número', async () => {
    montar('/painel');
    await waitFor(() => expect(resumoDoSuporte).toHaveBeenCalled());
    expect(screen.queryByTestId('chamados-pendentes')).not.toBeInTheDocument();
  });
});

describe('<DialogoDeSuporte />', () => {
  const montar = () => render(
    <MemoryRouter><DialogoDeSuporte tela={telaDaRota('/aprovacoes')} rota="/aprovacoes" aoFechar={() => {}} /></MemoryRouter>);

  it('só libera o envio com as mesmas réguas do servidor', async () => {
    montar();
    const abrir = screen.getByRole('button', { name: 'Abrir chamado' });
    expect(abrir).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Assunto/), 'Ajud');
    await userEvent.type(screen.getByLabelText(/O que aconteceu/), 'Não aparece o botão.');
    expect(abrir).toBeDisabled();   // 4 caracteres de assunto
    await userEvent.type(screen.getByLabelText(/Assunto/), 'a');
    expect(abrir).toBeEnabled();
  });

  it('manda a tela e o nome dela sem a pessoa digitar, e mostra o número do chamado', async () => {
    vi.mocked(abrirChamado).mockResolvedValue({ ticket: chamado(), firstMessageId: 'm-1' });
    montar();
    await userEvent.type(screen.getByLabelText(/Assunto/), 'Não consigo aprovar');
    await userEvent.type(screen.getByLabelText(/O que aconteceu/), 'O botão de aprovar não aparece.');
    await userEvent.click(screen.getByRole('button', { name: 'Abrir chamado' }));

    expect(abrirChamado).toHaveBeenCalledWith(expect.objectContaining({
      screen: '/aprovacoes', screenLabel: 'Central de Aprovação', category: 'DUVIDA', subject: 'Não consigo aprovar',
    }));
    expect(await screen.findByTestId('numero-do-chamado')).toHaveTextContent('CH-2026-000001');
  });

  it('o print vai para a primeira mensagem; se for recusado, o chamado continua aberto e isso é dito', async () => {
    vi.mocked(abrirChamado).mockResolvedValue({ ticket: chamado(), firstMessageId: 'm-1' });
    vi.mocked(anexarAoChamado).mockRejectedValue(new Error('Arquivo acima de 10 MB.'));
    montar();
    await userEvent.type(screen.getByLabelText(/Assunto/), 'Não consigo aprovar');
    await userEvent.type(screen.getByLabelText(/O que aconteceu/), 'O botão de aprovar não aparece.');
    await userEvent.upload(screen.getByLabelText(/Print ou arquivo/), new File(['x'], 'tela.png', { type: 'image/png' }));
    await userEvent.click(screen.getByRole('button', { name: 'Abrir chamado' }));

    expect(await screen.findByTestId('numero-do-chamado')).toBeInTheDocument();
    expect(anexarAoChamado).toHaveBeenCalledWith('ch-1', 'm-1', expect.any(File));
    expect(screen.getByRole('alert')).toHaveTextContent('Arquivo acima de 10 MB.');
  });

  it('a recusa do servidor aparece no diálogo, que continua aberto', async () => {
    vi.mocked(abrirChamado).mockRejectedValue(new Error('Descreva o que aconteceu em pelo menos 10 caracteres.'));
    montar();
    await userEvent.type(screen.getByLabelText(/Assunto/), 'Não consigo aprovar');
    await userEvent.type(screen.getByLabelText(/O que aconteceu/), 'aaaaaaaaaaaa');
    await userEvent.click(screen.getByRole('button', { name: 'Abrir chamado' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('pelo menos 10 caracteres');
    expect(screen.getByRole('dialog', { name: 'Abrir chamado de suporte' })).toBeInTheDocument();
  });
});

describe('<MeusChamados />', () => {
  const montar = () => render(<MemoryRouter><MeusChamados /></MemoryRouter>);

  it('quem não atende não vê a fila', async () => {
    montar();
    await waitFor(() => expect(listarChamados).toHaveBeenCalledWith('meus', undefined, expect.anything()));
    expect(screen.queryByRole('tab', { name: /Fila do suporte/ })).not.toBeInTheDocument();
  });

  it('sem chamado, diz onde se abre um', async () => {
    montar();
    expect(await screen.findByText(/use o botão "Suporte" no topo de qualquer tela/)).toBeInTheDocument();
  });

  it('quem atende vê a fila com o número e com quem abriu', async () => {
    vi.mocked(resumoDoSuporte).mockResolvedValue({ paraMim: 0, fila: 2, atende: true });
    vi.mocked(listarChamados).mockResolvedValue([chamado()]);
    montar();
    await userEvent.click(await screen.findByRole('tab', { name: 'Fila do suporte (2)' }));
    await waitFor(() => expect(listarChamados).toHaveBeenLastCalledWith('fila', undefined, expect.anything()));
    const tabela = await screen.findByTestId('tabela-chamados');
    expect(within(tabela).getByText('Caio')).toBeInTheDocument();
    expect(within(tabela).getByText('Aguardando o suporte')).toBeInTheDocument();
  });

  it('para quem abriu, a situação diz de quem é a vez', async () => {
    vi.mocked(listarChamados).mockResolvedValue([chamado({ status: 'AGUARDANDO_USUARIO' })]);
    montar();
    expect(await screen.findByText('Aguardando você')).toBeInTheDocument();
  });
});

describe('<Chamado />', () => {
  const montar = () => render(
    <ToastProvider>
      <MemoryRouter initialEntries={['/suporte/ch-1']}>
        <Routes><Route path="/suporte/:id" element={<Chamado />} /></Routes>
      </MemoryRouter>
    </ToastProvider>);

  it('quem abriu vê de quem é a vez e pode encerrar sem escrever', async () => {
    vi.mocked(lerChamado).mockResolvedValue(completo({ ticket: chamado({ status: 'AGUARDANDO_USUARIO' }) }));
    vi.mocked(responderChamado).mockResolvedValue({ ticket: chamado({ status: 'RESOLVIDO' }), message: null });
    montar();
    expect(await screen.findByTestId('a-vez-de')).toHaveTextContent('O suporte respondeu — a vez é sua.');
    await userEvent.click(screen.getByRole('button', { name: 'Encerrar chamado' }));
    expect(responderChamado).toHaveBeenCalledWith('ch-1', { text: undefined, resolve: true });
  });

  it('o suporte só resolve dizendo o que foi feito (CH-ERR-021)', async () => {
    vi.mocked(lerChamado).mockResolvedValue(completo({ souDoSuporte: true }));
    montar();
    const resolver = await screen.findByRole('button', { name: 'Responder e resolver' });
    expect(resolver).toBeDisabled();
    await userEvent.type(screen.getByLabelText('Responder'), 'Liberei o módulo.');
    expect(resolver).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Encerrar chamado' })).not.toBeInTheDocument();
  });

  it('o navegador de quem abriu aparece só para o suporte', async () => {
    vi.mocked(lerChamado).mockResolvedValue(completo({ souDoSuporte: true }));
    const { unmount } = montar();
    expect(await screen.findByText(/janela 1920x1080/)).toBeInTheDocument();
    unmount();
    vi.mocked(lerChamado).mockResolvedValue(completo());
    montar();
    await screen.findByTestId('conversa');
    expect(screen.queryByText(/janela 1920x1080/)).not.toBeInTheDocument();
  });

  it('a tela de origem é um link, para quem atende ir direto ao lugar', async () => {
    vi.mocked(lerChamado).mockResolvedValue(completo());
    montar();
    expect(await screen.findByRole('link', { name: 'Central de Aprovação' })).toHaveAttribute('href', '/aprovacoes');
  });

  it('responder o resolvido diz que reabre', async () => {
    vi.mocked(lerChamado).mockResolvedValue(completo({
      ticket: chamado({ status: 'RESOLVIDO', resolvedAt: '2026-09-23T10:00:00Z', resolvedByLabel: 'Beto' }),
    }));
    montar();
    expect(await screen.findByLabelText('Responder (reabre o chamado)')).toBeInTheDocument();
    expect(screen.getByTestId('a-vez-de')).toHaveTextContent('Resolvido por Beto');
  });
});
