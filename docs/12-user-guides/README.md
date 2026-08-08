**Documento:** DOC-001 — Guias do Usuário (Índice)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Criticidade:** 🟡 Suporte
**Escopo:** Plataforma Trino Supply

> Documentos relacionados: `*-14-ux` e `*-15-wireframes` dos módulos, VIS-001, TPES-002 (publicação MkDocs).

# DOC-001 — Guias do Usuário

> Índice e padrão dos guias do usuário. Os guias detalham **como operar**, por papel e por módulo, alinhados às telas (`*-14-ux`/`*-15-wireframes`). Publicação em **MkDocs** (TPES-002).

## 1. Estrutura por Papel

| Papel | Guia cobre |
|-------|-----------|
| Solicitante | Criar/acompanhar solicitação de material, confirmar recebimento |
| Aprovador | Fila de aprovações, aprovação parcial, parecer |
| Almoxarife | Recebimento/conferência, movimentações, reservas, inventário |
| Supervisor | Aprovação de ajustes/divergências, endereçamento |
| Comprador | Solicitação de compra, acompanhamento de pedidos |
| Gerente de Suprimentos | KPIs, parâmetros, sugestões de reposição |
| Administrador | Usuários/permissões, locais de entrega, configuração |
| Auditor | Consulta de trilhas e timeline |

## 2. Estrutura por Módulo

| Módulo | Guia base | Telas de referência |
|--------|-----------|---------------------|
| Item Catalog (MMS-002) | Cadastro e ativação de item, sinônimos, unidades/conversão | MMS-002-14/15 |
| Material Requisition (MMS-003) | Solicitar, acompanhar, confirmar | MMS-003-14/15 |
| Inventory (MMS-004) | Movimentar, reservar, inventariar, reposição | MMS-004-14/15 |
| Receiving (MMS-005) | Receber, conferir, tratar divergência | MMS-005-14/15 |
| Purchase Requisition (PR-001) | Solicitar compra, aprovar | PR-001-14/15 |

## 3. Padrão de Cada Guia
Objetivo · Pré-requisitos (papel/permissão) · Passo a passo (com referência às telas) · Erros comuns e o que fazer · Perguntas frequentes · Glossário (remete a MMS-001 §25).

## 4. Publicação
- Fonte em Markdown; build **MkDocs** publicado a cada release (OPS-001).
- Localização pt-BR (padrão) e en-US (roadmap), via catálogo i18n do Foundation.
- Versionado junto ao produto; guia reflete a versão da capacidade.

## 5. Governança
Cada guia publicado é registrado no GOV-002; guias não substituem a documentação de negócio (SSOT) — traduzem-na para o operador.

## 6. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Índice e padrão dos Guias do Usuário: estrutura por papel e por módulo (alinhada a `*-14-ux`/`*-15-wireframes`), padrão de cada guia, publicação MkDocs/i18n e governança — base para a produção dos guias por release. |
