# MMS-004-05 — Event Storming

**Documento:** MMS-004-05 — Event Storming
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.1.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004 (visão do módulo v1.1.0), MMS-004-02 (Business Rules v1.1.0), MMS-004-03 (State Machine), MMS-004-04 (Domain Model v1.1.0), ADR-010 (mensageria e eventos), ADR-013 (Conversão de UoM), ADR-014 (Motor de Regras de Reposição), FD-001-05 (Notifications), FD-001-06 (Audit), FD-001-07 (Timeline)
**Referências:** MMS-002-05 (padrão de formato Enterprise), MMS-002 (Item Catalog), MMS-003 (Material Requisition), MMS-005 (Receiving), PR-001 (Compras), GOV-001

---

## 1. Objetivo

Este documento registra o **Event Storming** do módulo **Inventory Management (MMS-004)**: os comandos que provocam mudanças de estado, os eventos de domínio resultantes, as políticas que reagem a esses eventos e as reações esperadas dos serviços e módulos dependentes.

O Event Storming do Inventory Management responde a três perguntas:

1. **O que pode acontecer** no ciclo de vida dos documentos de movimentação, reservas, ajustes e inventários;
2. **Quem reage** quando algo acontece (Timeline, Auditoria, Notificações, projeção de saldo, módulos consumidores);
3. **Como os eventos são entregues** de forma confiável, idempotente e ordenada (ADR-010).

Fonte de verdade complementar: os comandos e invariantes formais estão em **MMS-004-04 (Domain Model)**; as transições de estado em **MMS-004-03 (State Machine)**; as regras de negócio em **MMS-004-02 (Business Rules)**. Em caso de conflito, prevalece a State Machine para transições e as Business Rules para validações.

---

## 2. Fluxo de Alto Nível

```
Documento de origem (solicitação MMS-003 / recebimento MMS-005 / operação / contagem)
      │
      ▼
Registrar Movimentação (rascunho) ──► Confirmar ──► efeito no saldo (projeção)
      │                                   │
      │                                   ├─► Reserva: criar / atender / liberar / vencer
      │                                   ├─► Ajuste: registrar → aprovar/rejeitar
      │                                   ├─► Inventário: abrir → contar → fechar
      │                                   ├─► Alertas: mínimo / ruptura / reserva vencendo
      │                                   └─► Timeline + Auditoria (saldo anterior/posterior)
      ▼
Estorno (única forma de correção — IV-BR-003)
```

Estados (MMS-004-03): Documento `ST-IV-001..004` · Reserva `ST-IV-010..013` · Ajuste `ST-IV-020..023` · Inventário `ST-IV-030..033`.

---

## 3. Atores

| Ator | Tipo | Papel no Event Storming |
|---|---|---|
| Almoxarife | Humano | Executa movimentações operacionais, atendimentos, transferências e contagens |
| Supervisor de Almoxarifado | Humano | Aprova/rejeita ajustes; gerencia locais; abre e fecha inventários |
| Gerente de Suprimentos | Humano | Consulta posição e KPIs; aprovador alternativo de ajustes |
| Administrador | Humano | Configura parâmetros (`materials.inventory.*`) |
| Sistema (Inventory) | Sistema | Confirmações, efeitos sobre saldo (StockBalanceService), alertas, auditoria, timeline |
| Sistema (Job de vencimento) | Sistema | Expira reservas vencidas emitindo documento de liberação (IV-BR-021) |
| MMS-002 Item Catalog | Módulo | Publica Item inativado (consumido); fornece identidade/parâmetros do item |
| MMS-003 Material Requisition | Módulo consumidor | Solicita validação e reserva; consome eventos de reserva e saldo |
| MMS-005 Receiving | Módulo | Publica Recebimento conferido (consumido → entrada) |
| PR-001 Compras | Módulo consumidor | Consome demanda de reposição e posição de estoque |
| Auditor | Humano (leitura) | Consulta trilha e extrato; nunca emite comandos |

---

## 4. Comandos

