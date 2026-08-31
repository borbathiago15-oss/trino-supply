-- CreateSchema
CREATE SCHEMA IF NOT EXISTS "catalogo";

-- CreateSchema
CREATE SCHEMA IF NOT EXISTS "fornecimento";

-- CreateEnum
CREATE TYPE "catalogo"."unidade_medida" AS ENUM ('UN', 'CX', 'PC', 'PAR', 'KG', 'G', 'L', 'ML', 'M', 'M2', 'M3', 'RL', 'FD');

-- CreateEnum
CREATE TYPE "fornecimento"."status_homologacao" AS ENUM ('PENDENTE', 'HOMOLOGADO', 'SUSPENSO', 'BLOQUEADO');

-- CreateEnum
CREATE TYPE "fornecimento"."tipo_documento" AS ENUM ('CND_FEDERAL', 'CND_ESTADUAL', 'CND_MUNICIPAL', 'FGTS', 'TRABALHISTA', 'CONTRATO_SOCIAL', 'ALVARA', 'CERTIFICADO_ISO', 'APOLICE_SEGURO', 'OUTRO');

-- CreateTable
CREATE TABLE "catalogo"."familia_produto" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "codigo" VARCHAR(20) NOT NULL,
    "nome" VARCHAR(150) NOT NULL,
    "lead_time_medio_dias" SMALLINT NOT NULL DEFAULT 0,
    "ativo" BOOLEAN NOT NULL DEFAULT true,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "familia_produto_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "catalogo"."tipo_produto" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "familia_id" UUID NOT NULL,
    "codigo" VARCHAR(20) NOT NULL,
    "nome" VARCHAR(150) NOT NULL,
    "ativo" BOOLEAN NOT NULL DEFAULT true,

    CONSTRAINT "tipo_produto_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "catalogo"."sku_base" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "tipo_produto_id" UUID NOT NULL,
    "codigo" VARCHAR(40) NOT NULL,
    "descricao" VARCHAR(255) NOT NULL,
    "unidade_medida" "catalogo"."unidade_medida" NOT NULL,
    "exige_ca" BOOLEAN NOT NULL DEFAULT false,
    "ativo" BOOLEAN NOT NULL DEFAULT true,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "sku_base_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "catalogo"."variante_sku" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "sku_base_id" UUID NOT NULL,
    "codigo" VARCHAR(50) NOT NULL,
    "grade" VARCHAR(40),
    "tamanho" VARCHAR(20),
    "cor" VARCHAR(40),
    "codigo_barras" VARCHAR(20),
    "ativo" BOOLEAN NOT NULL DEFAULT true,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "variante_sku_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "catalogo"."parametro_estoque" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "variante_id" UUID NOT NULL,
    "centro_custo_id" UUID NOT NULL,
    "ponto_pedido_rop" DECIMAL(15,4) NOT NULL DEFAULT 0,
    "estoque_seguranca" DECIMAL(15,4) NOT NULL DEFAULT 0,
    "lead_time_dias" SMALLINT NOT NULL DEFAULT 0,
    "gerar_sc_automatica" BOOLEAN NOT NULL DEFAULT false,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "parametro_estoque_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "catalogo"."saldo_estoque" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "variante_id" UUID NOT NULL,
    "centro_custo_id" UUID NOT NULL,
    "qtd_disponivel" DECIMAL(15,4) NOT NULL DEFAULT 0,
    "qtd_reservada" DECIMAL(15,4) NOT NULL DEFAULT 0,
    "version" INTEGER NOT NULL DEFAULT 0,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "saldo_estoque_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "fornecimento"."fornecedor" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "cnpj" VARCHAR(14) NOT NULL,
    "razao_social" VARCHAR(200) NOT NULL,
    "nome_fantasia" VARCHAR(200),
    "email_contato" VARCHAR(255),
    "telefone" VARCHAR(20),
    "status_homologacao" "fornecimento"."status_homologacao" NOT NULL DEFAULT 'PENDENTE',
    "motivo_status" VARCHAR(255),
    "score_desempenho" DECIMAL(5,2),
    "homologado_em" TIMESTAMPTZ(6),
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "fornecedor_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "fornecimento"."documento_fornecedor" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "fornecedor_id" UUID NOT NULL,
    "tipo" "fornecimento"."tipo_documento" NOT NULL,
    "numero" VARCHAR(60),
    "data_emissao" DATE NOT NULL,
    "data_validade" DATE NOT NULL,
    "obrigatorio" BOOLEAN NOT NULL DEFAULT true,
    "arquivo_uri" TEXT,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "documento_fornecedor_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "fornecimento"."certificado_aprovacao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "variante_id" UUID NOT NULL,
    "fornecedor_id" UUID NOT NULL,
    "numero_ca" VARCHAR(20) NOT NULL,
    "data_validade" DATE NOT NULL,
    "arquivo_uri" TEXT,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "certificado_aprovacao_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "fornecimento"."fornecedor_sku" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "fornecedor_id" UUID NOT NULL,
    "variante_id" UUID NOT NULL,
    "ultimo_preco" DECIMAL(15,4) NOT NULL,
    "moeda" CHAR(3) NOT NULL DEFAULT 'BRL',
    "lead_time_dias" SMALLINT NOT NULL DEFAULT 0,
    "vigente_em" DATE NOT NULL DEFAULT CURRENT_DATE,

    CONSTRAINT "fornecedor_sku_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "uq_familia_codigo" ON "catalogo"."familia_produto"("tenant_id", "codigo");

