import { Type } from 'class-transformer';
import {
  IsBoolean,
  IsDateString,
  IsEmail,
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

// Espelham os enums do DDL (fornecimento.status_homologacao / tipo_documento).
export const STATUS_HOMOLOGACAO = ['PENDENTE', 'HOMOLOGADO', 'SUSPENSO', 'BLOQUEADO'] as const;
export type StatusHomologacao = (typeof STATUS_HOMOLOGACAO)[number];

export const TIPOS_DOCUMENTO = [
  'CND_FEDERAL', 'CND_ESTADUAL', 'CND_MUNICIPAL', 'FGTS', 'TRABALHISTA',
  'CONTRATO_SOCIAL', 'ALVARA', 'CERTIFICADO_ISO', 'APOLICE_SEGURO', 'OUTRO',
] as const;
export type TipoDocumento = (typeof TIPOS_DOCUMENTO)[number];

export class CriarFornecedorDto {
  @Matches(/^[0-9]{14}$/, { message: 'cnpj deve ter 14 dígitos (sem pontuação).' })
  cnpj: string;

  @IsString() @Length(1, 200)
  razaoSocial: string;

  @IsOptional() @IsString() @Length(1, 200)
  nomeFantasia?: string;

  @IsOptional() @IsEmail({}, { message: 'emailContato inválido.' }) @Length(1, 255)
  emailContato?: string;

  @IsOptional() @IsString() @Length(8, 20)
  telefone?: string;
}

export class AtualizarFornecedorDto {
  @IsOptional() @IsString() @Length(1, 200)
  razaoSocial?: string;

  @IsOptional() @IsString() @Length(1, 200)
  nomeFantasia?: string;

  @IsOptional() @IsEmail({}, { message: 'emailContato inválido.' }) @Length(1, 255)
  emailContato?: string;

  @IsOptional() @IsString() @Length(8, 20)
  telefone?: string;

  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 2 }) @Min(0) @Max(100)
  scoreDesempenho?: number;
}

export class AlterarStatusDto {
  @IsEnum(STATUS_HOMOLOGACAO as unknown as object, {
    message: `status deve ser um de: ${STATUS_HOMOLOGACAO.join(', ')}`,
  })
  status: StatusHomologacao;

  @IsOptional() @IsString() @Length(1, 255)
  motivo?: string;
}

export class RegistrarDocumentoDto {
  @IsEnum(TIPOS_DOCUMENTO as unknown as object, {
    message: `tipo deve ser um de: ${TIPOS_DOCUMENTO.join(', ')}`,
  })
  tipo: TipoDocumento;

  @IsOptional() @IsString() @Length(1, 60)
  numero?: string;

  @IsDateString({}, { message: 'dataEmissao deve ser uma data ISO (AAAA-MM-DD).' })
  dataEmissao: string;

  @IsDateString({}, { message: 'dataValidade deve ser uma data ISO (AAAA-MM-DD).' })
  dataValidade: string;

  /** Documento obrigatório vencido bloqueia a elegibilidade do fornecedor. */
  @IsOptional() @IsBoolean()
  obrigatorio?: boolean;

  @IsOptional() @IsString() @Length(1, 2048)
  arquivoUri?: string;
}

export class RegistrarCaDto {
  @IsUUID()
  varianteId: string;

  @Matches(/^[0-9A-Za-z.\-\/]{1,20}$/, { message: 'numeroCa inválido.' })
  numeroCa: string;

  @IsDateString({}, { message: 'dataValidade deve ser uma data ISO (AAAA-MM-DD).' })
  dataValidade: string;

  @IsOptional() @IsString() @Length(1, 2048)
  arquivoUri?: string;
}

export class VincularSkuDto {
  @IsUUID()
  varianteId: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 4 }) @Min(0)
  ultimoPreco: number;

  @IsOptional() @IsString() @Length(3, 3)
  moeda?: string;

  @IsOptional() @Type(() => Number) @IsInt() @Min(0) @Max(32767)
  leadTimeDias?: number;
}
