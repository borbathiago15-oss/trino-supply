import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { entrarNoPortal, EVENTO_PORTAL_EXPIRADO, minhasCotacoes, sessaoPortal } from './portal';

const resposta = (status: number, corpo: unknown) =>
  Promise.resolve({ status, ok: status >= 200 && status < 300, json: () => Promise.resolve(corpo) } as Response);

describe('sessão do portal', () => {
  beforeEach(() => { sessaoPortal.token = null; vi.restoreAllMocks(); });
  afterEach(() => { sessaoPortal.token = null; });

  it('401 no login é credencial errada: mostra a mensagem do servidor', async () => {
    vi.spyOn(globalThis, 'fetch').mockReturnValue(resposta(401, {
      error: { code: 'RFQ-ERR-051', message: 'CNPJ/CPF ou chave de acesso inválidos, ou fornecedor sem acesso ao portal.' },
    }));
    await expect(entrarNoPortal('123', 'errada')).rejects.toThrow(/chave de acesso inválidos/);
  });

  it('401 fora do login é sessão vencida: descarta o token e avisa a tela', async () => {
    sessaoPortal.token = 'token-velho';
    const expirou = vi.fn();
    globalThis.addEventListener(EVENTO_PORTAL_EXPIRADO, expirou);
    vi.spyOn(globalThis, 'fetch').mockReturnValue(resposta(401, {}));

    await expect(minhasCotacoes('')).rejects.toThrow(/Sessão expirada/);
    expect(sessaoPortal.ativa).toBe(false);
    expect(expirou).toHaveBeenCalled();
    globalThis.removeEventListener(EVENTO_PORTAL_EXPIRADO, expirou);
  });

  it('o login bem-sucedido guarda o token da sessão do fornecedor', async () => {
    vi.spyOn(globalThis, 'fetch').mockReturnValue(resposta(200, {
      data: { accessToken: 'tok', supplier: { id: 's1', name: 'Alfa', taxId: '1' } },
    }));
    const fornecedor = await entrarNoPortal('123', 'certa');
    expect(fornecedor.name).toBe('Alfa');
    expect(sessaoPortal.token).toBe('tok');
  });

  it('a sessão do portal não é a do time interno', async () => {
    sessionStorage.setItem('ts.access', 'token-do-app-interno');
    expect(sessaoPortal.ativa).toBe(false);
  });
});
