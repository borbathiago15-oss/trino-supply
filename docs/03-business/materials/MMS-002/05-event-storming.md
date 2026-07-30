# MMS-002-05 — Event Storming

**Documento:** MMS-002-05 — Event Storming
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 v1.1.0 (visão do módulo), MMS-002-02 v1.1.0 (Business Rules), MMS-002-03 (State Machine), MMS-002-04 (Domain Model), ADR-010 (mensageria e eventos), FD-001-05 (Notifications), FD-001-06 (Audit), FD-001-07 (Timeline), FD-001-09 (Master Data)
**Referências:** PR-001-05 (padrão de formato Enterprise), MMS-003 (Stock/Warehouse), MMS-004 (Inventory Movements), MMS-005 (Dashboards), PR-001 (Purchase Requisition), GOV-001 (governança de documentos)

---

## 1. Objetivo

Este documento registra o **Event Storming** do módulo **Item Catalog (MMS-002)**: os comandos que provocam mudanças de estado, os eventos de domínio resultantes, as políticas que reagem a esses eventos e as reações esperadas dos serviços e módulos dependentes.

O Event Storming do Item Catalog responde a três perguntas:

1. **O que pode acontecer** no ciclo de vida de um item do catálogo (cadastro, parametrização, ativação, uso, inativação, descarte);
2. **Quem reage** quando algo acontece (Timeline, Auditoria, Notificações, Cache de catálogo, módulos consumidores);
3. **Como os eventos são entregues** de forma confiável, idempotente e ordenada (ADR-010).

Fonte de verdade complementar: os comandos e invariantes formais estão em **MMS-002-04 (Domain Model)**; as transições de estado em **MMS-002-03 (State Machine)**; as regras de negócio em **MMS-002-02 v1.1.0 (Business Rules)**. Em caso de conflito, prevalece a State Machine para transições e as Business Rules para validações.

---

## 2. Fluxo de Alto Nível

```
Cadastrar Item (rascunho)
      │
      ▼
Parametrizar (sinônimos, grade de tamanhos, CA, imagem, parâmetros de reposição)
      │
      ▼
Ativar Item ──────────────────────► Item disponível para os módulos consumidores
      │                                (MMS-003, MMS-004, MMS-005, PR-001)
      ▼
Usar / Editar (dados cadastrais, parâmetros, sinônimos)
      │
      ▼
Inativar Item
      │
      ├────────► Reativar Item ──► volta a Ativo
      │
      └────────► Descartar Item ──► Inativado (Descarte) — terminal
```

Estados (MMS-002-03): `ST-IC-001 Rascunho` → `ST-IC-002 Ativo` ⇄ `ST-IC-003 Inativo` → `ST-IC-004 Inativo (Descarte)`.

---

## 3. Atores

| Ator | Tipo | Papel no Event Storming |
|---|---|---|
| Gerente de Suprimentos | Humano | Mantenedor do catálogo: cadastra, edita, ativa, inativa, reativa, descarta, gerencia sinônimos e parâmetros |
| Administrador | Humano | Todas as ações do Gerente + administração de configurações (`materials.item.*`) |
| Sistema (Item Catalog) | Sistema | Executa validações automáticas, emite eventos, registra timeline e auditoria |
| Auditor | Humano (leitura) | Consulta trilha de auditoria e timeline; nunca emite comandos |
| MMS-003 Stock/Warehouse | Módulo consumidor | Valida item Ativo em solicitações de estoque (IC-BR-021); projeta catálogo para leitura |
| MMS-004 Inventory Movements | Módulo consumidor | Referencia itens em movimentações; projeta catálogo para leitura |
| MMS-005 Dashboards | Módulo consumidor | Consome dados de catálogo para indicadores (itens críticos, mais solicitados) |
| PR-001 Purchase Requisition | Módulo consumidor | Referencia itens do catálogo em requisições de compra |
| FD-001-09 Master Data | Foundation | Publica eventos de Master Data consumidos pelo Item Catalog (seção 13) |

---

## 4. Comandos

