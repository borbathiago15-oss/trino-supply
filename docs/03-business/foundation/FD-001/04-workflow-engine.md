**Documento:** FD-001-04 — Workflow Engine
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Dependências:** FD-001 (Foundation Domain), FD-001-01 (IAM), FD-001-02 (Organization), ADR-010, ADR-011

> Referências de consumo já formalizadas: PR-001-03 (State Machine), PR-001-05 §14 (Eventos), PR-001-06 (BPMN — gateways, timers, escalonamentos, compensações), PR-001-09 (Permissions — SoD, alçada, delegação).

# FD-001-04 — Workflow Engine

> O domínio **Workflow Engine** é responsável por todos os fluxos de aprovação da plataforma Trino Supply: definição de workflows, instanciação, execução de cadeias de aprovadores, SLA, escalonamento, delegação operacional e compensações.

---

# 1. Objetivo

Centralizar a execução de fluxos de aprovação em um único serviço do Foundation, garantindo que **nenhum módulo de negócio implemente motor de aprovação próprio**.

Os módulos (Purchase Requisition, RFQ, Purchase Order, Contracts, Receiving etc.) solicitam a instanciação de workflows e consomem decisões exclusivamente pelos contratos e eventos deste domínio.

Os objetivos são:

* Single Source of Truth para aprovações e fluxos.
* Cadeias de aprovadores configuráveis por organização — sem código por módulo.
* Rastreabilidade completa de cada decisão (quem, quando, em nome de quem, com qual parecer).
* SLA, lembretes e escalonamentos automáticos e auditáveis.
* Multiempresa com isolamento garantido.

---

# 2. Responsabilidades

O Workflow Engine é responsável por:

* Definição e versionamento de workflows (templates de fluxo)
* Resolução da cadeia de aprovadores por contexto (empresa, unidade, centro de custo, valor, categoria)
* Instanciação e execução de workflows (níveis sequenciais e paralelos)
* Registro de decisões (aprovar, rejeitar, retornar) com parecer
* SLA por nível, timers de lembrete e escalonamento
* Substituição de aprovador por delegação vigente (em conjunto com o IAM)
* Compensações: suspensão, cancelamento de pendências e reinício de ciclo
* Eventos de domínio do ciclo de vida do workflow
* Auditoria de todas as decisões e transições

O Workflow Engine **não** é responsável por:

* Regras de negócio dos módulos consumidores (ex.: quando uma requisição pode ser submetida — PR-BR-020)
* Estado de negócio dos aggregates consumidores (o módulo é dono do seu status; o workflow emite fatos, o módulo transiciona)
* Autenticação e modelo de papéis (IAM — FD-001-01)
* Estrutura organizacional (Organization — FD-001-02)
* Entrega de notificações (Notification Center — FD-001-05); o workflow apenas publica os eventos que as disparam
* Persistência da trilha de auditoria (Audit Service — FD-001-06) e da timeline (FD-001-07)

---

# 3. Conceitos do Domínio

## Workflow Definition

Template de fluxo configurado por uma organização. Define as etapas (passos) de aprovação e suas condições.

Atributos principais:

* Id
* Empresa
* Código e nome (ex.: `pr-approval-default`)
* Tipo de processo atendido (ex.: `purchase-requisition`, `rfq`, `contract`)
* Versão (semver) e status (Draft, Active, Retired)
* Passos (Workflow Steps) ordenados
* Política de SLA padrão
* Vigência (início/fim opcionais)

## Workflow Step

Uma etapa de aprovação dentro da definição.

Atributos principais:

* Ordem (nível)
* Modo de execução: `SEQUENTIAL` (um aprovador decide) ou `PARALLEL` (múltiplos aprovadores no mesmo nível)
* Política de decisão do nível paralelo: `ALL` (todos devem aprovar) ou `ANY` (primeira aprovação conclui; rejeição avaliada conforme política da empresa)
* Origem dos aprovadores: papel, gestor hierárquico, usuário específico ou regra de resolução
* Condição de aplicabilidade (faixa de valor, categoria, centro de custo, unidade)
* SLA do nível (horas) e percentual de lembrete
* Obrigatoriedade (se a condição não se aplica, o passo é pulado com registro)

