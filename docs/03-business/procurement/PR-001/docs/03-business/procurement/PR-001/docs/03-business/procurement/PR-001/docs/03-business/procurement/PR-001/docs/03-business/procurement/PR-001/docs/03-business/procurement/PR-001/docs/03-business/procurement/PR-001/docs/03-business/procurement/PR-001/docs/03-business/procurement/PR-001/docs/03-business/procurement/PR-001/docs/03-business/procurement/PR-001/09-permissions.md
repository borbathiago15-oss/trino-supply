# PR-001-09 — Permissions & Authorization

> Este documento define o modelo de autorização do módulo Purchase Requisition.

---

# 1. Objetivo

Garantir que apenas usuários autorizados possam visualizar, criar, editar, aprovar ou cancelar Solicitações de Compra.

O modelo de autorização deve suportar múltiplas empresas, unidades organizacionais e diferentes responsabilidades para um mesmo usuário.

---

# 2. Modelo de Autorização

O Trino Supply utiliza um modelo híbrido composto por:

- RBAC (Role-Based Access Control)
- ABAC (Attribute-Based Access Control)
- Escopo Organizacional

A decisão de acesso considera:

- Papel do usuário
- Empresa
- Unidade
- Departamento
- Centro de custo
- Estado da requisição
- Ação solicitada

---

# 3. Papéis (Roles)

## Requester

Responsável por criar e acompanhar solicitações.

---

## Approver

Responsável por aprovar ou rejeitar solicitações.

---

## Buyer

Responsável pelo processo de aquisição após a aprovação.

---

## Procurement Manager

Responsável pela gestão da equipe de Compras.

---

## System Administrator

Responsável pela configuração da plataforma.

---

## Auditor

Responsável apenas pela consulta e auditoria dos registros.

---

# 4. Ações (Permissions)

| Código | Ação |
|---------|------|
| PR-PERM-001 | Criar requisição |
| PR-PERM-002 | Editar requisição |
| PR-PERM-003 | Excluir rascunho |
| PR-PERM-004 | Enviar para aprovação |
| PR-PERM-005 | Aprovar |
| PR-PERM-006 | Rejeitar |
| PR-PERM-007 | Retornar para ajuste |
| PR-PERM-008 | Cancelar |
| PR-PERM-009 | Consultar |
| PR-PERM-010 | Exportar |
| PR-PERM-011 | Inserir comentário |
| PR-PERM-012 | Anexar documentos |
| PR-PERM-013 | Visualizar auditoria |
| PR-PERM-014 | Visualizar timeline |

---

# 5. Permissões por Papel

| Permissão | Requester | Approver | Buyer | Manager | Admin | Auditor |
|------------|-----------|-----------|--------|----------|--------|----------|
| Criar | ✔ | | | | ✔ | |
| Editar Draft | ✔ | | | | ✔ | |
| Enviar | ✔ | | | | ✔ | |
| Aprovar | | ✔ | | ✔ | ✔ | |
| Rejeitar | | ✔ | | ✔ | ✔ | |
| Cancelar | ✔* | ✔* | | ✔ | ✔ | |
| Consultar | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Exportar | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Auditoria | | | | ✔ | ✔ | ✔ |

\* Conforme política da empresa.

---

# 6. Restrições por Estado

| Estado | Editar | Aprovar | Cancelar |
|---------|---------|----------|-----------|
| Draft | ✔ | ❌ | ✔ |
| Submitted | ❌ | ❌ | ✔* |
| Waiting Approval | ❌ | ✔ | ✔* |
| Approved | ❌ | ❌ | Configurável |
| Ready for Procurement | ❌ | ❌ | ❌ |
| Cancelled | ❌ | ❌ | ❌ |
| Closed | ❌ | ❌ | ❌ |

---

# 7. Escopo Organizacional

Uma permissão pode ser limitada por:

- Empresa
- Unidade
- Departamento
- Centro de custo
- Projeto
- Categoria de compra

Exemplo:

Usuário João

Role:

Approver

Empresa:

Grupo Trino

Unidade:

João Pessoa

Centro de custo:

Logística

Esse usuário somente poderá aprovar requisições desse contexto.

---

# 8. Segregação de Funções (SoD)

O sistema deverá impedir conflitos de interesse.

Exemplos:

- O solicitante não pode aprovar sua própria requisição (salvo política específica).
- Um aprovador não pode aprovar acima de seu limite de alçada.
- Um comprador não pode alterar uma requisição aprovada sem autorização.

Essas regras deverão ser parametrizáveis conforme a política da organização.

---

# 9. Delegação

O sistema poderá permitir delegação temporária de aprovações.

A delegação deve conter:

- Delegante
- Delegado
- Data de início
- Data de término
- Motivo

Toda delegação deve ser auditada.

---

# 10. Auditoria

Toda decisão de autorização deve registrar:

- Usuário
- Papel utilizado
- Empresa
- Unidade
- Ação executada
- Resultado (Permitido/Negado)
- Data e hora

---

# 11. Dependências

Foundation

Approval Engine

Audit Service

Timeline Service

Identity & Access Management (IAM)
