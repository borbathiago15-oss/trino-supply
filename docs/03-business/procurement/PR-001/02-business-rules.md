# PR-001-02 — Business Rules

> Este documento define as regras de negócio que governam o módulo de Solicitação de Compra (Purchase Requisition).

**Versão:** 1.1.0
**Status:** 🟢 Approved

---

# 1. Objetivo

Estabelecer todas as regras funcionais e operacionais para criação, edição, aprovação, cancelamento e processamento das Solicitações de Compra.

Todas as funcionalidades do sistema deverão respeitar estas regras.

---

# 2. Classificação das Regras

As regras são classificadas em:

| Tipo | Descrição |
|------|-----------|
| Obrigatória | Sempre aplicada. Não pode ser desativada. |
| Configurável | Pode ser alterada pelo administrador conforme a política da empresa. |
| Opcional | Pode ou não ser utilizada pela organização. |

---

# 3. Convenções deste Documento

Cada regra possui ficha completa com: código, nome, descrição, tipo, validação, mensagem de erro, código do erro, evento, caso de uso, API, caso de teste, configuração e observações.

* **Códigos de erro:** padrão `PR-ERR-XXX`, retornados pela API com `application/problem+json` (RFC 7807).
* **Configuração:** regras configuráveis são parametrizadas via Configuration (FD-001-10), por empresa.
* **Eventos:** sempre eventos de negócio (ADR-010).

---

# 4. Regras Gerais

## PR-BR-001 — Identificador Único

| Campo | Valor |
|-------|-------|
| Código | PR-BR-001 |
| Nome | Identificador Único |
| Descrição | Toda Solicitação de Compra deverá possuir um identificador único gerado automaticamente pelo sistema. |
| Tipo | Obrigatória |
| Validação | UUID gerado no momento da criação; número sequencial único por empresa. |
| Mensagem de erro | "Não foi possível gerar o identificador da solicitação." |
| Código do erro | PR-ERR-001 |
| Evento | PurchaseRequisitionCreated |
| Caso de uso | UC-001 |
| API | POST /purchase-requisitions |
| Caso de teste | TC-001 |
| Configuração | Formato da máscara do número (ex.: PR-AAAA-NNNNNN) por empresa. |
| Observações | O número é único por empresa (constraint PR-001-11). |

---

## PR-BR-002 — Empresa

| Campo | Valor |
|-------|-------|
| Código | PR-BR-002 |
| Nome | Empresa Obrigatória |
| Descrição | Toda solicitação pertence obrigatoriamente a uma empresa cadastrada. |
| Tipo | Obrigatória |
| Validação | Empresa existente e ativa no Organization (FD-001-02); usuário autorizado para a empresa. |
| Mensagem de erro | "Empresa inválida, inativa ou não autorizada." |
| Código do erro | PR-ERR-002 |
| Evento | PurchaseRequisitionCreated |
| Caso de uso | UC-001 |
| API | POST /purchase-requisitions |
| Caso de teste | TC-002 |
| Configuração | Não configurável. |
| Observações | Nenhuma consulta pode ignorar o filtro de empresa (PR-BR-071). |

---

## PR-BR-003 — Unidade

| Campo | Valor |
|-------|-------|
| Código | PR-BR-003 |
| Nome | Vínculo com Unidade |
| Descrição | A organização poderá exigir que cada solicitação esteja vinculada a uma unidade operacional. |
| Tipo | Configurável |
| Validação | Quando ativa: unidade existente, ativa e pertencente à empresa da solicitação. |
| Mensagem de erro | "Unidade obrigatória, inválida ou inativa." |
| Código do erro | PR-ERR-003 |
| Evento | PurchaseRequisitionCreated |
| Caso de uso | UC-001 |
| API | POST /purchase-requisitions |
| Caso de teste | TC-003 |
| Configuração | `procurement.pr.require-business-unit` (true/false), por empresa. |
| Observações | Padrão: exigido. |

