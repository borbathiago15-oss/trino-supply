**Documento:** ARC-004 — Application Architecture
**Versão:** 1.0.0
**Status:** Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: Clean Architecture (Martin), Domain-Driven Design tático (Evans/Vernon), SOLID, CQRS (leve), Transactional Outbox, .NET Application Architecture Guides.
> Documentos relacionados: ARC-001, ARC-002, ARC-005, ADR-009/010, `*-04-domain-model`, `*-13-api`, SEC-003.

# ARC-004 — Application Architecture

> Define **como cada Bounded Context é estruturado por dentro**: camadas, padrões táticos de DDD, fluxo de comando/consulta, transações e tratamento de erros. É o padrão de código que todo módulo segue; os `*-04-domain-model` de cada módulo são a instância concreta deste padrão.

---

## 1. Estilo interno: Clean Architecture + DDD

Cada contexto é organizado em quatro camadas concêntricas. A **Dependency Rule** é inviolável: dependências apontam sempre para dentro; o Domínio não conhece infraestrutura, framework ou banco.

```text
        ┌─────────────────────────────────────────────────┐
        │ Infrastructure (borda)                           │
        │  EF Core · Repositórios · RabbitMQ · Redis ·     │
        │  MinIO · e-mail · relógio · geradores de ID      │
        │   ┌─────────────────────────────────────────┐    │
        │   │ Application                              │    │
        │   │  Use Cases · Command/Query Handlers ·    │    │
        │   │  transações · Outbox · portas (interfaces)│   │
        │   │   ┌─────────────────────────────────┐    │    │
        │   │   │ Domain (centro)                 │    │    │
        │   │   │  Aggregates · Entities · Value  │    │    │
        │   │   │  Objects · Domain Events ·      │    │    │
        │   │   │  Domain Services · Policies ·   │    │    │
        │   │   │  Specifications · invariantes   │    │    │
        │   │   └─────────────────────────────────┘    │    │
        │   └─────────────────────────────────────────┘    │
        └─────────────────────────────────────────────────┘
        Presentation (Controllers/Minimal APIs) fica na borda,
        depende de Application; nunca contém regra de negócio.
```

---

## 2. Responsabilidade por camada

| Camada | Contém | NÃO contém |
|--------|--------|------------|
| **Presentation** | Controllers/Minimal APIs, DTOs de request/response, validação de formato, mapeamento HTTP↔comando, autorização declarativa (delegando ao motor) | Regra de negócio, acesso a banco |
| **Application** | Casos de uso (Commands/Queries), orquestração, controle transacional, publicação via Outbox, portas (interfaces) para infraestrutura | Regras de invariante de agregado (ficam no Domain), SQL |
| **Domain** | Aggregates, Entities, Value Objects, Domain Events, Domain Services, Policies, Specifications, invariantes | Framework, EF Core, HTTP, I/O, dependência externa |
| **Infrastructure** | Implementações de repositório (EF Core), publisher RabbitMQ, cache Redis, cliente MinIO, adapters externos (ACL), relógio/IDs | Regra de negócio |

---

## 3. Padrões táticos de DDD (padronizados)

Já aplicados em `PR-001-04`, `MMS-002-04`, `MMS-004-04`; este documento os eleva a padrão da plataforma.

| Padrão | Regra na plataforma |
|--------|---------------------|
| **Aggregate** | Unidade de consistência transacional. Uma transação = um agregado modificado (regra geral). Referência a outro agregado é **por ID**, nunca por objeto. |
| **Aggregate Root** | Único ponto de entrada; garante invariantes (`INV-*`). Toda mutação passa pela raiz. |
| **Value Object** | Imutável, sem identidade, validado na construção (ex.: `Quantity`, `Money`, códigos). |
| **Domain Event** | Fato de negócio no passado (ADR-010); emitido pela raiz; nome de negócio (`...Submitted`, `...Received`). |
| **Domain Service** | Regra que não pertence a um único agregado; sem estado. |
| **Policy / Specification** | Regra de negócio reutilizável e testável isoladamente. |
| **Repository** | Abstrai persistência do agregado; **não escreve projeções derivadas** (ex.: saldo de estoque é projeção, não escrita direta — MMS-004-04, ADR-009). |
| **Factory** | Criação consistente de agregados (ex.: `Item.CreateEPI`). |

---

## 4. Fluxo de Command (escrita)

```text
Controller → CommandHandler (Application)
  1. Abre transação (Unit of Work)
  2. Carrega Aggregate via Repository
  3. Invoca método de domínio na raiz → valida invariantes, muda estado
  4. Raiz registra Domain Event(s)
  5. Repository persiste o agregado
  6. Eventos gravados na tabela OUTBOX na MESMA transação  [ADR-010, ARC-005]
  7. Commit  (atomicidade domínio + evento)
  8. Publisher assíncrono relê Outbox → RabbitMQ (at-least-once)
```

