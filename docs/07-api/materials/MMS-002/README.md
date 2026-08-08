# MMS-002-13 — API do Item Catalog

| Campo | Valor |
|---|---|
| **Documento** | MMS-002-13 |
| **Módulo** | Materials — Item Catalog (MMS-002) |
| **Versão** | 1.1.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-08-08 |
| **Dependências** | MMS-002 (Visão) v1.2.0, MMS-002-02 (Business Rules) v1.2.0, MMS-002-03 (State Machine), MMS-002-05 (Event Storming), MMS-002-07 (Use Cases) v1.1.0, MMS-002-09 (Permissions), MMS-002-11 (Database) v1.1.0, ADR-013 (Conversão de UoM), ADR-014 (Motor de Regras de Reposição), FD-001-01 (IAM), FD-001-03 (Documents/Storage), FD-001-05 (Notifications), FD-001-06 (Audit), FD-001-07 (Timeline), FD-001-09 (Master Data), FD-001-10 (Configuration), SEC-001/003, ADR-009, ADR-010 |
| **Escopo** | API REST pública `v1` do módulo Item Catalog |

---

## 1. Objetivo

Este documento é a especificação oficial da **API REST do módulo Item Catalog**. Ele consolida os endpoints referenciados pelos casos de uso (MMS-002-07) em um contrato único, com convenções, modelos de dados, códigos de status, erros, segurança e versionamento.

**Regra-mestre:** este documento descreve; as regras de negócio permanecem em MMS-002-02, as transições em MMS-002-03, as permissões em MMS-002-09 e os eventos em MMS-002-05. Em caso de conflito, prevalecem os documentos de negócio.

---

## 2. Convenções Gerais

### 2.1 Base URL e versionamento

- Base: `/api/v1`
- Versionamento por path (`v1`); mudanças incompatíveis exigem `v2` — nunca quebra em versão publicada.
- Contrato formal em OpenAPI 3.1 gerado a partir deste documento (artefato de build), com este texto como fonte semântica.

### 2.2 Envelope de resposta

**Sucesso (recurso único):**

```json
{
  "data": { "id": "…", "code": "EPI-LUV-001" },
  "correlationId": "01J…"
}
```

**Sucesso (coleção, keyset):**

```json
{
  "data": [ … ],
  "page": { "nextCursor": "eyJ…", "hasMore": true, "pageSize": 20 },
  "correlationId": "01J…"
}
```

**Erro:**

```json
{
  "error": {
    "code": "IC-ERR-081",
    "message": "O Certificado de Aprovação (CA) informado está vencido.",
    "details": [{ "field": "caNumber", "reason": "EXPIRED" }],
    "correlationId": "01J…",
    "traceId": "…"
  }
}
```

### 2.3 Paginação (keyset)

- Ordenação padrão: `(code, id) ASC` no catálogo; `(created_at, id) DESC` em histórico/timeline; cursor **assinado**, validade 15 minutos (padrão Foundation).
- Parâmetros: `cursor`, `pageSize` (padrão 20, máx. 100).
- Offset/limit é **proibido** (ADR do Foundation — keyset pagination; MMS-002-11 §Keyset).
- Cursor inválido/expirado ⇒ `400 IC-ERR-400`.

### 2.4 Idempotência

- Endpoints de escrita aceitam header `Idempotency-Key` (UUID por operação do cliente); replay dentro de 24h retorna a resposta original (`200`/`201` com `idempotentReplay: true`).
- Sem a chave, dupla submissão é tratada pelas regras de estado e unicidade (ex.: `409 IC-ERR-010` código duplicado, `409 IC-ERR-090` estado não permite).

### 2.5 Autenticação e autorização

- Bearer JWT (15 min) + refresh rotativo (FD-001-01, SEC-003).
- Scopes do módulo: `items.read`, `items.write`, `items.lifecycle`, `items.admin`, `items.audit` (MMS-002-09).
- Fluxo de autorização por request: **escopo (404) → RBAC → ABAC (ABAC-IC-01..05) → delegação (403)** — deny by default (POL-IC-AUTH-001..008).
- Todo `403` e toda ação sensível geram trilha de auditoria (FD-001-06).

