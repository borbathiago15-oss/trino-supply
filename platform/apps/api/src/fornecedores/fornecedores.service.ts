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
  AlterarStatusDto,
  AtualizarFornecedorDto,
  CriarFornecedorDto,
  RegistrarCaDto,
  RegistrarDocumentoDto,
  STATUS_HOMOLOGACAO,
  StatusHomologacao,
  VincularSkuDto,
} from './dto/fornecedores.dto';
import { ElegibilidadeService } from './elegibilidade.service';

/** Data ISO (AAAA-MM-DD) → meia-noite UTC, como o Postgres guarda DATE. */
function paraData(iso: string): Date {
  return new Date(`${iso.slice(0, 10)}T00:00:00.000Z`);
}

@Injectable()
export class FornecedoresService {
  constructor(private readonly elegibilidade: ElegibilidadeService) {}

  listar(db: ClientEscopado, status?: string) {
    if (status && !STATUS_HOMOLOGACAO.includes(status as StatusHomologacao)) {
      throw requisicaoInvalida('FOR-ERR-005', `status deve ser um de: ${STATUS_HOMOLOGACAO.join(', ')}.`);
    }
    return db.fornecedor.findMany({
      where: status ? { statusHomologacao: status } : undefined,
      orderBy: { razaoSocial: 'asc' },
    });
  }

  async obter(db: ClientEscopado, id: string) {
    const fornecedor = await db.fornecedor.findUnique({ where: { id } });
    if (!fornecedor) throw naoEncontrado('FOR-ERR-404', 'Fornecedor não encontrado neste tenant.');
    const [documentos, certificados, skus] = await Promise.all([
      db.documentoFornecedor.findMany({ where: { fornecedorId: id }, orderBy: { dataValidade: 'desc' } }),
      db.certificadoAprovacao.findMany({ where: { fornecedorId: id }, orderBy: { dataValidade: 'desc' } }),
      db.fornecedorSku.findMany({ where: { fornecedorId: id } }),
    ]);
    return { ...fornecedor, documentos, certificados, skus };
  }

