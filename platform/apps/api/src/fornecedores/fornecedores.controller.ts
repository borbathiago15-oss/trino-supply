import { Body, Controller, Get, Param, ParseUUIDPipe, Patch, Post, Query, Req, UseGuards } from '@nestjs/common';
import { Auditar } from '../audit/auditar.decorator';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import {
  AlterarStatusDto,
  AtualizarFornecedorDto,
  CriarFornecedorDto,
  RegistrarCaDto,
  RegistrarDocumentoDto,
  VincularSkuDto,
} from './dto/fornecedores.dto';
import { ElegibilidadeService } from './elegibilidade.service';
import { FornecedoresService } from './fornecedores.service';

@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('fornecedores')
export class FornecedoresController {
  constructor(
    private readonly fornecedores: FornecedoresService,
    private readonly elegibilidade: ElegibilidadeService,
  ) {}

  @Get()
  listar(@Req() req: any, @Query('status') status?: string) {
    return this.fornecedores.listar(req.db, status);
  }

  @Get(':id')
  obter(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return this.fornecedores.obter(req.db, id);
  }

  /**
   * Elegibilidade para processos de compra. `varianteId` opcional: informe o
   * item para que a checagem de CA (EPI) entre na conta.
   */
  @Get(':id/elegibilidade')
  elegibilidadeDe(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Query('varianteId', new ParseUUIDPipe({ optional: true })) varianteId?: string) {
    return this.elegibilidade.avaliar(req.db, id, varianteId);
  }

  @Post()
  @Auditar('fornecedor')
  criar(@Req() req: any, @Body() dto: CriarFornecedorDto) {
    return this.fornecedores.criar(req.db, dto);
  }

  @Patch(':id')
  @Auditar('fornecedor')
  atualizar(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AtualizarFornecedorDto) {
    return this.fornecedores.atualizar(req.db, id, dto);
  }

  @Patch(':id/status')
  @Auditar('fornecedor')
  alterarStatus(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AlterarStatusDto) {
    return this.fornecedores.alterarStatus(req.db, id, dto);
  }

  @Get(':id/documentos')
  listarDocumentos(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return this.fornecedores.listarDocumentos(req.db, id);
  }

  @Post(':id/documentos')
  @Auditar('documento_fornecedor', 'documentoFornecedor')
  registrarDocumento(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: RegistrarDocumentoDto) {
    return this.fornecedores.registrarDocumento(req.db, id, dto);
  }

  @Post(':id/certificados')
  @Auditar('certificado_aprovacao', 'certificadoAprovacao')
  registrarCa(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: RegistrarCaDto) {
    return this.fornecedores.registrarCa(req.db, id, dto);
  }

  /** Vínculo com SKU: barrado se o fornecedor não estiver elegível. */
  @Post(':id/skus')
  @Auditar('fornecedor_sku', 'fornecedorSku')
  vincularSku(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: VincularSkuDto) {
    return this.fornecedores.vincularSku(req.db, id, dto);
  }
}
