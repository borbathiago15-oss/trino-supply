# PR-001 — Purchase Requisition

> A Solicitação de Compra (Purchase Requisition) é o ponto de entrada oficial do processo de suprimentos no Trino Supply.

**Documento:** PR-001 — Visão do Módulo
**Versão:** 1.1.0
**Status:** 🟢 Approved
**Criticidade:** 🔴 Core

---

# Objetivo

O módulo de Solicitação de Compra tem como objetivo registrar, controlar e acompanhar todas as necessidades de aquisição de materiais e serviços da organização, garantindo rastreabilidade, governança e padronização desde a identificação da necessidade até a emissão do Pedido de Compra.

Este módulo é o primeiro estágio do domínio **Procurement** e representa o início formal do ciclo de compras.

---

# Escopo

Este módulo contempla:

- Registro de solicitações de compra
- Inclusão de materiais e serviços
- Solicitações com um ou vários itens
- Controle de anexos
- Justificativas
- Centros de custo
- Projeto (quando aplicável)
- Prioridade
- Data de necessidade
- Workflow de aprovação
- Histórico completo
- Comentários
- Auditoria
- Timeline

Não contempla:

- Cotações (RFQ)
- Equalização
- Negociação
- Pedido de Compra
- Recebimento
- Estoque

Esses processos pertencem a módulos específicos.

---

# Objetivos do Negócio

O módulo deve permitir que qualquer necessidade de compra seja registrada de forma estruturada, eliminando solicitações por e-mail, mensagens instantâneas ou planilhas.

Os principais objetivos são:

- Padronizar solicitações
- Reduzir retrabalho
- Melhorar a qualidade das informações
- Automatizar aprovações
- Aumentar a rastreabilidade
- Reduzir tempo de processamento
- Melhorar indicadores de compras

---

# Objetivos do MVP

No MVP, o módulo entrega obrigatoriamente:

- Criação de solicitação manual com um ou vários itens
- Anexos via Document Management (Foundation)
- Submissão com validações automáticas
- Workflow de aprovação (sequencial e paralelo, conforme parametrização)
- Aprovação, rejeição e retorno para ajuste
- Cancelamento com justificativa
- Comentários via Collaboration (Foundation)
- Timeline e auditoria completas
- Consulta com filtros combináveis e paginação
- Notificações por sistema e e-mail

Fora do MVP (roadmap): templates, cópia de requisição, favoritos, catálogo, sugestão de fornecedor, integração ERP, IA, mobile.

---

# Problemas que Resolve

Sem este módulo, normalmente ocorrem:

- Pedidos informais
- Compras sem autorização
- Falta de rastreabilidade
- Perda de histórico
- Dificuldade de auditoria
- Retrabalho
- Compras duplicadas
- Informações incompletas

---

# Atores

## Solicitante

Responsável por registrar a necessidade.

---

## Gestor

Responsável por aprovar ou rejeitar a solicitação.

---

## Comprador

Recebe solicitações aprovadas para iniciar o processo de aquisição.

---

## Administrador

Configura regras, permissões e parâmetros.

---

# Fluxo Macro

Necessidade

↓

Solicitação

↓

Validação

↓

Aprovação

↓

Disponível para Compras

---

# Entradas

O módulo recebe:

- Necessidade de compra
- Lista de itens
- Quantidades
- Unidade
- Centro de custo
- Projeto
- Data necessária
- Prioridade
- Justificativa
- Anexos

---

# Saídas

O módulo produz:

- Solicitação de Compra
- Histórico
- Timeline
- Registro de auditoria
- Solicitação aprovada para RFQ
- Solicitação rejeitada
- Solicitação cancelada

---

# Integrações Internas

Foundation

- Usuários
- Empresas
- Unidades
- Permissões
- Auditoria

Approval Workflow

- Aprovação

Supplier

- Consulta futura

Notifications

- Alertas

Analytics

- Indicadores

---

# Dependências do Foundation

