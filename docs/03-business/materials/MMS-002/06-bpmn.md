# MMS-002-06 — Business Process Specification (BPMN)

**Documento:** MMS-002-06 — Business Process Specification (BPMN)
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 v1.1.0, MMS-002-02 v1.1.0 (Business Rules), MMS-002-03 (State Machine), MMS-002-04 (Domain Model), MMS-002-05 (Event Storming), ADR-010, FD-001-03, FD-001-05, FD-001-06, FD-001-07, FD-001-09
**Referências:** PR-001-06 (padrão de formato Enterprise), GOV-001

> Especificação do processo de negócio para o ciclo de vida do Catálogo de Itens (Item Catalog).

---

# 1. Objetivo

Descrever o fluxo operacional completo do Item Catalog, desde a identificação da necessidade de cadastro de um novo item até sua ativação para uso pelos módulos consumidores (MMS-003, MMS-004, MMS-005, PR-001), bem como os fluxos de manutenção, inativação, reativação e descarte.

Este documento é a base para modelagem BPMN 2.0 e para implementação do módulo. Em caso de conflito: transições de estado prevalecem conforme MMS-002-03; validações conforme MMS-002-02; contratos de eventos conforme MMS-002-05 §14.

---

# 2. Participantes (Pools e Lanes)

## Pool: Empresa

### Lane: Gerente de Suprimentos (Mantenedor do Catálogo)
- Identifica necessidade de novo item
- Cadastra e edita itens
- Parametriza (sinônimos, grade de tamanhos, CA, imagem, parâmetros de reposição)
- Ativa, inativa, reativa e descarta itens

### Lane: Administrador
- Todas as ações do Gerente de Suprimentos
- Administra configurações do módulo (`materials.item.*`)
- Trata exceções de Master Data e segurança

### Lane: Sistema
- Executa validações automáticas (guards de cadastro e ativação)
- Publica eventos de domínio (Outbox → RabbitMQ)
- Registra timeline e auditoria
- Invalida/atualiza a projeção de leitura do catálogo (cache)
- Dispara notificações (POL-IC-04, POL-IC-05)

### Lane: Módulos Consumidores (MMS-003 / MMS-004 / MMS-005 / PR-001)
- Consomem projeção do catálogo para seleção de itens Ativos
- Referenciam itens em operações próprias (nunca alteram o catálogo)

---

# 3. Evento Inicial

### Start Event

**Necessidade de Item Identificada**

Gatilhos:

- Novo EPI ou fardamento a disponibilizar aos colaboradores
- Novo material de consumo/almoxarifado
- Substituição de item descontinuado
- Regularização de item criado fora do catálogo (legado)
- Necessidade identificada em solicitação (MMS-003) sem item cadastrado

**Tipo BPMN:** None Start Event (início manual, acionado pelo mantenedor).

---

# 4. Fluxo Principal

## Atividade 1

Cadastrar Item

Responsável:

Gerente de Suprimentos / Administrador

Entrada mínima: código, descrição, unidade de medida, grupo (EPI / Fardamento / Material), categoria.

Saída:

Item em Rascunho (ST-IC-001)

**Tipo BPMN:** User Task.
**Business Object de saída:** BO-IC-001 Item (status = Rascunho).
**Evento:** MSG-IC-001 `ItemCreated` (EVT-IC-001).

---

## Atividade 2

Parametrizar Item

Opcional, repetível

Inclui uma ou mais de:

- Incluir/remover sinônimos de busca
- Vincular grade de tamanhos (FD-001-09 `SIZE_GRID`)
- Informar CA (Certificado de Aprovação) — obrigatório para grupo EPI (IC-BR-080)
- Associar imagem do item (FD-001-03)
- Definir parâmetros de reposição (ponto de reposição, mínimo, máximo, lead time)

**Tipo BPMN:** User Task (opcional, multi-instância).
**Business Objects:** BO-IC-002 Synonym, BO-IC-003 ReplenishmentParameters, BO-IC-004 ItemImage.
**Data Store:** DS-IC-002 Document Storage (MinIO) para imagem — escrita via FD-001-03; nunca expõe URL pública.
**Eventos:** MSG-IC-002 `ItemUpdated` (EVT-IC-002, com `changedFields`), MSG-IC-006 `ReplenishmentParametersChanged` (EVT-IC-006), MSG-IC-007/008 `SynonymAdded`/`SynonymRemoved` (EVT-IC-007/008).

