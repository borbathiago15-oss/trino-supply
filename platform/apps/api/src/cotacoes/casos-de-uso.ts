import { ConflictException, Injectable } from '@nestjs/common';
import { ClientEscopado, Prisma } from '@trino/db';
import { conflitoDeUnicidade, ehViolacaoDeUnicidade, naoEncontrado, requisicaoInvalida } from '../comum/erros';
import { ElegibilidadeService } from '../fornecedores/elegibilidade.service';
import {
  assertEscolhaJustificada,
  equalizar,
  EqualizacaoError,
  PesosEqualizacao,
  PESOS_PADRAO,
  prazoPagamentoDeCondicao,
  PropostaParaEqualizar,
  validarPesos,
} from './dominio/equalizacao';
import { AbrirCotacaoDto, EqualizarDto, RegistrarPropostaDto } from './dto/cotacoes.dto';

export interface ContextoAutor {
  usuarioId: string;
  ip?: string | null;
  userAgent?: string | null;
  correlationId?: string | null;
}

const traduzirEqualizacao = (e: unknown) =>
  e instanceof EqualizacaoError ? new ConflictException({ codigo: e.codigo, mensagem: e.message, detalhe: e.detalhe }) : e;

/**
 * F5 — abertura da cotação (RFQ).
 *
 * Três coisas acontecem juntas, e é por isso que moram no mesmo caso de uso:
 *  1. os itens das requisições viram rfq_item com quantidade consolidada — uma
 *     cotação pode agrupar itens de várias SCs, que é o ganho de escala;
 *  2. só fornecedor ELEGÍVEL é convidado (homologado, certidões em dia e, para
 *     item que exige CA, com CA válido daquela variante) — a checagem é a mesma
 *     da F2, para não existirem duas definições de "fornecedor apto";
 *  3. o SLA das requisições envolvidas é CONGELADO: enquanto o mercado responde,
 *     o relógio não corre contra o time de compras.
 */
@Injectable()
export class AbrirCotacaoUseCase {
  constructor(private readonly elegibilidade: ElegibilidadeService) {}

  async executar(db: ClientEscopado, dto: AbrirCotacaoDto, autor: ContextoAutor) {
    const pesos = (dto.criterioEqualizacao ?? PESOS_PADRAO) as PesosEqualizacao;
    try {
      validarPesos(pesos);
    } catch (e) {
      throw traduzirEqualizacao(e);
    }
    if (dto.dispensaCotacao && !(dto.justificativaDispensa ?? '').trim()) {
      throw requisicaoInvalida('COT-ERR-001', 'Dispensa de cotação exige justificativa.');
    }

    const itens = await db.itemRequisicao.findMany({ where: { id: { in: dto.itensRequisicaoIds } } });
    if (itens.length !== dto.itensRequisicaoIds.length) {
      throw naoEncontrado('COT-ERR-404', 'Algum item de requisição não existe neste tenant.');
    }

    const requisicoes = await db.requisicaoCompra.findMany({
      where: { id: { in: [...new Set(itens.map((i: any) => i.requisicaoId))] } },
    });
    const foraDeCotacao = requisicoes.filter((r: any) => r.status !== 'EM_COTACAO');
    if (foraDeCotacao.length > 0) {
      throw conflitoDeUnicidade(
        'COT-ERR-002',
        'Só entram em cotação itens de requisições em EM_COTACAO.',
        );
    }

    // Elegibilidade por fornecedor: avaliada contra CADA variante da RFQ, porque
    // é a variante que decide se o CA é exigido.
    const variantes = [...new Set(itens.map((i: any) => i.varianteId))];
    const convidados: string[] = [];
    const recusados: { fornecedorId: string; motivos: unknown[] }[] = [];
    for (const fornecedorId of dto.fornecedorIds) {
      const motivos: unknown[] = [];
      for (const varianteId of variantes) {
        const resultado = await this.elegibilidade.avaliar(db, fornecedorId, varianteId);
        if (!resultado.elegivel) motivos.push(...resultado.motivos);
      }
      if (motivos.length === 0) convidados.push(fornecedorId);
      else recusados.push({ fornecedorId, motivos: [...new Map(motivos.map((m: any) => [m.codigo, m])).values()] });
    }
    if (convidados.length === 0) {
      throw conflitoDeUnicidade(
        'COT-ERR-003',
        'Nenhum fornecedor elegível entre os informados — a cotação nasceria vazia.',
      );
    }

    const numero = await proximoNumero(db, 'seq_cotacao', 'COT');
    const cotacao = await db.processoCotacao.create({
      data: {
        numero,
        compradorId: autor.usuarioId,
        dataLimiteResposta: new Date(dto.dataLimiteResposta),
        criterioEqualizacao: pesos as any,
        dispensaCotacao: dto.dispensaCotacao ?? false,
        justificativaDispensa: dto.justificativaDispensa?.trim() || null,
      },
    });

    for (const item of itens) {
      await db.rfqItem.create({
        data: { cotacaoId: cotacao.id, itemRequisicaoId: item.id, quantidadeConsolidada: item.quantidade },
      });
      await db.itemRequisicao.update({ where: { id: item.id }, data: { status: 'EM_COTACAO' } });
    }

    for (const fornecedorId of convidados) {
      await db.conviteFornecedor.create({ data: { cotacaoId: cotacao.id, fornecedorId } });
    }

    // Congela o SLA de cada requisição que entrou na cotação.
    const agora = new Date();
    for (const requisicao of requisicoes) {
      if (!requisicao.slaPausadoEm) {
        await db.requisicaoCompra.update({
          where: { id: requisicao.id },
          data: { slaPausadoEm: agora, version: { increment: 1 } },
        });
      }
    }

    await registrarAuditoria(db, autor, 'processo_cotacao', cotacao.id, 'ABRIR_COTACAO', null, {
      numero,
      itens: itens.length,
      convidados,
      recusados,
      requisicoesComSlaPausado: requisicoes.map((r: any) => r.id),
    });

    return { ...(await obterCotacao(db, cotacao.id)), convidados, recusados };
  }
}

