import { Body, Controller, Get, Param, ParseUUIDPipe, Post, Query, Req, UseGuards } from '@nestjs/common';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import { autor } from '../cotacoes/cotacoes.controller';
import { EmitirPedidoCompraUseCase, obterPedido, RegistrarRecebimentoUseCase, traduzirConciliacao } from './casos-de-uso';
import { EmitirPedidoDto, RegistrarRecebimentoDto } from './dto/pedidos.dto';

@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('pedidos')
export class PedidosController {
  constructor(
    private readonly emitir: EmitirPedidoCompraUseCase,
    private readonly receber: RegistrarRecebimentoUseCase,
  ) {}

  @Get()
  listar(@Req() req: any, @Query('status') status?: string, @Query('fornecedorId') fornecedorId?: string) {
    return req.db.pedidoCompra.findMany({
      where: { ...(status ? { status } : {}), ...(fornecedorId ? { fornecedorId } : {}) },
      orderBy: { emitidoEm: 'desc' },
    });
  }

  @Get(':id')
  obter(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return obterPedido(req.db, id);
  }

  /** T16 — emitir pedido a partir da aprovação de alçada. */
  @Post()
  emitirPedido(@Req() req: any, @Body() dto: EmitirPedidoDto) {
    return this.emitir.executar(req.db, dto, autor(req));
  }

  /** Recebimento com conciliação 3-way (Pedido × NF × Recebido). */
  @Post(':id/recebimentos')
  async registrarRecebimento(
    @Req() req: any,
    @Param('id', ParseUUIDPipe) id: string,
    @Body() dto: RegistrarRecebimentoDto,
  ) {
    try {
      return await this.receber.executar(req.db, id, dto, autor(req));
    } catch (e) {
      throw traduzirConciliacao(e);
    }
  }
}