Comandos são intenções de mudança emitidas por um ator contra o agregado `Item`. Nem todo comando gera evento próprio: comandos de parametrização podem ser consolidados em `ItemUpdated` (ver seção 5, nota de consolidação).

| Código | Comando | Ator | Descrição | Evento(s) resultante(s) |
|---|---|---|---|---|
| CMD-IC-001 | CreateItem | Gerente / Administrador | Cadastra novo item em estado Rascunho (código, descrição, unidade, grupo, categoria) | EVT-IC-001 |
| CMD-IC-002 | UpdateItem | Gerente / Administrador | Altera dados cadastrais (descrição, unidade, grupo, categoria) | EVT-IC-002 |
| CMD-IC-003 | SetReplenishmentParameters | Gerente / Administrador | Define/altera ponto de reposição, estoque mínimo/máximo, lead time | EVT-IC-006 |
| CMD-IC-004 | AddSynonym | Gerente / Administrador | Inclui sinônimo de busca | EVT-IC-007 |
| CMD-IC-005 | RemoveSynonym | Gerente / Administrador | Remove sinônimo de busca | EVT-IC-008 |
| CMD-IC-006 | SetSizeGrid | Gerente / Administrador | Vincula/altera grade de tamanhos (FD-001-09 `SIZE_GRID`) | EVT-IC-002 |
| CMD-IC-007 | SetCA | Gerente / Administrador | Informa/altera Certificado de Aprovação (obrigatório para grupo EPI — IC-BR-080) | EVT-IC-002 |
| CMD-IC-008 | SetImage | Gerente / Administrador | Associa/substitui imagem do item (FD-001-03 Storage) | EVT-IC-002 |
| CMD-IC-009 | ActivateItem | Gerente / Administrador | Ativa item (Rascunho → Ativo), após guards IC-BR-001..005/080..083 | EVT-IC-003 |
| CMD-IC-010 | InactivateItem | Gerente / Administrador | Inativa item (Ativo → Inativo) | EVT-IC-004 |
| CMD-IC-011 | ReactivateItem | Gerente / Administrador | Reativa item (Inativo → Ativo) | EVT-IC-003 |
| CMD-IC-012 | DiscardItem | Gerente / Administrador | Descarta item (Inativo → Inativo (Descarte)), terminal | EVT-IC-005 |
| CMD-IC-013 | AddComment | Gerente / Administrador / Auditoria | Registra comentário operacional na timeline do item | (somente timeline; sem evento de domínio) |

**Nota de consolidação:** CMD-IC-006, CMD-IC-007 e CMD-IC-008 não geram eventos de domínio próprios. Suas alterações são publicadas como `ItemUpdated` (EVT-IC-002) com `changedFields` indicando `sizeGrid`, `ca` ou `image`. Isso mantém o catálogo de eventos enxuto (8 eventos, alinhado à visão MMS-002 v1.1.0) sem perder rastreabilidade.

---

## 5. Eventos de Domínio

Os 8 eventos abaixo correspondem exatamente aos eventos funcionais previstos na visão do módulo (MMS-002 v1.1.0, seção de eventos). Nenhum evento adicional foi criado.

| Código | Evento | Nome funcional (MMS-002 v1.1.0) | Disparado por | Momento |
|---|---|---|---|---|
| EVT-IC-001 | ItemCreated | Item cadastrado | CMD-IC-001 | Persistência do item em Rascunho |
| EVT-IC-002 | ItemUpdated | Item alterado | CMD-IC-002 / 006 / 007 / 008 | Persistência da alteração |
| EVT-IC-003 | ItemActivated | Item ativado | CMD-IC-009 / 011 | Transição para Ativo confirmada |
| EVT-IC-004 | ItemInactivated | Item inativado | CMD-IC-010 | Transição para Inativo confirmada |
| EVT-IC-005 | ItemDiscarded | Item inativado (descarte) | CMD-IC-012 | Transição para Inativo (Descarte) confirmada |
| EVT-IC-006 | ReplenishmentParametersChanged | Parâmetros de reposição alterados | CMD-IC-003 | Persistência dos novos parâmetros |
| EVT-IC-007 | SynonymAdded | Sinônimo incluído | CMD-IC-004 | Persistência do sinônimo |
| EVT-IC-008 | SynonymRemoved | Sinônimo removido | CMD-IC-005 | Remoção do sinônimo |

