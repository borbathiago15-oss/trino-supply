**Documento:** PR-001-06 — Business Process Specification (BPMN)
**Versão:** 1.1.0
**Status:** Approved

# PR-001-06 — Business Process Specification (BPMN)

> Especificação do processo de negócio para Solicitação de Compra (Purchase Requisition).

---

# 1. Objetivo

Descrever o fluxo operacional completo da Solicitação de Compra, desde a identificação da necessidade até sua disponibilização para a área de Compras.

Este documento é a base para modelagem BPMN 2.0 e para implementação do Workflow Engine.

---

# 2. Participantes (Pools e Lanes)

## Pool: Empresa

### Lane: Solicitante
- Identifica necessidade
- Cria requisição
- Corrige pendências
- Acompanha status

### Lane: Gestor
- Analisa
- Aprova
- Rejeita
- Solicita ajustes

### Lane: Sistema
- Executa validações
- Calcula workflow
- Registra auditoria
- Dispara notificações

### Lane: Comprador
- Recebe requisição aprovada
- Inicia processo de cotação

---

# 3. Evento Inicial

### Start Event

**Necessidade de Compra Identificada**

Gatilhos:

- Falta de material
- Solicitação de serviço
- Projeto
- CAPEX
- OPEX
- Compra emergencial

**Tipo BPMN:** None Start Event (início manual, acionado pelo usuário).

---

# 4. Fluxo Principal

## Atividade 1

Criar Solicitação

Responsável:

Solicitante

Saída:

Draft

**Tipo BPMN:** User Task.
**Business Object de saída:** BO-001 Purchase Requisition (status = Draft).

---

## Atividade 2

Adicionar Itens

Responsável:

Solicitante

Validações:

- Quantidade
- Unidade
- Descrição

**Tipo BPMN:** User Task.
**Business Object:** BO-002 Purchase Requisition Item.

---

## Atividade 3

Anexar Documentos

Opcional

Conforme política da empresa.

**Tipo BPMN:** User Task (opcional).
**Business Object:** BO-003 Attachment.
**Data Store:** DS-002 Document Storage (MinIO) — escrita via FD-001-03 Document Management; nunca expõe URL pública.

---

## Atividade 4

Enviar Solicitação

Evento gerado:

PurchaseRequisitionSubmitted

**Tipo BPMN:** User Task seguida de Message Intermediate Throw Event (MSG-001).
**Message Event:** `PurchaseRequisitionSubmitted` publicado no exchange `trino.procurement` (RabbitMQ) conforme PR-001-05 §14.

---

## Atividade 5

Validação Automática

Executada pelo Sistema.

Valida:

- Campos obrigatórios
- Centro de custo
- Projeto
- Categoria
- Permissões
- Workflow

**Tipo BPMN:** Service Task.
**Timeout:** TIME-001 — 60 segundos; em caso de timeout, a instância é sinalizada como falha técnica e a requisição retorna para o solicitante com motivo técnico (ver §11 e §15.3).
**Data Store lido:** DS-001 Transactional Database (PostgreSQL).

---

## Gateway 1

Validação OK?

SIM

↓

Fluxo de Aprovação

NÃO

↓

Retornar para Ajuste

**Tipo BPMN:** Exclusive Gateway (XOR).
**Regras do gateway:** ver §9.1 (RG-GW-001).

---

## Atividade 6

Fluxo de Aprovação

Executado pelo Approval Engine.

Pode possuir:

- um aprovador
- múltiplos níveis
- paralelismo
- sequenciamento

**Tipo BPMN:** Sub-Process (expandido no §16).
**Escalonamento:** ver §15.1 (ESC-001, ESC-002).
**Timeout por nível:** TIME-002 — configurável por política da empresa (`approval.sla.hours`, padrão 48h); ao estourar, dispara escalonamento ESC-001.

---

## Gateway 2

Aprovado?

SIM

