import { ConflictException, Injectable } from '@nestjs/common';
import { ClientEscopado } from '@trino/db';
import { conflitoDeUnicidade, naoEncontrado, requisicaoInvalida } from '../comum/erros';
import { proximoNumero, registrarAuditoria, ContextoAutor } from '../cotacoes/casos-de-uso';
import { ElegibilidadeService } from '../fornecedores/elegibilidade.service';
import { TransicaoRequisicaoService } from '../requisicoes/transicao-requisicao.service';
import { conciliarRecebimento, ConciliacaoError, EventoConta408 } from './dominio/conciliacao';
import { EmitirPedidoDto, RegistrarRecebimentoDto } from './dto/pedidos.dto';

const traduzirConciliacao = (e: unknown) =>
  e instanceof ConciliacaoError ? new ConflictException({ codigo: e.codigo, mensagem: e.message, detalhe: e.detalhe }) : e;

/**
 * F6 — emissão do pedido de compra (T16 da esteira).
 *
 * O pedido só nasce do que já passou pelo funil: requisição em
 * APROVACAO_ALCADA com instância de alçada APROVADA. Antes de emitir, duas
 * validações que custam caro se falharem depois:
 *  - R09: orçamento. Estouro não impede — mas exige que o aprovador FINAL
 *    tenha autorizado (F3/B6). Emitir sem essa autorização seria furar por
 *    baixo a decisão que a alçada tomou por cima.
 *  - CA de EPI: todo item cuja SKU exige CA precisa de certificado VÁLIDO do
 *    fornecedor escolhido, para aquela variante. É a mesma checagem da F2.
 */
@Injectable()
export class EmitirPedidoCompraUseCase {
  constructor(
    private readonly elegibilidade: ElegibilidadeService,
    private readonly transicao: TransicaoRequisicaoService,
  ) {}

  async executar(db: ClientEscopado, dto: EmitirPedidoDto, autor: ContextoAutor) {
    const requisicao = await db.requisicaoCompra.findUnique({ where: { id: dto.requisicaoId } });
    if (!requisicao) throw naoEncontrado('REQ-ERR-404', 'Requisição não encontrada neste tenant.');

    // 1. A alçada precisa ter aprovado — é o gatilho do T16.
    const instancia = await db.instanciaAprovacao.findFirst({
      where: { requisicaoId: requisicao.id, status: 'APROVADA' },
    });
    if (!instancia) {
      throw conflitoDeUnicidade(
        'PED-ERR-001',
        'Só se emite pedido de requisição com aprovação de alçada concluída.',
      );
    }

    // 2. R09: estouro exige autorização do aprovador final.
    if (requisicao.orcamentoEstourado && !requisicao.estouroAutorizadoPor) {
      throw conflitoDeUnicidade(
        'PED-ERR-002',
        'Requisição com orçamento estourado sem autorização do aprovador final.',
      );
    }

    const itensRequisicao = await db.itemRequisicao.findMany({
      where: { requisicaoId: requisicao.id, status: { in: ['ATIVO', 'EM_COTACAO', 'COTADO'] } },
    });
    if (itensRequisicao.length === 0) {
      throw conflitoDeUnicidade('PED-ERR-003', 'Requisição sem itens para pedir.');
    }

    // 3. CA de EPI + elegibilidade do fornecedor escolhido, item a item.
    for (const item of itensRequisicao) {
      const resultado = await this.elegibilidade.avaliar(db, dto.fornecedorId, item.varianteId);
      if (!resultado.elegivel) {
        throw conflitoDeUnicidade(
          'PED-ERR-004',
          `Fornecedor inelegível para o item: ${resultado.motivos.map((m) => m.mensagem).join(' ')}`,
        );
      }
    }

    const precoPorItem = new Map(dto.itens.map((i) => [i.itemRequisicaoId, i]));
    let valorTotal = 0;
    for (const item of itensRequisicao) {
      const informado = precoPorItem.get(item.id);
      if (!informado) {
        throw requisicaoInvalida('PED-ERR-005', `Falta o preço do item ${item.id} no pedido.`);
      }
      valorTotal += Number(item.quantidade) * informado.precoUnitario;
    }
    valorTotal = Number(valorTotal.toFixed(2));

    const numero = await proximoNumero(db, 'seq_pedido', 'PC');
    const pedido = await db.pedidoCompra.create({
      data: {
        numero,
        requisicaoId: requisicao.id,
        cotacaoId: dto.cotacaoId ?? null,
        fornecedorId: dto.fornecedorId,
        centroCustoId: requisicao.centroCustoId,
        valorTotal,
        condicaoPagamento: dto.condicaoPagamento.trim(),
        prazoEntrega: new Date(`${dto.prazoEntrega.slice(0, 10)}T00:00:00.000Z`),
        emitidoPor: autor.usuarioId,
      },
    });

    let sequencia = 1;
    for (const item of itensRequisicao) {
      const informado = precoPorItem.get(item.id)!;
      await db.itemPedido.create({
        data: {
          pedidoId: pedido.id,
          itemRequisicaoId: item.id,
          varianteId: item.varianteId,
          sequencia: sequencia++,
          qtdPedida: item.quantidade,
          precoUnitario: informado.precoUnitario,
        },
      });
      await db.itemRequisicao.update({ where: { id: item.id }, data: { status: 'PEDIDO' } });
    }

    // T16 na máquina de estados: a requisição vira PEDIDO_GERADO.
    await this.transicao.executar(
      db,
      requisicao.id,
      { transicao: 'T16_EMITIR_PEDIDO', agora: new Date(), autorId: autor.usuarioId },
      autor,
    );

    await registrarAuditoria(db, autor, 'pedido_compra', pedido.id, 'T16_EMITIR_PEDIDO', null, {
      numero,
      requisicaoId: requisicao.id,
      fornecedorId: dto.fornecedorId,
      valorTotal,
      instanciaAprovacaoId: instancia.id,
      orcamentoEstourado: requisicao.orcamentoEstourado,
      estouroAutorizadoPor: requisicao.estouroAutorizadoPor,
    });

    return obterPedido(db, pedido.id);
  }
}

