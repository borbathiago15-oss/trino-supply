# FD-001-07 — Timeline Service

| Campo | Valor |
|---|---|
| **Documento** | FD-001-07 |
| **Módulo** | Foundation — Timeline Service (FD-BC-007) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-07-30 |
| **Dependências** | FD-001 (Foundation Overview), FD-001-01 (IAM), FD-001-02 (Organization), FD-001-06 (Audit Service), ADR-009 (Banco como Projeção do Domínio), ADR-010 (Eventos de Negócio), ADR-011 (Padrão de Documentação Foundation) |
| **Referências de negócio** | PR-001-05 §14.1 (Mensageria Transversal), PR-001-13 API (`/timeline` nas APIs de negócio), FD-001-04 Workflow Engine, FD-001-05 Notification Center |

---

## 1. Objetivo

O Timeline Service é o domínio do Foundation responsável por montar **visões cronológicas funcionais** da vida de uma entidade de negócio — "o que aconteceu com esta requisição, este pedido, este documento" — apresentadas ao usuário final dentro dos próprios módulos.

Ele responde à necessidade funcional de acompanhamento:

1. **Linha do tempo por entidade** — sequência legível de marcos: criação, edições relevantes, submissões, aprovações, rejeições, comentários, anexos, notificações-chave;
2. **Linguagem de negócio** — textos amigáveis ("Ana aprovou o nível 2"), não jargão técnico nem payload de evento;
3. **Respeito a permissões** — o usuário só vê entradas compatíveis com o que ele pode ver da entidade;
4. **Desempenho de leitura** — projeção pronta para consulta, sem varrer eventos a cada requisição.

### 1.1 Timeline ≠ Auditoria

| Aspecto | Timeline Service (FD-001-07) | Audit Service (FD-001-06) |
|---|---|---|
| Público | Usuário final do módulo | Administradores, compliance, auditoria externa |
| Conteúdo | Marcos de negócio, linguagem amigável | Evidência completa (ator, IP, changeSet, hashes) |
| Filtragem | Por permissão funcional do usuário | Por escopo administrativo dedicado |
| Mutabilidade | **Projeção reconstruível** (pode ser dropada e refeita) | **Append-only imutável** |
| Sensíveis | Nunca exibe | Registra com mascaramento |

Conforme **AUD-BR-013**: módulos de negócio não leem a trilha de auditoria; leituras funcionais consomem o Timeline Service. O Audit Service permanece a fonte de evidência — a timeline é uma **projeção** derivada de eventos.

### 1.2 O que o Timeline Service NÃO é

- **Não é fonte de verdade.** É read model: pode ser reconstruído a qualquer momento a partir dos eventos.
- **Não é feed social.** Não há "linha do tempo do usuário" agregando entidades alheias (roadmap v3, com escopo próprio).
- **Não armazena comentários ou anexos.** Esses pertencem a Collaboration (FD-001-08) e Document Management (FD-001-03); a timeline apenas referencia seus marcos.

---

## 2. Conceitos do Domínio

| Conceito | Descrição |
|---|---|
| **TimelineEntry** | Item da linha do tempo: marco de negócio projetado a partir de um evento, já em linguagem amigável. |
| **EntityTimeline** | Sequência ordenada de TimelineEntry de uma entidade (`entityType` + `entityId` + `organizationId`). |
| **Milestone** | Tipo do marco (ex.: `created`, `submitted`, `approval.granted`, `approval.rejected`, `comment.added`, `attachment.added`, `field.changed`, `notification.action-required`). Catálogo controlado. |
| **PresentationHint** | Dica de renderização para o frontend: ícone, cor/severidade (`info/success/warning/danger`), agrupamento visual. |
| **SummaryTemplate** | Modelo de texto amigável com variáveis (ex.: *"{actor} aprovou o nível {level}"*), versionado e localizado (pt-BR/en-US, fallback pt-BR). |
| **VisibilityRule** | Regra que define quem pode ver cada entrada: `PUBLIC_IN_ENTITY` (todos com acesso à entidade), `ACTOR_ONLY`, `ROLE_SCOPED` (ex.: aprovadores), `ADMIN_ONLY`. |
| **Projection** | Read model persistido (PostgreSQL) alimentado por consumo de eventos; reconstruível via **rebuild**. |
| **Rebuild** | Operação administrativa que recria a projeção de um escopo (entidade, organização ou global) a partir do replay dos eventos. |

