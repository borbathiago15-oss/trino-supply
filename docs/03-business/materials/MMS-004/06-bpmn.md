# MMS-004-06 — Business Process Specification (BPMN)

**Documento:** MMS-004-06 — Business Process Specification (BPMN)
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-004 v1.0.0, MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-04 (Domain Model), MMS-004-05 (Event Storming), MMS-001, MMS-002, MMS-003, MMS-005, ADR-010, FD-001-04, FD-001-05, FD-001-06, FD-001-07, FD-001-09
**Referências:** MMS-002-06 (padrão de formato Enterprise), PR-001-06, GOV-001

> Especificação do processo de negócio do Inventory Management: movimentações de estoque, reservas, ajustes e inventários.

---

# 1. Objetivo

Descrever os fluxos operacionais completos do Estoque: (a) movimentação de entrada — originada do recebimento (MMS-005) ou manual; (b) movimentação de saída — atendimento de reserva originada de solicitação aprovada (MMS-003) ou saída avulsa; (c) transferência entre locais; (d) reserva com vencimento automático; (e) ajuste com aprovação; (f) inventário com contagem e ajuste de divergências; (g) estorno como compensação formal; (h) alertas de estoque mínimo e ruptura.

Este documento é a base para modelagem BPMN 2.0 e para implementação do módulo. Em caso de conflito: transições de estado prevalecem conforme MMS-004-03; validações conforme MMS-004-02; contratos de eventos conforme MMS-004-05 §13/§14.

---

# 2. Participantes (Pools e Lanes)

## Pool: Empresa

### Lane: Almoxarife (Operador de Estoque)
- Registra e confirma movimentações (entrada, saída, transferência)
- Cria, atende e libera reservas
- Executa contagens de inventário
- Cadastra e mantém locais de armazenagem

### Lane: Supervisor de Almoxarifado
- Todas as ações do Almoxarife
- Registra ajustes
- Estorna documentos confirmados (com motivo obrigatório)
- Abre e fecha inventários

### Lane: Gestor de Suprimentos
- Aprova ou rejeita ajustes (Segregation of Duties: quem registra não aprova)
- Acompanha indicadores e alertas

### Lane: Administrador
- Administra configurações do módulo (`materials.inventory.*`)
- Trata exceções de segurança e integridade

### Lane: Sistema
- Executa validações automáticas (guards de saldo, segregação, tolerância)
- Aplica efeito de saldo via StockBalanceService (único ponto de efeito — INV-IV-01)
- Publica eventos de domínio (Outbox → RabbitMQ)
- Opera timers de vencimento de reserva e janelas de alerta
- Registra timeline e auditoria (saldo anterior/posterior)

### Lane: Módulos Integrados (MMS-002 / MMS-003 / MMS-005)
- MMS-005: origina entradas após conferência de recebimento
- MMS-003: origina reservas de solicitações aprovadas; consome confirmações de atendimento
- MMS-002: fornece dados do item; inativações bloqueiam novas movimentações

---

# 3. Eventos Iniciais

### Start Event 1 — Recebimento Conferido (externo)

**Tipo BPMN:** Message Start Event.
**Mensagem:** MSG-IV-C01 `ReceivingConferenceCompleted` (publicado pelo MMS-005; contrato em MMS-004-05 §13).
**Resultado:** início do Fluxo A (Entrada por Recebimento).

### Start Event 2 — Solicitação Aprovada (externo)

**Tipo BPMN:** Message Start Event.
**Mensagem:** MSG-IV-C02 `RequisitionApproved` (publicado pelo MMS-003).
**Resultado:** início do Fluxo C (Reserva por Solicitação).

### Start Event 3 — Operação Manual

**Tipo BPMN:** None Start Event (acionado por Almoxarife/Supervisor).
**Gatilhos:** entrada avulsa, saída avulsa, transferência, ajuste, abertura de inventário, cadastro de local, estorno.

### Start Event 4 — Vencimento de Reserva (tempo)

**Tipo BPMN:** Timer Start Event (agendamento por reserva).
**Gatilho:** TMR-IV-001 — atingido `reservation.ttl` (padrão 72h, configurável).
**Resultado:** sub-fluxo de vencimento (§4, Fluxo C — Caminho 3).

---

# 4. Fluxos Principais

## Fluxo A — Movimentação de Entrada

### Atividade A1 — Gerar Documento de Entrada

Responsável: Sistema (origem MMS-005) ou Almoxarife (entrada avulsa).

- Origem externa: documento criado automaticamente a partir da conferência do recebimento, com referência ao documento de origem (MMS-RG-06)
- Origem manual: Almoxarife informa itens, quantidades, local de destino e documento de referência

**Tipo BPMN:** Service Task (origem externa) / User Task (origem manual).
**Business Object de saída:** BO-IV-001 StockMovement (tipo = Entrada, status = Rascunho, ST-IV-001).
**Data Store:** DS-IV-001 Transactional Database (PostgreSQL, schema `materials`).

### Atividade A2 — Validar e Confirmar Entrada

Executada pelo Sistema (Sub-Processo SP-IV-01, §16.1):

- Item ativo no catálogo (IV-BR-010; POL-IV-06 — item inativado bloqueia)
- Quantidade > 0 por linha (IV-BR-011)
- Local de destino ativo e pertencente à empresa (IV-BR-060..062)
- Tamanho obrigatório quando o item possui grade (IV-BR-120)
- Idempotência por chave de idempotência do documento de origem (IV-BR-090)

**Tipo BPMN:** Service Task.
**Timeout:** TIME-IV-001 — 30 s; em falha técnica, EXC-IV-007 (§11, §15.2).

### Atividade A3 — Efetivar Saldo

