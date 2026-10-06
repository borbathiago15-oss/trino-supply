/**
 * Quando se busca produto no catálogo.
 *
 * <p>
 * Sem família e sem termo não se busca nada: o catálogo tem milhares de itens, e listar todos
 * é o que fazia o solicitante rolar a tela atrás da bota. Duas letras é o piso — uma letra
 * acha quase tudo e devolve a mesma rolagem com outro nome.
 * </p>
 *
 * <p>
 * A regra mora aqui, e não dentro de uma tela, porque <b>duas telas pedem produto</b> — a SC
 * (`SeletorDeProduto`) e a Solicitar Material. Deixá-la em uma delas faria a outra copiar o
 * número, e copiar é como as duas passariam a exigir coisas diferentes para a mesma busca.
 * </p>
 */
export const MINIMO_DA_BUSCA = 2;

export const podeBuscar = (familia: string, termo: string) =>
  !!familia || termo.trim().length >= MINIMO_DA_BUSCA;
