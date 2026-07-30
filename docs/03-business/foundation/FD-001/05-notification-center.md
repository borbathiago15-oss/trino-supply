# FD-001-05 — Notification Center

| Campo | Valor |
|---|---|
| **Documento** | FD-001-05 |
| **Módulo** | Foundation — Notification Center (FD-BC-005) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-07-30 |
| **Dependências** | FD-001 (Foundation Overview), FD-001-01 (IAM), FD-001-02 (Organization), ADR-010 (Mensageria — RabbitMQ), ADR-011 (Padrão de Documentação Foundation) |
| **Referências de negócio** | PR-001-10 Notifications v1.1.0, PR-001-05 §14.1 (Mensageria Transversal), FD-001-04 Workflow Engine (eventos WF-EVT-xxx) |

---

## 1. Objetivo

O Notification Center é o domínio do Foundation responsável por **entregar notificações aos usuários** a partir de eventos publicados por qualquer módulo do Trino Supply. Ele é o único ponto de despacho de notificações do sistema: nenhum módulo de negócio envia notificação diretamente.

O Notification Center:

1. **Consome** eventos de negócio publicados pelos módulos (ex.: Workflow Engine, Purchase Requisition);
2. **Resolve destinatários** no momento do despacho (nunca confia em destinatários embutidos no evento além do necessário);
3. **Renderiza templates versionados** com os dados do evento;
4. **Aplica preferências do usuário** dentro dos limites definidos pela organização;
5. **Despacha pelos canais habilitados** (MVP: sistema + e-mail);
6. **Consolida** notificações repetitivas (digest);
7. **Audita todo o ciclo de entrega** (Created → Queued → Dispatched → Delivered → Read, com Falha → Retry → DLQ → Reprocessamento).

### 1.1 O que o Notification Center NÃO é

- **Não é dono dos eventos de negócio.** Quem publica `WorkflowCompleted` é o Workflow Engine; o NC apenas consome.
- **Não contém regras de negócio dos módulos.** Quem decide que "aprovador deve ser lembrado após 24h" é o Workflow Engine (WF-EVT-009); o NC decide **como e por qual canal** entregar.
- **Não é sistema de e-mail marketing ou transacional genérico** para sistemas externos.

---

## 2. Conceitos do Domínio

| Conceito | Descrição |
|---|---|
| **Notification** | Instância de uma notificação destinada a um destinatário específico, gerada a partir de um evento de negócio. |
| **NotificationTemplate** | Modelo de conteúdo versionado (semver), com assunto, corpo e variáveis, por canal e idioma. Templates publicados são **imutáveis**. |
| **Channel** | Canal de entrega. MVP: `SYSTEM` (centro de notificações in-app) e `EMAIL`. Roadmap: Teams, Slack, Push, WhatsApp, Webhook. |
| **Delivery** | Registro de uma tentativa de entrega de uma Notification em um Channel, com estado próprio. |
| **UserPreference** | Preferências do usuário: canais habilitados, horário permitido, idioma, agrupamento, resumo (digest). |
| **EventSubscription** | Mapa de roteamento: tipo de evento → regra de resolução de destinatários + template + prioridade. |
| **Digest** | Consolidação de múltiplas notificações de mesmo tipo/destinatário em uma única entrega (ex.: "15 aprovações pendentes"). |
| **Suppression** | Decisão registrada de **não** entregar uma notificação (opt-out do usuário, canal desabilitado pela organização, duplicata idempotente). Sempre auditada. |

---

## 3. Canais

| Canal | Código | MVP | Prioridade máxima | Observações |
|---|---|---|---|---|
| Centro de notificações (in-app) | `SYSTEM` | ✅ Sim | Todas | Persistido; suporta marcação de leitura; contador de não lidas. |
| E-mail | `EMAIL` | ✅ Sim | Todas | Provider SMTP configurável; suppression list; nunca inclui anexo — apenas link autenticado. |
| Microsoft Teams | `TEAMS` | ❌ v2 | HIGH/NORMAL | Webhook por organização. |
| Slack | `SLACK` | ❌ v2 | HIGH/NORMAL | Webhook por organização. |
| Push (mobile) | `PUSH` | ❌ v2 | Todas | Depende do app mobile. |
| WhatsApp | `WHATSAPP` | ❌ v3 | HIGH | Provider externo aprovado. |
| Webhook assinado | `WEBHOOK` | ❌ v3 | Todas | Assinatura HMAC; para integrações. |

**Regra:** todo canal novo passa por ADR e por modelo de ameaça (SEC-002) antes de ser habilitado.

---

## 4. Prioridades, Filas e SLA de Despacho

