# MMS-004-10 — Notifications

**Documento:** MMS-004-10 — Notifications
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-23
**Dependências:** MMS-004 v1.0.0, MMS-004-05 (Event Storming), MMS-004-06 (BPMN — timers, escalonamentos e exceções), MMS-004-07 (Use Cases), MMS-004-09 (Permissions), FD-001-05 (Notification Service), FD-001-06 (Audit)
**Referências:** MMS-002-10 (padrão de formato Enterprise), PR-001-10, GOV-001

> Este documento define os eventos do módulo Inventory Management que geram notificações e como essas notificações devem ser consumidas pelo Notification Center da plataforma Trino Supply.

---

# 1. Objetivo

Garantir que os responsáveis pela operação de estoque sejam informados nos momentos em que a integridade e a continuidade da operação exigem ação — ruptura, estoque abaixo do mínimo, reserva vencendo/vencida, ajuste pendente de decisão, inventário aberto e bloqueios de segurança — utilizando notificações relevantes, configuráveis e rastreáveis.

O Inventory Management é um módulo transacional de alto volume: **movimentações bem-sucedidas não geram notificação** — seu feedback é dado na própria tela (MMS-004-07, mensagens MSG-IV-UC) e nos eventos de domínio consumidos pelos módulos integrados. Notifica-se **ação necessária**, nunca rotina (IV-BR-092: alertas nunca bloqueiam a movimentação principal).

---

# 2. Princípios

As notificações devem obedecer aos seguintes princípios:

- Baseadas em eventos de negócio.
- Configuráveis por organização.
- Configuráveis por usuário.
- Não bloquear o fluxo principal (IV-BR-092).
- Totalmente auditáveis.
- Reprocessáveis em caso de falha.
- Parcimônia: somente eventos que exigem ação de alguém geram notificação — confirmações rotineiras de entrada, saída, transferência e contagem não notificam.

---

# 3. Canais Suportados

## MVP

- Notificação dentro do sistema (Notification Center)
- E-mail

## Roadmap

- Microsoft Teams
- Slack
- Push Mobile (prioridade para almoxarifado em campo)
- Webhooks

---

# 4. Eventos que Geram Notificações

| Evento | Origem | Destinatário | Obrigatória |
|---------|--------|--------------|-------------|
| StockoutAlerted (ruptura) | EVT-IV-016 + POL-IV-10 | Gestor de Suprimentos + responsável pelo ressuprimento | Sim |
| StockMinimumAlerted (mínimo) | EVT-IV-015 + POL-IV-09 | Responsável pelo ressuprimento (`alerts.recipients.*`) | Sim |
| ReservationExpiring (reserva vencendo) | TMR-IV-002 (24h antes, `expiring-window`) | Almoxarife responsável + solicitante (via MMS-003) | Sim |
| ReservationExpired (reserva vencida) | EVT-IV-008 + POL-IV-05 | Almoxarifado + solicitante (via MMS-003) | Sim |
| AdjustmentRegistered (ajuste pendente) | EVT-IV-009 | Aprovadores (IV-PERM-005) do escopo | Sim |
| AdjustmentApproved / AdjustmentRejected | EVT-IV-010 / EVT-IV-011 | Registrador do ajuste | Sim |
| InventoryCountStarted (inventário aberto) | EVT-IV-012 | Almoxarifado do escopo | Sim |
| ItemInactivatedWithActiveReservations | MSG-IV-C04 + FA-IV-003 | Almoxarifado (reservas ativas do item) | Sim |
| AdjustmentApprovalOverdue | ESC-IV-001 (TMR-IV-003, SLA 24h) | Gestor de Suprimentos | Sim |
| StockoutNotNormalized | ESC-IV-004 (ruptura aberta > 48h) | Gestor de Suprimentos | Sim |
| InactivatedItemReservationsOverdue | ESC-IV-003 (5 dias úteis) | Supervisor + Gestor | Sim |
| SecurityBlockOnInventory | EXC-IV-002 / EXC-IV-006 (segregação, SoD, permissão) | Administrador (security audit) | Sim |
| CriticalEventInDLQ | ESC-IV-002 (EVT-IV-001..004 em DLQ) | Operações/SRE | Sim |
| AlertNormalized (normalização de alerta) | Entrada de saldo que normaliza BO-IV-008 | Destinatários do alerta original | Configurável |