---

## Atividade 3

Solicitar Ativação

Responsável:

Gerente de Suprimentos / Administrador

**Tipo BPMN:** User Task.
**Pré-condição:** item em Rascunho (ou Inativo, no caso de reativação — FA-IC-003).

---

## Atividade 4

Validação Automática de Ativação

Executada pelo Sistema.

Valida (guards IC-BR-001..005, 011, 080..083):

- Código único por empresa
- Descrição obrigatória e íntegra
- Unidade de medida ativa no Master Data
- Grupo/categoria válidos e ativos
- CA presente e válido quando grupo = EPI
- Grade de tamanhos ativa quando vinculada
- Imagem conforme (tipo/tamanho) quando presente
- Código ERP único quando informado

**Tipo BPMN:** Service Task.
**Timeout:** TIME-IC-001 — 30 segundos; em timeout, EXC-IC-007 (ver §11 e §15.2).
**Data Store lido:** DS-IC-001 Transactional Database (PostgreSQL) + consulta ao FD-001-09 Master Data.

---

## Gateway 1

Ativação Válida?

SIM

↓

Item Ativo

NÃO

↓

Retornar ao Rascunho com pendências

**Tipo BPMN:** Exclusive Gateway (XOR).
**Regras do gateway:** ver §9.1 (RG-IC-GW-001).

---

## Atividade 5

Publicar Item Ativo

Item torna-se visível na busca operacional dos módulos consumidores.

**Tipo BPMN:** Service Task + Message Intermediate Throw Event.
**Message Event:** MSG-IC-003 `ItemActivated` (EVT-IC-003) publicado no exchange `trino.materials` (MMS-002-05 §14).
**Reação dos consumidores:** projeções locais incluem o item como selecionável (MMS-002-05 §8.5).

---

## Atividade 6

Manter Item

Em estado Ativo, o mantenedor pode editar dados cadastrais e parâmetros (Atividade 2 repetida), respeitando IC-BR-021 (restrições quando o item está em uso).

**Tipo BPMN:** User Task (repetível).
**Eventos:** MSG-IC-002/006/007/008 conforme a alteração.

---

## Atividade 7

Inativar Item

Responsável:

Gerente de Suprimentos / Administrador

Item deixa de ser selecionável em novas operações; referências históricas preservadas.

**Tipo BPMN:** User Task + Message Intermediate Throw Event.
**Message Event:** MSG-IC-004 `ItemInactivated` (EVT-IC-004).
**Política:** POL-IC-04 — se item crítico, notificar Gerente de Suprimentos e Almoxarifado.

---

## Gateway 2

Destino do Item Inativo?

REATIVAR

↓

Retorna à Atividade 3 (Solicitar Ativação — FA-IC-003)

DESCARTAR

↓

Descarte terminal

**Tipo BPMN:** Exclusive Gateway (XOR).
**Regras do gateway:** ver §9.2 (RG-IC-GW-002).

---

## Atividade 8

Descartar Item

Transição terminal: Inativo → Inativo (Descarte). Irreversível.

**Tipo BPMN:** User Task + Message End Event.
**Message Event:** MSG-IC-005 `ItemDiscarded` (EVT-IC-005) — evento crítico (MMS-002-05 §14.3).

---

## Evento Final

Fim do Processo

**Tipo BPMN:** End Events múltiplos conforme desfecho (ver §8).

---

# 5. Fluxos Alternativos

## FA-IC-001

Ativação Rejeitada pela Validação

Origem:

Atividade 4 (GW-001 = NÃO)

Destino:

Rascunho com lista de pendências

**Detalhamento:** o sistema devolve ao mantenedor a lista exata de pendências (ex.: "CA obrigatório para grupo EPI", "Grade de tamanhos inativa"). Nenhum evento de ativação é publicado; a tentativa é registrada em Timeline/Audit.

---

## FA-IC-002

Edição de Item em Uso

Origem:

Atividade 6

**Detalhamento:** quando o item está referenciado por operações em aberto (MMS-003/MMS-004), alterações estruturais (grupo, unidade, grade) seguem IC-BR-021: bloqueadas ou restringidas conforme configuração `materials.item.edit.in-use-policy`. Alterações descritivas (sinônimos, imagem, parâmetros) permanecem livres.

---

## FA-IC-003

Reativação de Item Inativo

Origem:

GW-002 (REATIVAR)

Destino:

Atividade 3 → validação completa de ativação é reexecutada (guards IC-BR-080..083 incluídos)

**Detalhamento:** reativação exige as mesmas validações da ativação original — um item inativado pode ter ficado inválido por mudança de Master Data (POL-IC-05). Em sucesso, MSG-IC-003 `ItemActivated`.

---

## FA-IC-004

Impacto de Master Data

Origem:

Evento externo FD-001-09 (`MasterDataValueUpdated` / `MasterDataValueInactivated`)

**Detalhamento:** o sistema avalia os itens que referenciam o valor alterado/inativado (POL-IC-05): novas ativações que usem o valor inválido são bloqueadas (IC-BR-003/004/005/082) e o mantenedor é notificado. Nenhum dado de item é alterado automaticamente.

---

# 6. Gateways

## GW-IC-001

Validação de ativação

Resultado:

Válido

ou

Retornar ao Rascunho com pendências

## GW-IC-002

Destino do item inativo

Resultado:

Reativar

ou

Descartar

**As regras detalhadas de cada gateway estão na Seção 9.**

---

# 7. Objetos de Dados

Item

Synonym

ReplenishmentParameters

ItemImage

Timeline Entry

Audit Record

**Especificação completa dos Business Objects e Data Stores na Seção 8.1.**

---

# 8. Eventos BPMN

### Start Event

Need for Item Identified

---

### Intermediate Events

ItemCreated

ItemUpdated

ItemActivated

ItemInactivated

ItemReactivated

ItemDiscarded

MasterDataImpactDetected

---

### End Events

Item Active (em uso pelos consumidores)

Item Discarded (terminal)

---

## 8.1 Business Objects e Data Stores (detalhamento Enterprise)

### Business Objects

| Código | Business Object | Descrição | Estados possíveis no processo | Persistência |
| ------ | --------------- | --------- | ------------------------------ | ------------ |
| BO-IC-001 | Item | Aggregate principal do catálogo | Rascunho, Ativo, Inativo, Inativo (Descarte) | DS-IC-001 |
| BO-IC-002 | Synonym | Sinônimo de busca do item | Ativo, Removido | DS-IC-001 |
| BO-IC-003 | ReplenishmentParameters | Parâmetros de reposição (ponto, mínimo, máximo, lead time) | Vigente | DS-IC-001 |
| BO-IC-004 | ItemImage | Imagem do item | Pendente de upload, Confirmada | DS-IC-001 (metadados) + DS-IC-002 (binário) |
| BO-IC-005 | Timeline Entry | Registro cronológico de evento do item | Imutável (append-only) | DS-IC-001 |
| BO-IC-006 | Audit Record | Registro de auditoria de alteração | Imutável (append-only) | DS-IC-001 |

### Data Stores

| Código | Data Store | Tecnologia | Acesso |
| ------ | ---------- | ---------- | ------ |
| DS-IC-001 | Transactional Database | PostgreSQL | Leitura/escrita transacional via repositórios do domínio; isolamento por `company_id` |
| DS-IC-002 | Document Storage | MinIO | Imagem do item via FD-001-03; sem URL pública; acesso via URLs assinadas de curta duração |
| DS-IC-003 | Message Broker | RabbitMQ | Publicação de eventos (outbox relay) no exchange `trino.materials` e consumo pelos serviços (Timeline, Audit, Notification, projeções) |
| DS-IC-004 | Distributed Cache | Redis | Projeção de leitura do catálogo (IC-BR-071); nunca fonte de verdade; TTL configurável |
| DS-IC-005 | Master Data Store | PostgreSQL (FD-001-09) | Leitura de unidades, grupos, categorias e grades de tamanho para validação |

---

## 8.2 Message Events (detalhamento Enterprise)

