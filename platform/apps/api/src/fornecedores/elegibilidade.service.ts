import { ConflictException, Injectable } from '@nestjs/common';
import { ClientEscopado } from '@trino/db';
import { naoEncontrado } from '../comum/erros';
import { TipoDocumento } from './dto/fornecedores.dto';

/**
 * Certidões exigidas de qualquer fornecedor para operar. Padrão do negócio
 * (regularidade fiscal, FGTS e trabalhista); ajustável por ambiente com
 * FORNECEDOR_CERTIDOES_EXIGIDAS="CND_FEDERAL,FGTS" (vazio desliga a exigência
 * de presença, mas nunca a de validade).
 */
export const CERTIDOES_EXIGIDAS_PADRAO: TipoDocumento[] = ['CND_FEDERAL', 'FGTS', 'TRABALHISTA'];

export interface MotivoBloqueio {
  codigo: string;
  mensagem: string;
}

export interface ResultadoElegibilidade {
  elegivel: boolean;
  fornecedorId: string;
  statusHomologacao: string;
  motivos: MotivoBloqueio[];
  documentosVencidos: { id: string; tipo: string; dataValidade: Date }[];
  certidoesFaltantes: string[];
  avaliadoEm: string;
}

/** Meia-noite UTC de hoje: datas do banco são DATE, sem hora. */
function hojeUtc(): Date {
  const agora = new Date();
  return new Date(Date.UTC(agora.getUTCFullYear(), agora.getUTCMonth(), agora.getUTCDate()));
}

/**
 * Elegibilidade do fornecedor: a pergunta que precede qualquer vínculo em
 * processo de compra. Bloqueia se o fornecedor não estiver HOMOLOGADO, se
 * houver documento obrigatório vencido, se faltar certidão exigida ou — quando
 * o item exige CA (EPI) — se não houver CA válido para aquela variante.
 * O resultado sempre lista TODOS os motivos, para o comprador resolver de uma vez.
 */
@Injectable()
export class ElegibilidadeService {
  private get certidoesExigidas(): TipoDocumento[] {
    const bruto = process.env.FORNECEDOR_CERTIDOES_EXIGIDAS;
    if (bruto === undefined) return CERTIDOES_EXIGIDAS_PADRAO;
    return bruto
      .split(',')
      .map((t) => t.trim().toUpperCase())
      .filter(Boolean) as TipoDocumento[];
  }

  async avaliar(db: ClientEscopado, fornecedorId: string, varianteId?: string): Promise<ResultadoElegibilidade> {
    const fornecedor = await db.fornecedor.findUnique({ where: { id: fornecedorId } });
    if (!fornecedor) throw naoEncontrado('FOR-ERR-404', 'Fornecedor não encontrado neste tenant.');

    const hoje = hojeUtc();
    const motivos: MotivoBloqueio[] = [];

    if (fornecedor.statusHomologacao !== 'HOMOLOGADO') {
      motivos.push({
        codigo: 'FOR-ELG-001',
        mensagem: `Fornecedor com homologação ${fornecedor.statusHomologacao}${
          fornecedor.motivoStatus ? ` (${fornecedor.motivoStatus})` : ''
        }.`,
      });
    }

    const documentos = await db.documentoFornecedor.findMany({ where: { fornecedorId } });

    // Um documento obrigatório vence e o fornecedor sai de campo — vale o mais
    // recente de cada tipo: reemissão substitui a certidão anterior.
    const maisRecentePorTipo = new Map<string, (typeof documentos)[number]>();
    for (const doc of documentos) {
      const atual = maisRecentePorTipo.get(doc.tipo);
      if (!atual || doc.dataValidade > atual.dataValidade) maisRecentePorTipo.set(doc.tipo, doc);
    }

    const documentosVencidos = [...maisRecentePorTipo.values()]
      .filter((d) => d.obrigatorio && d.dataValidade < hoje)
      .map((d) => ({ id: d.id, tipo: d.tipo, dataValidade: d.dataValidade }));

    if (documentosVencidos.length > 0) {
      motivos.push({
        codigo: 'FOR-ELG-002',
        mensagem: `Documento obrigatório vencido: ${documentosVencidos.map((d) => d.tipo).join(', ')}.`,
      });
    }

    const certidoesFaltantes = this.certidoesExigidas.filter((tipo) => !maisRecentePorTipo.has(tipo));
    if (certidoesFaltantes.length > 0) {
      motivos.push({
        codigo: 'FOR-ELG-003',
        mensagem: `Certidão exigida não cadastrada: ${certidoesFaltantes.join(', ')}.`,
      });
    }

    // Item de EPI: sem CA válido daquele fornecedor para aquela variante, não rola.
    if (varianteId) {
      const variante = await db.varianteSku.findUnique({ where: { id: varianteId } });
      if (!variante) throw naoEncontrado('CAT-ERR-407', 'Variante não encontrada neste tenant.');
      const skuBase = await db.skuBase.findUnique({ where: { id: variante.skuBaseId } });
      if (skuBase?.exigeCa) {
        const caValido = await db.certificadoAprovacao.findFirst({
          where: { fornecedorId, varianteId, dataValidade: { gte: hoje } },
        });
        if (!caValido) {
          motivos.push({
            codigo: 'FOR-ELG-004',
            mensagem: 'Item exige Certificado de Aprovação (CA) e o fornecedor não tem CA válido para esta variante.',
          });
        }
      }
    }

    return {
      elegivel: motivos.length === 0,
      fornecedorId,
      statusHomologacao: fornecedor.statusHomologacao,
      motivos,
      documentosVencidos,
      certidoesFaltantes,
      avaliadoEm: new Date().toISOString(),
    };
  }

  /**
   * Porta de entrada dos processos de compra: ou o fornecedor está elegível,
   * ou a operação para aqui com os motivos.
   */
  async exigirElegivel(db: ClientEscopado, fornecedorId: string, varianteId?: string) {
    const resultado = await this.avaliar(db, fornecedorId, varianteId);
    if (!resultado.elegivel) {
      throw new ConflictException({
        codigo: 'FOR-ELG-409',
        mensagem: 'Fornecedor inelegível para processos de compra.',
        motivos: resultado.motivos,
      });
    }
    return resultado;
  }
}