StockBalanceService aplica o efeito linha a linha sobre a chave de saldo (empresa, item, local, tamanho quando aplicável), registrando saldo anterior e posterior por linha (IV-BR-002, IV-BR-095).

**Tipo BPMN:** Service Task (transacional com A2).
**Message Event:** MSG-IV-001 `StockEntryRegistered` (EVT-IV-001) no exchange `trino.materials`.

### Atividade A4 — Avaliar Alertas

Após o efeito, o Sistema reavalia ponto de reposição do item (POL-IV-09): entrada pode **cancelar** alerta de mínimo/ruptura vigente.

**Tipo BPMN:** Service Task.
**Message Event condicional:** notificação de normalização via FD-001-05.

---

## Fluxo B — Movimentação de Saída (Atendimento)

### Atividade B1 — Gerar Documento de Saída

Origens: atendimento de reserva (Fluxo C — Caminho 1) ou saída avulsa do Almoxarife (ex.: consumo interno sem reserva, quando permitido por `materials.inventory.issue.without-reservation`).

**Tipo BPMN:** User Task.
**Business Object:** BO-IV-001 StockMovement (tipo = Saída, status = Rascunho).

### Atividade B2 — Validar e Confirmar Saída

Sub-Processo SP-IV-01 com guards adicionais:

- **Saldo disponível suficiente** por linha (IV-BR-020; MMS-RG-04 — saldo nunca negativo)
- **Segregação** cliente/contrato quando habilitada (IV-BR-070; MMS-RG-10; config `materials.inventory.segregation.enabled`)
- Reserva vinculada **ativa e não vencida** quando a saída atende reserva (IV-BR-030..034)

**Tipo BPMN:** Service Task.

### Gateway GW-IV-001 — Saldo/Segregação OK?

- **SIM** → Atividade B3
- **NÃO** → EXC-IV-001 (saldo insuficiente) ou EXC-IV-002 (segregação violada); documento permanece Rascunho com motivo (§11)

**Tipo BPMN:** Exclusive Gateway (XOR). **Regras:** §9.1.

### Atividade B3 — Efetivar Saída e Baixar Reserva

Efeito de saída aplicado; quando vinculada a reserva, a quantidade atendida é baixada da reserva (total → Reserva Atendida; parcial → permanece Ativa com saldo restante).

**Tipo BPMN:** Service Task (transacional).
**Message Events:** MSG-IV-002 `StockIssueRegistered` (EVT-IV-002); MSG-IV-006 `ReservationFulfilled` (EVT-IV-006) quando baixa total.

### Atividade B4 — Avaliar Alertas de Mínimo/Ruptura

Reavaliação pós-saída (POL-IV-09/POL-IV-10): saldo ≤ mínimo → MSG-IV-015 `StockMinimumAlerted` (EVT-IV-015); saldo = 0 → MSG-IV-016 `StockoutAlerted` (EVT-IV-016, prioridade alta).

**Tipo BPMN:** Service Task.

---

## Fluxo C — Reserva

### Atividade C1 — Criar Reserva

Origens: MSG-IV-C02 (solicitação aprovada do MMS-003 — automática) ou Almoxarife (manual).

Validações: item ativo; quantidade > 0; saldo disponível ≥ quantidade reservada (IV-BR-030); local ativo; tamanho quando grade (IV-BR-121).

**Tipo BPMN:** Service Task (origem externa) / User Task (manual).
**Business Object:** BO-IV-002 Reservation (status = Ativa, ST-IV-010; `expiresAt = now + reservation.ttl`).
**Message Event:** MSG-IV-005 `ReservationCreated` (EVT-IV-005) — consumido pelo MMS-003 (solicitação em separação).

### Gateway GW-IV-005 — Destino da Reserva

Três caminhos a partir de Reserva Ativa:

**Caminho 1 — Atendimento** → Fluxo B (saída vinculada); baixa total → Reserva Atendida (ST-IV-011), MSG-IV-006.

**Caminho 2 — Liberação Manual** → Atividade C2.

**Caminho 3 — Vencimento (timer)** → Atividade C3.

**Tipo BPMN:** Exclusive Gateway baseado em evento (Event-Based Gateway).

### Atividade C2 — Liberar Reserva

Responsável: Almoxarife/Supervisor (manual) ou Sistema (reação a MSG-IV-C03 `RequisitionCancelled` — POL-IV-08).

Saldo reservado retorna ao disponível.

**Tipo BPMN:** User Task / Service Task.
**Message Event:** MSG-IV-007 `ReservationReleased` (EVT-IV-007).

### Atividade C3 — Vencer Reserva (Timer)

Disparado por TMR-IV-001 (`expiresAt` atingido). Saldo reservado retorna ao disponível; reserva → Vencida (ST-IV-013).

**Tipo BPMN:** Timer Intermediate Catch (interrupting) sobre a reserva Ativa + Service Task.
**Message Event:** MSG-IV-008 `ReservationExpired` (EVT-IV-008) — consumido pelo MMS-003 (necessidade reaberta) e FD-001-05 (notificação).
**Timer auxiliar:** TMR-IV-002 (non-interrupting, `expiring-window` = 24h antes) → alerta "reserva vencendo" via FD-001-05.

---

## Fluxo D — Transferência entre Locais

### Atividade D1 — Registrar Transferência

Responsável: Almoxarife. Origem e destino ativos, distintos, mesma empresa (IV-BR-050..053); saldo disponível na origem; tamanho quando grade.

**Tipo BPMN:** User Task.

### Atividade D2 — Confirmar Transferência

Efeito atômico: saída na origem + entrada no destino na mesma transação (INV-IV-09); saldo global por item inalterado.

