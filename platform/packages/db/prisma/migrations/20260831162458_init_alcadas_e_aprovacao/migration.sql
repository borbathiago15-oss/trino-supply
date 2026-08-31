-- btree_gist habilita o operador = em índices GiST, exigido pelo EXCLUDE
-- de compras.regra_alcada (tenant_id/nivel com =, daterange com &&).
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- CreateSchema
CREATE SCHEMA IF NOT EXISTS "compras";

-- CreateEnum
CREATE TYPE "compras"."status_instancia" AS ENUM ('PENDENTE', 'APROVADA', 'REJEITADA', 'INVALIDADA');

-- CreateEnum
CREATE TYPE "compras"."decisao_etapa" AS ENUM ('PENDENTE', 'APROVADO', 'REJEITADO');

-- CreateTable
CREATE TABLE "compras"."regra_alcada" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "nivel" SMALLINT NOT NULL,
    "papel_exigido" VARCHAR(60) NOT NULL,
    "valor_min" DECIMAL(15,2) NOT NULL,
    "valor_max" DECIMAL(15,2),
    "vigencia_inicio" DATE NOT NULL,
    "vigencia_fim" DATE,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "regra_alcada_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."aprovador_centro_custo" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "centro_custo_id" UUID NOT NULL,
    "usuario_id" UUID NOT NULL,
    "nivel" SMALLINT NOT NULL,
    "ordem" SMALLINT NOT NULL DEFAULT 1,
    "vigencia_inicio" DATE NOT NULL DEFAULT CURRENT_DATE,
    "vigencia_fim" DATE,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "aprovador_centro_custo_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."delegacao_alcada" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "delegante_id" UUID NOT NULL,
    "delegado_id" UUID NOT NULL,
    "centro_custo_id" UUID,
    "motivo" VARCHAR(255) NOT NULL,
    "vigencia_inicio" TIMESTAMPTZ(6) NOT NULL,
    "vigencia_fim" TIMESTAMPTZ(6) NOT NULL,
    "ativa" BOOLEAN NOT NULL DEFAULT true,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "delegacao_alcada_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."instancia_aprovacao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "requisicao_id" UUID NOT NULL,
    "valor_base" DECIMAL(15,2) NOT NULL,
    "nivel_exigido" SMALLINT NOT NULL,
    "regra_snapshot" JSONB NOT NULL,
    "status" "compras"."status_instancia" NOT NULL DEFAULT 'PENDENTE',
    "motivo_invalidacao" TEXT,
    "version" INTEGER NOT NULL DEFAULT 0,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "encerrado_em" TIMESTAMPTZ(6),

    CONSTRAINT "instancia_aprovacao_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."etapa_aprovacao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "instancia_id" UUID NOT NULL,
    "nivel" SMALLINT NOT NULL,
    "aprovador_id" UUID,
    "delegante_id" UUID,
    "solicitante_id" UUID NOT NULL,
    "comprador_id" UUID,
    "decisao" "compras"."decisao_etapa" NOT NULL DEFAULT 'PENDENTE',
    "comentario" TEXT,
    "decidido_em" TIMESTAMPTZ(6),
    "version" INTEGER NOT NULL DEFAULT 0,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "etapa_aprovacao_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "ix_aprovador_cc_nivel" ON "compras"."aprovador_centro_custo"("centro_custo_id", "nivel");

-- CreateIndex
CREATE UNIQUE INDEX "uq_aprovador_cc" ON "compras"."aprovador_centro_custo"("centro_custo_id", "usuario_id", "nivel", "vigencia_inicio");

-- CreateIndex
CREATE INDEX "ix_delegacao_delegado" ON "compras"."delegacao_alcada"("delegado_id", "ativa", "vigencia_fim");

-- CreateIndex
CREATE UNIQUE INDEX "uq_etapa_nivel" ON "compras"."etapa_aprovacao"("instancia_id", "nivel");

-- AddForeignKey
ALTER TABLE "compras"."regra_alcada" ADD CONSTRAINT "regra_alcada_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."aprovador_centro_custo" ADD CONSTRAINT "aprovador_centro_custo_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."aprovador_centro_custo" ADD CONSTRAINT "aprovador_centro_custo_centro_custo_id_fkey" FOREIGN KEY ("centro_custo_id") REFERENCES "core"."centro_custo"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."aprovador_centro_custo" ADD CONSTRAINT "aprovador_centro_custo_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."delegacao_alcada" ADD CONSTRAINT "delegacao_alcada_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."delegacao_alcada" ADD CONSTRAINT "delegacao_alcada_delegante_id_fkey" FOREIGN KEY ("delegante_id") REFERENCES "core"."usuario"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."delegacao_alcada" ADD CONSTRAINT "delegacao_alcada_delegado_id_fkey" FOREIGN KEY ("delegado_id") REFERENCES "core"."usuario"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."delegacao_alcada" ADD CONSTRAINT "delegacao_alcada_centro_custo_id_fkey" FOREIGN KEY ("centro_custo_id") REFERENCES "core"."centro_custo"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."instancia_aprovacao" ADD CONSTRAINT "instancia_aprovacao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."etapa_aprovacao" ADD CONSTRAINT "etapa_aprovacao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."etapa_aprovacao" ADD CONSTRAINT "etapa_aprovacao_instancia_id_fkey" FOREIGN KEY ("instancia_id") REFERENCES "compras"."instancia_aprovacao"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."etapa_aprovacao" ADD CONSTRAINT "etapa_aprovacao_aprovador_id_fkey" FOREIGN KEY ("aprovador_id") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."etapa_aprovacao" ADD CONSTRAINT "etapa_aprovacao_delegante_id_fkey" FOREIGN KEY ("delegante_id") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."etapa_aprovacao" ADD CONSTRAINT "etapa_aprovacao_solicitante_id_fkey" FOREIGN KEY ("solicitante_id") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."etapa_aprovacao" ADD CONSTRAINT "etapa_aprovacao_comprador_id_fkey" FOREIGN KEY ("comprador_id") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- ---------------------------------------------------------------------------
-- Escrito à mão: o schema.prisma não expressa CHECKs, EXCLUDE constraints nem
-- índices PARCIAIS. Estas cláusulas são a espinha das regras de alçada — não
-- as remova ao gerar novas migrations (o Prisma pode propor DROPs por não
-- enxergá-las).
-- ---------------------------------------------------------------------------

