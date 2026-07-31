# MMS-004-09 — Permissions & Authorization

**Documento:** MMS-004-09 — Permissions & Authorization
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-004 v1.0.0, MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-07 (Use Cases), FD-001-01 (IAM), FD-001-06 (Audit), SEC-001, SEC-003
**Referências:** MMS-002-09 (padrão de formato Enterprise), PR-001-09, GOV-001

> Este documento define o modelo de autorização do módulo Inventory Management.

---

# 1. Objetivo

Garantir que apenas usuários autorizados possam registrar e confirmar movimentações, gerenciar reservas, registrar e aprovar ajustes, executar inventários, estornar documentos, gerenciar locais e consultar posições/extratos de estoque — com segregação de funções nos pontos de controle (aprovação de ajuste, estorno), visão do almoxarifado exclusiva para perfis operacionais de estoque e isolamento por empresa.

O modelo deve suportar múltiplas empresas (tenants), múltiplos almoxarifados por empresa, segregação opcional de saldo por cliente/contrato (MMS-RG-10) e recorte de dados sensíveis (consumo por colaborador — LGPD).

---

# 2. Modelo de Autorização

O Trino Supply utiliza um modelo híbrido composto por:

- RBAC (Role-Based Access Control)
- ABAC (Attribute-Based Access Control)
- Escopo Organizacional

A decisão de acesso considera:

- Papel do usuário
- Empresa
- Almoxarifado / local de armazenagem
- Estado do documento (Rascunho, Confirmado, Estornado, Cancelado; estados de Reserva, Ajuste e Inventário)
- Ação solicitada
- Atributos de segregação (cliente/contrato, quando habilitada)
- Relação sujeito × recurso (registrador × aprovador; aprovador × estornante — SoD)

**Detalhamento das camadas (Seção 12):** RBAC define o conjunto base de permissões por papel; ABAC refina por atributos do recurso, do sujeito e do contexto; o Escopo Organizacional restringe o universo de dados visíveis. As três camadas são avaliadas em conjunto pelo fluxo de avaliação (Seção 16).

---

# 3. Papéis (Roles)

## Warehouse Operator (Almoxarife)

Responsável pela operação diária: registra e confirma entradas, saídas e transferências; cria, atende e libera reservas; executa contagens de inventário; trata alertas.

## Warehouse Supervisor (Supervisor de Almoxarifado)

Todas as ações do Almoxarife, mais: registra ajustes, estorna documentos confirmados, abre/fecha/cancela inventários e gerencia locais de armazenagem.

## Supply Manager (Gestor de Suprimentos)

Aprova ou rejeita ajustes (nunca os registrados por ele — SoD); acompanha posição, extratos, alertas e indicadores. Não executa movimentações operacionais.

## Requester (Solicitante)

Usuário das solicitações (MMS-003). **Não possui nenhuma permissão no MMS-004**: não acessa a visão do almoxarifado, posições ou extratos (IV-BR-097). Listado aqui para explicitar a negação.

## System Administrator

Responsável pela configuração do módulo (`materials.inventory.*`) e por todas as ações operacionais em caráter de contingência (auditoria reforçada).

## Auditor

Responsável apenas pela consulta completa (todos os documentos, estados e extratos) e pela auditoria dos registros. Sem ações de escrita.

---

# 4. Ações (Permissions)

| Código | Ação |
|---------|------|
| IV-PERM-001 | Registrar movimentação (entrada, saída, transferência) |
| IV-PERM-002 | Confirmar movimentação (efetivar saldo) |
| IV-PERM-003 | Gerenciar reservas (criar, atender, liberar) |
| IV-PERM-004 | Registrar ajuste |
| IV-PERM-005 | Aprovar / Rejeitar ajuste |
| IV-PERM-006 | Gerenciar inventário (abrir, encerrar lançamentos, fechar, cancelar) |
| IV-PERM-007 | Estornar documento confirmado |
| IV-PERM-008 | Gerenciar locais de armazenagem |
| IV-PERM-009 | Consultar posição, extrato e alertas |
| IV-PERM-010 | Tratar alertas (ciência) |
| IV-PERM-011 | Administrar configurações do módulo |

