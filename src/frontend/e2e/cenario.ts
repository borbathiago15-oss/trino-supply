import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { ARQUIVO_CENARIO, ARQUIVO_MFA, ARQUIVO_SESSAO, segredoDoMfa } from './sessao';
import { totp } from './totp';

const API = process.env.API_URL ?? 'http://127.0.0.1:5099';
const EMAIL = process.env.ADMIN_EMAIL ?? 'admin@trinosupply.com.br';
const SENHA = process.env.ADMIN_PASSWORD ?? 'TrinoSupply@2026!';

async function chamar<T>(caminho: string, token?: string, corpo?: unknown, metodo?: string): Promise<T> {
  const res = await fetch(API + caminho, {
    method: metodo ?? (corpo === undefined ? 'GET' : 'POST'),
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
    body: corpo === undefined ? undefined : JSON.stringify(corpo),
  });
  const json = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(`${caminho} → ${res.status} ${json.error?.code ?? ''} ${json.error?.message ?? ''}`);
  return (json.data ?? json) as T;
}

/**
 * Senha definitiva do admin nas execuções da suíte. O admin semeado nasce com a
 * senha do ambiente marcada como provisória (SEC-004), então a primeira execução
 * contra um banco novo troca por esta; as seguintes já entram direto com ela.
 */
export const SENHA_E2E = 'E2e#Suite!2026zx';

async function postarLogin(password: string) {
  return fetch(API + '/api/v1/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, password }),
  });
}

function guardarSessao(tokens: { accessToken: string; refreshToken: string }) {
  mkdirSync(dirname(ARQUIVO_SESSAO), { recursive: true });
  writeFileSync(ARQUIVO_SESSAO, JSON.stringify({
    accessToken: tokens.accessToken, refreshToken: tokens.refreshToken,
  }));
}

type Sessao = { accessToken: string; refreshToken: string; user?: { mustChangePassword?: boolean; mustSetupMfa?: boolean } };

/**
 * O administrador não entra sem a verificação em duas etapas. Na primeira execução contra
 * este banco a suíte cadastra o autenticador (e guarda o segredo em .e2e/mfa.json); nas
 * seguintes, calcula o código a partir dele.
 */
async function garantirAutenticador(sessao: Sessao): Promise<Sessao> {
  if (!sessao.user?.mustSetupMfa) return sessao;
  const cadastro = await chamar<{ secret: string }>('/api/v1/auth/mfa/setup', sessao.accessToken, {});
  const ativado = await chamar<{ sessao: Sessao }>('/api/v1/auth/mfa/enable', sessao.accessToken, { code: totp(cadastro.secret) });
  mkdirSync(dirname(ARQUIVO_MFA), { recursive: true });
  writeFileSync(ARQUIVO_MFA, JSON.stringify({ secret: cadastro.secret }));
  return ativado.sessao;
}

/** A segunda etapa do login do administrador, com o segredo guardado por esta máquina. */
async function segundaEtapa(mfaToken: string): Promise<Sessao> {
  const segredo = segredoDoMfa();
  if (!segredo)
    throw new Error('o administrador tem autenticador cadastrado, mas .e2e/mfa.json não existe nesta máquina: '
      + 'redefina o autenticador dele em Usuários (ou recrie o banco) e rode de novo');
  const etapa = await chamar<{ sessao: Sessao }>('/api/v1/auth/mfa/verify', undefined, { mfaToken, code: totp(segredo) });
  return etapa.sessao;
}

/**
 * Um único login por execução: o login aceita 10 tentativas por minuto por IP
 * (SEC-003), e um login por teste estouraria o limite.
 */
async function entrar(): Promise<string> {
  for (let tentativa = 1; tentativa <= 6; tentativa++) {
    // banco novo: a senha do ambiente ainda vale, e vem marcada como provisória.
    // banco já usado por uma execução anterior: só a SENHA_E2E entra.
    let res = await postarLogin(SENHA);
    let usadaAProvisoria = true;
    if (res.status === 401) { res = await postarLogin(SENHA_E2E); usadaAProvisoria = false; }

    if (res.ok) {
      const json = await res.json();
      let tokens: Sessao = json.data ?? json;
      // com o autenticador já cadastrado, a senha certa dá o token do meio do login
      if ((json.data ?? json).mfaRequired) tokens = await segundaEtapa((json.data ?? json).mfaToken);
      if (!tokens.user?.mustChangePassword) {
        tokens = await garantirAutenticador(tokens);
        guardarSessao(tokens); return tokens.accessToken;
      }

      if (!usadaAProvisoria)
        throw new Error('a senha da suíte entrou mas continua provisória — banco em estado inesperado');

      // primeira execução deste banco: define a senha da suíte e segue com ela
      let trocados = await chamar<Sessao>(
        '/api/v1/auth/change-password', tokens.accessToken,
        { currentPassword: SENHA, newPassword: SENHA_E2E },
      );
      // e, logo depois da senha, o autenticador: a sessão trocada ainda está presa no cadastro
      trocados = await garantirAutenticador(trocados);
      guardarSessao(trocados);
      return trocados.accessToken;
    }
    if (res.status !== 429) throw new Error(`login falhou: ${res.status} ${await res.text()}`);
    await new Promise((r) => setTimeout(r, 8000));
  }
  throw new Error('login bloqueado pelo limite de tentativas — espere um minuto e rode de novo');
}