---

## PR-BR-004 — Solicitante

| Campo | Valor |
|-------|-------|
| Código | PR-BR-004 |
| Nome | Solicitante Responsável |
| Descrição | Toda solicitação deverá possuir exatamente um solicitante responsável. |
| Tipo | Obrigatória |
| Validação | Usuário autenticado e ativo no IAM; solicitante = usuário da sessão. |
| Mensagem de erro | "Solicitante inválido ou inativo." |
| Código do erro | PR-ERR-004 |
| Evento | PurchaseRequisitionCreated |
| Caso de uso | UC-001 |
| API | POST /purchase-requisitions |
| Caso de teste | TC-004 |
| Configuração | Não configurável. |
| Observações | Invariante do Aggregate (PR-001-04, seção 7). |

---

## PR-BR-005 — Data de Criação

| Campo | Valor |
|-------|-------|
| Código | PR-BR-005 |
| Nome | Registro de Data e Hora |
| Descrição | A data e hora da criação deverão ser registradas automaticamente. |
| Tipo | Obrigatória |
| Validação | Timestamp UTC gerado pelo servidor; nunca informado pelo cliente. |
| Mensagem de erro | — (não aplicável; automático) |
| Código do erro | — |
| Evento | PurchaseRequisitionCreated |
| Caso de uso | UC-001 |
| API | POST /purchase-requisitions |
| Caso de teste | TC-005 |
| Configuração | Não configurável. |
| Observações | Timezone UTC conforme convenção PR-001-11. |

---

## PR-BR-006 — Status Inicial

| Campo | Valor |
|-------|-------|
| Código | PR-BR-006 |
| Nome | Status Inicial Draft |
| Descrição | Toda nova solicitação será criada no estado Draft (Rascunho). |
| Tipo | Obrigatória |
| Validação | Estado inicial fixo na factory do Aggregate (PR-001-04). |
| Mensagem de erro | — (não aplicável; automático) |
| Código do erro | — |
| Evento | PurchaseRequisitionCreated |
| Caso de uso | UC-001 |
| API | POST /purchase-requisitions |
| Caso de teste | TC-006 |
| Configuração | Não configurável. |
| Observações | Conforme State Machine ST-001 (PR-001-03). |

---

# 5. Regras dos Itens

## PR-BR-010 — Quantidade

| Campo | Valor |
|-------|-------|
| Código | PR-BR-010 |
| Nome | Quantidade Positiva |
| Descrição | A quantidade deverá ser maior que zero. |
| Tipo | Obrigatória |
| Validação | `quantity > 0`, NUMERIC(18,4); validação de domínio + check constraint no banco. |
| Mensagem de erro | "A quantidade deve ser maior que zero." |
| Código do erro | PR-ERR-010 |
| Evento | ItemAdded |
| Caso de uso | UC-002 |
| API | POST /purchase-requisitions/{id}/items |
| Caso de teste | TC-010 |
| Configuração | Não configurável. |
| Observações | Value Object `Quantity` garante a invariante. |

---

## PR-BR-011 — Unidade de Medida

| Campo | Valor |
|-------|-------|
| Código | PR-BR-011 |
| Nome | Unidade de Medida Obrigatória |
| Descrição | Todo item deverá possuir unidade de medida. |
| Tipo | Obrigatória |
| Validação | Unidade existente no Master Data (FD-001-09). |
| Mensagem de erro | "Unidade de medida obrigatória ou inválida." |
| Código do erro | PR-ERR-011 |
| Evento | ItemAdded |
| Caso de uso | UC-002 |
| API | POST /purchase-requisitions/{id}/items |
| Caso de teste | TC-011 |
| Configuração | Lista de unidades no Master Data. |
| Observações | — |

---

## PR-BR-012 — Descrição

