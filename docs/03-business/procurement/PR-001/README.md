# PR-001 — Purchase Requisition

> A Solicitação de Compra (Purchase Requisition) é o ponto de entrada oficial do processo de suprimentos no Trino Supply.

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
