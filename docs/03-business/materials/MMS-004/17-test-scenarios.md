# MMS-004-17 — Test Scenarios

**Documento:** MMS-004-17 — Test Scenarios
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004-02 (Business Rules v1.1.0), MMS-004-07 (Use Cases), MMS-004-11 (Database), MMS-004-13 (API), MMS-004-16 (Acceptance)
**Referências:** MMS-002-17 / MMS-003-17 (padrão), GOV-001

> Cenários por caso de uso + transversais, estratégia por camada e matriz 100%. Fecha o pacote MMS-004.

# 1. Estratégia por Camada
Unit (invariantes INV-IV, VOs, factories) · Integração (StockBalanceService, outbox, serialização por chave, Testcontainers PostgreSQL/RabbitMQ) · Contrato/API (31 endpoints, IV-ERR, autorização) · E2E (Playwright) · Segurança (SoD, escopo, saldo protegido) · Performance (k6, saldo < 2 s).

# 2. Cenários por Caso de Uso

| Código | Cenário | UC | AC/Regra |
|--------|---------|-----|----------|
| TC-IV-001-1 | Entrada em caixa → saldo na base + fator | UC-IV-001 | AC-IV-010 |
| TC-IV-002-1 | Saída > disponível → IV-ERR-004 | UC-IV-002 | AC-IV-002 |
| TC-IV-002-2 | Entrega baixa reserva atomicamente | UC-IV-002 | AC-IV-022 |
| TC-IV-003-1 | Reserva sobre disponível | UC-IV-003 | AC-IV-020 |
| TC-IV-004-1 | Reserva vencida → documento de liberação | UC-IV-004 | AC-IV-021 |
| TC-IV-005-1 | Transferência preserva segregação | UC-IV-005 | AC-IV-042 |
| TC-IV-006-1 | Registrante aprova o próprio ajuste → bloqueado | UC-IV-006 | AC-IV-030 |
| TC-IV-007-1 | Contagem não altera saldo; divergência vs snapshot | UC-IV-007 | AC-IV-031 |
| TC-IV-007-2 | Divergência > tolerância → ajuste vinculado | UC-IV-007 | AC-IV-032 |
| TC-IV-008-1 | Estorno de documento confirmado | UC-IV-008 | AC-IV-003 |
| TC-IV-009-1 | Inativar local com saldo → IV-ERR-052 | UC-IV-009 | AC-IV-041 |
| TC-IV-010-1 | Alerta de ruptura com demanda aberta | UC-IV-010 | (IV-BR-081) |
| TC-IV-011-1 | Consulta de saldo < 2 s p95 | UC-IV-011 | AC-IV-062 |
| TC-IV-012-1 | Disponível ≤ ponto de pedido → sugestão | UC-IV-012 | AC-IV-050 |
| TC-IV-012-2 | Confirmar rota compra → demanda PR-001 | UC-IV-012 | AC-IV-051 |
| TC-IV-012-3 | Sugestão nunca altera saldo | UC-IV-012 | AC-IV-052 |

# 3. Cenários Transversais
| Código | Cenário | Regra |
|--------|---------|-------|
| TC-IV-SEC-1 | Escrita direta de saldo bloqueada (todos os caminhos) | IV-BR-001 |
| TC-IV-SEC-2 | Recurso de outra empresa → 404 | IV-BR-006 |
| TC-IV-SEC-3 | Estoque dedicado → outro cliente/contrato bloqueado | IV-BR-060 |
| TC-IV-AUD-1 | Auditoria com saldo anterior/posterior | IV-BR-090 |
| TC-IV-CON-1 | Confirmação concorrente na mesma chave → serializada | IV-BR-012 |
| TC-IV-EVT-1 | Eventos EVT-IV-001..019 via outbox, idempotência | MMS-004-05 |
| TC-IV-BD-1 | `available_qty` gerada = total − reservado; check não-negativo | MMS-004-11 |

# 4. Matriz de Rastreabilidade
100%: 42/42 regras IV-BR e ~25 AC-IV cobertos; UC-IV-001..012 com cenários positivos e negativos; 19 eventos cobertos.

# 5. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Catálogo TC-IV por caso de uso (UC-IV-001..012) + transversais (saldo protegido, multiempresa, segregação, auditoria, concorrência, eventos, banco), estratégia por camada e matriz de rastreabilidade 100% (42 regras, 19 eventos) — **fecha o pacote MMS-004 (17/17)** — padrão MMS-002-17/MMS-003-17. |
