import { CanActivate, ExecutionContext, ForbiddenException, Injectable } from '@nestjs/common';
import { forTenant } from '@trino/db';
import { PrismaService } from '../prisma/prisma.service';
import { UsuarioAutenticado } from './jwt.strategy';

/**
 * TenantGuard — o tenantId vem EXCLUSIVAMENTE do payload do token JWT
 * (request.user, montado pela JwtStrategy a partir do token assinado).
 * Headers como x-tenant-id, body ou query string são ignorados por construção:
 * este guard nunca os lê. Com o tenant do token, anexa à requisição:
 *   - request.tenantId
 *   - request.db = forTenant(prisma, tenantId)  ← único client que os
 *     controllers/services de aplicação devem usar.
 * Deve ser usado SEMPRE depois do JwtAuthGuard.
 */
@Injectable()
export class TenantGuard implements CanActivate {
  constructor(private readonly prisma: PrismaService) {}

  canActivate(context: ExecutionContext): boolean {
    const request = context.switchToHttp().getRequest();
    const usuario = request.user as UsuarioAutenticado | undefined;

    if (!usuario?.tenantId) {
      throw new ForbiddenException({
        codigo: 'TENANT-ERR-005',
        mensagem: 'Requisição sem tenant no token — autentique-se novamente.',
      });
    }

    request.tenantId = usuario.tenantId;
    request.db = forTenant(this.prisma, usuario.tenantId);
    return true;
  }
}
