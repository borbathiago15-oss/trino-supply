import { ehSubgrupo, MENU } from './menu';

/** Título da barra superior por rota, tirado do próprio menu. */
const porRota = new Map<string, string>(
  MENU.flatMap((g) => g.itens.flatMap((i) => (ehSubgrupo(i) ? i.filhos : [i])))
    .filter((i) => i.rota)
    .map((i) => [i.rota as string, i.rotulo]),
);

export function tituloDaRota(pathname: string): string {
  // a rota exata vence; /pedidos/{id} cai no título de /pedidos
  const raiz = '/' + (pathname.split('/')[1] ?? '');
  return porRota.get(pathname) ?? porRota.get(raiz) ?? 'Trino Supply';
}