Conforme PR-001-10 e ADR-010 (RabbitMQ):

| Prioridade | Fila RabbitMQ | SLA de despacho | Casos típicos |
|---|---|---|---|
| `HIGH` | `trino.notifications.high` | ≤ 1 minuto | SLA de aprovação expirado, escalonamento, erros críticos de processo |
| `NORMAL` | `trino.notifications.normal` | ≤ 5 minutos | Aprovação solicitada, workflow concluído/rejeitado, tarefa reatribuída |
| `LOW` | `trino.notifications.low` | ≤ 30 minutos | Lembretes informativos, resumos, digests |

- Filas separadas garantem que picos de `LOW` (digests) nunca atrasem `HIGH` (escalonamentos).
- Cada fila possui consumidores independentes com concurrency configurável.
- Durabilidade: filas e mensagens persistentes (ADR-010).

---

## 5. Retry, Dead Letter e Reprocessamento

Conforme PR-001-10:

- **Tentativas:** até 5 por entrega, com backoff: 30s → 2min → 8min → 32min → 2h.
- **DLQ:** `trino.notifications.dlq` — após a 5ª falha, a Delivery é movida para a DLQ com motivo da falha.
- **Reprocessamento:** operação administrativa auditada (NC-EVT-009), manual ou por rotina, sempre preservando o histórico de tentativas.
- **Idempotência de consumo:** conforme PR-001-05 §14.1 — chave `eventId + consumer`. O NC nunca cria duas Notifications para o mesmo evento + destinatário + tipo.
- **Idempotência de despacho:** e-mails reenviados após timeout incerto não geram duplicidade visível (dedup por `notificationId + channel` na janela de retry).

---

## 6. Templates

### 6.1 Modelo

| Campo | Descrição |
|---|---|
| `templateCode` | Código único (ex.: `NC-TPL-001`). |
| `version` | SemVer. Publicado = imutável. |
| `channel` | `SYSTEM`, `EMAIL`, … (um template por canal). |
| `locale` | `pt-BR`, `en-US` (fallback: usuário → organização → `pt-BR`). |
| `subject` | Assunto com variáveis `{{variavel}}`. |
| `body` | Corpo (texto/HTML conforme canal) com variáveis. |
| `variables` | Contrato de variáveis obrigatórias/opcionais. |
| `status` | `DRAFT` → `PUBLISHED` → `DEPRECATED`. |

### 6.2 Regras de versionamento

- Template `PUBLISHED` é **imutável**: correções exigem nova versão.
- No envio, a Notification **fixa (pin)** `templateCode + version`: reprocessamentos futuros renderizam com a versão original, garantindo auditabilidade.
- `DEPRECATED` não é usado em novos envios, mas continua renderizável para histórico.
- Variável ausente no payload: falha de renderização → Delivery `FAILED` com motivo `TEMPLATE_RENDER_ERROR` (nunca envia com `{{variavel}}` cru).

### 6.3 Localização

Resolução de idioma: preferência do usuário → padrão da organização → `pt-BR`. Se não houver template no idioma resolvido, usa `pt-BR` e registra fallback no log de entrega.

---

## 7. Resolução de Destinatários

1. O evento carrega os **dados de negócio** (ex.: `workflowInstanceId`, `approvalTaskId`, `requesterId`), nunca listas fechadas de e-mails.
2. O NC consulta o **EventSubscription** correspondente ao tipo do evento, que define a **regra de resolução**:
   - `EVENT_FIELD` — destinatário vem de um campo do evento (ex.: `requesterId`);
   - `ROLE_IN_ORG_UNIT` — membros de um papel em uma unidade organizacional (resolve via FD-001-01/FD-001-02 **no momento do despacho**);
   - `USER_LIST` — lista explícita (uso restrito, auditado);
   - `ESCALATION_CHAIN` — cadeia de escalonamento (usada com WF-EVT-011).
3. Resolução **tardia (late binding)**: se o aprovador mudou entre o evento e o despacho, o NC entrega para o aprovador **atual** quando a regra assim exigir (ex.: WF-EVT-012 reatribuição).
4. Usuários desativados no IAM (FD-001-01) são removidos do conjunto de destinatários; a supressão é auditada (NC-EVT-007).

---

## 8. Preferências do Usuário

