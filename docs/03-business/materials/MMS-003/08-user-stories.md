# MMS-003-08 — User Stories

**Documento:** MMS-003-08 — User Stories
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 v1.1.0, MMS-003-02 (Business Rules), MMS-003-07 (Use Cases)
**Referências:** MMS-002-08 / PR-001-08 (padrão), GOV-001

> Stories em Gherkin, agrupadas por Feature, com persona, valor, critérios, regras, evento e UC.

## Feature 1 — Solicitar Material

### US-MR-001 — Criar solicitação
**Como** Solicitante, **quero** pedir itens ao almoxarifado com justificativa e centro de custo, **para** obter material com rastro.
```gherkin
Dado que estou autenticado e há itens Ativos no catálogo
Quando crio uma solicitação com itens, quantidades, justificativa, motivo, centro de custo, local e data
Então a solicitação é salva em Rascunho
E o evento "Solicitação criada" é publicado
```
Regras: MR-BR-001..013 · UC-MR-001.

### US-MR-002 — Selecionar tamanho de EPI/fardamento
**Como** Solicitante, **quero** escolher o tamanho quando o item tem grade, **para** receber o item correto.
```gherkin
Dado um item com grade de tamanhos
Quando adiciono o item sem informar o tamanho
Então o sistema recusa com MR-ERR-012
```
Regras: MR-BR-012 · UC-MR-001.

### US-MR-003 — Anexar evidência
**Como** Solicitante, **quero** anexar foto quando o motivo for "danificado/perda/roubo", **para** justificar a solicitação.
```gherkin
Dado um motivo que exige anexo
Quando submeto sem anexo
Então o sistema recusa com MR-ERR-013
```
Regras: MR-BR-013 · UC-MR-001.

## Feature 2 — Aprovar

### US-MR-004 — Aprovação com contexto
**Como** Aprovador, **quero** ver justificativa, centro de custo e saldo, **para** decidir com base.
```gherkin
Dado uma solicitação submetida no meu escopo
Quando abro a fila de aprovações
Então vejo o contexto decisório de cada item
```
Regras: MR-BR-030 · UC-MR-003.

### US-MR-005 — Aprovação parcial
**Como** Aprovador, **quero** alterar quantidades e rejeitar itens específicos, **para** aprovar só o necessário.
```gherkin
Dado uma solicitação com vários itens
Quando reduzo a quantidade de um item e rejeito outro com motivo
Então apenas os itens aprovados seguem para o roteamento
E o evento "Aprovação parcial registrada" é publicado
```
Regras: MR-BR-031 · UC-MR-003.

### US-MR-006 — Segregação de funções
**Como** organização, **quero** impedir que o solicitante aprove a própria solicitação, **para** garantir controle.
```gherkin
Dado que sou o solicitante
Quando tento aprovar a minha solicitação
Então o sistema recusa com MR-ERR-032
```
Regras: MR-BR-032 · UC-MR-003.

## Feature 3 — Atender e Acompanhar

### US-MR-007 — Rota mista
**Como** Solicitante, **quero** que itens com saldo sejam atendidos pelo estoque e o restante vá à compra, **para** receber o mais rápido possível.
```gherkin
Dado uma solicitação aprovada
Quando a validação de estoque roda
Então itens com saldo vão para reserva e itens sem saldo geram demanda de compra com referência de origem
```
Regras: MR-BR-040..043 · UC-MR-004.

### US-MR-008 — Acompanhamento consolidado
**Como** Solicitante, **quero** ver todos os itens na mesma solicitação (estoque e compra), **para** saber o andamento.
```gherkin
Dado uma solicitação com rota mista
Quando abro a solicitação
Então vejo o status de cada item nas duas rotas
```
Regras: MR-BR-051 · UC-MR-005.

### US-MR-009 — Confirmar recebimento
**Como** Solicitante, **quero** confirmar o recebimento, **para** concluir a solicitação.
```gherkin
Dado que todos os itens foram entregues/recebidos
Quando confirmo o recebimento
Então a solicitação é Concluída
```
Regras: MR-BR-052 · UC-MR-005.

## Feature 4 — Cancelar / Administrar

### US-MR-010 — Cancelar
**Como** Solicitante, **quero** cancelar nos estados permitidos, **para** desfazer uma demanda desnecessária.
```gherkin
Dado uma solicitação em estado permitido
Quando cancelo
Então as reservas ativas vinculadas são liberadas
```
Regras: MR-BR-022 · UC-MR-006.

### US-MR-011 — Locais de entrega
**Como** Administrador, **quero** cadastrar locais de entrega com código gerado, **para** padronizar a entrega.
Regras: MR-BR-060 · UC-MR-007.

### US-MR-012 — Visão do almoxarifado
**Como** Almoxarife, **quero** uma fila filtrável de solicitações aprovadas, **para** atender com eficiência.
```gherkin
Dado que sou do almoxarifado
Quando abro a visão do almoxarifado
Então vejo todas as solicitações aprovadas com filtros (solicitante, período, status, CC, empresa, categoria, número)
E um Solicitante comum não acessa esta visão
```
Regras: MR-BR-070 · UC-MR-008.

## Matriz de Rastreabilidade

| Story | Feature | Regras | UC |
|-------|---------|--------|-----|
| US-MR-001 | Solicitar | 001..013 | UC-MR-001 |
| US-MR-002 | Solicitar | 012 | UC-MR-001 |
| US-MR-003 | Solicitar | 013 | UC-MR-001 |
| US-MR-004 | Aprovar | 030 | UC-MR-003 |
| US-MR-005 | Aprovar | 031 | UC-MR-003 |
| US-MR-006 | Aprovar | 032 | UC-MR-003 |
| US-MR-007 | Atender | 040..043 | UC-MR-004 |
| US-MR-008 | Atender | 051 | UC-MR-005 |
| US-MR-009 | Atender | 052 | UC-MR-005 |
| US-MR-010 | Cancelar/Admin | 022 | UC-MR-006 |
| US-MR-011 | Cancelar/Admin | 060 | UC-MR-007 |
| US-MR-012 | Cancelar/Admin | 070 | UC-MR-008 |

# Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 12 user stories US-MR-001..012 em 4 Features (Solicitar, Aprovar, Atender/Acompanhar, Cancelar/Administrar) com persona, valor, Gherkin, regras MR-BR e UC — padrão MMS-002-08. |
