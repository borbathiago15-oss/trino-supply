import { Body, Controller, Get, Param, ParseUUIDPipe, Patch, Post, Query, Req, UseGuards } from '@nestjs/common';
import { Auditar } from '../audit/auditar.decorator';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import { AprovacoesService } from './aprovacoes.service';
import { AprovarEtapaUseCase } from './aprovar-etapa.usecase';
import {
  AbrirInstanciaDto,
  CriarDelegacaoDto,
  CriarRegraAlcadaDto,
  DecidirEtapaDto,
  DefinirAprovadorDto,
  EncerrarDelegacaoDto,
} from './dto/aprovacoes.dto';

const UuidOpcional = new ParseUUIDPipe({ optional: true });

@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('aprovacoes')
export class AprovacoesController {
  constructor(
    private readonly aprovacoes: AprovacoesService,
    private readonly aprovarEtapa: AprovarEtapaUseCase,
  ) {}

  @Get('regras')
  listarRegras(@Req() req: any) {
    return this.aprovacoes.listarRegras(req.db);
  }

  @Post('regras')
  @Auditar('regra_alcada', 'regraAlcada')
  criarRegra(@Req() req: any, @Body() dto: CriarRegraAlcadaDto) {
    return this.aprovacoes.criarRegra(req.db, dto);
  }

  @Get('aprovadores')
  listarAprovadores(@Req() req: any, @Query('centroCustoId', UuidOpcional) centroCustoId?: string) {
    return this.aprovacoes.listarAprovadores(req.db, centroCustoId);
  }

  @Post('aprovadores')
  @Auditar('aprovador_centro_custo', 'aprovadorCentroCusto')
  definirAprovador(@Req() req: any, @Body() dto: DefinirAprovadorDto) {
    return this.aprovacoes.definirAprovador(req.db, dto);
  }

  @Get('delegacoes')
  listarDelegacoes(@Req() req: any, @Query('delegadoId', UuidOpcional) delegadoId?: string) {
    return this.aprovacoes.listarDelegacoes(req.db, delegadoId);
  }

  @Post('delegacoes')
  @Auditar('delegacao_alcada', 'delegacaoAlcada')
  criarDelegacao(@Req() req: any, @Body() dto: CriarDelegacaoDto) {
    return this.aprovacoes.criarDelegacao(req.db, dto);
  }

  @Patch('delegacoes/:id')
  @Auditar('delegacao_alcada', 'delegacaoAlcada')
  encerrarDelegacao(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: EncerrarDelegacaoDto) {
    return this.aprovacoes.encerrarDelegacao(req.db, id, dto.ativa);
  }

  @Post('instancias')
  @Auditar('instancia_aprovacao', 'instanciaAprovacao')
  abrirInstancia(@Req() req: any, @Body() dto: AbrirInstanciaDto) {
    return this.aprovacoes.abrirInstancia(req.db, dto);
  }

  @Get('instancias/:id')
  obterInstancia(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return this.aprovacoes.obterInstancia(req.db, id);
  }

  /**
   * Decisão da etapa. SEM @Auditar de propósito: o próprio caso de uso grava o
   * AuditLog DENTRO da transação serializável, junto com a decisão — auditar
   * de fora, depois do commit, deixaria brecha entre decidir e registrar.
   */
  @Post('etapas/:id/decisao')
  decidirEtapa(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: DecidirEtapaDto) {
    return this.aprovarEtapa.executar({
      db: req.db,
      tenantId: req.tenantId,
      usuarioId: req.user.userId,
      etapaId: id,
      decisao: dto.decisao,
      autorizarEstouro: dto.autorizarEstouro === true,
      comentario: dto.comentario ?? null,
      ip: req.ip ?? null,
      userAgent: req.headers?.['user-agent'] ?? null,
      correlationId: this.correlationId(req),
    });
  }

  private correlationId(req: any): string | null {
    const bruto = req.headers?.['x-correlation-id'];
    return typeof bruto === 'string' && /^[0-9a-f-]{36}$/i.test(bruto) ? bruto : null;
  }
}