| Código | Comando | Ator | Descrição | Evento(s) resultante(s) |
|---|---|---|---|---|
| CMD-IV-001 | CreateEntryMovement | Sistema (MMS-005) / Almoxarife | Registra entrada (recebimento, devolução, ajuste, carga inicial) — IV-BR-010 | EVT-IV-001 (após confirmar) |
| CMD-IV-002 | CreateIssueMovement | Almoxarife / Sistema (MMS-003) | Registra saída (atendimento, consumo, ajuste) — IV-BR-011 | EVT-IV-002 (após confirmar) |
| CMD-IV-003 | CreateTransfer | Almoxarife / Supervisor | Registra transferência origem→destino — IV-BR-030 | EVT-IV-003 (após confirmar) |
| CMD-IV-004 | ConfirmMovement | Almoxarife / Sistema | Confirma documento: aplica efeito no saldo (INV-IV-01) | EVT-IV-001/002/003 |
| CMD-IV-005 | CancelMovement | Almoxarife | Cancela rascunho sem efeito | (somente timeline) |
| CMD-IV-006 | ReverseMovement | Almoxarife / Supervisor | Estorna documento confirmado — IV-BR-003 | EVT-IV-004 |
| CMD-IV-007 | CreateReservation | Sistema (MMS-003) / Almoxarife | Cria reserva sobre o disponível — IV-BR-020 | EVT-IV-005 |
| CMD-IV-008 | ReleaseReservation | Sistema (MMS-003) / Almoxarife | Libera reserva ativa — IV-BR-022 | EVT-IV-007 |
| CMD-IV-009 | ExpireReservation | Sistema (job) | Vence reserva expirada — IV-BR-021 | EVT-IV-008 |
| CMD-IV-010 | FulfillReservation | Almoxarife / Sistema | Baixa reserva na saída por atendimento — IV-BR-023 | EVT-IV-006 (com EVT-IV-002) |
| CMD-IV-011 | RegisterAdjustment | Almoxarife / Supervisor | Registra ajuste com justificativa — IV-BR-040 | EVT-IV-009 |
| CMD-IV-012 | ApproveAdjustment | Supervisor / Gerente | Aprova ajuste (≠ registrante — IV-BR-041) e confirma efeito | EVT-IV-010 |
| CMD-IV-013 | RejectAdjustment | Supervisor / Gerente | Rejeita ajuste com motivo | EVT-IV-011 |
| CMD-IV-014 | OpenInventoryCount | Supervisor | Abre inventário (ABC ou geral) — IV-BR-070 | EVT-IV-012 |
| CMD-IV-015 | StartCounting | Supervisor | Inicia a contagem do escopo | (mudança de estado; timeline) |
| CMD-IV-016 | RegisterCount | Almoxarife | Registra contagem com snapshot — IV-BR-071/073 | EVT-IV-013 |
| CMD-IV-017 | CloseInventory | Supervisor | Fecha inventário gerando propostas de ajuste — IV-BR-072 | EVT-IV-014 |
| CMD-IV-018 | CancelInventory | Supervisor | Cancela inventário sem contagens | (somente timeline) |
| CMD-IV-019 | ManageLocation | Supervisor / Administrador | Cria/edita/inativa locais — IV-BR-050..052 | (administrativo; timeline/auditoria) |
| CMD-IV-020 | AddComment | Papéis autorizados | Comentário operacional na timeline | (somente timeline) |
| CMD-IV-021 | ConfirmReplenishmentSuggestion | Almoxarife / Gerente | Confirma sugestão de reposição, disparando a rota (compra→PR-001 / transferência) — IV-BR-131 | EVT-IV-018 |
| CMD-IV-022 | DiscardReplenishmentSuggestion | Almoxarife / Gerente | Descarta a sugestão sem efeito | EVT-IV-019 |

**Notas:**
- `EvaluateStockAlerts` não é comando de ator — é reação automática do `StockAlertEvaluator` às confirmações (POL-IV-03/04), podendo publicar EVT-IV-015/016.
- `GenerateReplenishmentSuggestion` também não é comando de ator: é reação automática do `ReplenishmentEvaluator` (POL-IV-10) à confirmação que reduz o disponível abaixo do ponto de pedido, publicando EVT-IV-017 (ADR-014, IV-BR-130). A execução automática da rota (sem CMD-IV-021) é opt-in na v2.0 (`materials.replenishment.auto-execute`).
- **Conversão de UoM (ADR-013):** os comandos de entrada/saída/transferência podem capturar a quantidade em unidade alternativa; o `UomConversionApplier` converte para a base antes da confirmação (IV-BR-009) — não há comando nem evento próprios de conversão.

---

## 5. Eventos de Domínio

Os eventos abaixo correspondem aos eventos funcionais previstos na visão do módulo (MMS-004 README — Eventos Publicados), detalhados pela State Machine (MMS-004-03 §11). A revisão 1.1.0 acrescenta 3 eventos de reposição (EVT-IV-017..019) derivados do ADR-014, totalizando 19 eventos.

| Código | Evento | Nome funcional | Disparado por | Momento |
|---|---|---|---|---|
| EVT-IV-001 | StockEntryRegistered | Entrada registrada | CMD-IV-004 (sobre CMD-IV-001) | Confirmação da entrada persistida |
| EVT-IV-002 | StockIssueRegistered | Saída registrada | CMD-IV-004 (sobre CMD-IV-002) | Confirmação da saída persistida |
| EVT-IV-003 | StockTransferRegistered | Transferência registrada | CMD-IV-004 (sobre CMD-IV-003) | Confirmação atômica do par saída+entrada |
| EVT-IV-004 | MovementReversed | Estorno registrado | CMD-IV-006 | Confirmação do estorno persistida |
| EVT-IV-005 | ReservationCreated | Reserva criada | CMD-IV-007 | Bloqueio do disponível persistido |
| EVT-IV-006 | ReservationFulfilled | Reserva atendida | CMD-IV-010 | Baixa da reserva na mesma transação da saída |
| EVT-IV-007 | ReservationReleased | Reserva liberada | CMD-IV-008 | Devolução ao disponível persistida |
| EVT-IV-008 | ReservationExpired | Reserva vencida | CMD-IV-009 | Liberação por vencimento persistida (job) |
| EVT-IV-009 | AdjustmentRegistered | Ajuste registrado | CMD-IV-011 | Ajuste persistido (Pendente ou Aprovado direto) |
| EVT-IV-010 | AdjustmentApproved | Ajuste aprovado | CMD-IV-012 | Efeito do ajuste confirmado no saldo |
| EVT-IV-011 | AdjustmentRejected | Ajuste rejeitado | CMD-IV-013 | Rejeição persistida (sem efeito) |
| EVT-IV-012 | InventoryCountStarted | Inventário iniciado | CMD-IV-014 | Abertura persistida com escopo materializado |
| EVT-IV-013 | CountEntryRegistered | Contagem registrada | CMD-IV-016 | Contagem persistida com snapshot |
| EVT-IV-014 | InventoryCountClosed | Inventário fechado | CMD-IV-017 | Fechamento persistido com sumário |
| EVT-IV-015 | StockMinimumAlerted | Alerta de estoque mínimo | StockAlertEvaluator (POL-IV-03/04) | Disponível cruzou o mínimo (IV-BR-080) |
| EVT-IV-016 | StockoutAlerted | Alerta de ruptura | StockAlertEvaluator (POL-IV-03) | Disponível zero com demanda aberta (IV-BR-081) |
| EVT-IV-017 | ReplenishmentSuggestionGenerated | Sugestão de reposição gerada | ReplenishmentEvaluator (POL-IV-10) | Disponível ≤ ponto de pedido, regra habilitada (IV-BR-130) |
| EVT-IV-018 | ReplenishmentSuggestionConfirmed | Sugestão de reposição confirmada | CMD-IV-021 | Rota disparada: demanda PR-001 ou transferência (IV-BR-131) |
| EVT-IV-019 | ReplenishmentSuggestionDiscarded | Sugestão de reposição descartada | CMD-IV-022 | Descarte persistido, sem efeito no saldo |