---

# 5. Permissões por Papel

| Permissão | Operator | Supervisor | Manager | Admin | Auditor | Requester |
|-----------|----------|-----------|---------|-------|---------|-----------|
| IV-PERM-001 Registrar movimentação | ✔ | ✔ | | ✔ | | ❌ |
| IV-PERM-002 Confirmar movimentação | ✔ | ✔ | | ✔ | | ❌ |
| IV-PERM-003 Reservas | ✔ | ✔ | | ✔ | | ❌ |
| IV-PERM-004 Registrar ajuste | | ✔ | | ✔ | | ❌ |
| IV-PERM-005 Aprovar ajuste | | | ✔ | ✔ | | ❌ |
| IV-PERM-006 Inventário | contagem apenas* | ✔ | | ✔ | | ❌ |
| IV-PERM-007 Estornar | | ✔ | | ✔ | | ❌ |
| IV-PERM-008 Locais | | ✔ | | ✔ | | ❌ |
| IV-PERM-009 Consultar | ✔ | ✔ | ✔ | ✔ | ✔ | ❌ |
| IV-PERM-010 Tratar alertas | ✔ | ✔ | ✔ | ✔ | | ❌ |
| IV-PERM-011 Configurações | | | | ✔ | | ❌ |

\* O Almoxarife registra contagens (CountEntry) em inventários abertos, mas não abre, encerra lançamentos, fecha nem cancela inventários — ações exclusivas do Supervisor (IV-PERM-006).

**Nota:** a Permission Matrix completa por código (IV-PERM × papel × escopo × condições) está na Seção 13.

---

# 6. Restrições por Estado

## 6.1 Documento de Movimentação

| Estado | Editar linhas | Confirmar | Cancelar | Estornar |
|--------|---------------|-----------|----------|----------|
| Rascunho (ST-IV-001) | ✔ (quem registrou ou Supervisor) | ✔ (IV-PERM-002) | ✔ | ❌ |
| Confirmado (ST-IV-002) | ❌ (imutável — MMS-P-08) | ❌ | ❌ | ✔ (IV-PERM-007) |
| Estornado (ST-IV-003) | ❌ | ❌ | ❌ | ❌ |
| Cancelado (ST-IV-004) | ❌ | ❌ | ❌ | ❌ |

## 6.2 Reserva

| Estado | Atender | Liberar | Vencer |
|--------|---------|---------|--------|
| Ativa (ST-IV-010) | ✔ (IV-PERM-003) | ✔ (IV-PERM-003) | ✔ (Sistema — TMR-IV-001) |
| Atendida / Liberada / Vencida | ❌ | ❌ | ❌ (terminais) |

## 6.3 Ajuste

| Estado | Aprovar/Rejeitar | Estornar |
|--------|------------------|----------|
| Pendente (ST-IV-020) | ✔ (IV-PERM-005 ∧ aprovador ≠ registrador) | ❌ |
| Aprovado (ST-IV-021) | ❌ | ✔ (IV-PERM-007 ∧ estornante ≠ aprovador) |
| Rejeitado (ST-IV-022) | ❌ | ❌ |
| Estornado (ST-IV-023) | ❌ | ❌ |

## 6.4 Inventário

| Estado | Lançar contagem | Encerrar lançamentos | Fechar | Cancelar |
|--------|-----------------|----------------------|--------|----------|
| Aberto (ST-IV-030) | ✔ (IV-PERM-001) | ✔ (IV-PERM-006) | ❌ | ✔ (IV-PERM-006) |
| Em Contagem (ST-IV-031) | ✔ | ✔ | ✔ (IV-PERM-006, condições IV-BR-102) | ✔ |
| Fechado (ST-IV-032) | ❌ | ❌ | ❌ | ❌ |
| Cancelado (ST-IV-033) | ❌ | ❌ | ❌ | ❌ |

