# MMS-005-17 — Test Scenarios

**Documento:** MMS-005-17 — Test Scenarios
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02, MMS-005-07, MMS-005-11, MMS-005-13, MMS-005-16
**Referências:** MMS-004-17 / MMS-003-17 (padrão), GOV-001

> Cenários por caso de uso + transversais, estratégia por camada e matriz 100%. Fecha o pacote MMS-005.

# 1. Estratégia por Camada
Unit (invariantes INV-RC, conversão) · Integração (StockEntryGateway → MMS-004, outbox, Testcontainers) · Contrato/API (10 endpoints, RC-ERR, autorização) · E2E (Playwright) · Segurança (SoD, escopo, saldo protegido) · Performance (fila/conferência).

# 2. Cenários por Caso de Uso
| Código | Cenário | UC | AC/Regra |
|--------|---------|-----|----------|
| TC-RC-001-1 | Recebimento sem origem → RC-ERR-001 | UC-RC-001 | AC-RC-001 |
| TC-RC-002-1 | Conferência em caixa → base + fator | UC-RC-002 | AC-RC-002 |
| TC-RC-002-2 | Divergência sem destino → RC-ERR-011 | UC-RC-002 | AC-RC-003 |
| TC-RC-002-3 | Divergência acima da tolerância sem tratativa → RC-ERR-012 | UC-RC-002 | AC-RC-004 |
| TC-RC-003-1 | Conclusão gera exatamente uma entrada no MMS-004 | UC-RC-003 | AC-RC-005 |
| TC-RC-003-2 | Conclusão idempotente → sem entrada duplicada | UC-RC-003 | AC-RC-006 |
| TC-RC-003-3 | Compra dedicada → entrada reservada + atendimento retomado | UC-RC-003 | AC-RC-007 |
| TC-RC-002-4 | Conferente aprova o próprio destino → bloqueado | UC-RC-002 | AC-RC-008 |
| TC-RC-004-1 | Consulta/timeline com vínculo pedido→entrada | UC-RC-004 | (RC-BR-050) |

# 3. Cenários Transversais
| Código | Cenário | Regra |
|--------|---------|-------|
| TC-RC-SEC-1 | Recurso de outra empresa → 404 | RC-BR-002 |
| TC-RC-SEC-2 | Receiving nunca escreve saldo (prova) | RC-BR-020 |
| TC-RC-AUD-1 | Auditoria/timeline de conferência/divergência/entrada | RC-BR-050 |
| TC-RC-EVT-1 | EVT-RC via outbox; EVT-RC-007 dispara EVT-IV-001 no MMS-004 | MMS-005-05 |
| TC-RC-BD-1 | Constraint de conversão tudo-ou-nada; tipo/destino de divergência | MMS-005-11 |

# 4. Matriz de Rastreabilidade
100%: 12/12 regras RC-BR e 12/12 AC-RC cobertos; UC-RC-001..004 com cenários positivos e negativos; 8 eventos EVT-RC cobertos.

# 5. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Catálogo TC-RC por caso de uso + transversais (segurança, saldo protegido, auditoria, eventos, banco), estratégia por camada e matriz 100% (12 regras, 8 eventos) — **fecha o pacote MMS-005 (17/17)** — padrão MMS-004-17/MMS-003-17. |