### 2.6 Cabeçalhos obrigatórios e suportados

| Header | Uso |
|---|---|
| `Authorization: Bearer` | Obrigatório em todos os endpoints. |
| `X-Correlation-Id` | Opcional; se ausente, o servidor gera. Propagado a eventos e auditoria. |
| `Idempotency-Key` | Recomendado em POST/PUT/PATCH/DELETE de escrita. |
| `If-Match` | Obrigatório em PATCH/PUT/DELETE de recursos versionados (controle otimista; `409 IC-ERR-409` em conflito). |
| `Accept-Language` | `pt-BR` (padrão) ou `en-US` — afeta `message` de erros. |

### 2.7 Códigos HTTP

| Código | Uso |
|---|---|
| `200` | Sucesso em consulta/ação síncrona. |
| `201` | Recurso criado (com `Location`). |
| `204` | Exclusão lógica bem-sucedida. |
| `400` | Validação de entrada (`IC-ERR-0xx` de formato, cursor, payload). |
| `401` | Token ausente/inválido/expirado. |
| `403` | Autenticado, mas sem permissão (RBAC/ABAC/delegação) — `IC-ERR-900`. |
| `404` | Recurso inexistente **ou fora do escopo** do usuário (anti-enumeração). |
| `409` | Conflito de unicidade (`IC-ERR-010/011/040`), de estado (`IC-ERR-090`) ou de versão (`IC-ERR-409`). |
| `422` | Regra de negócio violada com payload válido (ex.: CA obrigatório/vencido, completude, campo estrutural imutável). |
| `429` | Rate limit excedido (`Retry-After` presente) — `IC-ERR-429`. |
| `500` | Erro interno (sempre com `correlationId`; nunca stack trace). |

### 2.8 Rate limit

| Classe | Limite padrão | Escopo |
|---|---|---|
| Leitura | 600 req/min | por usuário |
| Escrita | 120 req/min | por usuário |
| Ações de ciclo de vida | 60 req/min | por usuário |

Resposta `429` com corpo de erro `IC-ERR-429` e `Retry-After`. Limites ajustáveis via Configuration (`materials.item.rate-limit.*`, FD-001-10).

---

## 3. Modelo de Recursos (contratos)

### 3.1 `Item`

```json
{
  "id": "uuid",
  "code": "EPI-LUV-001",
  "erpCode": "1002345",
  "description": "Luva de segurança vaqueta",
  "group": "EPI | FARDAMENTO",
  "baseUnitOfMeasure": "UN",
  "unitOfMeasure": "UN",
  "alternativeUnits": [
    { "uom": "CX", "conversionFactor": 12, "role": "PURCHASE | CONSUMPTION | null" }
  ],
  "status": "DRAFT | ACTIVE | INACTIVE | DISCARDED",
  "ca": {
    "caNumber": "12345",
    "issuedAt": "2026-01-15",
    "expiresAt": "2031-01-14",
    "status": "VALID | EXPIRING | EXPIRED"
  },
  "sizeGrid": ["P", "M", "G", "GG"],
  "image": { "attachmentId": "uuid", "downloadUrl": "assinada ≤ 5 min" },
  "synonyms": ["luva vaqueta", "luva de couro"],
  "replenishmentParameters": {
    "reorderPoint": 50,
    "minStock": 30,
    "maxStock": 200,
    "safetyStock": 20,
    "policy": "ATE_MAXIMO | MULTIPLO_EMBALAGEM | LOTE_FIXO | null",
    "supplyRoute": "TRANSFERENCIA | COMPRA | MANUAL | null",
    "fixedLotQuantity": "number | null"
  },
  "version": 4,
  "createdAt": "2026-07-30T14:00:00Z",
  "updatedAt": "2026-07-30T15:10:00Z",
  "activatedAt": "…|null",
  "inactivatedAt": "…|null"
}
```

