import { Type } from 'class-transformer';
import {
  IsDateString,
  IsIn,
  IsInt,
  IsNumber,
  IsOptional,
  IsString,
  IsUUID,
  Length,
  Min,
} from 'class-validator';

export const PRIORIDADES = ['BAIXA', 'NORMAL', 'ALTA', 'EMERGENCIAL'] as const;
export type Prioridade = (typeof PRIORIDADES)[number];

export class CriarRequisicaoDto {
  @IsUUID()
  centroCustoId: string;

  @IsOptional() @IsUUID()
  contratoId?: string;

  @IsOptional() @IsIn(PRIORIDADES, { message: `prioridade deve ser uma de: ${PRIORIDADES.join(', ')}` })
  prioridade?: Prioridade;

  @IsOptional() @IsString() @Length(1, 4000)
  justificativa?: string;

  @IsOptional() @IsDateString({}, { message: 'dataNecessidade deve ser uma data ISO (AAAA-MM-DD).' })
  dataNecessidade?: string;
}

export class EditarRequisicaoDto {
  /** Versão lida pelo cliente — bloqueio otimista. */
  @IsOptional() @Type(() => Number) @IsInt() @Min(0)
  version?: number;

  @IsOptional() @IsString() @Length(1, 4000)
  justificativa?: string;

  @IsOptional() @IsIn(PRIORIDADES)
  prioridade?: Prioridade;

  @IsOptional() @IsDateString()
  dataNecessidade?: string;
}

export class AdicionarItemDto {
  @IsUUID()
  varianteId: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0.0001)
  quantidade: number;

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  precoReferencia?: number;

  @IsOptional() @IsString() @Length(1, 500)
  observacao?: string;
}

export class ComandoSimplesDto {
  @IsOptional() @Type(() => Number) @IsInt() @Min(0)
  version?: number;

  @IsOptional() @IsString() @Length(1, 4000)
  justificativa?: string;
}

export class ComandoComMotivoDto {
  @IsOptional() @Type(() => Number) @IsInt() @Min(0)
  version?: number;

  @IsString() @Length(3, 4000)
  motivo: string;
}

export class AssumirTriagemDto {
  @IsOptional() @Type(() => Number) @IsInt() @Min(0)
  version?: number;

  /** Ausente = quem está chamando assume. */
  @IsOptional() @IsUUID()
  compradorId?: string;
}

export class CancelarDto {
  @IsOptional() @Type(() => Number) @IsInt() @Min(0)
  version?: number;

  @IsOptional() @IsString() @Length(3, 4000)
  motivo?: string;
}