**Regras gerais dos eventos:**

- Todo evento é emitido **apenas após** a confirmação da persistência (padrão Outbox — seção 14).
- Eventos de transição de estado (EVT-IC-003, 004, 005) carregam `previousStatus` e `newStatus` para permitir reconstrução da linha do tempo sem consultar o agregado.
- `ItemUpdated` carrega `changedFields` (lista de campos alterados) e, quando aplicável, `previousValues`/`newValues` para auditoria (IC-BR-070).
- Nenhum evento é emitido para consultas (UC-IC-006) ou comentários (CMD-IC-013).

---

## 6. Políticas

Políticas são reações automáticas do sistema a eventos ("quando X acontecer, faça Y"). Formalizam as Policies do Domain Model (MMS-002-04 §14).

| Código | Política | Gatilho (evento) | Ação automática |
|---|---|---|---|
| POL-IC-01 | Registro de Timeline | Todos os EVT-IC-001..008 | Gravar entrada na timeline do item (FD-001-07) com ator, data/hora, ação e detalhes |
| POL-IC-02 | Registro de Auditoria | Todos os EVT-IC-001..008 | Gravar registro imutável de auditoria (FD-001-06) com before/after quando aplicável (IC-BR-070) |
| POL-IC-03 | Invalidação de Cache de Catálogo | EVT-IC-002, 003, 004, 005, 007, 008 | Invalidar/atualizar a projeção de leitura do catálogo usada na busca operacional (IC-BR-071) |
| POL-IC-04 | Notificação de Item Crítico Inativado | EVT-IC-004, 005 | Se o item estiver abaixo do ponto de reposição ou for referência de solicitações em aberto (MMS-003), notificar Gerente de Suprimentos e Almoxarifado (FD-001-05) |
| POL-IC-05 | Avaliação de Impacto de Master Data | Evento externo de Master Data (seção 13) | Quando categoria, unidade ou grade de tamanhos usada por itens for alterada/inativada no FD-001-09, avaliar impacto e notificar o mantenedor do catálogo |

---

## 7. Validações Automáticas

Validações executadas **antes** da emissão do evento; a falha bloqueia o comando e nenhum evento é publicado.

| Validação | Regra de origem | Aplica-se a | Efeito em caso de falha |
|---|---|---|---|
| Código do item único por empresa | IC-BR-001 | CMD-IC-001 | Erro `IC-ERR-001`; comando rejeitado |
| Descrição obrigatória e dentro do limite | IC-BR-002 | CMD-IC-001, 002 | Erro `IC-ERR-002` |
| Unidade de medida ativa no Master Data | IC-BR-003 | CMD-IC-001, 002 | Erro `IC-ERR-003` |
| Grupo/categoria válidos e ativos | IC-BR-004, 005 | CMD-IC-001, 002 | Erro `IC-ERR-004`/`IC-ERR-005` |
| CA obrigatório para grupo EPI | IC-BR-080 | CMD-IC-001, 007, 009, 011 | Erro `IC-ERR-080`; ativação bloqueada sem CA |
| CA válido (formato/vigência) | IC-BR-081 | CMD-IC-007, 009, 011 | Erro `IC-ERR-081` |
| Grade de tamanhos ativa no Master Data | IC-BR-082 | CMD-IC-006, 009, 011 | Erro `IC-ERR-082` |
| Imagem conforme (tipo, tamanho máximo) | IC-BR-083 | CMD-IC-008 | Erro `IC-ERR-083` |
| Código ERP único quando informado | IC-BR-011 | CMD-IC-001, 002 | Erro `IC-ERR-011` |
| Sinônimo não duplicado para o mesmo item | IC-BR-040 | CMD-IC-004 | Erro `IC-ERR-040` |
| Transição de estado permitida | MMS-002-03 | CMD-IC-009..012 | Erro `IC-ERR-090`; comando rejeitado |
| Item sem bloqueio de edição (uso em transação aberta) | IC-BR-021 | CMD-IC-002, 010, 012 | Erro `IC-ERR-021` ou advertência conforme configuração |

