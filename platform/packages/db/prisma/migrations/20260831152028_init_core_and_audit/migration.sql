-- CreateSchema
CREATE SCHEMA IF NOT EXISTS "auditoria";

-- CreateSchema
CREATE SCHEMA IF NOT EXISTS "core";

-- CreateEnum
CREATE TYPE "core"."tipo_escopo" AS ENUM ('REGIONAL', 'CENTRO_CUSTO', 'CONTRATO');

-- CreateTable
CREATE TABLE "core"."tenant" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "cnpj" VARCHAR(14) NOT NULL,
    "razao_social" VARCHAR(200) NOT NULL,
    "nome_fantasia" VARCHAR(200),
    "ativo" BOOLEAN NOT NULL DEFAULT true,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "tenant_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."usuario" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "nome" VARCHAR(200) NOT NULL,
    "email" VARCHAR(255) NOT NULL,
    "senha_hash" TEXT NOT NULL,
    "cargo_funcional" VARCHAR(120),
    "mfa_habilitado" BOOLEAN NOT NULL DEFAULT false,
    "mfa_secret" TEXT,
    "ativo" BOOLEAN NOT NULL DEFAULT true,
    "ultimo_login_em" TIMESTAMPTZ(6),
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "usuario_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."regional" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "codigo" VARCHAR(20) NOT NULL,
    "nome" VARCHAR(150) NOT NULL,
    "gestor_id" UUID,
    "ativo" BOOLEAN NOT NULL DEFAULT true,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "regional_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."centro_custo" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "regional_id" UUID NOT NULL,
    "codigo" VARCHAR(30) NOT NULL,
    "nome" VARCHAR(150) NOT NULL,
    "gestor_id" UUID,
    "orcamento_anual" DECIMAL(15,2) NOT NULL DEFAULT 0,
    "ativo" BOOLEAN NOT NULL DEFAULT true,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "centro_custo_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."orcamento_centro_custo" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "centro_custo_id" UUID NOT NULL,
    "exercicio" SMALLINT NOT NULL,
    "valor_orcado" DECIMAL(15,2) NOT NULL DEFAULT 0,
    "valor_comprometido" DECIMAL(15,2) NOT NULL DEFAULT 0,
    "valor_realizado" DECIMAL(15,2) NOT NULL DEFAULT 0,
    "version" INTEGER NOT NULL DEFAULT 0,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "orcamento_centro_custo_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."contrato_operacao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "centro_custo_id" UUID NOT NULL,
    "codigo" VARCHAR(40) NOT NULL,
    "cliente_nome" VARCHAR(200) NOT NULL,
    "cliente_cnpj" VARCHAR(14),
    "vigencia_inicio" DATE NOT NULL,
    "vigencia_fim" DATE,
    "ativo" BOOLEAN NOT NULL DEFAULT true,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "atualizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "contrato_operacao_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."papel" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "nome" VARCHAR(60) NOT NULL,
    "descricao" VARCHAR(255),
    "sistema" BOOLEAN NOT NULL DEFAULT false,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "papel_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."permissao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "codigo" VARCHAR(80) NOT NULL,
    "descricao" VARCHAR(255) NOT NULL,

    CONSTRAINT "permissao_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "core"."papel_permissao" (
    "papel_id" UUID NOT NULL,
    "permissao_id" UUID NOT NULL,

    CONSTRAINT "papel_permissao_pkey" PRIMARY KEY ("papel_id","permissao_id")
);

-- CreateTable
CREATE TABLE "core"."escopo_acesso" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "usuario_id" UUID NOT NULL,
    "papel_id" UUID NOT NULL,
    "tipo_escopo" "core"."tipo_escopo" NOT NULL,
    "escopo_id" UUID NOT NULL,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "escopo_acesso_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "auditoria"."audit_log" (
    "id" BIGSERIAL NOT NULL,
    "tenant_id" UUID NOT NULL,
    "actor_id" UUID,
    "entidade" VARCHAR(80) NOT NULL,
    "entity_id" UUID NOT NULL,
    "acao" VARCHAR(40) NOT NULL,
    "before_json" JSONB,
    "after_json" JSONB,
    "ip" INET,
    "user_agent" TEXT,
    "correlation_id" UUID,
    "timestamp_utc" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "pk_audit_log" PRIMARY KEY ("id","timestamp_utc")
) PARTITION BY RANGE ("timestamp_utc");

-- Partições do audit_log (DDL validado: RANGE por timestamp_utc).
-- A DEFAULT garante que nenhuma escrita se perde antes de existir a partição do período;
-- crie partições anuais/mensais conforme o volume (exemplo: 2026 abaixo).
CREATE TABLE "auditoria"."audit_log_2026" PARTITION OF "auditoria"."audit_log"
    FOR VALUES FROM ('2026-01-01 00:00:00+00') TO ('2027-01-01 00:00:00+00');
CREATE TABLE "auditoria"."audit_log_default" PARTITION OF "auditoria"."audit_log" DEFAULT;

-- CreateIndex
CREATE UNIQUE INDEX "uq_tenant_cnpj" ON "core"."tenant"("cnpj");