## Routing Rule

Regra de resolução que determina **qual definição de workflow** atende uma solicitação, avaliada em ordem de especificidade (mais específica vence):

```text
empresa → unidade de negócio → centro de custo → faixa de valor → categoria
```

(Conforme RG-GW-003-A, PR-001-06 §9.3.)

## SLA Policy

Parâmetros temporais do nível: duração (`sla.hours`), percentual de lembrete (`reminder.pct`, padrão 50), segunda escalada (`escalation.second.hours`) e política em estouro (notificar, redirecionar, ambos).

## Workflow Instance

Execução concreta de uma definição para uma entidade de negócio.

Atributos principais:

* Id (`workflowInstanceId`)
* Empresa
* Definição e versão utilizadas (pinadas na instanciação — imutável)
* Entidade de origem: tipo (ex.: `purchase-requisition`) + id + número de exibição
* Ciclo (`cycle`, iniciado em 1; incrementado a cada reinício após retorno para ajuste)
* Status da instância (Seção 5)
* Nível corrente
* Solicitante de origem e valor de referência (para SoD e alçada)
* Datas de criação, conclusão e cancelamento

## Approval Task

Tarefa de decisão atribuída a um aprovador dentro de um nível.

Atributos principais:

* Id
* Instância e nível
* Aprovador designado (e origem da designação: direta, delegação, escalonamento)
* Delegante (`delegatedBy`), quando aplicável
* Status: Pending, Decided, Cancelled, Escalated
* Decisão: Approved, Rejected, Returned (+ parecer obrigatório em Rejected/Returned)
* Datas de criação, lembrete, escalonamento e decisão

## Escalation Record

Registro de cada escalonamento: nível, motivo (SLA estourado, aprovador indisponível), aprovador original, destino (gestor imediato ou designado), data e resolução.

---

# 4. Modelo de Definição e Resolução

## 4.1 Versionamento de definições

* Toda alteração de uma definição ativa gera **nova versão**; versões publicadas são imutáveis.
* A instância executa sempre a versão pinada no momento da instanciação — mudanças posteriores na definição **nunca** afetam instâncias em andamento.
* Somente uma versão `Active` por código de workflow e escopo.

## 4.2 Resolução de cadeia (Chain Resolution)

Na instanciação, o motor:

1. Resolve a definição pela Routing Rule mais específica aplicável ao contexto da entidade.
2. Avalia a condição de aplicabilidade de cada passo; passos não aplicáveis são registrados como `Skipped` (não criam tarefas).
3. Resolve os aprovadores de cada passo (papel + escopo, hierarquia ou usuário específico).
4. Se nenhum aprovador for resolvido para um passo obrigatório → exceção `WF-EXC-001` (aprovador indisponível) e escalonamento administrativo (Seção 8.3).
5. Se nenhuma definição for aplicável → exceção de domínio reportada ao módulo consumidor (que a trata conforme suas regras — ex.: EXC-001 do PR-001-06 §11.1).

## 4.3 Modos de execução dos níveis

| Modo | Comportamento |
| ---- | ------------- |
| `SEQUENTIAL` | Um aprovador por nível; a decisão dele conclui o nível |
| `PARALLEL` + `ALL` | Todas as tarefas do nível devem ser aprovadas; uma rejeição encerra o workflow; um retorno suspende a cadeia |
| `PARALLEL` + `ANY` | A primeira aprovação conclui o nível (demais tarefas são canceladas com registro); rejeição encerra o workflow conforme política da empresa (`workflow.parallel.reject-policy`) |

## 4.4 Alçada e SoD na execução