**Idempotência:** comandos que criam efeito externo aceitam `Idempotency-Key` (header); a Application deduplica (SEC/`*-13-api`). Isso protege contra reenvio de rede.

---

## 5. Fluxo de Query (leitura)

```text
Controller → QueryHandler (Application)
  1. Consulta modelo de leitura (read model / view / projeção)
  2. Aplica filtro obrigatório company_id (multi-tenant)  [ADR-009, SEC-001]
  3. Paginação keyset (não offset) para estabilidade e performance  [*-11-database]
  4. Cache Redis quando aplicável (com invalidação por evento)
  5. Retorna DTO (nunca expõe o agregado diretamente)
```

**CQRS leve:** separação lógica de comandos e consultas **sem** bancos separados no MVP. Leituras podem usar views/MVs (ex.: `MMS-002-11`) e projeções (ex.: saldo de estoque MMS-004) alimentadas por evento. Bancos de leitura dedicados são roadmap (exige ADR).

---

## 6. Contrato com Foundation (dependências internas)

Todo contexto consome o Foundation por porta de aplicação (interface), nunca acessando o schema do Foundation diretamente:

| Necessidade | Porta consumida | Implementação |
|-------------|-----------------|---------------|
| Quem é o usuário / permissões | IAM (FD-001-01) | Motor de autorização + JWT claims |
| Estrutura organizacional / escopo | Organization (FD-001-02) | Filtro de escopo `company_id`/unidade |
| Anexos | Document Management (FD-001-03) | Referência a objeto MinIO |
| Aprovações | Workflow Engine (FD-001-04) | Início/consulta de fluxo por evento |
| Notificar | Notification Center (FD-001-05) | Publicação de evento notificável |
| Auditar | Audit Service (FD-001-06) | Evento de negócio → trilha append-only |
| Linha do tempo | Timeline (FD-001-07) | Projeção por evento |
| Comentários/menções | Collaboration (FD-001-08) | Herança de permissão da entidade |
| Catálogos de referência | Master Data (FD-001-09) | Consulta com cache invalidado por evento |
| Parâmetros/flags | Configuration (FD-001-10) | Leitura tipada com escopo |

Módulo **não reimplementa** capacidade do Foundation (ADR-011).

---

## 7. Tratamento de erros e resultado

- **Domínio** sinaliza violação de invariante com exceção/`Result` de domínio tipado (ex.: `IC-ERR-*`, `IV-ERR-*`, `PR-ERR-*`).
- **Application** traduz para um **catálogo de erro** do módulo.
- **Presentation** mapeia para HTTP conforme `*-13-api` (ex.: recurso fora do escopo → **404** anti-IDOR, nunca 403 revelando existência — SEC-001 §6).
- **Fail securely** (SEC-001): erro fecha acesso, não vaza detalhe interno.

---

## 8. Padrões de projeto e SOLID

| Princípio/Padrão | Aplicação |
|------------------|-----------|
| **SRP** | Cada handler = um caso de uso; cada agregado = uma consistência. |
| **DIP** | Application define portas; Infrastructure implementa. Domínio nunca depende de detalhe. |
| **OCP** | Novas regras via Policy/Specification, não `if` espalhado. |
| **ISP** | Portas pequenas e específicas por necessidade. |
| **Mediator/Handler** | Command/Query handlers desacoplam Presentation de Application. |
| **Outbox** | Consistência entre estado e evento (ARC-005). |
| **ACL** | Isolamento de integrações externas (ARC-002 §4). |

---

## 9. Estrutura de solução de referência (.NET)

Organização por **contexto**, não por camada global (TPES-002):

```text
src/
  Foundation/           (BC-FND)  Domain/ Application/ Infrastructure/ Api/
  Procurement/          (BC-PRC)  Domain/ Application/ Infrastructure/ Api/
  Materials/            (BC-MMS)  Domain/ Application/ Infrastructure/ Api/
  Supplier/  (roadmap)
  Contract/  (roadmap)
  Analytics/ (roadmap)
  BuildingBlocks/       kernel técnico compartilhado (Result, Outbox, Money, IDs…)
host/
  Api/                  composição do Modular Monolith (DI, pipeline, endpoints)
  Worker/               consumidores de eventos (RabbitMQ)
```

`BuildingBlocks` é **técnico** (não é domínio de negócio) e não cria acoplamento entre contextos.

---

## 10. Rastreabilidade

| Este documento | Deriva de / realiza |
|----------------|---------------------|
| Camadas Clean Arch | ARC-001 §6, TPES-002 (Clean Arch/SOLID) |
| Padrões táticos DDD | `PR-001-04`, `MMS-002-04`, `MMS-004-04` |
| Outbox / eventos | ADR-010, ARC-005 |
| Banco como projeção / saldo derivado | ADR-009, MMS-004-04 |
| Consumo do Foundation | ADR-011, FD-001 |
| Erros/segurança | SEC-001, SEC-003, `*-13-api` |