**Regras gerais dos eventos:**

- Todo evento é emitido **apenas após** a confirmação da persistência (padrão Outbox — seção 14).
- Eventos que afetam saldo (EVT-IV-001..008, 010) carregam `balanceBefore` e `balanceAfter` por linha afetada (IV-BR-090) — a trilha pode ser reconstruída sem consultar o agregado.
- EVT-IV-002 e EVT-IV-006 são publicados na mesma transação quando a saída baixa reserva (IV-BR-023): `correlationId` compartilhado, `causationId` do EVT-IV-006 apontando para o comando de saída.
- EVT-IV-015/016 são eventos-alerta: publicados para o barramento e para o Notification Center; nunca bloqueiam a movimentação principal (IV-BR-083).
- Nenhum evento é emitido para consultas, comentários ou administração de locais (CMD-IV-019/020 — somente timeline/auditoria).

---

## 6. Políticas

Políticas são reações automáticas do sistema a eventos ("quando X acontecer, faça Y"). Formalizam as Policies do Domain Model (MMS-004-04 §13).

| Código | Política | Gatilho (evento) | Ação automática |
|---|---|---|---|
| POL-IV-01 | Registro de Timeline | Todos os EVT-IV-001..016 | Gravar entrada na timeline do documento/reserva/ajuste/inventário e do item (FD-001-07) |
| POL-IV-02 | Registro de Auditoria | Todos os EVT-IV-001..016 | Gravar registro imutável (FD-001-06) com saldo anterior/posterior quando aplicável (IV-BR-090) |
| POL-IV-03 | Avaliação de Alertas de Estoque | EVT-IV-001, 002, 003, 004, 010 | Reavaliar mínimo e ruptura dos itens afetados; publicar EVT-IV-015/016 quando cruzar limiares (IV-BR-080/081) |
| POL-IV-04 | Alerta na Redução do Disponível | EVT-IV-005 (reserva criada) | Reavaliar mínimo/ruptura (reserva reduz o disponível) |
| POL-IV-05 | Notificação de Reserva Vencida | EVT-IV-008 | Notificar almoxarifado e MMS-003; alimentar KPI de reservas vencidas |
| POL-IV-06 | Bloqueio por Item Inativado | Item inativado (MMS-002 — seção 13) | Bloquear novas reservas do item; saldo segue movimentável (IV-BR-007) |
| POL-IV-07 | Entrada por Recebimento | Recebimento conferido (MMS-005 — seção 13) | Gerar documento de entrada vinculado (IV-BR-010); compra dedicada → entrada já reservada |
| POL-IV-08 | Liberação por Cancelamento | Solicitação cancelada (MMS-003 — seção 13) | Liberar reservas ativas vinculadas à solicitação (IV-BR-022) |
| POL-IV-09 | Invalidação de Cache de Saldo | EVT-IV-001..008, 010 | Invalidar/atualizar projeção de leitura de saldos (IV-BR-110) |
| POL-IV-10 | Alerta de Reserva a Vencer | Avaliação periódica (janela — IV-BR-082) | Notificar almoxarifado e MMS-003 sobre reservas na janela pré-vencimento |

---

## 7. Validações Automáticas

Validações executadas **antes** da emissão do evento; a falha bloqueia o comando e nenhum evento é publicado.

| Validação | Regra de origem | Aplica-se a | Efeito em caso de falha |
|---|---|---|---|
| Origem do documento referenciável | IV-BR-002/010/011 | CMD-IV-001, 002, 006 | Erro `IV-ERR-002/010/011` |
| Saldo/disponível suficiente | IV-BR-004/005 | CMD-IV-002, 003, 004, 007, 012 | Erro `IV-ERR-004/005/020` |
| Item Ativo no catálogo | IV-BR-007 | CMD-IV-001..003, 007 | Erro `IV-ERR-007` |
| Local válido e granularidade | IV-BR-008/050/051 | CMD-IV-001..003, 016, 019 | Erro `IV-ERR-008/050` |
| Quantidade positiva na unidade do item | IV-BR-009 | Todos de movimentação | Erro `IV-ERR-009` |
| Segregação cliente/contrato | IV-BR-060/031 | CMD-IV-002, 003, 007 | Erro `IV-ERR-060/031` |
| Tamanho da grade (EPI/Fardamento) | IV-BR-120/121 | Todos de movimentação com grade | Erro `IV-ERR-120/121` |
| Justificativa e motivo do ajuste | IV-BR-040 | CMD-IV-011 | Erro `IV-ERR-040` |
| Aprovador ≠ registrante | IV-BR-041 | CMD-IV-012/013 | Erro `IV-ERR-041` |
| Motivo de estorno e vínculo | IV-BR-003 | CMD-IV-006 | Erro `IV-ERR-003` |
| Escopo de inventário sem sobreposição | IV-BR-070 | CMD-IV-014 | Erro `IV-ERR-070` |
| Contagem dentro do escopo | IV-BR-071 | CMD-IV-016 | Erro `IV-ERR-071` |
| Freeze de inventário | IV-BR-073 | Todos de movimentação | Erro `IV-ERR-073` |
| Transição de estado permitida | MMS-004-03 | Todos | Erro `IV-ERR-090` |
| Versão (optimistic concurrency) | IV-BR-012 | Todos de escrita | Erro `IV-ERR-409` |

