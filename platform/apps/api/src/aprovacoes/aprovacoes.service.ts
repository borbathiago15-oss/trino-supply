import { Injectable } from '@nestjs/common';
import { ClientEscopado } from '@trino/db';
import {
  conflitoDeUnicidade,
  ehNaoEncontrado,
  ehViolacaoDeUnicidade,
  naoEncontrado,
  requisicaoInvalida,
} from '../comum/erros';
import {
  AbrirInstanciaDto,
  CriarDelegacaoDto,
  CriarRegraAlcadaDto,
  DefinirAprovadorDto,
} from './dto/aprovacoes.dto';
import { AlcadaError, RegraAlcadaVigente, regraParaValor, regraVigenteEm } from './dominio/faixas-alcada';

const soDia = (iso: string) => new Date(`${iso.slice(0, 10)}T00:00:00.000Z`);

/** Regra do banco → forma do domínio (Decimal do Prisma vira number). */
function paraDominio(r: any): RegraAlcadaVigente {
  return {
    id: r.id,
    nivel: r.nivel,
    papelExigido: r.papelExigido,
    valorMin: Number(r.valorMin),
    valorMax: r.valorMax === null ? null : Number(r.valorMax),
    vigenciaInicio: r.vigenciaInicio,
    vigenciaFim: r.vigenciaFim,
  };
}

@Injectable()
export class AprovacoesService {
  // ----- regras de alçada --------------------------------------------------
  listarRegras(db: ClientEscopado) {
    return db.regraAlcada.findMany({ orderBy: [{ nivel: 'asc' }, { vigenciaInicio: 'desc' }] });
  }

  async criarRegra(db: ClientEscopado, dto: CriarRegraAlcadaDto) {
    if (dto.valorMax !== undefined && dto.valorMax <= dto.valorMin) {
      throw requisicaoInvalida('ALC-ERR-003', 'valorMax precisa ser maior que valorMin.');
    }
    try {
      return await db.regraAlcada.create({
        data: {
          nivel: dto.nivel,
          papelExigido: dto.papelExigido.trim(),
          valorMin: dto.valorMin,
          valorMax: dto.valorMax ?? null,
          vigenciaInicio: soDia(dto.vigenciaInicio),
          vigenciaFim: dto.vigenciaFim ? soDia(dto.vigenciaFim) : null,
        },
      });
    } catch (e) {
      // ex_regra_nivel_vigencia: duas regras do mesmo nível vigentes ao mesmo tempo.
      if (String((e as Error)?.message ?? '').includes('ex_regra_nivel_vigencia')) {
        throw conflitoDeUnicidade(
          'ALC-ERR-004',
          'Já existe regra vigente para este nível no período — encerre a anterior antes.',
        );
      }
      throw e;
    }
  }

  // ----- aprovadores por centro de custo -----------------------------------
  listarAprovadores(db: ClientEscopado, centroCustoId?: string) {
    return db.aprovadorCentroCusto.findMany({
      where: centroCustoId ? { centroCustoId } : undefined,
      orderBy: [{ nivel: 'asc' }, { ordem: 'asc' }],
    });
  }