| Preferência | Opções | Padrão | Observações |
|---|---|---|---|
| Canal por prioridade | SYSTEM / EMAIL por prioridade | SYSTEM+EMAIL para HIGH; SYSTEM para NORMAL/LOW | Organização pode impor mínimos (ex.: HIGH sempre e-mail). |
| Horário permitido | janela + fuso | 08:00–20:00, fuso do usuário | `LOW` respeita a janela; `HIGH` é sempre imediata. |
| Idioma | `pt-BR`, `en-US` | herda da organização | — |
| Agrupamento | ligado/desligado | ligado | Consolida notificações idênticas repetidas. |
| Resumo (digest) | desligado / diário / semanal | desligado | Aplica-se apenas a tipos configuráveis como "digestíveis". |
| Opt-out por tipo | por tipo de notificação | tudo ligado | **Somente tipos configuráveis.** Notificações de compliance/segurança não admitem opt-out. |

**Limites da organização (FD-001-02):** a organização define quais tipos admitem opt-out e os canais mínimos por prioridade. A preferência do usuário nunca reduz o mínimo institucional.

---

## 9. Consolidação (Digest)

- Tipos marcados como digestíveis (ex.: "aprovações pendentes", "documentos aguardando leitura") podem ser consolidados.
- Janela de consolidação configurável (padrão: 15 minutos para agrupamento imediato; diário/semanal para resumo).
- Exemplo: 15 eventos de aprovação pendente para o mesmo aprovador → 1 notificação: *"Você tem 15 aprovações pendentes"*.
- Itens individuais continuam visíveis no centro de notificações (SYSTEM); o digest afeta apenas o canal `EMAIL`.

---

## 10. Escalonamento

Fluxo em 4 etapas quando uma `Delivery` falha definitivamente ou uma notificação `HIGH` não é entregue no SLA:

1. **Etapa 1 — Retry automático:** backoff padrão (seção 5).
2. **Etapa 2 — Canal alternativo:** se o canal primário falha (ex.: SMTP indisponível), tenta `SYSTEM` e marca `degraded=true`.
3. **Etapa 3 — DLQ + alerta operacional:** movida para `trino.notifications.dlq` e publicado NC-EVT-008 (NotificationEscalated) para o time de operações.
4. **Etapa 4 — Notificação ao administrador da organização:** se a falha persiste após reprocessamento, administrador é notificado com o relatório de falhas.

Todo escalonamento é auditado com motivo, etapa e ator (sistema ou usuário).

---

## 11. Auditoria de Entrega

### 11.1 Ciclo de vida da Notification

```
Created → Queued → Dispatched → Delivered → Read
                     │
                     ├─ (falha) → Retrying → Dispatched (nova tentativa)
                     └─ (5 falhas) → Failed → DLQ → Reprocessed → Queued
                     └─ (suprimida) → Suppressed
```

### 11.2 Registro

Cada transição gera um evento NC-EVT-xxx e um registro imutável de auditoria contendo: `notificationId`, `eventId` de origem, `correlationId`, destinatário, canal, `templateCode+version`, timestamp, resultado e motivo.

- **Retenção:** 5 anos (alinhado à política de auditoria do Foundation).
- **Prova de entrega:** `Delivered` em `EMAIL` registra o identificador do provider; `Read` registra primeiro acesso no SYSTEM.
- Consultas de auditoria respeitam isolamento multiempresa (NC-BR-012).

---

## 12. Regras de Negócio

| Código | Regra |
|---|---|
| **NC-BR-001** | Toda notificação nasce de um evento de negócio consumido; não existe criação manual de notificação por API pública. |
| **NC-BR-002** | O consumo de eventos é idempotente por `eventId + consumer`; duplicatas são suprimidas e auditadas. |
| **NC-BR-003** | Destinatários são resolvidos no momento do despacho conforme a regra do EventSubscription (late binding). |
| **NC-BR-004** | Template `PUBLISHED` é imutável; o envio fixa `templateCode + version`. |
| **NC-BR-005** | Falha de renderização bloqueia o envio (nunca entrega conteúdo com variável não resolvida). |
| **NC-BR-006** | Prioridade `HIGH` despacha em ≤ 1 minuto e ignora janela de horário do usuário. |
| **NC-BR-007** | Prioridade `LOW` respeita a janela de horário do usuário; fora da janela, permanece na fila até o próximo horário válido. |
| **NC-BR-008** | Retry: 5 tentativas (30s → 2min → 8min → 32min → 2h); após a 5ª, DLQ com motivo. |
| **NC-BR-009** | Opt-out do usuário aplica-se somente a tipos configuráveis; notificações de compliance e segurança são sempre entregues. |
| **NC-BR-010** | Toda supressão (opt-out, canal desabilitado, duplicata, usuário desativado) é registrada com NC-EVT-007 e motivo. |
| **NC-BR-011** | Toda entrega, falha, retry, leitura, escalonamento e reprocessamento é auditada com ciclo completo e retenção de 5 anos. |
| **NC-BR-012** | Isolamento multiempresa: notificações, preferências e auditoria são particionadas por organização; nenhum dado cruza fronteira organizacional. |
| **NC-BR-013** | E-mails nunca contêm anexos ou dados sensíveis no corpo; apenas resumo + link autenticado com expiração. |
| **NC-BR-014** | Consolidação (digest) só se aplica a tipos marcados como digestíveis e nunca a prioridade `HIGH`. |

