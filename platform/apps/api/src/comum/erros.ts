import { BadRequestException, ConflictException, NotFoundException } from '@nestjs/common';
import { TenantScopeError } from '@trino/db';

/**
 * Tradução única dos erros do Prisma/escopo para respostas da API.
 * Códigos são estáveis: o front e os testes dependem deles.
 */
export function conflitoDeUnicidade(codigo: string, mensagem: string) {
  return new ConflictException({ codigo, mensagem });
}

export function naoEncontrado(codigo: string, mensagem: string) {
  return new NotFoundException({ codigo, mensagem });
}

export function requisicaoInvalida(codigo: string, mensagem: string) {
  return new BadRequestException({ codigo, mensagem });
}

/**
 * Registro de outro tenant e registro inexistente respondem igual — 404 —
 * para não revelar a existência de dados de outro grupo.
 */
export function ehNaoEncontrado(e: unknown): boolean {
  return e instanceof TenantScopeError || (e as { code?: string })?.code === 'P2025';
}

export function ehViolacaoDeUnicidade(e: unknown): boolean {
  return (e as { code?: string })?.code === 'P2002';
}

export function ehViolacaoDeCheck(e: unknown): boolean {
  // P2010/P2034 e afins chegam como erro bruto do Postgres (23514 = check_violation).
  const meta = (e as { meta?: { code?: string } })?.meta;
  return meta?.code === '23514' || String((e as Error)?.message ?? '').includes('violates check constraint');
}