| Campo | Valor |
|-------|-------|
| Código | PR-BR-012 |
| Nome | Descrição Obrigatória |
| Descrição | Todo item deverá possuir descrição. |
| Tipo | Obrigatória |
| Validação | Texto não vazio após trim; tamanho mínimo configurável. |
| Mensagem de erro | "A descrição do item é obrigatória." |
| Código do erro | PR-ERR-012 |
| Evento | ItemAdded |
| Caso de uso | UC-002 |
| API | POST /purchase-requisitions/{id}/items |
| Caso de teste | TC-012 |
| Configuração | `procurement.pr.item-description-min-length` (padrão: 3). |
| Observações | — |

---

## PR-BR-013 — Categoria

| Campo | Valor |
|-------|-------|
| Código | PR-BR-013 |
| Nome | Categoria do Item |
| Descrição | A organização poderá tornar obrigatória a categoria do item. |
| Tipo | Configurável |
| Validação | Quando ativa: categoria existente no Master Data. |
| Mensagem de erro | "Categoria obrigatória ou inválida." |
| Código do erro | PR-ERR-013 |
| Evento | ItemAdded |
| Caso de uso | UC-002 |
| API | POST /purchase-requisitions/{id}/items |
| Caso de teste | TC-013 |
| Configuração | `procurement.pr.require-category` (true/false), por empresa. |
| Observações | Categoria também pode exigir anexos (PR-BR-023). |

---

## PR-BR-014 — Centro de Custo por Item

| Campo | Valor |
|-------|-------|
| Código | PR-BR-014 |
| Nome | Centro de Custo por Item ou por Solicitação |
| Descrição | O centro de custo poderá ser informado por item ou pela solicitação inteira. |
| Tipo | Configurável |
| Validação | Conforme modo configurado: no cabeçalho ou em cada item; centro de custo ativo (ORG-BR-007). |
| Mensagem de erro | "Centro de custo obrigatório, inválido ou inativo." |
| Código do erro | PR-ERR-014 |
| Evento | ItemAdded / PurchaseRequisitionUpdated |
| Caso de uso | UC-001 / UC-002 |
| API | POST /purchase-requisitions · POST .../items |
| Caso de teste | TC-014 |
| Configuração | `procurement.pr.cost-center-mode` (`header` ou `item`), por empresa. |
| Observações | Modo `item` exige centro de custo em todos os itens. |

---

## PR-BR-015 — Projeto

| Campo | Valor |
|-------|-------|
| Código | PR-BR-015 |
| Nome | Vínculo com Projeto |
| Descrição | O projeto poderá ser obrigatório conforme parametrização. |
| Tipo | Configurável |
| Validação | Quando ativa: projeto existente, ativo e dentro da vigência (ORG-BR-005). |
| Mensagem de erro | "Projeto obrigatório, inválido ou encerrado." |
| Código do erro | PR-ERR-015 |
| Evento | PurchaseRequisitionCreated / ItemAdded |
| Caso de uso | UC-001 / UC-002 |
| API | POST /purchase-requisitions · POST .../items |
| Caso de teste | TC-015 |
| Configuração | `procurement.pr.require-project` (true/false), por empresa. |
| Observações | — |

---

# 6. Regras de Validação

## PR-BR-020 — Campos Obrigatórios

| Campo | Valor |
|-------|-------|
| Código | PR-BR-020 |
| Nome | Completude para Submissão |
| Descrição | Uma solicitação somente poderá ser enviada para aprovação quando todos os campos obrigatórios estiverem preenchidos. |
| Tipo | Obrigatória |
| Validação | Specification `ReadyForSubmission` (PR-001-04) avalia todos os campos conforme configuração vigente. |
| Mensagem de erro | "Existem campos obrigatórios pendentes: {lista}." |
| Código do erro | PR-ERR-020 |
| Evento | PurchaseRequisitionSubmitted (sucesso) / PurchaseRequisitionReturned (falha) |
| Caso de uso | UC-003 |
| API | POST /purchase-requisitions/{id}/submit |
| Caso de teste | TC-020 |
| Configuração | Composição das regras configuráveis (PR-BR-003/013/014/015/022/023). |
| Observações | Executada no estado Under Validation (ST-003). |

