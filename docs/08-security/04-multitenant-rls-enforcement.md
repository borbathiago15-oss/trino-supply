**Documento:** SEC-004 — Enforcement de Isolamento Multi-Tenant (RLS)
**Versão:** 1.0.0
**Status:** Approved
**Escopo:** Plataforma Trino Supply (todos os módulos com dados por tenant)

> Referências normativas: OWASP ASVS 4.0 (V4 Access Control), OWASP API Security Top 10 (API1:2023 BOLA / IDOR), PostgreSQL Row Level Security, NIST SP 800-207 (Zero Trust).
> Documentos relacionados: SEC-001 (Arquitetura de Segurança §21), SEC-002 (Threat Model — Information Disclosure), ADR-015 (Stack .NET + RLS), ARC-006 §12.6 (Concorrência/Escala), FD-001-01 (IAM), GOV-002 (Registro).

# SEC-004 — Enforcement de Isolamento Multi-Tenant (RLS)

> Este documento define **como** o isolamento entre tenants é efetivamente aplicado no Trino Supply — não apenas a política (SEC-001), mas o mecanismo verificável no código e no banco. O vazamento cross-tenant é a ameaça P1 do produto (SEC-002); este é o controle que a fecha em profundidade.

---

# 1. Modelo em três camadas (defesa em profundidade)

O isolamento multi-tenant **nunca** depende de um único controle. São três camadas independentes; a falha de uma não abre o dado:

| Camada | Onde | O que garante | Falha isolada |
| ------ | ---- | ------------- | ------------- |
| **1. Autorização server-side** | Aplicação (SEC-001) | Usuário só opera dentro do seu tenant e com permissão explícita (deny by default) | Ainda barrado por RLS |
| **2. Filtro `company_id`** | Consultas/repos da aplicação | Toda query filtra pelo tenant do JWT | Ainda barrado por RLS |
| **3. Row Level Security (RLS)** | PostgreSQL | O banco **recusa** linhas de outro tenant, mesmo que a app esqueça o filtro | Ainda barrado pela autorização/filtro |

RLS é a **última linha**: protege contra o erro humano (um `WHERE company_id` esquecido, um IDOR). Não substitui as camadas 1 e 2 — complementa.

---

# 2. Como o tenant chega ao banco

```
JWT (claim company_id)
      │  FD-001-01
      ▼
HttpTenantContext  ──►  ITenantContext.CompanyId        (escopo da requisição)
      │
      ▼
TenantConnectionInterceptor  (DbConnectionInterceptor, EF Core)
      │  a cada conexão aberta:
      ▼
SELECT set_config('app.current_company', '<uuid>', false)   (parametrizado)
      │
      ▼
foundation.current_company()  ──►  usado por TODA policy RLS
```

- O tenant é **derivado do JWT**, nunca de parâmetro de rota/query/body (anti-IDOR).
- O valor é aplicado **por conexão**, no momento em que o EF Core a abre (`ConnectionOpenedAsync`).
- O valor é passado por **parâmetro** (`@company`), nunca interpolado — blindado contra injeção via claim.

---

# 3. Fail-closed (o requisito mais importante)

O controle é **fail-closed por construção**: na ausência de tenant, nada é visível.

| Situação | Comportamento | Resultado |
| -------- | ------------- | --------- |
| Requisição sem tenant (sem JWT válido, sem claim) | Interceptor seta `app.current_company = ''` | `current_company()` → `NULL` → policies negam **todas** as linhas |
| Host sem `ITenantContext` sobreposto (ex.: Worker) | `NullTenantContext.HasTenant = false` → GUC vazio | Nenhum dado de negócio acessível |
| Conexão reciclada do pool | Npgsql reseta o estado da sessão; o interceptor **reaplica** a cada abertura | Sem herança do tenant anterior (sem vazamento) |
| Claim `company_id` malformado | `HttpTenantContext.TryGet` falha → `HasTenant = false` | GUC vazio → nega tudo |

> Regra inviolável: **um GUC vazio ou nulo nunca é interpretado como "ver tudo"**. É interpretado como "ver nada". Toda policy usa igualdade estrita com `foundation.current_company()`, e igualdade com `NULL` é falsa.

---

# 4. Policy RLS padrão (tabelas de negócio)

Toda tabela com `company_id` recebe (ver `deploy/db/rls.sql`):

