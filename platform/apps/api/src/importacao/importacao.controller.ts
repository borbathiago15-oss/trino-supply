import { Body, Controller, Get, Header, Param, ParseUUIDPipe, Post, Req, Res, UseGuards } from '@nestjs/common';
import type { Response } from 'express';
import { IsIn, IsOptional, IsString, Length } from 'class-validator';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import { TipoLote } from './dominio/parsers';
import { ImportacaoStagingService } from './importacao-staging.service';

const TIPOS: TipoLote[] = ['CATALOGO_SKU', 'PARAMETRO_ESTOQUE', 'FORNECEDOR', 'ORCAMENTO_CC'];

export class IngerirDto {
  @IsIn(TIPOS, { message: `tipo deve ser um de: ${TIPOS.join(', ')}` })
  tipo: TipoLote;

  @IsString() @Length(1, 255)
  nomeArquivo: string;

  /** Conteúdo do arquivo em texto (CSV). */
  @IsString() @Length(1, 20_000_000)
  conteudo: string;

  @IsOptional() @IsString()
  arquivoUri?: string;
}

@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('importacoes')
export class ImportacaoController {
  constructor(private readonly staging: ImportacaoStagingService) {}

  @Get()
  listar(@Req() req: any) {
    return req.db.loteImportacao.findMany({ orderBy: { iniciadoEm: 'desc' } });
  }

  /**
   * Recebe o arquivo, grava as linhas em staging e devolve na hora — o
   * processamento roda no worker. Reenvio do mesmo conteúdo devolve o lote
   * anterior (idempotência por SHA-256).
   */
  @Post()
  ingerir(@Req() req: any, @Body() dto: IngerirDto) {
    return this.staging.ingerir(req.db, req.tenantId, req.user.userId, dto);
  }

  @Get(':id')
  obter(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return this.staging.obterLote(req.db, id);
  }

  /** Relatório de inconformidades em JSON. */
  @Get(':id/inconformidades')
  inconformidades(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return this.staging.inconformidades(req.db, id);
  }

  /** Mesmo relatório em CSV, para baixar e corrigir na planilha. */
  @Get(':id/inconformidades.csv')
  @Header('content-type', 'text/csv; charset=utf-8')
  async baixarInconformidades(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Res() res: Response) {
    const { nome, csv } = await this.staging.inconformidadesCsv(req.db, id);
    res.setHeader('content-disposition', `attachment; filename="${nome}"`);
    // BOM para o Excel abrir acentuação corretamente.
    res.send(`﻿${csv}`);
  }
}