↓

Liberar para Compras

NÃO

↓

Rejeitar

**Tipo BPMN:** Exclusive Gateway (XOR).
**Regras do gateway:** ver §9.2 (RG-GW-002).

---

## Atividade 7

Disponibilizar para Compras

Evento:

ReadyForProcurement

**Tipo BPMN:** Service Task + Message End Event.
**Message Event:** disponibilização da requisição para o módulo Procurement (consumer interno do evento `PurchaseRequisitionApproved`, conforme PR-001-05 §14.2 EVT-009 / POL-005).

---

## Evento Final

Fim do Processo

**Tipo BPMN:** End Events múltiplos conforme desfecho (ver §8).

---

# 5. Fluxos Alternativos

## FA-001

Solicitação Cancelada

Origem:

Draft

Destino:

Cancelled

**Detalhamento:** o cancelamento é permitido também a partir de Returned e, conforme State Machine (PR-001-03), de estados intermediários autorizados. Dispara EVT-012 `PurchaseRequisitionCancelled`, que encerra a instância de workflow e cancela aprovações pendentes (compensação COMP-002, ver §15.4).

---

## FA-002

Solicitação Rejeitada

Origem:

Approval

Destino:

Rejected

**Detalhamento:** motivo obrigatório (PR-BR-031). Dispara EVT-010 e notificação ao solicitante. Estado terminal — sem compensação aplicável.

---

## FA-003

Retorno para Ajustes

Origem:

Validation

Destino:

Returned

Após correção:

Submitted

**Detalhamento:** o retorno pode ocorrer por falha de validação automática (origem técnica) ou por decisão do aprovador (origem funcional, GW-002). Em ambos os casos o ciclo de aprovação reinicia do zero após novo envio; contadores de ciclo são incrementados e auditados.

---

# 6. Gateways

## GW-001

Validação automática

Resultado:

Aprovado

ou

Retornar

## GW-002

Aprovação

Resultado:

Aprovado

Rejeitado

## GW-003

Workflow

Determina:

- aprovadores
- sequência
- paralelismo

**As regras detalhadas de cada gateway estão na Seção 9.**

---

# 7. Objetos de Dados

Purchase Requisition

Purchase Requisition Item

Attachment

Approval

Comment

Timeline

Audit

**Especificação completa dos Business Objects e Data Stores na Seção 8.1.**

---

# 8. Eventos BPMN

### Start Event

Need Identified

---

### Intermediate Events

Submitted

Approved

Rejected

Returned

Cancelled

---

### End Events

Ready for Procurement

Cancelled

Rejected

---

## 8.1 Business Objects e Data Stores (detalhamento Enterprise)

### Business Objects

| Código | Business Object | Descrição | Estados possíveis no processo | Persistência |
| ------ | --------------- | --------- | ------------------------------ | ------------ |
| BO-001 | Purchase Requisition | Aggregate principal do processo | Draft, Submitted, Validation, Approval, Returned, Approved, Rejected, Cancelled, Closed | DS-001 |
| BO-002 | Purchase Requisition Item | Item da requisição (material ou serviço) | Ativo, Removido | DS-001 |
| BO-003 | Attachment | Documento anexado à requisição | Pendente de upload, Confirmado | DS-001 (metadados) + DS-002 (binário) |
| BO-004 | Approval | Decisão de um nível de aprovação | Pending, Approved, Rejected, Cancelled | DS-001 |
| BO-005 | Comment | Comentário interno ou público | Publicado | DS-001 |
| BO-006 | Timeline Entry | Registro cronológico de evento do processo | Imutável (append-only) | DS-001 |
| BO-007 | Audit Record | Registro de auditoria de alteração | Imutável (append-only) | DS-001 |

### Data Stores

