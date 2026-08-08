# GO-001 — Prontidão de Implementação & Kickoff de Build

**Documento:** GO-001 — Build Kickoff
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Criticidade:** 🔴 Core
**Escopo:** MVP do Trino Supply (Foundation + Procurement PR-001 + Materials MMS-002/003/004/005)

> Este documento **encerra a fase de design do MVP** e **abre a fase de construção**. Certifica que a documentação está pronta, define a estrutura do código, a ordem de build mapeada aos documentos e a primeira sprint. Fonte de verdade para o time que vai implementar.
> Documentos relacionados: TPES-002, GOV-002, ARC-001..007, ADR-009..016, QA-001, OPS-001, REL-001.

---

# 1. Prontidão de Implementação (checklist)

| Pré-requisito | Documento | Status |
|---------------|-----------|--------|
| Metodologia e stack definidas | TPES-002, ADR-015, ADR-016 | ✅ |
| Arquitetura (contextos, camadas, deployment, eventos, NFR, robustez) | ARC-001..007 | ✅ |
| Segurança (arquitetura, STRIDE, dev seguro) | SEC-001/002/003 | ✅ |
| Foundation (10 domínios) | FD-001 | ✅ |
| Módulos MVP com domínio→banco→API | PR-001, MMS-002/003/004/005 (17/17 cada) | ✅ |
| Estratégia de testes | QA-001 | ✅ |
| CI/CD, ambientes, migrations, runbooks | OPS-001 | ✅ |
| Release/versionamento | REL-001 | ✅ |
| Decisões arquiteturais formalizadas | ADR-009..016 | ✅ |

**Veredicto:** documentação do MVP **completa e consistente** — apta a iniciar o desenvolvimento.

---

# 2. Estrutura do Repositório de Código

Organização por **Bounded Context** (ARC-002/ARC-004), nunca por camada técnica global:

```text
trino-supply/                     (este repo — docs/ permanece a SSOT)
├── docs/                         # documentação (fonte de verdade)
├── src/
│   ├── BuildingBlocks/           # kernel técnico (Result, Outbox, Money, Ids, keyset)
│   ├── Foundation/               # BC-FND  Domain/ Application/ Infrastructure/ Api/
│   ├── Procurement/              # BC-PRC  (PR-001)
│   ├── Materials/                # BC-MMS  (MMS-002/003/004/005)
│   └── (Supplier/ Contract/ Analytics — ondas futuras)
├── host/
│   ├── Api/                      # composição do Modular Monolith (.NET 9): DI, pipeline, endpoints
│   └── Worker/                   # consumidores de eventos (RabbitMQ)
├── web/                          # frontend Next.js/React/TS (Tailwind, Zod, TanStack Query, Zustand*)
├── deploy/
│   ├── docker-compose.yml        # postgres, redis, rabbitmq, (minio|r2-mock)
│   └── (manifests de ambiente)
└── .github/workflows/            # pipeline (OPS-001)
```
> *Zod/TanStack Query/Zustand são boas escolhas de front trazidas na avaliação Supabase (ADR-015) — adotáveis no frontend Next.js sem mudar a stack de backend.

Cada contexto segue Clean Architecture (ARC-004 §1): `Domain` (agregados, invariantes INV-*, VOs) → `Application` (use cases, Outbox) → `Infrastructure` (EF Core, RabbitMQ, Redis, storage) → `Api` (Minimal APIs/Controllers).

---

# 3. Ordem de Build (mapeada aos documentos)

Segue ADR-011 (Foundation antes das APIs) e a dependência técnica:

| # | Entrega | Documentos-fonte | Resultado |
|---|---------|------------------|-----------|
| 0 | **Esteira**: solution .NET 9 + `docker-compose` + pipeline CI + esqueleto Next.js | ARC-003, OPS-001, QA-001 | Build/test/deploy verdes com "hello" |
| 1 | **Foundation** (IAM, Org, Config, Master Data, Document, Workflow, Notification, Audit, Timeline) | FD-001-01..10, SEC-001 | AuthN/AuthZ, multi-tenant, RLS, auditoria, workflow |
| 2 | **MMS-002 Item Catalog** | MMS-002-04/11/13 (+02/03/05/07/09) | Catálogo + conversão de UoM |
| 3 | **MMS-004 Inventory** | MMS-004-04/11/13 | Saldo (projeção), movimentações, reservas, reposição |
| 4 | **MMS-003 Material Requisition** | MMS-003-04/11/13 | Solicitação + roteamento estoque/compra |
| 5 | **MMS-005 Receiving** | MMS-005-04/11/13 | Recebimento → entrada no estoque |
| 6 | **PR-001 Purchase Requisition** | PR-001-04/11/13 | Solicitação de compra + workflow |
| 7 | **Frontend** por workspace (Requester/Warehouse/Approvals/Management) | `*-14-ux`, `*-15-wireframes` | Telas por papel |

Cada entrega fecha com testes (QA-001) e critérios de aceite (`*-16`) verdes.

---

# 4. Primeira Sprint (fatia vertical do Foundation)

Objetivo: provar a arquitetura ponta a ponta com a menor fatia útil.

1. Esteira (item 0) — solution, compose, pipeline, health check.
2. **IAM mínimo** (FD-001-01): login JWT + refresh, RBAC/escopo, multi-tenant por `company_id` + **RLS**.
3. **Configuration + Master Data** (FD-001-09/10): base para os módulos.
4. **Outbox + evento de exemplo** (ARC-005): provar Outbox→RabbitMQ→consumidor idempotente.
5. **Auditoria** (FD-001-06): trilha imutável de uma operação.
6. Teste de fatia: unit (invariante) + integração (Testcontainers) + contrato (OpenAPI) + segurança (404 anti-enumeração).

Entregável: uma operação autenticada, multi-tenant, auditada e com evento publicado — a espinha dorsal reusada por todos os módulos.

---

# 5. Definição de Pronto (por entrega)
- [ ] Domínio implementa as invariantes INV-* do módulo (testes provam).
- [ ] Banco conforme `*-11` (schema, constraints, índices, RLS).
- [ ] API conforme `*-13` (endpoints, erros, autorização, idempotência).
- [ ] Eventos via Outbox conforme `*-05` (at-least-once, DLQ, idempotência).
- [ ] Cobertura de regras/AC conforme QA-001; segurança e performance no verde.
- [ ] Observabilidade e migrations conforme OPS-001.

---

# 6. Guardas de Robustez (não negociáveis — ARC-006 §12)
Optimistic concurrency em toda escrita · serialização por chave de saldo · saldo como projeção (nunca escrito direto) · SoD nas aprovações · Outbox + idempotência · `company_id` + RLS · keyset em todas as listagens.

---

# 7. Fora do escopo do build inicial (roadmap)
Onda Procurement (PRC-002 RFQ, PRC-003 Equalização, PRC-004 Purchase Order, PRC-005 Supplier Management detalhado, PRC-006 Contract Management) e Analytics — documentadas em nível de visão (PRC-001, PRC-005); entram após o MVP.

---

# 8. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Kickoff de Build: certifica a prontidão da documentação do MVP, define a estrutura do repositório de código (por Bounded Context, Clean Arch), a ordem de build mapeada aos documentos (esteira → Foundation → MMS-002 → MMS-004 → MMS-003 → MMS-005 → PR-001 → frontend), a primeira sprint (fatia vertical do Foundation), o DoD por entrega e as guardas de robustez — encerra a fase de design do MVP e abre a construção. |
