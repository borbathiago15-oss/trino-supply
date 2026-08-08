# MMS-005-06 — BPMN

**Documento:** MMS-005-06 — BPMN
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02/03/05, MMS-001 (§9), MMS-004, FD-001-04
**Referências:** MMS-004-06 / MMS-003-06 (padrão), GOV-001

> Notação textual: [T] tarefa, ⟨G⟩ gateway, (E) evento, ✉ mensagem.

# 1. Processo Principal
```
(E) Documento de origem (Pedido/Transferência/Devolução)
  → [T] Registrar recebimento (Aguardando)                     [RC-BR-001]
  → [T] Iniciar conferência (Em Conferência)
  → [T] Conferir item a item (receber; converter UoM)          [RC-BR-010]
  → ⟨G divergência?⟩
        ├─ não / dentro da tolerância → (segue)                [RC-BR-012]
        └─ sim → [T] Registrar divergência + destino            [RC-BR-011]
                 → ⟨G aprovação exigida?⟩ → [T] Aprovar destino [RC-BR-040]
  → [T] Concluir                                                [RC-BR-020]
  → ✉ [T] Gerar entrada no estoque (MMS-004)                    → EVT-RC-007
  → ⟨G compra dedicada?⟩ → entra reservada + ✉ retoma MMS-003   [RC-BR-021]
  → (E fim) Concluído / Concluído com Divergência
```

# 2. Gateways
| Gateway | Regra | Saídas |
|---------|-------|--------|
| RG-RC-GW-01 Divergência? | recebido ≠ esperado além da tolerância | segue / tratar |
| RG-RC-GW-02 Aprovação exigida? | `materials.receiving.divergence.approval-required` | direto / workflow |
| RG-RC-GW-03 Compra dedicada? | `materials.receiving.dedicated-purchase.auto-reserve` | reservada / comum |

# 3. Fluxos Alternativos
- FA-RC-01 Cancelamento (Aguardando/Em Conferência, antes da entrada).
- FA-RC-02 Recebimento parcial (`materials.receiving.partial.allowed`) — documento de origem segue aberto.
- FA-RC-03 Avaria → destino "recusa/devolução ao fornecedor" documentado.

# 4. Business Objects / Data Stores
Recebimento, Linha, Divergência (MMS-005-11); entrada e saldo no MMS-004; pedido no PR-001.

# 5. Message Events
✉ Pedido confirmado←PR-001 (cria Aguardando); ✉ Entrada→MMS-004 (EVT-RC-007); ✉ Atendimento retomado→MMS-003. Exchange `trino.materials` (`evt.receiving.*`).

# 6. SLA/Escalonamento
Recebimentos pendentes há > N dias geram alerta; aprovação de divergência com SLA do workflow (FD-001-04); notificação nunca bloqueia.

# 7. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | BPMN do Receiving: processo principal (registrar→conferir→divergência/aprovar→concluir→entrada MMS-004→compra dedicada), 3 gateways RG-RC-GW, fluxos alternativos (cancelamento, parcial, avaria), message events e SLA — padrão MMS-004-06/MMS-003-06. |
