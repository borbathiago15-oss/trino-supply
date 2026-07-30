# ADR-009 — Banco de dados como projeção do domínio

**Status:** 🟢 Accepted
**Data:** 2026-07-30 (decisão original registrada inline em PR-001-11; formalizada nesta ADR no saneamento AUD-001)
**Criticidade:** 🔴 Core

---

## Contexto

Projetos enterprise frequentemente começam pela modelagem do banco de dados e derivam o sistema a partir dela (Banco → API → Tela). Isso acopla o domínio à persistência e degrada a qualidade da modelagem de negócio.

## Decisão

O banco de dados **não é o modelo do sistema**. O banco é **uma projeção persistente do modelo de domínio**:

* O domínio manda.
* O banco implementa.
* Nunca modelar tabelas pensando na tela ou na API.

A ordem de engenharia segue: **Negócio → Domínio → Dados**.

## Consequências

* Todo modelo físico (ex.: PR-001-11) declara-se implementação do modelo de domínio correspondente (ex.: PR-001-04).
* Mudanças de persistência não alteram o domínio; mudanças de domínio propagam para o banco via migrations.
* Migrations versionadas e auditáveis são obrigatórias.

## Referências

* PR-001-04 — Domain Model
* PR-001-11 — Database Model