**Alerta aberto (BO-IV-008):** não há duplicidade de notificação enquanto o alerta da mesma chave de saldo permanecer Aberto (IV-BR-091 — deduplicação).

---

# 5. Tipos de Notificação

## Ação Necessária

Exemplo:

"RUPTURA: o item CAP-001 (Capacete de segurança) está sem saldo no Depósito Central e possui 3 solicitações em aberto."

"Ajuste AJ-2026-0042 aguarda sua aprovação (motivo: divergência de inventário)."

"A reserva RS-2026-0815 vence em 24h. Providencie o atendimento ou a liberação."

## Alerta

Exemplo:

"O item LUV-013 atingiu o estoque mínimo no Depósito Central (disponível: 8, mínimo: 10)."

"O inventário INV-2026-007 foi aberto para o seu almoxarifado. Prazo de contagem: 26/08."

## Erro / Segurança

Exemplo:

"Tentativa bloqueada de aprovação do próprio ajuste (usuário X, ajuste Y) — SoD IV-BR-085."

"Evento crítico StockIssueRegistered em DLQ há mais de 5 minutos. Reconciliação necessária."

---

# 6. Templates

Cada tipo de notificação utilizará um template versionado.

| Template | Assunto | Variáveis |
|----------|---------|-----------|
| IV-NOT-001 | Ruptura de estoque | Item (código, descrição), local, demanda aberta (nº de solicitações e quantidades), link para posição |
| IV-NOT-002 | Estoque abaixo do mínimo | Item, local, disponível, mínimo, déficit, link para posição |
| IV-NOT-003 | Reserva vencendo | Nº da reserva, item, quantidade, local, solicitação de origem, `expiresAt`, link para a fila |
| IV-NOT-004 | Reserva vencida e liberada | Nº da reserva, item, quantidade devolvida ao disponível, solicitação de origem reaberta |
| IV-NOT-005 | Ajuste pendente de aprovação | Nº do ajuste, tipo (positivo/negativo), motivo, itens/quantidades, registrador, link para a fila de aprovação |
| IV-NOT-006 | Ajuste aprovado / rejeitado | Nº do ajuste, decisão, decisor, motivo da rejeição (quando houver), saldo afetado |
| IV-NOT-007 | Inventário aberto | Nº do inventário, escopo (locais/classes), responsável, prazo, nº de chaves de saldo a contar |
| IV-NOT-008 | Item inativado com reservas ativas | Item, nº de reservas ativas, prazo de tratamento (5 dias úteis), link para a fila filtrada |
| IV-NOT-009 | Escalonamento — ajuste além do SLA | Idem IV-NOT-005 + horas em aberto |
| IV-NOT-010 | Escalonamento — ruptura sem normalização | Idem IV-NOT-001 + horas em aberto + consumo recente |
| IV-NOT-011 | Bloqueio de segurança no estoque | Usuário, ação tentada, recurso (documento/ajuste), regra violada (SoD/segregação/permissão), timestamp |
| IV-NOT-012 | Evento crítico em DLQ | Evento, aggregateId, fila, tentativas, motivo da última falha |
| IV-NOT-013 | Alerta normalizado | Item, local, saldo restabelecido, alerta de origem |

---

# 7. Preferências do Usuário

Cada usuário poderá definir:

Receber por:

- Sistema
- E-mail
- Ambos

Horário permitido

Idioma

Agrupamento

Resumo diário (futuro)

---

# 8. Escalonamento

Cadeias de escalonamento do módulo (MMS-004-06 §15.1), todas parametrizáveis:

1. **Ajuste pendente:** lembrete ao aprovador em 50% do SLA; estouro do SLA (`adjustment.approval.sla.hours`, padrão 24h — TMR-IV-003) → Gestor de Suprimentos (ESC-IV-001 / IV-NOT-009).
2. **Ruptura:** alerta imediato (IV-NOT-001); sem normalização em 48h → Gestor de Suprimentos com posição e consumo recente (ESC-IV-004 / IV-NOT-010).
3. **Item inativado com reservas ativas:** notificação imediata (IV-NOT-008); sem tratamento em 5 dias úteis (`inactivated-item.sla.days`) → Supervisor + Gestor (ESC-IV-003).
4. **Evento crítico em DLQ:** alerta operacional imediato a Operações/SRE (ESC-IV-002 / IV-NOT-012).

