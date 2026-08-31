import { Type } from 'class-transformer';
import {
  IsBoolean,
  IsDateString,
  IsIn,
  IsInt,
  IsNumber,
  IsOptional,
  IsString,
  IsUUID,
  Length,
  Max,
  Min,
} from 'class-validator';

export class CriarRegraAlcadaDto {
  @Type(() => Number) @IsInt() @Min(1) @Max(9)
  nivel: number;

  @IsString() @Length(1, 60)
  papelExigido: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 2 }) @Min(0)
  valorMin: number;

  /** Ausente = sem teto (nível topo). A faixa é valorMin <= valor < valorMax. */
  @IsOptional() @Type(() => Number) @IsNumber({ maxDecimalPlaces: 2 }) @Min(0)
  valorMax?: number;

  @IsDateString({}, { message: 'vigenciaInicio deve ser uma data ISO (AAAA-MM-DD).' })
  vigenciaInicio: string;

  @IsOptional() @IsDateString({}, { message: 'vigenciaFim deve ser uma data ISO (AAAA-MM-DD).' })
  vigenciaFim?: string;
}

export class DefinirAprovadorDto {
  @IsUUID()
  centroCustoId: string;

  @IsUUID()
  usuarioId: string;

  @Type(() => Number) @IsInt() @Min(1) @Max(9)
  nivel: number;

  @IsOptional() @Type(() => Number) @IsInt() @Min(1) @Max(99)
  ordem?: number;

  @IsOptional() @IsDateString()
  vigenciaInicio?: string;

  @IsOptional() @IsDateString()
  vigenciaFim?: string;
}

export class CriarDelegacaoDto {
  @IsUUID()
  deleganteId: string;

  @IsUUID()
  delegadoId: string;

  /** Ausente = vale para qualquer centro de custo do delegante. */
  @IsOptional() @IsUUID()
  centroCustoId?: string;

  @IsString() @Length(3, 255)
  motivo: string;

  @IsDateString({}, { message: 'vigenciaInicio deve ser um instante ISO.' })
  vigenciaInicio: string;

  @IsDateString({}, { message: 'vigenciaFim deve ser um instante ISO.' })
  vigenciaFim: string;
}

export class EncerrarDelegacaoDto {
  @IsBoolean()
  ativa: boolean;
}

export class AbrirInstanciaDto {
  @IsUUID()
  requisicaoId: string;

  @IsUUID()
  centroCustoId: string;

  @IsUUID()
  solicitanteId: string;

  @IsOptional() @IsUUID()
  compradorId?: string;

  @Type(() => Number) @IsNumber({ maxDecimalPlaces: 2 }) @Min(0)
  valorBase: number;
}

export class DecidirEtapaDto {
  @IsIn(['APROVADO', 'REJEITADO'], { message: 'decisao deve ser APROVADO ou REJEITADO.' })
  decisao: 'APROVADO' | 'REJEITADO';

  /** Obrigatório em rejeição — o banco também exige (ck_etapa_motivo_rejeicao). */
  @IsOptional() @IsString() @Length(1, 2000)
  comentario?: string;

  /**
   * R09 — autorização explícita do estouro de orçamento. Só o aprovador final
   * pode enviar; sem isso, ele não consegue aprovar uma requisição estourada.
   */
  @IsOptional() @IsBoolean()
  autorizarEstouro?: boolean;
}