**Tipo BPMN:** Service Task.
**Message Event:** MSG-IV-003 `StockTransferRegistered` (EVT-IV-003).
**Exceção:** EXC-IV-001 na origem → documento permanece Rascunho.

---

## Fluxo E — Ajuste com Aprovação

### Atividade E1 — Registrar Ajuste

Responsável: Supervisor. Tipo (positivo/negativo), motivo estruturado (`materials.inventory.adjustment.reasons`, FD-001-09), itens/quantidades/locais (IV-BR-080..083).

**Tipo BPMN:** User Task.
**Business Object:** BO-IV-003 Adjustment (status = Pendente, ST-IV-020).
**Message Event:** MSG-IV-009 `AdjustmentRegistered` (EVT-IV-009) — notifica aprovadores (POL-IV-05).

### Gateway GW-IV-003 — Ajuste Aprovado?

Responsável pela decisão: Gestor de Suprimentos (**SoD: quem registrou não aprova** — IV-BR-085).

- **APROVAR** → Atividade E2
- **REJEITAR** → Atividade E3

**Tipo BPMN:** Exclusive Gateway (XOR). **Regras:** §9.3.
**Timer:** TMR-IV-003 — SLA de aprovação (`adjustment.approval.sla.hours`, padrão 24h) → ESC-IV-001.

### Atividade E2 — Aplicar Ajuste Aprovado

Efeito de saldo (positivo soma, negativo subtrai — negativo exige saldo suficiente, IV-BR-084).

**Tipo BPMN:** Service Task.
**Message Event:** MSG-IV-010 `AdjustmentApproved` (EVT-IV-010).

### Atividade E3 — Rejeitar Ajuste

Motivo da rejeição obrigatório; sem efeito de saldo.

**Tipo BPMN:** User Task + Message Intermediate Throw.
**Message Event:** MSG-IV-011 `AdjustmentRejected` (EVT-IV-011).

---

## Fluxo F — Inventário (Contagem Física)

### Atividade F1 — Abrir Inventário

Responsável: Supervisor. Escopo: por local, por item/família ou geral; tipo (geral ou cíclico — classe A = 30d, B = 90d, C = 180d, `materials.inventory.cycle-count.*`).

**Tipo BPMN:** User Task.
**Business Object:** BO-IV-004 InventoryCount (status = Aberto, ST-IV-030).
**Message Event:** MSG-IV-012 `InventoryCountStarted` (EVT-IV-012).

### Atividade F2 — Registrar Contagens

Responsável: Almoxarife. Inventário → Em Contagem (ST-IV-031) no primeiro lançamento. Congelamento de movimentação no escopo somente se `count.freeze = true` (padrão false — IV-BR-100).

**Tipo BPMN:** User Task (multi-instância por item/endereço).
**Message Event:** MSG-IV-013 `CountEntryRegistered` (EVT-IV-013).

### Atividade F3 — Apurar Divergências

Sistema compara contado × saldo sistêmico por chave de saldo.

**Tipo BPMN:** Service Task.

### Gateway GW-IV-004 — Divergência > Tolerância?

Tolerância padrão 0 (`materials.inventory.count.tolerance`).

- **SEM DIVERGÊNCIA (ou dentro da tolerância)** → Atividade F4
- **COM DIVERGÊNCIA** → Atividade F5

**Tipo BPMN:** Exclusive Gateway (XOR). **Regras:** §9.4.

### Atividade F4 — Fechar Inventário sem Ajuste

Inventário → Fechado (ST-IV-032); acuracidade registrada.

**Tipo BPMN:** Service Task + Message End.
**Message Event:** MSG-IV-014 `InventoryCountClosed` (EVT-IV-014).

### Atividade F5 — Gerar Ajustes de Divergência

Para cada divergência, o sistema gera BO-IV-003 Adjustment (tipo e quantidade derivados) vinculado ao inventário → segue Fluxo E completo (aprovação obrigatória, IV-BR-101). Inventário só fecha após conclusão dos ajustes vinculados.

**Tipo BPMN:** Service Task + Call Activity (Fluxo E).
**Message Event:** MSG-IV-014 após ajustes concluídos.

---

## Fluxo G — Estorno (Compensação Formal)

### Atividade G1 — Registrar Estorno

Responsável: Supervisor. Somente documento Confirmado (ST-IV-002) pode ser estornado; motivo obrigatório (IV-BR-110..112). O estorno é um **novo documento** com efeito inverso — nunca altera o original (MMS-P-08; INV-IV-05).

**Tipo BPMN:** User Task.

### Gateway GW-IV-002 — Estorno Viável?

- Efeito inverso não torna saldo negativo (IV-BR-113)?
- Documento original não estornado anteriormente (IV-BR-111)?

**SIM** → Atividade G2 · **NÃO** → EXC-IV-004.

**Tipo BPMN:** Exclusive Gateway (XOR). **Regras:** §9.2.

### Atividade G2 — Efetivar Estorno

Efeito inverso aplicado; original → Estornado (ST-IV-003); estorno nasce Confirmado.

**Tipo BPMN:** Service Task (transacional).
**Message Event:** MSG-IV-004 `MovementReversed` (EVT-IV-004) — compensação COMP-IV-001 (§15.4).

---

# 5. Fluxos Alternativos

## FA-IV-001 — Saldo Insuficiente na Confirmação

**Origem:** GW-IV-001 (NÃO — saldo).
**Detalhamento:** documento permanece Rascunho com indicação por linha (disponível × solicitado); Almoxarife ajusta quantidades, altera local de origem ou cancela. Nenhum efeito parcial é aplicado (INV-IV-04 — atomicidade do documento). Tentativa registrada em Timeline/Audit.

## FA-IV-002 — Violação de Segregação

