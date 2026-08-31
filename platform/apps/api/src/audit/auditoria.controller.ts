import { Controller, Get, Query, Req, UseGuards } from '@nestjs/common';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';

/**
 * Leitura da trilha de auditoria. É o histórico de transições que a interface
 * mostra: cada mudança de estado gravou um registro com snapshot antes/depois.
 * Somente leitura e sempre escopada ao tenant do token.
 */
@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('auditoria')
export class AuditoriaController {
  @Get()
  async listar(
    @Req() req: any,
    @Query('entidade') entidade?: string,
    @Query('entityId') entityId?: string,
    @Query('limite') limite?: string,
  ) {
    const registros = await req.db.auditLog.findMany({
      where: {
        ...(entidade ? { entidade } : {}),
        ...(entityId ? { entityId } : {}),
      },
      orderBy: { timestampUtc: 'desc' },
      take: Math.min(Number(limite ?? 100), 500),
    });
    // id é BIGINT: o JSON do Nest não serializa BigInt.
    return registros.map((r: any) => ({ ...r, id: String(r.id) }));
  }
}