---

# 7. Escopo Organizacional

Uma permissão pode ser limitada por:

- Empresa (obrigatório — o estoque é particionado por `company_id`)
- Almoxarifado / conjunto de locais (recorte operacional por unidade física)
- Cliente/contrato (quando `materials.inventory.segregation.enabled = true` — MMS-RG-10)

Exemplo:

Usuário João

Role:

Warehouse Operator

Empresa:

Grupo Trino — Filial Nordeste

Almoxarifado:

Almoxarifado Central

Cliente/contrato:

Contrato CT-2026-014

Esse usuário somente poderá operar movimentações, reservas e contagens no Almoxarifado Central dessa empresa, e somente enxergará saldos do contrato CT-2026-014 quando a segregação estiver habilitada.

---

# 8. Segregação de Funções (SoD)

O sistema deverá impedir conflitos de interesse nos pontos de controle do estoque.

Exemplos:

- Quem registra um ajuste **não pode aprová-lo** (IV-BR-085) — controle anti-fraude central do módulo.
- Quem aprovou um ajuste **não pode estorná-lo** (IV-BR-114).
- O operador não altera configurações do módulo (separação operação × configuração).
- No inventário, a aprovação dos ajustes de divergência é sempre do Gestor — nunca do Supervisor que abriu o inventário nem do Almoxarife que contou (cadeia conta → apura → aprova com três papéis distintos).

Essas regras deverão ser parametrizáveis apenas na margem permitida (matriz completa na Seção 14); IV-BR-085 e IV-BR-114 **não são desligáveis**.

---

# 9. Delegação

O sistema poderá permitir delegação temporária de responsabilidades (ex.: férias do Supervisor; ausência do Gestor aprovador).

A delegação deve conter:

- Delegante
- Delegado
- Data de início
- Data de término
- Motivo

Toda delegação deve ser auditada (regras completas na Seção 15.2). A delegação de IV-PERM-005 (aprovação de ajustes) deve ser sempre direcionada a outro papel habilitado a aprovar — o SoD continua valendo integralmente para o delegado.

---

# 10. Auditoria

Toda decisão de autorização deve registrar:

- Usuário (e `actedAs`/`delegatedBy` quando delegado)
- Papel utilizado
- Empresa / almoxarifado
- Ação executada
- Recurso (documento, reserva, ajuste, inventário, local)
- Resultado (Permitido/Negado)
- Data e hora

Decisões de negação nas rotas sensíveis (aprovação de ajuste, estorno, visão do almoxarifado por solicitante) geram **alerta de segurança** além do registro (SEC-001).

---

# 11. Dependências

Foundation

Identity & Access Management (FD-001-01 IAM)

Audit Service (FD-001-06)

Timeline Service (FD-001-07)

Master Data (FD-001-09 — recortes; motivos de ajuste)

Security Architecture (SEC-001) e Secure Development Standard (SEC-003)

---

# 12. Modelo Formal de Autorização (Enterprise)

## 12.1 RBAC — Controle de Acesso Baseado em Papéis

Cada papel (Seção 3) é um conjunto nomeado de permissões (Seção 4). Atribuições usuário→papel são **sempre escopadas** (empresa + almoxarifado quando aplicável), nunca globais por padrão.

| Elemento | Definição |
| -------- | --------- |
| Role | Conjunto nomeado de permissões (ex.: `Warehouse Supervisor`) |
| Role Assignment | Tupla `(userId, role, scope)` — ex.: `(joao, Operator, empresa=Grupo Trino Filial Nordeste, almoxarifado=Central, contrato=CT-2026-014)` |
| Grant | Concedido e revogado apenas por System Administrator; toda atribuição é auditada |
| Cardinalidade | Um usuário pode ter múltiplos papéis em escopos distintos (ex.: Operator no Almoxarifado Central e Auditor na mesma empresa); o conjunto efetivo é a união das permissões dos papéis ativos no escopo avaliado, respeitado o SoD |

