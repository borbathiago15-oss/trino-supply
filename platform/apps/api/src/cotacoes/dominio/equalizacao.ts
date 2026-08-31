/**
 * A matriz de equalização vive em @trino/contratos: o frontend precisa da
 * MESMA conta para mostrar o mapa comparativo antes de gravar a decisão.
 * Este arquivo mantém o caminho de import estável para o resto da API.
 */
export {
  assertEscolhaJustificada,
  equalizar,
  EqualizacaoError,
  PESOS_PADRAO,
  prazoPagamentoDeCondicao,
  validarPesos,
} from '@trino/contratos';
export type {
  CriterioEqualizacao,
  NotaProposta,
  PesosEqualizacao,
  PropostaParaEqualizar,
  ResultadoEqualizacao,
} from '@trino/contratos';