---

# 9. Consolidação

O Notification Center poderá agrupar eventos semelhantes.

Exemplo:

Em vez de:

- 15 notificações de estoque mínimo (uma por item) após um atendimento em lote

Exibir:

"15 itens do Depósito Central ficaram abaixo do mínimo após os atendimentos de hoje."

A consolidação nunca se aplica a ruptura (IV-NOT-001) nem a bloqueios de segurança (IV-NOT-011) — cada ocorrência é individual.

---

# 10. Auditoria

Cada notificação deverá registrar:

- Identificador
- Evento de origem
- Destinatário
- Canal
- Template utilizado
- Data de envio
- Data de entrega
- Data de leitura (quando aplicável)
- Status

---

# 11. Indicadores

O módulo deverá permitir medir:

- Notificações enviadas por tipo
- Taxa de entrega
- Taxa de leitura
- Tempo médio entre alerta de ruptura e normalização
- Tempo médio entre alerta de reserva vencendo e atendimento/liberação
- Tempo médio de aprovação de ajuste após notificação
- Notificações escaladas

---

# 12. Regras de Negócio

- Toda notificação deve estar vinculada a um evento de negócio ou timer do processo.
- O envio não pode bloquear a execução do processo principal (IV-BR-092).
- Falhas de envio devem permitir reprocessamento.
- Templates devem ser versionados.
- O conteúdo deve respeitar o idioma do usuário.
- Movimentações bem-sucedidas (entrada, saída, transferência, contagem) não geram notificação — feedback em tela.
- Enquanto um alerta (BO-IV-008) da mesma chave de saldo estiver Aberto, novos cruzamentos do limiar não geram nova notificação (IV-BR-091).

---

# 13. Dependências

- Notification Center (FD-001-05)
- Event Bus (exchange `trino.materials` — ADR-010)
- Audit Service (FD-001-06)
- Identity & Access Management (FD-001-01 — resolução de destinatários por papel/escopo)
- Configuration (FD-001-10 — `materials.inventory.alerts.recipients.*`, SLAs e janelas)
- MMS-003 Material Requisition (notificação ao solicitante via contexto da solicitação)

---

# 14. Especificação Operacional (Enterprise)

## 14.1 Queue e Processamento Assíncrono

| Aspecto | Definição |
| ------- | --------- |
| **Origem** | Notification Service consome eventos do exchange `trino.materials` (RabbitMQ) conforme MMS-004-05 §14, além dos timers do processo (TMR-IV-002/003/004) e exceções de segurança roteadas pelo Audit |
| **Filas** | `trino.notifications.high`, `trino.notifications.normal`, `trino.notifications.low` — filas compartilhadas da plataforma, uma por classe de prioridade (§14.2) |
| **Workers** | Pool de consumidores com prefetch configurável (`notification.worker.prefetch`, padrão 10); escalonamento horizontal por fila |
| **Não bloqueio** | A publicação do evento de negócio nunca aguarda o envio da notificação (outbox + consumo assíncrono); falha de notificação nunca afeta confirmação de movimentação, reserva, ajuste ou inventário (IV-BR-092) |
| **Idempotência** | Deduplicação por `(eventId, recipientId, channel)` — reentrega do evento não gera notificação duplicada; alertas deduplicados adicionalmente por chave de saldo enquanto Abertos (IV-BR-091) |

## 14.2 Prioridade

| Classe | Notificações | SLA de despacho | Fila |
| ------ | ------------ | --------------- | ---- |
| **HIGH** | Ruptura (IV-NOT-001), Reserva vencida (IV-NOT-004), Bloqueio de segurança (IV-NOT-011), Evento crítico em DLQ (IV-NOT-012), Escalonamentos (IV-NOT-009/010) | ≤ 1 min | `trino.notifications.high` |
| **NORMAL** | Estoque mínimo (IV-NOT-002), Reserva vencendo (IV-NOT-003), Ajuste pendente/decidido (IV-NOT-005/006), Inventário aberto (IV-NOT-007), Item inativado com reservas (IV-NOT-008) | ≤ 5 min | `trino.notifications.normal` |
| **LOW** | Normalização de alerta (IV-NOT-013), consolidações e resumos | ≤ 30 min | `trino.notifications.low` |

