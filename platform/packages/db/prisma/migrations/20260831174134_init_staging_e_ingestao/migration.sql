-- Fase F8 — staging de ingestão.
-- NOTA: o Prisma reemite um ALTER em compras.proposta.valor_total a cada
-- migration porque não modela COLUNAS GERADAS. Esse ALTER é removido à mão
-- (o Postgres o recusa). Faça o mesmo nas próximas migrations.

-- CreateEnum
CREATE TYPE "core"."tipo_lote_importacao" AS ENUM ('CATALOGO_SKU', 'PARAMETRO_ESTOQUE', 'FORNECEDOR', 'ORCAMENTO_CC');

-- CreateEnum
CREATE TYPE "core"."status_lote_importacao" AS ENUM ('RECEBIDO', 'PROCESSANDO', 'CONCLUIDO_COM_SUCESSO', 'CONCLUIDO_COM_FALHAS', 'FALHA_CRITICA');

-- CreateEnum
CREATE TYPE "core"."status_linha_staging" AS ENUM ('PENDENTE', 'IMPORTADO', 'ERRO_VALIDACAO', 'ERRO_PERSISTENCIA');

-- CreateTable
CREATE TABLE "core"."lote_importacao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "tipo" "core"."tipo_lote_importacao" NOT NULL,
    "nome_arquivo" VARCHAR(255) NOT NULL,
    "arquivo_uri" TEXT,
    "total_linhas" INTEGER NOT NULL DEFAULT 0,
    "linhas_sucesso" INTEGER NOT NULL DEFAULT 0,
    "linhas_erro" INTEGER NOT NULL DEFAULT 0,
    "status" "core"."status_lote_importacao" NOT NULL DEFAULT 'RECEBIDO',
    "iniciado_por" UUID NOT NULL,
    "iniciado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "concluido_em" TIMESTAMPTZ(6),
    "checksum_sha256" VARCHAR(64) NOT NULL,

    CONSTRAINT "lote_importacao_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."staging_linha_importacao" (
    "id" BIGSERIAL NOT NULL,
    "tenant_id" UUID NOT NULL,
    "lote_id" UUID NOT NULL,
    "numero_linha" INTEGER NOT NULL,
    "conteudo_bruto" JSONB NOT NULL,
    "status" "core"."status_linha_staging" NOT NULL DEFAULT 'PENDENTE',
    "mensagem_erro" TEXT,
    "processado_em" TIMESTAMPTZ(6),

    CONSTRAINT "staging_linha_importacao_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "ix_lote_tenant_status" ON "core"."lote_importacao"("tenant_id", "status");

-- CreateIndex
CREATE INDEX "ix_lote_checksum" ON "core"."lote_importacao"("tenant_id", "checksum_sha256");

-- CreateIndex
CREATE INDEX "ix_staging_lote_status" ON "core"."staging_linha_importacao"("lote_id", "status");

-- CreateIndex
CREATE UNIQUE INDEX "uq_staging_linha" ON "core"."staging_linha_importacao"("lote_id", "numero_linha");

-- AddForeignKey
ALTER TABLE "core"."lote_importacao" ADD CONSTRAINT "lote_importacao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."lote_importacao" ADD CONSTRAINT "lote_importacao_iniciado_por_fkey" FOREIGN KEY ("iniciado_por") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."staging_linha_importacao" ADD CONSTRAINT "staging_linha_importacao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."staging_linha_importacao" ADD CONSTRAINT "staging_linha_importacao_lote_id_fkey" FOREIGN KEY ("lote_id") REFERENCES "core"."lote_importacao"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- Escrito à mão: o CHECK dos contadores do DDL.
ALTER TABLE "core"."lote_importacao"
  ADD CONSTRAINT "ck_lote_totais" CHECK ("total_linhas" >= 0 AND "linhas_sucesso" >= 0 AND "linhas_erro" >= 0);