---

## 8. Reações Esperadas

O que cada serviço/módulo faz ao receber cada evento.

### 8.1 Timeline Service (FD-001-07)

| Evento | Reação |
|---|---|
| Todos (EVT-IC-001..008) | Criar entrada na timeline do item: ícone da ação, ator, timestamp, resumo legível ("Item ativado por Maria Silva", "Sinônimo 'capacete branco' incluído") |

### 8.2 Audit Service (FD-001-06)

| Evento | Reação |
|---|---|
| Todos (EVT-IC-001..008) | Persistir registro imutável: eventId, aggregateId, actorId, companyId, timestamp, payload completo, before/after para EVT-IC-002/003/004/005/006 |

### 8.3 Notification Service (FD-001-05)

| Evento | Reação |
|---|---|
| EVT-IC-004 / EVT-IC-005 | Aplicar POL-IC-04: se item crítico (abaixo do ponto de reposição ou com solicitações em aberto), notificar Gerente de Suprimentos e Almoxarifado |
| Demais eventos | Nenhuma notificação por padrão (catálogo é módulo de cadastro; ruído de notificação é indesejado) |

### 8.4 Projeção de Leitura do Catálogo (Cache — IC-BR-071)

| Evento | Reação |
|---|---|
| EVT-IC-001 | Incluir item na projeção como Rascunho (não visível na busca operacional) |
| EVT-IC-002 / 007 / 008 | Atualizar atributos do item na projeção (descrição, sinônimos, imagem) |
| EVT-IC-003 | Marcar item como Ativo → passa a aparecer na busca operacional dos consumidores |
| EVT-IC-004 / 005 | Marcar item como Inativo/Descarte → sai da busca operacional; permanece na consulta de catálogo com filtro de status |
| EVT-IC-006 | Atualizar parâmetros de reposição na projeção (usados por MMS-005 para indicadores de estoque crítico) |

### 8.5 Módulos Consumidores (MMS-003, MMS-004, MMS-005, PR-001)

| Evento | Reação |
|---|---|
| EVT-IC-003 | Atualizar projeção local de itens disponíveis (item passa a ser selecionável) |
| EVT-IC-004 / 005 | Atualizar projeção local (item deixa de ser selecionável em novas operações; referências históricas preservadas) |
| EVT-IC-002 | Atualizar dados de exibição (descrição, imagem) nas projeções locais |
| EVT-IC-001 / 006 / 007 / 008 | Consumo opcional por projeção local; não há ação obrigatória |

---

## 9. Eventos Externos (Futuros)

Eventos fora do escopo do MVP, previstos para evolução. **Não implementar.**

| Evento externo | Origem | Finalidade futura |
|---|---|---|
| ItemCatalogSnapshotExported | MMS-002 (futuro) | Exportação de snapshot do catálogo para BI/Analytics e data lake |
| ErpItemSynced | Integração ERP (futuro) | Sincronização bidirecional de itens com o ERP corporativo (código ERP — IC-BR-011) |
| CatalogBulkImportCompleted | MMS-002 (futuro) | Conclusão de importação em massa de itens |

---

## 10. Eventos Não Permitidos

Ações que **não geram evento** e não existem no domínio do Item Catalog:

| Ação proibida | Motivo |
|---|---|
| ItemDeleted (exclusão física) | Itens nunca são excluídos; o ciclo de vida termina em Inativo (Descarte) — preserva histórico e referências (MMS-002-03) |
| ItemStatusChanged genérico | Transições são explícitas (Ativação, Inativação, Reativação, Descarte); evento genérico quebraria semântica e auditoria |
| ItemPriceChanged / ItemCostChanged | Preço/custo não são atributos do Item Catalog no MVP; pertencem ao domínio de compras/estoque |
| ItemStockUpdated | Saldo de estoque é responsabilidade do MMS-004, nunca do catálogo |
| Qualquer evento emitido antes da persistência | Viola o padrão Outbox (ADR-010) e geraria eventos fantasmas em caso de rollback |

---

## 11. Matriz Evento × Reação

