import { Injectable } from '@nestjs/common';
import { ClientEscopado } from '@trino/db';
import { conflitoDeUnicidade, naoEncontrado, requisicaoInvalida } from '../comum/erros';
import { checksumArquivo, lerCsv, TipoLote } from './dominio/parsers';
import { FilaImportacao } from './fila';

export interface EntradaIngestao {
  tipo: TipoLote;
  nomeArquivo: string;
  /** Conteúdo do arquivo (CSV ou planilha exportada em CSV), em texto. */
  conteudo: string;
  arquivoUri?: string | null;
}

/**
 * ImportacaoStagingService — a porta de entrada da ingestão (F8).
 *
 * O que ela garante:
 *  1. IDEMPOTÊNCIA por arquivo: o SHA-256 do conteúdo é a identidade do lote.
 *     Reenviar o mesmo arquivo devolve o lote anterior em vez de duplicar
 *     trabalho — inclusive quando o primeiro ainda está processando.
 *  2. Ingestão que NÃO BLOQUEIA o HTTP: as linhas entram no staging como
 *     PENDENTE e o processamento vai para a fila; a resposta volta na hora com
 *     o id do lote para acompanhamento.
 *  3. Nenhuma linha é validada aqui. Validar é trabalho do worker — o staging
 *     guarda o arquivo como veio, que é o que permite reprocessar e auditar.
 */
@Injectable()
export class ImportacaoStagingService {
  constructor(private readonly fila: FilaImportacao) {}

  async ingerir(db: ClientEscopado, tenantId: string, usuarioId: string, entrada: EntradaIngestao) {
    const checksum = checksumArquivo(entrada.conteudo);

    // Idempotência: mesmo conteúdo, mesmo lote.
    const existente = await db.loteImportacao.findFirst({
      where: { checksumSha256: checksum, tipo: entrada.tipo },
      orderBy: { iniciadoEm: 'desc' },
    });
    if (existente) {
      return { ...existente, jaProcessado: true, mensagem: 'Arquivo idêntico já enviado — nada foi reprocessado.' };
    }

    const { cabecalho, linhas } = lerCsv(entrada.conteudo);
    if (cabecalho.length === 0 || linhas.length === 0) {
      throw requisicaoInvalida('IMP-ERR-001', 'Arquivo vazio ou sem linhas além do cabeçalho.');
    }

    const lote = await db.loteImportacao.create({
      data: {
        tipo: entrada.tipo,
        nomeArquivo: entrada.nomeArquivo,
        arquivoUri: entrada.arquivoUri ?? null,
        totalLinhas: linhas.length,
        checksumSha256: checksum,
        iniciadoPor: usuarioId,
      },
    });

    // createMany em vez de N inserts: arquivo grande não pode virar N round-trips.
    await db.stagingLinhaImportacao.createMany({
      data: linhas.map((l) => ({
        loteId: lote.id,
        numeroLinha: l.numeroLinha,
        conteudoBruto: l.conteudo as any,
      })),
    });

    await this.fila.enfileirar({ loteId: lote.id, tenantId });
    return { ...lote, jaProcessado: false, mensagem: 'Arquivo recebido; o processamento roda em segundo plano.' };
  }

  async obterLote(db: ClientEscopado, loteId: string) {
    const lote = await db.loteImportacao.findUnique({ where: { id: loteId } });
    if (!lote) throw naoEncontrado('IMP-ERR-404', 'Lote de importação não encontrado neste tenant.');
    return lote;
  }

  /**
   * Relatório de inconformidades: as linhas que falharam e por quê.
   * O id do staging é BIGINT e o JSON do Nest não serializa BigInt — vira
   * string na borda, que é o formato que o cliente consegue usar.
   */
  async inconformidades(db: ClientEscopado, loteId: string) {
    const lote = await this.obterLote(db, loteId);
    const linhas = await db.stagingLinhaImportacao.findMany({
      where: { loteId, status: { in: ['ERRO_VALIDACAO', 'ERRO_PERSISTENCIA'] } },
      orderBy: { numeroLinha: 'asc' },
    });
    return { lote, linhas: linhas.map((l: any) => ({ ...l, id: String(l.id) })) };
  }

  /** Mesmo relatório em CSV, para abrir na planilha e corrigir. */
  async inconformidadesCsv(db: ClientEscopado, loteId: string): Promise<{ nome: string; csv: string }> {
    const { lote, linhas } = await this.inconformidades(db, loteId);
    const colunas = [...new Set(linhas.flatMap((l: any) => Object.keys(l.conteudoBruto ?? {})))];
    const cabecalho = ['linha', 'status', 'motivo', ...colunas];
    const escapar = (v: unknown) => {
      const t = String(v ?? '');
      return /[";\n]/.test(t) ? `"${t.replace(/"/g, '""')}"` : t;
    };
    const corpo = linhas.map((l: any) =>
      [l.numeroLinha, l.status, l.mensagemErro ?? '', ...colunas.map((c) => l.conteudoBruto?.[c] ?? '')]
        .map(escapar)
        .join(';'),
    );
    return {
      nome: `inconformidades-${lote.nomeArquivo.replace(/\.[^.]+$/, '')}-${lote.id.slice(0, 8)}.csv`,
      csv: [cabecalho.join(';'), ...corpo].join('\n'),
    };
  }

  private conflito(mensagem: string) {
    return conflitoDeUnicidade('IMP-ERR-002', mensagem);
  }
}
