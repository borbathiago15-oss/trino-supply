# FD-001-08 — Collaboration

| Campo | Valor |
|---|---|
| **Documento** | FD-001-08 |
| **Módulo** | Foundation — Collaboration (FD-BC-008) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-07-30 |
| **Dependências** | FD-001 (Foundation Overview), FD-001-01 (IAM), FD-001-02 (Organization), FD-001-03 (Document Management), FD-001-05 (Notification Center), FD-001-06 (Audit Service), FD-001-07 (Timeline Service), ADR-009/010/011, SEC-001/003 |
| **Referências de negócio** | PR-001-05 §14.1 (Mensageria Transversal), PR-001-13 API (`/comments` nas APIs de negócio), FD-001-04 Workflow Engine |

---

## 1. Objetivo

O Collaboration é o domínio do Foundation responsável pela **conversação contextual** sobre entidades de negócio: comentários, respostas e menções que acontecem **dentro** de uma requisição, pedido ou documento — no lugar onde a decisão acontece, não em e-mails soltos.

Ele entrega:

1. **Comentários com threading** — discussões organizadas em tópicos e respostas por entidade;
2. **Menções (@usuário)** — convocam pessoas para a conversa e disparam notificação (via Notification Center);
3. **Referências a anexos** — comentários podem referenciar documentos (Document Management), sem duplicar conteúdo;
4. **Rastro funcional** — marcos de colaboração alimentam a Timeline (FD-001-07) e a auditoria (FD-001-06);
5. **Governança** — edição com histórico, exclusão lógica, moderação administrativa e anti-abuso.

### 1.1 O que o Collaboration NÃO é

- **Não é chat.** Não há conversas 1:1 nem canais livres fora de uma entidade de negócio.
- **Não é repositório de arquivos.** Anexos pertencem ao Document Management (FD-001-03); aqui há apenas referência.
- **Não é sistema de aprovação.** Pareceres formais de aprovação pertencem ao Workflow Engine (FD-001-04); comentários são informais e não substituem decisões.
- **Não é fórum público.** Toda conversa está ancorada a uma entidade e herda as permissões dela.

---

## 2. Conceitos do Domínio

| Conceito | Descrição |
|---|---|
| **Comment** | Unidade básica: texto (markdown restrito) de um autor sobre uma entidade, com threading de 1 nível (comentário → respostas). |
| **Thread** | Comentário raiz + suas respostas. Responder a resposta não cria novo nível (aninha na mesma thread). |
| **Mention** | Referência `@usuário` no corpo do comentário; gera notificação ACTION/INFORMATION via Notification Center. |
| **EntityAnchor** | Âncora da conversa: `{ entityType, entityId, organizationId }`; a conversa herda as permissões da entidade. |
| **AttachmentRef** | Referência a documento do FD-001-03 (`documentId` + rótulo); renderização como card de link, nunca cópia. |
| **CommentHistory** | Versões anteriores de um comentário editado (imutáveis, auditáveis). |
| **Moderation** | Ação administrativa: ocultar/restaurar comentário com motivo obrigatório. |
| **RateLimitPolicy** | Política anti-abuso: limite de comentários por usuário/janela, tamanho máximo, limite de menções por comentário. |

---

## 3. Modelo de Dados do Comment

| Campo | Descrição |
|---|---|
| `commentId` | UUIDv7. |
| `entity` | `{ entityType, entityId, organizationId }` — âncora obrigatória. |
| `threadRootId` | (Opcional) id do comentário raiz; nulo = comentário raiz. |
| `authorId` / `authorLabel` | Autor (desnormalizado para leitura). |
| `body` | Markdown restrito (negrito, itálico, listas, links, menções); HTML sanitizado (SEC-003 — XSS). |
| `mentions` | `[userId]` extraídos do corpo (máx. configurável). |
| `attachmentRefs` | `[{ documentId, label }]` (máx. configurável). |
| `createdAt` / `editedAt` | Timestamps UTC; `editedAt` nulo = nunca editado. |
| `status` | `ACTIVE` \| `DELETED_BY_AUTHOR` \| `HIDDEN_BY_MODERATOR` (exclusão/ocultação são lógicas). |
| `moderationReason` | Motivo obrigatório quando `HIDDEN_BY_MODERATOR`. |
| `correlationId` | Correlação do fluxo de origem. |

