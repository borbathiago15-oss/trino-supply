# MMS-002-09 — Permissions & Authorization

**Documento:** MMS-002-09 — Permissions & Authorization
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 v1.1.0, MMS-002-02 v1.1.0, MMS-002-03 (State Machine), MMS-002-07 (Use Cases), FD-001-01 (IAM), FD-001-06 (Audit), SEC-001, SEC-003
**Referências:** PR-001-09 (padrão de formato Enterprise), GOV-001

> Este documento define o modelo de autorização do módulo Item Catalog.

---

# 1. Objetivo

Garantir que apenas usuários autorizados possam cadastrar, editar, ativar, inativar, descartar, parametrizar e consultar itens do catálogo, com recorte de visibilidade por perfil e isolamento por empresa.

O modelo deve suportar múltiplas empresas (tenants) e diferentes responsabilidades para um mesmo usuário, incluindo perfis exclusivamente operacionais (leitura de itens Ativos) e perfis de governo do catálogo (mantenedor, administrador, auditor).

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
- Estado do item (Rascunho, Ativo, Inativo, Descarte)
- Ação solicitada
- Uso do item em operações abertas (IC-BR-021)

**Detalhamento das camadas (Seção 12):** RBAC define o conjunto base de permissões por papel; ABAC refina por atributos do recurso, do sujeito e do contexto; o Escopo Organizacional restringe o universo de dados visíveis. As três camadas são avaliadas em conjunto pelo fluxo de avaliação (Seção 16).

---

# 3. Papéis (Roles)

## Catalog Maintainer (Gerente de Suprimentos)

Responsável pela manutenção do catálogo: cadastra, edita, ativa, inativa, reativa, descarta, gerencia sinônimos, imagens, grades e parâmetros.

## System Administrator

Responsável pela configuração do módulo (`materials.item.*`) e por todas as ações do mantenedor.

## Operational User (Solicitante / Almoxarifado / Gestor)

Usuário dos módulos consumidores (MMS-003, MMS-004, PR-001). Consulta o catálogo apenas em recorte operacional (itens Ativos), sem ações de escrita.

## Auditor

Responsável apenas pela consulta completa (todos os estados) e pela auditoria dos registros.

---

# 4. Ações (Permissions)

| Código | Ação |
|---------|------|
| IC-PERM-001 | Cadastrar item |
| IC-PERM-002 | Editar item |
| IC-PERM-003 | Ativar / Reativar item |
| IC-PERM-004 | Inativar / Descartar item |
| IC-PERM-005 | Gerenciar sinônimos |
| IC-PERM-006 | Consultar catálogo |
| IC-PERM-007 | Gerenciar parâmetros de reposição |
| IC-PERM-008 | Administrar configurações do módulo |
| IC-PERM-009 | Exportar catálogo |
| IC-PERM-010 | Visualizar auditoria do item |

---

# 5. Permissões por Papel

| Permissão | Maintainer | Admin | Operational | Auditor |
|-----------|-----------|-------|-------------|---------|
| Cadastrar | ✔ | ✔ | | |
| Editar | ✔ | ✔ | | |
| Ativar/Reativar | ✔ | ✔ | | |
| Inativar/Descartar | ✔ | ✔ | | |
| Sinônimos | ✔ | ✔ | | |
| Consultar | ✔ (todos os estados) | ✔ | ✔ (apenas Ativos) | ✔ (todos os estados) |
| Parâmetros | ✔ | ✔ | | |
| Configurações | | ✔ | | |
| Exportar | ✔ | ✔ | | ✔ |
| Auditoria | | ✔ | | ✔ |

**Nota:** a Permission Matrix completa por código (IC-PERM × papel × escopo × condições) está na Seção 13.

---

# 6. Restrições por Estado

| Estado | Editar | Ativar | Inativar | Reativar | Descartar | Sinônimos/Imagem/Parâmetros |
|--------|--------|--------|----------|----------|-----------|------------------------------|
| Rascunho | ✔ | ✔ | ❌ | ❌ | ❌ | ✔ |
| Ativo | ✔ (IC-BR-021 se em uso) | ❌ | ✔ | ❌ | ❌ | ✔ |
| Inativo | ✔ | ❌ | ❌ | ✔ | ✔ (IC-BR-021) | ✔ |
| Inativo (Descarte) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

---

# 7. Escopo Organizacional

Uma permissão pode ser limitada por:

- Empresa (obrigatório — o catálogo é particionado por `company_id`)
- Unidade (leitura operacional pode ser recortada por unidade quando a política da empresa exigir)
- Grupo/categoria de itens (recorte de manutenção por família, configurável)