Síntese de quem reage a cada evento. ✅ = reação obrigatória; ◐ = condicional; — = sem reação.

| Evento | Timeline | Auditoria | Notificação | Cache/Projeção | Módulos consumidores |
|---|---|---|---|---|---|
| EVT-IC-001 ItemCreated | ✅ | ✅ | — | ✅ (Rascunho) | — |
| EVT-IC-002 ItemUpdated | ✅ | ✅ | — | ✅ | ◐ (exibição) |
| EVT-IC-003 ItemActivated | ✅ | ✅ | — | ✅ (Ativo) | ✅ (selecionável) |
| EVT-IC-004 ItemInactivated | ✅ | ✅ | ◐ (POL-IC-04) | ✅ (Inativo) | ✅ (não selecionável) |
| EVT-IC-005 ItemDiscarded | ✅ | ✅ | ◐ (POL-IC-04) | ✅ (Descarte) | ✅ (não selecionável) |
| EVT-IC-006 ReplenishmentParametersChanged | ✅ | ✅ | — | ✅ | ◐ (MMS-005) |
| EVT-IC-007 SynonymAdded | ✅ | ✅ | — | ✅ | — |
| EVT-IC-008 SynonymRemoved | ✅ | ✅ | — | ✅ | — |

---

## 12. Dependências

| Dependência | Tipo | Uso neste documento |
|---|---|---|
| MMS-002 v1.1.0 | Visão do módulo | Lista funcional dos 8 eventos |
| MMS-002-02 v1.1.0 | Business Rules | Validações (IC-BR-xxx) e erros (IC-ERR-xxx) |
| MMS-002-03 | State Machine | Transições permitidas e estados terminais |
| MMS-002-04 | Domain Model | Comandos, invariantes, policies do agregado `Item` |
| ADR-010 | Decisão arquitetural | Mensageria (RabbitMQ), padrão Outbox, envelope de evento |
| FD-001-05 | Foundation | Notification Service (consumidor) |
| FD-001-06 | Foundation | Audit Service (consumidor) |
| FD-001-07 | Foundation | Timeline Service (consumidor) |
| FD-001-09 | Foundation | Master Data (publica eventos consumidos — seção 13) |

---

## 13. Eventos Consumidos

O Item Catalog não é apenas publicador: consome eventos do Foundation para manter a integridade referencial do catálogo.

| Evento consumido | Publicador | Reação no Item Catalog | Política |
|---|---|---|---|
| MasterDataValueUpdated (categoria, unidade, grade de tamanhos) | FD-001-09 Master Data | Atualizar referência de leitura; avaliar impacto nos itens que usam o valor alterado | POL-IC-05 |
| MasterDataValueInactivated | FD-001-09 Master Data | Identificar itens que referenciam o valor inativado; bloquear novas ativações que o utilizem (IC-BR-003/004/005/082); notificar mantenedor | POL-IC-05 |

**Regra:** o Item Catalog nunca altera automaticamente os dados de itens existentes em reação a eventos de Master Data — apenas avalia impacto, bloqueia novas ativações inválidas e notifica o responsável. Alterações em itens exigem comando humano explícito.

---

## 14. Especificação Técnica dos Eventos

### 14.1 Padrões Transversais (ADR-010)

Aplicam-se a **todos** os eventos do Item Catalog:

| Aspecto | Padrão |
|---|---|
| Transporte | RabbitMQ |
| Exchange | `trino.materials` (topic, durable) |
| Dead Letter Exchange | `trino.materials.dlq` |
| Envelope | `{ eventId, eventType, version, aggregateId, aggregateType, occurredAt, correlationId, causationId, actorId, companyId, payload }` |
| Serialização | JSON (UTF-8), campos em camelCase |
| Publicação | Padrão **Outbox**: evento gravado na tabela outbox na mesma transação do agregado; relay assíncrono publica no broker |
| Garantia de entrega | **At-least-once** (consumidor deve ser idempotente) |
| Retry | Até 5 tentativas com backoff exponencial (`messaging.retry.max-attempts=5`); após esgotar, mensagem vai para DLQ |
| Idempotência | Chave `(eventId, consumerName)` persistida pelo consumidor; redeliveries são descartadas sem efeito colateral |
| Ordenação | Garantida **por agregado** (`aggregateId` como routing key de partição); sem garantia de ordem global entre agregados distintos |
| Versionamento | Campo `version` no envelope; versão inicial `1` para todos os eventos deste documento |
| Correlação | `correlationId` propaga a cadeia de causação (ex.: Inativação → Notificação); `causationId` referencia o comando/evento causador |

