import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { ARQUIVO_SESSAO } from './sessao';

const API = process.env.API_URL ?? 'http://127.0.0.1:5099';
const EMAIL = process.env.ADMIN_EMAIL ?? 'admin@trinosupply.com.br';
const SENHA = process.env.ADMIN_PASSWORD ?? 'TrinoSupply@2026!';

/**
 * Um único login por execução, gravado em disco para todos os workers.
 * O login aceita 10 tentativas por minuto por IP (SEC-003); um login por
 * teste estouraria o limite e derrubaria a suíte por engano.
 */
export default async function globalSetup() {
  for (let tentativa = 1; tentativa <= 6; tentativa++) {
    const res = await fetch(API + '/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: EMAIL, password: SENHA }),
    });
    if (res.ok) {
      const json = await res.json();
      const { accessToken, refreshToken } = json.data ?? json;
      mkdirSync(dirname(ARQUIVO_SESSAO), { recursive: true });
      writeFileSync(ARQUIVO_SESSAO, JSON.stringify({ accessToken, refreshToken }));
      return;
    }
    if (res.status !== 429) throw new Error(`login falhou: ${res.status} ${await res.text()}`);
    await new Promise((r) => setTimeout(r, 8000));
  }
  throw new Error('login bloqueado pelo limite de tentativas — espere um minuto e rode de novo');
}