## 12.2 ABAC — Controle de Acesso Baseado em Atributos

Atributos avaliados na decisão:

| Categoria | Atributos |
| --------- | --------- |
| **Sujeito** | userId, papéis ativos, escopo organizacional atribuído, almoxarifados habilitados, vínculos de cliente/contrato, delegações vigentes |
| **Recurso** | companyId, locationId (e ancestrais), estado do documento/reserva/ajuste/inventário, registeredBy (registrador), approvedBy (aprovador), clientId/contractId (segregação), costCenterId, requesterId (dados de consumo) |
| **Ação** | Permissão solicitada (IV-PERM-xxx) |
| **Contexto** | Data/hora, canal (API/UI), delegationId, políticas da empresa (`materials.inventory.segregation.enabled`, `materials.inventory.issue.without-reservation`, `materials.inventory.adjustment.approval-required`) |

Regras ABAC estruturais do módulo:

- **ABAC-IV-01 — Estado:** ação permitida apenas nos estados definidos na Seção 6.
- **ABAC-IV-02 — Visão do almoxarifado exclusiva:** a fila operacional (movimentações e reservas com dados de solicitante) é acessível apenas a Operator, Supervisor, Manager, Admin e Auditor; perfil Requester recebe 404/403 conforme o passo do fluxo (IV-BR-097) e a tentativa é registrada.
- **ABAC-IV-03 — Segregação cliente/contrato:** quando habilitada, o sujeito só visualiza e opera saldos/documentos dos clientes/contratos do seu escopo; saldo de outro contrato é tratado como inexistente (não vaza disponibilidade — IV-ERR-070 genérico).
- **ABAC-IV-04 — Dados sensíveis (LGPD):** consumo por colaborador e identificação do solicitante em extratos/exportações são visíveis apenas a Supervisor, Manager, Admin e Auditor; Operator vê solicitante apenas nas reservas/movimentações que opera (necessidade operacional).
- **ABAC-IV-05 — Recorte por almoxarifado:** operador/supervisor com recorte de almoxarifado só executa ações em locais pertencentes aos seus almoxarifados atribuídos.
- **ABAC-IV-06 — SoD relacional:** aprovação de ajuste exige `subject.userId ≠ resource.registeredBy`; estorno de ajuste exige `subject.userId ≠ resource.approvedBy` (IV-BR-085/114).

## 12.3 Policies

Policies são regras declarativas avaliadas pelo motor de autorização. Cada policy combina condições RBAC + ABAC.

| Código | Policy | Efeito | Condição |
| ------ | ------ | ------ | -------- |
| POL-IV-AUTH-001 | Registrar/confirmar movimentação | Permit | `IV-PERM-001/002 ∈ roles(subject)` ∧ `scope(subject) ⊇ resource.location` ∧ ABAC-IV-01 ∧ ABAC-IV-03 ∧ ABAC-IV-05 |
| POL-IV-AUTH-002 | Gerenciar reservas | Permit | `IV-PERM-003` ∧ ABAC-IV-01 (Ativa) ∧ ABAC-IV-03 ∧ ABAC-IV-05 |
| POL-IV-AUTH-003 | Registrar ajuste | Permit | `IV-PERM-004` ∧ ABAC-IV-03 ∧ ABAC-IV-05 |
| POL-IV-AUTH-004 | Aprovar/Rejeitar ajuste | Permit | `IV-PERM-005` ∧ ABAC-IV-01 (Pendente) ∧ ABAC-IV-06 (`userId ≠ registeredBy`) |
| POL-IV-AUTH-005 | Gerenciar inventário | Permit | `IV-PERM-006` ∧ ABAC-IV-01 ∧ ABAC-IV-05 |
| POL-IV-AUTH-006 | Estornar documento | Permit | `IV-PERM-007` ∧ ABAC-IV-01 (Confirmado) ∧ (se ajuste: ABAC-IV-06 `userId ≠ approvedBy`) |
| POL-IV-AUTH-007 | Gerenciar locais | Permit | `IV-PERM-008` ∧ ABAC-IV-05 |
| POL-IV-AUTH-008 | Consultar posição/extrato/alertas | Permit | `IV-PERM-009` ∧ ABAC-IV-02 (visão) ∧ ABAC-IV-03 ∧ ABAC-IV-04 (campos sensíveis) |
| POL-IV-AUTH-009 | Tratar alertas | Permit | `IV-PERM-010` ∧ ABAC-IV-05 |
| POL-IV-AUTH-010 | Configurar módulo | Permit | `IV-PERM-011` ∧ papel = Admin |
| POL-IV-AUTH-011 | Deny-by-default | Deny | Qualquer condição não avaliada explicitamente como Permit |