**Origem:** GW-IV-001 (NÃO — segregação).
**Detalhamento:** quando `segregation.enabled = true`, saldo de cliente/contrato distinto não é considerado disponível; exceção EXC-IV-002 com auditoria. Sem contorno por configuração de módulo (MMS-RG-10).

## FA-IV-003 — Item Inativado com Reservas Ativas

**Origem:** MSG-IV-C04 `ItemInactivated` (MMS-002) — POL-IV-06.
**Detalhamento:** novas movimentações/reservas do item são bloqueadas (IV-BR-010); reservas ativas existentes são sinalizadas ao Almoxarifado para atendimento ou liberação em até 5 dias úteis (SLA §12). Nenhuma reserva é cancelada automaticamente.

## FA-IV-004 — Divergência de Inventário com Ajuste Rejeitado

**Origem:** Fluxo F5 → Fluxo E → GW-IV-003 (REJEITAR).
**Detalhamento:** divergência permanece apontada no inventário; Supervisor registra novo ajuste com evidência complementar ou registra justificativa de manutenção da divergência (auditoria obrigatória). Inventário não fecha com divergência pendente sem justificativa (IV-BR-102).

## FA-IV-005 — Entrada Duplicada (Idempotência)

**Origem:** Atividade A2.
**Detalhamento:** mesma chave de idempotência do documento de origem → sistema retorna o documento já registrado, sem efeito duplicado (IV-BR-090). Ocorrência registrada em auditoria.

## FA-IV-006 — Reserva Vencida com Entrega em Separação

**Origem:** TMR-IV-001.
**Detalhamento:** o vencimento é automático e prevalece; se havia separação em curso, a saída vinculada falhará na validação de reserva ativa (IV-BR-033) e o Almoxarife deve criar nova reserva (troca de tamanho de EPI/Fardamento sempre exige liberação + nova reserva — IV-BR-121).

---

# 6. Gateways

## GW-IV-001 — Validação de Saldo e Segregação
Resultado: confirmar saída/transferência ou exceção (EXC-IV-001/002).

## GW-IV-002 — Viabilidade de Estorno
Resultado: efetivar estorno ou EXC-IV-004.

## GW-IV-003 — Decisão de Aprovação de Ajuste
Resultado: aprovar (aplica efeito) ou rejeitar (sem efeito).

## GW-IV-004 — Divergência de Inventário
Resultado: fechar sem ajuste ou gerar ajustes vinculados.

## GW-IV-005 — Destino da Reserva (Event-Based)
Resultado: atendimento (Fluxo B), liberação (C2) ou vencimento (C3).

**As regras detalhadas de cada gateway estão na Seção 9.**

---

# 7. Objetos de Dados

StockMovement · MovementLine · Reservation · Adjustment · AdjustmentLine · InventoryCount · CountEntry · Location · StockBalance (projeção) · StockAlert · Timeline Entry · Audit Record

**Especificação completa dos Business Objects e Data Stores na Seção 8.1.**

---

# 8. Eventos BPMN

### Start Events
ReceivingConferenceCompleted (message) · RequisitionApproved (message) · Operação Manual (none) · ReservationExpirationReached (timer)

### Intermediate Events
StockEntryRegistered · StockIssueRegistered · StockTransferRegistered · MovementReversed · ReservationCreated · ReservationFulfilled · ReservationReleased · ReservationExpired · AdjustmentRegistered · AdjustmentApproved · AdjustmentRejected · InventoryCountStarted · CountEntryRegistered · InventoryCountClosed · StockMinimumAlerted · StockoutAlerted · ItemInactivated (catch) · RequisitionCancelled (catch)

### End Events
Documento Confirmado (efeito aplicado) · Documento Cancelado · Reserva Atendida/Liberada/Vencida · Ajuste Aprovado/Rejeitado · Inventário Fechado · Documento Estornado

---

## 8.1 Business Objects e Data Stores (detalhamento Enterprise)

### Business Objects

| Código | Business Object | Descrição | Estados possíveis no processo | Persistência |
| ------ | --------------- | --------- | ------------------------------ | ------------ |
| BO-IV-001 | StockMovement | Documento de movimentação (entrada/saída/transferência/estorno) | Rascunho, Confirmado, Estornado, Cancelado | DS-IV-001 |
| BO-IV-002 | Reservation | Reserva de saldo com validade | Ativa, Atendida, Liberada, Vencida | DS-IV-001 |
| BO-IV-003 | Adjustment | Ajuste de saldo com aprovação | Pendente, Aprovado, Rejeitado, Estornado | DS-IV-001 |
| BO-IV-004 | InventoryCount | Inventário (geral/cíclico) | Aberto, Em Contagem, Fechado, Cancelado | DS-IV-001 |
| BO-IV-005 | CountEntry | Lançamento de contagem por chave de saldo | Registrado (imutável após fechamento) | DS-IV-001 |
| BO-IV-006 | Location | Local de armazenagem (almoxarifado→depósito→endereço) | Ativo, Inativo | DS-IV-001 |
| BO-IV-007 | StockBalance | Projeção de saldo (nunca editável diretamente — INV-IV-01) | Vigente | DS-IV-001 + DS-IV-004 |
| BO-IV-008 | StockAlert | Alerta de mínimo/ruptura | Aberto, Normalizado | DS-IV-001 |
| BO-IV-009 | Timeline Entry | Registro cronológico por documento | Imutável (append-only) | DS-IV-001 |
| BO-IV-010 | Audit Record | Registro de auditoria (inclui saldo anterior/posterior) | Imutável (append-only) | DS-IV-001 |

### Data Stores

