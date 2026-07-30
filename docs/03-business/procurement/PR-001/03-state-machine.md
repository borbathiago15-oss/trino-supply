# PR-001-03 — Purchase Requisition State Machine

> Este documento define todos os estados possíveis de uma Solicitação de Compra (Purchase Requisition), suas transições, restrições e eventos associados.

**Versão:** 1.1.0
**Status:** 🟢 Approved

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

# 4. Especificação dos Estados

Cada estado define: Entry Actions, Exit Actions, Eventos aceitos, Eventos rejeitados, Guard Conditions, Side Effects, SLA, Responsável e Auditoria.

---

## ST-001 — Draft

Situação inicial.

Características:

- Editável
- Exclusão permitida
- Itens podem ser alterados
- Anexos podem ser adicionados
- Nenhuma aprovação iniciada

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Criar Aggregate; gerar número; registrar `created_at`/`created_by`; inicializar `version = 1` |
| Exit Actions | Validar invariantes mínimas para o destino |
| Eventos aceitos | CreatePurchaseRequisition, UpdatePurchaseRequisition, AddItem, RemoveItem, AddAttachment, AddComment, SubmitPurchaseRequisition, CancelPurchaseRequisition |
| Eventos rejeitados | ApprovePurchaseRequisition, RejectPurchaseRequisition, ReturnForAdjustment, ClosePurchaseRequisition |
| Guard Conditions | Submit: PR-BR-020/021/022/023 satisfeitas · Cancel: sempre permitido neste estado |
| Side Effects | ItemAdded/ItemRemoved/AttachmentAdded/CommentAdded publicados; Timeline e auditoria atualizadas |
| SLA | Livre (sem SLA) |
| Responsável | Solicitante |
| Auditoria | Criação e toda alteração de campos, itens e anexos |

---

## ST-002 — Submitted

A solicitação foi enviada pelo solicitante.

Neste momento:

- edição bloqueada
- inicia validações automáticas
- dispara notificações

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Bloquear edição; publicar PurchaseRequisitionSubmitted; acionar validações |
| Exit Actions | Nenhuma (transição automática para Under Validation) |
| Eventos aceitos | CancelPurchaseRequisition (conforme política) |
| Eventos rejeitados | UpdatePurchaseRequisition, AddItem, RemoveItem, ApprovePurchaseRequisition, RejectPurchaseRequisition |
| Guard Conditions | Cancel: política `cancel-after-submission` |
| Side Effects | Notificação aos aprovadores (POL-004); Timeline; auditoria |
| SLA | < 1 minuto (transição automática) |
| Responsável | Sistema |
| Auditoria | Submissão com usuário, IP e dispositivo |

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

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar ValidationStarted; executar Specification `ReadyForSubmission` |
| Exit Actions | Publicar ValidationCompleted (com resultado) |
| Eventos aceitos | Validações internas do sistema |
| Eventos rejeitados | Todos os comandos de usuário |
| Guard Conditions | Sucesso → Waiting Approval · Falha → Returned for Adjustment |
| Side Effects | Se falha: PurchaseRequisitionReturned + notificação ao solicitante; se sucesso: localizar workflow (PR-BR-030) |
| SLA | < 1 minuto |
| Responsável | Sistema |
| Auditoria | Resultado de cada validação (aprovada/reprovada) |

---

## ST-004 — Waiting Approval

Aguardando decisão dos aprovadores.

Pode existir:

- um aprovador
- vários aprovadores
- aprovação paralela
- aprovação sequencial

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar ApprovalStarted; criar etapas de aprovação no Workflow Engine; notificar aprovadores |
| Exit Actions | Encerrar etapas pendentes conforme decisão |
| Eventos aceitos | ApprovePurchaseRequisition, RejectPurchaseRequisition, ReturnForAdjustment, CancelPurchaseRequisition (conforme política), delegação |
| Eventos rejeitados | UpdatePurchaseRequisition, AddItem, RemoveItem, SubmitPurchaseRequisition |
| Guard Conditions | Approve: aprovador do nível vigente, escopo e alçada válidos, SoD (solicitante ≠ aprovador) · Reject: motivo obrigatório (UC-005) |
| Side Effects | A cada decisão: Timeline, auditoria, notificação; última aprovação → Approved; uma rejeição → Rejected |
| SLA | Configurável por nível; escalonamento ao expirar (PR-001-10, seção 8) |
| Responsável | Gestor (aprovador do nível vigente) |
| Auditoria | Decisão, parecer, nível, aprovador, delegação |

---

## ST-005 — Approved

Todos os aprovadores concluíram.

A solicitação encontra-se oficialmente aprovada.

Ainda não entrou em Compras.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar PurchaseRequisitionApproved; notificar solicitante e fila de Compras |
| Exit Actions | Publicar disponibilidade para Procurement |
| Eventos aceitos | CancelPurchaseRequisition (somente conforme política — PR-BR-050 configurável) |
| Eventos rejeitados | UpdatePurchaseRequisition, AddItem, RemoveItem, ApprovePurchaseRequisition |
| Guard Conditions | Cancel: política `cancel-after-approval` + sem Pedido de Compra |
| Side Effects | POL-005: disponibilizar para o módulo Procurement |
| SLA | Liberação para Compras imediata |
| Responsável | Sistema |
| Auditoria | Aprovação final com total de níveis e tempo total |

