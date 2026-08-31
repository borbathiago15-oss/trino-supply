import { Injectable } from '@nestjs/common';
import { ClientEscopado } from '@trino/db';
import { naoEncontrado } from '../comum/erros';
import {
  calcularMetricas,
  CONFIG_COMERCIAL_PADRAO,
  ConfiguracaoSla,
  RegimeSla,
  segundosUteis,
} from './dominio/calculadora-sla';

/**
 * SlaCalculatorService — a ponte entre o domínio puro e o mundo: carrega os
 * feriados do tenant, lê o regime do ambiente e entrega funções prontas.
 *
 * Regime por ambiente:
 *   SLA_REGIME=COMERCIAL (padrão)  → 08:00–18:00, seg–sex, menos feriados
 *   SLA_REGIME=24_7                → relógio corrido
 *   SLA_HORA_INICIO / SLA_HORA_FIM / SLA_OFFSET_MINUTOS ajustam o expediente.
 */
@Injectable()
export class SlaCalculatorService {
  /** Config completa do tenant — os feriados vêm do banco a cada chamada. */
  async configuracao(db: ClientEscopado): Promise<ConfiguracaoSla> {
    const regime: RegimeSla = process.env.SLA_REGIME === '24_7' ? 'VINTE_QUATRO_SETE' : 'COMERCIAL';
    const feriados =
      regime === 'COMERCIAL'
        ? await db.feriado.findMany({ select: { data: true } })
        : [];
    return {
      regime,
      horaInicio: Number(process.env.SLA_HORA_INICIO ?? CONFIG_COMERCIAL_PADRAO.horaInicio),
      horaFim: Number(process.env.SLA_HORA_FIM ?? CONFIG_COMERCIAL_PADRAO.horaFim),
      offsetMinutos: Number(process.env.SLA_OFFSET_MINUTOS ?? CONFIG_COMERCIAL_PADRAO.offsetMinutos),
      feriados: new Set(feriados.map((f: any) => f.data.toISOString().slice(0, 10))),
    };
  }

  /** Segundos úteis entre dois instantes no regime vigente do tenant. */
  async segundosUteisEntre(db: ClientEscopado, inicio: Date, fim: Date): Promise<number> {
    return segundosUteis(inicio, fim, await this.configuracao(db));
  }

  /** TTO e TTR da requisição, em tempo útil, com o estado da pausa. */
  async metricasDaRequisicao(db: ClientEscopado, requisicaoId: string) {
    const requisicao = await db.requisicaoCompra.findUnique({ where: { id: requisicaoId } });
    if (!requisicao) throw naoEncontrado('REQ-ERR-404', 'Requisição não encontrada neste tenant.');
    const config = await this.configuracao(db);
    const metricas = calcularMetricas(
      {
        submetidaEm: requisicao.submetidaEm,
        triagemEm: requisicao.triagemEm,
        concluidaEm: requisicao.concluidaEm,
        slaPausadoEm: requisicao.slaPausadoEm,
        slaSegundosPausados: requisicao.slaSegundosPausados,
      },
      new Date(),
      config,
    );
    return {
      requisicaoId,
      numero: requisicao.numero,
      status: requisicao.status,
      regime: config.regime,
      ...metricas,
    };
  }
}