| Código | Data Store | Tecnologia | Acesso |
| ------ | ---------- | ---------- | ------ |
| DS-IV-001 | Transactional Database | PostgreSQL (schema `materials`) | Leitura/escrita transacional via repositórios; isolamento por `company_id`; efeito de saldo exclusivo do StockBalanceService |
| DS-IV-002 | Message Broker | RabbitMQ | Exchange `trino.materials` (eventos EVT-IV) + consumo de MMS-002/003/005; DLQ `trino.materials.dlq` |
| DS-IV-003 | Notification Center | FD-001-05 | Alertas e lembretes (mínimo, ruptura, reserva vencendo/vencida, ajuste pendente/aprovado/rejeitado) |
| DS-IV-004 | Distributed Cache | Redis | Projeção de leitura da posição de estoque (`materials.inventory.cache.ttl`); nunca fonte de verdade |
| DS-IV-005 | Master Data Store | PostgreSQL (FD-001-09) | Motivos de ajuste estruturados, unidades |
| DS-IV-006 | Document Storage | MinIO (FD-001-03) | Evidências de contagem/ajuste quando anexadas; URLs assinadas |

---

## 8.2 Message Events (detalhamento Enterprise)

| Código | Message Event | Tipo BPMN | Momento | Payload/Referência |
| ------ | ------------- | --------- | ------- | ------------------ |
| MSG-IV-C01 | ReceivingConferenceCompleted | Message Start | Início do Fluxo A | MMS-004-05 §13 (publicado por MMS-005) |
| MSG-IV-C02 | RequisitionApproved | Message Start | Início do Fluxo C (C1 automática) | MMS-004-05 §13 (publicado por MMS-003) |
| MSG-IV-C03 | RequisitionCancelled | Intermediate Catch | POL-IV-08 → Atividade C2 | MMS-004-05 §13 (publicado por MMS-003) |
| MSG-IV-C04 | ItemInactivated | Intermediate Catch | FA-IV-003 → POL-IV-06 | MMS-004-05 §13 (publicado por MMS-002) |
| MSG-IV-001 | StockEntryRegistered | Intermediate Throw | Após A3 | MMS-004-05 §14 EVT-IV-001 |
| MSG-IV-002 | StockIssueRegistered | Intermediate Throw | Após B3 | MMS-004-05 §14 EVT-IV-002 |
| MSG-IV-003 | StockTransferRegistered | Intermediate Throw | Após D2 | MMS-004-05 §14 EVT-IV-003 |
| MSG-IV-004 | MovementReversed | Intermediate Throw | Após G2 | MMS-004-05 §14 EVT-IV-004 |
| MSG-IV-005 | ReservationCreated | Intermediate Throw | Após C1 | MMS-004-05 §14 EVT-IV-005 |
| MSG-IV-006 | ReservationFulfilled | Intermediate Throw | Baixa total em B3 | MMS-004-05 §14 EVT-IV-006 |
| MSG-IV-007 | ReservationReleased | Intermediate Throw | Após C2 | MMS-004-05 §14 EVT-IV-007 |
| MSG-IV-008 | ReservationExpired | Intermediate Throw | Após C3 | MMS-004-05 §14 EVT-IV-008 |
| MSG-IV-009 | AdjustmentRegistered | Intermediate Throw | Após E1 | MMS-004-05 §14 EVT-IV-009 |
| MSG-IV-010 | AdjustmentApproved | Intermediate Throw | Após E2 | MMS-004-05 §14 EVT-IV-010 |
| MSG-IV-011 | AdjustmentRejected | Intermediate Throw | Após E3 | MMS-004-05 §14 EVT-IV-011 |
| MSG-IV-012 | InventoryCountStarted | Intermediate Throw | Após F1 | MMS-004-05 §14 EVT-IV-012 |
| MSG-IV-013 | CountEntryRegistered | Intermediate Throw | Após cada lançamento em F2 | MMS-004-05 §14 EVT-IV-013 |
| MSG-IV-014 | InventoryCountClosed | Message End | Após F4/F5 | MMS-004-05 §14 EVT-IV-014 |
| MSG-IV-015 | StockMinimumAlerted | Intermediate Throw | B4/D2/G2 (saldo ≤ mínimo) | MMS-004-05 §14 EVT-IV-015 |
| MSG-IV-016 | StockoutAlerted | Intermediate Throw | B4/D2/G2 (saldo = 0) | MMS-004-05 §14 EVT-IV-016 |

---

# 9. Regras por Gateway (detalhamento Enterprise)

## 9.1 RG-IV-GW-001 — Saldo e Segregação (saída/transferência)

| Código | Condição avaliada | Regra de negócio associada | Resultado quando falha |
| ------ | ----------------- | -------------------------- | ---------------------- |
| RG-IV-GW-001-A | Saldo disponível ≥ quantidade por linha (chave empresa+item+local+tamanho) | IV-BR-020, MMS-RG-04 | EXC-IV-001 (`IV-ERR-020`) — documento permanece Rascunho |
| RG-IV-GW-001-B | Item ativo no catálogo | IV-BR-010 | EXC-IV-003 (`IV-ERR-010`) |
| RG-IV-GW-001-C | Local de origem/destino ativo e da empresa | IV-BR-060..062 | EXC-IV-003 (`IV-ERR-060`) |
| RG-IV-GW-001-D | Segregação cliente/contrato respeitada (quando habilitada) | IV-BR-070, MMS-RG-10 | EXC-IV-002 (`IV-ERR-070`) — auditoria obrigatória |
| RG-IV-GW-001-E | Reserva vinculada ativa e não vencida (saída por reserva) | IV-BR-030..034 | EXC-IV-005 (`IV-ERR-030`) |
| RG-IV-GW-001-F | Tamanho informado quando item possui grade | IV-BR-120/121 | EXC-IV-003 (`IV-ERR-120`) |
| RG-IV-GW-001-G | Operador possui permissão no escopo organizacional | MMS-004-09 Permissions | EXC-IV-006 (bloqueio de segurança; não retorna ao rascunho) |

