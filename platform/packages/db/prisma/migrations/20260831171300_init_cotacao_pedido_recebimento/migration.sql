-- Numeração de cotações e pedidos: sequências dedicadas.
CREATE SEQUENCE IF NOT EXISTS "compras"."seq_cotacao";
CREATE SEQUENCE IF NOT EXISTS "compras"."seq_pedido";

-- CreateEnum
CREATE TYPE "compras"."status_cotacao" AS ENUM ('ABERTA', 'ENCERRADA', 'CANCELADA');

-- CreateEnum
CREATE TYPE "compras"."status_convite" AS ENUM ('ENVIADO', 'VISUALIZADO', 'RESPONDIDO', 'RECUSADO', 'EXPIRADO');

-- CreateEnum
CREATE TYPE "compras"."status_pedido" AS ENUM ('EMITIDO', 'ENVIADO', 'CONFIRMADO', 'RECEBIDO_PARCIAL', 'RECEBIDO_TOTAL', 'CANCELADO');

-- CreateEnum
CREATE TYPE "compras"."tipo_recebimento" AS ENUM ('TOTAL', 'PARCIAL');

-- CreateEnum
CREATE TYPE "compras"."ocorrencia_recebimento" AS ENUM ('SEM_OCORRENCIA', 'AVARIA', 'DIVERGENCIA_QUANTIDADE', 'DIVERGENCIA_ESPECIFICACAO', 'ATRASO', 'RECUSA_TOTAL');