### 14.2 Fichas Técnicas

#### EVT-IC-001 — ItemCreated

| Campo | Valor |
|---|---|
| Payload | `{ itemId, code, description, unitOfMeasureId, groupId, categoryId, ca, sizeGridId, imageFileId, status: "DRAFT" }` |
| Publisher | Item Catalog Service (MMS-002) |
| Consumers | Timeline Service, Audit Service, CatalogReadModelRefresher |
| Version | 1 |
| CorrelationId | Obrigatório (requestId do comando CreateItem) |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `itemId` |
| Garantias | At-least-once; evento não crítico para operação (item em Rascunho não é visível aos consumidores) |

#### EVT-IC-002 — ItemUpdated

| Campo | Valor |
|---|---|
| Payload | `{ itemId, changedFields[], previousValues{}, newValues{} }` — inclui alterações de dados cadastrais, grade de tamanhos, CA e imagem |
| Publisher | Item Catalog Service (MMS-002) |
| Consumers | Timeline Service, Audit Service, CatalogReadModelRefresher, projeções locais dos módulos consumidores |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName); consumidores de projeção aplicam por versão do agregado |
| Ordem | Por `itemId` |
| Garantias | At-least-once |

#### EVT-IC-003 — ItemActivated

| Campo | Valor |
|---|---|
| Payload | `{ itemId, code, description, groupId, categoryId, previousStatus, newStatus: "ACTIVE", activatedBy, activatedAt }` |
| Publisher | Item Catalog Service (MMS-002) |
| Consumers | Timeline Service, Audit Service, CatalogReadModelRefresher, projeções MMS-003/004/005/PR-001 |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `itemId` |
| Garantias | At-least-once; **evento crítico** — afeta a busca operacional dos consumidores; falha de entrega gera alerta operacional |

#### EVT-IC-004 — ItemInactivated

| Campo | Valor |
|---|---|
| Payload | `{ itemId, code, previousStatus, newStatus: "INACTIVE", reason, inactivatedBy, inactivatedAt }` |
| Publisher | Item Catalog Service (MMS-002) |
| Consumers | Timeline Service, Audit Service, Notification Service (POL-IC-04), CatalogReadModelRefresher, projeções MMS-003/004/005/PR-001 |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `itemId` |
| Garantias | At-least-once; **evento crítico** — item deixa de ser selecionável; falha de entrega gera alerta operacional |

#### EVT-IC-005 — ItemDiscarded

| Campo | Valor |
|---|---|
| Payload | `{ itemId, code, previousStatus, newStatus: "DISCARDED", reason, discardedBy, discardedAt }` |
| Publisher | Item Catalog Service (MMS-002) |
| Consumers | Timeline Service, Audit Service, Notification Service (POL-IC-04), CatalogReadModelRefresher, projeções MMS-003/004/005/PR-001 |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `itemId` |
| Garantias | At-least-once; **evento crítico e irreversível** — estado terminal; falha de entrega gera alerta operacional |

#### EVT-IC-006 — ReplenishmentParametersChanged

| Campo | Valor |
|---|---|
| Payload | `{ itemId, previousParameters{ reorderPoint, minStock, maxStock, leadTimeDays }, newParameters{ reorderPoint, minStock, maxStock, leadTimeDays }, changedBy }` |
| Publisher | Item Catalog Service (MMS-002) |
| Consumers | Timeline Service, Audit Service, CatalogReadModelRefresher, MMS-005 (indicadores de estoque crítico) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `itemId` |
| Garantias | At-least-once |

#### EVT-IC-007 — SynonymAdded