---

## PR-BR-021 — Pelo Menos Um Item

| Campo | Valor |
|-------|-------|
| Código | PR-BR-021 |
| Nome | Pelo Menos Um Item |
| Descrição | Uma solicitação deverá possuir pelo menos um item. |
| Tipo | Obrigatória |
| Validação | `items.Count >= 1` na submissão; invariante do Aggregate. |
| Mensagem de erro | "A solicitação deve possuir pelo menos um item." |
| Código do erro | PR-ERR-021 |
| Evento | PurchaseRequisitionSubmitted |
| Caso de uso | UC-003 |
| API | POST /purchase-requisitions/{id}/submit |
| Caso de teste | TC-021 |
| Configuração | Não configurável. |
| Observações | — |

---

## PR-BR-022 — Justificativa

| Campo | Valor |
|-------|-------|
| Código | PR-BR-022 |
| Nome | Justificativa |
| Descrição | A justificativa poderá ser obrigatória dependendo do tipo de compra. |
| Tipo | Configurável |
| Validação | Quando ativa para o tipo: texto não vazio; compras emergenciais sempre exigem. |
| Mensagem de erro | "A justificativa é obrigatória para este tipo de compra." |
| Código do erro | PR-ERR-022 |
| Evento | PurchaseRequisitionSubmitted |
| Caso de uso | UC-003 |
| API | POST /purchase-requisitions/{id}/submit |
| Caso de teste | TC-022 |
| Configuração | `procurement.pr.justification-required` por tipo de compra (normal/emergencial/projeto). |
| Observações | — |

---

## PR-BR-023 — Anexos

| Campo | Valor |
|-------|-------|
| Código | PR-BR-023 |
| Nome | Anexos Obrigatórios por Categoria |
| Descrição | Anexos poderão ser obrigatórios para categorias específicas. |
| Tipo | Configurável |
| Validação | Quando a categoria exige: Document Collection da solicitação com ao menos um documento (FD-001-03). |
| Mensagem de erro | "Esta categoria exige ao menos um anexo." |
| Código do erro | PR-ERR-023 |
| Evento | PurchaseRequisitionSubmitted / AttachmentAdded |
| Caso de uso | UC-003 / UC-010 |
| API | POST /purchase-requisitions/{id}/submit · POST .../attachments |
| Caso de teste | TC-023 |
| Configuração | `procurement.pr.attachment-required-categories` (lista de categorias). |
| Observações | Anexos seguem as regras do Document Management (DM-BR-003/004). |

---

# 7. Regras de Aprovação

## PR-BR-030 — Workflow

| Campo | Valor |
|-------|-------|
| Código | PR-BR-030 |
| Nome | Workflow Obrigatório |
| Descrição | Toda solicitação deverá seguir um fluxo de aprovação. |
| Tipo | Obrigatória |
| Validação | Workflow Engine (FD-001-04) localiza fluxo aplicável ao contexto (empresa, valor, categoria, centro de custo). |
| Mensagem de erro | "Nenhum workflow aplicável foi encontrado para esta solicitação." |
| Código do erro | PR-ERR-030 |
| Evento | ApprovalStarted |
| Caso de uso | UC-003 |
| API | POST /purchase-requisitions/{id}/submit |
| Caso de teste | TC-030 |
| Configuração | Fluxos definidos no Workflow Engine, por empresa. |
| Observações | Exceção documentada em PR-001-06, seção 11. |

---

## PR-BR-031 — Aprovação Automática

