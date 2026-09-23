import type { ComponentProps, MouseEvent } from 'react';

/**
 * Link para uma tela que vive fora do `AppLayout` e por isso abre em aba nova.
 *
 * A sessão deste sistema mora no `sessionStorage`, que é **por aba**, e o navegador
 * só copia o `sessionStorage` para a aba nova quando ela mantém o `opener`. O
 * `target="_blank"` sozinho não serve: os navegadores passaram a aplicar `noopener`
 * por conta própria, e a aba nascia sem token — quem já estava logado via a tela de
 * login de novo, que foi exatamente o que aconteceu com o cockpit.
 *
 * Duas coisas devolvem o `opener`, e as duas ficam porque cobrem caminhos diferentes:
 * `rel="opener"` vale para **qualquer** forma de seguir o link (inclusive ctrl+clique
 * e botão do meio, que são do navegador e não passam por handler nenhum), e o
 * `window.open` do clique simples é o que garante o comportamento onde o `rel` não
 * for respeitado. Nenhum dos dois é risco de *tabnabbing*: o destino é rota interna,
 * da mesma origem.
 *
 * O `href` continua no elemento — é ele que faz os atalhos do navegador existirem.
 * Abrir a URL direto (o caso da TV da sala, que se liga uma vez e fica) pede login,
 * e isso está certo: ali não há aba de origem de onde herdar sessão.
 */
export function LinkAbaNova({ href, onClick, children, ...resto }: ComponentProps<'a'> & { href: string }) {
  const abrir = (e: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(e);
    if (e.defaultPrevented) return;
    // atalhos do navegador continuam sendo dele
    if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    // bloqueador de pop-up devolve null: aí o target="_blank" cuida, que é melhor que nada
    if (window.open(href, '_blank')) e.preventDefault();
  };

  return <a href={href} target="_blank" rel="opener" onClick={abrir} {...resto}>{children}</a>;
}
