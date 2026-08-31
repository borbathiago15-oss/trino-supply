import { Body, Controller, Get, Param, ParseUUIDPipe, Patch, Post, Put, Query, Req, UseGuards } from '@nestjs/common';
import { Auditar } from '../audit/auditar.decorator';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import { CatalogoService } from './catalogo.service';
import {
  AjustarSaldoDto,
  AtualizarFamiliaDto,
  AtualizarSkuBaseDto,
  AtualizarTipoProdutoDto,
  AtualizarVarianteDto,
  CriarFamiliaDto,
  CriarSaldoDto,
  CriarSkuBaseDto,
  CriarTipoProdutoDto,
  CriarVarianteDto,
  DefinirParametroEstoqueDto,
} from './dto/catalogo.dto';

// Query params opcionais também são UUID: string inválida vira 400, não 500.
const UuidOpcional = new ParseUUIDPipe({ optional: true });

/**
 * Catálogo de materiais — hierarquia família → tipo → SKU base → variante,
 * mais ROP e saldo. Toda rota exige JWT e opera pelo client escopado; as
 * mutações são auditadas com snapshot antes/depois.
 */
@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('catalogo')
export class CatalogoController {
  constructor(private readonly catalogo: CatalogoService) {}

  /**
   * Centros de custo do tenant — leitura simples, usada pelo seletor de
   * contexto do frontend e pelos filtros da esteira.
   */
  @Get('centros-custo')
  listarCentrosCusto(@Req() req: any) {
    return req.db.centroCusto.findMany({ where: { ativo: true }, orderBy: { codigo: 'asc' } });
  }

  @Get('familias')
  listarFamilias(@Req() req: any) {
    return this.catalogo.listarFamilias(req.db);
  }

  @Post('familias')
  @Auditar('familia_produto', 'familiaProduto')
  criarFamilia(@Req() req: any, @Body() dto: CriarFamiliaDto) {
    return this.catalogo.criarFamilia(req.db, dto);
  }

  @Patch('familias/:id')
  @Auditar('familia_produto', 'familiaProduto')
  atualizarFamilia(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AtualizarFamiliaDto) {
    return this.catalogo.atualizarFamilia(req.db, id, dto);
  }

  @Get('tipos')
  listarTipos(@Req() req: any, @Query('familiaId', UuidOpcional) familiaId?: string) {
    return this.catalogo.listarTipos(req.db, familiaId);
  }

  @Post('tipos')
  @Auditar('tipo_produto', 'tipoProduto')
  criarTipo(@Req() req: any, @Body() dto: CriarTipoProdutoDto) {
    return this.catalogo.criarTipo(req.db, dto);
  }

  @Patch('tipos/:id')
  @Auditar('tipo_produto', 'tipoProduto')
  atualizarTipo(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AtualizarTipoProdutoDto) {
    return this.catalogo.atualizarTipo(req.db, id, dto);
  }

  @Get('skus')
  listarSkusBase(@Req() req: any, @Query('tipoProdutoId', UuidOpcional) tipoProdutoId?: string) {
    return this.catalogo.listarSkusBase(req.db, tipoProdutoId);
  }

  @Post('skus')
  @Auditar('sku_base', 'skuBase')
  criarSkuBase(@Req() req: any, @Body() dto: CriarSkuBaseDto) {
    return this.catalogo.criarSkuBase(req.db, dto);
  }

  @Patch('skus/:id')
  @Auditar('sku_base', 'skuBase')
  atualizarSkuBase(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AtualizarSkuBaseDto) {
    return this.catalogo.atualizarSkuBase(req.db, id, dto);
  }

  @Get('variantes')
  listarVariantes(@Req() req: any, @Query('skuBaseId', UuidOpcional) skuBaseId?: string) {
    return this.catalogo.listarVariantes(req.db, skuBaseId);
  }

  @Post('variantes')
  @Auditar('variante_sku', 'varianteSku')
  criarVariante(@Req() req: any, @Body() dto: CriarVarianteDto) {
    return this.catalogo.criarVariante(req.db, dto);
  }

  @Patch('variantes/:id')
  @Auditar('variante_sku', 'varianteSku')
  atualizarVariante(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AtualizarVarianteDto) {
    return this.catalogo.atualizarVariante(req.db, id, dto);
  }

  @Get('parametros-estoque')
  listarParametros(@Req() req: any, @Query('centroCustoId', UuidOpcional) centroCustoId?: string) {
    return this.catalogo.listarParametros(req.db, centroCustoId);
  }

  /** Define/atualiza o ROP da variante no centro de custo (um por par). */
  @Put('parametros-estoque')
  @Auditar('parametro_estoque', 'parametroEstoque')
  definirParametro(@Req() req: any, @Body() dto: DefinirParametroEstoqueDto) {
    return this.catalogo.definirParametro(req.db, dto);
  }

  @Get('saldos')
  listarSaldos(@Req() req: any, @Query('centroCustoId', UuidOpcional) centroCustoId?: string) {
    return this.catalogo.listarSaldos(req.db, centroCustoId);
  }

  @Post('saldos')
  @Auditar('saldo_estoque', 'saldoEstoque')
  criarSaldo(@Req() req: any, @Body() dto: CriarSaldoDto) {
    return this.catalogo.criarSaldo(req.db, dto);
  }

  /** Ajuste com bloqueio otimista: exige a `version` lida. */
  @Patch('saldos/:id')
  @Auditar('saldo_estoque', 'saldoEstoque')
  ajustarSaldo(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AjustarSaldoDto) {
    return this.catalogo.ajustarSaldo(req.db, id, dto);
  }
}