* **Alçada:** se o valor de referência da entidade exceder a alçada do aprovador resolvido, a tarefa é redirecionada ao próximo aprovador elegível com alçada suficiente (escalonamento registrado) — ABAC-02, PR-001-09 §12.2.
* **SoD:** se o aprovador resolvido for o próprio solicitante de origem, a tarefa é redirecionada ao próximo elegível; a tentativa de decisão direta é bloqueada com `WF-EXC-002` e auditoria obrigatória — ABAC-01/SOD-001.
* Exceções a SoD somente por política explícita da empresa (`sod.self-approval.allow`, padrão `false`).

---

# 5. Ciclo de Vida da Instância

```text
                instanciação
                    │
                    ▼
               ┌─────────┐   passo sem aprovador   ┌────────────────┐
               │ Running │ ──────────────────────▶ │ WaitingAdmin   │
               └────┬────┘   (WF-EXC-001)          │ (admin resolve)│
                    │                               └───────┬────────┘
        nível       │ resolvido                             │
        ativo       ▼                                       │
             ┌──────────────┐  SLA 50%   lembrete           │
             │ InProgress   │ ─────────▶ (TMR: reminder)    │
             │ (por nível)  │ ─────────▶ SLA 100%           │
             └──────┬───────┘            │                  │
                    │                    ▼                  │
                    │             ┌────────────┐            │
                    │             │ Escalated  │ ───────────┘
                    │             └─────┬──────┘
                    │                   │ redirecionado
                    ▼                   ▼
        ┌───────────────────────────────────────────┐
        │ Decisões do nível:                         │
        │  aprovado → próximo nível ou Completed     │
        │  rejeitado → Rejected (terminal)           │
        │  retorno   → Suspended (aguarda resubmissão)│
        └───────────────────────────────────────────┘
                    │
   cancelamento da entidade (ex.: EVT-012)
                    ▼
               Cancelled (terminal) — tarefas pendentes canceladas
```

| Estado | Significado | Terminal |
| ------ | ----------- | -------- |
| Running | Instância ativa, nível corrente em andamento | Não |
| Escalated | Nível corrente escalonado por SLA ou indisponibilidade | Não |
| Suspended | Cadeia suspensa por retorno para ajuste; aguarda resubmissão da entidade | Não |
| WaitingAdmin | Falta de aprovador/definição aguardando ação administrativa | Não |
| Completed | Todos os níveis aprovados | **Sim** |
| Rejected | Rejeição em qualquer nível | **Sim** |
| Cancelled | Entidade cancelada ou workflow encerrado administrativamente | **Sim** |

**Invariantes:**

* INV-WF-01 — Uma entidade possui no máximo **uma** instância ativa (Running, Escalated, Suspended ou WaitingAdmin) por vez.
* INV-WF-02 — Instância terminal nunca reabre; resubmissão cria **nova instância** com `cycle` incrementado.
* INV-WF-03 — Tarefa `Decided` é imutável (decisão, aprovador, delegante e data não podem ser alterados).
* INV-WF-04 — Toda transição de instância e toda decisão geram evento de domínio e auditoria na mesma transação.

---

# 6. Regras de Negócio

## WF-BR-001 — Definição obrigatória

Nenhum workflow é instanciado sem definição ativa aplicável resolvida por Routing Rule.
**Tipo:** Obrigatória · **Erro:** `WF-ERR-001`

## WF-BR-002 — Instância única ativa por entidade

Conforme INV-WF-01; tentativa de segunda instanciação é idempotente (retorna a instância ativa existente).
**Tipo:** Obrigatória · **Erro:** `WF-ERR-002`

## WF-BR-003 — Parecer obrigatório em rejeição e retorno

Decisões `Rejected` e `Returned` exigem parecer (10–1.000 caracteres). Aprovação admite parecer opcional.
**Tipo:** Obrigatória · **Erro:** `WF-ERR-003`

## WF-BR-004 — SoD: aprovador ≠ solicitante

Conforme Seção 4.4; bloqueio com redirecionamento e auditoria.
**Tipo:** Configurável (padrão: ativa) · **Erro:** `WF-ERR-004`

## WF-BR-005 — Alçada por valor

Decisão de aprovação exige alçada ≥ valor de referência da entidade.
**Tipo:** Obrigatória · **Erro:** `WF-ERR-005`

