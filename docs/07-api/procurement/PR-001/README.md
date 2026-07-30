# PR-001-13 — API do Purchase Requisition

| Campo | Valor |
|---|---|
| **Documento** | PR-001-13 |
| **Módulo** | Procurement — Purchase Requisition (PR-001) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-07-30 |
| **Dependências** | PR-001 (Visão) v1.1.0, PR-001-02 (Business Rules), PR-001-03 (State Machine), PR-001-05 (Event Storming), PR-001-07 (Use Cases), PR-001-09 (Permissions), PR-001-11 (Database), FD-001-01 (IAM), FD-001-03 (Documents), FD-001-04 (Workflow), FD-001-05 (Notifications), FD-001-06 (Audit), FD-001-07 (Timeline), FD-001-08 (Collaboration), FD-001-09 (Master Data), FD-001-10 (Configuration), SEC-001/003 |
| **Escopo** | API REST pública `v1` do módulo Purchase Requisition |

---

## 1. Objetivo

Este documento é a especificação oficial da **API REST do módulo Purchase Requisition**. Ele consolida os endpoints já referenciados pelos casos de uso (PR-001-07) em um contrato único, com convenções, modelos de dados, códigos de status, erros, segurança e versionamento.

**Regra-mestre:** este documento descreve; as regras de negócio permanecem em PR-001-02, as transições em PR-001-03, as permissões em PR-001-09 e os eventos em PR-001-05. Em caso de conflito, prevalecem os documentos de negócio.

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
  "data": { "id": "…", "number": "PR-2026-000123" },
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
    "code": "PR-ERR-021",
    "message": "Centro de custo inválido ou inativo para a unidade selecionada.",
    "details": [{ "field": "costCenterId", "reason": "INACTIVE" }],
    "correlationId": "01J…",
    "traceId": "…"
  }
}
```

### 2.3 Paginação (keyset)

- Ordenação padrão: `(created_at, id) DESC`; cursor **assinado**, validade 15 minutos (padrão Foundation).
- Parâmetros: `cursor`, `pageSize` (padrão 20, máx. 100).
- Offset/limit é **proibido** em endpoints novos (ADR do Foundation — keyset pagination).

### 2.4 Idempotência

- Endpoints de escrita aceitam header `Idempotency-Key` (UUID por operação do cliente); replay dentro de 24h retorna a resposta original (`200`/`201` com `idempotentReplay: true`).
- Sem a chave, dupla submissão é tratada pelas regras de estado (ex.: 409 `PR-ERR-040`).

### 2.5 Autenticação e autorização

- Bearer JWT (15 min) + refresh rotativo (FD-001-01, SEC-003).
- Fluxo de autorização por request: **escopo (404) → RBAC → ABAC → delegação (403)** — deny by default (PR-001-09).
- Todo `403` e toda ação sensível geram trilha de auditoria (FD-001-06, AUD-BR-010).

### 2.6 Cabeçalhos obrigatórios e suportados

| Header | Uso |
|---|---|
| `Authorization: Bearer` | Obrigatório em todos os endpoints. |
| `X-Correlation-Id` | Opcional; se ausente, o servidor gera. Propagado a eventos e auditoria. |
| `Idempotency-Key` | Recomendado em POST/PATCH de escrita. |
| `If-Match` | Obrigatório em PATCH/DELETE de recursos versionados (controle otimista; 409 `PR-ERR-060` em conflito). |
| `Accept-Language` | `pt-BR` (padrão) ou `en-US` — afeta `message` de erros. |

### 2.7 Códigos HTTP

| Código | Uso |
|---|---|
| `200` | Sucesso em consulta/ação síncrona. |
| `201` | Recurso criado (com `Location`). |
| `202` | Ação aceita para processamento assíncrono (ex.: submit com validação assíncrona). |
| `204` | Exclusão lógica bem-sucedida. |
| `400` | Validação de entrada (`PR-ERR-01x/02x`). |
| `401` | Token ausente/inválido/expirado. |
| `403` | Autenticado, mas sem permissão (RBAC/ABAC/delegação). |
| `404` | Recurso inexistente **ou fora do escopo** do usuário (anti-enumeração). |
| `409` | Conflito de estado (`PR-ERR-040`) ou de versão (`PR-ERR-060`). |
| `422` | Regra de negócio violada com payload válido (ex.: transição inválida detalhada). |
| `429` | Rate limit excedido (`Retry-After` presente). |
| `500` | Erro interno (sempre com `correlationId`; nunca stack trace). |

### 2.8 Rate limit

| Classe | Limite padrão | Escopo |
|---|---|---|
| Leitura | 600 req/min | por usuário |
| Escrita | 120 req/min | por usuário |
| Ações de aprovação | 60 req/min | por usuário |

Resposta `429` com corpo de erro `PR-ERR-090` e `Retry-After`. Limites ajustáveis via Configuration (`api.pr.rate-limit.*`, FD-001-10).

---

## 3. Modelo de Recursos (contratos)

### 3.1 `PurchaseRequisition`

```json
{
  "id": "uuid",
  "number": "PR-2026-000123",
  "status": "DRAFT | SUBMITTED | IN_APPROVAL | APPROVED | REJECTED | RETURNED | CANCELLED",
  "cycle": 1,
  "companyId": "uuid",
  "organizationalUnitId": "uuid",
  "costCenterId": "uuid",
  "projectId": "uuid|null",
  "priority": "LOW | NORMAL | HIGH | URGENT",
  "neededBy": "2026-08-15",
  "justification": "string",
  "requesterId": "uuid",
  "requesterLabel": "Ana Souza",
  "totalEstimatedValue": 15230.55,
  "currency": "BRL",
  "category": "TI-004",
  "items": [ "PurchaseRequisitionItem" ],
  "version": 7,
  "createdAt": "2026-07-30T14:00:00Z",
  "updatedAt": "2026-07-30T15:10:00Z",
  "submittedAt": "…|null",
  "decidedAt": "…|null"
}
```

- `currency` e `category` referenciam Master Data por código (FD-001-09); `number` é sequencial por empresa (`fn_next_pr_number`, PR-001-11).
- `version` sustenta `If-Match` (controle otimista).
- `cycle` incrementa a cada resubmissão (PR-001-03/FD-001-04).

### 3.2 `PurchaseRequisitionItem`

```json
{
  "itemId": "uuid",
  "sequence": 1,
  "description": "Notebook 14\" i7 32GB",
  "quantity": 10,
  "unitOfMeasure": "UN",
  "estimatedUnitPrice": 1523.06,
  "estimatedTotal": 15230.60,
  "costCenterId": "uuid|null",
  "category": "TI-004",
  "neededBy": "2026-08-15|null",
  "notes": "string|null"
}
```

- `unitOfMeasure` e `category` por código (FD-001-09); `costCenterId` nulo herda o cabeçalho (PR-001-02, UC-002).

### 3.3 `ApprovalTaskView` (fila de aprovação)

```json
{
  "taskId": "uuid",
  "requisitionId": "uuid",
  "number": "PR-2026-000123",
  "requesterLabel": "Ana Souza",
  "level": 2,
  "totalEstimatedValue": 15230.55,
  "currency": "BRL",
  "submittedAt": "…",
  "slaDueAt": "…",
  "slaStatus": "ON_TIME | DUE_SOON | EXPIRED",
  "assignedVia": "DIRECT | DELEGATION | ESCALATION"
}
```

### 3.4 `AttachmentView` / `CommentView` / `TimelineEntryView`

- `AttachmentView`: `{ attachmentId, fileName, contentType, sizeBytes, uploadedBy, uploadedAt, status }` — conteúdo via URL assinada (FD-001-03; nunca bytes no JSON).
- `CommentView`: `{ commentId, authorLabel, body, threadRootId, internal, createdAt, editedAt }` — `internal=true` visível apenas conforme ABAC-04 (PR-001-09).
- `TimelineEntryView`: conforme FD-001-07 (`milestone`, `summary`, `actor`, `occurredAt`, `presentation`, `details`).

---

## 4. Catálogo de Endpoints

### 4.1 Requisições (ciclo de vida)

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 1 | `POST` | `/api/v1/purchase-requisitions` | UC-001 | PR-PERM-001 | Cria em `DRAFT`; `201` + `id`/`number`; publica EVT-001 |
| 2 | `GET` | `/api/v1/purchase-requisitions?cursor=&pageSize=&status=&number=&costCenterId=&projectId=&priority=&dateFrom=&dateTo=` | UC-008 | PR-PERM-009 | Listagem com keyset; sempre filtrada por escopo |
| 3 | `GET` | `/api/v1/purchase-requisitions/{id}` | UC-008 | PR-PERM-009 | Detalhe completo (cabeçalho + itens); `404` fora do escopo |
| 4 | `PATCH` | `/api/v1/purchase-requisitions/{id}` | UC-001(A2) | PR-PERM-002 | Edição em `DRAFT`/`RETURNED` + titularidade (ABAC-05); `If-Match` obrigatório |
| 5 | `DELETE` | `/api/v1/purchase-requisitions/{id}` | UC-001 | PR-PERM-003 | Exclui rascunho (somente `DRAFT` + titular); `204` |
| 6 | `POST` | `/api/v1/purchase-requisitions/{id}/submit` | UC-003 | PR-PERM-004 | Submete; `202` (validação assíncrona) ou `200`; publica EVT-002 |
| 7 | `POST` | `/api/v1/purchase-requisitions/{id}/cancel` | UC-007 | PR-PERM-008 | Cancela; corpo `{ reason }` obrigatório; política `pr.cancel.policy` |

### 4.2 Itens

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 8 | `POST` | `/api/v1/purchase-requisitions/{id}/items` | UC-002 | PR-PERM-002 | Adiciona item; `201` + `itemId`/`sequence`; publica EVT-003 |
| 9 | `PATCH` | `/api/v1/purchase-requisitions/{id}/items/{itemId}` | UC-002 | PR-PERM-002 | Edita item em estado editável; `If-Match` |
| 10 | `DELETE` | `/api/v1/purchase-requisitions/{id}/items/{itemId}` | UC-002 | PR-PERM-002 | Remove item; publica EVT-004; recalcula total |

### 4.3 Aprovação

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 11 | `GET` | `/api/v1/approvals/pending?cursor=` | UC-004 | PR-PERM-005/006/007 | Fila pendente do usuário (direta/delegada/escalonada), keyset |
| 12 | `POST` | `/api/v1/purchase-requisitions/{id}/approve` | UC-004 | PR-PERM-005 | Aprova o nível atual; corpo `{ comments? }`; ABAC-01 (SoD) + ABAC-02 (alçada) + nível atribuído |
| 13 | `POST` | `/api/v1/purchase-requisitions/{id}/reject` | UC-005 | PR-PERM-006 | Rejeita; corpo `{ reason, comments? }` com `reason` obrigatório |
| 14 | `POST` | `/api/v1/purchase-requisitions/{id}/return` | UC-006 | PR-PERM-007 | Retorna para ajuste; corpo `{ reason }` obrigatório |

### 4.4 Colaboração e evidência

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 15 | `GET` | `/api/v1/purchase-requisitions/{id}/timeline?cursor=` | UC-009 | PR-PERM-014 | Atalho para FD-001-07 (`entityType=purchase-requisition`) |
| 16 | `GET` | `/api/v1/purchase-requisitions/{id}/history?cursor=` | UC-009 | PR-PERM-013 | Histórico de auditoria funcional do recurso |
| 17 | `POST` | `/api/v1/purchase-requisitions/{id}/attachments` | UC-010 | PR-PERM-012 | Upload multipart; `201` + `attachmentId` (FD-001-03) |
| 18 | `GET` | `/api/v1/purchase-requisitions/{id}/attachments/{attachmentId}/download` | UC-010 | PR-PERM-009 | Emite URL assinada ≤ 5 min (MinIO, SEC-001) |
| 19 | `DELETE` | `/api/v1/purchase-requisitions/{id}/attachments/{attachmentId}` | UC-010 | PR-PERM-012 | Remove anexo (estados editáveis; auditado) |
| 20 | `POST` | `/api/v1/purchase-requisitions/{id}/comments` | UC-011 | PR-PERM-011 | Comenta; corpo `{ message, internal }`; atalho FD-001-08 |

### 4.5 Mapeamento de eventos publicados

| Endpoint | Eventos (PR-001-05) | Efeitos colaterais |
|---|---|---|
| `POST /purchase-requisitions` | EVT-001 (Created) | Timeline `created`, auditoria |
| `POST …/submit` | EVT-002 (Submitted) | Instancia workflow (FD-001-04), notifica aprovadores (FD-001-05) |
| `POST/DELETE …/items` | EVT-003/EVT-004 | Recalcula total, timeline |
| `POST …/approve` | EVT-005 (LevelApproved) / EVT-006 (Approved) | Avança/conclui workflow, notifica solicitante |
| `POST …/reject` | EVT-007 (Rejected) | Conclui workflow, notifica solicitante |
| `POST …/return` | EVT-008 (Returned) | Suspende nível, notifica solicitante, incrementa `cycle` na resubmissão |
| `POST …/cancel` | EVT-009 (Cancelled) | Compensação do workflow (WF-COMP), notifica |
| `POST …/attachments` | EVT-010 (AttachmentAdded) | Timeline, auditoria |
| `POST …/comments` | CO-EVT-001 (+ CO-EVT-003 em menções) | Timeline, notificação de menções |

---

## 5. Erros do Módulo

Catálogo consolidado (detalhes e mensagens em PR-001-02/PR-001-07; `Accept-Language` se aplica):

| Código | HTTP | Situação |
|---|---|---|
| `PR-ERR-001` | 403 | Sem permissão para a ação (RBAC/ABAC/delegação negou) |
| `PR-ERR-010` | 400 | Quantidade deve ser maior que zero |
| `PR-ERR-021` | 400 | Centro de custo inválido/inativo para a unidade |
| `PR-ERR-022` | 400 | Código de Master Data inativo/vencido na data (FD-001-09, MD-BR-005) |
| `PR-ERR-030` | 400 | Campos obrigatórios de submissão ausentes (PR-BR-020) |
| `PR-ERR-040` | 409 | Estado não permite a operação (transição inválida — PR-001-03) |
| `PR-ERR-041` | 422 | SoD: criador não pode aprovar a própria requisição (ABAC-01) |
| `PR-ERR-042` | 422 | Alçada insuficiente para o valor total (ABAC-02) |
| `PR-ERR-043` | 422 | Nível de aprovação não atribuído ao usuário (nem delegação/escalonamento) |
| `PR-ERR-050` | 422 | Data de necessidade no passado na submissão (PR-BR-050) |
| `PR-ERR-060` | 409 | Conflito de versão (`If-Match` divergente) |
| `PR-ERR-070` | 400 | Anexo inválido (tipo/tamanho — política FD-001-03) |
| `PR-ERR-080` | 429 | (reservado) |
| `PR-ERR-090` | 429 | Rate limit excedido |

---

## 6. Segurança (resumo operacional)

1. **Anti-enumeração:** ausência de escopo ⇒ `404` (nunca `403`) — POL do Foundation.
2. **SoD e alçada** avaliados server-side em toda decisão (ABAC-01/02); tentativa negada é auditada (AUD-BR-010).
3. **Mass assignment:** payloads são bindados por allowlist de campos por endpoint; campos calculados (`totalEstimatedValue`, `number`, `status`) são somente leitura.
4. **Upload:** whitelist de content-type, limite de tamanho, varredura antivírus (FD-001-03, SEC-003); download somente por URL assinada ≤ 5 min.
5. **XSS:** campos de texto livre sanitizados na entrada e escapados na saída (SEC-003; comentários seguem CO-BR-003).
6. **CSRF:** API stateless com Bearer — sem cookies de sessão; CSRF não se aplica (SEC-003).
7. **Auditoria:** toda mutação e decisão trilhada (FD-001-06); `correlationId` ponta a ponta.

---

## 7. Versionamento e Compatibilidade

- **Aditivo permitido em v1:** novos campos opcionais em respostas, novos filtros, novos endpoints.
- **Proibido em v1:** remover/renomear campos, mudar tipos, alterar semântica de códigos de erro, mudar ordenação padrão.
- **Depreciação:** campo/endpoint marcado `deprecated: true` na OpenAPI por no mínimo 2 releases antes da remoção (que ocorre apenas em `v2`).
- **Contrato de eventos** segue PR-001-05 §14.1 (versionamento `v1` por evento, consumo tolerante).

---

## 8. Requisitos Não Funcionais da API

| Categoria | Requisito |
|---|---|
| **Desempenho** | P95 ≤ 300 ms em leituras de detalhe; P95 ≤ 500 ms em listagens com filtros compostos (índices PR-001-11). |
| **Disponibilidade** | Ações de aprovação fail-closed em indisponibilidade de auditoria (AUD-BR-014). |
| **Observabilidade** | Métricas por endpoint (latência, taxa de erro, 403/404 ratio), tracing por `correlationId`. |
| **Rate limit** | Conforme §2.8; resposta com `Retry-After`. |
| **Documentação** | OpenAPI 3.1 publicada no portal de desenvolvedores a cada release; exemplos pt-BR. |

---

## 9. Critérios de Conclusão

- [ ] Todos os 20 endpoints implementados conforme este catálogo, com testes de contrato (OpenAPI diff limpo).
- [ ] Fluxo de autorização escopo(404)→RBAC→ABAC→delegação verificado em testes por endpoint.
- [ ] Keyset pagination com cursor assinado em todas as coleções.
- [ ] Idempotency-Key funcional nos POSTs de escrita.
- [ ] Catálogo de erros PR-ERR implementado e localizado (pt-BR/en-US).
- [ ] Eventos publicados conforme §4.5 com outbox e idempotência de consumidores.
- [ ] Testes relacionados TC-UC-xxx-y (PR-001-07) verdes na suíte de API.

---

## 10. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Este catálogo (20 endpoints), erros, segurança, keyset, idempotência, OpenAPI 3.1. |
| **v1.1** | UC-012 Copiar Requisição, UC-013 Template, UC-014 Exportar PDF (endpoints já reservados: `POST …/copy`, `POST /api/v1/purchase-requisitions/from-template`, `GET …/export.pdf`) + PR-PERM-010. |
| **v2.0** | Webhooks assinados de ciclo de vida (FD-001-05 v3), bulk actions (`POST …/bulk-approve` com relatório por item), filtro full-text. |

---

## 11. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Versão inicial aprovada: convenções (envelope, keyset, idempotência, autorização, headers, HTTP, rate limit), modelos de recursos, catálogo de 20 endpoints mapeados a UC/permissão/evento, catálogo de erros PR-ERR, segurança operacional, versionamento, NFRs, critérios de conclusão e roadmap. | Arquitetura Trino |
