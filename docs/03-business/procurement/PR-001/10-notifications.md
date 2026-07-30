**Documento:** PR-001-10 — Notifications
**Versão:** 1.1.0
**Status:** Approved

# PR-001-10 — Notifications

> Este documento define os eventos do módulo Purchase Requisition que geram notificações e como essas notificações devem ser consumidas pelo Notification Center da plataforma Trino Supply.

---

# 1. Objetivo

Garantir que todos os participantes do processo sejam informados no momento adequado, utilizando notificações relevantes, configuráveis e rastreáveis.

O objetivo é reduzir atrasos, evitar perda de prazos e aumentar a visibilidade do processo.

---

# 2. Princípios

As notificações devem obedecer aos seguintes princípios:

- Baseadas em eventos de negócio.
- Configuráveis por organização.
- Configuráveis por usuário.
- Não bloquear o fluxo principal.
- Totalmente auditáveis.
- Reprocessáveis em caso de falha.

---

# 3. Canais Suportados

## MVP

- Notificação dentro do sistema (Notification Center)
- E-mail

## Roadmap

- Microsoft Teams
- Slack
- Push Mobile
- WhatsApp Business (integração)
- Webhooks

---

# 4. Eventos que Geram Notificações

| Evento | Destinatário | Obrigatória |
|---------|--------------|-------------|
| PurchaseRequisitionSubmitted | Aprovadores | Sim |
| PurchaseRequisitionApproved | Solicitante | Sim |
| PurchaseRequisitionRejected | Solicitante | Sim |
| PurchaseRequisitionReturned | Solicitante | Sim |
| PurchaseRequisitionCancelled | Participantes | Configurável |
| CommentAdded | Participantes | Configurável |
| ApprovalDelegated | Novo aprovador | Sim |
| SLAExpired | Gestor | Sim |

---

# 5. Tipos de Notificação

## Informação

Exemplo:

"Sua requisição foi aprovada."

---

## Ação Necessária

Exemplo:

"Você possui uma aprovação pendente."

---

## Alerta

Exemplo:

"O SLA da requisição foi excedido."

---

## Erro

Exemplo:

"Falha ao localizar workflow."

---

# 6. Templates

Cada tipo de notificação utilizará um template versionado.

Exemplo:

Template:

PR-NOT-001

Assunto:

Nova Solicitação para Aprovação

Variáveis:

- Número da requisição
- Empresa
- Solicitante
- Prioridade
- Data de necessidade

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

Caso uma aprovação permaneça pendente além do SLA:

1. Relembrar o aprovador.
2. Notificar o gestor do aprovador.
3. Escalar conforme política da organização.

Todas as etapas devem ser parametrizáveis.

---

# 9. Consolidação

O Notification Center poderá agrupar eventos semelhantes.

Exemplo:

Em vez de:

- 15 notificações

Exibir:

"Você possui 15 aprovações pendentes."

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

- Notificações enviadas
- Taxa de entrega
- Taxa de leitura
- Tempo médio até leitura
- Aprovações realizadas após notificação
- Notificações escaladas

---

# 12. Regras de Negócio

- Toda notificação deve estar vinculada a um evento de negócio.
- O envio não pode bloquear a execução do processo principal.
- Falhas de envio devem permitir reprocessamento.
- Templates devem ser versionados.
- O conteúdo deve respeitar o idioma do usuário.

---

# 13. Dependências

- Notification Center
- Event Bus
- Workflow Engine
- Audit Service
- Identity & Access Management

---

# 14. Especificação Operacional (Enterprise)

## 14.1 Queue e Processamento Assíncrono

| Aspecto | Definição |
| ------- | --------- |
| **Origem** | Notification Service consome eventos do exchange `trino.procurement` (RabbitMQ) conforme PR-001-05 §14 |
| **Filas** | `trino.notifications.high`, `trino.notifications.normal`, `trino.notifications.low` — uma fila por classe de prioridade (§14.2) |
| **Workers** | Pool de consumidores com prefetch configurável (`notification.worker.prefetch`, padrão 10); escalonamento horizontal por fila |
| **Não bloqueio** | A publicação do evento de negócio nunca aguarda o envio da notificação (outbox + consumo assíncrono); falha de notificação nunca afeta o fluxo da requisição |
| **Idempotência** | Deduplicação por `(eventId, recipientId, channel)` — reentrega do evento não gera notificação duplicada |

## 14.2 Prioridade

| Classe | Notificações | SLA de despacho | Fila |
| ------ | ------------ | --------------- | ---- |
| **HIGH** | Ação necessária (aprovação pendente), Alerta (SLAExpired), Erro (workflow não localizado) | ≤ 1 min | `trino.notifications.high` |
| **NORMAL** | Informação de decisão (aprovada, rejeitada, retornada), ApprovalDelegated, cancelamento | ≤ 5 min | `trino.notifications.normal` |
| **LOW** | CommentAdded, consolidações e resumos | ≤ 30 min | `trino.notifications.low` |

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
| **Identificação** | `PR-NOT-xxx` + `version` (semver) — ex.: `PR-NOT-001 v2.1.0` |
| **Imutabilidade** | Versão publicada é imutável; alterações geram nova versão |
| **Pinagem** | A notificação registra a versão exata do template usada no envio (auditoria, Seção 10) |
| **Fallback** | Se a versão configurada não existir para o idioma do usuário: fallback para o idioma padrão da organização; se inexistente, `pt-BR`; sempre registrado |
| **Governança** | Criação/alteração de templates por Administrador, com aprovação registrada; templates nunca contêm lógica executável (renderização segura) |

