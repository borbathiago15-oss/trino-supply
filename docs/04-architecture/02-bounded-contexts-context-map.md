**Documento:** ARC-002 — Bounded Contexts & Context Map
**Versão:** 1.0.0
**Status:** Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: Domain-Driven Design (Strategic Design, Context Mapping — Evans/Vernon).
> Documentos relacionados: TPES-002 (Domínios da Plataforma), ARC-001, ARC-005, FD-001, PR-001, MMS-001, ADR-010/011/012.

# ARC-002 — Bounded Contexts & Context Map

> Define os **Bounded Contexts** oficiais do Trino Supply, suas responsabilidades, fronteiras e relações (Context Map). É a base para a organização de código, schemas de banco e contratos de evento. Nenhum código é organizado por camada técnica global — sempre por contexto (TPES-002).

---

## 1. Contextos oficiais

Os contextos derivam diretamente dos "Domínios da Plataforma" do TPES-002 e dos módulos já documentados em 03-business.

| Código | Bounded Context | Tipo | Módulos / documentos |
|--------|-----------------|------|----------------------|
| BC-FND | **Foundation** (Core Platform) | Suporte / Compartilhado | FD-001 (IAM, Organization, Document, Workflow, Notification, Audit, Timeline, Collaboration, Master Data, Configuration) |
| BC-PRC | **Procurement** | Core (principal) | PR-001 (Purchase Requisition); RFQ, Equalização, Purchase Order (roadmap) |
| BC-MMS | **Materials Management** | Core | MMS-001..005 (Item Catalog, Material Requisition, Inventory, Receiving) |
| BC-SUP | **Supplier Management** | Core | Roadmap (Cadastro, Homologação, Avaliação, Documentação) |
| BC-CTR | **Contract Management** | Core | Roadmap (Contratos, Vigências, Alertas) |
| BC-RCV | **Receiving** | Core | MMS-005 (propriedade MMS por ADR-012) |
| BC-ANL | **Analytics** | Genérico (leitura) | Roadmap (Dashboards, KPIs, Relatórios) |
| BC-ADM | **Administration** | Suporte | Configuração operacional (consome FD-001-10) |
| BC-AUD | **Audit** | Suporte / Transversal | FD-001-06 (trilha imutável, consumido por todos) |

> **Nota de propriedade (ADR-012):** *Receiving* e *Inventory (futuro)* pertencem à **Materials Management Suite**. *Purchasing* permanece em *Procurement*. Este documento respeita essa decisão: BC-RCV é realizado dentro da suíte MMS.

---

## 2. Classificação estratégica (Core / Supporting / Generic)

| Classe | Contextos | Implicação de investimento |
|--------|-----------|----------------------------|
| **Core Domain** | Procurement, Materials Management, Supplier, Contract | Maior esforço de modelagem; diferencial do produto. |
| **Supporting** | Foundation, Administration, Audit | Habilitadores; padronização e reuso (ADR-011). |
| **Generic** | Analytics, Notification (dentro do Foundation), Document/MinIO | Comprável/substituível; menor customização. |

---

## 3. Context Map (relações e padrões DDD)

Padrões DDD utilizados: **CF** = Conformist, **ACL** = Anti-Corruption Layer, **OHS** = Open Host Service, **PL** = Published Language, **CS/SK** = Customer-Supplier / Shared Kernel, **U/D** = Upstream/Downstream.

```text
                         ┌───────────────────────────────┐
                         │        BC-FND Foundation       │  OHS + Published Language
                         │  IAM · Org · Workflow · Notif  │  (upstream de todos)
                         │  Audit · Timeline · MasterData │
                         └───────────────────────────────┘
        ┌───────────┬───────────┬─────────┴──────┬───────────┬───────────┐
     D  │ CF        │ CF        │ CF             │ CF        │ CF        │ D
   ┌────▼────┐ ┌────▼────┐ ┌────▼─────┐    ┌─────▼────┐ ┌────▼────┐ ┌────▼────┐
   │ BC-PRC  │ │ BC-MMS  │ │ BC-SUP   │    │ BC-CTR   │ │ BC-RCV  │ │ BC-ADM  │
   │Procure  │ │Materials│ │Supplier  │    │Contract  │ │Receiving│ │Admin    │
   └────┬────┘ └────┬────┘ └──────────┘    └──────────┘ └────┬────┘ └─────────┘
        │  events   │  events                                 │
        │ (PL)      │ (PL)                                    │(RCV entrega → MMS Inventory)
        └─────┬─────┴───────────────┬─────────────────────────┘
              ▼                     ▼
        ┌───────────┐        ┌─────────────┐
        │  BC-ANL   │        │   BC-AUD    │  consome eventos de todos (D)
        │ Analytics │        │   Audit     │
        └───────────┘        └─────────────┘
```

