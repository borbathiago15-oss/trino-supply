import { createHmac } from 'node:crypto';

/**
 * O mesmo TOTP (RFC 6238) que o servidor confere, para a suíte fazer o papel do telefone do
 * administrador: diretor e administrador não entram sem a segunda etapa, e o login único da
 * execução é do administrador.
 */
const ALFABETO = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

export function base32Decodificar(texto: string): Buffer {
  const limpo = texto.toUpperCase().replace(/[=\s]/g, '');
  const saida: number[] = [];
  let buffer = 0, bits = 0;
  for (const c of limpo) {
    const valor = ALFABETO.indexOf(c);
    if (valor < 0) throw new Error('segredo fora do alfabeto Base32');
    buffer = (buffer << 5) | valor; bits += 5;
    if (bits >= 8) { saida.push((buffer >> (bits - 8)) & 0xff); bits -= 8; }
  }
  return Buffer.from(saida);
}

export function totp(segredoBase32: string, em = Date.now()): string {
  const contador = Math.floor(em / 1000 / 30);
  const mensagem = Buffer.alloc(8);
  mensagem.writeBigUInt64BE(BigInt(contador));
  const hash = createHmac('sha1', base32Decodificar(segredoBase32)).update(mensagem).digest();
  const offset = hash[hash.length - 1] & 0x0f;
  const binario = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
  return String(binario % 1_000_000).padStart(6, '0');
}