| Campo | Valor |
|---|---|
| Payload | `{ itemId, synonymId, term, addedBy }` |
| Publisher | Item Catalog Service (MMS-002) |
| Consumers | Timeline Service, Audit Service, CatalogReadModelRefresher (índice de busca) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `itemId` |
| Garantias | At-least-once |

#### EVT-IC-008 — SynonymRemoved

| Campo | Valor |
|---|---|
| Payload | `{ itemId, synonymId, term, removedBy }` |
| Publisher | Item Catalog Service (MMS-002) |
| Consumers | Timeline Service, Audit Service, CatalogReadModelRefresher (índice de busca) |
| Version | 1 |
| CorrelationId | Obrigatório |
| Retry | 5× exponencial → DLQ |
| Dead Letter | `trino.materials.dlq` |
| Idempotência | (eventId, consumerName) |
| Ordem | Por `itemId` |
| Garantias | At-least-once |

### 14.3 Matriz Resumo de Garantias

| Evento | At-least-once | Idempotente | Ordenado por agregado | Crítico (alerta em falha de entrega) |
|---|---|---|---|---|
| EVT-IC-001 ItemCreated | ✅ | ✅ | ✅ | Não |
| EVT-IC-002 ItemUpdated | ✅ | ✅ | ✅ | Não |
| EVT-IC-003 ItemActivated | ✅ | ✅ | ✅ | **Sim** |
| EVT-IC-004 ItemInactivated | ✅ | ✅ | ✅ | **Sim** |
| EVT-IC-005 ItemDiscarded | ✅ | ✅ | ✅ | **Sim** (irreversível) |
| EVT-IC-006 ReplenishmentParametersChanged | ✅ | ✅ | ✅ | Não |
| EVT-IC-007 SynonymAdded | ✅ | ✅ | ✅ | Não |
| EVT-IC-008 SynonymRemoved | ✅ | ✅ | ✅ | Não |

### 14.4 Rastreabilidade Comando → Evento

| Comando | Evento publicado | Seção de validação |
|---|---|---|
| CMD-IC-001 CreateItem | EVT-IC-001 ItemCreated | Seção 7 (IC-BR-001..005, 011, 080) |
| CMD-IC-002 UpdateItem | EVT-IC-002 ItemUpdated | Seção 7 (IC-BR-002..005, 011, 021) |
| CMD-IC-003 SetReplenishmentParameters | EVT-IC-006 ReplenishmentParametersChanged | MMS-002-02 (IC-BR-050..052) |
| CMD-IC-004 AddSynonym | EVT-IC-007 SynonymAdded | Seção 7 (IC-BR-040) |
| CMD-IC-005 RemoveSynonym | EVT-IC-008 SynonymRemoved | MMS-002-02 (IC-BR-041) |
| CMD-IC-006 SetSizeGrid | EVT-IC-002 ItemUpdated (`changedFields: ["sizeGrid"]`) | Seção 7 (IC-BR-082) |
| CMD-IC-007 SetCA | EVT-IC-002 ItemUpdated (`changedFields: ["ca"]`) | Seção 7 (IC-BR-080/081) |
| CMD-IC-008 SetImage | EVT-IC-002 ItemUpdated (`changedFields: ["image"]`) | Seção 7 (IC-BR-083) |
| CMD-IC-009 ActivateItem | EVT-IC-003 ItemActivated | Seção 7 (guards de ativação) |
| CMD-IC-010 InactivateItem | EVT-IC-004 ItemInactivated | MMS-002-03 (transição) |
| CMD-IC-011 ReactivateItem | EVT-IC-003 ItemActivated | Seção 7 (guards de ativação) |
| CMD-IC-012 DiscardItem | EVT-IC-005 ItemDiscarded | MMS-002-03 (transição terminal) |
| CMD-IC-013 AddComment | — (somente timeline) | — |

---

## Histórico de Versão

| Versão | Data | Autor | Descrição |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: Event Storming completo do Item Catalog com 13 comandos, 8 eventos de domínio, 5 políticas, validações automáticas, matriz evento × reação, eventos consumidos (FD-001-09) e especificação técnica por evento conforme ADR-010 (exchange `trino.materials`, Outbox, at-least-once, DLQ). |
