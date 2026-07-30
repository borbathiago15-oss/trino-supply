# MMS-002-10 — Notifications

**Documento:** MMS-002-10 — Notifications
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 v1.1.0, MMS-002-05 (Event Storming), MMS-002-06 (BPMN), MMS-002-09 (Permissions), FD-001-05 (Notification Service), FD-001-06 (Audit)
**Referências:** PR-001-10 (padrão de formato Enterprise), GOV-001

> Este documento define os eventos do módulo Item Catalog que geram notificações e como essas notificações devem ser consumidas pelo Notification Center da plataforma Trino Supply.

---

# 1. Objetivo

Garantir que os responsáveis pelo catálogo e pela operação sejam informados nos momentos em que a integridade do catálogo exige ação — item crítico inativado/descartado, impacto de Master Data e bloqueios de segurança — utilizando notificações relevantes, configuráveis e rastreáveis.

O Item Catalog é um módulo de cadastro: **o volume de notificações é intencionalmente baixo**. Operações rotineiras (cadastro, edição, ativação, sinônimos) não geram notificações — seu feedback é dado na própria tela (MMS-002-07, mensagens MSG-IC-UC).

---

# 2. Princípios

As notificações devem obedecer aos seguintes princípios:

- Baseadas em eventos de negócio.
- Configuráveis por organização.
- Configuráveis por usuário.
- Não bloquear o fluxo principal.
- Totalmente auditáveis.
- Reprocessáveis em caso de falha.
- Parcimônia: somente eventos que exigem ação de alguém geram notificação.

---

# 3. Canais Suportados

## MVP

- Notificação dentro do sistema (Notification Center)
- E-mail

## Roadmap

- Microsoft Teams
- Slack
- Push Mobile
- Webhooks

---

# 4. Eventos que Geram Notificações

| Evento | Origem | Destinatário | Obrigatória |
|---------|--------|--------------|-------------|
| ItemInactivated (item crítico) | EVT-IC-004 + POL-IC-04 | Gerente de Suprimentos + Almoxarifado | Sim |
| ItemDiscarded (item crítico) | EVT-IC-005 + POL-IC-04 | Gerente de Suprimentos + Almoxarifado | Sim |
| MasterDataImpactDetected | MSG-IC-009 + POL-IC-05 | Mantenedor do catálogo | Sim |
| MasterDataImpactEscalated | ESC-IC-001 (SLA de tratamento estourado) | Gerente de Suprimentos | Sim |
| SecurityBlockOnCatalog | EXC-IC-003 (ação sem permissão) | Administrador (security audit) | Sim |
| ValidationFailureSystematic | EXC-IC-007 recorrente | Suporte/Operações | Configurável |

**Item crítico (POL-IC-04):** item abaixo do ponto de reposição ou com solicitações em aberto no MMS-003.

---

# 5. Tipos de Notificação

## Ação Necessária

Exemplo:

"O item CAP-001 (EPI) possui solicitações em aberto e foi inativado. Avalie as solicitações pendentes."

"Valores de Master Data usados por 12 itens foram inativados. Revise o catálogo."

## Alerta

Exemplo:

"Impacto de Master Data sem tratamento há 5 dias úteis."

## Erro / Segurança

Exemplo:

"Tentativa bloqueada de ativação de item sem permissão (usuário X, item Y)."

---

# 6. Templates

Cada tipo de notificação utilizará um template versionado.

| Template | Assunto | Variáveis |
|----------|---------|-----------|
| IC-NOT-001 | Item crítico inativado | Código, descrição, motivo, nº de solicitações em aberto, mantenedor, empresa |
| IC-NOT-002 | Item crítico descartado | Código, descrição, motivo, nº de solicitações em aberto, mantenedor, empresa |
| IC-NOT-003 | Impacto de Master Data no catálogo | Valor de Master Data, tipo (unidade/categoria/grade), nº de itens afetados, link para a lista filtrada |
| IC-NOT-004 | Escalonamento de impacto de Master Data | Idem IC-NOT-003 + dias em aberto |
| IC-NOT-005 | Bloqueio de segurança no catálogo | Usuário, ação tentada, item, timestamp |
| IC-NOT-006 | Falha sistemática de validação | Item, contagem de falhas, último erro |

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