| Código | Data Store | Tecnologia | Acesso |
| ------ | ---------- | ---------- | ------ |
| DS-001 | Transactional Database | PostgreSQL | Leitura/escrita transacional via repositórios do domínio; isolamento por `company_id` |
| DS-002 | Document Storage | MinIO | Escrita/leitura via FD-001-03 Document Management; sem URL pública; acesso via URLs assinadas de curta duração |
| DS-003 | Message Broker | RabbitMQ | Publicação de eventos (outbox relay) e consumo pelos motores (Workflow, Notification, Timeline, Audit) |
| DS-004 | Distributed Cache | Redis | Cache de consulta e sessão; nunca fonte de verdade; TTL configurável |

---

## 8.2 Message Events (detalhamento Enterprise)

| Código | Message Event | Tipo BPMN | Momento | Payload/Referência |
| ------ | ------------- | --------- | ------- | ------------------ |
| MSG-001 | PurchaseRequisitionSubmitted | Intermediate Throw | Após Atividade 4 | PR-001-05 §14.2 EVT-005 |
| MSG-002 | ValidationCompleted | Intermediate Throw | Após Atividade 5 | PR-001-05 §14.2 EVT-007 |
| MSG-003 | ApprovalStarted | Intermediate Throw | Entrada do Sub-Process de Aprovação | PR-001-05 §14.2 EVT-008 |
| MSG-004 | PurchaseRequisitionApproved | Intermediate Throw | Saída "SIM" de GW-002 | PR-001-05 §14.2 EVT-009 |
| MSG-005 | PurchaseRequisitionRejected | Intermediate Throw | Saída "NÃO" de GW-002 | PR-001-05 §14.2 EVT-010 |
| MSG-006 | PurchaseRequisitionReturned | Intermediate Throw | Saída "NÃO" de GW-001 ou retorno por aprovador | PR-001-05 §14.2 EVT-011 |
| MSG-007 | PurchaseRequisitionCancelled | Intermediate Throw | FA-001 | PR-001-05 §14.2 EVT-012 |
| MSG-008 | ApprovalReminderDue | Intermediate Catch (Timer → Message) | Lembrete de SLA de aprovação | Dispara notificação de lembrete (PR-001-10) |
| MSG-009 | ApprovalEscalationDue | Intermediate Catch (Timer → Message) | Estouro de SLA de aprovação | Dispara escalonamento ESC-001 (§15.1) |

---

# 9. Regras por Gateway (detalhamento Enterprise)

## 9.1 RG-GW-001 — Regras do Gateway de Validação

| Código | Condição avaliada | Regra de negócio associada | Resultado quando falha |
| ------ | ----------------- | -------------------------- | ---------------------- |
| RG-GW-001-A | Todos os campos obrigatórios preenchidos (justificativa, data necessária, centro de custo, ao menos 1 item) | PR-BR-020 | Retorno para Ajuste com lista de pendências |
| RG-GW-001-B | Centro de custo válido e ativo na estrutura organizacional | PR-BR-021 | Retorno para Ajuste |
| RG-GW-001-C | Projeto válido quando informado | PR-BR-022 | Retorno para Ajuste |
| RG-GW-001-D | Categoria de cada item válida | PR-BR-040 | Retorno para Ajuste |
| RG-GW-001-E | Solicitante possui permissão de submissão no escopo organizacional | PR-001-09 Permissions | Exceção EXC-003 (não retorna para ajuste; bloqueio de segurança) |
| RG-GW-001-F | Existe workflow aplicável à combinação empresa/unidade/valor/categoria | PR-BR-030 | Exceção EXC-001 (workflow inexistente) |
| RG-GW-001-G | Data necessária não é passada e respeita antecedência mínima configurada | PR-BR-050 | Retorno para Ajuste |

**Semântica:** qualquer falha em A–D ou G → fluxo "Retornar para Ajuste" (MSG-006, origem Validation). Falha em E ou F → evento de exceção (§11), com registro de auditoria e notificação ao Administrador.