## 14.5 Localization

| Aspecto | Definição |
| ------- | --------- |
| **Idiomas MVP** | `pt-BR` (padrão), `en-US` |
| **Resolução** | Preferência do usuário → padrão da organização → `pt-BR` |
| **Conteúdo localizado** | Assunto, corpo, rótulos de ação e formatos de data/número/moeda (ex.: data necessária em `dd/MM/yyyy` para pt-BR) |
| **Chaves i18n** | `pr.not.*` (ex.: `pr.not.submitted.subject`); templates referenciam chaves, nunca texto fixo |
| **Fuso horário** | Datas exibidas no fuso do usuário; armazenamento sempre UTC |

## 14.6 Scheduling

| Aspecto | Definição |
| ------- | --------- |
| **Imediatas** | Notificações HIGH/NORMAL despachadas assim que o evento é consumido |
| **Horário permitido** | Preferência do usuário (Seção 7): notificações LOW fora do horário permitido são agendadas para o próximo horário válido (`notification.schedule.respect-user-hours`, padrão `true`; HIGH sempre imediata) |
| **Lembretes de SLA** | Agendados pelo timer do workflow (TMR-001/MSG-008 em PR-001-06): lembrete em 50% do SLA de aprovação |
| **Resumo diário (roadmap)** | Job agregador por usuário, horário configurável; consolida pendências e atualizações |

## 14.7 Escalonamento (detalhamento)

Complementando a Seção 8:

| Etapa | Gatilho | Destinatário | Configuração |
| ----- | ------- | ------------ | ------------ |
| 1. Lembrete | 50% do SLA do nível | Aprovador pendente | `approval.sla.reminder.pct=50` |
| 2. Estouro | 100% do SLA (SLAExpired) | Aprovador + gestor imediato | `approval.sla.hours` |
| 3. Escalonamento | Segundo estouro | Gestor do aprovador assume ou redireciona o nível | `approval.escalation.second.hours` |
| 4. Sistêmico | Falhas de entrega em massa ou DLQ | Time de Operações/SRE | Alerta operacional |

Cada etapa gera notificação própria, auditada, com referência à etapa anterior (cadeia de escalonamento visível na Timeline).

## 14.8 Preferências (detalhamento)

Complementando a Seção 7:

| Preferência | Valores | Padrão | Observação |
| ----------- | ------- | ------ | ---------- |
| Canal | `system`, `email`, `both` | `both` | Notificações **obrigatórias** (Seção 4) ignoram opt-out de e-mail crítico; opt-out vale apenas para configuráveis |
| Horário permitido | Janela diária (ex.: 07h–20h) + fuso | 07h–20h local | Aplica-se a LOW (§14.6) |
| Idioma | `pt-BR`, `en-US` | `pt-BR` | §14.5 |
| Agrupamento | `on`, `off` | `on` | Consolidação (Seção 9) |
| Resumo diário | `on`, `off` | `off` | Roadmap |

Precedência: configuração da organização define os limites; preferência do usuário opera dentro desses limites. Toda alteração de preferência é auditada.

## 14.9 Regras de Envio (detalhamento)

Complementando a Seção 12:

| Código | Regra |
| ------ | ----- |
| NOT-BR-001 | Toda notificação vinculada a um evento de negócio (eventId obrigatório) |
| NOT-BR-002 | Envio assíncrono; nunca bloqueia o fluxo principal |
| NOT-BR-003 | Idempotência por `(eventId, recipientId, channel)` |
| NOT-BR-004 | Notificações obrigatórias não admitem opt-out de canal crítico |
| NOT-BR-005 | Destinatários sempre resolvidos no momento do despacho (nunca snapshot antigo de papel/escopo) |
| NOT-BR-006 | Autor da ação não recebe notificação da própria ação (ex.: autor de comentário) |
| NOT-BR-007 | Destinatário fora do escopo da requisição nunca é notificado (isolamento organizacional) |
| NOT-BR-008 | Conteúdo nunca inclui dados de anexos; apenas link autenticado para a requisição |
| NOT-BR-009 | Falha de template/idioma usa cadeia de fallback (§14.4) e registra o fallback usado |
| NOT-BR-010 | Reprocessamento de DLQ exige ação operacional autenticada e auditada |

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
| eventId / correlationId | Rastreio até o evento de negócio e o comando original |
| recipientId / channel | Destinatário e canal efetivo |
| templateId + templateVersion + locale | Pinagem completa do conteúdo (§14.4) |
| priority / queue | Classe e fila utilizadas |
| scheduledFor / sentAt / deliveredAt / readAt | Timestamps UTC de cada transição |
| attemptCount / lastError | Histórico de retry e motivo da última falha |
| status final | `Delivered`, `Read`, `Failed`, `Suppressed` (opt-out), `Reprocessed` |
| escalatedFrom | Referência à notificação de etapa anterior (escalonamento) |

Retenção: registros de entrega por 5 anos (política de retenção corporativa), em storage append-only. Consulta de entrega disponível ao Administrador e ao Auditor (PR-PERM-013).

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | — | Arquitetura Trino | Versão inicial aprovada |
| 1.1.0 | 2026-07-30 | Arquitetura Trino | Adicionada Seção 14 — Especificação Operacional: queue e processamento assíncrono, prioridade, retry, template version, localization, scheduling, detalhamento de escalonamento, preferências, regras de envio (NOT-BR-001..010) e auditoria de entrega com ciclo de vida completo. Todo o conteúdo das Seções 1–13 foi preservado |