-- CreateIndex
CREATE INDEX "ix_tipo_familia" ON "catalogo"."tipo_produto"("familia_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_tipo_codigo" ON "catalogo"."tipo_produto"("tenant_id", "codigo");

-- CreateIndex
CREATE INDEX "ix_sku_base_tipo" ON "catalogo"."sku_base"("tipo_produto_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_sku_base_codigo" ON "catalogo"."sku_base"("tenant_id", "codigo");

-- CreateIndex
CREATE INDEX "ix_variante_sku_base" ON "catalogo"."variante_sku"("sku_base_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_variante_codigo" ON "catalogo"."variante_sku"("tenant_id", "codigo");

-- CreateIndex
CREATE UNIQUE INDEX "uq_variante_atributos" ON "catalogo"."variante_sku"("sku_base_id", "grade", "tamanho", "cor");

-- CreateIndex
CREATE UNIQUE INDEX "uq_parametro_estoque" ON "catalogo"."parametro_estoque"("variante_id", "centro_custo_id");

-- CreateIndex
CREATE INDEX "ix_saldo_cc" ON "catalogo"."saldo_estoque"("centro_custo_id", "variante_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_saldo_estoque" ON "catalogo"."saldo_estoque"("variante_id", "centro_custo_id");

-- CreateIndex
CREATE INDEX "ix_fornecedor_status" ON "fornecimento"."fornecedor"("tenant_id", "status_homologacao");

-- CreateIndex
CREATE UNIQUE INDEX "uq_fornecedor_cnpj" ON "fornecimento"."fornecedor"("tenant_id", "cnpj");

-- CreateIndex
CREATE INDEX "ix_documento_fornecedor" ON "fornecimento"."documento_fornecedor"("fornecedor_id", "tipo");

-- CreateIndex
CREATE INDEX "ix_ca_validade" ON "fornecimento"."certificado_aprovacao"("data_validade");

-- CreateIndex
CREATE UNIQUE INDEX "uq_ca" ON "fornecimento"."certificado_aprovacao"("variante_id", "fornecedor_id", "numero_ca");

-- CreateIndex
CREATE UNIQUE INDEX "uq_fornecedor_sku" ON "fornecimento"."fornecedor_sku"("fornecedor_id", "variante_id");

-- AddForeignKey
ALTER TABLE "catalogo"."familia_produto" ADD CONSTRAINT "familia_produto_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."tipo_produto" ADD CONSTRAINT "tipo_produto_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."tipo_produto" ADD CONSTRAINT "tipo_produto_familia_id_fkey" FOREIGN KEY ("familia_id") REFERENCES "catalogo"."familia_produto"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."sku_base" ADD CONSTRAINT "sku_base_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."sku_base" ADD CONSTRAINT "sku_base_tipo_produto_id_fkey" FOREIGN KEY ("tipo_produto_id") REFERENCES "catalogo"."tipo_produto"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."variante_sku" ADD CONSTRAINT "variante_sku_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."variante_sku" ADD CONSTRAINT "variante_sku_sku_base_id_fkey" FOREIGN KEY ("sku_base_id") REFERENCES "catalogo"."sku_base"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."parametro_estoque" ADD CONSTRAINT "parametro_estoque_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."parametro_estoque" ADD CONSTRAINT "parametro_estoque_variante_id_fkey" FOREIGN KEY ("variante_id") REFERENCES "catalogo"."variante_sku"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."parametro_estoque" ADD CONSTRAINT "parametro_estoque_centro_custo_id_fkey" FOREIGN KEY ("centro_custo_id") REFERENCES "core"."centro_custo"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."saldo_estoque" ADD CONSTRAINT "saldo_estoque_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."saldo_estoque" ADD CONSTRAINT "saldo_estoque_variante_id_fkey" FOREIGN KEY ("variante_id") REFERENCES "catalogo"."variante_sku"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "catalogo"."saldo_estoque" ADD CONSTRAINT "saldo_estoque_centro_custo_id_fkey" FOREIGN KEY ("centro_custo_id") REFERENCES "core"."centro_custo"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."fornecedor" ADD CONSTRAINT "fornecedor_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."documento_fornecedor" ADD CONSTRAINT "documento_fornecedor_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."documento_fornecedor" ADD CONSTRAINT "documento_fornecedor_fornecedor_id_fkey" FOREIGN KEY ("fornecedor_id") REFERENCES "fornecimento"."fornecedor"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."certificado_aprovacao" ADD CONSTRAINT "certificado_aprovacao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."certificado_aprovacao" ADD CONSTRAINT "certificado_aprovacao_variante_id_fkey" FOREIGN KEY ("variante_id") REFERENCES "catalogo"."variante_sku"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."certificado_aprovacao" ADD CONSTRAINT "certificado_aprovacao_fornecedor_id_fkey" FOREIGN KEY ("fornecedor_id") REFERENCES "fornecimento"."fornecedor"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."fornecedor_sku" ADD CONSTRAINT "fornecedor_sku_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."fornecedor_sku" ADD CONSTRAINT "fornecedor_sku_fornecedor_id_fkey" FOREIGN KEY ("fornecedor_id") REFERENCES "fornecimento"."fornecedor"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "fornecimento"."fornecedor_sku" ADD CONSTRAINT "fornecedor_sku_variante_id_fkey" FOREIGN KEY ("variante_id") REFERENCES "catalogo"."variante_sku"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- ---------------------------------------------------------------------------
-- Escrito à mão: o schema.prisma não expressa CHECK constraints nem índices
-- PARCIAIS. Estas cláusulas fazem parte do DDL validado — não as remova ao
-- gerar novas migrations (o Prisma pode propor DROPs por não enxergá-las).
-- ---------------------------------------------------------------------------

-- Índices parciais do DDL
CREATE UNIQUE INDEX "ux_variante_ean" ON "catalogo"."variante_sku" ("tenant_id", "codigo_barras") WHERE "codigo_barras" IS NOT NULL;
CREATE INDEX "ix_documento_validade" ON "fornecimento"."documento_fornecedor" ("data_validade") WHERE "obrigatorio" = TRUE;

-- CHECK constraints do DDL
ALTER TABLE "catalogo"."familia_produto"
  ADD CONSTRAINT "ck_familia_leadtime" CHECK ("lead_time_medio_dias" >= 0);

ALTER TABLE "catalogo"."parametro_estoque"
  ADD CONSTRAINT "ck_parametro_valores" CHECK ("ponto_pedido_rop" >= 0 AND "estoque_seguranca" >= 0 AND "lead_time_dias" >= 0);

ALTER TABLE "catalogo"."saldo_estoque"
  ADD CONSTRAINT "ck_saldo_nao_negativo" CHECK ("qtd_disponivel" >= 0 AND "qtd_reservada" >= 0);

ALTER TABLE "catalogo"."saldo_estoque"
  ADD CONSTRAINT "ck_saldo_reserva" CHECK ("qtd_reservada" <= "qtd_disponivel");

ALTER TABLE "fornecimento"."fornecedor"
  ADD CONSTRAINT "ck_fornecedor_cnpj" CHECK ("cnpj" ~ '^[0-9]{14}$');

ALTER TABLE "fornecimento"."fornecedor"
  ADD CONSTRAINT "ck_fornecedor_score" CHECK ("score_desempenho" IS NULL OR "score_desempenho" BETWEEN 0 AND 100);

ALTER TABLE "fornecimento"."documento_fornecedor"
  ADD CONSTRAINT "ck_documento_datas" CHECK ("data_validade" >= "data_emissao");

ALTER TABLE "fornecimento"."fornecedor_sku"
  ADD CONSTRAINT "ck_fornecedor_sku_preco" CHECK ("ultimo_preco" >= 0);