/**
 * F6 — recebimento com conciliação 3-way (Pedido × NF × Recebido).
 *
 * A conciliação é DOMÍNIO PURO; aqui ficam as gravações, todas na MESMA
 * transação:
 *  - o recebimento e seus itens;
 *  - `qtd_recebida` acumulada no item do pedido (o banco trava em 110% do
 *    pedido, ck_item_pedido_qtd);
 *  - saldo de estoque incrementado ATOMICAMENTE, só com o que não veio
 *    avariado — mercadoria quebrada não vira disponível;
 *  - status do pedido (RECEBIDO_PARCIAL/TOTAL) e a transição da requisição;
 *  - os eventos da CONTA 408 (avaria, divergência de quantidade, de valor e de
 *    especificação), gravados na trilha de auditoria — que é o registro
 *    durável do sistema, já que o DDL não prevê tabela de eventos.
 */
@Injectable()
export class RegistrarRecebimentoUseCase {
  constructor(private readonly transicao: TransicaoRequisicaoService) {}

  async executar(db: ClientEscopado, pedidoId: string, dto: RegistrarRecebimentoDto, autor: ContextoAutor) {
    const resultado = await db.$transaction(async (tx: any) => {
      const pedido = await tx.pedidoCompra.findUnique({ where: { id: pedidoId } });
      if (!pedido) throw naoEncontrado('PED-ERR-404', 'Pedido não encontrado neste tenant.');
      if (pedido.status === 'CANCELADO') {
        throw conflitoDeUnicidade('PED-ERR-006', 'Pedido cancelado não recebe mercadoria.');
      }
      if (pedido.status === 'RECEBIDO_TOTAL') {
        throw conflitoDeUnicidade('PED-ERR-007', 'Pedido já recebido integralmente.');
      }

      const itensPedido = await tx.itemPedido.findMany({ where: { pedidoId } });

      let notaFiscal = null;
      if (dto.notaFiscal) {
        // A NF é do fornecedor do pedido — conferir isso aqui evita conciliar
        // contra documento de outra compra.
        notaFiscal = await tx.notaFiscalEntrada.findFirst({
          where: { chaveAcesso: dto.notaFiscal.chaveAcesso },
        });
        if (!notaFiscal) {
          notaFiscal = await tx.notaFiscalEntrada.create({
            data: {
              fornecedorId: pedido.fornecedorId,
              chaveAcesso: dto.notaFiscal.chaveAcesso,
              numero: dto.notaFiscal.numero,
              serie: dto.notaFiscal.serie,
              valorTotal: dto.notaFiscal.valorTotal,
              dataEmissao: new Date(`${dto.notaFiscal.dataEmissao.slice(0, 10)}T00:00:00.000Z`),
              arquivoXmlUri: dto.notaFiscal.arquivoXmlUri ?? null,
            },
          });
        } else if (notaFiscal.fornecedorId !== pedido.fornecedorId) {
          throw conflitoDeUnicidade('PED-ERR-008', 'A nota fiscal é de outro fornecedor.');
        }
      }

      const conciliacao = conciliarRecebimento({
        itensPedido: itensPedido.map((i: any) => ({
          id: i.id,
          varianteId: i.varianteId,
          qtdPedida: Number(i.qtdPedida),
          qtdRecebidaAcumulada: Number(i.qtdRecebida),
          precoUnitario: Number(i.precoUnitario),
        })),
        linhas: dto.itens,
        notaFiscal: notaFiscal
          ? { id: notaFiscal.id, valorTotal: Number(notaFiscal.valorTotal), fornecedorId: notaFiscal.fornecedorId }
          : null,
      });

      const recebimento = await tx.recebimento.create({
        data: {
          pedidoId,
          notaFiscalId: notaFiscal?.id ?? null,
          tipo: conciliacao.tipo,
          recebedorId: autor.usuarioId,
          observacao: dto.observacao?.trim() || null,
        },
      });

      for (const linha of conciliacao.linhas) {
        await tx.itemRecebimento.create({
          data: {
            recebimentoId: recebimento.id,
            itemPedidoId: linha.itemPedidoId,
            qtdRecebida: linha.qtdRecebida,
            qtdAvariada: linha.qtdAvariada,
            ocorrencia: linha.ocorrencia,
            descricaoOcorrencia:
              dto.itens.find((i) => i.itemPedidoId === linha.itemPedidoId)?.descricaoOcorrencia?.trim() ||
              (linha.ocorrencia === 'SEM_OCORRENCIA' ? null : 'Ocorrência registrada no recebimento.'),
          },
        });

        // Acumula no item do pedido — increment atômico, sem ler-e-escrever.
        await tx.itemPedido.update({
          where: { id: linha.itemPedidoId },
          data: { qtdRecebida: { increment: linha.qtdRecebida }, version: { increment: 1 } },
        });

        // Estoque: só o que está íntegro. O avariado fica fora e vira evento 408.
        if (linha.qtdParaEstoque > 0) {
          await incrementarSaldo(tx, linha.varianteId, pedido.centroCustoId, linha.qtdParaEstoque);
        }
      }

      const novoStatus = conciliacao.tipo === 'TOTAL' ? 'RECEBIDO_TOTAL' : 'RECEBIDO_PARCIAL';
      await tx.pedidoCompra.update({
        where: { id: pedidoId },
        data: { status: novoStatus, version: { increment: 1 } },
      });

      await tx.auditLog.create({
        data: {
          actorId: autor.usuarioId,
          entidade: 'recebimento',
          entityId: recebimento.id,
          acao: conciliacao.tipo === 'TOTAL' ? 'RECEBER_TOTAL' : 'RECEBER_PARCIAL',
          beforeJson: { pedidoId, statusAnterior: pedido.status } as any,
          afterJson: {
            pedidoId,
            status: novoStatus,
            tipo: conciliacao.tipo,
            valorRecebido: conciliacao.valorRecebido,
            valorNotaFiscal: conciliacao.valorNotaFiscal,
            conciliado: conciliacao.conciliado,
            divergencias: conciliacao.divergencias,
            linhas: conciliacao.linhas,
          } as any,
          ip: autor.ip ?? null,
          userAgent: autor.userAgent ?? null,
          correlationId: autor.correlationId ?? null,
        },
      });

      // Cada evento da Conta 408 é um lançamento próprio na trilha: divergência
      // e avaria precisam ser localizáveis uma a uma pelo financeiro.
      for (const evento of conciliacao.eventosConta408) {
        await registrarEvento408(tx, autor, recebimento.id, pedido, evento);
      }

      return { recebimento, conciliacao, pedidoStatus: novoStatus, requisicaoId: pedido.requisicaoId };
    });

    // A requisição acompanha o pedido: parcial ou total.
    await this.transicao.executar(
      db,
      resultado.requisicaoId,
      {
        transicao: resultado.conciliacao.tipo === 'TOTAL' ? 'T18_RECEBER_TOTAL' : 'T17_RECEBER_PARCIAL',
        agora: new Date(),
        autorId: autor.usuarioId,
      },
      autor,
    );

    return {
      recebimento: resultado.recebimento,
      tipo: resultado.conciliacao.tipo,
      pedidoStatus: resultado.pedidoStatus,
      conciliado: resultado.conciliacao.conciliado,
      valorRecebido: resultado.conciliacao.valorRecebido,
      valorNotaFiscal: resultado.conciliacao.valorNotaFiscal,
      divergencias: resultado.conciliacao.divergencias,
      eventosConta408: resultado.conciliacao.eventosConta408,
      linhas: resultado.conciliacao.linhas,
    };
  }
}