- Índices: `(organization_id, entity_type, entity_id, created_at)` e `(organization_id, thread_root_id, created_at)`; keyset pagination.
- FKs lógicas (ADR-009). Dados pessoais limitados ao necessário (LGPD — minimização).

---

## 4. Permissões e Herança de Contexto

| Ação | Quem pode |
|---|---|
| Ler comentários | Qualquer usuário com **leitura na entidade** (sem acesso à entidade → 404, padrão Foundation). |
| Comentar | Usuário com leitura na entidade + permissão `collaboration.comment` (padrão: todos os papéis operacionais). |
| Editar | **Somente o autor**, dentro da janela configurável (padrão: 24h) e nunca após moderação. |
| Excluir (lógico) | O autor (próprio comentário, sem respostas de terceiros) ou moderador. |
| Moderar (ocultar/restaurar) | Permissão dedicada `collaboration.moderate`, com motivo obrigatório. |
| Mencionar | Quem comenta; menção a usuário **sem acesso à entidade** é bloqueada (CO-BR-006). |

- Menções concedem **notificação**, nunca acesso: o mencionado sem permissão na entidade recebe aviso de que foi citado, mas não abre a conversa (evita IDOR — SEC-003).

---

## 5. Notificações e Timeline

### 5.1 Menções (via Notification Center)

- Evento CO-EVT-003 (UserMentioned) → Notification Center despacha conforme preferências do mencionado (FD-001-05), prioridade `NORMAL` (ou `HIGH` se configurado pelo módulo).
- Payload mínimo: quem mencionou, entidade, trecho do contexto (sem corpo integral sensível), link autenticado.

### 5.2 Marcos na Timeline (FD-001-07)

- `comment.added`, `comment.edited` (opcional por módulo), `comment.hidden` (`ADMIN_ONLY`) entram via MilestoneMapper configurável.
- Resumo amigável: *"Carlos comentou: 'Verifiquei o fornecedor…'"* (trecho truncado, nunca corpo integral).

---

## 6. Regras de Negócio

| Código | Regra |
|---|---|
| **CO-BR-001** | Todo comentário está ancorado a uma entidade e herda suas permissões; sem acesso à entidade, a conversa não existe para o usuário (404). |
| **CO-BR-002** | Threading é de 1 nível: respostas pertencem ao comentário raiz; não há sub-thread. |
| **CO-BR-003** | Corpo em markdown restrito com sanitização server-side; HTML injetado é neutralizado (XSS — SEC-003). |
| **CO-BR-004** | Edição somente pelo autor, na janela configurável (padrão 24h), com histórico imutável de versões. |
| **CO-BR-005** | Exclusão pelo autor é lógica e só permitida sem respostas de terceiros; com respostas, só moderação. |
| **CO-BR-006** | Menção a usuário sem acesso à entidade é bloqueada na gravação; menção nunca concede acesso. |
| **CO-BR-007** | Limite de menções por comentário (padrão 10) e rate limit por usuário (padrão 60 comentários/hora); excedentes são rejeitados com código de erro. |
| **CO-BR-008** | Moderação exige permissão dedicada e motivo; ocultação e restauração são auditadas (FD-001-06). |
| **CO-BR-009** | Toda criação, edição, exclusão e moderação publica evento e gera trilha de auditoria (FD-001-06). |
| **CO-BR-010** | Isolamento multiempresa: comentários, menções e histórico particionados por `organizationId`. |
| **CO-BR-011** | Comentários não têm efeito de negócio: não alteram estado de entidade, não aprovam, não bloqueiam workflow. |
| **CO-BR-012** | AttachmentRef valida existência e permissão do documento no FD-001-03; referência a documento inacessível ao leitor não é renderizada para ele. |

