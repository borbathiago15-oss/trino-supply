import { Injectable } from '@nestjs/common';
import { ClientEscopado } from '@trino/db';
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
import {
  conflitoDeUnicidade,
  ehNaoEncontrado,
  ehViolacaoDeUnicidade,
  naoEncontrado,
} from '../comum/erros';

/**
 * Catálogo hierárquico: família → tipo → SKU base → variante, mais os
 * parâmetros de estoque (ROP) e o saldo por centro de custo.
 * Recebe sempre o client escopado (request.db): o tenant vem do token e
 * nenhum método aqui vê tenantId.
 */
@Injectable()
export class CatalogoService {
  // ----- Família -----------------------------------------------------------
  listarFamilias(db: ClientEscopado) {
    return db.familiaProduto.findMany({ orderBy: { codigo: 'asc' } });
  }

  async criarFamilia(db: ClientEscopado, dto: CriarFamiliaDto) {
    try {
      return await db.familiaProduto.create({
        data: {
          codigo: dto.codigo.trim().toUpperCase(),
          nome: dto.nome.trim(),
          leadTimeMedioDias: dto.leadTimeMedioDias ?? 0,
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('CAT-ERR-001', 'Já existe família com este código no tenant.');
      }
      throw e;
    }
  }

  async atualizarFamilia(db: ClientEscopado, id: string, dto: AtualizarFamiliaDto) {
    try {
      return await db.familiaProduto.update({ where: { id }, data: { ...dto } });
    } catch (e) {
      throw this.traduzir(e, 'CAT-ERR-404', 'Família não encontrada neste tenant.');
    }
  }

  // ----- Tipo de produto ---------------------------------------------------
  listarTipos(db: ClientEscopado, familiaId?: string) {
    return db.tipoProduto.findMany({
      where: familiaId ? { familiaId } : undefined,
      orderBy: { codigo: 'asc' },
    });
  }

  async criarTipo(db: ClientEscopado, dto: CriarTipoProdutoDto) {
    await this.exigirFamilia(db, dto.familiaId);
    try {
      return await db.tipoProduto.create({
        data: {
          familiaId: dto.familiaId,
          codigo: dto.codigo.trim().toUpperCase(),
          nome: dto.nome.trim(),
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('CAT-ERR-002', 'Já existe tipo de produto com este código no tenant.');
      }
      throw e;
    }
  }

  async atualizarTipo(db: ClientEscopado, id: string, dto: AtualizarTipoProdutoDto) {
    try {
      return await db.tipoProduto.update({ where: { id }, data: { ...dto } });
    } catch (e) {
      throw this.traduzir(e, 'CAT-ERR-405', 'Tipo de produto não encontrado neste tenant.');
    }
  }

  // ----- SKU base ----------------------------------------------------------
  listarSkusBase(db: ClientEscopado, tipoProdutoId?: string) {
    return db.skuBase.findMany({
      where: tipoProdutoId ? { tipoProdutoId } : undefined,
      orderBy: { codigo: 'asc' },
    });
  }

  async criarSkuBase(db: ClientEscopado, dto: CriarSkuBaseDto) {
    const tipo = await db.tipoProduto.findUnique({ where: { id: dto.tipoProdutoId } });
    if (!tipo) throw naoEncontrado('CAT-ERR-405', 'Tipo de produto não encontrado neste tenant.');
    try {
      return await db.skuBase.create({
        data: {
          tipoProdutoId: dto.tipoProdutoId,
          codigo: dto.codigo.trim().toUpperCase(),
          descricao: dto.descricao.trim(),
          unidadeMedida: dto.unidadeMedida,
          exigeCa: dto.exigeCa ?? false,
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('CAT-ERR-003', 'Já existe SKU base com este código no tenant.');
      }
      throw e;
    }
  }

  async atualizarSkuBase(db: ClientEscopado, id: string, dto: AtualizarSkuBaseDto) {
    try {
      return await db.skuBase.update({ where: { id }, data: { ...dto } });
    } catch (e) {
      throw this.traduzir(e, 'CAT-ERR-406', 'SKU base não encontrado neste tenant.');
    }
  }

  // ----- Variante ----------------------------------------------------------
  listarVariantes(db: ClientEscopado, skuBaseId?: string) {
    return db.varianteSku.findMany({
      where: skuBaseId ? { skuBaseId } : undefined,
      orderBy: { codigo: 'asc' },
    });
  }

  async criarVariante(db: ClientEscopado, dto: CriarVarianteDto) {
    const skuBase = await db.skuBase.findUnique({ where: { id: dto.skuBaseId } });
    if (!skuBase) throw naoEncontrado('CAT-ERR-406', 'SKU base não encontrado neste tenant.');
    try {
      return await db.varianteSku.create({
        data: {
          skuBaseId: dto.skuBaseId,
          codigo: dto.codigo.trim().toUpperCase(),
          grade: dto.grade ?? null,
          tamanho: dto.tamanho ?? null,
          cor: dto.cor ?? null,
          codigoBarras: dto.codigoBarras ?? null,
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade(
          'CAT-ERR-004',
          'Já existe variante com este código, código de barras ou combinação grade/tamanho/cor.',
        );
      }
      throw e;
    }
  }

  async atualizarVariante(db: ClientEscopado, id: string, dto: AtualizarVarianteDto) {
    try {
      return await db.varianteSku.update({ where: { id }, data: { ...dto } });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('CAT-ERR-004', 'Código de barras ou combinação grade/tamanho/cor já usada.');
      }
      throw this.traduzir(e, 'CAT-ERR-407', 'Variante não encontrada neste tenant.');
    }
  }

  // ----- Parâmetros de estoque (ROP) ---------------------------------------
  listarParametros(db: ClientEscopado, centroCustoId?: string) {
    return db.parametroEstoque.findMany({
      where: centroCustoId ? { centroCustoId } : undefined,
      orderBy: { atualizadoEm: 'desc' },
    });
  }

  /**
   * Um parâmetro por (variante, centro de custo): definir de novo atualiza.
   * O CHECK ck_parametro_valores no banco é a última linha de defesa; a
   * validação do DTO evita chegar lá com valor negativo.
   */
  async definirParametro(db: ClientEscopado, dto: DefinirParametroEstoqueDto) {
    await this.exigirVariante(db, dto.varianteId);
    await this.exigirCentroCusto(db, dto.centroCustoId);
    const dados = {
      pontoPedidoRop: dto.pontoPedidoRop,
      estoqueSeguranca: dto.estoqueSeguranca,
      leadTimeDias: dto.leadTimeDias ?? 0,
      gerarScAutomatica: dto.gerarScAutomatica ?? false,
    };
    return db.parametroEstoque.upsert({
      where: { varianteId_centroCustoId: { varianteId: dto.varianteId, centroCustoId: dto.centroCustoId } },
      update: dados,
      create: { varianteId: dto.varianteId, centroCustoId: dto.centroCustoId, ...dados },
    });
  }

  // ----- Saldo -------------------------------------------------------------
  listarSaldos(db: ClientEscopado, centroCustoId?: string) {
    return db.saldoEstoque.findMany({
      where: centroCustoId ? { centroCustoId } : undefined,
      orderBy: { atualizadoEm: 'desc' },
    });
  }

  async criarSaldo(db: ClientEscopado, dto: CriarSaldoDto) {
    await this.exigirVariante(db, dto.varianteId);
    await this.exigirCentroCusto(db, dto.centroCustoId);
    try {
      return await db.saldoEstoque.create({
        data: {
          varianteId: dto.varianteId,
          centroCustoId: dto.centroCustoId,
          qtdDisponivel: dto.qtdDisponivel ?? 0,
          qtdReservada: dto.qtdReservada ?? 0,
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('EST-ERR-002', 'Já existe saldo para esta variante neste centro de custo.');
      }
      throw e;
    }
  }

  /**
   * Bloqueio otimista: a atualização só acontece se a versão em banco ainda for
   * a que o chamador leu. Duas edições concorrentes → a segunda recebe 409
   * EST-ERR-409 e relê, em vez de sobrescrever silenciosamente.
   */
  async ajustarSaldo(db: ClientEscopado, id: string, dto: AjustarSaldoDto) {
    const atual = await db.saldoEstoque.findUnique({ where: { id } });
    if (!atual) throw naoEncontrado('EST-ERR-404', 'Saldo não encontrado neste tenant.');

    const { count } = await db.saldoEstoque.updateMany({
      where: { id, version: dto.version },
      data: {
        ...(dto.qtdDisponivel !== undefined ? { qtdDisponivel: dto.qtdDisponivel } : {}),
        ...(dto.qtdReservada !== undefined ? { qtdReservada: dto.qtdReservada } : {}),
        version: { increment: 1 },
      },
    });

    if (count === 0) {
      throw conflitoDeUnicidade(
        'EST-ERR-409',
        `Saldo alterado por outra operação (versão atual ${atual.version}). Releia antes de gravar.`,
      );
    }
    return db.saldoEstoque.findUnique({ where: { id } });
  }

  // ----- apoio -------------------------------------------------------------
  private async exigirFamilia(db: ClientEscopado, id: string) {
    if (!(await db.familiaProduto.findUnique({ where: { id } }))) {
      throw naoEncontrado('CAT-ERR-404', 'Família não encontrada neste tenant.');
    }
  }

  private async exigirVariante(db: ClientEscopado, id: string) {
    if (!(await db.varianteSku.findUnique({ where: { id } }))) {
      throw naoEncontrado('CAT-ERR-407', 'Variante não encontrada neste tenant.');
    }
  }

  private async exigirCentroCusto(db: ClientEscopado, id: string) {
    if (!(await db.centroCusto.findUnique({ where: { id } }))) {
      throw naoEncontrado('CAT-ERR-408', 'Centro de custo não encontrado neste tenant.');
    }
  }

  private traduzir(e: unknown, codigo: string, mensagem: string) {
    return ehNaoEncontrado(e) ? naoEncontrado(codigo, mensagem) : e;
  }
}
