import { Injectable, Logger, OnModuleInit } from '@nestjs/common';
import { forTenant, PrismaClient } from '@trino/db';
import { PrismaService } from '../prisma/prisma.service';
import { mensagemDeErros, TipoLote, VALIDADORES } from './dominio/parsers';
import { FilaImportacao, JobImportacao } from './fila';

/** Quantas linhas o worker carrega por vez — arquivo grande não vira um SELECT gigante. */
const TAMANHO_PAGINA = 200;

/**
 * ProcessadorDeImportacaoWorker — consome a fila e processa o lote linha a linha.
 *
 * A regra que manda aqui é RESILIÊNCIA: uma linha ruim não derruba o arquivo.
 * Cada linha é validada e persistida isoladamente; erro vira ERRO_VALIDACAO ou
 * ERRO_PERSISTENCIA com a mensagem detalhada, e o laço segue. No fim, os
 * contadores fecham o lote em CONCLUIDO_COM_SUCESSO ou CONCLUIDO_COM_FALHAS —
 * FALHA_CRITICA fica para o que impede processar o lote inteiro.
 */
@Injectable()
export class ProcessadorDeImportacaoWorker implements OnModuleInit {
  private readonly log = new Logger(ProcessadorDeImportacaoWorker.name);

  constructor(
    private readonly fila: FilaImportacao,
    private readonly prisma: PrismaService,
  ) {}

  onModuleInit() {
    this.fila.registrarProcessador((job) => this.processar(job));
  }

  async processar(job: JobImportacao): Promise<void> {
    // O worker roda fora de uma requisição: o escopo de tenant vem do job.
    const db = forTenant(this.prisma as unknown as PrismaClient, job.tenantId);
    const lote = await db.loteImportacao.findUnique({ where: { id: job.loteId } });
    if (!lote) {
      this.log.warn(`Lote ${job.loteId} não existe mais — nada a processar.`);
      return;
    }
    if (lote.status !== 'RECEBIDO') {
      // Reentrega do BullMQ: não reprocessa o que já foi concluído.
      this.log.warn(`Lote ${lote.id} já está em ${lote.status} — ignorando reentrega.`);
      return;
    }

    await db.loteImportacao.update({ where: { id: lote.id }, data: { status: 'PROCESSANDO' } });

    const validar = VALIDADORES[lote.tipo as TipoLote];
    let sucesso = 0;
    let erro = 0;

    try {
      for (;;) {
        const pagina = await db.stagingLinhaImportacao.findMany({
          where: { loteId: lote.id, status: 'PENDENTE' },
          orderBy: { numeroLinha: 'asc' },
          take: TAMANHO_PAGINA,
        });
        if (pagina.length === 0) break;

        for (const linha of pagina) {
          const resultado = validar(linha.conteudoBruto as Record<string, string>);
          if (!resultado.valido) {
            erro += 1;
            await this.marcar(db, linha.id, 'ERRO_VALIDACAO', mensagemDeErros(resultado.erros));
            continue;
          }
          try {
            await this.persistir(db, lote.tipo as TipoLote, resultado.dados);
            sucesso += 1;
            await this.marcar(db, linha.id, 'IMPORTADO', null);
          } catch (e) {
            // Falha ao gravar é da LINHA, não do arquivo: registra e segue.
            erro += 1;
            await this.marcar(db, linha.id, 'ERRO_PERSISTENCIA', String((e as Error)?.message ?? e).slice(0, 2000));
          }
        }
      }

      await db.loteImportacao.update({
        where: { id: lote.id },
        data: {
          linhasSucesso: sucesso,
          linhasErro: erro,
          status: erro === 0 ? 'CONCLUIDO_COM_SUCESSO' : 'CONCLUIDO_COM_FALHAS',
          concluidoEm: new Date(),
        },
      });
    } catch (e) {
      // Só chega aqui o que impede processar o lote inteiro.
      this.log.error(`Falha crítica no lote ${lote.id}: ${(e as Error)?.message}`);
      await db.loteImportacao.update({
        where: { id: lote.id },
        data: { status: 'FALHA_CRITICA', linhasSucesso: sucesso, linhasErro: erro, concluidoEm: new Date() },
      });
      throw e;
    }
  }

  private marcar(db: any, id: bigint, status: string, mensagemErro: string | null) {
    return db.stagingLinhaImportacao.update({
      where: { id },
      data: { status, mensagemErro, processadoEm: new Date() },
    });
  }