Exemplo:

Usuária Maria

Role:

Catalog Maintainer

Empresa:

Grupo Trino — Filial Nordeste

Grupo de itens:

EPI

Essa usuária somente poderá manter itens do grupo EPI dessa empresa.

---

# 8. Segregação de Funções (SoD)

O sistema deverá impedir conflitos de interesse no governo do catálogo.

Exemplos:

- Quem cadastra não deve ser o único capaz de ativar quando a política da empresa exigir dupla verificação (`materials.item.sod.self-activation.allow=false`).
- O mantenedor não pode alterar configurações do módulo (separação operação × configuração).
- Descarte de item com histórico relevante exige papel com alçada de descarte.

Essas regras deverão ser parametrizáveis conforme a política da organização (matriz completa na Seção 14).

---

# 9. Delegação

O sistema poderá permitir delegação temporária de manutenção do catálogo (ex.: férias do mantenedor).

A delegação deve conter:

- Delegante
- Delegado
- Data de início
- Data de término
- Motivo

Toda delegação deve ser auditada (regras completas na Seção 15.2).

---

# 10. Auditoria

Toda decisão de autorização deve registrar:

- Usuário
- Papel utilizado
- Empresa
- Ação executada
- Resultado (Permitido/Negado)
- Data e hora

---

# 11. Dependências

Foundation

Identity & Access Management (FD-001-01 IAM)

Audit Service (FD-001-06)

Timeline Service (FD-001-07)

Master Data (FD-001-09 — recortes por grupo/categoria)

---

# 12. Modelo Formal de Autorização (Enterprise)

## 12.1 RBAC — Controle de Acesso Baseado em Papéis

Cada papel (Seção 3) é um conjunto nomeado de permissões (Seção 4). Atribuições usuário→papel são **sempre escopadas** (empresa), nunca globais por padrão.

| Elemento | Definição |
| -------- | --------- |
| Role | Conjunto nomeado de permissões (ex.: `Catalog Maintainer`) |
| Role Assignment | Tupla `(userId, role, scope)` — ex.: `(maria, Maintainer, empresa=Grupo Trino Filial Nordeste, grupo=EPI)` |
| Grant | Concedido e revogado apenas por System Administrator; toda atribuição é auditada |
| Cardinalidade | Um usuário pode ter múltiplos papéis em escopos distintos; o conjunto efetivo é a união das permissões dos papéis ativos no escopo avaliado |

## 12.2 ABAC — Controle de Acesso Baseado em Atributos

Atributos avaliados na decisão:

| Categoria | Atributos |
| --------- | --------- |
| **Sujeito** | userId, papéis ativos, escopo organizacional atribuído, recorte por grupo/categoria, delegações vigentes |
| **Recurso** | companyId, status (estado do item), groupId, categoryId, createdBy, inUse (operações abertas — IC-BR-021) |
| **Ação** | Permissão solicitada (IC-PERM-xxx) |
| **Contexto** | Data/hora, canal (API/UI), delegationId (quando agindo como delegado), políticas da empresa (`materials.item.sod.*`, `materials.item.edit.in-use-policy`) |

Regras ABAC estruturais do módulo:

- **ABAC-IC-01 — Estado:** ação permitida apenas nos estados definidos na Seção 6.
- **ABAC-IC-02 — Visibilidade operacional:** perfil Operational enxerga apenas itens `status = Ativo`; demais estados retornam 404 no recorte operacional.
- **ABAC-IC-03 — Uso em operação:** alterações estruturais em item com `inUse = true` seguem `materials.item.edit.in-use-policy` (bloqueio ou confirmação reforçada).
- **ABAC-IC-04 — Recorte por grupo/categoria:** mantenedor com recorte só executa ações em itens do seu grupo/categoria atribuídos.
- **ABAC-IC-05 — Campos sensíveis:** parâmetros de reposição e auditoria completa não são visíveis ao perfil Operational.

## 12.3 Policies

Policies são regras declarativas avaliadas pelo motor de autorização. Cada policy combina condições RBAC + ABAC.

