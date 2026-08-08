**Documento:** ARC-005 — Integration & Eventing Architecture
**Versão:** 1.0.0
**Status:** Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: Enterprise Integration Patterns (Hohpe/Woolf), Transactional Outbox, Event-Driven Architecture, ADR-010.
> Documentos relacionados: ARC-001, ARC-002, ARC-004, ADR-010, `*-05-event-storming`, FD-001-04/05/06/07.

# ARC-005 — Integration & Eventing Architecture

> Define **como os Bounded Contexts se comunicam**: quando síncrono, quando assíncrono, o padrão de publicação/consumo de eventos, garantias de entrega, idempotência, ordenação e tratamento de falhas. Consolida e operacionaliza a ADR-010 (Business Domain Events como linguagem oficial).

---

## 1. Princípios de integração

1. **Assíncrono por padrão entre contextos** — eventos de negócio desacoplam produtor e consumidor.
2. **Síncrono só quando indispensável** — leitura que exige resposta imediata (ex.: validar existência de item no momento do comando).
3. **Eventos são de negócio** — nunca técnicos (`RowUpdated` é proibido — ADR-010).
4. **RabbitMQ só quando há real necessidade** de assíncrono (TPES-002); nem toda operação vira evento externo.
5. **O estado é do Postgres, não da fila** — a mensagem transporta um fato já persistido (Outbox), nunca é a fonte de verdade.

---

## 2. Estilos de comunicação

| Estilo | Quando usar | Mecanismo | Exemplo |
|--------|-------------|-----------|---------|
| **Síncrono (REST interno)** | Consulta pontual necessária para completar um comando | Chamada de porta de aplicação / API interna versionada | Procurement valida item ativo no Catalog |
| **Assíncrono (evento)** | Reação a fato consumado; propagação para múltiplos consumidores | RabbitMQ + Outbox | `PurchaseRequisitionSubmitted` → Workflow, Notification, Audit, Timeline |
| **Integração externa (roadmap)** | ERP/SSO/portal fornecedor | Adapter com **Anti-Corruption Layer** | Sincronização de item com ERP (v2) |

Regra: **nunca** join direto no schema de outro contexto (ARC-002 §4).

---

## 3. Transactional Outbox (padrão obrigatório)

Garante que **persistir o estado** e **publicar o evento** sejam atômicos, sem depender de commit distribuído.

```text
Transação de negócio (uma transação de banco):
   ┌──────────────────────────────────────────────┐
   │ 1. UPDATE/INSERT no agregado (schema próprio) │
   │ 2. INSERT do evento na tabela `outbox`        │
   └──────────────────────────────────────────────┘
                 │ commit atômico
                 ▼
   Outbox Publisher (processo separado / worker)
   3. SELECT eventos não publicados (ordenado)
   4. PUBLISH no RabbitMQ (exchange do contexto)
   5. Marca como publicado (ou incrementa retry)
```

- Se a publicação falhar, o evento permanece na Outbox e é reenviado → **at-least-once**.
- Consumidores devem ser **idempotentes** (§6), pois "pelo menos uma vez" implica possível duplicata.

---

## 4. Topologia de mensageria (RabbitMQ)

```text
Produtor (contexto) ──► Exchange (topic) ──► Binding (routing key) ──► Queue (por consumidor) ──► Consumer
                         trino.materials         evt.inventory.received      q.audit.materials       Audit
                                                                             q.notif.materials       Notification
                                                                             q.timeline.materials    Timeline
```

| Elemento | Convenção | Exemplo |
|----------|-----------|---------|
| Exchange | `trino.<suite/contexto>`, tipo `topic`, durável | `trino.materials`, `trino.procurement`, `trino.foundation` |
| Routing key | `evt.<contexto>.<evento>` | `evt.inventory.received`, `evt.pr.submitted` |
| Queue | `q.<consumidor>.<contexto>`, durável | `q.audit.materials` |
| DLX/DLQ | `dlx.<contexto>` + `q.dead.<contexto>` | mensagens que esgotam retry |

Já normativo por MMS-002/004: exchange `trino.materials`, Outbox, at-least-once, DLQ, idempotência, ordenação por agregado.

---