  /**
   * Persistência nas tabelas de destino. Cada tipo resolve suas referências por
   * CÓDIGO (não por id): a planilha do usuário fala em códigos de negócio, e é
   * responsabilidade daqui traduzir — referência inexistente vira erro da linha.
   */
  private async persistir(db: any, tipo: TipoLote, dados: any) {
    switch (tipo) {
      case 'CATALOGO_SKU': {
        const tipoProduto = await db.tipoProduto.findFirst({ where: { codigo: dados.codigoTipo } });
        if (!tipoProduto) throw new Error(`Tipo de produto "${dados.codigoTipo}" não existe no catálogo.`);

        // findFirst + create/update em vez de upsert por chave composta: o
        // escopo de tenant já filtra, e não amarra a importação ao nome exato
        // das uniques do schema.
        const skuExistente = await db.skuBase.findFirst({ where: { codigo: dados.codigo } });
        const sku = skuExistente
          ? await db.skuBase.update({
              where: { id: skuExistente.id },
              data: { descricao: dados.descricao, unidadeMedida: dados.unidadeMedida, exigeCa: dados.exigeCa },
            })
          : await db.skuBase.create({
              data: {
                tipoProdutoId: tipoProduto.id, codigo: dados.codigo, descricao: dados.descricao,
                unidadeMedida: dados.unidadeMedida, exigeCa: dados.exigeCa,
              },
            });

        const varianteExistente = await db.varianteSku.findFirst({ where: { codigo: dados.varianteCodigo } });
        if (varianteExistente) {
          await db.varianteSku.update({
            where: { id: varianteExistente.id },
            data: { tamanho: dados.tamanho, cor: dados.cor },
          });
        } else {
          await db.varianteSku.create({
            data: { skuBaseId: sku.id, codigo: dados.varianteCodigo, tamanho: dados.tamanho, cor: dados.cor },
          });
        }
        return;
      }

      case 'PARAMETRO_ESTOQUE': {
        const variante = await db.varianteSku.findFirst({ where: { codigo: dados.varianteCodigo } });
        if (!variante) throw new Error(`Variante "${dados.varianteCodigo}" não existe no catálogo.`);
        const centroCusto = await db.centroCusto.findFirst({ where: { codigo: dados.centroCusto } });
        if (!centroCusto) throw new Error(`Centro de custo "${dados.centroCusto}" não existe.`);
        const dadosParametro = {
          pontoPedidoRop: dados.pontoPedidoRop, estoqueSeguranca: dados.estoqueSeguranca,
          leadTimeDias: dados.leadTimeDias, gerarScAutomatica: dados.gerarScAutomatica,
        };
        const parametro = await db.parametroEstoque.findFirst({
          where: { varianteId: variante.id, centroCustoId: centroCusto.id },
        });
        if (parametro) await db.parametroEstoque.update({ where: { id: parametro.id }, data: dadosParametro });
        else await db.parametroEstoque.create({ data: { varianteId: variante.id, centroCustoId: centroCusto.id, ...dadosParametro } });
        return;
      }

      case 'FORNECEDOR': {
        const existente = await db.fornecedor.findFirst({ where: { cnpj: dados.cnpj } });
        if (existente) {
          await db.fornecedor.update({
            where: { id: existente.id },
            data: {
              razaoSocial: dados.razaoSocial, nomeFantasia: dados.nomeFantasia,
              emailContato: dados.emailContato, telefone: dados.telefone,
            },
          });
          return;
        }
        await db.fornecedor.create({
          data: {
            cnpj: dados.cnpj, razaoSocial: dados.razaoSocial, nomeFantasia: dados.nomeFantasia,
            emailContato: dados.emailContato, telefone: dados.telefone,
          },
        });
        return;
      }

      case 'ORCAMENTO_CC': {
        const centroCusto = await db.centroCusto.findFirst({ where: { codigo: dados.centroCusto } });
        if (!centroCusto) throw new Error(`Centro de custo "${dados.centroCusto}" não existe.`);
        const orcamento = await db.orcamentoCentroCusto.findFirst({
          where: { centroCustoId: centroCusto.id, exercicio: dados.exercicio },
        });
        if (orcamento) await db.orcamentoCentroCusto.update({ where: { id: orcamento.id }, data: { valorOrcado: dados.valorOrcado } });
        else await db.orcamentoCentroCusto.create({ data: { centroCustoId: centroCusto.id, exercicio: dados.exercicio, valorOrcado: dados.valorOrcado } });
        return;
      }

      default:
        throw new Error(`Tipo de lote não suportado: ${tipo}`);
    }
  }
}