---

## 8. Reações Esperadas

### 8.1 Timeline Service (FD-001-07)

| Evento | Reação |
|---|---|
| Todos (EVT-IV-001..016) | Criar entrada na timeline do documento/reserva/ajuste/inventário e do item: ícone da ação, ator, timestamp, resumo legível ("Entrada de 50 UN de 'Capacete de segurança' confirmada no Depósito Central", "Reserva #4821 vencida e liberada") |

### 8.2 Audit Service (FD-001-06)

| Evento | Reação |
|---|---|
| EVT-IV-001..008, 010 | Persistir registro imutável com `balanceBefore`/`balanceAfter` por linha, eventId, aggregateId, actorId, companyId, timestamp, correlationId, payload completo (IV-BR-090) |
| EVT-IV-009, 011..016 | Persistir registro imutável com payload completo e vínculos (inventário, reserva, documento de origem) |

### 8.3 Notification Service (FD-001-05)

| Evento | Reação |
|---|---|
| EVT-IV-008 | Notificar almoxarifado e solicitante (via MMS-003): reserva vencida liberada (POL-IV-05) |
| EVT-IV-009 | Notificar aprovadores: ajuste pendente de aprovação |
| EVT-IV-010 / 011 | Notificar registrante: ajuste aprovado/rejeitado |
| EVT-IV-012 | Notificar almoxarifado do escopo: inventário aberto |
| EVT-IV-015 | Notificar responsável pelo ressuprimento: item abaixo do mínimo (destinatários por `materials.inventory.alerts.recipients.*`) |
| EVT-IV-016 | Notificar com prioridade elevada: ruptura com demanda aberta |
| Demais eventos | Nenhuma notificação por padrão |

### 8.4 Projeção de Leitura de Saldo (StockBalance — IV-BR-110)

| Evento | Reação |
|---|---|
| EVT-IV-001 | Aumentar total/disponível na chave (item, tamanho?, local, segregação?) |
| EVT-IV-002 | Reduzir total; se com reserva, reduzir reservado (via EVT-IV-006) |
| EVT-IV-003 | Reduzir origem e aumentar destino (atômico — IV-BR-030) |
| EVT-IV-004 | Reverter efeito do documento original |
| EVT-IV-005 | Mover quantidade do disponível para o reservado |
| EVT-IV-006 | Reduzir reservado (e total, via EVT-IV-002) |
| EVT-IV-007 / 008 | Devolver quantidade ao disponível |
| EVT-IV-010 | Aplicar delta do ajuste |
| EVT-IV-009, 011..016 | Sem efeito na projeção de saldo |

**Nota arquitetural:** a projeção é atualizada pelo `StockBalanceService` na mesma transação da confirmação; os eventos servem para **invalidar cache e sincronizar réplicas de leitura** — nunca como caminho alternativo de escrita (INV-IV-01).

### 8.5 Módulos Consumidores

| Evento | Consumidor | Reação |
|---|---|---|
| EVT-IV-005 | MMS-003 | Marcar item da solicitação como reservado; iniciar etapa de separação |
| EVT-IV-006 | MMS-003 | Marcar item como atendido; avançar a solicitação |
| EVT-IV-007 / 008 | MMS-003 | Reavaliar atendimento do item liberado/vencido (nova reserva ou rota de compra) |
| EVT-IV-002 | MMS-003 | Confirmar entrega ao solicitante |
| EVT-IV-001 | MMS-003 | Retomar atendimento pendente por falta de estoque |
| EVT-IV-015 / 016 | PR-001 | Avaliar demanda de reposição/compra (quando parametrizado) |
| EVT-IV-001..008, 010 | PR-001 / Analytics | Atualizar posição e KPIs |
| Todos | Auditoria | Trilha completa (seção 8.2) |

---

## 9. Eventos Externos (Futuros)

Eventos fora do escopo do MVP, previstos para evolução. **Não implementar.**

| Evento externo | Origem | Finalidade futura |
|---|---|---|
| StockReplenishmentSuggested | MMS-004 (v2.0) | Reposição automática por ponto de pedido gerando PR-001 |
| StockRebalanceSuggested | MMS-004 (v2.0) | Sugestão de rebalanceamento entre depósitos |
| StockLotExpiringAlerted | MMS-004 (v2.0) | Alerta de validade de lote (controle por lote/série) |
| QuarantineStatusChanged | MMS-005 v2.0 | Quarentena de qualidade integrada |
| InventoryCycleAutoOpened | MMS-004 (v1.1) | Abertura automática de inventário cíclico por agenda ABC |

---

## 10. Eventos Não Permitidos

Ações que **não geram evento** e não existem no domínio do Inventory Management:

| Ação proibida | Motivo |
|---|---|
| BalanceUpdated / BalanceAdjusted diretamente | Saldo é projeção derivada; nenhuma escrita direta existe (IV-BR-001, MMS-P-08) |
| MovementDeleted (exclusão física) | Documentos nunca são excluídos; correção é por estorno (IV-BR-003) |
| MovementEdited após confirmação | Documento confirmado é imutável (IV-BR-003) |
| ReservationUpdated (quantidade/item/tamanho) | Reserva não se edita: libera e cria nova (IV-BR-121) |
| StockValuationChanged (fiscal/contábil) | Valorização fiscal/contábil fora do MVP (MMS-001 §8.3); custo médio é apenas referência gerencial |
| CountAppliedToBalance | Contagem nunca altera saldo; divergência via ajuste aprovado (IV-BR-071/072) |
| Qualquer evento emitido antes da persistência | Viola o padrão Outbox (ADR-010) e geraria eventos fantasmas em rollback |