## WF-BR-006 — Delegação vigente e não redelegável

Tarefa executada por delegado exige delegação vigente, escopo compatível e registro de `delegatedBy`; delegado não pode redelegar (DEL-001..007, PR-001-09 §15.2).
**Tipo:** Obrigatória · **Erro:** `WF-ERR-006`

## WF-BR-007 — Rejeição encerra o fluxo

Uma rejeição em qualquer nível encerra a instância (`Rejected`) e cancela as tarefas pendentes dos demais níveis.
**Tipo:** Obrigatória

## WF-BR-008 — Retorno suspende a cadeia

Um retorno para ajuste suspende a instância (`Suspended`), cancela as tarefas pendentes e informa o motivo; a resubmissão da entidade cria nova instância (ciclo +1) com cadeia completa.
**Tipo:** Obrigatória

## WF-BR-009 — Cancelamento compensa pendências

O cancelamento da entidade de origem encerra a instância (`Cancelled`) e cancela todas as tarefas pendentes, com evento e notificação aos aprovadores em fila (COMP-002, PR-001-06 §15.4).
**Tipo:** Obrigatória

## WF-BR-010 — SLA por nível com lembrete e escalonamento

Todo nível possui SLA; em 50% (configurável) dispara lembrete; em 100% dispara escalonamento; em segundo estouro, redireciona conforme SLA Policy.
**Tipo:** Configurável (valores), Obrigatória (existência do mecanismo)

## WF-BR-011 — Versão pinada na instanciação

A instância executa a versão da definição vigente no momento da instanciação, imutável durante todo o ciclo.
**Tipo:** Obrigatória

## WF-BR-012 — Isolamento multiempresa

Definições, instâncias, tarefas e escalonamentos pertencem a exatamente uma empresa; nenhuma consulta ignora esse filtro.
**Tipo:** Obrigatória

## WF-BR-013 — Auditoria obrigatória

Instanciação, decisão, lembrete, escalonamento, redirecionamento, suspensão, cancelamento e alteração de definição geram auditoria e evento de domínio.
**Tipo:** Obrigatória

## WF-BR-014 — Idempotência de comandos e eventos

Comandos repetidos (mesma `Idempotency-Key`) e eventos reentregues (mesmo `eventId`) não produzem efeito duplicado.
**Tipo:** Obrigatória

---

# 7. Decisões e Políticas de Aprovação

## 7.1 Comandos aceitos

| Código | Comando | Origem |
| ------ | ------- | ------ |
| WF-CMD-001 | StartWorkflow (entityType, entityId, contexto, valor, solicitante) | Módulo consumidor |
| WF-CMD-002 | ApproveTask (taskId, comments?) | Aprovador (ou delegado) |
| WF-CMD-003 | RejectTask (taskId, reason) | Aprovador (ou delegado) |
| WF-CMD-004 | ReturnTask (taskId, reason) | Aprovador (ou delegado) |
| WF-CMD-005 | CancelWorkflow (instanceId, reason) | Módulo consumidor (entidade cancelada) |
| WF-CMD-006 | ResumeWorkflow (entityId, novo ciclo) | Módulo consumidor (resubmissão) |
| WF-CMD-007 | ReassignTask (taskId, novoAprovador, motivo) | Administrador / Procurement Manager |
| WF-CMD-008 | EscalateLevel (instanceId, motivo) | Sistema (timer) ou Administrador |
| WF-CMD-009 | UpsertWorkflowDefinition (definição, versão) | Administrador |
| WF-CMD-010 | RetireWorkflowDefinition (código, versão) | Administrador |

## 7.2 Validações por decisão

Toda decisão (Approve/Reject/Return) valida, server-side e na ordem:

1. Tarefa existe, está `Pending` e pertence ao escopo do decididor.
2. Decididor é o designado, delegado vigente ou destino de escalonamento (WF-BR-006).
3. SoD: decididor ≠ solicitante de origem (WF-BR-004).
4. Alçada: somente em `Approve` (WF-BR-005).
5. Parecer: obrigatório em `Reject`/`Return` (WF-BR-003).
6. Instância não está em estado terminal (INV-WF-02).

