**Documento:** PR-001-09 — Permissions & Authorization
**Versão:** 1.1.0
**Status:** Approved

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

**Detalhamento das camadas (Seção 12):** RBAC define o conjunto base de permissões por papel; ABAC refina por atributos do recurso, do sujeito e do contexto; o Escopo Organizacional restringe o universo de dados visíveis. As três camadas são avaliadas em conjunto pelo fluxo de avaliação (Seção 16).

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

**Nota:** a Permission Matrix completa por código (PR-PERM × papel × escopo) está na Seção 13.

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

---

# 12. Modelo Formal de Autorização (Enterprise)

## 12.1 RBAC — Controle de Acesso Baseado em Papéis

Cada papel (Seção 3) é um conjunto nomeado de permissões (Seção 4). Atribuições usuário→papel são **sempre escopadas** (empresa/unidade), nunca globais por padrão.

| Elemento | Definição |
| -------- | --------- |
| Role | Conjunto nomeado de permissões (ex.: `Approver`) |
| Role Assignment | Tupla `(userId, role, scope)` — ex.: `(joão, Approver, empresa=Grupo Trino, unidade=João Pessoa)` |
| Grant | Concedido e revogado apenas por System Administrator ou Procurement Manager (para papéis operacionais); toda atribuição é auditada |
| Cardinalidade | Um usuário pode ter múltiplos papéis em escopos distintos; o conjunto efetivo é a união das permissões dos papéis ativos no escopo avaliado |

## 12.2 ABAC — Controle de Acesso Baseado em Atributos

Atributos avaliados na decisão:

| Categoria | Atributos |
| --------- | --------- |
| **Sujeito** | userId, papéis ativos, alçada (`approvalLimit`), escopo organizacional atribuído, delegações vigentes |
| **Recurso** | companyId, businessUnitId, costCenterId, projectId, categoryId, status (estado da requisição), requesterId, totalEstimatedValue |
| **Ação** | Permissão solicitada (PR-PERM-xxx) |
| **Contexto** | Data/hora, canal (API/UI), delegationId (quando agindo como delegado), política da empresa (`pr.cancel.policy`, `approval.parallel.policy`) |

Regras ABAC estruturais do módulo:

- **ABAC-01 — SoD criador/aprovador:** `resource.requesterId ≠ subject.userId` para PR-PERM-005/006/007 (salvo política específica por empresa).
- **ABAC-02 — Alçada:** `resource.totalEstimatedValue ≤ subject.approvalLimit` para PR-PERM-005.
- **ABAC-03 — Estado:** ação permitida apenas nos estados definidos na Seção 6.
- **ABAC-04 — Comentários internos:** leitura de `internal = true` restrita a Approver, Buyer, Manager, Admin.
- **ABAC-05 — Titularidade:** edição em Draft (PR-PERM-002) exige `resource.requesterId = subject.userId` ou papel Admin no escopo.

## 12.3 Policies

Policies são regras declarativas avaliadas pelo motor de autorização. Cada policy combina condições RBAC + ABAC.

| Código | Policy | Efeito | Condição |
| ------ | ------ | ------ | -------- |
| POL-AUTH-001 | Criação de requisição | Permit | `PR-PERM-001 ∈ roles(subject)` ∧ `scope(subject) ⊇ resource.company/unidade` |
| POL-AUTH-002 | Edição em Draft | Permit | `PR-PERM-002` ∧ ABAC-03 (Draft) ∧ ABAC-05 (titularidade) |
| POL-AUTH-003 | Aprovação | Permit | `PR-PERM-005` ∧ ABAC-01 (SoD) ∧ ABAC-02 (alçada) ∧ nível pendente atribuído ao sujeito (direto/delegado/escalonado) |
| POL-AUTH-004 | Cancelamento | Permit | `PR-PERM-008` ∧ estado cancelável ∧ política `pr.cancel.policy` |
| POL-AUTH-005 | Comentário interno (leitura) | Permit | papel ∈ {Approver, Buyer, Manager, Admin} |
| POL-AUTH-006 | Auditoria (leitura) | Permit | `PR-PERM-013` ∧ escopo |
| POL-AUTH-007 | Deny-by-default | Deny | Qualquer condição não avaliada explicitamente como Permit |