-- CreateTable
CREATE TABLE "compras"."processo_cotacao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "numero" VARCHAR(20) NOT NULL,
    "comprador_id" UUID NOT NULL,
    "status" "compras"."status_cotacao" NOT NULL DEFAULT 'ABERTA',
    "data_limite_resposta" TIMESTAMPTZ(6) NOT NULL,
    "criterio_equalizacao" JSONB NOT NULL DEFAULT '{"preco":0.60,"lead_time":0.20,"frete":0.10,"cond_pagto":0.10}',
    "dispensa_cotacao" BOOLEAN NOT NULL DEFAULT false,
    "justificativa_dispensa" TEXT,
    "motivo_cancelamento" TEXT,
    "encerrada_em" TIMESTAMPTZ(6),
    "version" INTEGER NOT NULL DEFAULT 0,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "processo_cotacao_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."rfq_item" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "cotacao_id" UUID NOT NULL,
    "item_requisicao_id" UUID NOT NULL,
    "quantidade_consolidada" DECIMAL(15,4) NOT NULL,

    CONSTRAINT "rfq_item_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."convite_fornecedor" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "cotacao_id" UUID NOT NULL,
    "fornecedor_id" UUID NOT NULL,
    "status" "compras"."status_convite" NOT NULL DEFAULT 'ENVIADO',
    "data_envio" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "token_acesso" TEXT,

    CONSTRAINT "convite_fornecedor_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."proposta" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "cotacao_id" UUID NOT NULL,
    "fornecedor_id" UUID NOT NULL,
    "valor_itens" DECIMAL(15,2) NOT NULL DEFAULT 0,
    "frete" DECIMAL(15,2) NOT NULL DEFAULT 0,
    "desconto" DECIMAL(15,2) NOT NULL DEFAULT 0,
    -- Coluna GERADA: o total nunca diverge das parcelas porque ninguém o escreve.
    "valor_total" DECIMAL(15,2) GENERATED ALWAYS AS ("valor_itens" + "frete" - "desconto") STORED,
    "condicao_pagamento" VARCHAR(60) NOT NULL,
    "prazo_entrega_dias" SMALLINT NOT NULL,
    "validade_proposta" DATE NOT NULL,
    "recebida_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "proposta_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."item_proposta" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "proposta_id" UUID NOT NULL,
    "rfq_item_id" UUID NOT NULL,
    "preco_unitario" DECIMAL(15,4) NOT NULL,
    "marca" VARCHAR(120),
    "prazo_item_dias" SMALLINT,

    CONSTRAINT "item_proposta_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."equalizacao" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "cotacao_id" UUID NOT NULL,
    "proposta_vencedora_id" UUID NOT NULL,
    "proposta_menor_preco_id" UUID NOT NULL,
    "notas" JSONB NOT NULL,
    "justificativa_desvio" TEXT,
    "equalizado_por" UUID NOT NULL,
    "equalizado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "equalizacao_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."pedido_compra" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "numero" VARCHAR(20) NOT NULL,
    "requisicao_id" UUID NOT NULL,
    "cotacao_id" UUID,
    "fornecedor_id" UUID NOT NULL,
    "centro_custo_id" UUID NOT NULL,
    "status" "compras"."status_pedido" NOT NULL DEFAULT 'EMITIDO',
    "valor_total" DECIMAL(15,2) NOT NULL,
    "condicao_pagamento" VARCHAR(60) NOT NULL,
    "prazo_entrega" DATE NOT NULL,
    "emitido_por" UUID NOT NULL,
    "emitido_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "enviado_em" TIMESTAMPTZ(6),
    "cancelado_em" TIMESTAMPTZ(6),
    "motivo_cancelamento" TEXT,
    "version" INTEGER NOT NULL DEFAULT 0,

    CONSTRAINT "pedido_compra_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."item_pedido" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "pedido_id" UUID NOT NULL,
    "item_requisicao_id" UUID NOT NULL,
    "variante_id" UUID NOT NULL,
    "sequencia" SMALLINT NOT NULL,
    "qtd_pedida" DECIMAL(15,4) NOT NULL,
    "qtd_recebida" DECIMAL(15,4) NOT NULL DEFAULT 0,
    "preco_unitario" DECIMAL(15,4) NOT NULL,
    "version" INTEGER NOT NULL DEFAULT 0,

    CONSTRAINT "item_pedido_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."nota_fiscal_entrada" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "fornecedor_id" UUID NOT NULL,
    "chave_acesso" VARCHAR(44) NOT NULL,
    "numero" VARCHAR(20) NOT NULL,
    "serie" VARCHAR(5) NOT NULL,
    "valor_total" DECIMAL(15,2) NOT NULL,
    "data_emissao" DATE NOT NULL,
    "arquivo_xml_uri" TEXT,
    "criado_em" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "nota_fiscal_entrada_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."recebimento" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "pedido_id" UUID NOT NULL,
    "nota_fiscal_id" UUID,
    "tipo" "compras"."tipo_recebimento" NOT NULL,
    "data_recebimento" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "recebedor_id" UUID NOT NULL,
    "observacao" TEXT,

    CONSTRAINT "recebimento_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "compras"."item_recebimento" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "tenant_id" UUID NOT NULL,
    "recebimento_id" UUID NOT NULL,
    "item_pedido_id" UUID NOT NULL,
    "qtd_recebida" DECIMAL(15,4) NOT NULL,
    "qtd_avariada" DECIMAL(15,4) NOT NULL DEFAULT 0,
    "ocorrencia" "compras"."ocorrencia_recebimento" NOT NULL DEFAULT 'SEM_OCORRENCIA',
    "descricao_ocorrencia" TEXT,

    CONSTRAINT "item_recebimento_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "ix_cotacao_status" ON "compras"."processo_cotacao"("tenant_id", "status", "data_limite_resposta");

-- CreateIndex
CREATE UNIQUE INDEX "uq_cotacao_numero" ON "compras"."processo_cotacao"("tenant_id", "numero");