Qualquer falha → erro `WF-ERR-xxx` correspondente, sem efeito colateral, com auditoria de negação.

---

# 8. SLA, Timers e Escalonamento

## 8.1 Timers

| Código | Timer | Disparo | Ação |
| ------ | ----- | ------- | ---- |
| WF-TMR-001 | Lembrete de nível | 50% do SLA (`reminder.pct`) | Evento `ApprovalReminderDue` → notificação ao aprovador |
| WF-TMR-002 | Estouro de nível | 100% do SLA | Evento `ApprovalSLAExpired` → escalonamento (Seção 8.2) |
| WF-TMR-003 | Segundo estouro | `escalation.second.hours` após o primeiro | Redirecionamento do nível conforme SLA Policy |

Timers são persistentes (sobrevivem a restart do serviço) e idempotentes (disparo duplicado não duplica efeito).

## 8.2 Escalonamentos

| Código | Escalonamento | Gatilho | Destino |
| ------ | ------------- | ------- | ------- |
| WF-ESC-001 | Estouro de SLA | WF-TMR-002 | Aprovador + gestor imediato notificados; tarefa marcada `Escalated` |
| WF-ESC-002 | Redirecionamento por segundo estouro | WF-TMR-003 | Gestor imediato do aprovador assume o nível (nova tarefa; original cancelada com motivo) |
| WF-ESC-003 | Aprovador indisponível | Resolução sem elegível (WF-EXC-001) | Instância → `WaitingAdmin`; Administrador designa substituto (WF-CMD-007) ou ativa delegação |

Todo escalonamento gera `Escalation Record`, evento de domínio e notificação. Cadeia de escalonamento visível na timeline da entidade.

## 8.3 Exceções operacionais

| Código | Exceção | Tratamento |
| ------ | ------- | ---------- |
| WF-EXC-001 | Nenhum aprovador elegível para o passo | Instância `WaitingAdmin` + alerta ao Administrador |
| WF-EXC-002 | Tentativa de decisão violando SoD | Bloqueio `WF-ERR-004` + auditoria de segurança |
| WF-EXC-003 | Definição inexistente/retired na instanciação | Erro `WF-ERR-001` ao módulo consumidor (tratado pelo módulo, ex.: EXC-001 PR-001-06) |
| WF-EXC-004 | Timer/escalonamento falhou após retry | DLQ + alerta operacional (evento crítico) |

---

# 9. Delegação e Substituição

* A titularidade da delegação (delegante, delegado, vigência, motivo, escopo) é registrada no IAM/Permissions (PR-001-09 §9 e §15.2).
* Na **resolução** de aprovadores, o motor substitui automaticamente aprovadores com delegação vigente pelo delegado (registrando a origem da designação).
* Na **decisão**, o motor valida a vigência no momento exato da decisão; delegação expirada entre atribuição e decisão bloqueia a tarefa (`WF-ERR-006`) e oferece redirecionamento.
* Toda decisão por delegado registra `delegatedBy` na tarefa, no evento e na auditoria.
* Substituição administrativa (WF-CMD-007) exige papel Administrador ou Procurement Manager, motivo obrigatório e gera auditoria + notificação ao substituído e ao substituto.

---

# 10. Compensações

| Código | Compensação | Gatilho | Efeito |
| ------ | ----------- | ------- | ------ |
| WF-COMP-001 | Suspensão por retorno | Decisão `Returned` | Tarefas pendentes canceladas (`Cancelled`, motivo `returned`); instância `Suspended`; evento ao módulo |
| WF-COMP-002 | Cancelamento da entidade | WF-CMD-005 | Instância `Cancelled`; tarefas pendentes canceladas; aprovadores em fila notificados |
| WF-COMP-003 | Reinício de ciclo | WF-CMD-006 | Nova instância (ciclo +1), cadeia completa recalculada; vínculo com a instância anterior registrado |
| WF-COMP-004 | Conclusão de nível paralelo ANY | Primeira aprovação | Demais tarefas do nível canceladas (`Cancelled`, motivo `decided-by-peer`) |
| WF-COMP-005 | Redirecionamento | WF-ESC-002 / WF-CMD-007 | Tarefa original cancelada com motivo; nova tarefa para o destino; prazo de SLA reiniciado (registrado) |