/**
 * Cenário mínimo de cada execução: fornecedor, local de estoque e um pedido de
 * compra em aberto. O pedido é sempre novo, porque os testes o levam até a
 * entrega — reaproveitar o da execução anterior deixaria a suíte instável.
 */
export async function prepararCenario() {
  const token = await entrar();

  const { items: fornecedores } = await chamar<{ items: { id: string; taxId: string }[] }>('/api/v1/suppliers/', token);
  const fornecedor = fornecedores.find((s) => s.taxId === '12345678000199')
    ?? await chamar<{ id: string }>('/api/v1/suppliers/', token, {
      legalName: 'Alfa Equipamentos de Proteção Ltda', tradeName: 'Alfa EPIs', taxId: '12345678000199',
      email: 'vendas@alfaepis.com.br', phone: '11 4000-0000',
    });

  // homologado: só fornecedor homologado vence o BID (SUP-ERR-030), e o E2E do orçamento escolhe o vencedor
  await chamar(`/api/v1/suppliers/${fornecedor.id}/homologation`, token, { status: 'HOMOLOGADO' }, 'PATCH');

  const { items: locais } = await chamar<{ items: { code: string }[] }>('/api/v1/inventory/locations', token);
  if (!locais.some((l) => l.code === 'ALM-01'))
    await chamar('/api/v1/inventory/locations', token, { code: 'ALM-01', name: 'Almoxarifado Central' });

  // Centro de custo, família e produto: as telas que escolhem um deles em uma
  // lista (Material, SC, lote) não podem depender de outra spec ter rodado antes.
  const { items: centros } = await chamar<{ items: { code: string }[] }>('/api/v1/cost-centers/', token);
  if (!centros.some((c) => c.code === 'E2E-001'))
    await chamar('/api/v1/cost-centers/', token, { code: 'E2E-001', name: 'Centro de Custo do Cenário E2E' });
  // o segundo centro existe para o filtro do Painel de Atendimentos: com um centro só, "o
  // seletor oferece o cadastro inteiro" e "o seletor oferece o recorte" dariam a mesma lista
  if (!centros.some((c) => c.code === 'E2E-002'))
    await chamar('/api/v1/cost-centers/', token, { code: 'E2E-002', name: 'Segundo Centro do Cenário E2E' });

  const { items: familias } = await chamar<{ items: { name: string }[] }>('/api/v1/product-families/', token);
  if (!familias.some((f) => f.name === 'EPI CENARIO E2E'))
    await chamar('/api/v1/product-families/', token, { name: 'EPI CENARIO E2E' });

  const { items: produtos } = await chamar<{ items: { code: string }[] }>(
    '/api/v1/items/?family=' + encodeURIComponent('EPI CENARIO E2E'), token);
  if (!produtos.length)
    await chamar('/api/v1/items/', token, {
      code: 'E2E-EPI-001', description: 'Luva nitrílica do cenário E2E', family: 'EPI CENARIO E2E',
      unitOfMeasure: 'PAR', referencePrice: 12.5, stockControlled: true, purchasable: true,
    });

  const pedido = await chamar<{ id: string; number: string }>('/api/v1/purchase-orders/', token, {
    supplierId: fornecedor.id,
    notes: 'Pedido criado pelo cenário E2E do frontend React',
    items: [
      { description: 'Luva nitrílica tamanho M', quantity: 10, unitOfMeasure: 'PAR', unitPrice: 12.5 },
      { description: 'Óculos de proteção incolor', quantity: 4, unitOfMeasure: 'UN', unitPrice: 18 },
    ],
  });

  mkdirSync(dirname(ARQUIVO_CENARIO), { recursive: true });
  writeFileSync(ARQUIVO_CENARIO, JSON.stringify({ pedidoNumero: pedido.number, pedidoId: pedido.id }));
  return pedido;
}
