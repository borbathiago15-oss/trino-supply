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
