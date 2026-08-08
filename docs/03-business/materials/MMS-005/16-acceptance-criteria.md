# MMS-005-16 — Acceptance Criteria

**Documento:** MMS-005-16 — Acceptance Criteria
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02, MMS-005-07, MMS-005-11, MMS-005-13
**Referências:** MMS-004-16 / MMS-003-16 (padrão), GOV-001

> Critérios Given/When/Then, prioridade P0/P1/P2, rastreados a RC-BR/UC.

| ID | Prio | Critério |
|----|------|----------|
| AC-RC-001 | P0 | **Dado** recebimento sem documento de origem **Quando** registro **Então** RC-ERR-001 (RC-BR-001) |
| AC-RC-002 | P0 | **Dado** conferência em caixa (1 CX=12 UN) **Quando** recebo 10 CX **Então** 120 na base + fator registrado (RC-BR-010/ADR-013) |
| AC-RC-003 | P0 | **Dado** divergência sem destino **Quando** registro **Então** RC-ERR-011 (RC-BR-011) |
| AC-RC-004 | P1 | **Dado** divergência acima da tolerância **Quando** concluo sem tratativa **Então** RC-ERR-012 (RC-BR-012) |
| AC-RC-005 | P0 | **Dado** recebimento conferido **Quando** concluo **Então** é gerada exatamente uma entrada no MMS-004 (RC-BR-020 / INV-RC-04) |
| AC-RC-006 | P0 | **Dado** conclusão reenviada com mesma Idempotency-Key **Então** nenhuma segunda entrada é gerada |
| AC-RC-007 | P1 | **Dado** compra dedicada **Quando** concluo **Então** a entrada é reservada à solicitação e retoma o atendimento (RC-BR-021) |
| AC-RC-008 | P0 | **Dado** destino de divergência que exige aprovação **Quando** o conferente aprova o próprio **Então** bloqueado (SOD-RC-001) |
| AC-RC-009 | P0 | **Dado** qualquer operação **Então** o Receiving nunca escreve saldo (RC-BR-020 / INV-RC-05) |
| AC-RC-010 | P0 | **Dado** recurso de outra empresa **Então** 404 (RC-BR-002) |
| AC-RC-011 | P1 | **Dado** transição inválida/documento concluído **Então** RC-ERR-030/020 |
| AC-RC-012 | P1 | **Dado** qualquer conferência/divergência/entrada **Então** gera auditoria e timeline (RC-BR-050) |

# Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 12 critérios AC-RC (documento de origem, conversão de UoM, divergência com destino/tolerância, entrada única e idempotente no MMS-004, compra dedicada, SoD, saldo protegido, multiempresa, auditoria) em Given/When/Then com prioridades e rastreabilidade a RC-BR/UC — padrão MMS-004-16. |
