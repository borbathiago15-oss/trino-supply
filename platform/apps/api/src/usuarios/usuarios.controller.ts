import { Body, Controller, Delete, Get, Param, Patch, Post, Req, UseGuards } from '@nestjs/common';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import { Auditar } from '../audit/auditar.decorator';
import { AtualizarUsuarioDto, CriarUsuarioDto, UsuariosService } from './usuarios.service';

/**
 * CRUD de usuários do tenant — toda rota exige JWT válido e opera pelo
 * request.db escopado (TenantGuard). Mutações são críticas: @Auditar grava
 * before/after no audit_log via AuditInterceptor global.
 */
@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('usuarios')
export class UsuariosController {
  constructor(private readonly usuarios: UsuariosService) {}

  @Get()
  listar(@Req() req: any) {
    return this.usuarios.listar(req.db);
  }

  @Post()
  @Auditar('usuario')
  criar(@Req() req: any, @Body() dto: CriarUsuarioDto) {
    return this.usuarios.criar(req.db, dto);
  }

  @Patch(':id')
  @Auditar('usuario')
  atualizar(@Req() req: any, @Param('id') id: string, @Body() dto: AtualizarUsuarioDto) {
    return this.usuarios.atualizar(req.db, id, dto);
  }

  @Delete(':id')
  @Auditar('usuario')
  remover(@Req() req: any, @Param('id') id: string) {
    return this.usuarios.remover(req.db, id);
  }
}