| Código | Message Event | Tipo BPMN | Momento | Payload/Referência |
| ------ | ------------- | --------- | ------- | ------------------ |
| MSG-IC-001 | ItemCreated | Intermediate Throw | Após Atividade 1 | MMS-002-05 §14.2 EVT-IC-001 |
| MSG-IC-002 | ItemUpdated | Intermediate Throw | Após Atividades 2 e 6 | MMS-002-05 §14.2 EVT-IC-002 |
| MSG-IC-003 | ItemActivated | Intermediate Throw | Saída "SIM" de GW-IC-001 (ativação ou reativação) | MMS-002-05 §14.2 EVT-IC-003 |
| MSG-IC-004 | ItemInactivated | Intermediate Throw | Após Atividade 7 | MMS-002-05 §14.2 EVT-IC-004 |
| MSG-IC-005 | ItemDiscarded | Message End | Após Atividade 8 | MMS-002-05 §14.2 EVT-IC-005 |
| MSG-IC-006 | ReplenishmentParametersChanged | Intermediate Throw | Atividade 2/6 (parâmetros) | MMS-002-05 §14.2 EVT-IC-006 |
| MSG-IC-007 | SynonymAdded | Intermediate Throw | Atividade 2/6 (sinônimo incluído) | MMS-002-05 §14.2 EVT-IC-007 |
| MSG-IC-008 | SynonymRemoved | Intermediate Throw | Atividade 2/6 (sinônimo removido) | MMS-002-05 §14.2 EVT-IC-008 |
| MSG-IC-009 | MasterDataValueChanged | Intermediate Catch | FA-IC-004 (evento externo FD-001-09) | MMS-002-05 §13 |

---

# 9. Regras por Gateway (detalhamento Enterprise)

## 9.1 RG-IC-GW-001 — Regras do Gateway de Ativação

| Código | Condição avaliada | Regra de negócio associada | Resultado quando falha |
| ------ | ----------------- | -------------------------- | ---------------------- |
| RG-IC-GW-001-A | Código único por empresa | IC-BR-001 | Retorno ao Rascunho com pendência (`IC-ERR-001`) |
| RG-IC-GW-001-B | Descrição obrigatória e dentro do limite | IC-BR-002 | Retorno ao Rascunho (`IC-ERR-002`) |
| RG-IC-GW-001-C | Unidade de medida ativa no Master Data | IC-BR-003 | Retorno ao Rascunho (`IC-ERR-003`) |
| RG-IC-GW-001-D | Grupo e categoria válidos e ativos | IC-BR-004, IC-BR-005 | Retorno ao Rascunho (`IC-ERR-004`/`IC-ERR-005`) |
| RG-IC-GW-001-E | CA presente e válido quando grupo = EPI (categorias de `materials.item.epi.categories`) | IC-BR-080, IC-BR-081 | Retorno ao Rascunho (`IC-ERR-080`/`IC-ERR-081`) |
| RG-IC-GW-001-F | Grade de tamanhos ativa quando vinculada | IC-BR-082 | Retorno ao Rascunho (`IC-ERR-082`) |
| RG-IC-GW-001-G | Imagem conforme (tipo/tamanho) quando presente | IC-BR-083 | Retorno ao Rascunho (`IC-ERR-083`) |
| RG-IC-GW-001-H | Código ERP único quando informado | IC-BR-011 | Retorno ao Rascunho (`IC-ERR-011`) |
| RG-IC-GW-001-I | Mantenedor possui permissão de ativação no escopo organizacional | MMS-002-09 Permissions | Exceção EXC-IC-003 (bloqueio de segurança; não retorna ao rascunho) |

**Semântica:** qualquer falha em A–H → fluxo "Retornar ao Rascunho com pendências" (FA-IC-001). Falha em I → exceção de segurança (§11), com auditoria obrigatória e notificação ao Administrador.

## 9.2 RG-IC-GW-002 — Regras do Gateway de Destino do Item Inativo

| Código | Condição avaliada | Regra de negócio associada | Resultado |
| ------ | ----------------- | -------------------------- | --------- |
| RG-IC-GW-002-A | Mantenedor escolhe reativar | MMS-002-03 (Inativo → Ativo) | FA-IC-003: validação completa reexecutada (RG-IC-GW-001) |
| RG-IC-GW-002-B | Mantenedor escolhe descartar, com motivo obrigatório | MMS-002-03 (Inativo → Inativo (Descarte)); IC-BR-021 | Atividade 8 — irreversível; bloqueado se houver restrição de uso ativa |
| RG-IC-GW-002-C | Item possui operações em aberto que impedem descarte | IC-BR-021 | Exceção EXC-IC-004 (descarte bloqueado; permanece Inativo) |

---

# 10. Indicadores do Processo

Tempo médio de cadastro até ativação

Taxa de ativação rejeitada (pendências)

Itens ativos / inativos / descartados por período

Tempo médio de permanência em Rascunho

Reativações por período

Itens impactados por mudança de Master Data

**Detalhamento:** todos os indicadores são derivados dos registros imutáveis de Timeline (BO-IC-005) e Audit (BO-IC-006), sem consulta ao estado mutável do aggregate. KPIs do módulo estão em MMS-002 v1.1.0 e MMS-005.