## 5. Garantias de entrega e retry

| Garantia | Decisão |
|----------|---------|
| Entrega | **At-least-once** (Outbox + ack manual do consumidor) |
| Confirmação de produção | Publisher confirms do RabbitMQ |
| Consumo | Ack **após** processamento com sucesso; nack→requeue/limite |
| Retry | Backoff exponencial com teto; após N tentativas → **DLQ** |
| Dead-letter | Inspeção manual/operacional; reprocessamento controlado |
| Perda zero de fato de negócio | Fato vive no Postgres (Outbox); fila é transporte reenviável |

---

## 6. Idempotência e deduplicação

Como a entrega é at-least-once, **todo consumidor é idempotente**:

- Cada evento carrega um **`eventId`** único (UUID) e metadados (`aggregateId`, `occurredAt`, `version`).
- Consumidor mantém registro de `eventId` processados (inbox/dedup) por escopo.
- Reprocessar um evento já visto é **no-op**.
- Comandos de entrada na API usam `Idempotency-Key` (ARC-004 §4).

---

## 7. Ordenação

- Ordenação **por agregado / chave de saldo**, não global (MMS-004 já define ordenação por chave de saldo).
- Mensagens do mesmo agregado preservam ordem (routing/particionamento por `aggregateId`).
- Consumidores não assumem ordem entre agregados distintos.

---

## 8. Envelope de evento (Published Language)

Contrato mínimo de todo evento de negócio (ADR-010). O payload de negócio é definido no `*-05-event-storming` de cada módulo.

```json
{
  "eventId": "uuid",
  "eventType": "InventoryReceived",
  "context": "materials",
  "aggregateType": "StockMovement",
  "aggregateId": "uuid",
  "companyId": "uuid",
  "occurredAt": "2026-08-08T12:00:00Z",
  "version": 1,
  "correlationId": "uuid",
  "causationId": "uuid",
  "data": { "...payload de negócio conforme *-05..." }
}
```

- **Versionado**: mudanças de contrato são retrocompatíveis; quebra exige nova `version` e período de convivência.
- **Sem dados sensíveis** desnecessários no payload (SEC-001); referências por ID.
- `correlationId`/`causationId` sustentam rastreabilidade fim-a-fim e a Timeline (FD-001-07).

---

## 9. Consumidores transversais do Foundation

Vários serviços do Foundation existem **para reagir a eventos** de todos os contextos:

| Consumidor | Reage a | Efeito |
|------------|---------|--------|
| Workflow Engine (FD-001-04) | Eventos que exigem aprovação (ex.: `...Submitted`) | Inicia/avança fluxo |
| Notification Center (FD-001-05) | Eventos notificáveis | Envia SYSTEM/EMAIL com template versionado |
| Audit Service (FD-001-06) | **Todos** os eventos de negócio | Trilha imutável append-only |
| Timeline Service (FD-001-07) | Eventos com marco relevante | Projeção cronológica por entidade |
| Analytics (roadmap) | Eventos de negócio | Read models/KPIs |

Assim, adicionar um novo módulo não exige alterar Foundation: basta o módulo **publicar seus eventos** na Published Language.

---

## 10. Integrações externas (roadmap, via ACL)

O MVP não integra ERP/SSO/portal de fornecedor (TPES-002). A arquitetura reserva o padrão:

```text
Sistema externo ⇄ [ Adapter + Anti-Corruption Layer ] ⇄ Evento/Comando interno
```

- O ACL traduz o modelo externo para a linguagem do domínio; **nenhum contexto conhece** o formato externo.
- Entrada de dados externos gera eventos de negócio internos como qualquer outra origem.
- Cada integração externa é uma decisão que **exige ADR**.

---

## 11. Rastreabilidade

| Este documento | Deriva de / realiza |
|----------------|---------------------|
| Eventos de negócio, proibição de eventos técnicos | ADR-010 |
| Outbox, at-least-once, DLQ, idempotência, ordenação | MMS-002-05, MMS-004-05 |
| Consumidores transversais | FD-001-04/05/06/07 |
| Fronteiras / sem join cross-contexto | ARC-002 §4 |
| ACL para externos | ARC-002 §4, TPES-002 (roadmap) |
