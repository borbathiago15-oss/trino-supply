-- Numeração das requisições: sequência dedicada, criada antes das tabelas.
CREATE SEQUENCE IF NOT EXISTS "compras"."seq_requisicao";

-- CreateEnum
CREATE TYPE "compras"."status_requisicao" AS ENUM ('RASCUNHO', 'SUBMETIDA', 'EM_TRIAGEM', 'DEVOLVIDA_AJUSTE', 'EM_COTACAO', 'COTADA', 'APROVACAO_ALCADA', 'PEDIDO_GERADO', 'RECEBIDA_PARCIAL', 'RECEBIDA_TOTAL', 'REJEITADA', 'CANCELADA');

-- CreateEnum
CREATE TYPE "compras"."status_item_requisicao" AS ENUM ('ATIVO', 'EM_COTACAO', 'COTADO', 'PEDIDO', 'ATENDIDO', 'CANCELADO');

-- CreateEnum
CREATE TYPE "compras"."prioridade" AS ENUM ('BAIXA', 'NORMAL', 'ALTA', 'EMERGENCIAL');

-- CreateTable
CREATE TABLE "compras"."requisicao_compra" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "numero" VARCHAR(20) NOT NULL,
    "centro_custo_id" UUID NOT NULL,
    "contrato_id" UUID,
    "solicitante_id" UUID NOT NULL,
    "comprador_id" UUID,
    "status" "compras"."status_requisicao" NOT NULL DEFAULT 'RASCUNHO',
    "prioridade" "compras"."prioridade" NOT NULL DEFAULT 'NORMAL',
    "justificativa" TEXT,
    "motivo_recusa" TEXT,
    "valor_estimado" DECIMAL(15,2) NOT NULL DEFAULT 0,
    "valor_aprovado" DECIMAL(15,2),
    "data_necessidade" DATE,
    "origem_automatica" BOOLEAN NOT NULL DEFAULT false,
    "submetida_em" TIMESTAMPTZ(6),
    "triagem_em" TIMESTAMPTZ(6),
    "concluida_em" TIMESTAMPTZ(6),
    "sla_pausado_em" TIMESTAMPTZ(6),
    "sla_segundos_pausados" INTEGER NOT NULL DEFAULT 0,
    "version" INTEGER NOT NULL DEFAULT 0,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "requisicao_compra_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."item_requisicao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "requisicao_id" UUID NOT NULL,
    "variante_id" UUID NOT NULL,
    "sequencia" SMALLINT NOT NULL,
    "quantidade" DECIMAL(15,4) NOT NULL,
    "preco_referencia" DECIMAL(15,4),
    "status" "compras"."status_item_requisicao" NOT NULL DEFAULT 'ATIVO',
    "observacao" VARCHAR(500),

    CONSTRAINT "item_requisicao_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "ix_requisicao_esteira" ON "compras"."requisicao_compra"("tenant_id", "status", "criado_em" DESC);

-- CreateIndex
CREATE INDEX "ix_requisicao_cc" ON "compras"."requisicao_compra"("centro_custo_id", "status");

-- CreateIndex
CREATE INDEX "ix_requisicao_solicitante" ON "compras"."requisicao_compra"("solicitante_id", "status");

-- CreateIndex
CREATE UNIQUE INDEX "uq_requisicao_numero" ON "compras"."requisicao_compra"("tenant_id", "numero");

-- CreateIndex
CREATE INDEX "ix_item_requisicao" ON "compras"."item_requisicao"("requisicao_id", "status");

-- CreateIndex
CREATE INDEX "ix_item_variante" ON "compras"."item_requisicao"("variante_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_item_requisicao_seq" ON "compras"."item_requisicao"("requisicao_id", "sequencia");

-- AddForeignKey
ALTER TABLE "compras"."requisicao_compra" ADD CONSTRAINT "requisicao_compra_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."requisicao_compra" ADD CONSTRAINT "requisicao_compra_centro_custo_id_fkey" FOREIGN KEY ("centro_custo_id") REFERENCES "core"."centro_custo"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."requisicao_compra" ADD CONSTRAINT "requisicao_compra_contrato_id_fkey" FOREIGN KEY ("contrato_id") REFERENCES "core"."contrato_operacao"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."requisicao_compra" ADD CONSTRAINT "requisicao_compra_solicitante_id_fkey" FOREIGN KEY ("solicitante_id") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."requisicao_compra" ADD CONSTRAINT "requisicao_compra_comprador_id_fkey" FOREIGN KEY ("comprador_id") REFERENCES "core"."usuario"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_requisicao" ADD CONSTRAINT "item_requisicao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_requisicao" ADD CONSTRAINT "item_requisicao_requisicao_id_fkey" FOREIGN KEY ("requisicao_id") REFERENCES "compras"."requisicao_compra"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_requisicao" ADD CONSTRAINT "item_requisicao_variante_id_fkey" FOREIGN KEY ("variante_id") REFERENCES "catalogo"."variante_sku"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- ---------------------------------------------------------------------------
-- Escrito à mão: CHECKs e índice PARCIAL que o schema.prisma não expressa.
-- Não remova ao gerar novas migrations.
-- ---------------------------------------------------------------------------

ALTER TABLE "compras"."requisicao_compra"
  ADD CONSTRAINT "ck_requisicao_valores" CHECK (
    "valor_estimado" >= 0 AND ("valor_aprovado" IS NULL OR "valor_aprovado" >= 0)
  );

-- Fora do rascunho, justificativa é obrigatória.
ALTER TABLE "compras"."requisicao_compra"
  ADD CONSTRAINT "ck_requisicao_justificativa" CHECK (
    "status" = 'RASCUNHO' OR ("justificativa" IS NOT NULL AND length(btrim("justificativa")) > 0)
  );

ALTER TABLE "compras"."requisicao_compra"
  ADD CONSTRAINT "ck_requisicao_motivo_recusa" CHECK (
    "status" <> 'REJEITADA' OR ("motivo_recusa" IS NOT NULL AND length(btrim("motivo_recusa")) > 0)
  );

-- Quem pede não é quem compra.
ALTER TABLE "compras"."requisicao_compra"
  ADD CONSTRAINT "ck_requisicao_comprador_distinto" CHECK (
    "comprador_id" IS NULL OR "comprador_id" <> "solicitante_id"
  );

-- Detecção de fracionamento: só interessa o que já foi submetido.
CREATE INDEX "ix_requisicao_fracionamento"
  ON "compras"."requisicao_compra" ("centro_custo_id", "submetida_em")
  WHERE "submetida_em" IS NOT NULL;

ALTER TABLE "compras"."item_requisicao"
  ADD CONSTRAINT "ck_item_quantidade" CHECK ("quantidade" > 0);

ALTER TABLE "compras"."item_requisicao"
  ADD CONSTRAINT "ck_item_preco" CHECK ("preco_referencia" IS NULL OR "preco_referencia" >= 0);