---

## 11. Matriz Evento × Reação

Síntese de quem reage a cada evento. ✅ = reação obrigatória; ◐ = condicional; — = sem reação.

| Evento | Timeline | Auditoria | Notificação | Projeção de saldo | Módulos consumidores |
|---|---|---|---|---|---|
| EVT-IV-001 StockEntryRegistered | ✅ | ✅ (saldos) | — | ✅ | ✅ (MMS-003 retoma) |
| EVT-IV-002 StockIssueRegistered | ✅ | ✅ (saldos) | — | ✅ | ✅ (MMS-003 entrega) |
| EVT-IV-003 StockTransferRegistered | ✅ | ✅ (saldos) | — | ✅ | ◐ (Analytics) |
| EVT-IV-004 MovementReversed | ✅ | ✅ (saldos) | — | ✅ | ◐ |
| EVT-IV-005 ReservationCreated | ✅ | ✅ (saldos) | — | ✅ | ✅ (MMS-003) |
| EVT-IV-006 ReservationFulfilled | ✅ | ✅ (saldos) | — | ✅ | ✅ (MMS-003) |
| EVT-IV-007 ReservationReleased | ✅ | ✅ (saldos) | — | ✅ | ✅ (MMS-003) |
| EVT-IV-008 ReservationExpired | ✅ | ✅ (saldos) | ✅ (POL-IV-05) | ✅ | ✅ (MMS-003) |
| EVT-IV-009 AdjustmentRegistered | ✅ | ✅ | ✅ (aprovadores) | — | — |
| EVT-IV-010 AdjustmentApproved | ✅ | ✅ (saldos) | ✅ (registrante) | ✅ | ◐ |
| EVT-IV-011 AdjustmentRejected | ✅ | ✅ | ✅ (registrante) | — | — |
| EVT-IV-012 InventoryCountStarted | ✅ | ✅ | ✅ (escopo) | — | — |
| EVT-IV-013 CountEntryRegistered | ✅ | ✅ | — | — | — |
| EVT-IV-014 InventoryCountClosed | ✅ | ✅ | — | — | ◐ (Analytics — acuracidade) |
| EVT-IV-015 StockMinimumAlerted | ✅ | ✅ | ✅ (ressuprimento) | — | ◐ (PR-001) |
| EVT-IV-016 StockoutAlerted | ✅ | ✅ | ✅ (prioridade alta) | — | ◐ (PR-001) |
| EVT-IV-017 ReplenishmentSuggestionGenerated | ✅ | ✅ | ✅ (responsável) | — | — |
| EVT-IV-018 ReplenishmentSuggestionConfirmed | ✅ | ✅ | — | — | ✅ (PR-001, se rota compra) |
| EVT-IV-019 ReplenishmentSuggestionDiscarded | ✅ | ✅ | — | — | — |

---

## 12. Dependências

| Dependência | Tipo | Uso neste documento |
|---|---|---|
| MMS-004 (visão) | Visão do módulo | Lista funcional dos eventos |
| MMS-004-02 | Business Rules | Validações (IV-BR) e erros (IV-ERR) |
| MMS-004-03 | State Machine | Transições permitidas e estados terminais |
| MMS-004-04 | Domain Model | Comandos, invariantes, policies, StockBalanceService |
| ADR-010 | Decisão arquitetural | Mensageria (RabbitMQ), padrão Outbox, envelope de evento |
| FD-001-05 | Foundation | Notification Service (consumidor; despacho exclusivo — IV-BR-083) |
| FD-001-06 | Foundation | Audit Service (consumidor; saldo anterior/posterior) |
| FD-001-07 | Foundation | Timeline Service (consumidor) |

---

## 13. Eventos Consumidos

O Inventory Management não é apenas publicador: consome eventos dos módulos da suíte para executar suas integrações (MMS-004 README — Eventos Consumidos).

| Evento consumido | Publicador | Reação no Inventory Management | Política |
|---|---|---|---|
| Recebimento conferido | MMS-005 Receiving | Gerar documento de entrada vinculado ao recebimento; compra dedicada → entrada já reservada ao documento de origem | POL-IV-07 |
| Solicitação aprovada | MMS-003 Material Requisition | Executar validação de estoque (somente disponível — IV-BR-005) e criar reservas por item atendido | (integração síncrona de validação + CMD-IV-007) |
| Solicitação cancelada | MMS-003 Material Requisition | Liberar todas as reservas ativas vinculadas à solicitação (IV-BR-022) | POL-IV-08 |
| Item inativado | MMS-002 Item Catalog | Bloquear novas reservas do item; saldo remanescente segue movimentável até zerar (IV-BR-007) | POL-IV-06 |
| Pedido confirmado / previsão de entrega | Procurement | Registrar visibilidade de entrada futura (somente leitura; nenhuma movimentação) | — |

**Regras:**

- Toda reação que altera saldo ocorre **somente** por documento gerado a partir do evento (POL-IV-07) — nunca por efeito direto do evento no saldo (INV-IV-01);
- Consumidores são idempotentes (seção 14.1): redelivery de "Recebimento conferido" não gera entrada duplicada — a verificação de origem já processada (`GetByOrigin`) garante unicidade do documento por origem;
- Falha no processamento de evento consumido segue retry/DLQ como qualquer outra mensagem.

