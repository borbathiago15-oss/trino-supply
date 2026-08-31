import { Injectable, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { PrismaClient } from '@trino/db';

/**
 * Client Prisma BASE (sem escopo de tenant). Uso direto é restrito a:
 *  - resolução de tenant no login (tenant é catálogo global);
 *  - bootstrap/seed e jobs administrativos.
 * Todo acesso de aplicação em rotas autenticadas passa por request.db,
 * que o TenantGuard cria com forTenant(prisma, tenantId-do-token).
 */
@Injectable()
export class PrismaService extends PrismaClient implements OnModuleInit, OnModuleDestroy {
  async onModuleInit() {
    await this.$connect();
  }

  async onModuleDestroy() {
    await this.$disconnect();
  }
}