- `group` por código (`EPI`/`FARDAMENTO`); `unitOfMeasure` referencia Master Data por código (FD-001-09).
- `ca` **obrigatório** para `group=EPI` (IC-BR-080/081); proibido para `FARDAMENTO`.
- `sizeGrid` restrito aos valores do domínio FD-001-09 SIZE_GRID (IC-BR-082).
- `image` via FD-001-03 (MinIO); conteúdo nunca em bytes no JSON — URL assinada ≤ 5 min (IC-BR-083, SEC-001).
- `baseUnitOfMeasure` é a unidade em que o saldo é mantido (ADR-013, IC-BR-090); `unitOfMeasure` é mantido como alias da base por compatibilidade v1. `alternativeUnits` lista unidades com `conversionFactor > 0` na **mesma categoria** da base (IC-BR-091/092); toda quantidade trafega/persiste na base — a API nunca expõe saldo em unidade alternativa.
- `replenishmentParameters.policy`/`supplyRoute` parametrizam a regra de reposição avaliada pelo MMS-004 (ADR-014, IC-BR-100/101); `fixedLotQuantity` é obrigatório quando `policy=LOTE_FIXO`.
- `version` sustenta `If-Match` (controle otimista).
- `status` segue a máquina de estados MMS-002-03 (`ST-IC-001..004`); `DISCARDED` é terminal.

### 3.2 `ItemSynonym`

```json
{
  "synonymId": "uuid",
  "itemId": "uuid",
  "term": "luva vaqueta",
  "normalizedTerm": "luva vaqueta",
  "createdBy": "uuid",
  "createdAt": "…"
}
```

- Unicidade `(company_id, normalized_term, item_id)` — duplicidade ⇒ `409 IC-ERR-040` (IC-BR-040..043).

### 3.3 `ItemListView` (listagem/busca)

```json
{
  "id": "uuid",
  "code": "EPI-LUV-001",
  "description": "Luva de segurança vaqueta",
  "group": "EPI",
  "unitOfMeasure": "PAR",
  "status": "ACTIVE",
  "caStatus": "VALID | EXPIRING | EXPIRED | NOT_APPLICABLE | MISSING",
  "imageThumbnailUrl": "assinada ≤ 5 min|null",
  "matchedBy": "DESCRIPTION | SYNONYM | ERP_CODE | CODE"
}
```

- `matchedBy` indica como o termo `q` casou (descrição > sinônimo > código ERP > código) — evidencia a busca por sinônimo da Jornada B (MMS-002-12).

### 3.4 `CompletenessCheckView` (checklist de ativação)

```json
{
  "itemId": "uuid",
  "activable": false,
  "gaps": [
    { "rule": "IC-BR-081", "field": "ca", "message": "CA vencido em 2026-01-14" }
  ],
  "warnings": [
    { "rule": "IC-BR-083", "field": "image", "message": "Item EPI sem imagem" }
  ]
}
```

### 3.5 `HistoryEntryView` / `TimelineEntryView`

- `HistoryEntryView`: `{ entryId, action, actor, actorLabel, reason, version, occurredAt, changes }` — auditoria funcional do recurso (FD-001-06).
- `TimelineEntryView`: conforme FD-001-07 (`milestone`, `summary`, `actor`, `occurredAt`, `presentation`, `details`).

---

## 4. Catálogo de Endpoints