| Campo | Valor |
|-------|-------|
| Código | PR-BR-031 |
| Nome | Aprovação Automática por Valor |
| Descrição | Solicitações abaixo de determinado valor poderão ser aprovadas automaticamente. |
| Tipo | Configurável |
| Validação | Quando ativa: `total_estimated_value <= limite` aprova sem workflow humano. |
| Mensagem de erro | — (não aplicável) |
| Código do erro | — |
| Evento | PurchaseRequisitionApproved |
| Caso de uso | UC-003 |
| API | POST /purchase-requisitions/{id}/submit |
| Caso de teste | TC-031 |
| Configuração | `procurement.pr.auto-approve-limit` (valor; 0 = desativado), por empresa. |
| Observações | A aprovação automática registra o sistema como aprovador na auditoria. |

---

## PR-BR-032 — Múltiplos Aprovadores

| Campo | Valor |
|-------|-------|
| Código | PR-BR-032 |
| Nome | Múltiplos Níveis de Aprovação |
| Descrição | O sistema deverá permitir múltiplos níveis de aprovação. |
| Tipo | Configurável |
| Validação | Workflow com N níveis; aprovação final somente após todos os níveis. |
| Mensagem de erro | — (não aplicável) |
| Código do erro | — |
| Evento | ApprovalStarted / PurchaseRequisitionApproved |
| Caso de uso | UC-004 |
| API | POST /purchase-requisitions/{id}/approvals/{approvalId}/approve |
| Caso de teste | TC-032 |
| Configuração | Definição de níveis no Workflow Engine. |
| Observações | — |

---

## PR-BR-033 — Aprovação Sequencial

| Campo | Valor |
|-------|-------|
| Código | PR-BR-033 |
| Nome | Aprovação Sequencial |
| Descrição | Os aprovadores poderão atuar em sequência. |
| Tipo | Configurável |
| Validação | Nível N+1 só é notificado após conclusão do nível N. |
| Mensagem de erro | "Existe um nível de aprovação anterior pendente." |
| Código do erro | PR-ERR-033 |
| Evento | ApprovalStarted |
| Caso de uso | UC-004 |
| API | POST .../approvals/{approvalId}/approve |
| Caso de teste | TC-033 |
| Configuração | Modo do workflow: `sequential`. |
| Observações | — |

---

## PR-BR-034 — Aprovação Paralela

| Campo | Valor |
|-------|-------|
| Código | PR-BR-034 |
| Nome | Aprovação Paralela |
| Descrição | O workflow poderá permitir aprovações paralelas. |
| Tipo | Configurável |
| Validação | Aprovadores do mesmo nível atuam simultaneamente; quórum conforme fluxo. |
| Mensagem de erro | — (não aplicável) |
| Código do erro | — |
| Evento | ApprovalStarted |
| Caso de uso | UC-004 |
| API | POST .../approvals/{approvalId}/approve |
| Caso de teste | TC-034 |
| Configuração | Modo do workflow: `parallel` + quórum. |
| Observações | — |

---

## PR-BR-035 — Delegação

| Campo | Valor |
|-------|-------|
| Código | PR-BR-035 |
| Nome | Delegação de Aprovação |
| Descrição | O aprovador poderá delegar sua aprovação conforme política da empresa. |
| Tipo | Configurável |
| Validação | Delegação ativa no IAM com início e fim (IAM-BR-007); delegado dentro do escopo. |
| Mensagem de erro | "Delegação inválida ou fora da vigência." |
| Código do erro | PR-ERR-035 |
| Evento | ApprovalDelegated |
| Caso de uso | UC-004 |
| API | POST /purchase-requisitions/{id}/approvals/{approvalId}/delegate |
| Caso de teste | TC-035 |
| Configuração | `iam.delegation-enabled` (true/false), por empresa. |
| Observações | Toda delegação é auditada (PR-001-09, seção 9). |

---

# 8. Regras de Alteração

## PR-BR-040 — Edição

