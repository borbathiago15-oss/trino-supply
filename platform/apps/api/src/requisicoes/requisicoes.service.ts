import { Injectable } from '@nestjs/common';
import { ClientEscopado, Prisma } from '@trino/db';
import { conflitoDeUnicidade, ehViolacaoDeUnicidade, naoEncontrado } from '../comum/erros';
import { transicoesDisponiveis } from './dominio/maquina-estados';
import { AdicionarItemDto, CriarRequisicaoDto } from './dto/requisicoes.dto';

const soDia = (iso: string) => new Date(`${iso.slice(0, 10)}T00:00:00.000Z`);

/** Estados em que a composição de itens ainda pode mudar. */
const ESTADOS_EDITAVEIS = new Set(['RASCUNHO', 'DEVOLVIDA_AJUSTE']);

@Injectable()
export class RequisicoesService {
  listar(
    db: ClientEscopado,
    filtros: { status?: string; centroCustoId?: string; solicitanteId?: string; orcamentoEstourado?: boolean },
  ) {
    return db.requisicaoCompra.findMany({
      where: {
        ...(filtros.status ? { status: filtros.status } : {}),
        ...(filtros.centroCustoId ? { centroCustoId: filtros.centroCustoId } : {}),
        ...(filtros.solicitanteId ? { solicitanteId: filtros.solicitanteId } : {}),
        // Fila de análise de estouro (R09) — apoiada por ix_requisicao_estouro.
        ...(filtros.orcamentoEstourado !== undefined ? { orcamentoEstourado: filtros.orcamentoEstourado } : {}),
      },
      orderBy: { criadoEm: 'desc' },
    });
  }

  async obter(db: ClientEscopado, id: string) {
    const requisicao = await db.requisicaoCompra.findUnique({ where: { id } });
    if (!requisicao) throw naoEncontrado('REQ-ERR-404', 'Requisição não encontrada neste tenant.');
    const itens = await db.itemRequisicao.findMany({ where: { requisicaoId: id }, orderBy: { sequencia: 'asc' } });
    // A UI não precisa adivinhar quais botões mostrar: a máquina responde.
    return { ...requisicao, itens, transicoesDisponiveis: transicoesDisponiveis(requisicao.status) };
  }

  /**
   * Cria em RASCUNHO (T01). O número sai da sequência dedicada
   * compras.seq_requisicao no formato RC-AAAA-NNNNNN.
   */
  async criar(db: ClientEscopado, dto: CriarRequisicaoDto, solicitanteId: string) {
    await this.exigir(db, 'centroCusto', dto.centroCustoId, 'CAT-ERR-408', 'Centro de custo não encontrado neste tenant.');
    if (dto.contratoId) {
      await this.exigir(db, 'contratoOperacao', dto.contratoId, 'REQ-ERR-410', 'Contrato não encontrado neste tenant.');
    }

    const numero = await this.proximoNumero(db);
    return db.requisicaoCompra.create({
      data: {
        numero,
        centroCustoId: dto.centroCustoId,
        contratoId: dto.contratoId ?? null,
        solicitanteId,
        prioridade: dto.prioridade ?? 'NORMAL',
        justificativa: dto.justificativa?.trim() || null,
        dataNecessidade: dto.dataNecessidade ? soDia(dto.dataNecessidade) : null,
      },
    });
  }

  /**
   * Itens só mudam com a requisição aberta (rascunho ou devolvida). Cada
   * mudança recalcula `valor_estimado` a partir dos itens: o valor é DERIVADO,
   * nunca digitado — é o que mantém a checagem de saldo honesta.
   */
  async adicionarItem(db: ClientEscopado, requisicaoId: string, dto: AdicionarItemDto) {
    const requisicao = await this.exigirEditavel(db, requisicaoId);
    await this.exigir(db, 'varianteSku', dto.varianteId, 'CAT-ERR-407', 'Variante não encontrada neste tenant.');

    const ultima = await db.itemRequisicao.findFirst({
      where: { requisicaoId },
      orderBy: { sequencia: 'desc' },
    });
    try {
      await db.itemRequisicao.create({
        data: {
          requisicaoId,
          varianteId: dto.varianteId,
          sequencia: (ultima?.sequencia ?? 0) + 1,
          quantidade: dto.quantidade,
          precoReferencia: dto.precoReferencia ?? null,
          observacao: dto.observacao ?? null,
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('REQ-ERR-011', 'Sequência de item já usada nesta requisição.');
      }
      throw e;
    }
    await this.recalcularValorEstimado(db, requisicao.id);
    return this.obter(db, requisicaoId);
  }

  async removerItem(db: ClientEscopado, requisicaoId: string, itemId: string) {
    await this.exigirEditavel(db, requisicaoId);
    const item = await db.itemRequisicao.findUnique({ where: { id: itemId } });
    if (!item || item.requisicaoId !== requisicaoId) {
      throw naoEncontrado('REQ-ERR-412', 'Item não encontrado nesta requisição.');
    }
    await db.itemRequisicao.delete({ where: { id: itemId } });
    await this.recalcularValorEstimado(db, requisicaoId);
    return this.obter(db, requisicaoId);
  }

  /** Soma quantidade × preço de referência dos itens ATIVOS. */
  async recalcularValorEstimado(db: ClientEscopado, requisicaoId: string) {
    const itens = await db.itemRequisicao.findMany({ where: { requisicaoId, status: 'ATIVO' } });
    const total = itens.reduce(
      (soma: number, i: any) => soma + Number(i.quantidade) * Number(i.precoReferencia ?? 0),
      0,
    );
    await db.requisicaoCompra.update({
      where: { id: requisicaoId },
      data: { valorEstimado: total.toFixed(2) },
    });
    return total;
  }

  private async exigirEditavel(db: ClientEscopado, requisicaoId: string) {
    const requisicao = await db.requisicaoCompra.findUnique({ where: { id: requisicaoId } });
    if (!requisicao) throw naoEncontrado('REQ-ERR-404', 'Requisição não encontrada neste tenant.');
    if (!ESTADOS_EDITAVEIS.has(requisicao.status)) {
      throw conflitoDeUnicidade(
        'REQ-ERR-010',
        `Itens não podem ser alterados com a requisição em ${requisicao.status}.`,
      );
    }
    return requisicao;
  }

  private async proximoNumero(db: ClientEscopado): Promise<string> {
    const [linha] = await db.$queryRaw(Prisma.sql`SELECT nextval('compras.seq_requisicao') AS valor`);
    const sequencial = String(linha.valor).padStart(6, '0');
    return `RC-${new Date().getUTCFullYear()}-${sequencial}`;
  }

  private async exigir(db: ClientEscopado, delegate: string, id: string, codigo: string, mensagem: string) {
    if (!(await (db as any)[delegate].findUnique({ where: { id } }))) {
      throw naoEncontrado(codigo, mensagem);
    }
  }
}