---

## 13. Eventos

### 13.1 Eventos publicados pelo Notification Center

| Código | Evento | Payload (resumo) | Consumidores típicos |
|---|---|---|---|
| **NC-EVT-001** | NotificationCreated | notificationId, eventId, recipientId, type, priority, correlationId | Analytics |
| **NC-EVT-002** | NotificationQueued | notificationId, channel, queue, scheduledAt | Observabilidade |
| **NC-EVT-003** | NotificationDispatched | notificationId, channel, attempt, dispatchedAt | Observabilidade |
| **NC-EVT-004** | NotificationDelivered | notificationId, channel, providerRef, deliveredAt | Analytics, módulo de origem |
| **NC-EVT-005** | NotificationRead | notificationId, readAt | Módulo de origem (ex.: Workflow) |
| **NC-EVT-006** | NotificationFailed | notificationId, channel, attempt, reason, nextRetryAt | Observabilidade |
| **NC-EVT-007** | NotificationSuppressed | notificationId, reason (opt-out/duplicate/disabled/inactive-user) | Auditoria |
| **NC-EVT-008** | NotificationEscalated | notificationId, stage, reason | Operações/SRE |
| **NC-EVT-009** | NotificationReprocessed | notificationId, reprocessedBy, previousAttempts | Auditoria |

Todos seguem PR-001-05 §14.1: outbox, at-least-once, `correlationId`, versionamento de contrato (`v1`).

### 13.2 Eventos consumidos (exemplos vinculantes)

| Origem | Evento | Uso pelo NC |
|---|---|---|
| FD-001-04 Workflow | WF-EVT-005/006/007/008 (Completed/Rejected/Suspended/Cancelled) | Notificar solicitante — NORMAL |
| FD-001-04 Workflow | WF-EVT-009 ApprovalReminderDue | Lembrete ao aprovador — NORMAL |
| FD-001-04 Workflow | WF-EVT-010 ApprovalSLAExpired | Alerta de SLA — HIGH |
| FD-001-04 Workflow | WF-EVT-011 ApprovalEscalated | Escalonamento ao superior — HIGH |
| FD-001-04 Workflow | WF-EVT-012 ApprovalTaskReassigned | Novo aprovador — NORMAL |
| FD-001-04 Workflow | WF-EVT-014 WorkflowWaitingAdmin | Admin da organização — HIGH |
| Módulos de negócio (roadmap) | eventos de negócio conforme seus documentos | conforme EventSubscription |

**Contrato:** novos tipos de evento consumidos exigem registro de EventSubscription e, se necessário, novos templates — sem alteração de código do NC para casos padronizados (configuração acima de customização).

---

## 14. APIs

| Método | Endpoint | Descrição | Permissão |
|---|---|---|---|
| `GET` | `/api/v1/notifications?cursor=&unreadOnly=` | Centro de notificações do usuário (keyset pagination) | autenticado |
| `POST` | `/api/v1/notifications/{id}/read` | Marca como lida | dono da notificação |
| `POST` | `/api/v1/notifications/read-all` | Marca todas como lidas | autenticado |
| `GET` | `/api/v1/notifications/unread-count` | Contador de não lidas | autenticado |
| `GET` | `/api/v1/notification-preferences` | Consulta preferências | autenticado |
| `PUT` | `/api/v1/notification-preferences` | Atualiza preferências (dentro dos limites da organização) | autenticado |
| `GET` | `/api/v1/admin/notification-templates` | Lista templates | `notification.template.read` |
| `POST` | `/api/v1/admin/notification-templates` | Cria rascunho de template | `notification.template.manage` |
| `POST` | `/api/v1/admin/notification-templates/{code}/publish` | Publica versão (imutável) | `notification.template.manage` |
| `POST` | `/api/v1/admin/notification-deliveries/{id}/reprocess` | Reprocessa entrega da DLQ | `notification.admin` |
| `GET` | `/api/v1/admin/notification-audit?...` | Consulta auditoria de entrega | `notification.audit.read` |

- Respostas seguem o envelope padrão do Foundation; erros com `code`, `message`, `correlationId`.
- APIs administrativas exigem escopo de organização e são auditadas.

---

## 15. Integrações com o Foundation

