import { prepararCenario } from './cenario';

/**
 * Login e cenário ficam aqui, e não num passo separado: os testes levam o
 * pedido semeado até a entrega, então cada execução precisa do seu.
 */
export default async function globalSetup() {
  const pedido = await prepararCenario();
  console.log('cenário pronto — pedido', pedido.number);
}
