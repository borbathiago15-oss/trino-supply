-- AlterTable
ALTER TABLE "compras"."requisicao_compra" ADD COLUMN     "estouro_autorizado_em" TIMESTAMPTZ(6),
ADD COLUMN     "estouro_autorizado_por" UUID,
ADD COLUMN     "orcamento_estourado" BOOLEAN NOT NULL DEFAULT false,
ADD COLUMN     "orcamento_snapshot" JSONB;

-- AddForeignKey
ALTER TABLE "compras"."requisicao_compra" ADD CONSTRAINT "requisicao_compra_estouro_autorizado_por_fkey" FOREIGN KEY ("estouro_autorizado_por") REFERENCES "core"."usuario"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- ---------------------------------------------------------------------------
-- Escrito à mão: coerência da autorização de estouro.
-- ---------------------------------------------------------------------------

-- Só se autoriza o que de fato estourou, e autor e data andam juntos.
ALTER TABLE "compras"."requisicao_compra"
  ADD CONSTRAINT "ck_requisicao_estouro_autorizacao" CHECK (
    ("estouro_autorizado_por" IS NULL) = ("estouro_autorizado_em" IS NULL)
    AND ("estouro_autorizado_por" IS NULL OR "orcamento_estourado")
  );

-- Fila de análise: as requisições que estouraram o orçamento.
CREATE INDEX "ix_requisicao_estouro"
  ON "compras"."requisicao_compra" ("tenant_id", "status")
  WHERE "orcamento_estourado";