---

# 11. Eventos de Domínio

Conforme ADR-010 e os padrões transversais de mensageria (PR-001-05 §14.1: outbox, at-least-once, retry exponencial, DLQ, idempotência, ordem por aggregate). Exchange: `trino.foundation.workflow`.

| Código | Evento | Payload (essencial) | Consumidores |
| ------ | ------ | ------------------- | ------------ |
| WF-EVT-001 | WorkflowStarted | instanceId, definitionCode+version, entityType/Id, cycle, chain resolvida, solicitante, valor | Módulo de origem, Timeline, Audit |
| WF-EVT-002 | ApprovalLevelStarted | instanceId, level, tasks (approverId, origem da designação), slaDeadline | Notification, Timeline |
| WF-EVT-003 | ApprovalTaskDecided | instanceId, taskId, level, decision, approverId, delegatedBy?, comments?, decidedAt | Módulo de origem, Timeline, Audit |
| WF-EVT-004 | ApprovalLevelCompleted | instanceId, level, resultado, nextLevel? | Workflow (avanço), Timeline |
| WF-EVT-005 | WorkflowCompleted | instanceId, entityType/Id, cycle, aprovadores por nível, completedAt | **Módulo de origem (decisão final)**, Notification, Timeline, Audit |
| WF-EVT-006 | WorkflowRejected | instanceId, entityType/Id, level, rejectedBy, reason | Módulo de origem, Notification, Timeline, Audit |
| WF-EVT-007 | WorkflowSuspended | instanceId, entityType/Id, level, returnedBy, reason, cycle | Módulo de origem, Notification, Timeline |
| WF-EVT-008 | WorkflowCancelled | instanceId, entityType/Id, reason, cancelledTasks | Módulo de origem, Notification, Timeline, Audit |
| WF-EVT-009 | ApprovalReminderDue | instanceId, taskId, approverId, slaDeadline | Notification |
| WF-EVT-010 | ApprovalSLAExpired | instanceId, taskId, level, approverId | Notification (aprovador + gestor), Timeline |
| WF-EVT-011 | ApprovalEscalated | instanceId, escalationRecord (origem, destino, motivo) | Notification, Timeline, Audit |
| WF-EVT-012 | ApprovalTaskReassigned | instanceId, taskId, de, para, motivo, actorId | Notification, Timeline, Audit |
| WF-EVT-013 | WorkflowDefinitionPublished | definitionCode, version, companyId, actorId | Audit, Configuration |
| WF-EVT-014 | WorkflowWaitingAdmin | instanceId, level, motivo | Notification (Administrador), Timeline |

**Mapeamento com o PR-001:** `WorkflowStarted` → EVT-008 ApprovalStarted; `WorkflowCompleted` → consumido pelo PR-001 para emitir EVT-009 Approved; `WorkflowRejected` → EVT-010; `WorkflowSuspended` → EVT-011; o PR-001 publica EVT-012 Cancelled → consumido aqui como WF-CMD-005.

---

# 12. Permissões

| Ação | Papel | Restrição |
| ---- | ----- | --------- |
| Decidir tarefa (aprovar/rejeitar/retornar) | Aprovador designado, delegado vigente ou destino de escalonamento | SoD + alçada + escopo (Seção 7.2) |
| Consultar instâncias/tarefas | Participantes da entidade, gestores, Administrador, Auditor | Escopo organizacional; fila pessoal por `approverId` |
| Criar/versionar/retirar definições | System Administrator | Empresa do escopo; alteração auditada (WF-EVT-013) |
| Redesignar tarefa | System Administrator, Procurement Manager | Motivo obrigatório; WF-EVT-012 |
| Reprocessar DLQ | Operações/SRE | Autenticado, auditado |

