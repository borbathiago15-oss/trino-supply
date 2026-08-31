-- Fase F7 — calendário de feriados por tenant (tempo útil do SLA).

-- CreateTable
CREATE TABLE "core"."feriado" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "data" DATE NOT NULL,
    "descricao" VARCHAR(120) NOT NULL,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "feriado_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "uq_feriado_data" ON "core"."feriado"("tenant_id", "data");

-- AddForeignKey
ALTER TABLE "core"."feriado" ADD CONSTRAINT "feriado_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;
