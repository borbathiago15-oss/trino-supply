**Documento:** VIS-001 — Visão do Produto
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply

> Documentos relacionados: TPES-002 (Prompt Master), PRD-001 (02-product), ARC-001, MMS-001, PR-001.

# VIS-001 — Visão do Produto

## 1. Propósito

O **Trino Supply** é uma plataforma SaaS Enterprise de **Gestão de Suprimentos** que substitui processos manuais, planilhas, e-mails e controles descentralizados por um ambiente **único, seguro, auditável e configurável**, cobrindo o ciclo de suprimentos ponta a ponta.

## 2. North Star

> **Toda necessidade de material atendida pelo caminho mais barato e rastreável — estoque primeiro, compra só do que falta — com governança e indicadores.**

O princípio é digitalizar → **controlar** → **governar** → **gerar indicadores** (não "digitalizar → IA → recomendação"; o MVP não tem IA — TPES-002, ADR-015).

## 3. Proposta de Valor

| Para | Dor | Valor entregue |
|------|-----|----------------|
| Operação | Pedido informal sem rastro | Solicitação rastreável, status ponta a ponta |
| Compras | Comprar o que já existe | Estoque validado antes da compra |
| Almoxarifado | Saldo que "não bate" | Saldo por movimento, inventário, acuracidade ≥ 98% |
| Gestão | Falta de visibilidade | KPIs de atendimento, ruptura, giro, custo |
| Auditoria | Não reconstruir "quem fez o quê" | Trilha imutável + timeline |

## 4. Objetivos de Negócio
1. Reduzir compras desnecessárias (atendimento pelo estoque primeiro).
2. Elevar a acuracidade de estoque e a confiança no saldo.
3. Controlar a demanda antes do gasto (workflow com contexto).
4. Dar governança e rastreabilidade a 100% das operações críticas.

## 5. Personas Macro
Solicitante · Aprovador · Comprador · Almoxarife/Supervisor · Gerente de Suprimentos · Administrador · Auditor · Fornecedor (portal, roadmap).

## 6. Posicionamento
Enterprise, com práticas de SAP/Oracle/Coupa, mas com **simplicidade operacional**: "nenhum clique sem propósito; toda informação gera uma decisão" (TPES-002). Configuração acima de customização; modular; seguro por padrão.

## 7. Escopo do MVP (visão)
Core Platform (Foundation), Procurement (Solicitação de Compra), Materials (Item Catalog, Material Requisition, Inventory, Receiving); **sem IA, sem integração ERP, sem mobile no MVP** — arquitetura preparada para todos (TPES-002).

## 8. Métricas de Sucesso (produto)
Taxa de atendimento pelo estoque, acuracidade de estoque, ciclo de aprovação, redução de compras emergenciais, adoção por papel.

## 9. Princípios
Simplicidade acima de complexidade · Configuração acima de customização · Modularidade · Segurança e auditoria por padrão · Baixo acoplamento / alta coesão · Single Source of Truth.

## 10. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Visão do Produto do Trino Supply: propósito, north star, proposta de valor por público, objetivos, personas macro, posicionamento, escopo do MVP, métricas de sucesso e princípios — derivada de TPES-002 e dos documentos mestres (MMS-001, PR-001, FD-001). |