SoD recomendado adicional (SOD-005, PR-001-09 §14): quem configura a definição não deve ser aprovador da mesma cadeia — alerta de conflito, política por empresa.

---

# 13. Integrações

## Consumidores (módulos)

* Purchase Requisition (PR-001) — fluxo de aprovação de requisições (contrato de referência já formalizado em PR-001-03/05/06/09)
* RFQ, Purchase Order, Contracts, Receiving, Supplier Management (roadmap)

## Provedores (Foundation)

* IAM (FD-001-01) — autenticação, papéis, escopo, alçada, delegações
* Organization (FD-001-02) — empresa, unidade, centro de custo, hierarquia de gestores
* Notification Center (FD-001-05) — entrega das notificações disparadas pelos eventos
* Audit Service (FD-001-06) — trilha imutável
* Timeline Service (FD-001-07) — cronologia por entidade
* Configuration (FD-001-10) — políticas (`workflow.*`, `approval.*`, `sod.*`)

---

# 14. Requisitos Não Funcionais

| Tema | Requisito |
| ---- | --------- |
| Persistência | Convenções do Foundation: UUID, `created_at/updated_at/deleted_at`, `created_by/updated_by/deleted_by`, `version`, `company_id` em toda tabela principal; soft delete |
| Concorrência | Optimistic concurrency em instância e tarefa; decisão duplicada → `409` (`WF-ERR-409`) |
| Idempotência | Comandos por `Idempotency-Key` (24 h); eventos por `(eventId, consumerName)` |
| Performance | Fila de aprovação por aprovador p95 < 200 ms; decisão p95 < 300 ms; resolução de cadeia p95 < 500 ms |
| Timers | Persistentes e idempotentes; tolerância de disparo ± 1 min |
| Escalabilidade | Stateless; escalonamento horizontal; filas por prioridade |
| Disponibilidade | Falha do motor não perde decisões (outbox + recuperação); restart retoma timers |
| Segurança | Conforme SEC-001/SEC-003; decisão de autorização server-side; auditoria de negações |
| Observabilidade | Métricas: instâncias ativas, decisões/dia, SLA estourado/nível, tempo médio por nível; logs com `correlationId` |

---

# 15. Entidades do Domínio

* WorkflowDefinition
* WorkflowDefinitionVersion
* WorkflowStep
* RoutingRule
* SLAPolicy
* WorkflowInstance
* ApprovalTask
* EscalationRecord
* DelegationReference (referência lógica ao IAM — sem tabela própria)

---

# 16. Roadmap

**Versão 1 (MVP)**

* Definições versionadas com níveis sequenciais e paralelos (ALL/ANY)
* Routing rules por empresa/unidade/centro de custo/valor/categoria
* Instâncias, tarefas, decisões com parecer
* SLA, lembrete, escalonamento (WF-ESC-001/002/003)
* Delegação vigente e substituição administrativa
* Compensações WF-COMP-001..005
* Eventos WF-EVT-001..014 + auditoria

**Versão 2**

* Quórum por percentual no nível paralelo (ex.: 2 de 3)
* Aprovação condicional (aprova com ressalva registrada)
* Simulador de cadeia (preview da resolução antes da publicação da definição)
* Aprovação em lote (múltiplas tarefas elegíveis de uma vez, com auditoria individual)

**Versão 3**

* Regras dinâmicas por motor de políticas (expressões configuráveis)
* Aprovação por assinatura digital integrada
* IA assistiva: sugestão de aprovadores e detecção de anomalias em decisões

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquitetura Trino | Versão inicial: objetivo, responsabilidades, conceitos (definição, passo, routing rule, SLA policy, instância, tarefa, escalonamento), modelo de definição e resolução, ciclo de vida da instância com invariantes, regras WF-BR-001..014, comandos WF-CMD-001..010, SLA/timers/escalonamentos, delegação e substituição, compensações WF-COMP-001..005, eventos WF-EVT-001..014, permissões, integrações, RNFs, entidades e roadmap |