| Campo | Valor |
|-------|-------|
| Código | PR-BR-040 |
| Nome | Edição Somente em Draft |
| Descrição | Somente solicitações em Draft poderão ser editadas livremente. |
| Tipo | Obrigatória |
| Validação | Estado atual = Draft (ST-001) ou Returned for Adjustment (ST-007, campos permitidos). |
| Mensagem de erro | "A solicitação não pode ser editada no estado atual." |
| Código do erro | PR-ERR-040 |
| Evento | PurchaseRequisitionUpdated |
| Caso de uso | UC-001 / UC-002 |
| API | PATCH /purchase-requisitions/{id} |
| Caso de teste | TC-040 |
| Configuração | Não configurável. |
| Observações | Restrição da State Machine (PR-001-03, seção 9). |

---

## PR-BR-041 — Alteração Após Aprovação

| Campo | Valor |
|-------|-------|
| Código | PR-BR-041 |
| Nome | Alteração Após Aprovação |
| Descrição | Após aprovação, somente usuários autorizados poderão alterar informações. |
| Tipo | Obrigatória |
| Validação | Permissão específica de administração; alterações não estruturais (nunca itens/valores). |
| Mensagem de erro | "Alteração não permitida após aprovação." |
| Código do erro | PR-ERR-041 |
| Evento | PurchaseRequisitionUpdated |
| Caso de uso | — (operação administrativa) |
| API | PATCH /purchase-requisitions/{id} |
| Caso de teste | TC-041 |
| Configuração | Não configurável. |
| Observações | Invariante: requisição aprovada não sofre alterações estruturais (PR-001-04, seção 7). |

---

## PR-BR-042 — Reenvio

| Campo | Valor |
|-------|-------|
| Código | PR-BR-042 |
| Nome | Reenvio de Rejeitadas |
| Descrição | Solicitações rejeitadas poderão ser corrigidas e reenviadas. |
| Tipo | Configurável |
| Validação | Quando ativa: transição Rejected → Returned for Adjustment → Submitted (PR-001-03, seção 7). |
| Mensagem de erro | "Reenvio não permitido pela política da empresa." |
| Código do erro | PR-ERR-042 |
| Evento | PurchaseRequisitionReturned / PurchaseRequisitionSubmitted |
| Caso de uso | UC-006 |
| API | POST /purchase-requisitions/{id}/submit |
| Caso de teste | TC-042 |
| Configuração | `procurement.pr.allow-resubmit-rejected` (true/false), por empresa. |
| Observações | Matriz de transições: Rejected → Returned for Adjustment = Configurável. |

---

# 9. Regras de Cancelamento

## PR-BR-050 — Cancelamento

| Campo | Valor |
|-------|-------|
| Código | PR-BR-050 |
| Nome | Cancelamento Antes do Pedido |
| Descrição | Solicitações poderão ser canceladas somente enquanto ainda não gerarem Pedido de Compra. |
| Tipo | Obrigatória |
| Validação | Nenhum item vinculado a Pedido de Compra; estado permite cancelamento (PR-001-03, seção 7). |
| Mensagem de erro | "A solicitação não pode ser cancelada: já possui Pedido de Compra." |
| Código do erro | PR-ERR-050 |
| Evento | PurchaseRequisitionCancelled |
| Caso de uso | UC-007 |
| API | POST /purchase-requisitions/{id}/cancel |
| Caso de teste | TC-050 |
| Configuração | Cancelamento após aprovação: `procurement.pr.cancel-after-approval` (true/false). |
| Observações | Cancelamento é estado terminal (ST-009). |

---

## PR-BR-051 — Justificativa de Cancelamento

| Campo | Valor |
|-------|-------|
| Código | PR-BR-051 |
| Nome | Motivo do Cancelamento |
| Descrição | Toda solicitação cancelada deverá registrar o motivo. |
| Tipo | Obrigatória |
| Validação | Motivo não vazio no comando de cancelamento. |
| Mensagem de erro | "O motivo do cancelamento é obrigatório." |
| Código do erro | PR-ERR-051 |
| Evento | PurchaseRequisitionCancelled |
| Caso de uso | UC-007 |
| API | POST /purchase-requisitions/{id}/cancel |
| Caso de teste | TC-051 |
| Configuração | Não configurável. |
| Observações | Motivo registrado na auditoria e na Timeline. |