Configuração por organização: `notification.priority.overrides` permite reclassificar tipos específicos (nunca rebaixar HIGH).

## 14.3 Retry

| Aspecto | Definição |
| ------- | --------- |
| **Tentativas** | 5 tentativas com backoff exponencial e jitter: 30s → 2min → 8min → 32min → 2h (`notification.retry.*`) |
| **Erros transitórios** | Falha de SMTP/provider, timeout, 5xx do canal → retry |
| **Erros definitivos** | Destinatário sem e-mail válido, opt-out, template inexistente → sem retry; status `Failed` + auditoria; alerta operacional se sistemático |
| **Dead Letter** | Após esgotar tentativas: mensagem para `trino.notifications.dlq` com `x-failure-reason`; reprocessamento manual via console operacional |

## 14.4 Template Version

| Aspecto | Definição |
| ------- | --------- |
| **Identificação** | `IV-NOT-xxx` + `version` (semver) — ex.: `IV-NOT-001 v1.0.0` |
| **Imutabilidade** | Versão publicada é imutável; alterações geram nova versão |
| **Pinagem** | A notificação registra a versão exata do template usada no envio (auditoria, Seção 10) |
| **Fallback** | Se a versão configurada não existir para o idioma do usuário: fallback para o idioma padrão da organização; se inexistente, `pt-BR`; sempre registrado |
| **Governança** | Criação/alteração de templates por Administrador, com aprovação registrada; templates nunca contêm lógica executável (renderização segura) |

## 14.5 Localization

| Aspecto | Definição |
| ------- | --------- |
| **Idiomas MVP** | `pt-BR` (padrão), `en-US` |
| **Resolução** | Preferência do usuário → padrão da organização → `pt-BR` |
| **Conteúdo localizado** | Assunto, corpo, rótulos de ação e formatos de data/número/quantidade |
| **Chaves i18n** | `iv.not.*` (ex.: `iv.not.stockout.subject`); templates referenciam chaves, nunca texto fixo |
| **Fuso horário** | Datas exibidas no fuso do usuário; armazenamento sempre UTC |

## 14.6 Scheduling

| Aspecto | Definição |
| ------- | --------- |
| **Imediatas** | Notificações HIGH/NORMAL despachadas assim que o evento é consumido |
| **Horário permitido** | Preferência do usuário (Seção 7): notificações LOW fora do horário permitido são agendadas para o próximo horário válido (`notification.schedule.respect-user-hours`, padrão `true`; HIGH sempre imediata) |
| **Lembretes de timers** | Agendados pelos timers do processo: TMR-IV-002 (reserva vencendo, `expiring-window` 24h), TMR-IV-003 (SLA de ajuste), TMR-IV-004 (verificação diária de reservas e alertas pendentes) |
| **Resumo diário (roadmap)** | Job agregador por usuário, horário configurável |

## 14.7 Escalonamento (detalhamento)

Complementando a Seção 8:

| Etapa | Gatilho | Destinatário | Configuração |
| ----- | ------- | ------------ | ------------ |
| 1. Lembrete de ajuste | 50% do SLA de aprovação | Aprovadores do escopo | `adjustment.approval.sla.hours` |
| 2. Estouro de ajuste (ESC-IV-001) | 100% do SLA (TMR-IV-003) | Gestor de Suprimentos | IV-NOT-009 |
| 3. Ruptura persistente (ESC-IV-004) | Alerta de ruptura Aberto > 48h | Gestor de Suprimentos | IV-NOT-010 |
| 4. Item inativado sem tratamento (ESC-IV-003) | 5 dias úteis (`inactivated-item.sla.days`) | Supervisor + Gestor | IV-NOT-008 (reenvio escalado) |
| 5. Sistêmico (ESC-IV-002) | Evento crítico em DLQ; falhas de entrega em massa | Time de Operações/SRE | IV-NOT-012 |

Cada etapa gera notificação própria, auditada, com referência à etapa anterior (cadeia de escalonamento visível na Timeline).

## 14.8 Preferências (detalhamento)

Complementando a Seção 7:

