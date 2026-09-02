// Cenário mínimo para o E2E: fornecedor, local de estoque e um pedido em aberto.
// Usa só a API pública, como um usuário faria.
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const API = process.env.API_URL ?? 'http://127.0.0.1:5099';
const ARQUIVO_CENARIO = fileURLToPath(new URL('../.e2e/cenario.json', import.meta.url));
const EMAIL = process.env.ADMIN_EMAIL ?? 'admin@trinosupply.com.br';
const SENHA = process.env.ADMIN_PASSWORD ?? 'TrinoSupply@2026!';

async function chamar(caminho, corpo, token) {
  const res = await fetch(API + caminho, {
    method: corpo === undefined ? 'GET' : 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
    body: corpo === undefined ? undefined : JSON.stringify(corpo),
  });
  const json = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(`${caminho} → ${res.status} ${json.error?.code ?? ''} ${json.error?.message ?? ''}`);
  return json.data ?? json;
}

// o login tem limite de tentativas: espera e repete em vez de falhar
async function entrar() {
  for (let i = 0; i < 6; i++) {
    try { return (await chamar('/api/v1/auth/login', { email: EMAIL, password: SENHA })).accessToken; }
    catch (e) { if (!String(e.message).includes('429') || i === 5) throw e; await new Promise((r) => setTimeout(r, 5000 * (i + 1))); }
  }
}

const token = await entrar();

const fornecedores = (await chamar('/api/v1/suppliers/', undefined, token)).items;
let fornecedor = fornecedores.find((s) => s.taxId === '12345678000199');
if (!fornecedor)
  fornecedor = await chamar('/api/v1/suppliers/', {
    legalName: 'Alfa Equipamentos de Proteção Ltda', tradeName: 'Alfa EPIs', taxId: '12345678000199',
    email: 'vendas@alfaepis.com.br', phone: '11 4000-0000',
  }, token);

const locais = (await chamar('/api/v1/inventory/locations', undefined, token)).items;
if (!locais.some((l) => l.code === 'ALM-01'))
  await chamar('/api/v1/inventory/locations', { code: 'ALM-01', name: 'Almoxarifado Central' }, token);

const pedido = await chamar('/api/v1/purchase-orders/', {
  supplierId: fornecedor.id,
  notes: 'Pedido criado pelo cenário E2E do frontend React',
  items: [
    { description: 'Luva nitrílica tamanho M', quantity: 10, unitOfMeasure: 'PAR', unitPrice: 12.5 },
    { description: 'Óculos de proteção incolor', quantity: 4, unitOfMeasure: 'UN', unitPrice: 18 },
  ],
}, token);

// o teste precisa saber qual pedido é o desta execução: o banco pode ter outros
mkdirSync(dirname(ARQUIVO_CENARIO), { recursive: true });
writeFileSync(ARQUIVO_CENARIO, JSON.stringify({ pedidoNumero: pedido.number, pedidoId: pedido.id }));

console.log('pedido semeado:', pedido.number, pedido.id);