### 4.1 Itens (ciclo de vida)

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 1 | `POST` | `/api/v1/items` | UC-IC-001 | IC-PERM-001 (`items.write`) | Cria em `DRAFT`; `201` + `id`/`code`; publica EVT-IC-001 |
| 2 | `GET` | `/api/v1/items?cursor=&pageSize=&status=&group=&q=&caStatus=&hasImage=` | UC-IC-007 | IC-PERM-009 (`items.read`) | Listagem/busca com keyset; `q` casa descrição/sinônimo/ERP/código; sempre filtrada por escopo (company) |
| 3 | `GET` | `/api/v1/items/{id}` | UC-IC-007 | IC-PERM-009 (`items.read`) | Detalhe completo (item + sinônimos + CA + grade + parâmetros); `404` fora do escopo |
| 4 | `PATCH` | `/api/v1/items/{id}` | UC-IC-003 | IC-PERM-002 (`items.write`) | Edita campos permitidos (descrição, dados não estruturais); campos estruturais (`code`, `group`, `unitOfMeasure`) somente em `DRAFT`; `If-Match` obrigatório; publica EVT-IC-002 |
| 5 | `DELETE` | `/api/v1/items/{id}` | UC-IC-001 | IC-PERM-003 (`items.write`) | Exclui rascunho (somente `DRAFT`, soft delete); `204` |
| 6 | `POST` | `/api/v1/items/{id}/activate` | UC-IC-002 | IC-PERM-004 (`items.lifecycle`) | Ativa; corpo `{ reason }` obrigatório; valida completude (IC-BR-020..023/080..083); publica EVT-IC-003 |
| 7 | `POST` | `/api/v1/items/{id}/inactivate` | UC-IC-004 | IC-PERM-005 (`items.lifecycle`) | Inativa; corpo `{ reason }` obrigatório; publica EVT-IC-004 |
| 8 | `POST` | `/api/v1/items/{id}/reactivate` | UC-IC-005 | IC-PERM-006 (`items.lifecycle`) | Reativa; corpo `{ reason }` obrigatório; revalida completude; publica EVT-IC-005 |
| 9 | `POST` | `/api/v1/items/{id}/discard` | UC-IC-006 | IC-PERM-007 (`items.admin`) | Descarta (terminal); corpo `{ reason }` obrigatório; somente se nunca consumido (IC-BR-070); publica EVT-IC-007 |
| 10 | `GET` | `/api/v1/items/{id}/completeness` | UC-IC-002 | IC-PERM-009 (`items.read`) | Checklist de completude para ativação (§3.4); alimenta a UX de "o que falta" |

### 4.2 Sinônimos

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 11 | `POST` | `/api/v1/items/{id}/synonyms` | UC-IC-003 | IC-PERM-002 (`items.write`) | Adiciona sinônimo; corpo `{ term }`; normalização server-side; `201`; publica EVT-IC-002 |
| 12 | `DELETE` | `/api/v1/items/{id}/synonyms/{synonymId}` | UC-IC-003 | IC-PERM-002 (`items.write`) | Remove sinônimo; `204`; auditado |

### 4.3 CA, grade de tamanhos, imagem e parâmetros

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 13 | `PUT` | `/api/v1/items/{id}/ca` | UC-IC-003 | IC-PERM-002 (`items.write`) | Registra/atualiza CA; corpo `{ caNumber, issuedAt, expiresAt }`; somente `group=EPI`; publica EVT-IC-006 |
| 14 | `DELETE` | `/api/v1/items/{id}/ca` | UC-IC-003 | IC-PERM-002 (`items.write`) | Remove CA (bloqueado se `ACTIVE` — IC-BR-081); `204` |
| 15 | `PUT` | `/api/v1/items/{id}/size-grid` | UC-IC-003 | IC-PERM-002 (`items.write`) | Define grade de tamanhos; corpo `{ sizes: [] }` ⊆ SIZE_GRID; `If-Match`; publica EVT-IC-002 |
| 16 | `POST` | `/api/v1/items/{id}/image` | UC-IC-003 | IC-PERM-002 (`items.write`) | Upload multipart da imagem (FD-001-03); `201` + `attachmentId`; publica EVT-IC-008 |
| 17 | `DELETE` | `/api/v1/items/{id}/image` | UC-IC-003 | IC-PERM-002 (`items.write`) | Remove imagem; `204`; auditado |
| 18 | `PUT` | `/api/v1/items/{id}/replenishment-parameters` | UC-IC-007 | IC-PERM-002 (`items.write`) | Define parâmetros de reposição, incluindo `policy`/`supplyRoute`/`fixedLotQuantity` (IC-BR-100/101); valida coerência (IC-BR-060..063); `If-Match`; publica EVT-IC-002 |