## 9.2 RG-GW-002 — Regras do Gateway de Aprovação

| Código | Condição avaliada | Regra de negócio associada | Resultado |
| ------ | ----------------- | -------------------------- | --------- |
| RG-GW-002-A | Todos os níveis obrigatórios da cadeia aprovados | PR-BR-031, PR-001-03 | Saída "SIM" → MSG-004 |
| RG-GW-002-B | Qualquer aprovador rejeita (com motivo obrigatório) | PR-BR-031 | Saída "NÃO" → MSG-005 (processo encerra, sem prosseguir níveis restantes) |
| RG-GW-002-C | Aprovador solicita ajuste | PR-BR-031 | MSG-006 (origem Approver) → Returned; cadeia é suspensa e reiniciada após novo submit |
| RG-GW-002-D | Em paralelismo, decisão segue política configurada: `approval.parallel.policy` = `ALL` (todos aprovam) ou `ANY` (primeira aprovação conclui; rejeição avaliada conforme política) | PR-BR-030 | Conforme política |
| RG-GW-002-E | Segregation of Duties: aprovador ≠ solicitante; alçada por valor respeitada | PR-001-09 Permissions §SoD | Violação → Exceção EXC-006 (bloqueio de segurança, auditoria obrigatória) |

## 9.3 RG-GW-003 — Regras do Gateway de Workflow

| Código | Condição avaliada | Resultado |
| ------ | ----------------- | --------- |
| RG-GW-003-A | Resolução da cadeia por: empresa → unidade de negócio → centro de custo → faixa de valor → categoria | Cadeia de aprovadores determinada |
| RG-GW-003-B | Nenhum aprovador resolvido para algum nível | Exceção EXC-005 (aprovador indisponível) → escalonamento ESC-002 |
| RG-GW-003-C | Política de delegação ativa para aprovador ausente (férias/afastamento) | Substituição por delegado vigente, com auditoria (PR-001-09 §Delegation) |

---

# 10. Indicadores do Processo

Lead Time

Tempo de Aprovação

Tempo em Ajuste

Tempo em Validação

Tempo até Compras

Número de Rejeições

Número de Cancelamentos

**Detalhamento:** todos os indicadores são derivados dos registros imutáveis de Timeline (BO-006) e Audit (BO-007), sem consulta ao estado mutável do aggregate. Metas de KPI estão em PR-001-01 §KPIs.

---

# 11. Exceções

Workflow inexistente

Centro de custo inválido

Solicitante sem permissão

Projeto inexistente

Categoria inválida

Aprovador indisponível

## 11.1 Catálogo de Exceções (detalhamento Enterprise)

| Código | Exceção | Origem | Tratamento | Notificação |
| ------ | ------- | ------ | ---------- | ----------- |
| EXC-001 | Workflow inexistente | GW-001 (RG-GW-001-F) | Processo suspenso; Administrador configura workflow; após configuração, solicitante reenvia | Administrador + Solicitante |
| EXC-002 | Centro de custo inválido/inativo | GW-001 (RG-GW-001-B) | Retorno para ajuste | Solicitante |
| EXC-003 | Solicitante sem permissão | GW-001 (RG-GW-001-E) | Bloqueio de segurança; auditoria obrigatória; não retorna para ajuste | Administrador (security audit) |
| EXC-004 | Projeto inexistente | GW-001 (RG-GW-001-C) | Retorno para ajuste | Solicitante |
| EXC-005 | Aprovador indisponível (sem delegado vigente) | GW-003 (RG-GW-003-B) | Escalonamento ESC-002 para Gestor de Compras; SLA pausado com registro | Procurement Manager |
| EXC-006 | Violação de Segregation of Duties ou alçada | GW-002 (RG-GW-002-E) | Bloqueio de segurança; auditoria obrigatória; workflow redirecionado ao próximo aprovador elegível | Procurement Manager + Auditor |
| EXC-007 | Falha técnica de validação (timeout TIME-001) | Atividade 5 | Compensação COMP-001: requisição retorna ao estado Submitted e validação é reexecutada (até 3 tentativas); após 3 falhas, Returned com motivo técnico | Solicitante + Suporte |

