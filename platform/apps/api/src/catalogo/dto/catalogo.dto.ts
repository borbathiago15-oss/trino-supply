import { Type } from 'class-transformer';
import {
  IsBoolean,
  IsEnum,
  IsInt,
  IsNumber,
  IsOptional,
  IsString,
  IsUUID,
  Length,
  Matches,
  Max,
  Min,
} from 'class-validator';

// Espelha catalogo.unidade_medida — a lista vem do DDL, não de convenção.
export const UNIDADES_MEDIDA = [
  'UN', 'CX', 'PC', 'PAR', 'KG', 'G', 'L', 'ML', 'M', 'M2', 'M3', 'RL', 'FD',
] as const;
export type UnidadeMedida = (typeof UNIDADES_MEDIDA)[number];

export class CriarFamiliaDto {
  @IsString() @Length(1, 20)
  codigo: string;

  @IsString() @Length(1, 150)
  nome: string;

  @IsOptional() @Type(() => Number) @IsInt() @Min(0) @Max(32767)
  leadTimeMedioDias?: number;
}

export class AtualizarFamiliaDto {
  @IsOptional() @IsString() @Length(1, 150)
  nome?: string;

  @IsOptional() @Type(() => Number) @IsInt() @Min(0) @Max(32767)
  leadTimeMedioDias?: number;

  @IsOptional() @IsBoolean()
  ativo?: boolean;
}

export class CriarTipoProdutoDto {
  @IsUUID()
  familiaId: string;

  @IsString() @Length(1, 20)
  codigo: string;

  @IsString() @Length(1, 150)
  nome: string;
}

export class AtualizarTipoProdutoDto {
  @IsOptional() @IsString() @Length(1, 150)
  nome?: string;

  @IsOptional() @IsBoolean()
  ativo?: boolean;
}

export class CriarSkuBaseDto {
  @IsUUID()
  tipoProdutoId: string;

  @IsString() @Length(1, 40)
  codigo: string;

  @IsString() @Length(1, 255)
  descricao: string;

  @IsEnum(UNIDADES_MEDIDA as unknown as object, {
    message: `unidadeMedida deve ser uma de: ${UNIDADES_MEDIDA.join(', ')}`,
  })
  unidadeMedida: UnidadeMedida;

  /** EPI: quando true, o fornecimento exige CA válido para a variante. */
  @IsOptional() @IsBoolean()
  exigeCa?: boolean;
}

export class AtualizarSkuBaseDto {
  @IsOptional() @IsString() @Length(1, 255)
  descricao?: string;

  @IsOptional() @IsBoolean()
  exigeCa?: boolean;

  @IsOptional() @IsBoolean()
  ativo?: boolean;
}

export class CriarVarianteDto {
  @IsUUID()
  skuBaseId: string;

  @IsString() @Length(1, 50)
  codigo: string;

  @IsOptional() @IsString() @Length(1, 40)
  grade?: string;

  @IsOptional() @IsString() @Length(1, 20)
  tamanho?: string;

  @IsOptional() @IsString() @Length(1, 40)
  cor?: string;

  @IsOptional() @IsString() @Matches(/^[0-9]{8,20}$/, { message: 'codigoBarras deve ter de 8 a 20 dígitos.' })
  codigoBarras?: string;
}

export class AtualizarVarianteDto {
  @IsOptional() @IsString() @Length(1, 40)
  grade?: string;

  @IsOptional() @IsString() @Length(1, 20)
  tamanho?: string;

  @IsOptional() @IsString() @Length(1, 40)
  cor?: string;

  @IsOptional() @IsString() @Matches(/^[0-9]{8,20}$/, { message: 'codigoBarras deve ter de 8 a 20 dígitos.' })
  codigoBarras?: string;

  @IsOptional() @IsBoolean()
  ativo?: boolean;
}

export class DefinirParametroEstoqueDto {
  @IsUUID()
  varianteId: string;

  @IsUUID()
  centroCustoId: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  pontoPedidoRop: number;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  estoqueSeguranca: number;

  @IsOptional() @Type(() => Number) @IsInt() @Min(0) @Max(32767)
  leadTimeDias?: number;

  @IsOptional() @IsBoolean()
  gerarScAutomatica?: boolean;
}

/**
 * Ajuste de saldo com bloqueio otimista: o chamador informa a versão que leu.
 * Divergiu → 409, ninguém sobrescreve leitura velha.
 */
export class AjustarSaldoDto {
  @Type(() => Number) @IsInt() @Min(0)
  version: number;

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  qtdDisponivel?: number;

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  qtdReservada?: number;
}

export class CriarSaldoDto {
  @IsUUID()
  varianteId: string;

  @IsUUID()
  centroCustoId: string;

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  qtdDisponivel?: number;

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  qtdReservada?: number;
}
