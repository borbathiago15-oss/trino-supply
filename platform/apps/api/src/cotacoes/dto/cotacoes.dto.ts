import { Type } from 'class-transformer';
import {
  ArrayMinSize,
  IsArray,
  IsBoolean,
  IsDateString,
  IsInt,
  IsNumber,
  IsObject,
  IsOptional,
  IsString,
  IsUUID,
  Length,
  Min,
  ValidateNested,
} from 'class-validator';

export class AbrirCotacaoDto {
  @IsArray() @ArrayMinSize(1) @IsUUID('4', { each: true })
  itensRequisicaoIds: string[];

  @IsArray() @ArrayMinSize(1) @IsUUID('4', { each: true })
  fornecedorIds: string[];

  @IsDateString({}, { message: 'dataLimiteResposta deve ser um instante ISO.' })
  dataLimiteResposta: string;

  /** Pesos: preco + lead_time + frete + cond_pagto = 1,0000. */
  @IsOptional() @IsObject()
  criterioEqualizacao?: Record<string, number>;

  @IsOptional() @IsBoolean()
  dispensaCotacao?: boolean;

  @IsOptional() @IsString() @Length(3, 4000)
  justificativaDispensa?: string;
}

export class ItemPropostaDto {
  @IsUUID()
  rfqItemId: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  precoUnitario: number;

  @IsOptional() @IsString() @Length(1, 120)
  marca?: string;

  @IsOptional() @Type(() => Number) @IsInt() @Min(0)
  prazoItemDias?: number;
}

export class RegistrarPropostaDto {
  @IsUUID()
  fornecedorId: string;

  @IsArray() @ArrayMinSize(1) @ValidateNested({ each: true }) @Type(() => ItemPropostaDto)
  itens: ItemPropostaDto[];

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 2 }) @Min(0)
  frete?: number;

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 2 }) @Min(0)
  desconto?: number;

  @IsString() @Length(1, 60)
  condicaoPagamento: string;

  @Type(() => Number) @IsInt() @Min(0)
  prazoEntregaDias: number;

  @IsDateString({}, { message: 'validadeProposta deve ser uma data ISO (AAAA-MM-DD).' })
  validadeProposta: string;
}

export class EqualizarDto {
  /** Ausente = vence a melhor nota da matriz. */
  @IsOptional() @IsUUID()
  propostaVencedoraId?: string;

  /** Obrigatória quando a escolhida não é a de menor preço. */
  @IsOptional() @IsString() @Length(3, 4000)
  justificativaDesvio?: string;
}