---

## 3. Pipeline de Projeção

```
Evento de negócio (WF-EVT / NC-EVT / PR-EVT / ...)
        │
        ▼
Consumidor idempotente (sourceEventId)         ← ADR-010, at-least-once
        │
        ▼
MilestoneMapper (mapa configurável: evento → milestone + SummaryTemplate + VisibilityRule)
        │
        ▼
TimelineEntry persistida (projeção)
        │
        ▼
Consulta funcional filtrada por permissão do usuário
```

1. **Mapa por configuração:** novos eventos entram na timeline por configuração versionada (evento → milestone), nunca por código novo (configuração acima de customização).
2. **Evento não mapeado:** ignorado para timeline (permanece na auditoria) e contabilizado em métrica para evolução do mapa.
3. **Late enrichment:** dados de exibição (nome do ator, rótulo da entidade) são resolvidos no momento da projeção e **desnormalizados** na entrada — a timeline não faz join em tempo de leitura.
4. **Ordenação:** `occurredAt` + sequência por entidade (campo `seq`), garantindo ordem estável mesmo com eventos no mesmo milissegundo.

---

## 4. Modelo de Dados da TimelineEntry

| Campo | Descrição |
|---|---|
| `entryId` | UUIDv7. |
| `entity` | `{ entityType, entityId, organizationId }`. |
| `seq` | Sequência monotônica por entidade (ordenação estável). |
| `occurredAt` | Timestamp UTC do evento de origem. |
| `milestone` | Código do marco (catálogo). |
| `summary` | Texto amigável já renderizado (idioma padrão da organização) + `summaryTemplateCode`/`version` para re-renderização. |
| `actor` | `{ actorId, actorLabel }` desnormalizado (quando aplicável). |
| `details` | Dados estruturados mínimos para o frontend (ex.: `{ level: 2, decision: "APPROVED" }`), **sem campos sensíveis**. |
| `presentation` | `{ icon, severity, groupKey? }`. |
| `visibility` | Regra aplicada (`PUBLIC_IN_ENTITY` / `ACTOR_ONLY` / `ROLE_SCOPED` / `ADMIN_ONLY` + `roleScope?`). |
| `sourceEventId` | Id do evento de origem (idempotência + rastreio). |
| `correlationId` | Correlação com o fluxo de origem. |

- Particionamento lógico por `organizationId`; índice principal `(organization_id, entity_type, entity_id, seq)`.
- FKs lógicas apenas (ADR-009). Nenhum dado pessoal além do necessário à exibição (LGPD — minimização).

---

## 5. Visibilidade e Permissões

| Regra | Quem vê | Uso típico |
|---|---|---|
| `PUBLIC_IN_ENTITY` | Qualquer usuário com acesso de leitura à entidade | criação, submissão, aprovação, conclusão |
| `ACTOR_ONLY` | Apenas o ator da ação | rascunho editado, ação corrigida |
| `ROLE_SCOPED` | Usuários com papel específico no contexto da entidade | pendências de aprovação, escalonamentos internos |
| `ADMIN_ONLY` | Administradores do módulo/organização | falhas técnicas, reprocessamentos, supressões |

- A filtragem é aplicada **na consulta** (nunca no frontend), combinada com a permissão de leitura da própria entidade: sem acesso à entidade → 404 (padrão escopo→404 do Foundation).
- Entradas `ADMIN_ONLY` também ficam fora do alcance de exportações funcionais.

---

## 6. Regras de Negócio

