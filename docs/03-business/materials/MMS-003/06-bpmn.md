# MMS-003-06 — BPMN

**Documento:** MMS-003-06 — BPMN
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 v1.1.0, MMS-003-02 (Business Rules), MMS-003-03 (State Machine), MMS-003-05 (Event Storming), MMS-001 (§9), FD-001-04
**Referências:** MMS-004-06 / MMS-002-06 (padrão), GOV-001

> Modelagem do processo da solicitação (recorte do fluxo corporativo MMS-001 §9). Notação textual BPMN: tarefas [T], gateways ⟨G⟩, eventos (E), mensagens ✉.

# 1. Processo Principal — Solicitação de Material

```
(E início) Necessidade
  → [T] Criar solicitação (itens, motivo, anexos, CC, local, data)   [MR-BR-001..013]
  → [T] Submeter                                                     (E✉ Submetida)
  → ⟨G aprovação exigida?⟩
        ├─ não → (segue à validação)
        └─ sim → [T] Aprovar (FD-001-04)  ⟨G decisão⟩
                    ├─ Rejeitar → (E fim) Rejeitada
                    ├─ Retornar → [T] Ajustar (volta a Rascunho)
                    └─ Aprovar (total/parcial)  [MR-BR-031/032]
  → [T] Validar estoque no MMS-004 (disponível)                      [MR-BR-040/041]
  → ⟨G por item: tem saldo?⟩
        ├─ sim → [T] Reservar/Separar/Entregar (MMS-004)   ✉→ MMS-004
        └─ não → [T] Gerar demanda de compra (PR-001)       ✉→ PR-001  [MR-BR-043]
                    → (sub-processo Compra→Recebimento)
                    → [T] Retomar atendimento (MMS-005 recebido)     [MR-BR-044]
  → ⟨G todos os itens concluídos?⟩ (não → aguarda) 
  → [T] Confirmar recebimento (solicitante)                          [MR-BR-052]
  → (E fim) Concluída
```

# 2. Gateways (regras)

| Gateway | Regra | Saídas |
|---------|-------|--------|
| RG-MR-GW-01 Aprovação exigida? | `materials.requisition.approval.required` + critérios | direto / workflow |
| RG-MR-GW-02 Decisão | parecer do aprovador (MR-BR-031) | rejeitar / retornar / aprovar |
| RG-MR-GW-03 Tem saldo? (por item) | disponível ≥ solicitado (MR-BR-041) | estoque / compra |
| RG-MR-GW-04 Todos concluídos? | itens em estado terminal (MR-BR-052) | concluir / aguardar |

# 3. Fluxos Alternativos
- FA-MR-01 Retorno ao solicitante (RG-MR-GW-02 → ajustar → resubmeter).
- FA-MR-02 Aprovação parcial (itens rejeitados/reduzidos saem do roteamento).
- FA-MR-03 Cancelamento (estados permitidos → libera reservas).
- FA-MR-04 Reserva vencida (item re-tratado; solicitação segue).

# 4. Sub-processo Compra → Recebimento
```
✉ Demanda (PR-001) → [PR-001: cotação/pedido] → [MMS-005: recebimento/conferência]
  → ⟨G divergência?⟩ (trata no MMS-005) → [T] Entrada no estoque (MMS-004)
  → compra dedicada? → entra reservada à solicitação (MR-BR-044) → retoma
```

# 5. Business Objects / Data Stores
Solicitação, Item da solicitação, Anexo, Local de entrega, Reserva (MMS-004), Demanda de compra (PR-001). Data stores: `material_requisition*`, `delivery_location` (MMS-003-11); saldos no MMS-004.

# 6. Message Events (integração)
✉ Submetida→Workflow; ✉ Aprovada→validação; ✉ Demanda→PR-001; ✉ Reserva/Entrega←MMS-004; ✉ Recebido←MMS-005. Todos via `trino.materials` (`evt.requisition.*` — MMS-003-05).

# 7. Exceções, SLA, Escalonamento
Aprovação com SLA/escalonamento do workflow (FD-001-04); alertas de data necessária (`materials.requisition.due-date.alert-days`); falha de notificação nunca bloqueia o fluxo (FD-001-05).

# 8. Rastreabilidade
Cada tarefa/gateway referencia MR-BR e ST-MR (MMS-003-02/03); mensagens referem EVT-MR (MMS-003-05).

# 9. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | BPMN do Material Requisition: processo principal (criar→submeter→aprovar→validar→rotear→atender/comprar→confirmar), 4 gateways RG-MR-GW, 4 fluxos alternativos, sub-processo Compra→Recebimento, business objects/data stores, message events de integração e SLA/escalonamento — padrão MMS-004-06. |
