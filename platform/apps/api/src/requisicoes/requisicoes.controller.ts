import { Body, Controller, Delete, Get, Param, ParseUUIDPipe, Patch, Post, Query, Req, UseGuards } from '@nestjs/common';
import { Auditar } from '../audit/auditar.decorator';
import { JwtAuthGuard } from '../auth/jwt-auth.guard';
import { TenantGuard } from '../auth/tenant.guard';
import {
  AssumirTriagemUseCase,
  DevolverAjusteUseCase,
  ReenviarRequisicaoUseCase,
  SubmeterRequisicaoUseCase,
} from './casos-de-uso';
import {
  AdicionarItemDto,
  AssumirTriagemDto,
  CancelarDto,
  ComandoComMotivoDto,
  ComandoSimplesDto,
  CriarRequisicaoDto,
  EditarRequisicaoDto,
} from './dto/requisicoes.dto';
import { RequisicoesService } from './requisicoes.service';
import { ContextoAutor, TransicaoRequisicaoService } from './transicao-requisicao.service';

const UuidOpcional = new ParseUUIDPipe({ optional: true });

/**
 * Esteira da requisição. As rotas de transição NÃO usam @Auditar: o
 * TransicaoRequisicaoService grava o AuditLog dentro da mesma transação da
 * mudança de estado, com snapshot antes/depois.
 */
@UseGuards(JwtAuthGuard, TenantGuard)
@Controller('requisicoes')
export class RequisicoesController {
  constructor(
    private readonly requisicoes: RequisicoesService,
    private readonly transicao: TransicaoRequisicaoService,
    private readonly submeter: SubmeterRequisicaoUseCase,
    private readonly reenviar: ReenviarRequisicaoUseCase,
    private readonly assumirTriagem: AssumirTriagemUseCase,
    private readonly devolverAjuste: DevolverAjusteUseCase,
  ) {}

  @Get()
  listar(
    @Req() req: any,
    @Query('status') status?: string,
    @Query('centroCustoId', UuidOpcional) centroCustoId?: string,
    @Query('solicitanteId', UuidOpcional) solicitanteId?: string,
  ) {
    return this.requisicoes.listar(req.db, { status, centroCustoId, solicitanteId });
  }

  @Get(':id')
  obter(@Req() req: any, @Param('id', ParseUUIDPipe) id: string) {
    return this.requisicoes.obter(req.db, id);
  }

  /** T01 — criar em RASCUNHO. Auditada com o nome da transição, como as demais. */
  @Post()
  @Auditar('requisicao_compra', 'requisicaoCompra', 'T01_CRIAR')
  criar(@Req() req: any, @Body() dto: CriarRequisicaoDto) {
    return this.requisicoes.criar(req.db, dto, req.user.userId);
  }

  /** T02/T07 — editar (rascunho ou em ajuste). */
  @Patch(':id')
  editar(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: EditarRequisicaoDto) {
    return this.executarEdicao(req, id, dto);
  }

  @Post(':id/itens')
  @Auditar('item_requisicao', 'itemRequisicao')
  adicionarItem(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AdicionarItemDto) {
    return this.requisicoes.adicionarItem(req.db, id, dto);
  }

  @Delete(':id/itens/:itemId')
  removerItem(
    @Req() req: any,
    @Param('id', ParseUUIDPipe) id: string,
    @Param('itemId', ParseUUIDPipe) itemId: string,
  ) {
    return this.requisicoes.removerItem(req.db, id, itemId);
  }

  /** T03 — submeter (justificativa, itens > 0 e saldo orçamentário R9). */
  @Post(':id/submeter')
  submeterRequisicao(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: ComandoSimplesDto) {
    return this.submeter.executar(
      req.db,
      id,
      { versionEsperada: dto.version, justificativa: dto.justificativa },
      this.autor(req),
    );
  }

  /** T05 — assumir triagem (grava comprador e fecha o TTO). */
  @Post(':id/assumir-triagem')
  assumir(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: AssumirTriagemDto) {
    return this.assumirTriagem.executar(
      req.db,
      id,
      { versionEsperada: dto.version, compradorId: dto.compradorId },
      this.autor(req),
    );
  }

  /** T06 — devolver para ajuste (motivo + congela o SLA). */
  @Post(':id/devolver')
  devolver(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: ComandoComMotivoDto) {
    return this.devolverAjuste.executar(
      req.db,
      id,
      { versionEsperada: dto.version, motivo: dto.motivo },
      this.autor(req),
    );
  }

  /** T08 — reenviar após o ajuste (destrava o SLA). */
  @Post(':id/reenviar')
  reenviarRequisicao(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: ComandoSimplesDto) {
    return this.reenviar.executar(
      req.db,
      id,
      { versionEsperada: dto.version, justificativa: dto.justificativa },
      this.autor(req),
    );
  }

  /** T09 — rejeitar (motivo obrigatório). */
  @Post(':id/rejeitar')
  rejeitar(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: ComandoComMotivoDto) {
    return this.transicao.executar(
      req.db,
      id,
      { transicao: 'T09_REJEITAR', agora: new Date(), versionEsperada: dto.version, autorId: req.user.userId, motivo: dto.motivo },
      this.autor(req),
    );
  }

  /** T04 — cancelar. */
  @Post(':id/cancelar')
  cancelar(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: CancelarDto) {
    return this.transicao.executar(
      req.db,
      id,
      { transicao: 'T04_CANCELAR', agora: new Date(), versionEsperada: dto.version, autorId: req.user.userId, motivo: dto.motivo ?? null },
      this.autor(req),
    );
  }

  /** T10 — enviar para cotação. */
  @Post(':id/enviar-cotacao')
  enviarCotacao(@Req() req: any, @Param('id', ParseUUIDPipe) id: string, @Body() dto: ComandoSimplesDto) {
    return this.transicao.executar(
      req.db,
      id,
      { transicao: 'T10_ENVIAR_COTACAO', agora: new Date(), versionEsperada: dto.version, autorId: req.user.userId },
      this.autor(req),
    );
  }

  /** Edição usa T02 ou T07 conforme o estado — a máquina valida os dois. */
  private async executarEdicao(req: any, id: string, dto: EditarRequisicaoDto) {
    const atual = await this.requisicoes.obter(req.db, id);
    const transicao = atual.status === 'DEVOLVIDA_AJUSTE' ? 'T07_EDITAR_EM_AJUSTE' : 'T02_EDITAR';
    const resultado = await this.transicao.executar(
      req.db,
      id,
      { transicao, agora: new Date(), versionEsperada: dto.version, autorId: req.user.userId, justificativa: dto.justificativa },
      this.autor(req),
    );
    // Campos que não fazem parte da máquina de estados.
    if (dto.prioridade !== undefined || dto.dataNecessidade !== undefined) {
      await req.db.requisicaoCompra.update({
        where: { id },
        data: {
          ...(dto.prioridade !== undefined ? { prioridade: dto.prioridade } : {}),
          ...(dto.dataNecessidade !== undefined
            ? { dataNecessidade: new Date(`${dto.dataNecessidade.slice(0, 10)}T00:00:00.000Z`) }
            : {}),
        },
      });
      return this.requisicoes.obter(req.db, id);
    }
    return resultado;
  }

  private autor(req: any): ContextoAutor {
    const bruto = req.headers?.['x-correlation-id'];
    return {
      usuarioId: req.user.userId,
      ip: req.ip ?? null,
      userAgent: req.headers?.['user-agent'] ?? null,
      correlationId: typeof bruto === 'string' && /^[0-9a-f-]{36}$/i.test(bruto) ? bruto : null,
    };
  }
}