---

# 12. SLA por Etapa

| Etapa | SLA Padrão |
|--------|------------|
| Criação | Livre |
| Validação | < 1 minuto |
| Aprovação | Configurável |
| Ajuste | Configurável |
| Liberação para Compras | Imediata |

## 12.1 Detalhamento de SLA (Enterprise)

| Etapa | SLA Padrão | Configuração | Medição | Ação em estouro |
| ----- | ---------- | ------------ | ------- | --------------- |
| Criação (Draft) | Livre | — | Não medido | — |
| Validação | < 1 minuto | `validation.timeout.seconds=60` | MSG-002 menos MSG-001 | EXC-007 / COMP-001 |
| Aprovação (por nível) | 48h | `approval.sla.hours` (por empresa/nível) | Entrada do nível → decisão | Lembrete em 50% do SLA (MSG-008); escalonamento no estouro (MSG-009 → ESC-001) |
| Ajuste (Returned) | 5 dias úteis | `returned.sla.days` | Entrada em Returned → novo Submit | Lembrete ao solicitante; sem bloqueio |
| Liberação para Compras | Imediata (< 5 min) | `procurement.handoff.timeout.minutes=5` | MSG-004 → consumo pelo módulo Procurement | Alerta operacional (evento crítico, DLQ + alerta) |
| Ciclo completo (Lead Time) | Monitorado | `leadtime.warning.days` | MSG-001 → MSG-004 | Relatório gerencial (Analytics) |

---

# 13. Pontos de Integração

Approval Engine

Notification Engine

Timeline

Audit

Analytics

Foundation

**Detalhamento:** Foundation inclui FD-001-01 IAM (autenticação/autorização e escopo organizacional), FD-001-02 Organization (empresa, unidade, centro de custo, projeto) e FD-001-03 Document Management (anexos via MinIO). Contratos de eventos conforme PR-001-05 §14.

---

# 14. Artefatos Relacionados

Business Rules

State Machine

Domain Model

Event Storming

API

Database

Workflow Engine

---

# 15. Elementos Enterprise do Processo

## 15.1 Escalonamentos

| Código | Escalonamento | Gatilho | Ação | Destino |
| ------ | ------------- | ------- | ---- | ------- |
| ESC-001 | Estouro de SLA de aprovação | MSG-009 (timer `approval.sla.hours`) | Notificar aprovador atual + seu gestor imediato; registrar em Timeline/Audit; se segundo estouro (`approval.escalation.second.hours`), redirecionar nível ao gestor do aprovador | Gestor imediato do aprovador |
| ESC-002 | Aprovador indisponível | EXC-005 | Pausar SLA, registrar motivo, notificar Procurement Manager para designar substituto ou ativar delegação | Procurement Manager |
| ESC-003 | Evento crítico em DLQ (EVT-005, EVT-008, EVT-009, EVT-012) | Falha de mensageria após retry | Alerta operacional imediato; reconciliação via outbox | Time de Operações/SRE |

## 15.2 Timeouts

| Código | Timeout | Escopo | Valor padrão | Comportamento |
| ------ | ------- | ------ | ------------ | ------------- |
| TIME-001 | Validação automática | Atividade 5 | 60 s | EXC-007 → COMP-001 |
| TIME-002 | Decisão de nível de aprovação | Sub-Process de Aprovação | 48 h (configurável) | MSG-008 (lembrete em 50%) → MSG-009 (escalonamento) |
| TIME-003 | Permanência em Returned | FA-003 | 5 dias úteis (configurável) | Lembrete ao solicitante |
| TIME-004 | Handoff para Compras | Atividade 7 | 5 min | ESC-003 |

