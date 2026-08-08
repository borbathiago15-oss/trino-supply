# MMS-005-10 — Notifications

**Documento:** MMS-005-10 — Notifications
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-05, FD-001-05, FD-001-10
**Referências:** MMS-004-10 / MMS-003-10 (padrão), GOV-001

> Despacho exclusivo via FD-001-05; nunca bloqueia o fluxo. Parcimônia.

# 1. Templates
| Template | Evento/condição | Destinatário | Canal | Prioridade |
|----------|-----------------|--------------|-------|------------|
| RC-NOT-001 | Recebimento pendente aguardando há > N dias | Almoxarife/Supervisor | SYSTEM | Alta |
| RC-NOT-002 | Divergência registrada (EVT-RC-003) | Supervisor + Comprador | SYSTEM + EMAIL | Alta |
| RC-NOT-003 | Destino de divergência aguardando aprovação | Supervisor | SYSTEM | Alta |
| RC-NOT-004 | Recebimento concluído (EVT-RC-005/006) | Comprador | SYSTEM | Normal |
| RC-NOT-005 | Atendimento retomado (compra dedicada) | Solicitante (via MMS-003) | SYSTEM | Normal |

# 2. Regras (NOT-RC-BR)
- Despacho exclusivo FD-001-05; falha isolada (RC-BR-050).
- Sem duplicidade; templates versionados/localizados (pt-BR/en-US); preferências e digest.
- Prioridade Alta para pendências e divergências.
- Auditoria de entrega registrada.

# 3. Configuração
`materials.receiving.notifications.*`, `materials.receiving.pending.alert-days`.

# 4. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 5 templates RC-NOT (pendência, divergência, aprovação de destino, conclusão, atendimento retomado) via FD-001-05 com prioridades, preferências, localização e auditoria de entrega — padrão MMS-004-10. |
