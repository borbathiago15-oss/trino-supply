**Documento:** MMS-003-03 — State Machine
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 (Visão do Módulo v1.1.0), MMS-003-02 (Business Rules v1.0.0), MMS-001 (Documento Mestre Funcional — §15.1, §9), MMS-004 (Inventory), PR-001 (Compras), FD-001-04 (Workflow), FD-001-06 (Audit), FD-001-07 (Timeline), FD-001-10 (Configuration)
**Referências:** MMS-004-03 / MMS-002-03 (padrão de formato), MMS-005, GOV-001

---

# 1. Objetivo

Definir os estados da **Solicitação de Material**, suas transições, restrições e eventos associados, garantindo evolução controlada, previsível, auditável e compatível com as regras do módulo (MMS-003-02) e o fluxo corporativo (MMS-001 §9).

O nível fino de atendimento (reserva → separação → entrega) é do **item** e vive no MMS-004; a solicitação (cabeçalho) reflete a **condição consolidada** dos seus itens. Nenhuma transição ocorre fora deste documento.

# 2. Princípios

- Toda solicitação possui exatamente um estado atual.
- Toda mudança de estado gera auditoria (MMS-P-02 / MR-BR-080), evento de domínio e marco na Timeline (MR-BR-081).
- Nenhum estado é alterado diretamente no banco — toda transição é **ação de negócio** (MMS-001 §15).
- **Validação de estoque só após a aprovação** (MR-BR-040): aprovar não compromete saldo; reservar, sim.
- O módulo **não movimenta saldo** (MR-BR-050): reserva/entrega são do MMS-004; a solicitação é o documento de origem.
- A solicitação só **conclui** quando todos os itens concluem (MR-BR-052) — não há conclusão parcial.

---

# 3. Estados Oficiais

| Código | Estado | Final | Descrição |
|--------|--------|-------|-----------|
| ST-MR-001 | Rascunho | Não | Em edição pelo solicitante; ainda não submetida |
| ST-MR-002 | Submetida | Não | Enviada; entra no workflow (ou segue direto à validação se aprovação não exigida) |
| ST-MR-003 | Em Aprovação | Não | Sob decisão do aprovador (FD-001-04) |
| ST-MR-004 | Aprovada | Não | Aprovada (total ou parcial); dispara a validação de estoque |
| ST-MR-005 | Em Atendimento | Não | Itens em reserva/separação/entrega pelo MMS-004 |
| ST-MR-006 | Aguardando Compra | Não | Há itens na rota de compra (PR-001) ainda não recebidos |
| ST-MR-007 | Concluída | Sim | Todos os itens atendidos e recebimento confirmado |
| ST-MR-008 | Rejeitada | Sim | Recusada integralmente na aprovação |
| ST-MR-009 | Cancelada | Sim | Cancelada pelo solicitante/gestão nos estados permitidos |

> **Em Atendimento × Aguardando Compra:** quando o roteamento (MR-BR-042) envia parte dos itens à compra, a solicitação assume **Aguardando Compra** enquanto houver item de compra pendente de recebimento; itens com saldo são atendidos em paralelo pelo MMS-004. Quando o material comprado é recebido (MMS-005) e retomado, a solicitação passa a **Em Atendimento** até a conclusão de todos os itens.

---

# 4. Especificação dos Estados

## ST-MR-001 — Rascunho
- **Entry:** cria a solicitação (MR-BR-020); todos os campos editáveis.
- **Eventos aceitos:** editar, adicionar/remover itens, anexar, submeter (MR-BR-021), descartar.
- **Guards de saída (submissão):** MR-BR-001..013 satisfeitas; ≥ 1 item.
- **Side effects:** nenhum sobre estoque.
- **Responsável:** Solicitante. **Auditoria:** criação e alterações.

## ST-MR-002 — Submetida
- **Entry:** publica *Solicitação submetida*; roteia ao workflow conforme `materials.requisition.approval.required` (MR-BR-030).
- **Transições:** → Em Aprovação (workflow exigido) ou → Aprovada (aprovação não exigida).
- **Guards:** SoD (MR-BR-032) preparada para a etapa de aprovação.

## ST-MR-003 — Em Aprovação
- **Entry:** cria a instância de workflow (FD-001-04); notifica aprovador (FD-001-05).
- **Eventos aceitos:** aprovar (total/parcial — MR-BR-031), rejeitar, retornar (devolver ao solicitante), cancelar.
- **Guards:** aprovador ≠ solicitante (MR-BR-032); ajustes por item auditados.
- **SLA/escalonamento:** conforme definição do workflow (FD-001-04).

