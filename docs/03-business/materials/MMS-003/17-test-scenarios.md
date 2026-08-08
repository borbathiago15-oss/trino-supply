# MMS-003-17 — Test Scenarios

**Documento:** MMS-003-17 — Test Scenarios
**Módulo:** MMS-003 — Material Requisition
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003-02 (Business Rules), MMS-003-07 (Use Cases), MMS-003-13 (API), MMS-003-16 (Acceptance)
**Referências:** MMS-002-17 / PR-001-17 (padrão), GOV-001

> Catálogo de cenários de teste por caso de uso + transversais, com estratégia de automação por camada e matriz de rastreabilidade. Fecha o pacote MMS-003.

# 1. Estratégia por Camada
| Camada | Escopo | Ferramenta-alvo |
|--------|--------|-----------------|
| Unit | Domínio (invariantes INV-MR, factories, VOs) | xUnit (.NET) |
| Integração | Repositórios, outbox, integração MMS-004/PR-001 (contrato) | Testcontainers (PostgreSQL/RabbitMQ) |
| Contrato/API | 19 endpoints, erros MR-ERR, autorização | testes de contrato OpenAPI |
| E2E | Fluxos por papel (criar→aprovar→atender→confirmar) | Playwright |
| Segurança | SoD, escopo, anti-enumeração | testes dedicados |
| Performance | Validação/consulta, visão do almoxarifado | k6 |

# 2. Cenários por Caso de Uso

| Código | Cenário | UC | AC/Regra |
|--------|---------|-----|----------|
| TC-MR-001-1 | Criar solicitação válida → Rascunho + EVT-MR-001 | UC-MR-001 | AC-MR-001 |
| TC-MR-001-2 | Item inativo → MR-ERR-010 | UC-MR-001 | AC-MR-002 |
| TC-MR-001-3 | Item com grade sem tamanho → MR-ERR-012 | UC-MR-001 | AC-MR-003 |
| TC-MR-001-4 | Motivo exige anexo, sem anexo → MR-ERR-013 | UC-MR-001 | AC-MR-004 |
| TC-MR-002-1 | Submeter incompleta → MR-ERR-021 | UC-MR-002 | AC-MR-001 |
| TC-MR-003-1 | Solicitante aprova a própria → MR-ERR-032 | UC-MR-003 | AC-MR-010 |
| TC-MR-003-2 | Aprovação parcial → só aprovados roteiam + EVT-MR-005 | UC-MR-003 | AC-MR-011 |
| TC-MR-003-3 | Retorno → volta a Rascunho | UC-MR-003 | AC-MR-012 |
| TC-MR-004-1 | Validação usa só disponível | UC-MR-004 | AC-MR-020 |
| TC-MR-004-2 | Item sem saldo → demanda PR-001 com origem | UC-MR-004 | AC-MR-021 |
| TC-MR-004-3 | Compra dedicada → entrada reservada | UC-MR-004 | AC-MR-022 |
| TC-MR-005-1 | Confirmar com pendências → MR-ERR-052 | UC-MR-005 | AC-MR-030 |
| TC-MR-005-2 | Status consolidado das 2 rotas | UC-MR-005 | AC-MR-031 |
| TC-MR-006-1 | Cancelar em estado não permitido → MR-ERR-022 | UC-MR-006 | AC-MR-005 |
| TC-MR-006-2 | Cancelar permitido → libera reservas | UC-MR-006 | AC-MR-005 |
| TC-MR-007-1 | Criar local → código gerado único | UC-MR-007 | AC-MR-040 |
| TC-MR-008-1 | Solicitante acessa visão do almoxarifado → MR-ERR-070 | UC-MR-008 | AC-MR-041 |
| TC-MR-008-2 | Filtro por categoria/período/status | UC-MR-008 | AC-MR-042 |

# 3. Cenários Transversais

| Código | Cenário | Regra |
|--------|---------|-------|
| TC-MR-SEC-1 | Recurso de outra empresa → 404 | MR-BR-001 |
| TC-MR-SEC-2 | SoD inviolável | MR-BR-032 |
| TC-MR-CON-1 | Conflito de versão → MR-ERR-409 | MR-BR-091 |
| TC-MR-AUD-1 | Toda transição gera auditoria + timeline | MR-BR-080/081 |
| TC-MR-INT-1 | Módulo nunca escreve saldo (prova) | MR-BR-050 |
| TC-MR-EVT-1 | Eventos via outbox, idempotência de consumidores | MMS-003-05 |
| TC-MR-PERF-1 | Visão do almoxarifado paginada < 500 ms p95 | NFR |

# 4. Matriz de Rastreabilidade
100%: 30/30 regras MR-BR e 28/28 AC-MR cobertos por ao menos um cenário; UC-MR-001..008 com cenários positivos e negativos.

# 5. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Catálogo de cenários TC-MR por caso de uso + transversais (segurança, concorrência, auditoria, integração, eventos, performance), estratégia de automação por camada (unit/integração/contrato/e2e/segurança/performance) e matriz de rastreabilidade 100% (30 regras, 28 ACs) — **fecha o pacote MMS-003** — padrão MMS-002-17. |