**Princípio:** *deny by default* — a ausência de um Permit explícito resulta em negação, sempre auditada.

## 12.4 JWT Claims

Token de acesso (FD-001-01 IAM) carrega as claims consumidas pelo módulo:

| Claim | Conteúdo | Uso |
| ----- | -------- | --- |
| `sub` | userId | Identidade do sujeito |
| `iss` / `aud` | Emissor / audiência (`trino-supply`) | Validação do token |
| `exp` / `iat` / `nbf` | Validade temporal | Expiração curta (padrão 15 min; refresh token rotativo) |
| `company_ids` | Empresas às quais o usuário pertence | Filtro de escopo |
| `roles` | Papéis com escopo: `[{ role, companyId, warehouseIds?, contractIds? }]` | RBAC + ABAC-IV-03/05 |
| `perms` | Permissões efetivas (`inventory.read`, `inventory.write`, ...) | Short-circuit de avaliação |
| `scope` | Scopes OAuth2 (ver 12.5) | Autorização de API |
| `jti` | Identificador único do token | Revogação/blacklist |
| `tenant_id` | Tenant da sessão | Isolamento multi-tenant |

**Regras:** tokens de terceiros/inválidos → 401; token válido sem permissão → 403; recurso fora do escopo → 404 ("Recurso não encontrado", anti-IDOR — IV-ERR-404).

## 12.5 Scopes (OAuth2)

| Scope | Descrição |
| ----- | --------- |
| `inventory.read` | Consulta de posição, extrato, documentos, reservas e alertas (recorte conforme perfil) |
| `inventory.write` | Registrar e confirmar movimentações; lançar contagens |
| `inventory.reserve` | Criar, atender e liberar reservas |
| `inventory.adjust` | Registrar ajustes |
| `inventory.adjust.approve` | Aprovar/rejeitar ajustes |
| `inventory.count` | Abrir, encerrar, fechar e cancelar inventários |
| `inventory.reverse` | Estornar documentos |
| `inventory.locations` | Gerenciar locais de armazenagem |
| `inventory.admin` | Configurações do módulo |
| `inventory.audit` | Visualizar auditoria completa e exportar extratos |

Aplicações cliente (frontend, integrações) recebem apenas os scopes estritamente necessários (least privilege). Integrações machine-to-machine (MMS-003/MMS-005) usam credenciais de serviço com `inventory.write` + `inventory.reserve` restritos ao escopo da integração.

---

# 13. Permission Matrix (completa)