## ST-MR-004 — Aprovada
- **Entry:** registra a decisão (parcial quando aplicável); dispara a **validação de estoque** (MR-BR-040/041).
- **Transições:** → Em Atendimento (todos os itens com saldo) ou → Aguardando Compra (há item sem saldo — MR-BR-042/043).
- **Side effects:** nenhuma escrita de saldo aqui; reservas são criadas na transição para atendimento.

## ST-MR-005 — Em Atendimento
- **Entry:** para cada item com saldo, o MMS-004 cria reserva (IV-BR-020) e conduz separação/entrega.
- **Eventos aceitos (consumidos):** reserva criada/liberada/vencida, separação, entrega (MR-BR-051).
- **Transições:** → Concluída (todos os itens entregues e recebimento confirmado — MR-BR-052); permanece até então.
- **Responsável:** Almoxarife (operação via MMS-004).

## ST-MR-006 — Aguardando Compra
- **Entry:** itens sem saldo geram demanda no PR-001 com referência de origem (MR-BR-043).
- **Eventos aceitos (consumidos):** compra aprovada, pedido confirmado, previsão de entrega, **material recebido** (MMS-005).
- **Transições:** → Em Atendimento (material recebido retoma o atendimento; compra dedicada entra reservada — MR-BR-044); → Concluída quando os itens de compra são os últimos e já entregues.
- **Observação:** itens com saldo seguem atendidos em paralelo; o estado do cabeçalho reflete a pendência de compra.

## ST-MR-007 — Concluída (final)
- **Entry:** todos os itens em estado terminal de atendimento; publica *Solicitação concluída*.
- **Eventos aceitos:** nenhum (terminal).

## ST-MR-008 — Rejeitada (final)
- **Entry:** recusa integral na aprovação, com parecer; publica *Solicitação rejeitada*.

## ST-MR-009 — Cancelada (final)
- **Entry:** cancelamento nos estados permitidos (MR-BR-022); libera reservas ativas vinculadas (IV-BR-022); publica *Solicitação cancelada*.

---

# 5. Fluxos Principais

```
Rascunho ──submeter──► Submetida ──► [workflow?] ──► Em Aprovação ──aprovar──► Aprovada
                                          │                                         │
                                    (não exigido)                          validação de estoque
                                          ▼                                         │
                                      Aprovada ◄───────────────────────────┐        ▼
                                                                           │  ┌──────┴────────┐
                                                              todos com saldo│  │ há item sem   │
                                                                           ▼  │ saldo          │
                                                                    Em Atendimento     Aguardando Compra
                                                                           │                  │ (recebido)
                                                                           │◄─────────────────┘
                                                                           ▼
                                                                       Concluída
```

# 6. Fluxos Alternativos

- **FA-MR-01 Retorno na aprovação:** Em Aprovação → Rascunho (devolvida ao solicitante para ajuste), auditada.
- **FA-MR-02 Aprovação parcial:** itens rejeitados/reduzidos ficam fora do roteamento; a solicitação segue com os aprovados (MR-BR-031).
- **FA-MR-03 Cancelamento:** de Rascunho/Submetida/Em Aprovação/Aguardando Compra → Cancelada (MR-BR-022), liberando reservas.
- **FA-MR-04 Reserva vencida:** item cuja reserva vence (IV-BR-021) retorna à condição de pendência; a solicitação permanece Em Atendimento e o item é re-tratado.

---

# 7. Matriz de Transições

| Origem | Destino | Permitido | Guard Condition | Evento |
|--------|---------|-----------|-----------------|--------|
| Rascunho | Submetida | Sim | MR-BR-021 (cadastro/itens válidos) | Solicitação submetida |
| Rascunho | Cancelada | Sim | Estado permitido (MR-BR-022) | Solicitação cancelada |
| Submetida | Em Aprovação | Sim | Aprovação exigida (MR-BR-030) | Solicitação em aprovação |
| Submetida | Aprovada | Sim | Aprovação não exigida | Solicitação aprovada |
| Submetida | Cancelada | Sim | MR-BR-022 | Solicitação cancelada |
| Em Aprovação | Aprovada | Sim | Aprovador ≠ solicitante (MR-BR-032) | Solicitação aprovada / Aprovação parcial |
| Em Aprovação | Rejeitada | Sim | Recusa integral com parecer | Solicitação rejeitada |
| Em Aprovação | Rascunho | Sim | Retorno ao solicitante (FA-MR-01) | Solicitação retornada |
| Em Aprovação | Cancelada | Sim | MR-BR-022 | Solicitação cancelada |
| Aprovada | Em Atendimento | Sim | Todos os itens com saldo (MR-BR-041/042) | Item roteado / Estoque validado |
| Aprovada | Aguardando Compra | Sim | Há item sem saldo (MR-BR-042/043) | Demanda de compra gerada |
| Aguardando Compra | Em Atendimento | Sim | Material comprado recebido (MR-BR-044) | (consumido) Material recebido |
| Aguardando Compra | Cancelada | Sim | MR-BR-022 (itens não entregues) | Solicitação cancelada |
| Em Atendimento | Concluída | Sim | Todos os itens entregues + recebimento confirmado (MR-BR-052) | Solicitação concluída |
| Aguardando Compra | Concluída | Sim | Itens de compra eram os últimos e já entregues | Solicitação concluída |
| Rejeitada / Cancelada / Concluída | qualquer | Não | Estado terminal | — |