## 15.3 Eventos Intermediários de Tempo

| Código | Tipo BPMN | Definição |
| ------ | --------- | --------- |
| TMR-001 | Timer Intermediate Catch (non-interrupting) no Sub-Process de Aprovação | 50% de `approval.sla.hours` → MSG-008 (lembrete) |
| TMR-002 | Timer Intermediate Catch (interrupting) no Sub-Process de Aprovação | 100% de `approval.sla.hours` → MSG-009 (escalonamento ESC-001); fluxo principal continua (não cancela a aprovação) |
| TMR-003 | Timer Intermediate Catch (non-interrupting) no estado Returned | `returned.sla.days` → notificação de lembrete ao solicitante |

## 15.4 Compensações

| Código | Compensação | Quando | Ação compensatória |
| ------ | ----------- | ------ | ------------------ |
| COMP-001 | Falha técnica na validação | EXC-007 | Requisição retorna a Submitted; validação reexecutada (máx. 3 tentativas); Timeline registra tentativa |
| COMP-002 | Cancelamento com workflow ativo | FA-001 a partir de estado com aprovação pendente | EVT-012 consumido pelo Workflow Engine encerra instância, cancela BO-004 pendentes (status = Cancelled) e notifica aprovadores pendentes |
| COMP-003 | Retorno para ajuste com workflow ativo | MSG-006 (origem Approver) | Cadeia de aprovação é suspensa: níveis pendentes marcados como Cancelled; após novo Submit, nova cadeia completa é instanciada (ciclo incrementado) |
| COMP-004 | Falha no handoff para Compras | TIME-004 | Republicação do evento via outbox relay; se persistir, DLQ + reconciliação manual documentada |

---

# 16. Sub-Processo de Aprovação (detalhamento)

```text
┌─────────────────────────────────────────────────────────────┐
│ Sub-Process: Fluxo de Aprovação (Atividade 6)               │
│                                                             │
│  MSG-003 ApprovalStarted                                    │
│      │                                                      │
│      ▼                                                      │
│  GW-003: Resolver cadeia (aprovadores, sequência,           │
│          paralelismo)                                       │
│      │                                                      │
│      ├── Nível sequencial: aguarda decisão do nível N       │
│      │   antes de ativar N+1                                │
│      │                                                      │
│      └── Nível paralelo: ativa múltiplos aprovadores;       │
│          decisão conforme approval.parallel.policy          │
│          (ALL | ANY)                                        │
│      │                                                      │
│  Timers: TMR-001 (lembrete 50%), TMR-002 (escalonamento)    │
│                                                             │
│  Decisões possíveis por nível:                              │
│    - Aprovar  → próximo nível ou MSG-004 (final)            │
│    - Rejeitar → MSG-005 (encerra processo)                  │
│    - Ajustar  → MSG-006 (suspende cadeia, COMP-003)         │
│                                                             │
│  Cancelamento externo (FA-001):                             │
│    - MSG-007 + COMP-002 encerram todas as pendências        │
└─────────────────────────────────────────────────────────────┘
```

Cada decisão gera registro em BO-004 (Approval) com `approverId`, `level`, `decision`, `comments` e `decidedAt`, conforme PR-001-11 Database.

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | — | Arquitetura Trino | Versão inicial aprovada |
| 1.1.0 | 2026-07-30 | Arquitetura Trino | Adicionados Business Objects, Data Stores, Message Events (MSG-001..009), regras por gateway (RG-GW-001/002/003), catálogo de exceções (EXC-001..007), detalhamento de SLA, escalonamentos (ESC-001..003), timeouts (TIME-001..004), timers intermediários (TMR-001..003), compensações (COMP-001..004) e detalhamento do Sub-Processo de Aprovação. Fluxo principal, fluxos alternativos, gateways, atividades, objetos de dados e eventos existentes foram integralmente mantidos |