/**
 * Incremento atômico do saldo. `updateMany` com increment resolve no banco, sem
 * ler-e-escrever, então N recebimentos simultâneos somam certo. Se a linha de
 * saldo ainda não existe, cria; corrida na criação cai na unique
 * (uq_saldo_estoque) e o retry incrementa a linha que venceu.
 * A trava não-negativa é o CHECK ck_saldo_nao_negativo, da F2.
 */
async function incrementarSaldo(tx: any, varianteId: string, centroCustoId: string, quantidade: number) {
  const { count } = await tx.saldoEstoque.updateMany({
    where: { varianteId, centroCustoId },
    data: { qtdDisponivel: { increment: quantidade }, version: { increment: 1 } },
  });
  if (count > 0) return;
  try {
    await tx.saldoEstoque.create({ data: { varianteId, centroCustoId, qtdDisponivel: quantidade } });
  } catch {
    await tx.saldoEstoque.updateMany({
      where: { varianteId, centroCustoId },
      data: { qtdDisponivel: { increment: quantidade }, version: { increment: 1 } },
    });
  }
}

/** Lançamento na Conta 408 — avaria e divergência viram registro rastreável. */
async function registrarEvento408(tx: any, autor: ContextoAutor, recebimentoId: string, pedido: any, evento: EventoConta408) {
  await tx.auditLog.create({
    data: {
      actorId: autor.usuarioId,
      entidade: 'evento_conta_408',
      entityId: recebimentoId,
      acao: evento.tipo,
      beforeJson: undefined,
      afterJson: {
        ...evento,
        pedidoId: pedido.id,
        pedidoNumero: pedido.numero,
        fornecedorId: pedido.fornecedorId,
        centroCustoId: pedido.centroCustoId,
      } as any,
      ip: autor.ip ?? null,
      userAgent: autor.userAgent ?? null,
      correlationId: autor.correlationId ?? null,
    },
  });
}

export async function obterPedido(db: ClientEscopado, id: string) {
  const pedido = await db.pedidoCompra.findUnique({ where: { id } });
  if (!pedido) throw naoEncontrado('PED-ERR-404', 'Pedido não encontrado neste tenant.');
  const [itens, recebimentos] = await Promise.all([
    db.itemPedido.findMany({ where: { pedidoId: id }, orderBy: { sequencia: 'asc' } }),
    db.recebimento.findMany({ where: { pedidoId: id }, orderBy: { dataRecebimento: 'asc' } }),
  ]);
  return { ...pedido, itens, recebimentos };
}

export { traduzirConciliacao };