| Preferência | Valores | Padrão | Observação |
| ----------- | ------- | ------ | ---------- |
| Canal | `system`, `email`, `both` | `both` | Notificações **obrigatórias** (Seção 4) ignoram opt-out de e-mail crítico; opt-out vale apenas para configuráveis |
| Horário permitido | Janela diária (ex.: 07h–20h) + fuso | 07h–20h local | Aplica-se a LOW (§14.6) |
| Idioma | `pt-BR`, `en-US` | `pt-BR` | §14.5 |
| Agrupamento | `on`, `off` | `on` | Consolidação (Seção 9); nunca para ruptura/segurança |
| Resumo diário | `on`, `off` | `off` | Roadmap |

Precedência: configuração da organização define os limites; preferência do usuário opera dentro desses limites. Toda alteração de preferência é auditada.

## 14.9 Regras de Envio (detalhamento)

Complementando a Seção 12:

| Código | Regra |
| ------ | ----- |
| NOT-IV-BR-001 | Toda notificação vinculada a um evento de negócio ou timer (eventId/timerId obrigatório) |
| NOT-IV-BR-002 | Envio assíncrono; nunca bloqueia o fluxo principal (IV-BR-092) |
| NOT-IV-BR-003 | Idempotência por `(eventId, recipientId, channel)` |
| NOT-IV-BR-004 | Notificações obrigatórias não admitem opt-out de canal crítico |
| NOT-IV-BR-005 | Destinatários sempre resolvidos no momento do despacho (papel/escopo vigente, incluindo delegações DEL-IV-005) |
| NOT-IV-BR-006 | Autor da ação não recebe notificação da própria ação (ex.: registrador do ajuste não é notificado do registro; é notificado da decisão) |
| NOT-IV-BR-007 | Destinatário fora do escopo organizacional (empresa/almoxarifado) do recurso nunca é notificado |
| NOT-IV-BR-008 | Dados de consumo por colaborador nunca aparecem no corpo da notificação (LGPD — apenas link autenticado, recorte ABAC-IV-04 aplicado na tela) |
| NOT-IV-BR-009 | Falha de template/idioma usa cadeia de fallback (§14.4) e registra o fallback usado |
| NOT-IV-BR-010 | Reprocessamento de DLQ exige ação operacional autenticada e auditada |
| NOT-IV-BR-011 | Movimentações bem-sucedidas e lançamentos de contagem nunca geram notificação (parcimônia) |
| NOT-IV-BR-012 | Alerta da mesma chave de saldo não é renotificado enquanto Aberto; a normalização fecha o ciclo e habilita novo alerta (IV-BR-091) |
| NOT-IV-BR-013 | Notificação ao solicitante (reserva vencendo/vencida) é entregue pelo contexto do MMS-003, nunca com dados operacionais do almoxarifado (IV-BR-097) |

## 14.10 Auditoria de Entrega (detalhamento)

Complementando a Seção 10 — ciclo de vida auditado de cada notificação:

```text
Created → Queued → Dispatched → Delivered → Read
                     │
                     └→ Retrying (n tentativas) → Failed → DLQ → Reprocessed
```

| Campo auditado | Descrição |
| -------------- | --------- |
| notificationId | Identificador único |
| eventId / correlationId | Rastreio até o evento de negócio, o timer ou o comando original |
| recipientId / channel | Destinatário e canal efetivo |
| templateId + templateVersion + locale | Pinagem completa do conteúdo (§14.4) |
| priority / queue | Classe e fila utilizadas |
| scheduledFor / sentAt / deliveredAt / readAt | Timestamps UTC de cada transição |
| attemptCount / lastError | Histórico de retry e motivo da última falha |
| status final | `Delivered`, `Read`, `Failed`, `Suppressed` (opt-out), `Reprocessed` |
| escalatedFrom | Referência à notificação de etapa anterior (escalonamento) |

Retenção: registros de entrega por 5 anos (política de retenção corporativa), em storage append-only. Consulta de entrega disponível ao Administrador e ao Auditor (IV-PERM-009 com escopo de auditoria).

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-08-23 | Arquiteto Principal | Versão inicial aprovada: 14 eventos notificáveis (parcimônia — movimentações bem-sucedidas não notificam), 13 templates IV-NOT, cadeias de escalonamento (ajuste, ruptura, item inativado, DLQ), consolidação com exceções (ruptura/segurança sempre individuais), especificação operacional completa (queues, 3 classes de prioridade, retry 5× exponencial, template version, localization pt-BR/en-US, scheduling por timers TMR-IV, preferências, regras de envio NOT-IV-BR-001..013 e auditoria de entrega com ciclo de vida completo). |
