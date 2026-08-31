import { BadRequestException, Injectable, UnauthorizedException } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';
import * as argon2 from 'argon2';
import { forTenant } from '@trino/db';
import { PrismaService } from '../prisma/prisma.service';

export interface LoginDto {
  cnpj: string;
  email: string;
  senha: string;
}

// Hash de sacrifício: quando o usuário não existe, ainda pagamos o custo de um
// argon2.verify para não denunciar a existência da conta pelo tempo de resposta.
const HASH_SACRIFICIO =
  '$argon2id$v=19$m=65536,t=3,p=4$c2FsdHNhbHRzYWx0c2FsdA$SPY1QUpEC8jSCVL5uGmMOUlxrCFR1AmY7Jek2XFj6t0';

@Injectable()
export class AuthService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly jwt: JwtService,
  ) {}

  /**
   * Login real contra core.usuario: tenant por CNPJ + usuário por (tenant, email)
   * + argon2.verify contra senha_hash. Não existe usuário assumido ("admin"):
   * quem não está na tabela, não entra.
   * Erro é sempre AUTH-ERR-001 genérico — não enumeramos contas nem tenants.
   */
  async login(dto: LoginDto) {
    const cnpj = (dto?.cnpj ?? '').replace(/\D/g, '');
    const email = (dto?.email ?? '').trim().toLowerCase();
    const senha = dto?.senha ?? '';
    if (!cnpj || !email || !senha) {
      throw new BadRequestException({
        codigo: 'AUTH-ERR-002',
        mensagem: 'Informe cnpj, email e senha.',
      });
    }

    const tenant = await this.prisma.tenant.findUnique({ where: { cnpj } });
    const usuario =
      tenant && tenant.ativo
        ? await this.prisma.usuario.findUnique({
            where: { tenantId_email: { tenantId: tenant.id, email } },
          })
        : null;

    const hashParaVerificar = usuario?.senhaHash ?? HASH_SACRIFICIO;
    let senhaConfere = false;
    try {
      senhaConfere = await argon2.verify(hashParaVerificar, senha);
    } catch {
      senhaConfere = false;
    }

    if (!usuario || !usuario.ativo || !senhaConfere) {
      throw new UnauthorizedException({
        codigo: 'AUTH-ERR-001',
        mensagem: 'Credenciais inválidas.',
      });
    }

    // ultimo_login_em via client escopado — coerente com a regra "toda escrita
    // de aplicação passa pelo forTenant".
    const db = forTenant(this.prisma, usuario.tenantId);
    await db.usuario.update({
      where: { id: usuario.id },
      data: { ultimoLoginEm: new Date() },
    });

    const expiraEm = process.env.JWT_EXPIRA_EM ?? '8h';
    const payload = {
      sub: usuario.id,
      tenantId: usuario.tenantId,
      email: usuario.email,
      nome: usuario.nome,
    };

    return {
      tokenAcesso: await this.jwt.signAsync(payload, { expiresIn: expiraEm as `${number}h` }),
      tipoToken: 'Bearer',
      expiraEm,
      usuario: {
        id: usuario.id,
        nome: usuario.nome,
        email: usuario.email,
        cargoFuncional: usuario.cargoFuncional,
      },
    };
  }

  /** Custo padrão do projeto para hash de senha (argon2id). */
  static hashSenha(senha: string): Promise<string> {
    return argon2.hash(senha, { type: argon2.argon2id });
  }
}