**Semântica:** falhas A–F → documento permanece Rascunho com motivo por linha (FA-IV-001/002). Falha G → exceção de segurança (§11).

## 9.2 RG-IV-GW-002 — Viabilidade de Estorno

| Código | Condição avaliada | Regra de negócio associada | Resultado |
| ------ | ----------------- | -------------------------- | --------- |
| RG-IV-GW-002-A | Documento original Confirmado e ainda não estornado | IV-BR-110/111 | EXC-IV-004 (`IV-ERR-110`) |
| RG-IV-GW-002-B | Efeito inverso não gera saldo negativo | IV-BR-113, MMS-RG-04 | EXC-IV-004 com indicação de saldo atual |
| RG-IV-GW-002-C | Motivo do estorno obrigatório | IV-BR-112 | EXC-IV-004 (`IV-ERR-112`) |
| RG-IV-GW-002-D | Estornante ≠ regras de SoD aplicáveis a ajustes (ajuste aprovado só é estornado por quem não o aprovou) | IV-BR-085/114 | EXC-IV-006 |

## 9.3 RG-IV-GW-003 — Decisão de Aprovação de Ajuste

| Código | Condição avaliada | Regra de negócio associada | Resultado |
| ------ | ----------------- | -------------------------- | --------- |
| RG-IV-GW-003-A | Aprovador possui escopo `inventory.adjust.approve` | MMS-004-09 | EXC-IV-006 |
| RG-IV-GW-003-B | Aprovador ≠ registrador do ajuste (SoD) | IV-BR-085 | EXC-IV-006 — auditoria obrigatória |
| RG-IV-GW-003-C | Ajuste negativo com saldo suficiente (revalidado na aprovação) | IV-BR-084 | Aprovação bloqueada; retorna ao Pendente com motivo |
| RG-IV-GW-003-D | Rejeição exige motivo | IV-BR-086 | Decisão não registrada sem motivo |
| RG-IV-GW-003-E | Aprovação habilitada por configuração | `materials.inventory.adjustment.approval-required` (padrão true) | Se false, ajuste é aplicado direto com auditoria reforçada (não recomendado; requer mudança de configuração) |

## 9.4 RG-IV-GW-004 — Divergência de Inventário

| Código | Condição avaliada | Regra de negócio associada | Resultado |
| ------ | ----------------- | -------------------------- | --------- |
| RG-IV-GW-004-A | |contado − sistêmico| ≤ tolerância (`count.tolerance`, padrão 0) | IV-BR-101 | Atividade F4 — fecha sem ajuste |
| RG-IV-GW-004-B | Divergência acima da tolerância | IV-BR-101 | Atividade F5 — ajuste vinculado obrigatório |
| RG-IV-GW-004-C | Divergência persistente com ajuste rejeitado | IV-BR-102 | FA-IV-004 — novo ajuste ou justificativa auditada |

## 9.5 RG-IV-GW-005 — Destino da Reserva (Event-Based)

| Código | Evento disparador | Regra de negócio associada | Resultado |
| ------ | ----------------- | -------------------------- | --------- |
| RG-IV-GW-005-A | Documento de saída vinculado confirmado | IV-BR-033/034 | Reserva Atendida (baixa total) ou permanece Ativa (baixa parcial) |
| RG-IV-GW-005-B | Liberação manual ou `RequisitionCancelled` | IV-BR-035, POL-IV-08 | Reserva Liberada |
| RG-IV-GW-005-C | TMR-IV-001 (`expiresAt` atingido) | IV-BR-036, MMS-RG-03 | Reserva Vencida — prevalece sobre qualquer outro caminho |

---

# 10. Indicadores do Processo

- Tempo médio de confirmação de entrada (recebimento → efeito de saldo)
- Tempo médio de atendimento de reserva (criação → atendimento)
- Taxa de reservas vencidas sem atendimento
- Tempo médio de aprovação de ajustes
- Taxa de divergência de inventário e acuracidade (meta ≥ 98% — MMS-004-01)
- Quantidade de estornos por período e por motivo
- Alertas de mínimo/ruptura abertos × normalizados

**Detalhamento:** indicadores derivados dos registros imutáveis de Timeline (BO-IV-009) e Audit (BO-IV-010), incluindo saldo anterior/posterior por linha — sem consulta ao estado mutável dos aggregates. KPIs do módulo em MMS-004-01 §10.

---

# 11. Exceções

Saldo insuficiente · Segregação violada · Referência inválida (item/local/tamanho) · Estorno inviável · Reserva inválida/vencida · Segurança/permissão · Falha técnica de validação ou efeito

## 11.1 Catálogo de Exceções (detalhamento Enterprise)

| Código | Exceção | Origem | Tratamento | Notificação |
| ------ | ------- | ------ | ---------- | ----------- |
| EXC-IV-001 | Saldo disponível insuficiente | GW-IV-001 (RG-IV-GW-001-A) | Documento permanece Rascunho com disponível × solicitado por linha | Almoxarife |
| EXC-IV-002 | Violação de segregação cliente/contrato | GW-IV-001 (RG-IV-GW-001-D) | Bloqueio; auditoria obrigatória; sem contorno | Almoxarife + Gestor (security audit) |
| EXC-IV-003 | Referência inválida (item inativo, local inativo, tamanho ausente) | GW-IV-001 (B/C/F) ou A2 | Documento permanece Rascunho com pendência | Almoxarife |
| EXC-IV-004 | Estorno inviável (não confirmado, já estornado, saldo negativo resultante, sem motivo) | GW-IV-002 | Operação rejeitada com motivo; original preservado | Supervisor |
| EXC-IV-005 | Reserva inexistente, vencida ou já atendida/liberada | GW-IV-001 (E) | Saída não confirmada; orientar nova reserva | Almoxarife |
| EXC-IV-006 | Violação de permissão ou SoD | Qualquer gateway (regra G/B/A) | Bloqueio de segurança; auditoria obrigatória | Administrador (security audit) |
| EXC-IV-007 | Falha técnica de validação ou efeito de saldo (timeout TIME-IV-001) | A2/B2/D2/E2/G2 | Compensação COMP-IV-002: operação reexecutada (até 3 tentativas via idempotência); após 3 falhas, documento permanece no estado anterior com motivo técnico | Almoxarife + Suporte/SRE |