---

# 11. Exceções

Código de item duplicado

Master Data inválido ou inativo (unidade, grupo, categoria, grade)

Mantenedor sem permissão

Descarte bloqueado por uso em operação aberta

CA ausente/inválido para EPI

Imagem fora da conformidade

Falha técnica de validação

## 11.1 Catálogo de Exceções (detalhamento Enterprise)

| Código | Exceção | Origem | Tratamento | Notificação |
| ------ | ------- | ------ | ---------- | ----------- |
| EXC-IC-001 | Código de item duplicado na empresa | GW-IC-001 (RG-IC-GW-001-A) | Retorno ao Rascunho com indicação do item conflitante | Mantenedor |
| EXC-IC-002 | Master Data inválido/inativo referenciado | GW-IC-001 (RG-IC-GW-001-C/D/F) ou FA-IC-004 | Ativação bloqueada; mantenedor ajusta referência; Administrador regulariza Master Data | Mantenedor + Administrador |
| EXC-IC-003 | Mantenedor sem permissão | GW-IC-001 (RG-IC-GW-001-I) | Bloqueio de segurança; auditoria obrigatória; não retorna ao rascunho | Administrador (security audit) |
| EXC-IC-004 | Descarte bloqueado por uso em operação aberta | GW-IC-002 (RG-IC-GW-002-C) | Item permanece Inativo; reavaliação após encerramento das operações | Mantenedor |
| EXC-IC-005 | CA ausente/inválido para grupo EPI | GW-IC-001 (RG-IC-GW-001-E) | Retorno ao Rascunho com pendência de CA | Mantenedor |
| EXC-IC-006 | Imagem fora da conformidade | GW-IC-001 (RG-IC-GW-001-G) | Retorno ao Rascunho; imagem rejeitada não é persistida | Mantenedor |
| EXC-IC-007 | Falha técnica de validação (timeout TIME-IC-001) | Atividade 4 | Compensação COMP-IC-001: validação reexecutada (até 3 tentativas); após 3 falhas, item permanece no estado anterior com motivo técnico registrado | Mantenedor + Suporte |

---

# 12. SLA por Etapa

| Etapa | SLA Padrão |
|--------|------------|
| Cadastro (Rascunho) | Livre |
| Validação de ativação | < 30 segundos |
| Publicação para consumidores | Imediata |
| Tratamento de impacto de Master Data | 5 dias úteis |
| Manutenção | Livre |

## 12.1 Detalhamento de SLA (Enterprise)

| Etapa | SLA Padrão | Configuração | Medição | Ação em estouro |
| ----- | ---------- | ------------ | ------- | --------------- |
| Cadastro (Rascunho) | Livre | — | Não medido | — |
| Validação de ativação | < 30 s | `materials.item.validation.timeout.seconds=30` | Entrada Atividade 4 → decisão GW-IC-001 | EXC-IC-007 / COMP-IC-001 |
| Publicação para consumidores | Imediata (< 5 min) | `messaging.critical-event.alert.minutes=5` | MSG-IC-003/004/005 → consumo pelas projeções | ESC-IC-002 (evento crítico em DLQ) |
| Impacto de Master Data | 5 dias úteis | `materials.item.masterdata-impact.sla.days=5` | MSG-IC-009 → ajuste ou ciência do mantenedor | Lembrete ao mantenedor; sem bloqueio |
| Manutenção | Livre | — | Não medido | — |

---

# 13. Pontos de Integração

Notification Engine (FD-001-05)

Timeline (FD-001-07)

Audit (FD-001-06)

Master Data (FD-001-09)

Document Management / MinIO (FD-001-03)

Módulos consumidores (MMS-003, MMS-004, MMS-005, PR-001)

**Detalhamento:** integrações de leitura com FD-001-09 (validações) e FD-001-03 (imagem); integrações de evento com Timeline, Audit e Notification conforme MMS-002-05 §8; projeções dos módulos consumidores alimentadas pelos eventos de ativação/inativação/descarte. Contratos de eventos conforme MMS-002-05 §14 (exchange `trino.materials`).

---

# 14. Artefatos Relacionados

Business Rules (MMS-002-02)

State Machine (MMS-002-03)

Domain Model (MMS-002-04)

Event Storming (MMS-002-05)

Use Cases (MMS-002-07 — a produzir)

API (MMS-002-13 — a produzir)