Conforme ADR-011, este módulo consome exclusivamente serviços do Foundation:

| Capacidade | Documento | Uso no módulo |
|------------|-----------|----------------|
| Identity & Access Management | FD-001-01 | Autenticação, autorização, escopo organizacional |
| Organization | FD-001-02 | Empresa, unidade, centro de custo, projeto |
| Document Management | FD-001-03 | Anexos da solicitação (Document Collection) |
| Workflow Engine | FD-001-04 | Fluxo de aprovação, alçadas, delegação |
| Notification Center | FD-001-05 | Notificações de eventos do módulo |
| Audit Service | FD-001-06 | Trilha de auditoria imutável |
| Timeline Service | FD-001-07 | Linha do tempo da solicitação |
| Collaboration | FD-001-08 | Comentários |
| Master Data | FD-001-09 | Categorias e unidades de medida |
| Configuration | FD-001-10 | Parâmetros das regras configuráveis |

---

# Eventos Publicados

Eventos de negócio emitidos pelo módulo (detalhamento em PR-001-05):

- PurchaseRequisitionCreated
- PurchaseRequisitionUpdated
- ItemAdded
- ItemRemoved
- PurchaseRequisitionSubmitted
- ValidationStarted
- ValidationCompleted
- ApprovalStarted
- PurchaseRequisitionApproved
- PurchaseRequisitionRejected
- PurchaseRequisitionReturned
- PurchaseRequisitionCancelled
- PurchaseRequisitionClosed
- AttachmentAdded
- CommentAdded

---

# Eventos Consumidos

O módulo reage a eventos do Foundation:

| Evento | Origem | Reação |
|--------|--------|--------|
| ApprovalCompleted | Workflow Engine | Transição para Approved ou Rejected |
| ApprovalDelegated | Workflow Engine | Recalcular pendências e notificar |
| SLAExpired | Workflow Engine | Escalonamento conforme política |
| UserDisabled | IAM | Bloquear ações pendentes do usuário |
| CompanyDeactivated | Organization | Congelar novas solicitações da empresa |
| CostCenterDeactivated | Organization | Impedir novos vínculos ao centro de custo |
| ProjectClosed | Organization | Impedir novos vínculos ao projeto |

---

# Requisitos Não Funcionais

| Categoria | Requisito |
|-----------|-----------|
| Performance | Consultas paginadas com Keyset Pagination; listagens < 2s |
| Segurança | RBAC + ABAC + Escopo Organizacional em toda operação (PR-001-09) |
| Auditoria | 100% das transições e alterações auditadas, logs imutáveis |
| Multiempresa | Filtro obrigatório por `company_id` em toda consulta |
| Concorrência | Optimistic Concurrency via `version` |
| Disponibilidade | Notificações nunca bloqueiam o fluxo principal |
| Internacionalização | Conteúdo de notificações no idioma do usuário |
| Persistência | Soft Delete; nunca exclusão física |
| Observabilidade | CorrelationId ponta a ponta em todos os eventos |

---

# Restrições Arquiteturais

1. O Aggregate Root é `PurchaseRequisition`; nenhuma entidade interna é alterada diretamente (PR-001-04).
2. Nenhuma transição de estado fora da State Machine oficial (PR-001-03); estados nunca alterados diretamente no banco.
3. O módulo não possui tabelas próprias de anexos, comentários ou aprovações — consome Foundation (ADR-011).
4. Somente eventos de negócio (ADR-010); eventos técnicos são proibidos.
5. O banco é projeção do domínio, nunca o contrário (ADR-009).
6. O módulo não implementa autenticação, autorização, notificação, auditoria ou timeline próprios.
7. Nenhuma funcionalidade do MVP pode depender de IA, ERP ou mobile (decisão definitiva do produto).

---

# Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Direção | Descrição |
|--------|------|---------|-----------|
| Foundation | Obrigatória | Consome | Todos os serviços compartilhados (tabela acima) |
| RFQ | Futura | Produz para | Requisições aprovadas viram demanda de cotação |
| Purchase Order | Futura | Produz para | Itens atendidos vinculados a pedidos |
| Supplier Management | Futura | Consome | Sugestão e consulta de fornecedores |
| Analytics | Opcional | Produz para | KPIs e indicadores do módulo |
| Contracts | Futura | Consome | Verificação de contrato vigente (alerta, não bloqueio) |
| Receiving | Nenhuma | — | Sem acoplamento direto |

---

# Documentação Relacionada

PR-001-01 — Business Context

PR-001-02 — Business Rules

PR-001-03 — State Machine

PR-001-04 — Domain Model

PR-001-05 — Event Storming

PR-001-06 — BPMN

PR-001-07 — Use Cases

PR-001-08 — User Stories

PR-001-09 — Permissions

PR-001-10 — Notifications

PR-001-11 — Database

PR-001-12 — Business Journey

PR-001-13 — API (planejado)

PR-001-14 — UX (planejado)

PR-001-15 — Wireframes (planejado)

PR-001-16 — Acceptance Criteria (planejado)

PR-001-17 — Test Scenarios (planejado)

---

# KPIs

- Tempo médio para criação
- Tempo médio para aprovação
- Tempo médio até Compras
- Solicitações por unidade
- Solicitações por centro de custo
- Solicitações emergenciais
- Solicitações canceladas
- Solicitações rejeitadas
- Lead Time da requisição

---

# Critérios de Qualidade

O módulo somente será considerado concluído quando:

- Todas as regras estiverem documentadas
- Todos os estados definidos
- APIs especificadas
- Banco de dados modelado
- Wireframes aprovados
- Casos de teste concluídos
- Critérios de aceite aprovados

---

# Critérios de Conclusão do Módulo (DoD)

O módulo PR-001 estará pronto para release quando, além dos Critérios de Qualidade acima:

1. Documentos PR-001-01 a PR-001-17 com status `Approved` no registry.
2. Cobertura de testes dos casos TC-001 a TC-006 e de todas as regras obrigatórias.
3. Todas as regras `Obrigatórias` com caso de teste automatizado associado.
4. Matriz de rastreabilidade (regra × UC × API × teste) 100% preenchida.
5. Revisão de segurança conforme SEC-001 e SEC-003 sem pendências críticas.
6. Eventos publicados validados contra PR-001-05 (payload, idempotência, DLQ).
7. Migrations versionadas aplicadas e reversíveis (PR-001-11).

---

# Roadmap

Versão 1

- Solicitação manual
- Workflow
- Aprovação
- Comentários
- Timeline
- Auditoria

Versão 2

- Templates
- Copiar solicitação
- Favoritos
- Catálogo
- Sugestão de fornecedor

Versão 3

- Recomendações inteligentes
- Integrações ERP
- Automação baseada em políticas

---

# Funcionalidades Futuras

Backlog oficial de evolução (não compromete o MVP):

- Templates de requisição e cópia de solicitações
- Favoritos e catálogo de itens
- Alertas inteligentes (item semelhante recente, contrato vigente, fornecedor exclusivo)
- Agrupamento e divisão de itens pelo comprador
- Exportação PDF da requisição (UC-014)
- Integração ERP e BI (eventos externos)
- Recomendações baseadas em IA (versão futura, fora do MVP)

---

# Versionamento do Módulo

| Versão do documento | Data | Alteração |
|---------------------|------|-----------|
| 1.0.0 | 2026-07-29 | Versão inicial do módulo |
| 1.1.0 | 2026-07-30 | Revisão Enterprise: MVP, dependências do Foundation, eventos publicados/consumidos, RNFs, restrições arquiteturais, matriz de dependência, DoD, funcionalidades futuras, versionamento |

O versionamento do **módulo** (software) seguirá Semantic Versioning: `MAJOR.MINOR.PATCH`, alinhado às releases definidas em `docs/11-release` (planejado).