Caso um impacto de Master Data permaneça sem tratamento além do SLA (`materials.item.masterdata-impact.sla.days`, padrão 5 dias úteis):

1. Relembrar o mantenedor (50% do SLA — TMR-IC-001).
2. Notificar o Gerente de Suprimentos no estouro (ESC-IC-001 / TMR-IC-002).

Todas as etapas devem ser parametrizáveis.

---

# 9. Consolidação

O Notification Center poderá agrupar eventos semelhantes.

Exemplo:

Em vez de:

- 12 notificações de impacto de Master Data (uma por item)

Exibir:

"A inativação da grade 'Tamanhos Calçados' impacta 12 itens do catálogo."

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
- Tempo médio de tratamento de impacto de Master Data
- Notificações escaladas

---

# 12. Regras de Negócio

- Toda notificação deve estar vinculada a um evento de negócio.
- O envio não pode bloquear a execução do processo principal.
- Falhas de envio devem permitir reprocessamento.
- Templates devem ser versionados.
- O conteúdo deve respeitar o idioma do usuário.
- Operações rotineiras do catálogo não geram notificação (feedback em tela).

---

# 13. Dependências

- Notification Center (FD-001-05)
- Event Bus (exchange `trino.materials` — ADR-010)
- Audit Service (FD-001-06)
- Identity & Access Management (FD-001-01)
- Master Data (FD-001-09 — eventos de origem da POL-IC-05)

---

# 14. Especificação Operacional (Enterprise)

## 14.1 Queue e Processamento Assíncrono

| Aspecto | Definição |
| ------- | --------- |
| **Origem** | Notification Service consome eventos do exchange `trino.materials` (RabbitMQ) conforme MMS-002-05 §14, além dos eventos FD-001-09 roteados pela POL-IC-05 |
| **Filas** | `trino.notifications.high`, `trino.notifications.normal`, `trino.notifications.low` — filas compartilhadas da plataforma, uma por classe de prioridade (§14.2) |
| **Workers** | Pool de consumidores com prefetch configurável (`notification.worker.prefetch`, padrão 10); escalonamento horizontal por fila |
| **Não bloqueio** | A publicação do evento de negócio nunca aguarda o envio da notificação (outbox + consumo assíncrono); falha de notificação nunca afeta o ciclo de vida do item |
| **Idempotência** | Deduplicação por `(eventId, recipientId, channel)` — reentrega do evento não gera notificação duplicada |

## 14.2 Prioridade

| Classe | Notificações | SLA de despacho | Fila |
| ------ | ------------ | --------------- | ---- |
| **HIGH** | Item crítico inativado/descartado (IC-NOT-001/002), Bloqueio de segurança (IC-NOT-005) | ≤ 1 min | `trino.notifications.high` |
| **NORMAL** | Impacto de Master Data (IC-NOT-003), Escalonamento (IC-NOT-004) | ≤ 5 min | `trino.notifications.normal` |
| **LOW** | Falha sistemática (IC-NOT-006), consolidações e resumos | ≤ 30 min | `trino.notifications.low` |

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
| **Identificação** | `IC-NOT-xxx` + `version` (semver) — ex.: `IC-NOT-001 v1.0.0` |
| **Imutabilidade** | Versão publicada é imutável; alterações geram nova versão |
| **Pinagem** | A notificação registra a versão exata do template usada no envio (auditoria, Seção 10) |
| **Fallback** | Se a versão configurada não existir para o idioma do usuário: fallback para o idioma padrão da organização; se inexistente, `pt-BR`; sempre registrado |
| **Governança** | Criação/alteração de templates por Administrador, com aprovação registrada; templates nunca contêm lógica executável (renderização segura) |