Database (MMS-002-11 — a produzir)

---

# 15. Elementos Enterprise do Processo

## 15.1 Escalonamentos

| Código | Escalonamento | Gatilho | Ação | Destino |
| ------ | ------------- | ------- | ---- | ------- |
| ESC-IC-001 | Impacto de Master Data sem tratamento | Estouro de `materials.item.masterdata-impact.sla.days` | Notificar mantenedor + Gerente de Suprimentos; registrar em Timeline/Audit | Gerente de Suprimentos |
| ESC-IC-002 | Evento crítico em DLQ (EVT-IC-003, 004, 005) | Falha de mensageria após retry | Alerta operacional imediato; reconciliação via outbox | Time de Operações/SRE |

## 15.2 Timeouts

| Código | Timeout | Escopo | Valor padrão | Comportamento |
| ------ | ------- | ------ | ------------ | ------------- |
| TIME-IC-001 | Validação automática de ativação | Atividade 4 | 30 s | EXC-IC-007 → COMP-IC-001 |
| TIME-IC-002 | Publicação de evento crítico | MSG-IC-003/004/005 | 5 min | ESC-IC-002 |
| TIME-IC-003 | Tratamento de impacto de Master Data | FA-IC-004 | 5 dias úteis (configurável) | ESC-IC-001 |

## 15.3 Eventos Intermediários de Tempo

| Código | Tipo BPMN | Definição |
| ------ | --------- | --------- |
| TMR-IC-001 | Timer Intermediate Catch (non-interrupting) em FA-IC-004 | 50% do SLA de impacto de Master Data → lembrete ao mantenedor |
| TMR-IC-002 | Timer Intermediate Catch (interrupting) em FA-IC-004 | 100% do SLA → ESC-IC-001; fluxo principal não é cancelado |

## 15.4 Compensações

| Código | Compensação | Quando | Ação compensatória |
| ------ | ----------- | ------ | ------------------ |
| COMP-IC-001 | Falha técnica na validação de ativação | EXC-IC-007 | Validação reexecutada (máx. 3 tentativas); item permanece no estado anterior; Timeline registra tentativa |
| COMP-IC-002 | Falha na publicação de evento crítico | TIME-IC-002 | Republicação via outbox relay; se persistir, DLQ + reconciliação manual documentada |
| COMP-IC-003 | Cache/projeção divergente do estado persistido | Detecção por reconciliação (POL-IC-03) | Reconstrução da projeção a partir do banco transacional (fonte de verdade); auditoria da divergência |

---

# 16. Sub-Processo de Validação de Ativação (detalhamento)

```text
┌─────────────────────────────────────────────────────────────┐
│ Sub-Process: Validação de Ativação (Atividade 4)            │
│                                                             │
│  Entrada: item em Rascunho ou Inativo                       │
│      │                                                      │
│      ▼                                                      │
│  Bateria de guards (RG-IC-GW-001-A..H):                     │
│    - Unicidade de código e código ERP                       │
│    - Integridade de descrição                               │
│    - Master Data: unidade, grupo, categoria, grade          │
│    - EPI: CA obrigatório e válido (IC-BR-080/081)           │
│    - Imagem conforme (IC-BR-083)                            │
│      │                                                      │
│  Autorização (RG-IC-GW-001-I):                              │
│    - Falha → EXC-IC-003 (bloqueio de segurança)             │
│      │                                                      │
│  Resultado:                                                 │
│    - Todas OK        → GW-IC-001 SIM → MSG-IC-003           │
│    - Qualquer falha  → GW-IC-001 NÃO → FA-IC-001            │
│                        (Rascunho + lista de pendências)     │
│                                                             │
│  Timeout: TIME-IC-001 (30 s) → EXC-IC-007 → COMP-IC-001     │
└─────────────────────────────────────────────────────────────┘
```

Cada tentativa de ativação (bem ou mal-sucedida) gera registro em BO-IC-005 (Timeline) e BO-IC-006 (Audit), conforme IC-BR-070.

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: processo completo do ciclo de vida do Item Catalog — 8 atividades, 2 gateways com regras detalhadas (RG-IC-GW-001/002), 4 fluxos alternativos, 6 Business Objects, 5 Data Stores, 9 Message Events mapeados aos EVT-IC, 7 exceções, SLA detalhado, 2 escalonamentos, 3 timeouts, 2 timers intermediários, 3 compensações e sub-processo de validação de ativação. |
