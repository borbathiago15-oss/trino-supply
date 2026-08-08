# MMS-003-10 — Notifications

**Documento:** MMS-003-10 — Notifications
**Módulo:** MMS-003 — Material Requisition
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 v1.1.0, MMS-003-05 (Event Storming), FD-001-05 (Notification Center), FD-001-10
**Referências:** MMS-002-10 / PR-001-10 (padrão), GOV-001

> Despacho **exclusivo** via Notification Center (FD-001-05). O módulo nunca notifica por conta própria; notificação nunca bloqueia o fluxo. Parcimônia: só eventos que exigem ação ou informam mudança relevante.

# 1. Eventos Notificáveis e Templates

| Template | Evento (MMS-003-05) | Destinatário | Canal | Prioridade |
|----------|---------------------|--------------|-------|------------|
| MR-NOT-001 | RequisitionSubmitted | Aprovador | SYSTEM + EMAIL | Normal |
| MR-NOT-002 | RequisitionApproved / PartialApproval | Solicitante | SYSTEM | Normal |
| MR-NOT-003 | RequisitionRejected / Returned | Solicitante | SYSTEM + EMAIL | Alta |
| MR-NOT-004 | Item disponível para retirada (entrega — consumido MMS-004) | Solicitante | SYSTEM + EMAIL | Alta |
| MR-NOT-005 | PurchaseDemandGenerated | Solicitante (informativo) / Comprador | SYSTEM | Normal |
| MR-NOT-006 | Material recebido (rota de compra — consumido MMS-005) | Solicitante | SYSTEM | Normal |
| MR-NOT-007 | Data necessária próxima/atrasada | Solicitante + Almoxarife | SYSTEM | Alta |
| MR-NOT-008 | RequisitionCancelled | Solicitante/Aprovador | SYSTEM | Normal |

# 2. Regras de Notificação (NOT-MR-BR)

- NOT-MR-BR-001 — Despacho exclusivo via FD-001-05; falha não afeta o fluxo (MR-BR-081).
- NOT-MR-BR-002 — Sem duplicidade: uma notificação por transição/condição.
- NOT-MR-BR-003 — Preferências do destinatário respeitadas (FD-001-05); digest opcional.
- NOT-MR-BR-004 — Templates versionados e localizados (pt-BR/en-US).
- NOT-MR-BR-005 — Prioridade Alta para rejeição, disponibilidade e atraso.
- NOT-MR-BR-006 — Escalonamento de aprovação é do Workflow (FD-001-04), não deste módulo.
- NOT-MR-BR-007 — Auditoria de entrega registrada (FD-001-05/06).

# 3. Configuração
`materials.requisition.notifications.*` (canais por template, digest, due-date alert-days — `materials.requisition.due-date.alert-days`).

# 4. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 8 templates MR-NOT (submissão, decisão, disponibilidade, demanda de compra, recebimento, prazo, cancelamento) via FD-001-05, com prioridades, preferências, localização e auditoria de entrega; regras NOT-MR-BR-001..007 — padrão MMS-002-10. |
