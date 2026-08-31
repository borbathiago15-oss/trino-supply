import { Body, Controller, Get, Param, ParseUUIDPipe, Post, Query, Req, UseGuards } from '@nestjs/common';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import { AbrirCotacaoUseCase, EqualizarPropostasUseCase, obterCotacao, RegistrarPropostaUseCase } from './casos-de-uso';
import { AbrirCotacaoDto, EqualizarDto, RegistrarPropostaDto } from './dto/cotacoes.dto';

/**
 * Cotação (F5). Nenhuma rota usa @Auditar: cada caso de uso grava a própria
 * trilha com o contexto que importa (convidados, recusados, matriz de notas).
 */
@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('cotacoes')
export class CotacoesController {
  constructor(
    private readonly abrir: AbrirCotacaoUseCase,
    private readonly registrarProposta: RegistrarPropostaUseCase,
    private readonly equalizar: EqualizarPropostasUseCase,
  ) {}

  @Get()
  listar(@Req() req: any, @Query('status') status?: string) {
    return req.db.processoCotacao.findMany({
      where: status ? { status } : undefined,
      orderBy: { criadoEm: 'desc' },
    });
  }

  @Get(':id')
  obter(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return obterCotacao(req.db, id);
  }

  @Post()
  abrirCotacao(@Req() req: any, @Body() dto: AbrirCotacaoDto) {
    return this.abrir.executar(req.db, dto, autor(req));
  }

  @Post(':id/propostas')
  propor(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: RegistrarPropostaDto) {
    return this.registrarProposta.executar(req.db, id, dto, autor(req));
  }

  @Post(':id/equalizacao')
  equalizarCotacao(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: EqualizarDto) {
    return this.equalizar.executar(req.db, id, dto, autor(req));
  }
}

export function autor(req: any) {
  const bruto = req.headers?.['x-correlation-id'];
  return {
    usuarioId: req.user.userId,
    ip: req.ip ?? null,
    userAgent: req.headers?.['user-agent'] ?? null,
    correlationId: typeof bruto === 'string' && /^[0-9a-f-]{36}$/i.test(bruto) ? bruto : null,
  };
}