  async criar(db: ClientEscopado, dto: CriarFornecedorDto) {
    try {
      return await db.fornecedor.create({
        data: {
          cnpj: dto.cnpj,
          razaoSocial: dto.razaoSocial.trim(),
          nomeFantasia: dto.nomeFantasia ?? null,
          emailContato: dto.emailContato?.toLowerCase() ?? null,
          telefone: dto.telefone ?? null,
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('FOR-ERR-001', 'Já existe fornecedor com este CNPJ no tenant.');
      }
      throw e;
    }
  }

  async atualizar(db: ClientEscopado, id: string, dto: AtualizarFornecedorDto) {
    try {
      return await db.fornecedor.update({ where: { id }, data: { ...dto } });
    } catch (e) {
      throw this.traduzir(e);
    }
  }

  /**
   * Homologação é decisão auditável: quem sai de HOMOLOGADO precisa dizer por
   * quê, e o carimbo de homologação registra quando entrou.
   */
  async alterarStatus(db: ClientEscopado, id: string, dto: AlterarStatusDto) {
    if (dto.status !== 'HOMOLOGADO' && !dto.motivo) {
      throw requisicaoInvalida('FOR-ERR-002', 'Informe o motivo ao suspender, bloquear ou despromover o fornecedor.');
    }
    try {
      return await db.fornecedor.update({
        where: { id },
        data: {
          statusHomologacao: dto.status,
          motivoStatus: dto.motivo ?? null,
          homologadoEm: dto.status === 'HOMOLOGADO' ? new Date() : null,
        },
      });
    } catch (e) {
      throw this.traduzir(e);
    }
  }

  // ----- documentos --------------------------------------------------------
  listarDocumentos(db: ClientEscopado, fornecedorId: string) {
    return db.documentoFornecedor.findMany({ where: { fornecedorId }, orderBy: { dataValidade: 'desc' } });
  }

  /**
   * Registro do documento (o arquivo em si vive fora do banco: guardamos a URI).
   * ck_documento_datas garante validade >= emissão no banco; validamos antes
   * para devolver erro de negócio em vez de erro de constraint.
   */
  async registrarDocumento(db: ClientEscopado, fornecedorId: string, dto: RegistrarDocumentoDto) {
    await this.exigirFornecedor(db, fornecedorId);
    const emissao = paraData(dto.dataEmissao);
    const validade = paraData(dto.dataValidade);
    if (validade < emissao) {
      throw requisicaoInvalida('FOR-ERR-003', 'dataValidade não pode ser anterior à dataEmissao.');
    }
    return db.documentoFornecedor.create({
      data: {
        fornecedorId,
        tipo: dto.tipo,
        numero: dto.numero ?? null,
        dataEmissao: emissao,
        dataValidade: validade,
        obrigatorio: dto.obrigatorio ?? true,
        arquivoUri: dto.arquivoUri ?? null,
      },
    });
  }

  // ----- certificados de aprovação (EPI) -----------------------------------
  async registrarCa(db: ClientEscopado, fornecedorId: string, dto: RegistrarCaDto) {
    await this.exigirFornecedor(db, fornecedorId);
    if (!(await db.varianteSku.findUnique({ where: { id: dto.varianteId } }))) {
      throw naoEncontrado('CAT-ERR-407', 'Variante não encontrada neste tenant.');
    }
    try {
      return await db.certificadoAprovacao.create({
        data: {
          fornecedorId,
          varianteId: dto.varianteId,
          numeroCa: dto.numeroCa,
          dataValidade: paraData(dto.dataValidade),
          arquivoUri: dto.arquivoUri ?? null,
        },
      });
    } catch (e) {
      if (ehViolacaoDeUnicidade(e)) {
        throw conflitoDeUnicidade('FOR-ERR-004', 'Este CA já está registrado para a variante e o fornecedor.');
      }
      throw e;
    }
  }

  // ----- vínculo fornecedor × SKU ------------------------------------------
  /**
   * Vincular SKU é entrada em processo de compra: só passa fornecedor elegível
   * (homologado, certidões válidas e, para EPI, com CA válido da variante).
   */
  async vincularSku(db: ClientEscopado, fornecedorId: string, dto: VincularSkuDto) {
    await this.exigirFornecedor(db, fornecedorId);
    if (!(await db.varianteSku.findUnique({ where: { id: dto.varianteId } }))) {
      throw naoEncontrado('CAT-ERR-407', 'Variante não encontrada neste tenant.');
    }
    await this.elegibilidade.exigirElegivel(db, fornecedorId, dto.varianteId);

    const dados = {
      ultimoPreco: dto.ultimoPreco,
      moeda: (dto.moeda ?? 'BRL').toUpperCase(),
      leadTimeDias: dto.leadTimeDias ?? 0,
      vigenteEm: new Date(),
    };
    return db.fornecedorSku.upsert({
      where: { fornecedorId_varianteId: { fornecedorId, varianteId: dto.varianteId } },
      update: dados,
      create: { fornecedorId, varianteId: dto.varianteId, ...dados },
    });
  }

  // ----- apoio -------------------------------------------------------------
  private async exigirFornecedor(db: ClientEscopado, id: string) {
    if (!(await db.fornecedor.findUnique({ where: { id } }))) {
      throw naoEncontrado('FOR-ERR-404', 'Fornecedor não encontrado neste tenant.');
    }
  }

  private traduzir(e: unknown) {
    if (ehViolacaoDeUnicidade(e)) {
      return conflitoDeUnicidade('FOR-ERR-001', 'Já existe fornecedor com este CNPJ no tenant.');
    }
    return ehNaoEncontrado(e) ? naoEncontrado('FOR-ERR-404', 'Fornecedor não encontrado neste tenant.') : e;
  }
}