---

## 14. Especificação Técnica dos Eventos

### 14.1 Padrões Transversais (ADR-010)

Aplicam-se a **todos** os eventos do Inventory Management:

| Aspecto | Padrão |
|---|---|
| Transporte | RabbitMQ |
| Exchange | `trino.materials` (topic, durable) |
| Dead Letter Exchange | `trino.materials.dlq` |
| Envelope | `{ eventId, eventType, version, aggregateId, aggregateType, occurredAt, correlationId, causationId, actorId, companyId, payload }` |
| Serialização | JSON (UTF-8), campos em camelCase |
| Publicação | Padrão **Outbox**: evento gravado na tabela outbox na mesma transação do agregado; relay assíncrono publica no broker |
| Garantia de entrega | **At-least-once** (consumidor deve ser idempotente) |
| Retry | Até 5 tentativas com backoff exponencial (`messaging.retry.max-attempts=5`); após esgotar, mensagem vai para DLQ |
| Idempotência | Chave `(eventId, consumerName)` persistida pelo consumidor; redeliveries são descartadas sem efeito colateral |
| Ordenação | Garantida **por agregado** (`aggregateId` como routing key de partição); para eventos que afetam o mesmo saldo, ordenação por chave de saldo (`balanceKey`) — sem garantia de ordem global |
| Versionamento | Campo `version` no envelope; versão inicial `1` para todos os eventos deste documento |
| Correlação | `correlationId` propaga a cadeia de causação (ex.: Saída → Baixa de reserva → Entrega MMS-003); `causationId` referencia o comando/evento causador |

### 14.2 Fichas Técnicas

#### EVT-IV-001 — StockEntryRegistered