### 3.1 Relações principais

| Upstream → Downstream | Padrão | Mecanismo | Observações |
|-----------------------|--------|-----------|-------------|
| Foundation → todos os Core | OHS + Published Language + Conformist | API interna + eventos FD | Módulos consomem serviços do Foundation e **não** reimplementam (ADR-011). Não há dependência inversa. |
| Procurement → Materials | Customer-Supplier | Eventos de negócio | Demanda de compra ↔ requisição de material com rastreabilidade bidirecional (MMS-003 ↔ PR-001). |
| Receiving → Materials/Inventory | Shared context (mesma suíte) | Chamada de domínio interna à suíte | Entrada no estoque via MMS-004 após conferência (MMS-005, MMS-RG-06). |
| Todos → Audit | Published Language | Eventos de negócio (ADR-010) | Trilha imutável append-only (FD-001-06). |
| Todos → Analytics | Published Language | Eventos + read models | Analytics é somente leitura; nunca escreve no domínio dos outros. |
| Integrações externas (ERP, roadmap) | Anti-Corruption Layer | Adapter dedicado | O ACL protege o domínio de modelos externos; nenhum contexto conhece o formato do ERP diretamente. |

---

## 4. Regras de fronteira (invioláveis)

1. **Sem dependência circular** entre contextos Core.
2. **Foundation não depende de módulo de negócio** (FD-001, princípio 2).
3. Comunicação entre contextos é por **contrato explícito**: evento de negócio (assíncrono, preferencial) ou API interna versionada (síncrono, quando indispensável). **Nunca** por join direto de tabelas de outro contexto.
4. Cada contexto é dono do seu **schema** no PostgreSQL (ex.: `materials`); outro contexto não lê tabela alheia — consome evento ou API.
5. Toda integração com sistema externo passa por **ACL** (roadmap ERP/SSO).
6. **Published Language** = catálogo de eventos por módulo (`*-05-event-storming`); é a linguagem oficial (ADR-010).

---

## 5. Mapa contexto → schema de banco → exchange de evento

| Contexto | Schema PostgreSQL | Exchange RabbitMQ | Prefixo de evento |
|----------|-------------------|-------------------|-------------------|
| Foundation | `foundation` (por capacidade: `iam`, `org`, `audit`, …) | `trino.foundation` | `FD-EVT`, `WF-EVT`, … |
| Procurement | `procurement` | `trino.procurement` | `PR-EVT` |
| Materials Management | `materials` | `trino.materials` | `EVT-IC`, `EVT-IV`, … |
| Supplier | `supplier` (roadmap) | `trino.supplier` | `SUP-EVT` (roadmap) |
| Contract | `contract` (roadmap) | `trino.contract` | `CTR-EVT` (roadmap) |
| Analytics | `analytics` (read models) | — (consumidor) | — |
| Audit | `audit` | — (consumidor) | — |

> Os prefixos e exchanges já usados por MMS-002/MMS-004 (`trino.materials`, `EVT-IC`/`EVT-IV`) são normativos; novos contextos seguem o mesmo padrão.

---

## 6. Ubiquitous Language (referência)

Cada contexto mantém seu glossário no respectivo `*-01-business-context`. Termos com significado diferente entre contextos **devem** ser qualificados pelo contexto (ex.: "Item" no Catalog vs. "linha de movimentação" no Inventory) — nunca compartilhar um modelo ambíguo entre fronteiras.

---

## 7. Rastreabilidade

| Este documento | Deriva de / realiza |
|----------------|---------------------|
| Lista de contextos | TPES-002 (Domínios da Plataforma) |
| Foundation upstream | ADR-011, FD-001 |
| Propriedade de Receiving/Inventory | ADR-012 |
| Eventos como Published Language | ADR-010, `*-05-event-storming` |
| Mapa contexto→schema | ADR-009, `*-11-database` |
| Transporte de eventos | ARC-005 |