| Código | Policy | Efeito | Condição |
| ------ | ------ | ------ | -------- |
| POL-IC-AUTH-001 | Cadastro de item | Permit | `IC-PERM-001 ∈ roles(subject)` ∧ `scope(subject) ⊇ resource.company` ∧ ABAC-IC-04 |
| POL-IC-AUTH-002 | Edição de item | Permit | `IC-PERM-002` ∧ ABAC-IC-01 (estado editável) ∧ ABAC-IC-03 (uso) ∧ ABAC-IC-04 |
| POL-IC-AUTH-003 | Ativação/Reativação | Permit | `IC-PERM-003` ∧ ABAC-IC-01 ∧ SOD-IC-001 (política de auto-ativação) |
| POL-IC-AUTH-004 | Inativação/Descarte | Permit | `IC-PERM-004` ∧ ABAC-IC-01 ∧ ABAC-IC-03 (descarte com uso) |
| POL-IC-AUTH-005 | Consulta operacional | Permit | `IC-PERM-006` ∧ ABAC-IC-02 (recorte Ativos) |
| POL-IC-AUTH-006 | Consulta completa (governo/auditoria) | Permit | `IC-PERM-006` ∧ papel ∈ {Maintainer, Admin, Auditor} ∧ escopo |
| POL-IC-AUTH-007 | Configuração do módulo | Permit | `IC-PERM-008` ∧ papel = Admin |
| POL-IC-AUTH-008 | Deny-by-default | Deny | Qualquer condição não avaliada explicitamente como Permit |

**Princípio:** *deny by default* — a ausência de um Permit explícito resulta em negação, sempre auditada.

## 12.4 JWT Claims

Token de acesso (FD-001-01 IAM) carrega as claims consumidas pelo módulo:

| Claim | Conteúdo | Uso |
| ----- | -------- | --- |
| `sub` | userId | Identidade do sujeito |
| `iss` / `aud` | Emissor / audiência (`trino-supply`) | Validação do token |
| `exp` / `iat` / `nbf` | Validade temporal | Expiração curta (padrão 15 min; refresh token rotativo) |
| `company_ids` | Empresas às quais o usuário pertence | Filtro de escopo |
| `roles` | Papéis com escopo: `[{ role, companyId, itemGroupIds? }]` | RBAC + ABAC-IC-04 |
| `perms` | Permissões efetivas (`items.read`, `items.write`, ...) | Short-circuit de avaliação |
| `scope` | Scopes OAuth2 (ver 12.5) | Autorização de API |
| `jti` | Identificador único do token | Revogação/blacklist |
| `tenant_id` | Tenant da sessão | Isolamento multi-tenant |

**Regras:** tokens de terceiros/inválidos → 401; token válido sem permissão → 403; recurso fora do escopo → 404 ("Recurso não encontrado", anti-IDOR).

## 12.5 Scopes (OAuth2)

| Scope | Descrição |
| ----- | --------- |
| `items.read` | Consulta do catálogo (recorte conforme perfil), timeline |
| `items.write` | Cadastrar, editar, sinônimos, imagem, parâmetros |
| `items.lifecycle` | Ativar, reativar, inativar, descartar |
| `items.admin` | Configurações do módulo |
| `items.audit` | Visualizar auditoria do item |

Aplicações cliente (frontend, integrações) recebem apenas os scopes estritamente necessários (least privilege).

---

# 13. Permission Matrix (completa)

| Permissão | Maintainer | Admin | Operational | Auditor | Condições ABAC |
| --------- |-----------|-------|-------------|---------| -------------- |
| IC-PERM-001 Cadastrar | ✔ | ✔ | — | — | Escopo empresa + ABAC-IC-04 |
| IC-PERM-002 Editar | ✔ | ✔ | — | — | ABAC-IC-01 + ABAC-IC-03 + ABAC-IC-04 |
| IC-PERM-003 Ativar/Reativar | ✔ | ✔ | — | — | ABAC-IC-01 + SOD-IC-001 |
| IC-PERM-004 Inativar/Descartar | ✔ | ✔ | — | — | ABAC-IC-01 + ABAC-IC-03 (descarte) |
| IC-PERM-005 Sinônimos | ✔ | ✔ | — | — | ABAC-IC-01 + ABAC-IC-04 |
| IC-PERM-006 Consultar | ✔ | ✔ | ✔ | ✔ | ABAC-IC-02 (Operational) / ABAC-IC-06 (demais) |
| IC-PERM-007 Parâmetros | ✔ | ✔ | — | — | ABAC-IC-01 + ABAC-IC-04 |
| IC-PERM-008 Configurações | — | ✔ | — | — | Papel Admin |
| IC-PERM-009 Exportar | ✔ | ✔ | — | ✔ | Escopo; exportação auditada |
| IC-PERM-010 Auditoria | — | ✔ | — | ✔ | Escopo; apenas leitura |

---

# 14. Segregation of Duties (SoD) — Matriz de Conflitos

