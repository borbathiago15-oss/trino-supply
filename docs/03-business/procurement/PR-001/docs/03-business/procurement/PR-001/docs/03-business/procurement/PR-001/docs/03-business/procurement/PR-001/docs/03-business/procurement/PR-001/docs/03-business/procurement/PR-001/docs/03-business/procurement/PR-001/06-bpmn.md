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

---

# 4. Fluxo Principal

## Atividade 1

Criar Solicitação

Responsável:

Solicitante

Saída:

Draft

---

## Atividade 2

Adicionar Itens

Responsável:

Solicitante

Validações:

- Quantidade
- Unidade
- Descrição

---

## Atividade 3

Anexar Documentos

Opcional

Conforme política da empresa.

---

## Atividade 4

Enviar Solicitação

Evento gerado:

PurchaseRequisitionSubmitted

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

---

## Gateway 1

Validação OK?

SIM

↓

Fluxo de Aprovação

NÃO

↓

Retornar para Ajuste

---

## Atividade 6

Fluxo de Aprovação

Executado pelo Approval Engine.

Pode possuir:

- um aprovador
- múltiplos níveis
- paralelismo
- sequenciamento

---

## Gateway 2

Aprovado?

SIM

↓

Liberar para Compras

NÃO

↓

Rejeitar

---

## Atividade 7

Disponibilizar para Compras

Evento:

ReadyForProcurement

---

## Evento Final

Fim do Processo

---

# 5. Fluxos Alternativos

## FA-001

Solicitação Cancelada

Origem:

Draft

Destino:

Cancelled

---

## FA-002

Solicitação Rejeitada

Origem:

Approval

Destino:

Rejected

---

## FA-003

Retorno para Ajustes

Origem:

Validation

Destino:

Returned

Após correção:

Submitted

---

# 6. Gateways

## GW-001

Validação automática

Resultado:

Aprovado

ou

Retornar

---

## GW-002

Aprovação

Resultado:

Aprovado

Rejeitado

---

## GW-003

Workflow

Determina:

- aprovadores
- sequência
- paralelismo

---

# 7. Objetos de Dados

Purchase Requisition

Purchase Requisition Item

Attachment

Approval

Comment

Timeline

Audit

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

# 9. Regras Relacionadas

PR-BR-020

PR-BR-021

PR-BR-022

PR-BR-030

PR-BR-031

PR-BR-040

PR-BR-050

---

# 10. Indicadores do Processo

Lead Time

Tempo de Aprovação

Tempo em Ajuste

Tempo em Validação

Tempo até Compras

Número de Rejeições

Número de Cancelamentos

---

# 11. Exceções

Workflow inexistente

Centro de custo inválido

Solicitante sem permissão

Projeto inexistente

Categoria inválida

Aprovador indisponível

---

# 12. SLA por Etapa

| Etapa | SLA Padrão |
|--------|------------|
| Criação | Livre |
| Validação | < 1 minuto |
| Aprovação | Configurável |
| Ajuste | Configurável |
| Liberação para Compras | Imediata |

---

# 13. Pontos de Integração

Approval Engine

Notification Engine

Timeline

Audit

Analytics

Foundation

---

# 14. Artefatos Relacionados

Business Rules

State Machine

Domain Model

Event Storming

API

Database

Workflow Engine
