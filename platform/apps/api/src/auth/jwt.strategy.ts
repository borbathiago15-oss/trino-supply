import { Injectable, UnauthorizedException } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { ExtractJwt, Strategy } from 'passport-jwt';

export interface UsuarioAutenticado {
  userId: string;
  tenantId: string;
  email: string;
  nome: string;
}

@Injectable()
export class JwtStrategy extends PassportStrategy(Strategy, 'jwt') {
  constructor() {
    super({
      jwtFromRequest: ExtractJwt.fromAuthHeaderAsBearerToken(),
      ignoreExpiration: false,
      secretOrKey: process.env.JWT_SECRET as string,
    });
  }

  /**
   * O que sai daqui vira request.user. O tenantId nasce AQUI, do payload
   * assinado — nenhuma outra fonte (header, body, query) é consultada.
   */
  validate(payload: { sub?: string; tenantId?: string; email?: string; nome?: string }): UsuarioAutenticado {
    if (!payload?.sub || !payload?.tenantId) {
      throw new UnauthorizedException({
        codigo: 'AUTH-ERR-003',
        mensagem: 'Token sem identidade ou sem tenant.',
      });
    }
    return {
      userId: payload.sub,
      tenantId: payload.tenantId,
      email: payload.email ?? '',
      nome: payload.nome ?? '',
    };
  }
}