**Princípio:** *deny by default* — a ausência de um Permit explícito resulta em negação, sempre auditada.

## 12.4 JWT Claims

Token de acesso (FD-001-01 IAM) carrega as claims consumidas pelo módulo:

| Claim | Conteúdo | Uso |
| ----- | -------- | --- |
| `sub` | userId | Identidade do sujeito |
| `iss` / `aud` | Emissor / audiência (`trino-supply`) | Validação do token |
| `exp` / `iat` / `nbf` | Validade temporal | Expiração curta (padrão 15 min; refresh token rotativo) |
| `company_ids` | Empresas às quais o usuário pertence | Filtro de escopo |
| `roles` | Papéis com escopo: `[{ role, companyId, businessUnitId? }]` | RBAC |
| `perms` | Permissões efetivas (`pr.create`, `pr.approve`, ...) | Short-circuit de avaliação |
| `approval_limit` | Alçada máxima de aprovação | ABAC-02 |
| `scope` | Scopes OAuth2 (ver 12.5) | Autorização de API |
| `jti` | Identificador único do token | Revogação/blacklist |
| `tenant_id` | Tenant da sessão | Isolamento multi-tenant |

**Regras:** tokens de terceiros/inválidos → 401; token válido sem permissão → 403; recurso fora do escopo → 404 ("Recurso não encontrado", anti-IDOR).

## 12.5 Scopes (OAuth2)

| Scope | Descrição |
| ----- | --------- |
| `pr.read` | Consulta, timeline, exportação |
| `pr.write` | Criar, editar, itens, anexos, comentários |
| `pr.submit` | Envio para aprovação |
| `pr.approve` | Aprovar, rejeitar, retornar |
| `pr.cancel` | Cancelar |
| `pr.audit` | Visualizar auditoria |

Aplicações cliente (frontend, integrações) recebem apenas os scopes estritamente necessários (least privilege).

---

# 13. Permission Matrix (completa)

| Permissão | Requester | Approver | Buyer | Manager | Admin | Auditor | Condições ABAC |
| --------- | --------- | -------- | ----- | ------- | ----- | ------- | -------------- |
| PR-PERM-001 Criar | ✔ | — | — | — | ✔ | — | Escopo empresa/unidade |
| PR-PERM-002 Editar | ✔ | — | — | — | ✔ | — | Estado Draft/Returned + titularidade (ABAC-05) |
| PR-PERM-003 Excluir rascunho | ✔ | — | — | — | ✔ | — | Estado Draft + titularidade |
| PR-PERM-004 Enviar | ✔ | — | — | — | ✔ | — | Estado Draft/Returned + titularidade |
| PR-PERM-005 Aprovar | — | ✔ | — | ✔ | ✔ | — | ABAC-01 (SoD) + ABAC-02 (alçada) + nível atribuído |
| PR-PERM-006 Rejeitar | — | ✔ | — | ✔ | ✔ | — | ABAC-01 + nível atribuído |
| PR-PERM-007 Retornar | — | ✔ | — | ✔ | ✔ | — | ABAC-01 + nível atribuído |
| PR-PERM-008 Cancelar | ✔* | ✔* | — | ✔ | ✔ | — | Estado cancelável + política `pr.cancel.policy` |
| PR-PERM-009 Consultar | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | Sempre filtrado por escopo (404 fora do escopo) |
| PR-PERM-010 Exportar | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | Escopo; exportação auditada |
| PR-PERM-011 Comentar | ✔ | ✔ | ✔ | ✔ | ✔ | — | Estado não terminal; interno conforme ABAC-04 |
| PR-PERM-012 Anexar | ✔ | ✔ | — | ✔ | ✔ | — | Estados permitidos conforme política |
| PR-PERM-013 Auditoria | — | — | — | ✔ | ✔ | ✔ | Escopo; apenas leitura |
| PR-PERM-014 Timeline | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | Escopo |

\* Conforme política da empresa.

---

# 14. Segregation of Duties (SoD) — Matriz de Conflitos