-- CreateIndex
CREATE INDEX "ix_rfq_item_requisicao" ON "compras"."rfq_item"("item_requisicao_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_rfq_item" ON "compras"."rfq_item"("cotacao_id", "item_requisicao_id");

-- CreateIndex
CREATE INDEX "ix_convite_fornecedor" ON "compras"."convite_fornecedor"("fornecedor_id", "status");

-- CreateIndex
CREATE UNIQUE INDEX "uq_convite" ON "compras"."convite_fornecedor"("cotacao_id", "fornecedor_id");

-- CreateIndex
CREATE INDEX "ix_proposta_cotacao" ON "compras"."proposta"("cotacao_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_proposta" ON "compras"."proposta"("cotacao_id", "fornecedor_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_item_proposta" ON "compras"."item_proposta"("proposta_id", "rfq_item_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_equalizacao" ON "compras"."equalizacao"("cotacao_id");

-- CreateIndex
CREATE INDEX "ix_pedido_fornecedor" ON "compras"."pedido_compra"("fornecedor_id", "status");

-- CreateIndex
CREATE INDEX "ix_pedido_cc" ON "compras"."pedido_compra"("centro_custo_id", "emitido_em" DESC);

-- CreateIndex
CREATE INDEX "ix_pedido_status" ON "compras"."pedido_compra"("tenant_id", "status");

-- CreateIndex
CREATE UNIQUE INDEX "uq_pedido_numero" ON "compras"."pedido_compra"("tenant_id", "numero");

-- CreateIndex
CREATE INDEX "ix_item_pedido" ON "compras"."item_pedido"("pedido_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_item_pedido_seq" ON "compras"."item_pedido"("pedido_id", "sequencia");

-- CreateIndex
CREATE UNIQUE INDEX "uq_nfe_chave" ON "compras"."nota_fiscal_entrada"("tenant_id", "chave_acesso");

-- CreateIndex
CREATE INDEX "ix_recebimento_pedido" ON "compras"."recebimento"("pedido_id", "data_recebimento");

-- CreateIndex
CREATE INDEX "ix_item_recebimento_pedido" ON "compras"."item_recebimento"("item_pedido_id");

-- CreateIndex
CREATE UNIQUE INDEX "uq_item_recebimento" ON "compras"."item_recebimento"("recebimento_id", "item_pedido_id");

-- AddForeignKey
ALTER TABLE "compras"."processo_cotacao" ADD CONSTRAINT "processo_cotacao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."processo_cotacao" ADD CONSTRAINT "processo_cotacao_comprador_id_fkey" FOREIGN KEY ("comprador_id") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."rfq_item" ADD CONSTRAINT "rfq_item_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."rfq_item" ADD CONSTRAINT "rfq_item_cotacao_id_fkey" FOREIGN KEY ("cotacao_id") REFERENCES "compras"."processo_cotacao"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."rfq_item" ADD CONSTRAINT "rfq_item_item_requisicao_id_fkey" FOREIGN KEY ("item_requisicao_id") REFERENCES "compras"."item_requisicao"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."convite_fornecedor" ADD CONSTRAINT "convite_fornecedor_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."convite_fornecedor" ADD CONSTRAINT "convite_fornecedor_cotacao_id_fkey" FOREIGN KEY ("cotacao_id") REFERENCES "compras"."processo_cotacao"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."convite_fornecedor" ADD CONSTRAINT "convite_fornecedor_fornecedor_id_fkey" FOREIGN KEY ("fornecedor_id") REFERENCES "fornecimento"."fornecedor"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."proposta" ADD CONSTRAINT "proposta_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."proposta" ADD CONSTRAINT "proposta_cotacao_id_fkey" FOREIGN KEY ("cotacao_id") REFERENCES "compras"."processo_cotacao"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."proposta" ADD CONSTRAINT "proposta_fornecedor_id_fkey" FOREIGN KEY ("fornecedor_id") REFERENCES "fornecimento"."fornecedor"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_proposta" ADD CONSTRAINT "item_proposta_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_proposta" ADD CONSTRAINT "item_proposta_proposta_id_fkey" FOREIGN KEY ("proposta_id") REFERENCES "compras"."proposta"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_proposta" ADD CONSTRAINT "item_proposta_rfq_item_id_fkey" FOREIGN KEY ("rfq_item_id") REFERENCES "compras"."rfq_item"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."equalizacao" ADD CONSTRAINT "equalizacao_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."equalizacao" ADD CONSTRAINT "equalizacao_cotacao_id_fkey" FOREIGN KEY ("cotacao_id") REFERENCES "compras"."processo_cotacao"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."equalizacao" ADD CONSTRAINT "equalizacao_proposta_vencedora_id_fkey" FOREIGN KEY ("proposta_vencedora_id") REFERENCES "compras"."proposta"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."equalizacao" ADD CONSTRAINT "equalizacao_proposta_menor_preco_id_fkey" FOREIGN KEY ("proposta_menor_preco_id") REFERENCES "compras"."proposta"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."equalizacao" ADD CONSTRAINT "equalizacao_equalizado_por_fkey" FOREIGN KEY ("equalizado_por") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."pedido_compra" ADD CONSTRAINT "pedido_compra_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."pedido_compra" ADD CONSTRAINT "pedido_compra_requisicao_id_fkey" FOREIGN KEY ("requisicao_id") REFERENCES "compras"."requisicao_compra"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."pedido_compra" ADD CONSTRAINT "pedido_compra_cotacao_id_fkey" FOREIGN KEY ("cotacao_id") REFERENCES "compras"."processo_cotacao"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."pedido_compra" ADD CONSTRAINT "pedido_compra_fornecedor_id_fkey" FOREIGN KEY ("fornecedor_id") REFERENCES "fornecimento"."fornecedor"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."pedido_compra" ADD CONSTRAINT "pedido_compra_centro_custo_id_fkey" FOREIGN KEY ("centro_custo_id") REFERENCES "core"."centro_custo"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."pedido_compra" ADD CONSTRAINT "pedido_compra_emitido_por_fkey" FOREIGN KEY ("emitido_por") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_pedido" ADD CONSTRAINT "item_pedido_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_pedido" ADD CONSTRAINT "item_pedido_pedido_id_fkey" FOREIGN KEY ("pedido_id") REFERENCES "compras"."pedido_compra"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_pedido" ADD CONSTRAINT "item_pedido_item_requisicao_id_fkey" FOREIGN KEY ("item_requisicao_id") REFERENCES "compras"."item_requisicao"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_pedido" ADD CONSTRAINT "item_pedido_variante_id_fkey" FOREIGN KEY ("variante_id") REFERENCES "catalogo"."variante_sku"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."nota_fiscal_entrada" ADD CONSTRAINT "nota_fiscal_entrada_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."nota_fiscal_entrada" ADD CONSTRAINT "nota_fiscal_entrada_fornecedor_id_fkey" FOREIGN KEY ("fornecedor_id") REFERENCES "fornecimento"."fornecedor"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."recebimento" ADD CONSTRAINT "recebimento_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."recebimento" ADD CONSTRAINT "recebimento_pedido_id_fkey" FOREIGN KEY ("pedido_id") REFERENCES "compras"."pedido_compra"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."recebimento" ADD CONSTRAINT "recebimento_nota_fiscal_id_fkey" FOREIGN KEY ("nota_fiscal_id") REFERENCES "compras"."nota_fiscal_entrada"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."recebimento" ADD CONSTRAINT "recebimento_recebedor_id_fkey" FOREIGN KEY ("recebedor_id") REFERENCES "core"."usuario"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_recebimento" ADD CONSTRAINT "item_recebimento_tenant_id_fkey" FOREIGN KEY ("tenant_id") REFERENCES "core"."tenant"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_recebimento" ADD CONSTRAINT "item_recebimento_recebimento_id_fkey" FOREIGN KEY ("recebimento_id") REFERENCES "compras"."recebimento"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "compras"."item_recebimento" ADD CONSTRAINT "item_recebimento_item_pedido_id_fkey" FOREIGN KEY ("item_pedido_id") REFERENCES "compras"."item_pedido"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- ---------------------------------------------------------------------------
-- Escrito à mão: CHECKs que o schema.prisma não expressa. Não remova ao gerar
-- novas migrations (o Prisma não os enxerga e pode propor DROPs).
-- ---------------------------------------------------------------------------

-- processo_cotacao
ALTER TABLE "compras"."processo_cotacao"
  ADD CONSTRAINT "ck_cotacao_dispensa" CHECK (
    "dispensa_cotacao" = FALSE
    OR ("justificativa_dispensa" IS NOT NULL AND length(btrim("justificativa_dispensa")) > 0)
  );

-- Os pesos da equalização precisam somar 1.0000 — a regra vive no banco, então
-- nenhum caminho de escrita consegue gravar um critério que não fecha.
ALTER TABLE "compras"."processo_cotacao"
  ADD CONSTRAINT "ck_cotacao_pesos" CHECK (
    round((
      ("criterio_equalizacao"->>'preco')::numeric +
      ("criterio_equalizacao"->>'lead_time')::numeric +
      ("criterio_equalizacao"->>'frete')::numeric +
      ("criterio_equalizacao"->>'cond_pagto')::numeric
    ), 4) = 1.0000
  );

-- rfq_item
ALTER TABLE "compras"."rfq_item"
  ADD CONSTRAINT "ck_rfq_item_qtd" CHECK ("quantidade_consolidada" > 0);

-- proposta
ALTER TABLE "compras"."proposta"
  ADD CONSTRAINT "ck_proposta_valores" CHECK (
    "valor_itens" >= 0 AND "frete" >= 0 AND "desconto" >= 0
    AND "desconto" <= "valor_itens" + "frete"
    AND "prazo_entrega_dias" >= 0
  );

-- item_proposta
ALTER TABLE "compras"."item_proposta"
  ADD CONSTRAINT "ck_item_proposta_preco" CHECK ("preco_unitario" >= 0);

-- equalizacao: vencedora diferente do menor preço exige justificativa.
ALTER TABLE "compras"."equalizacao"
  ADD CONSTRAINT "ck_equalizacao_desvio" CHECK (
    "proposta_vencedora_id" = "proposta_menor_preco_id"
    OR ("justificativa_desvio" IS NOT NULL AND length(btrim("justificativa_desvio")) > 0)
  );

-- pedido_compra
ALTER TABLE "compras"."pedido_compra"
  ADD CONSTRAINT "ck_pedido_valor" CHECK ("valor_total" >= 0);
ALTER TABLE "compras"."pedido_compra"
  ADD CONSTRAINT "ck_pedido_cancelamento" CHECK (
    "status" <> 'CANCELADO'
    OR ("motivo_cancelamento" IS NOT NULL AND "cancelado_em" IS NOT NULL)
  );

-- item_pedido: recebido nunca negativo e no máximo 10% acima do pedido.
ALTER TABLE "compras"."item_pedido"
  ADD CONSTRAINT "ck_item_pedido_qtd" CHECK (
    "qtd_pedida" > 0 AND "qtd_recebida" >= 0 AND "qtd_recebida" <= "qtd_pedida" * 1.10
  );
ALTER TABLE "compras"."item_pedido"
  ADD CONSTRAINT "ck_item_pedido_preco" CHECK ("preco_unitario" >= 0);

-- nota_fiscal_entrada
ALTER TABLE "compras"."nota_fiscal_entrada"
  ADD CONSTRAINT "ck_nfe_chave" CHECK ("chave_acesso" ~ '^[0-9]{44}$');
ALTER TABLE "compras"."nota_fiscal_entrada"
  ADD CONSTRAINT "ck_nfe_valor" CHECK ("valor_total" >= 0);

-- recebimento: nada de recebimento lançado no futuro.
ALTER TABLE "compras"."recebimento"
  ADD CONSTRAINT "ck_recebimento_data" CHECK ("data_recebimento" <= now() + INTERVAL '1 day');

-- item_recebimento
ALTER TABLE "compras"."item_recebimento"
  ADD CONSTRAINT "ck_item_recebimento_qtd" CHECK (
    "qtd_recebida" > 0 AND "qtd_avariada" >= 0 AND "qtd_avariada" <= "qtd_recebida"
  );
ALTER TABLE "compras"."item_recebimento"
  ADD CONSTRAINT "ck_item_recebimento_ocorrencia" CHECK (
    "ocorrencia" = 'SEM_OCORRENCIA'
    OR ("descricao_ocorrencia" IS NOT NULL AND length(btrim("descricao_ocorrencia")) > 0)
  );