### 4.4 Unidades de medida e conversões (ADR-013)

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 19 | `PUT` | `/api/v1/items/{id}/base-unit` | UC-IC-008 | IC-PERM-002 (`items.write`) | Define/altera a unidade de estoque (base); bloqueado após a 1ª movimentação (`422 IC-ERR-112`); `If-Match`; publica EVT-IC-002 |
| 20 | `GET` | `/api/v1/items/{id}/uom-conversions` | UC-IC-008 | IC-PERM-009 (`items.read`) | Lista as unidades alternativas do item (uom, fator, papel) |
| 21 | `POST` | `/api/v1/items/{id}/uom-conversions` | UC-IC-008 | IC-PERM-002 (`items.write`) | Inclui unidade alternativa `{ uom, conversionFactor, role? }`; valida categoria/fator/papel (IC-BR-091/092); `201`; publica EVT-IC-002 |
| 22 | `DELETE` | `/api/v1/items/{id}/uom-conversions/{uom}` | UC-IC-008 | IC-PERM-002 (`items.write`) | Remove unidade alternativa (lógico); `204`; publica EVT-IC-002 |

### 4.5 Auditoria e timeline

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 23 | `GET` | `/api/v1/items/{id}/history?cursor=` | UC-IC-007 | IC-PERM-010 (`items.audit`) | Histórico de auditoria funcional do item (FD-001-06) |
| 24 | `GET` | `/api/v1/items/{id}/timeline?cursor=` | UC-IC-007 | IC-PERM-009 (`items.read`) | Atalho para FD-001-07 (`entityType=item`) |

### 4.6 Mapeamento de eventos publicados

| Endpoint | Eventos (MMS-002-05) | Efeitos colaterais |
|---|---|---|
| `POST /items` | EVT-IC-001 (ItemCreated) | Timeline `created`, auditoria |
| `PATCH /items/{id}`, `PUT size-grid`, `PUT replenishment-parameters`, `POST/DELETE synonyms`, `PUT base-unit`, `POST/DELETE uom-conversions` | EVT-IC-002 (ItemUpdated) | Timeline, invalida cache de busca, auditoria |
| `POST …/activate` | EVT-IC-003 (ItemActivated) — crítico | Libera consumo por MMS-003/MMS-004, notifica (IC-NOT-001), timeline |
| `POST …/inactivate` | EVT-IC-004 (ItemInactivated) — crítico | Bloqueia novo consumo (IC-BR-050), notifica (IC-NOT-002), timeline |
| `POST …/reactivate` | EVT-IC-005 (ItemReactivated) — crítico | Relibera consumo, notifica (IC-NOT-003), timeline |
| `PUT …/ca` | EVT-IC-006 (ItemCaUpdated) | Alerta de vencimento reagendado (IC-NOT-005), timeline |
| `POST …/discard` | EVT-IC-007 (ItemDiscarded) | Remove do catálogo operacional, notifica (IC-NOT-004), timeline |
| `POST …/image` | EVT-IC-008 (ItemImageUpdated) | Timeline, invalida thumbnail |
| Todos os acima | — | Auditoria obrigatória (FD-001-06) com `correlationId` ponta a ponta |

---

## 5. Erros do Módulo

Catálogo consolidado (detalhes e mensagens em MMS-002-02/MMS-002-07; `Accept-Language` se aplica):