-- regra_alcada
ALTER TABLE "compras"."regra_alcada"
  ADD CONSTRAINT "ck_regra_nivel" CHECK ("nivel" BETWEEN 1 AND 9);
ALTER TABLE "compras"."regra_alcada"
  ADD CONSTRAINT "ck_regra_faixa" CHECK ("valor_min" >= 0 AND ("valor_max" IS NULL OR "valor_max" > "valor_min"));
ALTER TABLE "compras"."regra_alcada"
  ADD CONSTRAINT "ck_regra_vigencia" CHECK ("vigencia_fim" IS NULL OR "vigencia_fim" >= "vigencia_inicio");

-- Duas regras do mesmo nível não podem vigorar ao mesmo tempo no mesmo tenant.
ALTER TABLE "compras"."regra_alcada"
  ADD CONSTRAINT "ex_regra_nivel_vigencia" EXCLUDE USING gist (
    "tenant_id" WITH =,
    "nivel" WITH =,
    daterange("vigencia_inicio", COALESCE("vigencia_fim", 'infinity'::date), '[]') WITH &&
  );

-- aprovador_centro_custo
ALTER TABLE "compras"."aprovador_centro_custo"
  ADD CONSTRAINT "ck_aprovador_nivel" CHECK ("nivel" BETWEEN 1 AND 9);
ALTER TABLE "compras"."aprovador_centro_custo"
  ADD CONSTRAINT "ck_aprovador_vigencia" CHECK ("vigencia_fim" IS NULL OR "vigencia_fim" >= "vigencia_inicio");

-- delegacao_alcada
ALTER TABLE "compras"."delegacao_alcada"
  ADD CONSTRAINT "ck_delegacao_periodo" CHECK ("vigencia_fim" > "vigencia_inicio");
ALTER TABLE "compras"."delegacao_alcada"
  ADD CONSTRAINT "ck_delegacao_distinta" CHECK ("delegante_id" <> "delegado_id");

-- instancia_aprovacao
ALTER TABLE "compras"."instancia_aprovacao"
  ADD CONSTRAINT "ck_instancia_valor" CHECK ("valor_base" >= 0);
ALTER TABLE "compras"."instancia_aprovacao"
  ADD CONSTRAINT "ck_instancia_nivel" CHECK ("nivel_exigido" BETWEEN 1 AND 9);

-- Uma única instância PENDENTE por requisição.
CREATE UNIQUE INDEX "ux_instancia_pendente"
  ON "compras"."instancia_aprovacao" ("requisicao_id") WHERE "status" = 'PENDENTE';

-- etapa_aprovacao
ALTER TABLE "compras"."etapa_aprovacao"
  ADD CONSTRAINT "ck_etapa_nivel" CHECK ("nivel" BETWEEN 1 AND 9);

-- B1 e B2: o aprovador não pode ser o solicitante nem o comprador.
ALTER TABLE "compras"."etapa_aprovacao"
  ADD CONSTRAINT "ck_etapa_nao_auto_aprovacao" CHECK (
    "aprovador_id" IS NULL
    OR ("aprovador_id" <> "solicitante_id"
        AND ("comprador_id" IS NULL OR "aprovador_id" <> "comprador_id"))
  );

-- B3: a delegação não pode burlar B1 nem B2.
ALTER TABLE "compras"."etapa_aprovacao"
  ADD CONSTRAINT "ck_etapa_delegacao_valida" CHECK (
    "delegante_id" IS NULL
    OR ("delegante_id" <> "solicitante_id"
        AND ("comprador_id" IS NULL OR "delegante_id" <> "comprador_id")
        AND "delegante_id" <> "aprovador_id")
  );

ALTER TABLE "compras"."etapa_aprovacao"
  ADD CONSTRAINT "ck_etapa_decisao_completa" CHECK (
    "decisao" = 'PENDENTE' OR ("aprovador_id" IS NOT NULL AND "decidido_em" IS NOT NULL)
  );

ALTER TABLE "compras"."etapa_aprovacao"
  ADD CONSTRAINT "ck_etapa_motivo_rejeicao" CHECK (
    "decisao" <> 'REJEITADO' OR ("comentario" IS NOT NULL AND length(btrim("comentario")) > 0)
  );

-- B4: um usuário decide no máximo uma etapa por instância.
CREATE UNIQUE INDEX "ux_etapa_um_aprovador_por_instancia"
  ON "compras"."etapa_aprovacao" ("instancia_id", "aprovador_id")
  WHERE "aprovador_id" IS NOT NULL;
