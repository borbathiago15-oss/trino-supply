/**
 * Leva a tela até um bloco, quando o navegador souber rolar. `scrollIntoView`
 * não existe em todo ambiente (o jsdom dos testes, por exemplo), e rolar é
 * conforto: nunca deve derrubar a ação que o usuário pediu.
 */
export function rolarPara(id: string) {
  const alvo = document.getElementById(id);
  alvo?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
}