```sql
ALTER TABLE materials.item ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.item FORCE  ROW LEVEL SECURITY;   -- vale inclusive p/ o owner

CREATE POLICY tenant_isolation ON materials.item
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());
```

- `USING` filtra `SELECT/UPDATE/DELETE` para o tenant corrente.
- `WITH CHECK` impede `INSERT`/`UPDATE` gravar em **outro** tenant.
- `FORCE ROW LEVEL SECURITY` é **obrigatório**: sem ele o dono da tabela ignora a policy.
- Recurso fora do escopo → **0 linhas** → a aplicação traduz para **404** (anti-enumeração, SEC-002).

---

# 5. Hardening da role de banco

RLS só vale se a aplicação **não** puder ignorá-la. Portanto:

| Regra | Motivo |
| ----- | ------ |
| A aplicação conecta com role `trino_app` `NOSUPERUSER NOBYPASSRLS` | `SUPERUSER` e `BYPASSRLS` ignoram **todas** as policies |
| A aplicação **nunca** conecta como owner das tabelas | O owner ignora policies exceto sob `FORCE ROW LEVEL SECURITY` |
| `BYPASSRLS` é proibido por padrão | Qualquer exceção exige ADR e auditoria |
| Serviços de sistema cruzam tenants por **GRANT explícito** em tabelas de infra, não por bypass | Menor superfície; sem role "deus" |

Provisionamento em `deploy/db/rls.sql` (§ "Hardening da role de aplicação").

---

# 6. Tabelas SEM RLS (e por quê)

Nem toda tabela recebe RLS — algumas fariam o modelo quebrar se recebessem:

| Tabela | Sem RLS porque | Como é isolada |
| ------ | -------------- | -------------- |
| `foundation.outbox` | É infraestrutura; o **publisher** legitimamente lê **todos** os tenants; RLS quebraria a publicação e o bootstrap de empresa (`CompanyRegistered` ocorre fora de escopo de tenant) | Privilégio de role dedicada ao publisher (GRANT explícito) |
| `foundation.company` | É o **catálogo de tenants** (a raiz); um tenant não "pertence" a outro | Acesso restrito ao Admin de plataforma (SEC-001) |

> Decisão registrada: a `outbox` recebia RLS numa versão anterior do esqueleto, com `WITH CHECK (company_id = current_company())`, **sem** ninguém definir o GUC — o que **bloqueava todos os INSERTs** e dava falsa sensação de segurança. Removido. Ver `deploy/db/001_foundation_init.sql`.

---

# 7. Outbox e o tenant do evento

Ao gravar eventos de domínio no Outbox (ARC-005 §3), o `company_id` do evento é resolvido **fail-closed** (`FoundationDbContext.ResolveCompanyId`):

1. Se há tenant na requisição → usa `ITenantContext.CompanyId`.
2. No bootstrap de empresa (`CompanyRegistered`, ainda sem tenant) → usa o próprio `Id` da `Company`.
3. Caso contrário → **exceção**. Nunca se grava um tenant arbitrário (evita evento vazar para o tenant errado).

---

# 8. Verificação (testes obrigatórios — QA-001)

Este controle **exige** testes de integração com banco real (Testcontainers), não mocks:

| Teste | Espera |
| ----- | ------ |
| Ler linha de outro tenant com JWT do tenant A | 0 linhas / 404 |
| `INSERT` com `company_id` de outro tenant | Recusado (`WITH CHECK`) |
| Requisição sem tenant | 401/0 linhas (fail-closed) |
| Conexão reutilizada do pool após tenant A, agora tenant B | Só vê dados de B |
| Conexão como role com `BYPASSRLS` | **Proibida** em produção (teste de configuração) |

---

# 9. Rastreabilidade

| Artefato | Referência |
| -------- | ---------- |
| Interceptor | `src/Foundation/Infrastructure/Persistence/TenantConnectionInterceptor.cs` |
| Registro do interceptor (fail-closed) | `src/Foundation/Infrastructure/DependencyInjection.cs` |
| Função e policies | `deploy/db/rls.sql`, `deploy/db/001_foundation_init.sql` |
| Tenant do evento | `FoundationDbContext.ResolveCompanyId` |
| Origem do tenant (JWT) | `host/Api/Multitenancy/HttpTenantContext.cs` |
| AuthN fail-closed | `host/Api/Program.cs` (validação estrita de token) |
| Decisão de stack/RLS | ADR-015 §3 |
