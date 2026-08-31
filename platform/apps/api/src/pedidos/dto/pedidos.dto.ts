import { OCORRENCIAS_RECEBIMENTO, REGEX_CHAVE_NFE } from '@trino/contratos';
import { Type } from 'class-transformer';
import {
  ArrayMinSize,
  IsArray,
  IsDateString,
  IsIn,
  IsNumber,
  IsOptional,
  IsString,
  IsUUID,
  Length,
  Matches,
  Min,
  ValidateNested,
} from 'class-validator';

export class ItemPedidoDto {
  @IsUUID()
  itemRequisicaoId: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  precoUnitario: number;
}

export class EmitirPedidoDto {
  @IsUUID()
  requisicaoId: string;

  @IsUUID()
  fornecedorId: string;

  @IsOptional() @IsUUID()
  cotacaoId?: string;

  @IsArray() @ArrayMinSize(1) @ValidateNested({ each: true }) @Type(() => ItemPedidoDto)
  itens: ItemPedidoDto[];

  @IsString() @Length(1, 60)
  condicaoPagamento: string;

  @IsDateString({}, { message: 'prazoEntrega deve ser uma data ISO (AAAA-MM-DD).' })
  prazoEntrega: string;
}

export class NotaFiscalDto {
  /** Chave da NF-e: exatamente 44 dígitos (o banco repete em ck_nfe_chave). */
  @Matches(REGEX_CHAVE_NFE, { message: 'chaveAcesso deve ter exatamente 44 dígitos numéricos.' })
  chaveAcesso: string;

  @IsString() @Length(1, 20)
  numero: string;

  @IsString() @Length(1, 5)
  serie: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 2 }) @Min(0)
  valorTotal: number;

  @IsDateString()
  dataEmissao: string;

  @IsOptional() @IsString()
  arquivoXmlUri?: string;
}

// Vem do pacote compartilhado — a mesma lista do frontend e do banco.
export const OCORRENCIAS = OCORRENCIAS_RECEBIMENTO;

export class LinhaRecebimentoDto {
  @IsUUID()
  itemPedidoId: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0.0001)
  qtdRecebida: number;

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  qtdAvariada?: number;

  @IsOptional() @IsIn(OCORRENCIAS)
  ocorrencia?: (typeof OCORRENCIAS)[number];

  @IsOptional() @IsString() @Length(1, 2000)
  descricaoOcorrencia?: string;
}

export class RegistrarRecebimentoDto {
  @IsArray() @ArrayMinSize(1) @ValidateNested({ each: true }) @Type(() => LinhaRecebimentoDto)
  itens: LinhaRecebimentoDto[];

  @IsOptional() @ValidateNested() @Type(() => NotaFiscalDto)
  notaFiscal?: NotaFiscalDto;

  @IsOptional() @IsString() @Length(1, 2000)
  observacao?: string;
}
