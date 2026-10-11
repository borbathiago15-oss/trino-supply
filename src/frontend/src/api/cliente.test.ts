import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ErroApi } from './cliente';
import { EVENTO_SESSAO_EXPIRADA, sessao } from './sessao';

const resposta = (status: number, corpo: unknown) =>
  new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } });

describe('api()', () => {
  const fetchMock = vi.fn<typeof fetch>();
  beforeEach(() => { vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); });
  afterEach(() => { vi.unstubAllGlobals(); });

  it('devolve o `data` do envelope e manda o Bearer da sessão', async () => {
    sessao.set({ accessToken: 'abc' });
    fetchMock.mockResolvedValueOnce(resposta(200, { data: { items: [1, 2] }, correlationId: 'x' }));
    const r = await api<{ items: number[] }>('/api/v1/purchase-orders/');
    expect(r.items).toEqual([1, 2]);
    const [, init] = fetchMock.mock.calls[0];
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer abc');
    // a tranca anti-CSRF do cookie vai em toda chamada
    expect((init?.headers as Record<string, string>)['X-Requested-With']).toBe('TrinoSupply');
  });

  it('em 401 renova o token uma vez, pelo cookie, e repete a chamada', async () => {
    sessao.set({ accessToken: 'velho' });
    fetchMock
      .mockResolvedValueOnce(resposta(401, { error: { code: 'AUTH', message: 'expirou' } }))
      .mockResolvedValueOnce(resposta(200, { data: { accessToken: 'novo', refreshToken: null } }))
      .mockResolvedValueOnce(resposta(200, { data: { ok: true } }));
    const r = await api<{ ok: boolean }>('/api/v1/purchase-orders/');
    expect(r.ok).toBe(true);
    expect(sessao.access).toBe('novo');
    const [url, init] = fetchMock.mock.calls[1];
    expect(url).toBe('/api/v1/auth/refresh');
    // o refresh token não vai no corpo: vai no cookie, com a tranca do cabeçalho
    expect(init?.body).toBe('{}');
    expect(init?.credentials).toBe('same-origin');
    expect((init?.headers as Record<string, string>)['X-Requested-With']).toBe('TrinoSupply');
    expect((fetchMock.mock.calls[2][1]?.headers as Record<string, string>).Authorization).toBe('Bearer novo');
  });

  it('três chamadas simultâneas em 401 renovam uma vez só e todas seguem', async () => {
    sessao.set({ accessToken: 'velho' });
    let refreshes = 0;
    // o refresh demora: é justamente na janela de espera que as outras chamadas
    // precisam entrar na fila, em vez de cada uma abrir a sua renovação
    fetchMock.mockImplementation(async (url) => {
      const caminho = String(url);
      if (caminho === '/api/v1/auth/refresh') {
        refreshes++;
        await new Promise((r) => setTimeout(r, 10));
        return resposta(200, { data: { accessToken: 'novo' } });
      }
      return resposta(sessao.access === 'novo' ? 200 : 401, sessao.access === 'novo' ? { data: { ok: caminho } } : {});
    });

    const respostas = await Promise.all([
      api<{ ok: string }>('/api/v1/purchase-orders/'),
      api<{ ok: string }>('/api/v1/quotations/'),
      api<{ ok: string }>('/api/v1/catalog/'),
    ]);

    expect(refreshes).toBe(1);
    expect(respostas.map((r) => r.ok).sort())
      .toEqual(['/api/v1/catalog/', '/api/v1/purchase-orders/', '/api/v1/quotations/']);
    expect(sessao.access).toBe('novo');
  });

  it('depois de uma renovação concluída, um 401 novo renova de novo', async () => {
    sessao.set({ accessToken: 'velho' });
    fetchMock
      .mockResolvedValueOnce(resposta(401, {}))
      .mockResolvedValueOnce(resposta(200, { data: { accessToken: 'n1' } }))
      .mockResolvedValueOnce(resposta(200, { data: { ok: true } }))
      .mockResolvedValueOnce(resposta(401, {}))
      .mockResolvedValueOnce(resposta(200, { data: { accessToken: 'n2' } }))
      .mockResolvedValueOnce(resposta(200, { data: { ok: true } }));
    await api('/api/v1/purchase-orders/');
    await api('/api/v1/purchase-orders/');
    expect(sessao.access).toBe('n2');
  });

  it('refresh recusado pelo servidor limpa a sessão e avisa a aplicação', async () => {
    sessao.set({ accessToken: 'velho' });
    const ouvinte = vi.fn();
    globalThis.addEventListener(EVENTO_SESSAO_EXPIRADA, ouvinte);
    fetchMock
      .mockResolvedValueOnce(resposta(401, {}))
      .mockResolvedValueOnce(resposta(401, {}));
    await expect(api('/api/v1/purchase-orders/')).rejects.toMatchObject({ status: 401 });
    expect(sessao.access).toBeNull();
    expect(ouvinte).toHaveBeenCalledTimes(1);
    globalThis.removeEventListener(EVENTO_SESSAO_EXPIRADA, ouvinte);
  });

  it('falha passageira na renovação mantém o usuário logado', async () => {
    sessao.set({ accessToken: 'velho' });
    const ouvinte = vi.fn();
    globalThis.addEventListener(EVENTO_SESSAO_EXPIRADA, ouvinte);
    fetchMock
      .mockResolvedValueOnce(resposta(401, {}))
      .mockResolvedValueOnce(resposta(429, {}));   // limite de tentativas, não sessão inválida
    await expect(api('/api/v1/purchase-orders/')).rejects.toMatchObject({ status: 503 });
    expect(sessao.access).toBe('velho');
    expect(ouvinte).not.toHaveBeenCalled();
    globalThis.removeEventListener(EVENTO_SESSAO_EXPIRADA, ouvinte);
  });

  it('erro da API vira ErroApi com código e mensagem do servidor', async () => {
    fetchMock.mockResolvedValueOnce(resposta(409, { error: { code: 'PO-ERR-040', message: 'Pedido encerrado.' } }));
    const erro = (await api('/api/v1/x', { method: 'POST', body: { a: 1 } }).catch((e: unknown) => e)) as ErroApi;
    expect(erro).toBeInstanceOf(ErroApi);
    expect(erro.code).toBe('PO-ERR-040');
    expect(erro.message).toBe('Pedido encerrado.');
    expect(erro.status).toBe(409);
  });

  it('erro 500 sem corpo usa a mensagem padrão do legado', async () => {
    fetchMock.mockResolvedValueOnce(new Response('', { status: 500 }));
    await expect(api('/api/v1/x')).rejects.toThrow('O servidor não respondeu a esta operação. Tente novamente em instantes.');
  });
});