| Código | Regra |
|---|---|
| **TL-BR-001** | A timeline é projeção: toda TimelineEntry nasce de um evento consumido; não há criação manual por API pública. |
| **TL-BR-002** | Idempotência por `sourceEventId`: replay de evento nunca duplica entrada. |
| **TL-BR-003** | Toda entrada é rastreável ao evento de origem (`sourceEventId` + `correlationId`). |
| **TL-BR-004** | A projeção é reconstruível: rebuild de qualquer escopo reproduz a mesma sequência determinística a partir dos eventos. |
| **TL-BR-005** | Nenhum campo sensível (valores mascarados, tokens, dados pessoais além do exibível) entra na projeção; o mapper descarta na entrada. |
| **TL-BR-006** | A filtragem de visibilidade é server-side, combinada com a permissão de leitura da entidade; sem acesso à entidade, resposta 404. |
| **TL-BR-007** | Isolamento multiempresa: projeção e consulta particionadas por `organizationId`. |
| **TL-BR-008** | Ordenação estável por `(occurredAt, seq)`; a API expõe keyset pagination por `seq` DESC. |
| **TL-BR-009** | Textos amigáveis seguem SummaryTemplate versionado e localizado; re-renderização em rebuild usa o template vigente, preservando o original em `summary` histórico. |
| **TL-BR-010** | Marcos administrativos/sensíveis (falha técnica, reprocessamento, supressão de notificação) só entram com `ADMIN_ONLY`. |
| **TL-BR-011** | O catálogo de milestones e o mapa evento→milestone são versionados por configuração; string livre é proibida. |
| **TL-BR-012** | A timeline não substitui a auditoria: qualquer divergência entre projeção e trilha é resolvida a favor da trilha (FD-001-06) e tratada como incidente de projeção. |

---

## 7. Eventos

### 7.1 Consumidos

| Origem | Uso |
|---|---|
| Eventos de negócio dos módulos (ADR-010) | Fonte primária dos marcos via MilestoneMapper. |
| WF-EVT-001..014 (FD-001-04) | Marcos de workflow: instanciado, decidido, escalonado, delegado, concluído, rejeitado, suspenso, cancelado. |
| NC-EVT-004/005/007 (FD-001-05) | Marcos selecionados: entrega de ação necessária, leitura, supressão (`ADMIN_ONLY`). |
| AUD-EVT-002/005 (FD-001-06) | Incidentes de integridade e retenção (`ADMIN_ONLY`). |

### 7.2 Publicados pelo Timeline Service

| Código | Evento | Payload (resumo) | Consumidores |
|---|---|---|---|
| **TL-EVT-001** | TimelineEntryProjected | entryId, entityRef, milestone, visibility, correlationId | UI realtime (WebSocket/SignalR), analytics |
| **TL-EVT-002** | TimelineRebuildStarted | scope, rebuildId, requestedBy | Observabilidade |
| **TL-EVT-003** | TimelineRebuildCompleted | scope, rebuildId, entriesProjected, durationMs | Observabilidade |
| **TL-EVT-004** | TimelineProjectionDiverged | scope, expected, actual, detectedAt | SRE (incidente de projeção — TL-BR-012) |

Envelope e garantias conforme PR-001-05 §14.1 / ADR-010: outbox, at-least-once, versionamento `v1`, `correlationId`.

---

## 8. APIs

| Método | Endpoint | Descrição | Permissão |
|---|---|---|---|
| `GET` | `/api/v1/timeline/{entityType}/{entityId}?cursor=&limit=&milestone=` | Linha do tempo da entidade (keyset por `seq` DESC), filtrada por visibilidade | leitura da entidade |
| `GET` | `/api/v1/timeline/{entityType}/{entityId}/summary` | Contadores por milestone/severidade (para badges) | leitura da entidade |
| `GET` | `/api/v1/admin/timeline/catalog` | Catálogo de milestones e mapa evento→milestone | `timeline.admin` |
| `POST` | `/api/v1/admin/timeline/rebuild` | Rebuild por escopo (`entity` \| `organization` \| `global`) | `timeline.admin` |
| `GET` | `/api/v1/admin/timeline/rebuild/{rebuildId}` | Status do rebuild | `timeline.admin` |

- Endpoints de negócio (ex.: `GET /api/v1/purchase-requisitions/{id}/timeline`, previsto em PR-001-13) são **atalhos** que delegam ao Timeline Service com `entityType=purchase-requisition` — mesma projeção, mesma filtragem.
- Envelope e erros padrão do Foundation; cursor assinado (padrão keyset do Foundation).

---

## 9. Integrações com o Foundation

| Domínio | Integração |
|---|---|
| **FD-001-01 IAM** | Resolução de papéis para `ROLE_SCOPED`; autenticação das APIs. |
| **FD-001-02 Organization** | Idioma padrão dos summaries, isolamento multiempresa. |
| **FD-001-03 Document Management** | Marcos de anexos/documentos (referência, sem armazenar conteúdo). |
| **FD-001-04 Workflow Engine** | Principal fonte de marcos de processo. |
| **FD-001-05 Notification Center** | Marcos de entrega/leitura de ações necessárias. |
| **FD-001-06 Audit Service** | Fonte de evidência subjacente (TL-BR-012); a timeline nunca a expõe. |
| **FD-001-08 Collaboration** *(planejado)* | Marcos de comentários e menções. |