| Código | HTTP | Situação |
|---|---|---|
| `IC-ERR-900` | 403 | Sem permissão para a ação (RBAC/ABAC/delegação negou) |
| `IC-ERR-400` | 400 | Cursor de paginação inválido ou expirado |
| `IC-ERR-429` | 429 | Rate limit excedido |
| `IC-ERR-409` | 409 | Conflito de versão (`If-Match` divergente) |
| `IC-ERR-090` | 409 | Estado não permite a operação (transição inválida — MMS-002-03) |
| `IC-ERR-091` | 400 | Motivo (`reason`) obrigatório ausente em ação de ciclo de vida |
| `IC-ERR-010` | 409 | Código já utilizado por outro item ativo/inativo da empresa (IC-BR-010) |
| `IC-ERR-011` | 409 | Código ERP já vinculado a outro item da empresa (IC-BR-011) |
| `IC-ERR-020` | 422 | Completude insuficiente para ativação (lista de gaps no corpo — §3.4) |
| `IC-ERR-030` | 400 | Campos obrigatórios de criação ausentes ou inválidos (descrição, grupo, unidade) |
| `IC-ERR-031` | 422 | Campo estrutural imutável fora de `DRAFT` (`code`/`group`/`unitOfMeasure` — IC-BR-031) |
| `IC-ERR-040` | 409 | Sinônimo duplicado para o item (normalizado — IC-BR-041) |
| `IC-ERR-050` | 422 | Unidade de medida inexistente/inativa no Master Data (FD-001-09) |
| `IC-ERR-060` | 422 | Parâmetros de reposição incoerentes (ex.: mínimo > máximo — IC-BR-061) |
| `IC-ERR-070` | 422 | Descarte não permitido — item já consumido pela operação (IC-BR-070) |
| `IC-ERR-080` | 422 | CA obrigatório ausente para item do grupo EPI (IC-BR-080) |
| `IC-ERR-081` | 422 | CA vencido ou inválido (IC-BR-081) |
| `IC-ERR-082` | 400 | Tamanho fora da grade padrão SIZE_GRID (IC-BR-082) |
| `IC-ERR-083` | 400 | Imagem inválida (tipo/tamanho — política FD-001-03, IC-BR-083) |
| `IC-ERR-110` | 400 | Unidade alternativa inválida: fator ≤ 0 ou papel duplicado (IC-BR-091) |
| `IC-ERR-111` | 422 | Conversão inválida: unidade alternativa de categoria diferente da base (IC-BR-092) |
| `IC-ERR-112` | 422 | Unidade de estoque (base) inválida ou imutável após a 1ª movimentação (IC-BR-090/093) |
| `IC-ERR-120` | 400 | Política de reposição inválida/incompleta — `LOTE_FIXO` exige `fixedLotQuantity > 0` (IC-BR-100) |
| `IC-ERR-121` | 400 | Rota de suprimento inválida para a classificação do item (IC-BR-101) |

---

## 6. Segurança (resumo operacional)

1. **Anti-enumeração:** ausência de escopo ⇒ `404` (nunca `403`) — POL do Foundation; isolamento multi-tenant por `company_id` em todas as consultas (MMS-002-11).
2. **Ciclo de vida sensível:** ativação/inativação/reativação/descarte avaliadas server-side (RBAC `items.lifecycle`/`items.admin` + ABAC-IC), com motivo obrigatório e auditoria (IC-ERR-091).
3. **Mass assignment:** payloads bindados por allowlist de campos por endpoint; campos calculados (`status`, `version`, `code` pós-ativação, timestamps) são somente leitura.
4. **Upload de imagem:** whitelist de content-type (jpeg/png/webp), limite de tamanho, varredura antivírus (FD-001-03, SEC-003); download somente por URL assinada ≤ 5 min (MinIO).
5. **XSS:** descrição e sinônimos sanitizados na entrada e escapados na saída (SEC-003).
6. **CSRF:** API stateless com Bearer — sem cookies de sessão; CSRF não se aplica (SEC-003).
7. **Auditoria:** toda mutação trilhada (FD-001-06); `correlationId` ponta a ponta até o outbox de eventos.

---

## 7. Versionamento e Compatibilidade

