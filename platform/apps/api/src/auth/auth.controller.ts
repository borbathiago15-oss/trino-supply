import { Body, Controller, Get, Post, Req, UseGuards } from '@nestjs/common';
import { AuthService, LoginDto } from './auth.service';
import { JwtAuthGuard } from './jwt-auth.guard';
import { TenantGuard } from './tenant.guard';

@Controller('auth')
export class AuthController {
  constructor(private readonly auth: AuthService) {}

  /** Login real: cnpj do tenant + email + senha contra core.usuario. */
  @Post('login')
  login(@Body() dto: LoginDto) {
    return this.auth.login(dto);
  }

  /** Identidade extraída do token — útil para o front e para provar o payload. */
  @UseGuards(JwtAuthGuard, TenantGuard)
  @Get('me')
  me(@Req() req: any) {
    return { usuario: req.user, tenantId: req.tenantId };
  }
}