| Permissão | Operator | Supervisor | Manager | Admin | Auditor | Requester | Condições ABAC |
| --------- |----------|-----------|---------|-------|---------|-----------| -------------- |
| IV-PERM-001 Registrar movimentação | ✔ | ✔ | — | ✔ | — | ❌ | ABAC-IV-01/03/05 |
| IV-PERM-002 Confirmar movimentação | ✔ | ✔ | — | ✔ | — | ❌ | ABAC-IV-01/03/05; guards de saldo (IV-BR-020) |
| IV-PERM-003 Reservas | ✔ | ✔ | — | ✔ | — | ❌ | ABAC-IV-01 (Ativa)/03/05 |
| IV-PERM-004 Registrar ajuste | — | ✔ | — | ✔ | — | ❌ | ABAC-IV-03/05 |
| IV-PERM-005 Aprovar ajuste | — | — | ✔ | ✔ | — | ❌ | ABAC-IV-01 (Pendente) + ABAC-IV-06 (≠ registrador) |
| IV-PERM-006 Gerenciar inventário | contagem* | ✔ | — | ✔ | — | ❌ | ABAC-IV-01/05 |
| IV-PERM-007 Estornar | — | ✔ | — | ✔ | — | ❌ | ABAC-IV-01 (Confirmado) + ABAC-IV-06 (≠ aprovador, se ajuste) |
| IV-PERM-008 Locais | — | ✔ | — | ✔ | — | ❌ | ABAC-IV-05; IV-BR-063 (inativação) |
| IV-PERM-009 Consultar | ✔ | ✔ | ✔ | ✔ | ✔ | ❌ | ABAC-IV-02/03/04 (campos sensíveis) |
| IV-PERM-010 Tratar alertas | ✔ | ✔ | ✔ | ✔ | — | ❌ | ABAC-IV-05 |
| IV-PERM-011 Configurações | — | — | — | ✔ | — | ❌ | Papel Admin; alteração auditada como configuração |

\* Contagem apenas (CountEntry), sem abertura/fechamento.

---

# 14. Segregation of Duties (SoD) — Matriz de Conflitos

| Código | Conflito | Regra | Tratamento |
| ------ | -------- | ----- | ---------- |
| SOD-IV-001 | Registrar ajuste × Aprovar ajuste | Quem registrou não pode aprovar o mesmo ajuste (IV-BR-085) — **não desligável** | Bloqueio (IV-ERR-085); auditoria obrigatória; alerta de segurança em reincidência |
| SOD-IV-002 | Aprovar ajuste × Estornar ajuste | Quem aprovou não pode estornar o mesmo ajuste (IV-BR-114) — **não desligável** | Bloqueio (IV-ERR-085); auditoria obrigatória |
| SOD-IV-003 | Contar × Aprovar (inventário) | Ajustes de divergência de inventário são aprovados apenas por IV-PERM-005 (Gestor/Admin) — nunca pelo Supervisor que abriu nem pelo Almoxarife que contou | Estrutural: cadeia conta (Operator) → apura (Sistema) → aprova (Manager) |
| SOD-IV-004 | Operar × Configurar | Operadores/supervisores não alteram `materials.inventory.*`; Admin que configura não deve ser o operador regular do mesmo escopo (recomendado) | Bloqueio (IV-PERM-011 exclusivo de Admin); alerta de conflito para a recomendação |
| SOD-IV-005 | Delegar × Auto-delegar | Usuário não delega para si mesmo | Bloqueio na criação da delegação |
| SOD-IV-006 | Registrar × Estornar (recomendado) | Recomenda-se que o estornante não seja o registrador do documento original; quando for, exigir motivo reforçado (mínimo 30 caracteres) | Política `materials.inventory.reverse.self-reverse.requires-strong-reason=true` (padrão) |

---

# 15. Inheritance e Delegation

## 15.1 Inheritance (Herança de Permissões)

| Regra | Descrição |
| ----- | --------- |
| INH-IV-001 | Papel atribuído no nível **empresa** herda para todos os almoxarifados/locais da empresa, salvo exceção explícita |
| INH-IV-002 | Recorte por almoxarifado ou contrato **restringe** o escopo herdado (interseção), nunca amplia |
| INH-IV-003 | Exceção explícita (`deny override`) em nível inferior prevalece sobre herança (avaliação do mais específico para o mais geral) |
| INH-IV-004 | Não há herança entre empresas/tenants — isolamento absoluto (saldo nunca é compartilhado entre empresas) |

