import { Controller, Get, Module, ValidationPipe } from '@nestjs/common';
import { APP_INTERCEPTOR, APP_PIPE } from '@nestjs/core';
import { AuditInterceptor } from './audit/audit.interceptor';
import { AprovacoesModule } from './aprovacoes/aprovacoes.module';
import { AuthModule } from './auth/auth.module';
import { CotacoesModule } from './cotacoes/cotacoes.module';
import { PedidosModule } from './pedidos/pedidos.module';
import { CatalogoModule } from './catalogo/catalogo.module';
import { FornecedoresModule } from './fornecedores/fornecedores.module';
import { ImportacaoModule } from './importacao/importacao.module';
import { PrismaModule } from './prisma/prisma.module';
import { SlaModule } from './sla/sla.module';
import { RequisicoesModule } from './requisicoes/requisicoes.module';
import { UsuariosModule } from './usuarios/usuarios.module';

@Controller('health')
class HealthController {
  @Get()
  health() {
    return { status: 'ok', servico: 'trino-platform-api', fase: 'F8' };
  }
}

@Module({
  imports: [PrismaModule, AuthModule, UsuariosModule, CatalogoModule, FornecedoresModule, AprovacoesModule, RequisicoesModule, CotacoesModule, PedidosModule, SlaModule, ImportacaoModule],
  controllers: [HealthController],
  providers: [
    // Auditoria é GLOBAL: qualquer handler mutante marcado com @Auditar passa
    // pelo AuditInterceptor, em qualquer módulo presente ou futuro.
    { provide: APP_INTERCEPTOR, useClass: AuditInterceptor },
    // Validação GLOBAL com class-validator: campo desconhecido no payload é
    // erro (400), não algo silenciosamente ignorado.
    {
      provide: APP_PIPE,
      useValue: new ValidationPipe({
        whitelist: true,
        forbidNonWhitelisted: true,
        transform: true,
        transformOptions: { enableImplicitConversion: false },
      }),
    },
  ],
})
export class AppModule {}