---

# 10. Regras de Auditoria

## PR-BR-060 — Histórico

| Campo | Valor |
|-------|-------|
| Código | PR-BR-060 |
| Nome | Registro de Histórico |
| Descrição | Toda alteração deverá gerar registro de auditoria. |
| Tipo | Obrigatória |
| Validação | Audit Service (FD-001-06) recebe todos os eventos do Aggregate. |
| Mensagem de erro | — (falha de auditoria bloqueia a operação) |
| Código do erro | PR-ERR-060 |
| Evento | Todos |
| Caso de uso | Transversal |
| API | Transversal |
| Caso de teste | TC-060 |
| Configuração | Não configurável. |
| Observações | Logs imutáveis (princípio do produto). |

---

## PR-BR-061 — Timeline

| Campo | Valor |
|-------|-------|
| Código | PR-BR-061 |
| Nome | Atualização da Timeline |
| Descrição | Todo evento deverá aparecer na Timeline. |
| Tipo | Obrigatória |
| Validação | Timeline Service (FD-001-07) consome todos os eventos (POL-002). |
| Mensagem de erro | — (processamento assíncrono com retry) |
| Código do erro | — |
| Evento | Todos |
| Caso de uso | UC-009 |
| API | GET /purchase-requisitions/{id}/timeline |
| Caso de teste | TC-061 |
| Configuração | Não configurável. |
| Observações | — |

---

## PR-BR-062 — Imutabilidade

| Campo | Valor |
|-------|-------|
| Código | PR-BR-062 |
| Nome | Imutabilidade da Auditoria |
| Descrição | Registros de auditoria não poderão ser alterados. |
| Tipo | Obrigatória |
| Validação | Audit Service sem operações de update/delete; armazenamento append-only. |
| Mensagem de erro | — (não aplicável) |
| Código do erro | — |
| Evento | — |
| Caso de uso | Transversal |
| API | Transversal |
| Caso de teste | TC-062 |
| Configuração | Não configurável. |
| Observações | Conformidade LGPD e boas práticas (SEC-001). |

---

# 11. Regras de Segurança

## PR-BR-070 — Permissões

| Campo | Valor |
|-------|-------|
| Código | PR-BR-070 |
| Nome | Controle de Acesso por Perfil |
| Descrição | O acesso deverá respeitar os perfis definidos. |
| Tipo | Obrigatória |
| Validação | RBAC + ABAC + Escopo (PR-001-09); toda decisão auditada (IAM-BR-004). |
| Mensagem de erro | "Você não possui permissão para esta operação." |
| Código do erro | PR-ERR-070 |
| Evento | — (decisão de autorização registrada no IAM) |
| Caso de uso | Transversal |
| API | Transversal |
| Caso de teste | TC-070 |
| Configuração | Papéis e permissões no IAM. |
| Observações | — |

---

## PR-BR-071 — Multiempresa

| Campo | Valor |
|-------|-------|
| Código | PR-BR-071 |
| Nome | Isolamento Multiempresa |
| Descrição | Usuários somente visualizarão empresas autorizadas. |
| Tipo | Obrigatória |
| Validação | Filtro `company_id` obrigatório em toda consulta + escopo do IAM. |
| Mensagem de erro | "Recurso não encontrado." (não revela existência fora do escopo) |
| Código do erro | PR-ERR-071 |
| Evento | — |
| Caso de uso | UC-008 |
| API | GET /purchase-requisitions |
| Caso de teste | TC-071 |
| Configuração | Não configurável. |
| Observações | Proteção contra IDOR (SEC-003). |

---

## PR-BR-072 — Segregação