- **Aditivo permitido em v1:** novos campos opcionais em respostas, novos filtros, novos endpoints.
- **Proibido em v1:** remover/renomear campos, mudar tipos, alterar semântica de códigos de erro, mudar ordenação padrão.
- **Depreciação:** campo/endpoint marcado `deprecated: true` na OpenAPI por no mínimo 2 releases antes da remoção (que ocorre apenas em `v2`).
- **Contrato de eventos** segue MMS-002-05 (versionamento `v1` por evento, consumo tolerante, idempotência `(eventId, consumerName)`).

---

## 8. Requisitos Não Funcionais da API

| Categoria | Requisito |
|---|---|
| **Desempenho** | P95 ≤ 300 ms em leituras de detalhe; P95 ≤ 500 ms em listagens/busca `q` (índices parciais company_id-first, MMS-002-11). |
| **Disponibilidade** | Ações de ciclo de vida fail-closed em indisponibilidade de auditoria. |
| **Observabilidade** | Métricas por endpoint (latência, taxa de erro, 403/404 ratio), tracing por `correlationId`. |
| **Rate limit** | Conforme §2.8; resposta com `Retry-After`. |
| **Documentação** | OpenAPI 3.1 publicada no portal de desenvolvedores a cada release; exemplos pt-BR. |

---

## 9. Critérios de Conclusão

- [ ] Todos os 24 endpoints implementados conforme este catálogo, com testes de contrato (OpenAPI diff limpo).
- [ ] Fluxo de autorização escopo(404)→RBAC→ABAC→delegação verificado em testes por endpoint.
- [ ] Keyset pagination com cursor assinado em todas as coleções (`IC-ERR-400` testado).
- [ ] Idempotency-Key funcional nos endpoints de escrita.
- [ ] Catálogo de erros IC-ERR implementado e localizado (pt-BR/en-US).
- [ ] Eventos publicados conforme §4.5 com outbox e idempotência de consumidores.
- [ ] Testes relacionados TC-IC-xxx-y (MMS-002-07) verdes na suíte de API.

---

## 10. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Catálogo base (20 endpoints), erros, segurança, keyset, idempotência, OpenAPI 3.1. |
| **v1.1** | **(ADR-013/014, entregue neste documento)** 4 endpoints de unidade base e conversões (`/base-unit`, `/uom-conversions`), parâmetros de reposição com `policy`/`supplyRoute`/`fixedLotQuantity`, erros IC-ERR-110/111/112/120/121. Também previstos: importação em lote do ERP (`POST /api/v1/items/import` com relatório por linha), exportação CSV (`GET /api/v1/items/export.csv`), sugestão de duplicatas (`GET /api/v1/items/{id}/duplicates`). |
| **v2.0** | Webhooks assinados de ciclo de vida do item, bulk lifecycle (`POST /api/v1/items/bulk-activate` com relatório por item), busca full-text dedicada (`GET /api/v1/items/search` com ranking explícito e facetas). |

---

## 11. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Versão inicial aprovada: convenções (envelope, keyset, idempotência, autorização, headers, HTTP, rate limit), modelos de recursos (Item, ItemSynonym, ItemListView, CompletenessCheckView), catálogo de 20 endpoints mapeados a UC/permissão/evento, catálogo de erros IC-ERR, segurança operacional, versionamento, NFRs, critérios de conclusão e roadmap. | Arquitetura Trino |
| 1.1.0 | 2026-08-08 | Incorporação de ADR-013 e ADR-014: recurso `Item` ganha `baseUnitOfMeasure`, `alternativeUnits[]` e `replenishmentParameters.policy/supplyRoute/fixedLotQuantity`; 4 novos endpoints (nº 19–22: `PUT /base-unit`, `GET/POST/DELETE /uom-conversions`) mapeados a UC-IC-008; auditoria/timeline renumeradas para 23–24 (total 24); 5 novos erros IC-ERR-110/111/112/120/121 (faixa livre, sem colisão com os códigos de estado/motivo já usados); mapa de eventos e roadmap atualizados. | Arquitetura Trino |
