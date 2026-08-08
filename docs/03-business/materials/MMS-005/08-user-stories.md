# MMS-005-08 — User Stories

**Documento:** MMS-005-08 — User Stories
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02, MMS-005-07
**Referências:** MMS-004-08 / MMS-003-08 (padrão), GOV-001

## Feature 1 — Receber e Conferir

### US-RC-001 — Registrar recebimento contra documento
**Como** Almoxarife, **quero** registrar o recebimento contra um pedido/transferência/devolução, **para** que nada entre sem documento.
```gherkin
Quando registro um recebimento sem documento de origem
Então o sistema recusa com RC-ERR-001
```
RC-BR-001 · UC-RC-001.

### US-RC-002 — Conferir em unidade de compra
**Como** Almoxarife, **quero** conferir em caixas e o sistema converter para a unidade base, **para** não errar o saldo.
```gherkin
Dado item comprado em caixa (1 CX = 12 UN)
Quando informo 10 CX recebidas
Então o sistema registra 120 UN na base com o fator aplicado
```
RC-BR-010 / ADR-013 · UC-RC-002.

### US-RC-003 — Registrar divergência com destino
**Como** Almoxarife, **quero** registrar falta/excesso/avaria com destino, **para** que nenhuma diferença suma.
```gherkin
Quando registro uma avaria sem destino
Então o sistema recusa com RC-ERR-011
```
RC-BR-011 · UC-RC-002.

## Feature 2 — Concluir e Dar Entrada

### US-RC-004 — Entrada no estoque
**Como** Almoxarife, **quero** que a conclusão gere a entrada no estoque, **para** fechar o recebimento.
```gherkin
Quando concluo o recebimento
Então é gerado exatamente um documento de entrada no MMS-004
```
RC-BR-020 · UC-RC-003.

### US-RC-005 — Compra dedicada
**Como** organização, **quero** que material comprado para uma solicitação entre reservado a ela, **para** retomar o atendimento automaticamente.
RC-BR-021 · UC-RC-003.

### US-RC-006 — Conclusão idempotente
**Como** sistema, **quero** que a conclusão seja idempotente, **para** não gerar entrada duplicada.
```gherkin
Quando a conclusão é reenviada com a mesma Idempotency-Key
Então nenhuma segunda entrada é gerada
```
RC-BR-020 · UC-RC-003.

## Feature 3 — Governança

### US-RC-007 — Aprovação de divergência
**Como** Supervisor, **quero** aprovar o destino de divergências acima da tolerância, **para** controlar perdas.
RC-BR-040 · UC-RC-002.

### US-RC-008 — SoD conferente ≠ aprovador
**Como** organização, **quero** impedir que o conferente aprove o próprio destino restrito, **para** garantir controle.
SOD-RC-001 · UC-RC-002.

### US-RC-009 — Acompanhar recebimento (comprador)
**Como** Comprador, **quero** acompanhar o recebimento dos meus pedidos, **para** medir o fornecedor.
RC-BR-060 · UC-RC-004.

## Matriz
| Story | Feature | Regras | UC |
|-------|---------|--------|-----|
| US-RC-001 | Receber | 001 | UC-RC-001 |
| US-RC-002 | Receber | 010 | UC-RC-002 |
| US-RC-003 | Receber | 011 | UC-RC-002 |
| US-RC-004 | Concluir | 020 | UC-RC-003 |
| US-RC-005 | Concluir | 021 | UC-RC-003 |
| US-RC-006 | Concluir | 020 | UC-RC-003 |
| US-RC-007 | Governança | 040 | UC-RC-002 |
| US-RC-008 | Governança | SOD-RC-001 | UC-RC-002 |
| US-RC-009 | Governança | 060 | UC-RC-004 |

# Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 9 user stories US-RC-001..009 em 3 Features (Receber/Conferir, Concluir/Entrada, Governança) com Gherkin, regras RC-BR e UC — padrão MMS-004-08. |
