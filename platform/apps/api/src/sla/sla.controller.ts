import { Body, Controller, Delete, Get, Param, ParseUUIDPipe, Post, Req, UseGuards } from '@nestjs/common';
import { IsDateString, IsString, Length } from 'class-validator';
import { Auditar } from '../audit/auditar.decorator';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import { conflitoDeUnicidade, ehNaoEncontrado, ehViolacaoDeUnicidade, naoEncontrado } from '../comum/erros';
import { SlaCalculatorService } from './sla.service';

export class CriarFeriadoDto {
  @IsDateString({}, { message: 'data deve ser uma data ISO (AAAA-MM-DD).' })
  data: string;

  @IsString() @Length(1, 120)
  descricao: string;
}

/** Calendário de feriados do tenant — insumo do tempo útil (F7). */
@UseGuards(JwtAuthGuard, TenantGuard)
@Controller()
export class SlaController {
  constructor(private readonly sla: SlaCalculatorService) {}

  @Get('feriados')
  listar(@Req() req: any) {
    return req.db.feriado.findMany({ orderBy: { data: 'asc' } });
  }

  @Post('feriados')
  @Auditar('feriado')
  async criar(@Req() req: any, @Body() dto: CriarFeriadoDto) {
    try {
      return await req.db.feriado.create({
        data: { data: new Date(`${dto.data.slice(0, 10)}T00:00:00.000Z`), descricao: dto.descricao.trim() },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('SLA-ERR-001', 'Já existe feriado cadastrado nesta data.');
      }
      throw e;
    }
  }

  @Delete('feriados/:id')
  @Auditar('feriado')
  async remover(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    try {
      return await req.db.feriado.delete({ where: { id } });
    } catch (e) {
      if (ehNaoEncontrado(e)) throw naoEncontrado('SLA-ERR-404', 'Feriado não encontrado neste tenant.');
      throw e;
    }
  }

  /** TTO e TTR da requisição em tempo útil, com o estado atual da pausa. */
  @Get('requisicoes/:id/sla')
  metricas(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return this.sla.metricasDaRequisicao(req.db, id);
  }
}