---

# 12. SLA por Etapa

| Etapa | SLA Padrão |
|-------|------------|
| Confirmação de movimentação (validação + efeito) | < 30 segundos |
| Publicação de evento de domínio | Imediata (< 5 min) |
| Atendimento de reserva (criação → atendimento) | Conforme data necessária da solicitação |
| Vida útil da reserva | `reservation.ttl` (padrão 72h) |
| Aprovação de ajuste | 24 horas |
| Tratamento de item inativado com reserva ativa | 5 dias úteis |
| Fechamento de inventário após contagem completa | 2 dias úteis |
| Contagem/registro manual | Livre |

## 12.1 Detalhamento de SLA (Enterprise)

| Etapa | SLA Padrão | Configuração | Medição | Ação em estouro |
| ----- | ---------- | ------------ | ------- | --------------- |
| Confirmação de movimentação | < 30 s | `materials.inventory.validation.timeout.seconds=30` | Entrada da validação → decisão do gateway | EXC-IV-007 / COMP-IV-002 |
| Publicação de evento | < 5 min | `messaging.critical-event.alert.minutes=5` | Confirmação → consumo pelas projeções | ESC-IV-002 (DLQ) |
| Reserva | 72h de vida | `materials.inventory.reservation.ttl.hours=72` | Criação → atendimento/liberação/vencimento | TMR-IV-001 (vencimento automático) |
| Alerta de reserva vencendo | 24h antes | `materials.inventory.expiring-window.hours=24` | TMR-IV-002 | Notificação FD-001-05 |
| Aprovação de ajuste | 24h | `materials.inventory.adjustment.approval.sla.hours=24` | MSG-IV-009 → decisão GW-IV-003 | ESC-IV-001 |
| Item inativado com reserva ativa | 5 dias úteis | `materials.inventory.inactivated-item.sla.days=5` | MSG-IV-C04 → tratamento | Lembrete; ESC-IV-003 |
| Fechamento de inventário | 2 dias úteis | `materials.inventory.count.close.sla.days=2` | Contagem completa → fechamento | Lembrete ao Supervisor |

---

# 13. Pontos de Integração

- **MMS-005 (Receiving)** — origem de entradas (MSG-IV-C01)
- **MMS-003 (Material Requisition)** — origem de reservas (MSG-IV-C02) e cancelamentos (MSG-IV-C03); consumidor de MSG-IV-005/006/007/008
- **MMS-002 (Item Catalog)** — validação de item ativo; MSG-IV-C04; parâmetros de reposição para alertas
- **Notification Engine (FD-001-05)** — alertas e lembretes (DS-IV-003)
- **Timeline (FD-001-07)** — BO-IV-009 por documento
- **Audit (FD-001-06)** — BO-IV-010 com saldo anterior/posterior
- **Master Data (FD-001-09)** — motivos de ajuste, unidades (DS-IV-005)
- **Document Management / MinIO (FD-001-03)** — evidências (DS-IV-006)

**Detalhamento:** contratos de eventos conforme MMS-004-05 §13/§14 (exchange `trino.materials`, outbox, at-least-once, idempotência por (eventId, consumerName), ordenação por aggregateId + chave de saldo).

---

# 14. Artefatos Relacionados

- Business Rules (MMS-004-02)
- State Machine (MMS-004-03)
- Domain Model (MMS-004-04)
- Event Storming (MMS-004-05)
- Use Cases (MMS-004-07 — a produzir)
- API (MMS-004-13 — a produzir)
- Database (MMS-004-11 — a produzir)

---

# 15. Elementos Enterprise do Processo

## 15.1 Escalonamentos

| Código | Escalonamento | Gatilho | Ação | Destino |
| ------ | ------------- | ------- | ---- | ------- |
| ESC-IV-001 | Ajuste pendente além do SLA | TMR-IV-003 (24h) | Notificar Gestor de Suprimentos; registrar em Timeline/Audit | Gestor de Suprimentos |
| ESC-IV-002 | Evento crítico em DLQ (EVT-IV-001/002/003/004) | Falha de mensageria após retry | Alerta operacional imediato; reconciliação via outbox | Operações/SRE |
| ESC-IV-003 | Item inativado com reserva ativa sem tratamento | Estouro de 5 dias úteis | Notificar Supervisor + Gestor; reserva sinalizada na visão do almoxarifado | Supervisor de Almoxarifado |
| ESC-IV-004 | Alerta de ruptura sem normalização em 48h | BO-IV-008 aberto além do prazo | Escalonar ao Gestor de Suprimentos com posição e consumo recente | Gestor de Suprimentos |

## 15.2 Timeouts

| Código | Timeout | Escopo | Valor padrão | Comportamento |
| ------ | ------- | ------ | ------------ | ------------- |
| TIME-IV-001 | Validação + efeito de movimentação | A2/B2/D2/E2/G2 | 30 s | EXC-IV-007 → COMP-IV-002 |
| TIME-IV-002 | Publicação de evento crítico | MSG-IV-001..004 | 5 min | ESC-IV-002 |
| TIME-IV-003 | Consumo de mensagem externa (MSG-IV-C01/C02) | Fluxos A/C | 5 min com retry | DLQ + reconciliação |

