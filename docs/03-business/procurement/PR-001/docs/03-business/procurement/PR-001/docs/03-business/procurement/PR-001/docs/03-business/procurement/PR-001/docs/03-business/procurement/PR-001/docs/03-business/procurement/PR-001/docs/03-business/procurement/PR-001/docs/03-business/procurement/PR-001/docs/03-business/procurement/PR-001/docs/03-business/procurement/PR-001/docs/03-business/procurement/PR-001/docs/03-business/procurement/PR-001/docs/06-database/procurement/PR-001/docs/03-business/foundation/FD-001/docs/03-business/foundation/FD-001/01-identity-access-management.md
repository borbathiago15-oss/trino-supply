# FD-001-01 — Identity & Access Management (IAM)

> O domínio Identity & Access Management (IAM) é responsável pela autenticação, autorização e gerenciamento de identidades da plataforma Trino Supply.

---

# 1. Objetivo

Centralizar toda a gestão de acesso da plataforma, garantindo segurança, rastreabilidade e controle granular de permissões.

Nenhum módulo funcional implementará regras próprias de autenticação ou autorização.

Todo acesso será realizado através do IAM.

---

# 2. Responsabilidades

O IAM é responsável por:

- Usuários
- Papéis (Roles)
- Permissões
- Políticas
- Grupos
- Sessões
- Delegações
- Escopos organizacionais
- Autenticação
- Autorização

---

# 3. Conceitos do Domínio

## User

Representa uma pessoa autenticável na plataforma.

Atributos principais:

- Id
- Nome
- E-mail
- Login
- Status
- Idioma
- Fuso horário
- Último acesso

---

## Role

Define um conjunto de responsabilidades.

Exemplos:

- Requester
- Approver
- Buyer
- Procurement Manager
- Administrator
- Auditor

Uma Role nunca concede acesso sozinha.

---

## Permission

Representa uma ação executável.

Exemplos:

- purchase-requisition.create
- purchase-requisition.edit
- purchase-requisition.submit
- purchase-requisition.approve
- supplier.create
- supplier.disable

---

## Policy

Agrupa regras de autorização.

Exemplos:

- Aprovar somente até R$ 10.000
- Visualizar apenas sua unidade
- Editar apenas documentos em Draft

---

## Scope

Define o contexto organizacional onde a permissão é válida.

Pode incluir:

- Empresa
- Unidade
- Departamento
- Centro de custo
- Projeto

---

## Group

Permite organizar usuários.

Exemplos:

- Compradores Nordeste
- Gestores Operacionais
- Diretoria Financeira

---

## Session

Representa uma autenticação ativa.

Informações:

- IP
- Navegador
- Dispositivo
- Data/Hora
- Expiração

---

# 4. Modelo de Autorização

A autorização é composta por:

```text
User
    ↓
Group (opcional)
    ↓
Role
    ↓
Permission
    ↓
Policy
    ↓
Organizational Scope
    ↓
Decision (Allow / Deny)
```

---

# 5. Fluxo de Autenticação

1. Usuário informa credenciais.
2. Sistema valida identidade.
3. Sessão é criada.
4. Permissões são carregadas.
5. Escopos organizacionais são aplicados.
6. Token de acesso é emitido.

---

# 6. Fluxo de Autorização

Sempre que uma ação é solicitada:

1. Verificar autenticação.
2. Verificar status do usuário.
3. Verificar Role.
4. Verificar Permission.
5. Verificar Policy.
6. Verificar Scope.
7. Registrar auditoria.
8. Permitir ou negar.

---

# 7. Regras de Negócio

IAM-BR-001

Usuários inativos não podem autenticar.

---

IAM-BR-002

Permissões são concedidas por Roles.

---

IAM-BR-003

Policies podem restringir permissões concedidas.

---

IAM-BR-004

Toda decisão de autorização deve ser auditada.

---

IAM-BR-005

Um usuário pode possuir múltiplas Roles.

---

IAM-BR-006

As Roles podem variar conforme a empresa.

---

IAM-BR-007

Delegações possuem data de início e término.

---

IAM-BR-008

Nenhuma permissão pode ignorar o escopo organizacional.

---

# 8. Entidades do Domínio

- User
- UserRole
- Role
- Permission
- Policy
- PolicyRule
- Group
- UserGroup
- OrganizationalScope
- Session
- Delegation

---

# 9. Eventos de Domínio

- UserCreated
- UserActivated
- UserDisabled
- UserLoggedIn
- UserLoggedOut
- RoleAssigned
- RoleRemoved
- PermissionGranted
- PermissionRevoked
- DelegationCreated
- DelegationExpired

---

# 10. Integrações

Consumidores:

- Procurement
- Supplier Management
- Contracts
- Receiving
- Analytics
- Platform

Provedores futuros:

- Microsoft Entra ID (Azure AD)
- Google Workspace
- LDAP
- Active Directory
- OpenID Connect
- OAuth 2.1

---

# 11. Requisitos Não Funcionais

- Tempo máximo de autenticação: 2 segundos.
- Tokens JWT com expiração configurável.
- Suporte a rotação de chaves.
- Auditoria obrigatória de autenticação e autorização.
- Compatível com múltiplos tenants.

---

# 12. Roadmap

Versão 1

- Login local
- RBAC + ABAC
- Escopo organizacional
- Delegações
- Auditoria

Versão 2

- MFA
- SSO
- OpenID Connect
- OAuth 2.1
- Login social corporativo

Versão 3

- Passwordless
- Passkeys (FIDO2)
- Autenticação adaptativa
- Políticas baseadas em risco