---

# 8. Matriz de Editabilidade por Estado

| Campo | Rascunho | Submetida | Em Aprovação | Aprovada+ | Terminais |
|-------|:--------:|:---------:|:------------:|:---------:|:---------:|
| Itens/quantidades | ✏️ livre | 🔒 | 🔒 (só aprovador via MR-BR-031) | 🔒 | 🔒 |
| Justificativa/motivo/anexos | ✏️ | 🔒 | 🔒 | 🔒 | 🔒 |
| Centro de custo / local de entrega | ✏️ | 🔒 | 🔒 | 🔒 | 🔒 |
| Parecer do aprovador | — | — | ✏️ (aprovador) | 🔒 | 🔒 |

Edição em Submetida/Em Aprovação só ocorre via retorno ao solicitante (FA-MR-01) ou ajuste do aprovador (MR-BR-031).

---

# 9. Eventos de Domínio

| Evento | Transição/ação de origem |
|--------|--------------------------|
| Solicitação criada | criação → Rascunho |
| Solicitação submetida | Rascunho→Submetida |
| Solicitação em aprovação | Submetida→Em Aprovação |
| Solicitação aprovada | Em Aprovação/Submetida→Aprovada |
| Aprovação parcial registrada | decisão por item (MR-BR-031) |
| Solicitação rejeitada | Em Aprovação→Rejeitada |
| Solicitação retornada | Em Aprovação→Rascunho |
| Estoque validado para a solicitação | Aprovada (validação MR-BR-040/041) |
| Item roteado (estoque × compra) | Aprovada→Em Atendimento/Aguardando Compra |
| Demanda de compra gerada | roteamento à compra (MR-BR-043) |
| Solicitação cancelada | → Cancelada |
| Solicitação concluída | Em Atendimento/Aguardando Compra→Concluída |

(Especificação técnica no MMS-003-05, conforme ADR-010.)

---

# 10. Restrições

- Nenhuma transição sem auditoria (MR-BR-080) e sem marco na timeline (MR-BR-081).
- Validar estoque antes da aprovação é **proibido** (MR-BR-040).
- Concluir com itens pendentes é **proibido** (MR-BR-052) — não há conclusão parcial.
- Editar itens/quantidades fora de Rascunho só via retorno (FA-MR-01) ou ajuste do aprovador (MR-BR-031).
- Cancelar em estado não permitido é recusado (MR-BR-022).
- Alterar qualquer estado diretamente no banco (MMS-001 §15).
- O módulo nunca escreve saldo (MR-BR-050) — reservas/entregas são do MMS-004.

---

# 11. Rastreabilidade

| Estado/Transição | Regra | Evento |
|------------------|-------|--------|
| Rascunho→Submetida | MR-BR-021 | Solicitação submetida |
| Submetida→Em Aprovação/Aprovada | MR-BR-030 | Em aprovação / aprovada |
| Em Aprovação→Aprovada (parcial) | MR-BR-031/032 | Aprovação parcial registrada |
| Aprovada→validação | MR-BR-040/041 | Estoque validado |
| roteamento | MR-BR-042/043 | Item roteado / Demanda de compra |
| Aguardando Compra→Em Atendimento | MR-BR-044 | (consumido) Material recebido |
| →Concluída | MR-BR-052 | Solicitação concluída |
| →Cancelada | MR-BR-022 | Solicitação cancelada |

---

# 12. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Criação da State Machine do Material Requisition: 9 estados (ST-MR-001..009 — Rascunho, Submetida, Em Aprovação, Aprovada, Em Atendimento, Aguardando Compra, Concluída, Rejeitada, Cancelada) com entry/exit, eventos aceitos/consumidos, guards, side effects, responsável e auditoria; fluxos principais e alternativos (retorno, aprovação parcial, cancelamento, reserva vencida); matriz de transições; matriz de editabilidade por estado; eventos de domínio; restrições e rastreabilidade com regras MR-BR — derivada do fluxo corporativo (MMS-001 §9, §15.1) e das Business Rules (MMS-003-02), no padrão MMS-004-03 |
