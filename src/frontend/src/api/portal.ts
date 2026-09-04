import { ErroApi } from './cliente';

/**
 * O portal é do fornecedor, não do time interno: sessão própria, sem refresh e
 * sem o `ts.access` do app. Por isso ele não usa o `api()` do cliente interno —
 * um 401 aqui manda voltar ao login do portal, não à tela de login da empresa.
 */
const CHAVE = 'portal.token';

export const sessaoPortal = {
  get token() { return sessionStorage.getItem(CHAVE); },
  set token(v: string | null) {
    if (v) sessionStorage.setItem(CHAVE, v); else sessionStorage.removeItem(CHAVE);
  },
  get ativa() { return !!sessionStorage.getItem(CHAVE); },
};

export const EVENTO_PORTAL_EXPIRADO = 'portal:expirado';

interface Opcoes {
  method?: string;
  body?: unknown;
  form?: FormData;
  signal?: AbortSignal;
}

async function chamar<T>(caminho: string, o: Opcoes = {}): Promise<T> {
  const token = sessaoPortal.token;
  const res = await fetch(caminho, {
    method: o.method ?? 'GET',
    headers: {
      ...(o.form ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: o.form ?? (o.body !== undefined ? JSON.stringify(o.body) : undefined),
    signal: o.signal,
  });

  const json = await res.json().catch(() => ({}));
  const erro = (json as { error?: { code?: string; message?: string } }).error;

  // 401 no login é credencial errada — quem está entrando não tem sessão a expirar.
  // Fora do login, é sessão vencida: descarta o token e volta à tela de acesso.
  if (res.status === 401 && !caminho.endsWith('/login')) {
    sessaoPortal.token = null;
    globalThis.dispatchEvent(new Event(EVENTO_PORTAL_EXPIRADO));
    throw new ErroApi('Sessão expirada — entre novamente.', 401, 'PORTAL-401');
  }
  if (!res.ok)
    throw new ErroApi(erro?.message ?? 'Falha na operação.', res.status, erro?.code ?? 'PORTAL-ERR');
  return ((json as { data?: T }).data ?? json) as T;
}

export interface Fornecedor { id: string; name: string; taxId: string }

export interface ItemDaCotacao {
  id: string;
  sequence: number;
  description: string;
  quantity: number;
  unitOfMeasure: string;
}

export interface MinhaProposta {
  id: string;
  version: number;
  totalValue: number;
  deliveryDays: number | null;
  paymentTerms: string | null;
  freightValue: number | null;
  validUntil: string | null;
  notes: string | null;
  submittedAt: string;
  attachmentDocumentId: string | null;
  attachmentFileName: string | null;
  items: { quotationItemId: string; unitPrice: number; quantity: number }[];
}

export interface CotacaoDoPortal {
  id: string;
  number: string;
  kind: string;
  /** Só a cotação aberta aceita proposta nova. */
  open: boolean;
  status: string;
  deadline: string | null;
  notes: string | null;
  createdAt: string;
  items: ItemDaCotacao[];
  myProposals: MinhaProposta[];
}

export const entrarNoPortal = async (taxId: string, accessKey: string) => {
  const r = await chamar<{ accessToken: string; supplier: Fornecedor }>('/api/v1/portal/login', {
    method: 'POST', body: { taxId, accessKey },
  });
  sessaoPortal.token = r.accessToken;
  return r.supplier;
};

export const sairDoPortal = () => { sessaoPortal.token = null; };

const normalizar = (c: CotacaoDoPortal): CotacaoDoPortal => ({
  ...c,
  items: c.items ?? [],
  myProposals: (c.myProposals ?? []).map((p) => ({ ...p, items: p.items ?? [] })),
});

export async function minhasCotacoes(numero: string, signal?: AbortSignal) {
  const q = numero.trim() ? `?number=${encodeURIComponent(numero.trim())}` : '';
  const r = await chamar<{ items: CotacaoDoPortal[] }>(`/api/v1/portal/quotations${q}`, { signal });
  return (r.items ?? []).map(normalizar);
}

export const lerCotacao = async (id: string, signal?: AbortSignal) =>
  normalizar(await chamar<CotacaoDoPortal>(`/api/v1/portal/quotations/${id}`, { signal }));

export interface PropostaDoPortal {
  deliveryDays: number | null;
  paymentTerms: string | null;
  freightValue: number | null;
  validUntil: string | null;
  notes: string | null;
  items: { quotationItemId: string; unitPrice: number; quantity: null }[];
}

export const enviarProposta = (cotacaoId: string, dados: PropostaDoPortal) =>
  chamar<{ id: string; version: number; totalValue: number }>(
    `/api/v1/portal/quotations/${cotacaoId}/proposal`, { method: 'POST', body: dados });

export function anexarNaProposta(propostaId: string, arquivo: File) {
  const form = new FormData();
  form.append('file', arquivo);
  return chamar<unknown>(`/api/v1/portal/proposals/${propostaId}/attachment`, { method: 'POST', form });
}

/**
 * O portal exige preço de todos os itens: uma proposta parcial não dá para
 * comparar no mapa. (No lançamento interno, o comprador pode registrar o que
 * o fornecedor cotou de fato.)
 */
export function montarPropostaDoPortal(
  itens: ItemDaCotacao[],
  precos: Record<string, string>,
  campos: { prazoEntrega: string; condicaoPagamento: string; frete: string; validade: string; observacao: string },
): { proposta: PropostaDoPortal | null; erro: string | null } {
  const linhas = itens.map((i) => ({
    quotationItemId: i.id,
    unitPrice: Number((precos[i.id] ?? '').replace(',', '.')),
  }));
  if (linhas.some((l) => !Number.isFinite(l.unitPrice) || l.unitPrice <= 0))
    return { proposta: null, erro: 'Informe o preço de todos os itens.' };

  const inteiro = Number.parseInt(campos.prazoEntrega, 10);
  const frete = Number(campos.frete.replace(',', '.'));
  return {
    erro: null,
    proposta: {
      deliveryDays: Number.isNaN(inteiro) ? null : inteiro,
      paymentTerms: campos.condicaoPagamento || null,
      freightValue: campos.frete.trim() && !Number.isNaN(frete) ? frete : null,
      validUntil: campos.validade || null,
      notes: campos.observacao || null,
      items: linhas.map((l) => ({ ...l, quantity: null })),
    },
  };
}