| Domínio | Integração |
|---|---|
| **FD-001-01 IAM** | Resolução de usuários ativos, papéis e claims; autenticação das APIs; vínculo de preferências ao usuário. |
| **FD-001-02 Organization** | Limites institucionais de canal/opt-out, idioma padrão, fuso da organização, isolamento multiempresa. |
| **FD-001-03 Document Management** | Links autenticados em notificações que referenciam documentos (sem anexos por e-mail — NC-BR-013). |
| **FD-001-04 Workflow Engine** | Principal produtor de eventos consumidos no MVP (seção 13.2). |
| **FD-001-06 Audit Service** *(planejado)* | Destino dos trilhos de auditoria de entrega (retenção 5 anos). |

---

## 16. Requisitos Não Funcionais

| Categoria | Requisito |
|---|---|
| **Disponibilidade** | Despacho degradado: se `EMAIL` falha, `SYSTEM` continua operando (NC-BR-008 etapa 2). |
| **Desempenho** | SLA por prioridade (seção 4); consumidores escaláveis horizontalmente por fila. |
| **Confiabilidade** | At-least-once + idempotência + DLQ + reprocessamento auditado; nenhuma notificação se perde silenciosamente. |
| **Segurança** | SEC-001/002/003: conteúdo sanitizado contra XSS no centro de notificações; links com token de uso único e expiração; suppression list de e-mail; segredos de SMTP em cofre de segredos. |
| **Observabilidade** | Métricas por fila (depth, latência de despacho, taxa de falha), por canal e por template; alerta em backlog de `HIGH`. |
| **Privacidade (LGPD)** | Dados pessoais mínimos no payload; auditoria pseudonimizada após prazo legal conforme política de retenção. |

---

## 17. Restrições Arquiteturais

1. **Único ponto de despacho:** módulos de negócio são proibidos de enviar e-mail/notificação diretamente; violação é bloqueada em revisão arquitetural.
2. **Configuração acima de customização:** novos tipos de notificação = novo EventSubscription + template, não código novo.
3. **Sem lógica de negócio:** o NC não calcula prazos, SLA de aprovação nem regras de escalonamento de negócio — apenas entrega.
4. **Outbox obrigatório** na publicação dos NC-EVT (PR-001-05 §14.1).
5. **Multiempresa por construção:** toda consulta carrega `organizationId` do contexto autenticado.

---

## 18. Critérios de Conclusão do Módulo (MVP)

- [ ] Consumidores das filas `high/normal/low` com retry, backoff e DLQ operacionais.
- [ ] EventSubscription para todos os eventos WF-EVT da seção 13.2.
- [ ] Templates NC-TPL publicados (pt-BR) para os tipos do MVP, com versionamento e pin.
- [ ] Canais `SYSTEM` (centro de notificações + contador) e `EMAIL` (SMTP configurável, suppression list) entregando.
- [ ] Preferências do usuário com limites da organização aplicados.
- [ ] Auditoria completa do ciclo de entrega consultável via API administrativa.
- [ ] Testes: idempotência de consumo, late binding de destinatário, retry até DLQ, reprocessamento, opt-out permitido/bloqueado, isolamento multiempresa.

---

## 19. Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Descrição |
|---|---|---|
| FD-001-01 IAM | Obrigatória | Usuários, papéis, autenticação. |
| FD-001-02 Organization | Obrigatória | Limites, idioma/fuso padrão, isolamento. |
| FD-001-04 Workflow Engine | Consumo de eventos | Maior fonte de eventos do MVP. |
| PR-001 Purchase Requisition | Consumo de eventos | Notificações de requisição (roadmap pós-MVP do PR). |
| FD-001-06 Audit Service | Produção de trilhas | Auditoria de entrega (quando aprovado). |

---

## 20. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Canais SYSTEM + EMAIL; templates versionados; preferências; filas por prioridade; retry/DLQ; consolidação por agrupamento; auditoria de entrega; integração com eventos do Workflow Engine. |
| **v2.0** | Resumo diário/semanal (digest agendado); canais Teams e Slack; push mobile; analytics de entrega por template. |
| **v3.0** | WhatsApp; webhooks assinados (HMAC) para integrações externas; preferências granulares por tipo e por módulo; A/B de templates. |

---

## 21. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Versão inicial aprovada: objetivo, conceitos, canais, filas/prioridades, retry/DLQ, templates versionados, resolução de destinatários, preferências, digest, escalonamento, auditoria, regras NC-BR-001..014, eventos NC-EVT-001..009, APIs, integrações, NFRs, restrições, critérios de conclusão, matriz de dependência e roadmap. | Arquitetura Trino |
