import { Injectable, Logger, OnModuleDestroy } from '@nestjs/common';
import { Queue, Worker } from 'bullmq';

// O BullMQ recusa ':' no nome da fila (usa o caractere como separador de chave
// no Redis); o prefixo de namespace vai em prefix, não no nome.
export const FILA_IMPORTACAO = 'trino-importacao';

export interface JobImportacao {
  loteId: string;
  tenantId: string;
}

/** Conexão Redis do BullMQ, por ambiente (REDIS_URL ou host/porta). */
export function conexaoRedis() {
  const url = process.env.REDIS_URL;
  if (url) {
    const u = new URL(url);
    return {
      host: u.hostname,
      port: Number(u.port || 6379),
      ...(u.password ? { password: u.password } : {}),
      maxRetriesPerRequest: null as null,
    };
  }
  return {
    host: process.env.REDIS_HOST ?? '127.0.0.1',
    port: Number(process.env.REDIS_PORT ?? 6379),
    maxRetriesPerRequest: null as null,
  };
}

/**
 * Fila de importação. Envolver o BullMQ num provider próprio mantém o resto do
 * código sem saber de Redis — e permite o modo SÍNCRONO (IMPORTACAO_SINCRONA=1)
 * usado nos testes, em que o processamento roda em memória logo após o enfileiramento.
 */
@Injectable()
export class FilaImportacao implements OnModuleDestroy {
  private readonly log = new Logger(FilaImportacao.name);
  private fila: Queue | null = null;
  private worker: Worker | null = null;
  private processador: ((job: JobImportacao) => Promise<void>) | null = null;

  /**
   * Sem Redis configurado, a fila degrada para processamento SÍNCRONO e avisa —
   * é melhor que um job silenciosamente perdido ou um worker em laço de
   * reconexão. Em produção, configure REDIS_URL (ou REDIS_HOST/REDIS_PORT).
   */
  get sincrono(): boolean {
    if (process.env.IMPORTACAO_SINCRONA === '1') return true;
    const temRedis = !!(process.env.REDIS_URL || process.env.REDIS_HOST || process.env.REDIS_PORT);
    if (!temRedis && !this.avisou) {
      this.avisou = true;
      this.log.warn('Redis não configurado: importações serão processadas de forma síncrona.');
    }
    return !temRedis;
  }

  private avisou = false;

  private get conexao() {
    if (!this.fila) this.fila = new Queue(FILA_IMPORTACAO, { connection: conexaoRedis() });
    return this.fila;
  }

  /** Enfileira o processamento. Não espera o worker — a requisição HTTP volta na hora. */
  async enfileirar(job: JobImportacao): Promise<void> {
    if (this.sincrono) {
      if (this.processador) await this.processador(job);
      return;
    }
    await this.conexao.add('processar-lote', job, {
      attempts: 3,
      backoff: { type: 'exponential', delay: 5000 },
      removeOnComplete: 100,
      removeOnFail: 500,
    });
  }

  /** Liga o worker (chamado pelo módulo na subida, fora do modo síncrono). */
  registrarProcessador(processador: (job: JobImportacao) => Promise<void>) {
    this.processador = processador;
    if (this.sincrono || this.worker) return;
    this.worker = new Worker(
      FILA_IMPORTACAO,
      async (job) => processador(job.data as JobImportacao),
      { connection: conexaoRedis(), concurrency: Number(process.env.IMPORTACAO_CONCORRENCIA ?? 2) },
    );
    this.worker.on('failed', (job, erro) => {
      this.log.error(`Lote ${job?.data?.loteId} falhou: ${erro?.message}`);
    });
  }

  async onModuleDestroy() {
    await this.worker?.close();
    await this.fila?.close();
  }
}