---

## 10. Requisitos Não Funcionais

| Categoria | Requisito |
|---|---|
| **Desempenho de leitura** | Consulta por entidade sem join (projeção desnormalizada); P95 ≤ 150 ms para 50 entradas por página. |
| **Atualidade** | Lag de projeção P95 ≤ 5 s do evento à entrada consultável; realtime opcional via TL-EVT-001. |
| **Confiabilidade** | At-least-once + idempotência (TL-BR-002); lag de fila monitorado com alerta. |
| **Reconstruível** | Rebuild global completo sem indisponibilidade de leitura (projeção dupla: escreve em sombra e troca atomicamente por escopo). |
| **Segurança** | Filtragem server-side, 404 fora de escopo, sem sensíveis na projeção (TL-BR-005/006). |
| **Privacidade (LGPD)** | Minimização: apenas rótulos necessários à exibição; pseudonimização do Audit Service propaga-se por rebuild. |
| **Observabilidade** | Métricas de lag, taxa de eventos não mapeados, divergências de projeção (TL-EVT-004). |

---

## 11. Restrições Arquiteturais

1. **Read model puro:** nenhuma regra de negócio no Timeline Service; ele projeta e filtra, nada mais.
2. **Proibição de leitura da trilha:** a projeção consome eventos de negócio (e o mapa), nunca consulta o Audit Service em tempo de leitura.
3. **Configuração acima de customização:** milestones, templates e mapa evento→milestone são configuração versionada.
4. **Sem escrita pública:** não existe endpoint de criação/edição/exclusão de entradas.
5. **Multiempresa por construção:** `organizationId` em toda chave, índice e filtro.

---

## 12. Critérios de Conclusão do Módulo (MVP)

- [ ] Projeção persistida com índice `(organization_id, entity_type, entity_id, seq)` e keyset pagination.
- [ ] MilestoneMapper configurável cobrindo os eventos WF-EVT e os marcos de criação/edição do primeiro módulo de negócio.
- [ ] SummaryTemplates pt-BR publicados para os milestones do MVP.
- [ ] Filtragem de visibilidade server-side (4 regras) integrada à permissão da entidade.
- [ ] API de timeline + summary operacionais; atalho `/timeline` documentado para módulos de negócio.
- [ ] Rebuild por escopo com troca atômica e eventos TL-EVT-002/003.
- [ ] Testes: idempotência de projeção, ordenação estável, visibilidade por regra, 404 fora de escopo, rebuild determinístico, isolamento multiempresa.

---

## 13. Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Descrição |
|---|---|---|
| FD-001-01 IAM | Obrigatória | Papéis (ROLE_SCOPED), autenticação. |
| FD-001-02 Organization | Obrigatória | Idioma padrão, isolamento. |
| FD-001-04 Workflow Engine | Consumo de eventos | Marcos de processo. |
| FD-001-05 Notification Center | Consumo de eventos | Marcos de entrega/leitura/supressão. |
| FD-001-06 Audit Service | Fonte de evidência | Substrato de divergência (TL-BR-012); não lido em runtime. |
| FD-001-08 Collaboration | Consumo de eventos | Marcos de comentários/menções (quando aprovado). |
| Módulos de negócio (PR-001, …) | Consumo de eventos + atalho de API | Marcos de domínio e endpoint `/timeline`. |

---

## 14. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Projeção por entidade, MilestoneMapper configurável, 4 regras de visibilidade, API + summary, rebuild por escopo, SummaryTemplates pt-BR. |
| **v2.0** | Realtime (TL-EVT-001 via SignalR), agrupamento visual por dia/semana (`groupKey`), en-US, filtros combinados (milestone + ator + período), projeção dupla para rebuild global sem janela. |
| **v3.0** | Feed agregado "minhas entidades" (escopo próprio de permissão), marcos preditivos (próximo SLA), exportação funcional da timeline (PDF). |

---

## 15. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Versão inicial aprovada: objetivo, distinção timeline≠auditoria, conceitos, pipeline de projeção, modelo da TimelineEntry, visibilidade, regras TL-BR-001..012, eventos TL-EVT-001..004, APIs, integrações, NFRs, restrições, critérios de conclusão, matriz de dependência e roadmap. | Arquitetura Trino |