---

## ST-006 — Rejected

A solicitação foi rejeitada.

Dependendo da política poderá:

- Encerrar

ou

Voltar para ajuste.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar PurchaseRequisitionRejected; registrar motivo; notificar solicitante |
| Exit Actions | Se política permite (PR-BR-042): habilitar correção |
| Eventos aceitos | Retorno para ajuste (configurável) |
| Eventos rejeitados | Todos os comandos de edição e aprovação |
| Guard Conditions | Transição para Returned for Adjustment: `allow-resubmit-rejected = true` |
| Side Effects | Timeline, auditoria, notificação |
| SLA | Sem SLA |
| Responsável | Solicitante (acompanhamento) |
| Auditoria | Motivo da rejeição, aprovador, nível |

---

## ST-007 — Returned for Adjustment

Necessita correções.

Permite:

- editar
- anexar documentos
- alterar itens

Após correção retorna para Submitted.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar PurchaseRequisitionReturned; notificar solicitante com comentários do avaliador |
| Exit Actions | Revalidar invariantes antes do reenvio |
| Eventos aceitos | UpdatePurchaseRequisition (campos permitidos), AddItem, RemoveItem, AddAttachment, AddComment, SubmitPurchaseRequisition, CancelPurchaseRequisition |
| Eventos rejeitados | ApprovePurchaseRequisition, RejectPurchaseRequisition |
| Guard Conditions | Submit: mesmas validações da submissão original |
| Side Effects | Todo histórico preservado; novo ciclo de validação |
| SLA | Configurável; lembrete ao solicitante ao expirar |
| Responsável | Solicitante |
| Auditoria | Campos alterados no ajuste, reenvio |

---

## ST-008 — Ready for Procurement

A requisição foi entregue oficialmente ao módulo Procurement.

A partir daqui:

Compras assume responsabilidade.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Registrar entrega; entrar na fila do comprador |
| Exit Actions | Vínculo total a Pedidos de Compra ou encerramento justificado |
| Eventos aceitos | ClosePurchaseRequisition (quando todos os itens atendidos ou encerrados) |
| Eventos rejeitados | Todos os comandos de edição, aprovação e cancelamento |
| Guard Conditions | Close: todos os itens vinculados a OC ou encerrados com motivo (Jornada, etapa 15) |
| Side Effects | Timeline; indicadores de fila de Compras |
| SLA | Conforme SLA do processo de Compras |
| Responsável | Comprador |
| Auditoria | Entrega em Compras com timestamp |

---

## ST-009 — Cancelled

Estado terminal.

Não poderá retornar.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar PurchaseRequisitionCancelled; encerrar workflow; cancelar aprovações pendentes; notificar participantes |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum |
| Eventos rejeitados | Todos |
| Guard Conditions | — |
| Side Effects | Timeline final, auditoria, indicadores de cancelamento |
| SLA | — |
| Responsável | Sistema |
| Auditoria | Motivo (PR-BR-051), usuário, IP, dispositivo |

---

## ST-010 — Closed

Estado final.

Utilizado quando todo processo termina.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar PurchaseRequisitionClosed; consolidar indicadores do ciclo |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum |
| Eventos rejeitados | Todos |
| Guard Conditions | — |
| Side Effects | KPIs de Lead Time consolidados (Analytics) |
| SLA | — |
| Responsável | Sistema |
| Auditoria | Encerramento com resumo do ciclo |

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

| Origem | Destino | Permitido | Guard Condition | Evento |
|---------|----------|-----------|-----------------|--------|
| Draft | Submitted | Sim | PR-BR-020/021 | PurchaseRequisitionSubmitted |
| Draft | Cancelled | Sim | Motivo obrigatório | PurchaseRequisitionCancelled |
| Submitted | Under Validation | Sim | Automático | ValidationStarted |
| Under Validation | Waiting Approval | Sim | Validações OK | ApprovalStarted |
| Under Validation | Returned for Adjustment | Sim | Validação falhou | PurchaseRequisitionReturned |
| Waiting Approval | Approved | Sim | Todas as etapas concluídas | PurchaseRequisitionApproved |
| Waiting Approval | Rejected | Sim | Motivo obrigatório | PurchaseRequisitionRejected |
| Rejected | Returned for Adjustment | Configurável | `allow-resubmit-rejected` | PurchaseRequisitionReturned |
| Returned for Adjustment | Submitted | Sim | Revalidação OK | PurchaseRequisitionSubmitted |
| Approved | Ready for Procurement | Sim | Automático | ProcurementStarted |
| Approved | Cancelled | Configurável | `cancel-after-approval` + sem OC | PurchaseRequisitionCancelled |
| Ready for Procurement | Closed | Sim | Itens atendidos ou encerrados | RequisitionClosed |

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