-- CreateIndex
CREATE INDEX "ix_usuario_tenant_ativo" ON "core"."usuario"("tenant_id", "ativo");

-- CreateIndex
CREATE UNIQUE INDEX "uq_usuario_email" ON "core"."usuario"("tenant_id", "email");

-- CreateIndex
CREATE INDEX "ix_regional_tenant" ON "core"."regional"("tenant_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_regional_codigo" ON "core"."regional"("tenant_id", "codigo");

-- CreateIndex
CREATE INDEX "ix_centro_custo_tenant" ON "core"."centro_custo"("tenant_id", "ativo");

-- CreateIndex
CREATE INDEX "ix_centro_custo_regional" ON "core"."centro_custo"("regional_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_centro_custo_codigo" ON "core"."centro_custo"("tenant_id", "codigo");

-- CreateIndex
CREATE UNIQUE INDEX "uq_orcamento_cc_exercicio" ON "core"."orcamento_centro_custo"("centro_custo_id", "exercicio");

-- CreateIndex
CREATE INDEX "ix_contrato_centro_custo" ON "core"."contrato_operacao"("centro_custo_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_contrato_codigo" ON "core"."contrato_operacao"("tenant_id", "codigo");

-- CreateIndex
CREATE UNIQUE INDEX "uq_papel_nome" ON "core"."papel"("tenant_id", "nome");

-- CreateIndex
CREATE UNIQUE INDEX "uq_permissao_codigo" ON "core"."permissao"("codigo");

-- CreateIndex
CREATE INDEX "ix_escopo_usuario" ON "core"."escopo_acesso"("usuario_id");

-- CreateIndex
CREATE INDEX "ix_escopo_alvo" ON "core"."escopo_acesso"("tipo_escopo", "escopo_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_escopo_acesso" ON "core"."escopo_acesso"("usuario_id", "papel_id", "tipo_escopo", "escopo_id");

-- AddForeignKey
ALTER TABLE "core"."usuario" ADD CONSTRAINT "usuario_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."regional" ADD CONSTRAINT "regional_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."regional" ADD CONSTRAINT "regional_gestor_id_fkey" FOREIGN KEY ("gestor_id") REFERENCES "core"."usuario"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."centro_custo" ADD CONSTRAINT "centro_custo_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."centro_custo" ADD CONSTRAINT "centro_custo_regional_id_fkey" FOREIGN KEY ("regional_id") REFERENCES "core"."regional"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."centro_custo" ADD CONSTRAINT "centro_custo_gestor_id_fkey" FOREIGN KEY ("gestor_id") REFERENCES "core"."usuario"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."orcamento_centro_custo" ADD CONSTRAINT "orcamento_centro_custo_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."orcamento_centro_custo" ADD CONSTRAINT "orcamento_centro_custo_centro_custo_id_fkey" FOREIGN KEY ("centro_custo_id") REFERENCES "core"."centro_custo"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."contrato_operacao" ADD CONSTRAINT "contrato_operacao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."contrato_operacao" ADD CONSTRAINT "contrato_operacao_centro_custo_id_fkey" FOREIGN KEY ("centro_custo_id") REFERENCES "core"."centro_custo"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."papel" ADD CONSTRAINT "papel_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."papel_permissao" ADD CONSTRAINT "papel_permissao_papel_id_fkey" FOREIGN KEY ("papel_id") REFERENCES "core"."papel"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."papel_permissao" ADD CONSTRAINT "papel_permissao_permissao_id_fkey" FOREIGN KEY ("permissao_id") REFERENCES "core"."permissao"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."escopo_acesso" ADD CONSTRAINT "escopo_acesso_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."escopo_acesso" ADD CONSTRAINT "escopo_acesso_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "core"."usuario"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "core"."escopo_acesso" ADD CONSTRAINT "escopo_acesso_papel_id_fkey" FOREIGN KEY ("papel_id") REFERENCES "core"."papel"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- ============================================================================
-- CHECK constraints do DDL validado (o Prisma não as expressa no schema;
-- elas vivem aqui e nas migrations seguintes — não remover ao regenerar).
-- ============================================================================
ALTER TABLE "core"."tenant"
    ADD CONSTRAINT "ck_tenant_cnpj" CHECK (cnpj ~ '^[0-9]{14}$');
ALTER TABLE "core"."usuario"
    ADD CONSTRAINT "ck_usuario_email" CHECK (position('@' IN email) > 1);
ALTER TABLE "core"."centro_custo"
    ADD CONSTRAINT "ck_centro_custo_orcamento" CHECK (orcamento_anual >= 0);
ALTER TABLE "core"."orcamento_centro_custo"
    ADD CONSTRAINT "ck_orcamento_valores" CHECK (
        valor_orcado >= 0 AND valor_comprometido >= 0 AND valor_realizado >= 0
    );
ALTER TABLE "core"."orcamento_centro_custo"
    ADD CONSTRAINT "ck_orcamento_exercicio" CHECK (exercicio BETWEEN 2000 AND 2999);
ALTER TABLE "core"."contrato_operacao"
    ADD CONSTRAINT "ck_contrato_vigencia" CHECK (vigencia_fim IS NULL OR vigencia_fim >= vigencia_inicio);
