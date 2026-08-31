import { BadRequestException, Injectable, NotFoundException } from '@nestjs/common';
import { ClientEscopado, TenantScopeError } from '@trino/db';
import { AuthService } from '../auth/auth.service';

// Resposta pública de usuário — senha_hash e mfa_secret nunca saem da API.
const CAMPOS_PUBLICOS = {
  id: true,
  nome: true,
  email: true,
  cargoFuncional: true,
  mfaHabilitado: true,
  ativo: true,
  ultimoLoginEm: true,
  criadoEm: true,
  atualizadoEm: true,
} as const;

export interface CriarUsuarioDto {
  nome: string;
  email: string;
  senha: string;
  cargoFuncional?: string;
}

export interface AtualizarUsuarioDto {
  nome?: string;
  cargoFuncional?: string;
  ativo?: boolean;
}

/**
 * Todas as operações recebem o client já escopado ao tenant do token
 * (request.db) — este service nunca vê tenantId e nunca poderia vazar dados
 * de outro grupo.
 */
@Injectable()
export class UsuariosService {
  listar(db: ClientEscopado) {
    return db.usuario.findMany({ select: CAMPOS_PUBLICOS, orderBy: { nome: 'asc' } });
  }

  async criar(db: ClientEscopado, dto: CriarUsuarioDto) {
    const email = (dto?.email ?? '').trim().toLowerCase();
    if (!dto?.nome || !email || !dto?.senha) {
      throw new BadRequestException({ codigo: 'USR-ERR-001', mensagem: 'Informe nome, email e senha.' });
    }
    if (dto.senha.length < 8) {
      throw new BadRequestException({ codigo: 'USR-ERR-002', mensagem: 'A senha precisa de ao menos 8 caracteres.' });
    }
    const senhaHash = await AuthService.hashSenha(dto.senha);
    try {
      return await db.usuario.create({
        data: { nome: dto.nome, email, senhaHash, cargoFuncional: dto.cargoFuncional ?? null },
        select: CAMPOS_PUBLICOS,
      });
    } catch (e: any) {
      if (e?.code === 'P2002') {
        throw new BadRequestException({ codigo: 'USR-ERR-003', mensagem: 'Já existe usuário com este email no tenant.' });
      }
      throw e;
    }
  }

  async atualizar(db: ClientEscopado, id: string, dto: AtualizarUsuarioDto) {
    try {
      return await db.usuario.update({
        where: { id },
        data: {
          ...(dto?.nome !== undefined ? { nome: dto.nome } : {}),
          ...(dto?.cargoFuncional !== undefined ? { cargoFuncional: dto.cargoFuncional } : {}),
          ...(dto?.ativo !== undefined ? { ativo: dto.ativo } : {}),
        },
        select: CAMPOS_PUBLICOS,
      });
    } catch (e) {
      throw this.comoNaoEncontrado(e);
    }
  }

  async remover(db: ClientEscopado, id: string) {
    try {
      await db.usuario.delete({ where: { id } });
      return { id, removido: true };
    } catch (e) {
      throw this.comoNaoEncontrado(e);
    }
  }

  /** Registro de outro tenant (TENANT-ERR-404) responde como inexistente. */
  private comoNaoEncontrado(e: unknown) {
    if (e instanceof TenantScopeError || (e as any)?.code === 'P2025') {
      return new NotFoundException({ codigo: 'USR-ERR-404', mensagem: 'Usuário não encontrado neste tenant.' });
    }
    return e;
  }
}