| Código | Conflito | Regra | Tratamento |
| ------ | -------- | ----- | ---------- |
| SOD-001 | Criar × Aprovar | Solicitante não aprova a própria requisição | Bloqueio (ABAC-01); redireciona nível; auditoria obrigatória |
| SOD-002 | Aprovar × Alçada | Aprovador não decide acima de `approvalLimit` | Bloqueio; escalonamento ESC-001 |
| SOD-003 | Comprar × Alterar | Buyer não altera requisição aprovada | Bloqueio (estado não editável); alterações exigem processo específico |
| SOD-004 | Delegar × Auto-delegar | Usuário não delega para si mesmo | Bloqueio na criação da delegação |
| SOD-005 | Configurar × Operar (recomendado) | Admin que configura workflow não deve ser aprovador da mesma cadeia | Alerta de conflito; política por empresa |

Exceções a SOD-001 somente via política explícita por empresa (`sod.self-approval.allow`, padrão `false`), registrada em auditoria de configuração.

---

# 15. Inheritance e Delegation

## 15.1 Inheritance (Herança de Permissões)

| Regra | Descrição |
| ----- | --------- |
| INH-001 | Papel atribuído no nível **empresa** herda para todas as unidades da empresa, salvo exceção explícita |
| INH-002 | Papel atribuído no nível **unidade** não herda para outras unidades nem para a empresa |
| INH-003 | Exceção explícita (`deny override`) em nível inferior prevalece sobre herança (avaliação do mais específico para o mais geral) |
| INH-004 | Centro de custo e projeto **restringem** o escopo herdado (interseção), nunca ampliam |
| INH-005 | Não há herança entre empresas/tenants — isolamento absoluto |

## 15.2 Delegation (Delegação)

Complementando a Seção 9:

| Regra | Descrição |
| ----- | --------- |
| DEL-001 | Delegação é escopada: herda o escopo do delegante, nunca o amplia |
| DEL-002 | Vigência obrigatória (início/fim); delegações expiradas são ignoradas automaticamente |
| DEL-003 | Delegado não pode redelegar |
| DEL-004 | SoD continua aplicável ao delegado (ex.: delegado que também é solicitante não aprova a própria requisição) |
| DEL-005 | Decisões tomadas por delegado registram `actedAs = delegate` + `delegatedBy` na auditoria e na timeline |
| DEL-006 | Delegante pode revogar a qualquer momento; revogação tem efeito imediato |
| DEL-007 | Conflito de delegações sobrepostas → prevalece a mais recente; conflito auditado |

---

# 16. Permission Evaluation Flow

```text
Request (usuário, ação, recurso)
        │
        ▼
[1] Autenticação — JWT válido? (assinatura, exp, iss, aud, tenant)
        │ Não → 401 Unauthorized
        ▼
[2] Resolução de escopo — company/unidade do recurso ∈ escopo do sujeito?
        │ Não → 404 Not Found (anti-IDOR; log de segurança)
        ▼
[3] RBAC — permissão PR-PERM-xxx ∈ papéis efetivos no escopo?
        │ Não → 403 Forbidden + auditoria de negação
        ▼
[4] ABAC — regras ABAC-01..05 e policies POL-AUTH-xxx satisfeitas?
        (SoD, alçada, estado, titularidade, política da empresa)
        │ Não → 403 Forbidden + auditoria de negação
        ▼
[5] Delegação — agindo como delegado? validar DEL-001..006
        │ Inválida → 403 Forbidden + auditoria
        ▼
[6] PERMIT — executar ação + registrar auditoria de decisão
```

**Propriedades do fluxo:**

- Deny by default (POL-AUTH-007).
- Toda negação nos passos 2–5 gera registro de auditoria (Seção 10).
- O passo 2 precede o 3 intencionalmente: não revelar a existência de recursos fora do escopo.
- Avaliação executada server-side em **todas** as requisições; o frontend apenas reflete a decisão (nunca decide).
- Cache de decisão (Redis) permitido por até 60s, com invalidação em mudança de papel, escopo ou delegação.

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | — | Arquitetura Trino | Versão inicial aprovada |
| 1.1.0 | 2026-07-30 | Arquitetura Trino | Adicionadas Seções 12–16: modelo formal RBAC/ABAC, Policies (POL-AUTH-001..007), JWT Claims, Scopes OAuth2, Permission Matrix completa, matriz SoD (SOD-001..005), Inheritance (INH-001..005), Delegation (DEL-001..007) e Permission Evaluation Flow. Todo o conteúdo das Seções 1–11 foi preservado |