| Campo | Valor |
|---|---|
| Payload | `{ movementId, documentNumber, originType, originReference, lines[{ itemId, sizeCode?, quantity, toLocationId, segregationKey?, balanceBefore{total,reserved,available}, balanceAfter{total,reserved,available} }], confirmedBy, confirmedAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, InventoryReadModelRefresher, MMS-003 (retomada de pendências), PR-001/Analytics |
| Version | 1 |
| CorrelationId | Obrigatório (do evento/comando de origem — ex.: Recebimento conferido) |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `movementId`; efeito de saldo ordenado por `balanceKey` |
| Garantias | At-least-once; **evento crítico** — retoma atendimentos pendentes no MMS-003 |

#### EVT-IV-002 — StockIssueRegistered

| Campo | Valor |
|---|---|
| Payload | `{ movementId, documentNumber, originType, originReference, reservationId?, lines[{ itemId, sizeCode?, quantity, fromLocationId, segregationKey?, balanceBefore, balanceAfter }], confirmedBy, confirmedAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, InventoryReadModelRefresher, MMS-003 (confirmação de entrega), Analytics |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `movementId`; por `balanceKey` |
| Garantias | At-least-once; **evento crítico** — confirma entrega ao MMS-003 |

#### EVT-IV-003 — StockTransferRegistered

| Campo | Valor |
|---|---|
| Payload | `{ movementId, lines[{ itemId, sizeCode?, quantity, fromLocationId, toLocationId, segregationKey?, balanceBefore{origin}, balanceAfter{origin}, balanceBefore{destination}, balanceAfter{destination} }], confirmedBy, confirmedAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, InventoryReadModelRefresher, Analytics |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `movementId`; por `balanceKey` (origem e destino) |
| Garantias | At-least-once; publicado somente após a confirmação atômica do par (IV-BR-030) |

#### EVT-IV-004 — MovementReversed

| Campo | Valor |
|---|---|
| Payload | `{ reversalMovementId, originalMovementId, reason, reversedBy, reversedAt, lines[{ itemId, sizeCode?, quantity, balanceBefore, balanceAfter }] }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, InventoryReadModelRefresher, MMS-003 (quando o original afetava atendimento), Analytics |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `originalMovementId` (estorno sempre após o evento original) |
| Garantias | At-least-once; **evento crítico** — reverte efeitos já propagados |

#### EVT-IV-005 — ReservationCreated

| Campo | Valor |
|---|---|
| Payload | `{ reservationId, requisitionId, itemId, sizeCode?, quantity, locationId, segregationKey?, expiresAt, balanceBefore, balanceAfter, createdBy, createdAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, InventoryReadModelRefresher, MMS-003 (início da separação), StockAlertEvaluator (POL-IV-04) |
| Version | 1 |
| CorrelationId | Obrigatório (da solicitação aprovada — MMS-003) |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `reservationId`; por `balanceKey` |
| Garantias | At-least-once; **evento crítico** — habilita a separação no MMS-003 |

#### EVT-IV-006 — ReservationFulfilled

| Campo | Valor |
|---|---|
| Payload | `{ reservationId, requisitionId, movementId (saída), itemId, sizeCode?, fulfilledQuantity, remainingQuantity: 0, balanceBefore, balanceAfter, fulfilledBy, fulfilledAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, InventoryReadModelRefresher, MMS-003 (atendimento) |
| Version | 1 |
| CorrelationId | Obrigatório (compartilhado com EVT-IV-002 da mesma saída) |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `reservationId` (após ReservationCreated; junto da saída) |
| Garantias | At-least-once; publicado na mesma transação da saída (IV-BR-023) |

#### EVT-IV-007 — ReservationReleased

| Campo | Valor |
|---|---|
| Payload | `{ reservationId, requisitionId, itemId, sizeCode?, releasedQuantity, reason?, balanceBefore, balanceAfter, releasedBy, releasedAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, InventoryReadModelRefresher, MMS-003 (reavaliação) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `reservationId` |
| Garantias | At-least-once |

#### EVT-IV-008 — ReservationExpired

| Campo | Valor |
|---|---|
| Payload | `{ reservationId, requisitionId, itemId, sizeCode?, releasedQuantity, expiredAt, processedByJob, jobCycleId, balanceBefore, balanceAfter }` |
| Publisher | Inventory Service (MMS-004 — job de vencimento) |
| Consumers | Timeline, Audit, Notification (POL-IV-05), InventoryReadModelRefresher, MMS-003 (reavaliação/rota de compra) |
| Version | 1 |
| CorrelationId | Obrigatório (`jobCycleId` do ciclo do job) |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `reservationId` |
| Garantias | At-least-once; processamento do job em até 15 min após o vencimento (SLA — MMS-004-03) |

#### EVT-IV-009 — AdjustmentRegistered

| Campo | Valor |
|---|---|
| Payload | `{ adjustmentId, reasonType, justification, inventoryCountId?, lines[{ itemId, sizeCode?, quantityDelta, locationId, segregationKey? }], registeredBy, registeredAt, status }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, Notification (aprovadores) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `adjustmentId` |
| Garantias | At-least-once |

#### EVT-IV-010 — AdjustmentApproved

| Campo | Valor |
|---|---|
| Payload | `{ adjustmentId, movementId (efeito), approvedBy, approvedAt, lines[{ itemId, sizeCode?, quantityDelta, balanceBefore, balanceAfter }] }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit (saldos — IV-BR-042), Notification (registrante), InventoryReadModelRefresher, StockAlertEvaluator |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `adjustmentId`; por `balanceKey` |
| Garantias | At-least-once; **evento crítico** — corrige saldo e pode cruzar limiares de alerta |

#### EVT-IV-011 — AdjustmentRejected

| Campo | Valor |
|---|---|
| Payload | `{ adjustmentId, rejectedBy, rejectionReason, rejectedAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, Notification (registrante) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `adjustmentId` |
| Garantias | At-least-once; nenhum efeito sobre saldo |

#### EVT-IV-012 — InventoryCountStarted

| Campo | Valor |
|---|---|
| Payload | `{ inventoryCountId, scope{ type, classes?, locationIds[] }, responsibleId, deadline, itemsCount, openedBy, openedAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, Notification (almoxarifado do escopo) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `inventoryCountId` |
| Garantias | At-least-once |

#### EVT-IV-013 — CountEntryRegistered

| Campo | Valor |
|---|---|
| Payload | `{ inventoryCountId, entryId, itemId, sizeCode?, locationId, countedQty, systemQtySnapshot, countedBy, countedAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `inventoryCountId` |
| Garantias | At-least-once; contagem nunca altera saldo (IV-BR-071) |

#### EVT-IV-014 — InventoryCountClosed

| Campo | Valor |
|---|---|
| Payload | `{ inventoryCountId, summary{ counted, divergent, adjusted, withinTolerance }, adjustmentIds[], accuracyRate, closedBy, closedAt }` |
| Publisher | Inventory Service (MMS-004) |
| Consumers | Timeline, Audit, Analytics (KPI acuracidade), Notification (gerência) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `inventoryCountId` (após todas as CountEntry do ciclo) |
| Garantias | At-least-once; alimenta o KPI central acuracidade ≥ 98% |

#### EVT-IV-015 — StockMinimumAlerted

| Campo | Valor |
|---|---|
| Payload | `{ alertId, itemId, sizeCode?, locationId, availableQty, minStock, deficit, triggeredByMovementId, alertedAt }` |
| Publisher | Inventory Service (MMS-004 — StockAlertEvaluator) |
| Consumers | Timeline, Audit, Notification (ressuprimento), PR-001 (demanda de reposição, quando parametrizado) |
| Version | 1 |
| CorrelationId | Obrigatório (da movimentação que cruzou o limiar) |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName); sem alerta aberto duplicado para a mesma condição (IV-BR-080) |
| Ordem | Por `itemId`+`locationId` |
| Garantias | At-least-once; nunca bloqueia a movimentação principal (IV-BR-083) |

#### EVT-IV-016 — StockoutAlerted

| Campo | Valor |
|---|---|
| Payload | `{ alertId, itemId, sizeCode?, locationId, openDemand[{ requisitionId, quantity }], triggeredBy, alertedAt }` |
| Publisher | Inventory Service (MMS-004 — StockAlertEvaluator) |
| Consumers | Timeline, Audit, Notification (prioridade elevada), PR-001 (demanda urgente) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `itemId`+`locationId` |
| Garantias | At-least-once; **evento crítico** — ruptura é KPI da suíte e gatilho emergencial |

### 14.3 Matriz Resumo de Garantias

| Evento | At-least-once | Idempotente | Ordenado por agregado | Crítico (alerta em falha de entrega) |
|---|---|---|---|---|
| EVT-IV-001 StockEntryRegistered | ✅ | ✅ | ✅ | **Sim** |
| EVT-IV-002 StockIssueRegistered | ✅ | ✅ | ✅ | **Sim** |
| EVT-IV-003 StockTransferRegistered | ✅ | ✅ | ✅ | Não |
| EVT-IV-004 MovementReversed | ✅ | ✅ | ✅ | **Sim** |
| EVT-IV-005 ReservationCreated | ✅ | ✅ | ✅ | **Sim** |
| EVT-IV-006 ReservationFulfilled | ✅ | ✅ | ✅ | **Sim** |
| EVT-IV-007 ReservationReleased | ✅ | ✅ | ✅ | Não |
| EVT-IV-008 ReservationExpired | ✅ | ✅ | ✅ | **Sim** |
| EVT-IV-009 AdjustmentRegistered | ✅ | ✅ | ✅ | Não |
| EVT-IV-010 AdjustmentApproved | ✅ | ✅ | ✅ | **Sim** |
| EVT-IV-011 AdjustmentRejected | ✅ | ✅ | ✅ | Não |
| EVT-IV-012 InventoryCountStarted | ✅ | ✅ | ✅ | Não |
| EVT-IV-013 CountEntryRegistered | ✅ | ✅ | ✅ | Não |
| EVT-IV-014 InventoryCountClosed | ✅ | ✅ | ✅ | Não |
| EVT-IV-015 StockMinimumAlerted | ✅ | ✅ | ✅ | Não |
| EVT-IV-016 StockoutAlerted | ✅ | ✅ | ✅ | **Sim** |

### 14.4 Rastreabilidade Comando → Evento

| Comando | Evento publicado | Seção de validação |
|---|---|---|
| CMD-IV-001 + 004 (entrada) | EVT-IV-001 StockEntryRegistered | Seção 7 (origem, item, local, quantidade, segregação, tamanho) |
| CMD-IV-002 + 004 (saída) | EVT-IV-002 StockIssueRegistered (+ EVT-IV-006 quando com reserva) | Seção 7 (saldo, reserva, segregação, tamanho) |
| CMD-IV-003 + 004 (transferência) | EVT-IV-003 StockTransferRegistered | Seção 7 (IV-BR-030/031) |
| CMD-IV-005 CancelMovement | — (somente timeline) | MMS-004-03 (somente Rascunho) |
| CMD-IV-006 ReverseMovement | EVT-IV-004 MovementReversed | Seção 7 (IV-BR-003) |
| CMD-IV-007 CreateReservation | EVT-IV-005 ReservationCreated | Seção 7 (disponível, item ativo, segregação, tamanho) |
| CMD-IV-008 ReleaseReservation | EVT-IV-007 ReservationReleased | Seção 7 (IV-BR-022) |
| CMD-IV-009 ExpireReservation | EVT-IV-008 ReservationExpired | IV-BR-021 (job) |
| CMD-IV-010 FulfillReservation | EVT-IV-006 ReservationFulfilled | Seção 7 (IV-BR-023/121) |
| CMD-IV-011 RegisterAdjustment | EVT-IV-009 AdjustmentRegistered | Seção 7 (IV-BR-040) |
| CMD-IV-012 ApproveAdjustment | EVT-IV-010 AdjustmentApproved | Seção 7 (IV-BR-041/004) |
| CMD-IV-013 RejectAdjustment | EVT-IV-011 AdjustmentRejected | Seção 7 (motivo) |
| CMD-IV-014 OpenInventoryCount | EVT-IV-012 InventoryCountStarted | Seção 7 (IV-BR-070) |
| CMD-IV-015 StartCounting | — (mudança de estado; timeline) | MMS-004-03 |
| CMD-IV-016 RegisterCount | EVT-IV-013 CountEntryRegistered | Seção 7 (IV-BR-071) |
| CMD-IV-017 CloseInventory | EVT-IV-014 InventoryCountClosed | Seção 7 (IV-BR-072) |
| CMD-IV-018 CancelInventory | — (somente timeline) | MMS-004-03 |
| CMD-IV-019 ManageLocation | — (administrativo; timeline/auditoria) | Seção 7 (IV-BR-050..052) |
| CMD-IV-020 AddComment | — (somente timeline) | — |
| CMD-IV-021 ConfirmReplenishmentSuggestion | EVT-IV-018 ReplenishmentSuggestionConfirmed | IV-BR-131 (rota compra→PR-001 / transferência) |
| CMD-IV-022 DiscardReplenishmentSuggestion | EVT-IV-019 ReplenishmentSuggestionDiscarded | — |
| (automático) StockAlertEvaluator | EVT-IV-015 / EVT-IV-016 | IV-BR-080/081 (POL-IV-03/04) |
| (automático) ReplenishmentEvaluator | EVT-IV-017 ReplenishmentSuggestionGenerated | IV-BR-130 (POL-IV-10) |

> **Fichas técnicas (14.2):** EVT-IV-017..019 seguem os mesmos padrões transversais do §14.1 (exchange `trino.materials`, Outbox, at-least-once, retry 5×, DLQ, idempotência por `eventId`), com routing keys `evt.inventory.replenishment-suggested|confirmed|discarded` e ordenação por `aggregateId` da sugestão. EVT-IV-018 com rota `compra` tem como consumidor o PR-001 (geração de demanda com referência à origem).

---

## Histórico de Versão

| Versão | Data | Autor | Descrição |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: Event Storming completo do Inventory Management com 20 comandos, 16 eventos de domínio, 10 políticas, validações automáticas, matriz evento × reação, 5 eventos consumidos (MMS-002/003/005/Procurement) com regras de idempotência por origem, e especificação técnica por evento conforme ADR-010 (exchange `trino.materials`, Outbox, at-least-once, retry 5×, DLQ, ordenação por agregado e por chave de saldo). |
| 1.1.0 | 2026-08-08 | Arquiteto Principal | Incorporação de ADR-013 e ADR-014: 2 novos comandos (CMD-IV-021 ConfirmReplenishmentSuggestion, CMD-IV-022 DiscardReplenishmentSuggestion) + geração automática via ReplenishmentEvaluator (POL-IV-10); 3 novos eventos (EVT-IV-017..019) — total 19; nota de conversão de UoM na captura de movimentação (UomConversionApplier, sem evento próprio); matriz evento×reação e rastreabilidade comando→evento atualizadas. |