## 15.3 Eventos Intermediários de Tempo

| Código | Tipo BPMN | Definição |
| ------ | --------- | --------- |
| TMR-IV-001 | Timer Intermediate Catch (interrupting) sobre Reserva Ativa | `expiresAt` atingido → Atividade C3 (vencimento); cancela os caminhos de atendimento/liberação |
| TMR-IV-002 | Timer Intermediate Catch (non-interrupting) sobre Reserva Ativa | `expiring-window` (24h antes do vencimento) → alerta "reserva vencendo" via FD-001-05; fluxo não interrompido |
| TMR-IV-003 | Timer Intermediate Catch (non-interrupting) sobre Ajuste Pendente | SLA de aprovação (24h) → ESC-IV-001; fluxo não interrompido |
| TMR-IV-004 | Timer Start (recorrente) | Verificação diária de reservas a vencer e alertas pendentes (rotina do Sistema) |

## 15.4 Compensações

| Código | Compensação | Quando | Ação compensatória |
| ------ | ----------- | ------ | ------------------ |
| COMP-IV-001 | Estorno de movimentação confirmada | Erro operacional detectado após confirmação | Fluxo G completo — novo documento com efeito inverso, motivo obrigatório, original → Estornado (MMS-P-08) |
| COMP-IV-002 | Falha técnica na validação/efeito | EXC-IV-007 | Reexecução idempotente (máx. 3 tentativas); documento permanece no estado anterior; Timeline registra tentativa |
| COMP-IV-003 | Falha na publicação de evento | TIME-IV-002 | Republicação via outbox relay; se persistir, DLQ + reconciliação manual documentada |
| COMP-IV-004 | Projeção/cache de saldo divergente | Detecção por reconciliação (IV-BR-096) | Reconstrução da projeção a partir dos documentos confirmados (fonte de verdade); auditoria da divergência |

---

# 16. Sub-Processos (detalhamento)

## 16.1 SP-IV-01 — Validação e Confirmação de Movimentação

```text
┌─────────────────────────────────────────────────────────────┐
│ Sub-Process: Validação e Confirmação (Atividades A2/B2/D2)  │
│                                                             │
│  Entrada: StockMovement em Rascunho                         │
│      │                                                      │
│      ▼                                                      │
│  Bateria de guards:                                         │
│    - Item ativo (IV-BR-010)                                 │
│    - Quantidade > 0 (IV-BR-011)                             │
│    - Locais ativos e da empresa (IV-BR-060..062)            │
│    - Tamanho quando grade (IV-BR-120/121)                   │
│    - Idempotência da origem (IV-BR-090)                     │
│      │                                                      │
│  Guards de efeito (saída/transferência):                    │
│    - Saldo disponível (IV-BR-020 / MMS-RG-04)               │
│    - Segregação (IV-BR-070 / MMS-RG-10)                     │
│    - Reserva ativa quando vinculada (IV-BR-033)             │
│      │                                                      │
│  Efeito TRANSACIONAL (INV-IV-04):                           │
│    - Documento → Confirmado                                 │
│    - StockBalanceService aplica efeito por linha            │
│      (saldo anterior/posterior registrado — IV-BR-095)      │
│      │                                                      │
│  Resultado:                                                 │
│    - OK     → Evento de domínio correspondente              │
│    - Falha  → Documento permanece Rascunho (FA-IV-001/002)  │
│                                                             │
│  Timeout: TIME-IV-001 (30 s) → EXC-IV-007 → COMP-IV-002     │
└─────────────────────────────────────────────────────────────┘
```

Nunca há efeito parcial: ou todas as linhas são aplicadas, ou nenhuma (INV-IV-04). Cada tentativa gera registro em BO-IV-009 (Timeline) e BO-IV-010 (Audit), conforme IV-BR-095.

## 16.2 SP-IV-02 — Apuração e Fechamento de Inventário

```text
┌─────────────────────────────────────────────────────────────┐
│ Sub-Process: Apuração de Inventário (Atividades F3..F5)     │
│                                                             │
│  Entrada: InventoryCount Em Contagem                        │
│      │                                                      │
│      ▼                                                      │
│  Apuração por chave de saldo:                               │
│    contado (CountEntry) × sistêmico (StockBalance)          │
│      │                                                      │
│  GW-IV-004: |divergência| > tolerância (padrão 0)?          │
│    - NÃO → Fechamento direto (F4)                           │
│    - SIM → Gera Adjustment vinculado por divergência (F5)   │
│            → Call Activity: Fluxo E (aprovação obrigatória) │
│            → Inventário fecha somente após ajustes          │
│              concluídos (aprovados ou rejeitados com        │
│              justificativa — FA-IV-004)                     │
│      │                                                      │
│  Saída: InventoryCountClosed (MSG-IV-014) + acuracidade     │
│         registrada para o KPI ≥ 98%                         │
└─────────────────────────────────────────────────────────────┘
```

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: 7 fluxos principais (entrada, saída/atendimento, reserva com vencimento por timer, transferência, ajuste com aprovação, inventário com ajuste de divergências, estorno como compensação), 5 gateways com regras detalhadas (RG-IV-GW-001..005) rastreadas às IV-BR, 6 fluxos alternativos, 10 Business Objects, 6 Data Stores, 20 Message Events (16 publicados + 4 consumidos) mapeados aos EVT-IV, 7 exceções, SLA detalhado, 4 escalonamentos, 3 timeouts, 4 timers, 4 compensações e 2 sub-processos detalhados (confirmação de movimentação e apuração de inventário). |
