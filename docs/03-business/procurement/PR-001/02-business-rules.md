# PR-001-02 — Business Rules

> Este documento define as regras de negócio que governam o módulo de Solicitação de Compra (Purchase Requisition).

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

# 3. Regras Gerais

## PR-BR-001 – Identificador Único

**Tipo:** Obrigatória

Toda Solicitação de Compra deverá possuir um identificador único gerado automaticamente pelo sistema.

---

## PR-BR-002 – Empresa

**Tipo:** Obrigatória

Toda solicitação pertence obrigatoriamente a uma empresa cadastrada.

---

## PR-BR-003 – Unidade

**Tipo:** Configurável

A organização poderá exigir que cada solicitação esteja vinculada a uma unidade operacional.

---

## PR-BR-004 – Solicitante

**Tipo:** Obrigatória

Toda solicitação deverá possuir exatamente um solicitante responsável.

---

## PR-BR-005 – Data de Criação

**Tipo:** Obrigatória

A data e hora da criação deverão ser registradas automaticamente.

---

## PR-BR-006 – Status Inicial

**Tipo:** Obrigatória

Toda nova solicitação será criada no estado:

**Draft (Rascunho)**

---

# 4. Regras dos Itens

## PR-BR-010 – Quantidade

A quantidade deverá ser maior que zero.

Tipo:

Obrigatória

---

## PR-BR-011 – Unidade de Medida

Todo item deverá possuir unidade de medida.

Tipo:

Obrigatória

---

## PR-BR-012 – Descrição

Todo item deverá possuir descrição.

Tipo:

Obrigatória

---

## PR-BR-013 – Categoria

A organização poderá tornar obrigatória a categoria do item.

Tipo:

Configurável

---

## PR-BR-014 – Centro de Custo por Item

O centro de custo poderá ser informado por item ou pela solicitação inteira.

Tipo:

Configurável

---

## PR-BR-015 – Projeto

O projeto poderá ser obrigatório conforme parametrização.

Tipo:

Configurável

---

# 5. Regras de Validação

## PR-BR-020 – Campos Obrigatórios

Uma solicitação somente poderá ser enviada para aprovação quando todos os campos obrigatórios estiverem preenchidos.

---

## PR-BR-021 – Pelo Menos Um Item

Uma solicitação deverá possuir pelo menos um item.

---

## PR-BR-022 – Justificativa

A justificativa poderá ser obrigatória dependendo do tipo de compra.

Tipo:

Configurável

---

## PR-BR-023 – Anexos

Anexos poderão ser obrigatórios para categorias específicas.

Tipo:

Configurável

---

# 6. Regras de Aprovação

## PR-BR-030 – Workflow

Toda solicitação deverá seguir um fluxo de aprovação.

Tipo:

Obrigatória

---

## PR-BR-031 – Aprovação Automática

Solicitações abaixo de determinado valor poderão ser aprovadas automaticamente.

Tipo:

Configurável

---

## PR-BR-032 – Múltiplos Aprovadores

O sistema deverá permitir múltiplos níveis de aprovação.

Tipo:

Configurável

---

## PR-BR-033 – Aprovação Sequencial

Os aprovadores poderão atuar em sequência.

Tipo:

Configurável

---

## PR-BR-034 – Aprovação Paralela

O workflow poderá permitir aprovações paralelas.

Tipo:

Configurável

---

## PR-BR-035 – Delegação

O aprovador poderá delegar sua aprovação conforme política da empresa.

Tipo:

Configurável

---

# 7. Regras de Alteração

## PR-BR-040 – Edição

Somente solicitações em Draft poderão ser editadas livremente.

---

## PR-BR-041 – Alteração Após Aprovação

Após aprovação, somente usuários autorizados poderão alterar informações.

---

## PR-BR-042 – Reenvio

Solicitações rejeitadas poderão ser corrigidas e reenviadas.

Tipo:

Configurável

---

# 8. Regras de Cancelamento

## PR-BR-050 – Cancelamento

Solicitações poderão ser canceladas somente enquanto ainda não gerarem Pedido de Compra.

---

## PR-BR-051 – Justificativa de Cancelamento

Toda solicitação cancelada deverá registrar o motivo.

---

# 9. Regras de Auditoria

## PR-BR-060 – Histórico

Toda alteração deverá gerar registro de auditoria.

---

## PR-BR-061 – Timeline

Todo evento deverá aparecer na Timeline.

---

## PR-BR-062 – Imutabilidade

Registros de auditoria não poderão ser alterados.

---

# 10. Regras de Segurança

## PR-BR-070 – Permissões

O acesso deverá respeitar os perfis definidos.

---

## PR-BR-071 – Multiempresa

Usuários somente visualizarão empresas autorizadas.

---

## PR-BR-072 – Segregação

Um usuário poderá exercer diferentes papéis conforme a empresa ou unidade.

---

# 11. Regras de Performance

## PR-BR-080

O sistema deverá permitir pesquisa por:

- Número
- Solicitante
- Centro de custo
- Data
- Status
- Empresa

---

## PR-BR-081

Listagens deverão suportar paginação.

---

## PR-BR-082

Filtros deverão ser combináveis.

---

# 12. Matriz de Rastreabilidade

| Regra | Casos de Uso | API | Testes | Estado |
|--------|--------------|-----|--------|---------|
| PR-BR-001 | UC-001 | POST /purchase-requisitions | TC-001 | Draft |
| PR-BR-020 | UC-002 | POST /submit | TC-020 | Submitted |
| PR-BR-030 | UC-003 | POST /approve | TC-030 | In Approval |
| PR-BR-050 | UC-005 | POST /cancel | TC-050 | Cancelled |

---

# 13. Dependências

Este documento é referência obrigatória para:

- State Machine
- BPMN
- Use Cases
- API
- Banco de Dados
- Testes
- UX
- Workflow
