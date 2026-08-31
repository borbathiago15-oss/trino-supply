import { Controller, Get, Module } from '@nestjs/common';
import { APP_INTERCEPTOR } from '@nestjs/core';
import { AuditInterceptor } from './audit/audit.interceptor';
import { AuthModule } from './auth/auth.module';
import { PrismaModule } from './prisma/prisma.module';
import { UsuariosModule } from './usuarios/usuarios.module';

@Controller('health')
class HealthController {
  @Get()
  health() {
    return { status: 'ok', servico: 'trino-platform-api', fase: 'F1' };
  }
}

@Module({
  imports: [PrismaModule, AuthModule, UsuariosModule],
  controllers: [HealthController],
  providers: [
    // Auditoria é GLOBAL: qualquer handler mutante marcado com @Auditar passa
    // pelo AuditInterceptor, em qualquer módulo presente ou futuro.
    { provide: APP_INTERCEPTOR, useClass: AuditInterceptor },
  ],
})
export class AppModule {}