| Código | Conflito | Regra | Tratamento |
| ------ | -------- | ----- | ---------- |
| SOD-IC-001 | Cadastrar × Ativar | Quando `materials.item.sod.self-activation.allow=false`, quem cadastrou não pode ativar o mesmo item | Bloqueio; direciona ativação a outro mantenedor habilitado; auditoria obrigatória |
| SOD-IC-002 | Operar × Configurar | Mantenedor não altera configurações do módulo; Admin que configura não deve ser o operador regular do mesmo escopo (recomendado) | Bloqueio (IC-PERM-008 exclusivo de Admin); alerta de conflito para a recomendação |
| SOD-IC-003 | Descartar × Histórico crítico | Descarte de item com histórico de movimentações exige papel com alçada de descarte (`materials.item.discard.requires-elevated-role=true`) | Bloqueio; escalonamento ao Admin |
| SOD-IC-004 | Delegar × Auto-delegar | Usuário não delega para si mesmo | Bloqueio na criação da delegação |

Padrão de fábrica: `materials.item.sod.self-activation.allow=true` (catálogo mantido por equipe enxuta); a política estrita é opt-in por empresa e sua alteração é auditada como configuração.

---

# 15. Inheritance e Delegation

## 15.1 Inheritance (Herança de Permissões)

| Regra | Descrição |
| ----- | --------- |
| INH-IC-001 | Papel atribuído no nível **empresa** herda para todas as unidades da empresa, salvo exceção explícita |
| INH-IC-002 | Recorte por grupo/categoria **restringe** o escopo herdado (interseção), nunca amplia |
| INH-IC-003 | Exceção explícita (`deny override`) em nível inferior prevalece sobre herança (avaliação do mais específico para o mais geral) |
| INH-IC-004 | Não há herança entre empresas/tenants — isolamento absoluto |

## 15.2 Delegation (Delegação)

Complementando a Seção 9:

| Regra | Descrição |
| ----- | --------- |
| DEL-IC-001 | Delegação é escopada: herda o escopo do delegante (empresa + recorte de grupo/categoria), nunca o amplia |
| DEL-IC-002 | Vigência obrigatória (início/fim); delegações expiradas são ignoradas automaticamente |
| DEL-IC-003 | Delegado não pode redelegar |
| DEL-IC-004 | SoD continua aplicável ao delegado (ex.: SOD-IC-001 vale para ações do delegado) |
| DEL-IC-005 | Ações executadas por delegado registram `actedAs = delegate` + `delegatedBy` na auditoria e na timeline |
| DEL-IC-006 | Delegante pode revogar a qualquer momento; revogação tem efeito imediato |
| DEL-IC-007 | Delegação não cobre IC-PERM-008 (configurações nunca são delegáveis) |

---

# 16. Permission Evaluation Flow

```text
Request (usuário, ação, recurso)
        │
        ▼
[1] Autenticação — JWT válido? (assinatura, exp, iss, aud, tenant)
        │ Não → 401 Unauthorized
        ▼
[2] Resolução de escopo — company do recurso ∈ escopo do sujeito?
        │ Não → 404 Not Found (anti-IDOR; log de segurança)
        ▼
[3] RBAC — permissão IC-PERM-xxx ∈ papéis efetivos no escopo?
        │ Não → 403 Forbidden + auditoria de negação (IC-ERR-900)
        ▼
[4] ABAC — regras ABAC-IC-01..05 e policies POL-IC-AUTH-xxx satisfeitas?
        (estado, visibilidade, uso, recorte de grupo/categoria, campos sensíveis)
        │ Não → 403 Forbidden + auditoria de negação
        ▼
[5] Delegação — agindo como delegado? validar DEL-IC-001..007
        │ Inválida → 403 Forbidden + auditoria
        ▼
[6] PERMIT — executar ação + registrar auditoria de decisão
```

**Propriedades do fluxo:**

- Deny by default (POL-IC-AUTH-008).
- Toda negação nos passos 2–5 gera registro de auditoria (Seção 10).
- O passo 2 precede o 3 intencionalmente: não revelar a existência de recursos fora do escopo.
- Avaliação executada server-side em **todas** as requisições; o frontend apenas reflete a decisão (nunca decide).
- Cache de decisão (Redis) permitido por até 60s, com invalidação em mudança de papel, escopo ou delegação.

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: modelo híbrido RBAC + ABAC + Escopo Organizacional para o Item Catalog — 4 papéis, 10 permissões IC-PERM, restrições por estado, modelo formal (RBAC/ABAC/Policies/JWT Claims/Scopes), Permission Matrix completa, matriz SoD (SOD-IC-001..004), Inheritance (INH-IC-001..004), Delegation (DEL-IC-001..007) e Permission Evaluation Flow de 6 passos. |
