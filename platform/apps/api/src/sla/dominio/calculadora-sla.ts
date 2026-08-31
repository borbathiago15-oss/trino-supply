/**
 * O cálculo de tempo útil vive em @trino/contratos: o frontend precisa da
 * MESMA conta para pintar o farol de SLA sem uma ida ao servidor por linha.
 * Este arquivo mantém o caminho de import estável para o resto da API.
 */
export {
  ajustarPausaSla,
  calcularMetricas,
  CONFIG_COMERCIAL_PADRAO,
  ehDiaUtil,
  ESTADOS_COM_SLA_PAUSADO,
  segundosUteis,
} from '@trino/contratos';
export type {
  AjustePausa,
  ConfiguracaoSla,
  EstadoPausa,
  MarcosRequisicao,
  MetricaSla,
  RegimeSla,
} from '@trino/contratos';
