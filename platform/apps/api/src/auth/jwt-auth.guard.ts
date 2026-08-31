import { Injectable } from '@nestjs/common';
import { AuthGuard } from '@nestjs/passport';

/** Exige Bearer token JWT válido (assinatura + expiração) em toda rota protegida. */
@Injectable()
export class JwtAuthGuard extends AuthGuard('jwt') {}