## 15.2 Delegation (Delegação)

Complementando a Seção 9:

| Regra | Descrição |
| ----- | --------- |
| DEL-IV-001 | Delegação é escopada: herda o escopo do delegante (empresa + almoxarifado + contratos), nunca o amplia |
| DEL-IV-002 | Vigência obrigatória (início/fim); delegações expiradas são ignoradas automaticamente |
| DEL-IV-003 | Delegado não pode redelegar |
| DEL-IV-004 | SoD continua aplicável ao delegado: quem aprova ajuste como delegado do Gestor não pode aprovar ajustes registrados por si mesmo (SOD-IV-001/002 valem para a pessoa, não para o papel) |
| DEL-IV-005 | Ações executadas por delegado registram `actedAs = delegate` + `delegatedBy` na auditoria e na timeline |
| DEL-IV-006 | Delegante pode revogar a qualquer momento; revogação tem efeito imediato |
| DEL-IV-007 | Delegação não cobre IV-PERM-011 (configurações nunca são delegáveis) |
| DEL-IV-008 | Delegação de IV-PERM-005 só pode ter como delegado usuário que já possua IV-PERM-005 em outro escopo ou papel elegível a aprovação (Gestor/Admin) |

---

# 16. Permission Evaluation Flow

```text
Request (usuário, ação, recurso)
        │
        ▼
[1] Autenticação — JWT válido? (assinatura, exp, iss, aud, tenant)
        │ Não → 401 Unauthorized
        ▼
[2] Resolução de escopo — company/location do recurso ∈ escopo do sujeito?
        │ Não → 404 Not Found (anti-IDOR — IV-ERR-404; log de segurança)
        ▼
[3] RBAC — permissão IV-PERM-xxx ∈ papéis efetivos no escopo?
        │ Não → 403 Forbidden + auditoria de negação (IV-ERR-900)
        ▼
[4] ABAC — regras ABAC-IV-01..06 e policies POL-IV-AUTH-xxx satisfeitas?
        (estado, visão do almoxarifado, segregação, campos sensíveis,
         recorte por almoxarifado, SoD relacional)
        │ Não → 403 Forbidden + auditoria de negação
        │ (SoD: IV-ERR-085; visão por solicitante: IV-ERR-900 + alerta)
        ▼
[5] Delegação — agindo como delegado? validar DEL-IV-001..008
        │ Inválida → 403 Forbidden + auditoria
        ▼
[6] PERMIT — executar ação + registrar auditoria de decisão
```

**Propriedades do fluxo:**

- Deny by default (POL-IV-AUTH-011).
- Toda negação nos passos 2–5 gera registro de auditoria (Seção 10); negações por SoD e por acesso de solicitante à visão do almoxarifado geram alerta de segurança.
- O passo 2 precede o 3 intencionalmente: não revelar a existência de recursos fora do escopo.
- Avaliação executada server-side em **todas** as requisições; o frontend apenas reflete a decisão (nunca decide).
- Cache de decisão (Redis) permitido por até 60s, com invalidação em mudança de papel, escopo, delegação ou vínculo de contrato.
- A avaliação de SoD (passo 4) consulta `registeredBy`/`approvedBy` do recurso — a identidade da pessoa prevalece sobre o papel (DEL-IV-004).

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: modelo híbrido RBAC + ABAC + Escopo Organizacional para o Inventory Management — 6 papéis (incluindo Requester com negação explícita), 11 permissões IV-PERM, restrições por estado das 4 entidades, modelo formal (RBAC/ABAC/Policies/JWT Claims/Scopes `inventory.*`), Permission Matrix completa, matriz SoD (SOD-IV-001..006, com SOD-IV-001/002 não desligáveis), Inheritance (INH-IV-001..004), Delegation (DEL-IV-001..008) e Permission Evaluation Flow de 6 passos. |