/**
 * F5 — proposta do fornecedor. O valor dos itens é DERIVADO (preço unitário ×
 * quantidade consolidada da RFQ) e o `valor_total` é COLUNA GERADA no banco
 * (itens + frete − desconto): ninguém escreve o total, então ele não tem como
 * divergir das parcelas.
 */
@Injectable()
export class RegistrarPropostaUseCase {
  async executar(db: ClientEscopado, cotacaoId: string, dto: RegistrarPropostaDto, autor: ContextoAutor) {
    const cotacao = await db.processoCotacao.findUnique({ where: { id: cotacaoId } });
    if (!cotacao) throw naoEncontrado('COT-ERR-404', 'Cotação não encontrada neste tenant.');
    if (cotacao.status !== 'ABERTA') {
      throw conflitoDeUnicidade('COT-ERR-004', `Cotação ${cotacao.status}: não recebe mais propostas.`);
    }

    const convite = await db.conviteFornecedor.findFirst({
      where: { cotacaoId, fornecedorId: dto.fornecedorId },
    });
    if (!convite) {
      throw conflitoDeUnicidade('COT-ERR-005', 'Fornecedor não foi convidado para esta cotação.');
    }

    const rfqItens = await db.rfqItem.findMany({ where: { cotacaoId } });
    const porId = new Map(rfqItens.map((r: any) => [r.id, r]));
    if (dto.itens.length !== rfqItens.length) {
      throw requisicaoInvalida(
        'COT-ERR-006',
        `A proposta precisa cotar todos os ${rfqItens.length} itens da RFQ (recebidos ${dto.itens.length}).`,
      );
    }

    let valorItens = 0;
    for (const item of dto.itens) {
      const rfq = porId.get(item.rfqItemId);
      if (!rfq) throw naoEncontrado('COT-ERR-407', 'Item cotado não pertence a esta RFQ.');
      valorItens += Number(rfq.quantidadeConsolidada) * item.precoUnitario;
    }
    valorItens = Number(valorItens.toFixed(2));

    const frete = dto.frete ?? 0;
    const desconto = dto.desconto ?? 0;
    if (desconto > valorItens + frete) {
      throw requisicaoInvalida('COT-ERR-007', 'Desconto maior que o valor da proposta.');
    }

    let proposta;
    try {
      proposta = await db.proposta.create({
        data: {
          cotacaoId,
          fornecedorId: dto.fornecedorId,
          valorItens,
          frete,
          desconto,
          condicaoPagamento: dto.condicaoPagamento.trim(),
          prazoEntregaDias: dto.prazoEntregaDias,
          validadeProposta: new Date(`${dto.validadeProposta.slice(0, 10)}T00:00:00.000Z`),
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('COT-ERR-008', 'Este fornecedor já enviou proposta para esta cotação.');
      }
      throw e;
    }

    for (const item of dto.itens) {
      await db.itemProposta.create({
        data: {
          propostaId: proposta.id,
          rfqItemId: item.rfqItemId,
          precoUnitario: item.precoUnitario,
          marca: item.marca ?? null,
          prazoItemDias: item.prazoItemDias ?? null,
        },
      });
    }

    await db.conviteFornecedor.update({ where: { id: convite.id }, data: { status: 'RESPONDIDO' } });
    // Releitura obrigatória: valor_total é coluna GERADA, só existe depois do insert.
    const completa = (await db.proposta.findUnique({ where: { id: proposta.id } }))!;
    await registrarAuditoria(db, autor, 'proposta', proposta.id, 'REGISTRAR_PROPOSTA', null, {
      cotacaoId,
      fornecedorId: dto.fornecedorId,
      valorItens,
      frete,
      desconto,
      valorTotal: String(completa.valorTotal),
    });

    return { ...completa, itens: await db.itemProposta.findMany({ where: { propostaId: proposta.id } }) };
  }
}

/**
 * F5 — equalização. A matriz de notas é calculada pelo domínio PURO; aqui só
 * entram a leitura, a guarda da justificativa e a gravação. Escolher quem não é
 * o menor preço exige justificativa (o banco repete em ck_equalizacao_desvio),
 * e a matriz inteira fica gravada em `notas`: quem auditar daqui a um ano vê
 * exatamente as contas que levaram àquela escolha.
 */
@Injectable()
export class EqualizarPropostasUseCase {
  async executar(db: ClientEscopado, cotacaoId: string, dto: EqualizarDto, autor: ContextoAutor) {
    const cotacao = await db.processoCotacao.findUnique({ where: { id: cotacaoId } });
    if (!cotacao) throw naoEncontrado('COT-ERR-404', 'Cotação não encontrada neste tenant.');
    if (cotacao.status === 'CANCELADA') {
      throw conflitoDeUnicidade('COT-ERR-009', 'Cotação cancelada não é equalizada.');
    }

    const propostas = await db.proposta.findMany({ where: { cotacaoId } });
    if (propostas.length === 0) {
      throw conflitoDeUnicidade('COT-ERR-010', 'Nenhuma proposta recebida para equalizar.');
    }

    const paraEqualizar: PropostaParaEqualizar[] = propostas.map((p: any) => ({
      id: p.id,
      fornecedorId: p.fornecedorId,
      valorTotal: Number(p.valorTotal),
      frete: Number(p.frete),
      prazoEntregaDias: p.prazoEntregaDias,
      prazoPagamentoDias: prazoPagamentoDeCondicao(p.condicaoPagamento),
    }));

    let resultado;
    try {
      resultado = equalizar(paraEqualizar, cotacao.criterioEqualizacao as unknown as PesosEqualizacao);
      // Sem escolha explícita, vence a melhor nota.
      const escolhida = dto.propostaVencedoraId ?? resultado.vencedoraPorNota.propostaId;
      assertEscolhaJustificada(resultado, escolhida, dto.justificativaDesvio);
      var vencedoraId: string = escolhida;
    } catch (e) {
      throw traduzirEqualizacao(e);
    }

    const equalizacao = await db.equalizacao.create({
      data: {
        cotacaoId,
        propostaVencedoraId: vencedoraId,
        propostaMenorPrecoId: resultado.menorPreco.propostaId,
        notas: {
          pesos: resultado.pesos,
          matriz: resultado.matriz,
          vencedoraPorNota: resultado.vencedoraPorNota.propostaId,
          escolhaDoComprador: vencedoraId,
          equalizadoEm: new Date().toISOString(),
        } as any,
        justificativaDesvio: dto.justificativaDesvio?.trim() || null,
        equalizadoPor: autor.usuarioId,
      },
    });

    await db.processoCotacao.update({
      where: { id: cotacaoId },
      data: { status: 'ENCERRADA', encerradaEm: new Date(), version: { increment: 1 } },
    });

    await registrarAuditoria(db, autor, 'equalizacao', equalizacao.id, 'EQUALIZAR_COTACAO', null, {
      cotacaoId,
      vencedora: vencedoraId,
      menorPreco: resultado.menorPreco.propostaId,
      desvioDoMenorPreco: vencedoraId !== resultado.menorPreco.propostaId,
      matriz: resultado.matriz,
    });

    return { equalizacao, resultado };
  }
}

/** Numeração por sequência dedicada: PREFIXO-AAAA-NNNNNN. */
export async function proximoNumero(db: ClientEscopado, sequencia: string, prefixo: string): Promise<string> {
  const [linha] = await db.$queryRaw(
    Prisma.sql`SELECT nextval(${`compras.${sequencia}`}::regclass) AS valor`,
  );
  return `${prefixo}-${new Date().getUTCFullYear()}-${String(linha.valor).padStart(6, '0')}`;
}

export async function obterCotacao(db: ClientEscopado, id: string) {
  const cotacao = await db.processoCotacao.findUnique({ where: { id } });
  if (!cotacao) throw naoEncontrado('COT-ERR-404', 'Cotação não encontrada neste tenant.');
  const [itens, convites, propostas, equalizacao] = await Promise.all([
    db.rfqItem.findMany({ where: { cotacaoId: id } }),
    db.conviteFornecedor.findMany({ where: { cotacaoId: id } }),
    db.proposta.findMany({ where: { cotacaoId: id } }),
    db.equalizacao.findFirst({ where: { cotacaoId: id } }),
  ]);
  return { ...cotacao, itens, convites, propostas, equalizacao };
}

/** Auditoria dos passos de cotação — mesma trilha das demais fases. */
export async function registrarAuditoria(
  db: ClientEscopado,
  autor: ContextoAutor,
  entidade: string,
  entityId: string,
  acao: string,
  antes: unknown,
  depois: unknown,
) {
  await db.auditLog.create({
    data: {
      actorId: autor.usuarioId,
      entidade,
      entityId,
      acao,
      beforeJson: (antes ?? undefined) as any,
      afterJson: (depois ?? undefined) as any,
      ip: autor.ip ?? null,
      userAgent: autor.userAgent ?? null,
      correlationId: autor.correlationId ?? null,
    },
  });
}
