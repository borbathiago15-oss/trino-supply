import { sessao } from '@/api/sessao';

/**
 * O app de bolso e o sistema são o mesmo site: cada abertura já carrega o que está no ar,
 * porque o `index.html` sai com `no-cache` e os arquivos do Vite têm hash no nome. O buraco
 * é a página que **já estava aberta** — o app que o Android manteve na memória, a aba deixada
 * de manhã — que continua no código antigo até alguém recarregar, e pode chamar uma API que
 * não existe mais sem saber por quê.
 *
 * A régua é simples: a versão que esta página carregou é o **primeiro commit** que o servidor
 * disse a ela; quando uma consulta posterior diz outro, houve deploy desde então. Não depende
 * de gravar o commit dentro do build (que exigiria o build arg chegar certo no Railway, e
 * falharia em silêncio se não chegasse) — o `/health` já diz o commit para quem tem sessão.
 */

/** Quanto tempo entre consultas com a página à vista. */
export const INTERVALO_DE_CONSULTA = 5 * 60 * 1000;
/** Voltar ao app dispara uma consulta, mas não uma a cada troca de aba em segundos. */
export const FOLGA_ENTRE_CONSULTAS = 60 * 1000;

/** Há versão nova só quando as duas são conhecidas e diferem: sem commit no ambiente, nada avisa. */
export function haNovaVersao(carregada: string | null | undefined, noAr: string | null | undefined): boolean {
  if (!carregada || !noAr) return false;
  return carregada !== noAr;
}

/**
 * O commit no ar, pelo `/health` com a sessão; nulo quando o servidor não informa, quando a
 * sessão não vale ali ou quando a rede falha — e nulo nunca vira aviso. Não passa pelo
 * cliente da API de propósito: uma sondagem em segundo plano não deve renovar sessão nem
 * derrubá-la por causa de um 401.
 */
export async function commitNoAr(): Promise<string | null> {
  try {
    const token = sessao.access;
    if (!token) return null;
    const res = await fetch('/health', { headers: { Authorization: 'Bearer ' + token }, cache: 'no-store' });
    const json = await res.json().catch(() => null) as { commit?: unknown } | null;
    return typeof json?.commit === 'string' && json.commit ? json.commit : null;
  } catch {
    return null;
  }
}
