import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { ARQUIVO_CENARIO, ARQUIVO_SESSAO } from './sessao';

const API = process.env.API_URL ?? 'http://127.0.0.1:5099';
const EMAIL = process.env.ADMIN_EMAIL ?? 'admin@trinosupply.com.br';
const SENHA = process.env.ADMIN_PASSWORD ?? 'TrinoSupply@2026!';

async function chamar<T>(caminho: string, token?: string, corpo?: unknown): Promise<T> {
  const res = await fetch(API + caminho, {
    method: corpo === undefined ? 'GET' : 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
    body: corpo === undefined ? undefined : JSON.stringify(corpo),
  });
  const json = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(`${caminho} → ${res.status} ${json.error?.code ?? ''} ${json.error?.message ?? ''}`);
  return (json.data ?? json) as T;
}

/**
 * Um único login por execução: o login aceita 10 tentativas por minuto por IP
 * (SEC-003), e um login por teste estouraria o limite.
 */
async function entrar(): Promise<string> {
  for (let tentativa = 1; tentativa <= 6; tentativa++) {
    const res = await fetch(API + '/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: EMAIL, password: SENHA }),
    });
    if (res.ok) {
      const json = await res.json();
      const tokens = json.data ?? json;
      mkdirSync(dirname(ARQUIVO_SESSAO), { recursive: true });
      writeFileSync(ARQUIVO_SESSAO, JSON.stringify({ accessToken: tokens.accessToken, refreshToken: tokens.refreshToken }));
      return tokens.accessToken;
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

  const { items: locais } = await chamar<{ items: { code: string }[] }>('/api/v1/inventory/locations', token);
  if (!locais.some((l) => l.code === 'ALM-01'))
    await chamar('/api/v1/inventory/locations', token, { code: 'ALM-01', name: 'Almoxarifado Central' });

  // Centro de custo, família e produto: as telas que escolhem um deles em uma
  // lista (Material, SC, lote) não podem depender de outra spec ter rodado antes.
  const { items: centros } = await chamar<{ items: { code: string }[] }>('/api/v1/cost-centers/', token);
  if (!centros.some((c) => c.code === 'E2E-001'))
    await chamar('/api/v1/cost-centers/', token, { code: 'E2E-001', name: 'Centro de Custo do Cenário E2E' });

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