## 14.5 Localization

| Aspecto | Definição |
| ------- | --------- |
| **Idiomas MVP** | `pt-BR` (padrão), `en-US` |
| **Resolução** | Preferência do usuário → padrão da organização → `pt-BR` |
| **Conteúdo localizado** | Assunto, corpo, rótulos de ação e formatos de data/número |
| **Chaves i18n** | `ic.not.*` (ex.: `ic.not.critical-inactivated.subject`); templates referenciam chaves, nunca texto fixo |
| **Fuso horário** | Datas exibidas no fuso do usuário; armazenamento sempre UTC |

## 14.6 Scheduling

| Aspecto | Definição |
| ------- | --------- |
| **Imediatas** | Notificações HIGH/NORMAL despachadas assim que o evento é consumido |
| **Horário permitido** | Preferência do usuário (Seção 7): notificações LOW fora do horário permitido são agendadas para o próximo horário válido (`notification.schedule.respect-user-hours`, padrão `true`; HIGH sempre imediata) |
| **Lembretes de SLA** | Agendados pelos timers do processo (TMR-IC-001 em 50% do SLA de impacto de Master Data) |
| **Resumo diário (roadmap)** | Job agregador por usuário, horário configurável |

## 14.7 Escalonamento (detalhamento)

Complementando a Seção 8:

| Etapa | Gatilho | Destinatário | Configuração |
| ----- | ------- | ------------ | ------------ |
| 1. Lembrete | 50% do SLA de impacto de Master Data | Mantenedor do catálogo | TMR-IC-001 |
| 2. Estouro | 100% do SLA (MasterDataImpactEscalated) | Gerente de Suprimentos | `materials.item.masterdata-impact.sla.days` |
| 3. Sistêmico | Falhas de entrega em massa ou DLQ; evento crítico de catálogo em DLQ (ESC-IC-002) | Time de Operações/SRE | Alerta operacional |

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
| NOT-IC-BR-001 | Toda notificação vinculada a um evento de negócio (eventId obrigatório) |
| NOT-IC-BR-002 | Envio assíncrono; nunca bloqueia o fluxo principal |
| NOT-IC-BR-003 | Idempotência por `(eventId, recipientId, channel)` |
| NOT-IC-BR-004 | Notificações obrigatórias não admitem opt-out de canal crítico |
| NOT-IC-BR-005 | Destinatários sempre resolvidos no momento do despacho (nunca snapshot antigo de papel/escopo) |
| NOT-IC-BR-006 | Autor da ação não recebe notificação da própria ação (ex.: mantenedor que inativou o item) |
| NOT-IC-BR-007 | Destinatário fora do escopo da empresa do item nunca é notificado (isolamento organizacional) |
| NOT-IC-BR-008 | Conteúdo nunca inclui imagens/anexos do item; apenas link autenticado para o item |
| NOT-IC-BR-009 | Falha de template/idioma usa cadeia de fallback (§14.4) e registra o fallback usado |
| NOT-IC-BR-010 | Reprocessamento de DLQ exige ação operacional autenticada e auditada |
| NOT-IC-BR-011 | Operações rotineiras (cadastro, edição, ativação bem-sucedida, sinônimos, parâmetros) nunca geram notificação |

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

Retenção: registros de entrega por 5 anos (política de retenção corporativa), em storage append-only. Consulta de entrega disponível ao Administrador e ao Auditor (IC-PERM-010).

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: 6 eventos notificáveis (parcimônia — operações rotineiras não notificam), 6 templates IC-NOT, especificação operacional completa (queues, 3 classes de prioridade, retry 5× exponencial, template version, localization pt-BR/en-US, scheduling, escalonamento em 3 etapas, preferências, regras de envio NOT-IC-BR-001..011 e auditoria de entrega com ciclo de vida completo). |