---

## 7. Eventos

### 7.1 Publicados pelo Collaboration

| Código | Evento | Payload (resumo) | Consumidores |
|---|---|---|---|
| **CO-EVT-001** | CommentAdded | commentId, entityRef, authorId, threadRootId, hasMentions, correlationId | Timeline, Audit |
| **CO-EVT-002** | CommentEdited | commentId, version, editedAt | Timeline, Audit |
| **CO-EVT-003** | UserMentioned | commentId, entityRef, mentionedUserId, mentionedBy, contextExcerpt | Notification Center |
| **CO-EVT-004** | CommentDeleted | commentId, deletedBy (AUTHOR/MODERATOR), reason? | Timeline, Audit |
| **CO-EVT-005** | CommentHidden | commentId, moderatorId, reason | Timeline (ADMIN_ONLY), Audit |
| **CO-EVT-006** | CommentRestored | commentId, restoredBy, reason | Timeline, Audit |

Envelope e garantias conforme PR-001-05 §14.1 / ADR-010: outbox, at-least-once, idempotência por `eventId + consumer`, versionamento `v1`, `correlationId`.

### 7.2 Consumidos

| Origem | Uso |
|---|---|
| (MVP: nenhum) | O Collaboration não depende de eventos para operar; na v2, eventos de exclusão lógica de entidade suspendem novas postagens na âncora (CO-BR-001 reforçado). |

---

## 8. APIs

| Método | Endpoint | Descrição | Permissão |
|---|---|---|---|
| `GET` | `/api/v1/collaboration/{entityType}/{entityId}/comments?cursor=` | Threads da entidade (raiz + respostas agrupadas, keyset) | leitura da entidade |
| `POST` | `/api/v1/collaboration/{entityType}/{entityId}/comments` | Novo comentário (raiz) | `collaboration.comment` |
| `POST` | `/api/v1/collaboration/comments/{commentId}/replies` | Resposta na thread | `collaboration.comment` |
| `PATCH` | `/api/v1/collaboration/comments/{commentId}` | Edição pelo autor (janela) | autor |
| `DELETE` | `/api/v1/collaboration/comments/{commentId}` | Exclusão lógica pelo autor | autor (CO-BR-005) |
| `GET` | `/api/v1/collaboration/comments/{commentId}/history` | Versões anteriores | leitura da entidade |
| `POST` | `/api/v1/admin/collaboration/comments/{commentId}/hide` | Moderação: ocultar (motivo obrigatório) | `collaboration.moderate` |
| `POST` | `/api/v1/admin/collaboration/comments/{commentId}/restore` | Moderação: restaurar | `collaboration.moderate` |
| `GET` | `/api/v1/collaboration/mentionable-users?entityType=&entityId=&q=` | Autocomplete de menções (apenas usuários com acesso à entidade) | `collaboration.comment` |

- Endpoints de negócio (ex.: `POST /api/v1/purchase-requisitions/{id}/comments`, previsto em PR-001-13) são **atalhos** que delegam ao Collaboration com `entityType=purchase-requisition`.
- Erros padronizados: `CO-ERR-001` (sem acesso), `CO-ERR-002` (janela de edição expirada), `CO-ERR-003` (menção sem acesso), `CO-ERR-004` (rate limit), `CO-ERR-005` (exclusão bloqueada por respostas), `CO-ERR-006` (limite de menções).

---

## 9. Integrações com o Foundation

| Domínio | Integração |
|---|---|
| **FD-001-01 IAM** | Autores, autocomplete de menções, permissões `collaboration.*`. |
| **FD-001-02 Organization** | Isolamento multiempresa; configurações (janela de edição, limites). |
| **FD-001-03 Document Management** | AttachmentRefs validados; links assinados na renderização. |
| **FD-001-04 Workflow Engine** | Entidades de processo como âncoras; pareceres formais permanecem no workflow (CO-BR-011). |
| **FD-001-05 Notification Center** | Despacho de notificações de menção (CO-EVT-003). |
| **FD-001-06 Audit Service** | Trilha de criação/edição/exclusão/moderação (CO-BR-009). |
| **FD-001-07 Timeline Service** | Marcos `comment.*` via MilestoneMapper. |

