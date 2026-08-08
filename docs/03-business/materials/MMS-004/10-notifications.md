# MMS-004-10 — Notifications

**Documento:** MMS-004-10 — Notifications
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004 v1.1.0, MMS-004-05 (Event Storming), FD-001-05 (Notification Center), FD-001-10
**Referências:** MMS-002-10 / MMS-003-10 (padrão), GOV-001

> Despacho **exclusivo** via FD-001-05 (IV-BR-083); alertas nunca bloqueiam a movimentação. Parcimônia.

# 1. Templates

| Template | Evento/condição | Destinatário | Canal | Prioridade |
|----------|-----------------|--------------|-------|------------|
| IV-NOT-001 | Alerta de estoque mínimo (EVT-IV-015) | Responsável ressuprimento | SYSTEM + EMAIL | Alta |
| IV-NOT-002 | Alerta de ruptura (EVT-IV-016) | Gerente + Almoxarife | SYSTEM + EMAIL | Crítica |
| IV-NOT-003 | Reserva a vencer (janela — IV-BR-082) | Almoxarife + Solicitante (via MMS-003) | SYSTEM | Alta |
| IV-NOT-004 | Reserva vencida (EVT-IV-008) | Almoxarife + Solicitante | SYSTEM | Normal |
| IV-NOT-005 | Ajuste aguardando aprovação (EVT-IV-009) | Supervisor/Gerente | SYSTEM + EMAIL | Alta |
| IV-NOT-006 | Ajuste aprovado/rejeitado (EVT-IV-010/011) | Registrante | SYSTEM | Normal |
| IV-NOT-007 | Divergência de inventário aprovada (EVT-IV-014) | Gerente | SYSTEM | Normal |
| IV-NOT-008 | Sugestão de reposição gerada (EVT-IV-017) | Responsável reposição | SYSTEM | Normal |

# 2. Regras (NOT-IV-BR)
- Despacho exclusivo FD-001-05; falha isolada não afeta o saldo (IV-BR-083).
- Sem duplicidade de alerta aberto para a mesma condição (item×depósito).
- Prioridade Crítica para ruptura; Alta para mínimo/reserva a vencer/ajuste pendente.
- Templates versionados, localizados (pt-BR/en-US); preferências e digest do destinatário (FD-001-05).
- Auditoria de entrega registrada.

# 3. Configuração
`materials.inventory.alerts.recipients.*`, `materials.inventory.reservation.expiring-window`, `materials.replenishment.*`.

# 4. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 8 templates IV-NOT (mínimo, ruptura, reserva a vencer/vencida, ajuste pendente/decidido, divergência, sugestão de reposição) via FD-001-05 com prioridades, preferências, localização e auditoria de entrega; regras NOT-IV-BR — padrão MMS-002-10/MMS-003-10. |