| Campo | Valor |
|-------|-------|
| Código | PR-BR-072 |
| Nome | Papéis por Contexto |
| Descrição | Um usuário poderá exercer diferentes papéis conforme a empresa ou unidade. |
| Tipo | Obrigatória |
| Validação | Roles variam por empresa (IAM-BR-006); escopo organizacional sempre aplicado (IAM-BR-008). |
| Mensagem de erro | "Papel não disponível neste contexto organizacional." |
| Código do erro | PR-ERR-072 |
| Evento | — |
| Caso de uso | Transversal |
| API | Transversal |
| Caso de teste | TC-072 |
| Configuração | Atribuições no IAM. |
| Observações | — |

---

# 12. Regras de Performance

## PR-BR-080 — Pesquisa

| Campo | Valor |
|-------|-------|
| Código | PR-BR-080 |
| Nome | Critérios de Pesquisa |
| Descrição | O sistema deverá permitir pesquisa por número, solicitante, centro de custo, data, status e empresa. |
| Tipo | Obrigatória |
| Validação | Índices correspondentes no banco (PR-001-11, seção 6). |
| Mensagem de erro | — (não aplicável) |
| Código do erro | — |
| Evento | — |
| Caso de uso | UC-008 |
| API | GET /purchase-requisitions |
| Caso de teste | TC-080 |
| Configuração | Não configurável. |
| Observações | Filtros adicionais: unidade, projeto, prioridade (UC-008). |

---

## PR-BR-081 — Paginação

| Campo | Valor |
|-------|-------|
| Código | PR-BR-081 |
| Nome | Paginação de Listagens |
| Descrição | Listagens deverão suportar paginação. |
| Tipo | Obrigatória |
| Validação | Keyset Pagination (PR-001-11, seção 12); OFFSET elevado proibido. |
| Mensagem de erro | "Cursor de paginação inválido." |
| Código do erro | PR-ERR-081 |
| Evento | — |
| Caso de uso | UC-008 |
| API | GET /purchase-requisitions?cursor=... |
| Caso de teste | TC-081 |
| Configuração | Tamanho de página padrão e máximo por empresa. |
| Observações | — |

---

## PR-BR-082 — Filtros Combináveis

| Campo | Valor |
|-------|-------|
| Código | PR-BR-082 |
| Nome | Combinação de Filtros |
| Descrição | Filtros deverão ser combináveis. |
| Tipo | Obrigatória |
| Validação | Composição de Specifications na consulta (PR-001-04). |
| Mensagem de erro | "Combinação de filtros inválida." |
| Código do erro | PR-ERR-082 |
| Evento | — |
| Caso de uso | UC-008 |
| API | GET /purchase-requisitions |
| Caso de teste | TC-082 |
| Configuração | Não configurável. |
| Observações | — |

---

# 13. Matriz de Rastreabilidade

| Regra | Casos de Uso | API | Testes | Estado |
|--------|--------------|-----|--------|---------|
| PR-BR-001 | UC-001 | POST /purchase-requisitions | TC-001 | Draft |
| PR-BR-010 | UC-002 | POST .../items | TC-010 | Draft |
| PR-BR-020 | UC-003 | POST /submit | TC-020 | Submitted |
| PR-BR-021 | UC-003 | POST /submit | TC-021 | Submitted |
| PR-BR-030 | UC-003 | POST /submit | TC-030 | Waiting Approval |
| PR-BR-040 | UC-001/002 | PATCH /{id} | TC-040 | Draft |
| PR-BR-050 | UC-007 | POST /cancel | TC-050 | Cancelled |
| PR-BR-060/061/062 | Transversal | Transversal | TC-060/061/062 | Todos |
| PR-BR-070/071/072 | Transversal | Transversal | TC-070/071/072 | Todos |
| PR-BR-080/081/082 | UC-008 | GET /purchase-requisitions | TC-080/081/082 | Consulta |

---

# 14. Dependências

Este documento é referência obrigatória para:

- State Machine
- BPMN
- Use Cases
- API
- Banco de Dados
- Testes
- UX
- Workflow