---

## 10. Requisitos Não Funcionais

| Categoria | Requisito |
|---|---|
| **Desempenho** | Listagem por entidade com keyset; P95 ≤ 200 ms para 20 threads com respostas agrupadas. |
| **Segurança** | Sanitização XSS server-side (SEC-003); bloqueio de IDOR por herança de permissão da entidade; rate limit anti-abuso; menção sem acesso bloqueada. |
| **Confiabilidade** | Outbox nos eventos; gravação e publicação na mesma transação lógica. |
| **Privacidade (LGPD)** | Minimização; direito de eliminação atendido por exclusão lógica + pseudonimização posterior (FD-001-06 v2). |
| **Observabilidade** | Métricas de volume por entidade, rate-limit hits, menções bloqueadas, lag de eventos. |
| **Usabilidade** | Markdown restrito previsível; autocomplete de menções restrito a usuários com acesso. |

---

## 11. Restrições Arquiteturais

1. **Sem efeito colateral de negócio:** comentário nunca dispara transição de estado (CO-BR-011); integrações funcionais acontecem apenas via eventos lidos por Timeline/Notification/Audit.
2. **Permissão herdada, nunca reimplementada:** o Collaboration não replica regras de acesso dos módulos — consulta o contexto da entidade.
3. **Conteúdo único:** anexos vivem no FD-001-03; a conversa referencia, não copia.
4. **Configuração acima de customização:** janelas, limites e catálogo de marcos por configuração versionada.
5. **Multiempresa por construção:** `organizationId` em toda chave, índice e filtro.

---

## 12. Critérios de Conclusão do Módulo (MVP)

- [ ] CRUD de comentários com threading de 1 nível, keyset pagination e herança de permissão da entidade (404 fora de escopo).
- [ ] Menções com validação de acesso, limite por comentário e notificação via CO-EVT-003.
- [ ] Edição com janela + histórico imutável; exclusão lógica com regra de respostas.
- [ ] Moderação com motivo e auditoria.
- [ ] Sanitização XSS server-side com testes de payloads OWASP.
- [ ] Rate limit e limites de menções ativos.
- [ ] Marcos de timeline configurados; trilha de auditoria completa.
- [ ] Testes: herança de permissão, menção bloqueada, janela de edição, exclusão com respostas, isolamento multiempresa, idempotência de eventos.

---

## 13. Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Descrição |
|---|---|---|
| FD-001-01 IAM | Obrigatória | Autores, menções, permissões. |
| FD-001-02 Organization | Obrigatória | Isolamento, configurações. |
| FD-001-03 Document Management | Obrigatória | AttachmentRefs. |
| FD-001-05 Notification Center | Produção de eventos | Notificações de menção. |
| FD-001-06 Audit Service | Produção de eventos | Trilha de colaboração. |
| FD-001-07 Timeline Service | Produção de eventos | Marcos `comment.*`. |
| Módulos de negócio (PR-001, …) | Consumo (atalho de API) | `/comments` nas entidades de domínio. |

---

## 14. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Comentários + threading 1 nível, menções com notificação, edição com histórico, exclusão lógica, moderação, sanitização, rate limit, AttachmentRefs, eventos e auditoria. |
| **v2.0** | Reações (👍 etc.) com consolidação, "resolver thread", fixar comentário, realtime (SignalR), suspensão automática de postagem em entidade encerrada (consumo de eventos). |
| **v3.0** | Comentários em trecho de documento (âncora fina), sugestões de participantes, pesquisa full-text em comentários (com herança de permissão). |

---

## 15. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Versão inicial aprovada: objetivo, conceitos, modelo do Comment, permissões com herança da entidade, menções e notificações, regras CO-BR-001..012, eventos CO-EVT-001..006, APIs, integrações, NFRs, restrições, critérios de conclusão, matriz de dependência e roadmap. | Arquitetura Trino |
