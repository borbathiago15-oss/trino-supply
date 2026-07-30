# PR-001-03 — Purchase Requisition State Machine

> Este documento define todos os estados possíveis de uma Solicitação de Compra (Purchase Requisition), suas transições, restrições e eventos associados.

---

# 1. Objetivo

Garantir que toda Solicitação de Compra evolua de forma controlada, previsível, auditável e compatível com as políticas da organização.

Nenhuma transição poderá ocorrer fora das regras definidas neste documento.

---

# 2. Princípios

A máquina de estados deve atender aos seguintes princípios:

- Todo documento possui exatamente um estado atual.
- Toda mudança gera auditoria.
- Toda mudança gera evento de domínio.
- Toda mudança atualiza a Timeline.
- Nenhum estado pode ser alterado diretamente no banco de dados.
- Toda transição deve ser realizada por serviços de domínio.

---

# 3. Estados Oficiais

| Código | Estado | Final |
|---------|---------|-------|
| ST-001 | Draft | Não |
| ST-002 | Submitted | Não |
| ST-003 | Under Validation | Não |
| ST-004 | Waiting Approval | Não |
| ST-005 | Approved | Não |
| ST-006 | Rejected | Não |
| ST-007 | Returned for Adjustment | Não |
| ST-008 | Ready for Procurement | Não |
| ST-009 | Cancelled | Sim |
| ST-010 | Closed | Sim |

---

# 4. Descrição dos Estados

## ST-001 — Draft

Situação inicial.

Características:

- Editável
- Exclusão permitida
- Itens podem ser alterados
- Anexos podem ser adicionados
- Nenhuma aprovação iniciada

---

## ST-002 — Submitted

A solicitação foi enviada pelo solicitante.

Neste momento:

- edição bloqueada
- inicia validações automáticas
- dispara notificações

---

## ST-003 — Under Validation

O sistema executa validações automáticas.

Exemplos:

- orçamento

- centro de custo

- projeto

- categoria

- políticas internas

Resultado:

Aprovado para workflow

ou

Retornado para ajuste

---

## ST-004 — Waiting Approval

Aguardando decisão dos aprovadores.

Pode existir:

- um aprovador
- vários aprovadores
- aprovação paralela
- aprovação sequencial

---

## ST-005 — Approved

Todos os aprovadores concluíram.

A solicitação encontra-se oficialmente aprovada.

Ainda não entrou em Compras.

---

## ST-006 — Rejected

A solicitação foi rejeitada.

Dependendo da política poderá:

- Encerrar

ou

Voltar para ajuste.

---

## ST-007 — Returned for Adjustment

Necessita correções.

Permite:

- editar

- anexar documentos

- alterar itens

Após correção retorna para Submitted.

---

## ST-008 — Ready for Procurement

A requisição foi entregue oficialmente ao módulo Procurement.

A partir daqui:

Compras assume responsabilidade.

---

## ST-009 — Cancelled

Estado terminal.

Não poderá retornar.

---

## ST-010 — Closed

Estado final.

Utilizado quando todo processo termina.

---

# 5. Fluxo Principal

Draft

↓

Submitted

↓

Under Validation

↓

Waiting Approval

↓

Approved

↓

Ready for Procurement

↓

Closed

---

# 6. Fluxos Alternativos

Draft

↓

Cancelled

---

Submitted

↓

Returned for Adjustment

↓

Submitted

---

Waiting Approval

↓

Rejected

---

Rejected

↓

Returned for Adjustment

↓

Submitted

---

Approved

↓

Cancelled
(Somente conforme política)

---

# 7. Matriz de Transições

| Origem | Destino | Permitido |
|---------|----------|-----------|
| Draft | Submitted | Sim |
| Draft | Cancelled | Sim |
| Submitted | Under Validation | Sim |
| Under Validation | Waiting Approval | Sim |
| Under Validation | Returned for Adjustment | Sim |
| Waiting Approval | Approved | Sim |
| Waiting Approval | Rejected | Sim |
| Rejected | Returned for Adjustment | Configurável |
| Returned for Adjustment | Submitted | Sim |
| Approved | Ready for Procurement | Sim |
| Ready for Procurement | Closed | Sim |

---

# 8. Eventos de Domínio

| Evento |
|----------|
| RequisitionCreated |
| RequisitionSubmitted |
| ValidationStarted |
| ValidationFinished |
| ApprovalStarted |
| RequisitionApproved |
| RequisitionRejected |
| RequisitionAdjusted |
| ProcurementStarted |
| RequisitionCancelled |
| RequisitionClosed |

---

# 9. Restrições

Não permitido:

- Aprovar Draft.
- Cancelar documento encerrado.
- Editar documento aprovado.
- Alterar itens durante aprovação.
- Excluir documento aprovado.

---

# 10. Auditoria

Cada transição deverá registrar:

- usuário
- data
- hora
- estado anterior
- novo estado
- motivo
- IP
- dispositivo
- observações

---

# 11. Timeline

Cada transição gera automaticamente:

- Timeline
- Auditoria
- Evento
- Notificação (quando aplicável)

---

# 12. APIs Relacionadas

POST /purchase-requisitions

POST /submit

POST /approve

POST /reject

POST /return

POST /cancel

GET /timeline

GET /history

---

# 13. Regras Relacionadas

PR-BR-020

PR-BR-030

PR-BR-040

PR-BR-050

PR-BR-060

---

# 14. Casos de Uso Relacionados

UC-001

UC-002

UC-003

UC-004

UC-005

UC-006

---

# 15. Casos de Teste

TC-001

TC-002

TC-003

TC-004

TC-005

TC-006