  async definirAprovador(db: ClientEscopado, dto: DefinirAprovadorDto) {
    await this.exigir(db, 'centroCusto', dto.centroCustoId, 'CAT-ERR-408', 'Centro de custo não encontrado neste tenant.');
    await this.exigir(db, 'usuario', dto.usuarioId, 'USR-ERR-404', 'Usuário não encontrado neste tenant.');
    try {
      return await db.aprovadorCentroCusto.create({
        data: {
          centroCustoId: dto.centroCustoId,
          usuarioId: dto.usuarioId,
          nivel: dto.nivel,
          ordem: dto.ordem ?? 1,
          ...(dto.vigenciaInicio ? { vigenciaInicio: soDia(dto.vigenciaInicio) } : {}),
          vigenciaFim: dto.vigenciaFim ? soDia(dto.vigenciaFim) : null,
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('ALC-ERR-005', 'Este usuário já tem alçada neste nível e centro de custo a partir desta data.');
      }
      throw e;
    }
  }

  // ----- delegações --------------------------------------------------------
  listarDelegacoes(db: ClientEscopado, delegadoId?: string) {
    return db.delegacaoAlcada.findMany({
      where: delegadoId ? { delegadoId } : undefined,
      orderBy: { vigenciaFim: 'desc' },
    });
  }

  async criarDelegacao(db: ClientEscopado, dto: CriarDelegacaoDto) {
    if (dto.deleganteId === dto.delegadoId) {
      throw requisicaoInvalida('ALC-ERR-006', 'Delegante e delegado precisam ser pessoas diferentes.');
    }
    const inicio = new Date(dto.vigenciaInicio);
    const fim = new Date(dto.vigenciaFim);
    if (fim <= inicio) {
      throw requisicaoInvalida('ALC-ERR-007', 'vigenciaFim precisa ser posterior a vigenciaInicio.');
    }
    await this.exigir(db, 'usuario', dto.deleganteId, 'USR-ERR-404', 'Delegante não encontrado neste tenant.');
    await this.exigir(db, 'usuario', dto.delegadoId, 'USR-ERR-404', 'Delegado não encontrado neste tenant.');
    if (dto.centroCustoId) {
      await this.exigir(db, 'centroCusto', dto.centroCustoId, 'CAT-ERR-408', 'Centro de custo não encontrado neste tenant.');
    }
    return db.delegacaoAlcada.create({
      data: {
        deleganteId: dto.deleganteId,
        delegadoId: dto.delegadoId,
        centroCustoId: dto.centroCustoId ?? null,
        motivo: dto.motivo.trim(),
        vigenciaInicio: inicio,
        vigenciaFim: fim,
      },
    });
  }

  async encerrarDelegacao(db: ClientEscopado, id: string, ativa: boolean) {
    try {
      return await db.delegacaoAlcada.update({ where: { id }, data: { ativa } });
    } catch (e) {
      if (ehNaoEncontrado(e)) throw naoEncontrado('ALC-ERR-404', 'Delegação não encontrada neste tenant.');
      throw e;
    }
  }

  // ----- instâncias --------------------------------------------------------
  async obterInstancia(db: ClientEscopado, id: string) {
    const instancia = await db.instanciaAprovacao.findUnique({ where: { id } });
    if (!instancia) throw naoEncontrado('APV-ERR-404', 'Instância de aprovação não encontrada neste tenant.');
    const etapas = await db.etapaAprovacao.findMany({ where: { instanciaId: id }, orderBy: { nivel: 'asc' } });
    return { ...instancia, etapas };
  }

  /**
   * Abre a instância de aprovação de uma requisição: o valor determina o nível
   * exigido pelas regras VIGENTES, e a regra usada é congelada em
   * `regra_snapshot` — mudar a alçada depois não reescreve o que já está em
   * curso. O snapshot também carrega o `centroCustoId`, que é como a política
   * sabe onde checar vigência (a tabela de instância não tem essa coluna), e o
   * `nivelFinal`, que é quem pode autorizar estouro.
   * As etapas 1..nível exigido nascem PENDENTES.
   *
   * R09: se a requisição estourou o orçamento, a instância ESCALA até o
   * aprovador final, mesmo que o valor sozinho parasse antes. Sem isso a regra
   * seria letra morta — quem tem alçada para liberar o estouro nunca veria a
   * requisição.
   */
  async abrirInstancia(db: ClientEscopado, dto: AbrirInstanciaDto) {
    await this.exigir(db, 'centroCusto', dto.centroCustoId, 'CAT-ERR-408', 'Centro de custo não encontrado neste tenant.');
    await this.exigir(db, 'usuario', dto.solicitanteId, 'USR-ERR-404', 'Solicitante não encontrado neste tenant.');
    if (dto.compradorId) {
      await this.exigir(db, 'usuario', dto.compradorId, 'USR-ERR-404', 'Comprador não encontrado neste tenant.');
    }

    const agora = new Date();
    const regras = (await db.regraAlcada.findMany({})).map(paraDominio);
    let regra;
    try {
      regra = regraParaValor(dto.valorBase, regras, agora);
    } catch (e) {
      if (e instanceof AlcadaError) throw requisicaoInvalida(e.codigo, e.message);
      throw e;
    }

    // Aprovador final = topo das regras vigentes.
    const nivelFinal = regras
      .filter((r) => regraVigenteEm(r, agora))
      .reduce((maior, r) => Math.max(maior, r.nivel), regra.nivel);

    // R09: estouro sobe até o final.
    const requisicao = await db.requisicaoCompra.findUnique({ where: { id: dto.requisicaoId } });
    const escalonadoPorEstouro = requisicao?.orcamentoEstourado === true && regra.nivel < nivelFinal;
    const nivelExigido = escalonadoPorEstouro ? nivelFinal : regra.nivel;

    const snapshot = {
      centroCustoId: dto.centroCustoId,
      capturadoEm: agora.toISOString(),
      nivelFinal,
      nivelPorValor: regra.nivel,
      escalonadoPorEstouro,
      orcamentoEstourado: requisicao?.orcamentoEstourado ?? false,
      orcamentoSnapshot: requisicao?.orcamentoSnapshot ?? null,
      regra: {
        id: regra.id,
        nivel: regra.nivel,
        papelExigido: regra.papelExigido,
        valorMin: regra.valorMin,
        valorMax: regra.valorMax,
        vigenciaInicio: regra.vigenciaInicio,
        vigenciaFim: regra.vigenciaFim,
      },
    };

    try {
      const instancia = await db.instanciaAprovacao.create({
        data: {
          requisicaoId: dto.requisicaoId,
          valorBase: dto.valorBase,
          nivelExigido,
          regraSnapshot: snapshot as any,
        },
      });

      // Uma etapa por nível até o exigido: aprovação sobe degrau por degrau.
      for (let nivel = 1; nivel <= nivelExigido; nivel += 1) {
        await db.etapaAprovacao.create({
          data: {
            instanciaId: instancia.id,
            nivel,
            solicitanteId: dto.solicitanteId,
            compradorId: dto.compradorId ?? null,
          },
        });
      }
      return this.obterInstancia(db, instancia.id);
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('APV-ERR-006', 'Já existe uma instância PENDENTE para esta requisição.');
      }
      throw e;
    }
  }

  private async exigir(db: ClientEscopado, delegate: string, id: string, codigo: string, mensagem: string) {
    if (!(await (db as any)[delegate].findUnique({ where: { id } }))) {
      throw naoEncontrado(codigo, mensagem);
    }
  }
}
