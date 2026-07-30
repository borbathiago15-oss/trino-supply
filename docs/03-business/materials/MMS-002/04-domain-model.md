**Documento:** MMS-002-04 — Domain Model
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 (Visão do Módulo — v1.1.0), MMS-002-02 (Business Rules — v1.1.0), MMS-002-03 (State Machine), MMS-001 (Documento Mestre Funcional), FD-001-03, FD-001-09, FD-001-10
**Referências:** PR-001-04 (padrão de formato), MMS-003, MMS-004, MMS-005, PR-001 (consumidores), ADR-009 (domínio não conhece o banco), GOV-001

---

# 1. Objetivo

Representar os conceitos de negócio do módulo Item Catalog, seus relacionamentos, responsabilidades e regras de consistência.

O modelo de domínio é independente de banco de dados, APIs e interface de usuário.

---

# 2. Aggregate Root

## Item (CatalogItem)

O **Item** é o único Aggregate Root do módulo. Ele é responsável por garantir a consistência de todas as informações do cadastro mestre: identidade, classificação, atributos de EPI/Fardamento, parâmetros de reposição e sinônimos.

Nenhuma entidade interna poderá ser alterada diretamente sem passar pelo Aggregate Root.

Responsabilidades:

- Criar item (sempre em Rascunho — IC-BR-006);
- Alterar informações permitidas conforme o estado (matriz de editabilidade — MMS-002-03 §16);
- Definir e alterar parâmetros de reposição;
- Gerenciar sinônimos de busca;
- Definir grade de tamanhos, CA e imagem (atributos de EPI/Fardamento);
- Ativar, inativar, reativar e descartar (transições da State Machine — MMS-002-03);
- Validar regras (IC-BR) e gerar eventos de domínio.

---

# 3. Aggregate Diagram

```text
┌──────────────────────────────────────────────────────────────────┐
│                        AGGREGATE: Item                            │
│                       (Aggregate Root)                            │
│                                                                   │
│  Id · CompanyId · ItemCode · ErpCode · Description ·              │
│  DetailedDescription · Characteristics · Classification ·         │
│  Criticality · Status · UnitOfMeasureRef · CategoryRef ·          │
│  SizeGridRef · CA · ImageDocumentId · Version                     │
│                                                                   │
│  ├── 0..N  Synonym               (Entity)                         │
│  │         Term · CreatedBy · CreatedAt                           │
│  │                                                                │
│  └── 0..1  ReplenishmentParameters  (Entity)                      │
│            MinStock · MaxStock · OrderPoint · LeadTimeDays        │
│                                                                   │
│  Value Objects: ItemCode · ErpCode · ItemDescription ·            │
│  MasterDataReference · CertificateNumber · ReplenishmentLimits    │
│                                                                   │
│  Referências externas (por Id / referência lógica, fora do        │
│  Aggregate):                                                      │
│  Company (FD-001-02) · UnitOfMeasure (FD-001-09) ·                │
│  Category (FD-001-09) · SizeGrid (FD-001-09) ·                    │
│  ImageDocument (FD-001-03) · WorkflowInstance (FD-001-04)         │
└──────────────────────────────────────────────────────────────────┘
```

---

# 4. Relacionamentos UML

```text
Company (FD-001-02)        1 ────── N   Item

Item                     1 ────── N   Synonym
Item                     1 ────── 0..1 ReplenishmentParameters

Item                     N ────── 1   UnitOfMeasure (Master Data, ref lógica typeCode+code)
Item                     N ────── 1   Category      (Master Data, ref lógica typeCode+code)
Item                     N ────── 0..1 SizeGrid     (Master Data, ref lógica typeCode+code)

Item                     1 ────── 0..1 ImageDocument   (FD-001-03, por Id)
Item                     1 ────── 0..1 WorkflowInstance (FD-001-04, por Id — somente
                                          quando approval-required=true, IC-BR-020)
```

Cardinalidades de negócio:

- `Synonym`: 0..N por item; o termo nunca é identidade (IC-BR-030);
- `ReplenishmentParameters`: 0..1 por item no MVP; por depósito somente na versão 1.1 do roadmap;
- `Category`: 1 obrigatória; categorias do grupo EPI disparam a exigência de CA (IC-BR-080);
- `ImageDocument`: 0..1 imagem principal (IC-BR-082).

---

# 5. Entidades

## Item (Aggregate Root)

Representa o item do catálogo mestre.

Principais atributos:

- Id (identidade interna gerada pelo sistema — IC-BR-083);
- Empresa (CompanyId — isolamento multiempresa, IC-BR-007);
- Código (ItemCode — único por empresa, IC-BR-001);
- Código externo do ERP (ErpCode — opcional, IC-BR-083);
- Descrição oficial e descrição detalhada (IC-BR-002);
- Características (texto livre estruturável);
- Classificação: estocável / não estocável / sob encomenda (IC-BR-005);
- Criticidade: parametrizável (IC-BR-013);
- Estado: Rascunho / Ativo / Inativo / Descartado (MMS-002-03);
- Unidade de medida e categoria (referências ao Master Data);
- Grade de tamanhos (referência — IC-BR-081);
- CA (IC-BR-080);
- Imagem (referência FD-001-03 — IC-BR-082);
- Version (optimistic concurrency — IC-BR-042).

---

## Synonym

Representa um termo alternativo de busca (nome informal usado pela operação).

Atributos:

- Id
- Termo (texto normalizado para busca)
- Autor e data de inclusão

Regra: sinônimo é auxílio de busca, **nunca** identidade nem descrição oficial (IC-BR-030).

---

## ReplenishmentParameters

Representa o conjunto de parâmetros de reposição do item.

Atributos:

- Estoque mínimo
- Estoque máximo
- Ponto de pedido
- Lead time de referência (dias)

Invariante própria: mínimo ≤ ponto de pedido ≤ máximo (IC-BR-012); obrigatoriedade para estocáveis é parametrizável (IC-BR-011).

---

# 6. Value Objects

## ItemCode

- Valor (texto, máscara configurável por empresa)

Imutável após a ativação (IC-BR-001/041). Invariante: único por empresa; formato conforme máscara (`materials.item.code.*`).

---

## ErpCode

- Valor (texto)

Imutável após a ativação (IC-BR-083). Invariante: único por empresa quando informado. Igualdade por valor.

---

## ItemDescription

- Valor (texto oficial)

Imutável como Value Object (a alteração gera novo VO e evento Item alterado). Invariante: não vazia, tamanho mínimo/máximo parametrizável (`materials.item.description.*` — IC-BR-002); normalização de espaços e caixa para comparação de semelhança.

---

## MasterDataReference

- `typeCode` + `code`

Referência lógica a um vocabulário do Master Data (FD-001-09): unidade de medida, categoria e grade de tamanhos. Invariante: vocabulário vigente na data de referência (IC-BR-003/004/081); nunca referência por rótulo (fronteira formal da visão).

---

## CertificateNumber (CA)

- Número do Certificado de Aprovação (alfanumérico, máscara `materials.item.epi.ca-mask`)

Imutável como VO. Invariante: obrigatório quando a categoria do item consta em `materials.item.epi.categories` (IC-BR-080); proibido/vazio não se aplica a fardamento.

---

## ReplenishmentLimits

- MinStock (NUMERIC(18,4))
- MaxStock (NUMERIC(18,4))
- OrderPoint (NUMERIC(18,4))
- LeadTimeDays (inteiro ≥ 0)

Imutável. Invariantes: `min ≥ 0`; `min ≤ orderPoint ≤ max` (IC-BR-012); lead time ≥ 0.

---

## Criticality (Value Object de enumeração)

- Baixa, Média, Alta (lista parametrizável — IC-BR-013)

Imutável. Uso: workflow de aprovação cadastral e priorização de alertas.

---

# 7. Enumerações

## Item Status (MMS-002-03)

- Rascunho
- Ativo
- Inativo
- Descartado

## Item Classification

- Estocável
- Não Estocável
- Sob Encomenda

## Criticality

- Baixa / Média / Alta (padrão; extensível por configuração)

---

# 8. Relacionamentos

Item

possui

0..N Synonym

---

Item

possui

0..1 ReplenishmentParameters

---

Item

referencia

1 UnitOfMeasure (FD-001-09) · 1 Category (FD-001-09) · 0..1 SizeGrid (FD-001-09) · 0..1 ImageDocument (FD-001-03)

---

# 9. Invariantes

Um item nunca poderá existir:

- sem empresa (IC-BR-007);
- sem código (IC-BR-001);
- sem descrição oficial (IC-BR-002);
- sem unidade de medida e categoria vigentes (IC-BR-003/004);
- sem classificação (IC-BR-005);
- fora do estado inicial Rascunho na criação (IC-BR-006).

Um item Ativo nunca poderá:

- ter código ou código ERP alterados (IC-BR-041);
- ter unidade alterada sem motivo (IC-BR-040);
- estar em duplicidade de código ou código ERP na empresa (IC-BR-001/083);
- pertencer a categoria do grupo EPI sem CA (IC-BR-080).

Invariantes formalizadas:

| Código | Invariante | Origem |
|--------|-----------|--------|
| INV-IC-01 | `code` único por empresa, imutável após ativação | IC-BR-001/041 |
| INV-IC-02 | `erpCode` único por empresa quando informado | IC-BR-083 |
| INV-IC-03 | Estado inicial sempre Rascunho | IC-BR-006 |
| INV-IC-04 | Transições somente pela matriz da State Machine | MMS-002-03 §7 |
| INV-IC-05 | `min ≤ orderPoint ≤ max`, todos ≥ 0 | IC-BR-012 |
| INV-IC-06 | Categoria do grupo EPI ⇒ CA preenchido | IC-BR-080 |
| INV-IC-07 | Grade informada ⇒ vocabulário vigente (`SIZE_GRID`) | IC-BR-081 |
| INV-IC-08 | Estado Descartado não aceita nenhum comando | MMS-002-03 §4 |
| INV-IC-09 | `version` incrementa a cada alteração persistida | IC-BR-042 |
| INV-IC-10 | Somente Ativo é referenciável por outros módulos | IC-BR-021 (MMS-RG-08) |
| INV-IC-11 | Nenhuma exclusão física; descarte somente de Rascunho sem referências | IC-BR-024 |

---

# 10. Comportamentos

## Item

Métodos de domínio:

Create()

Update() — somente campos permitidos pelo estado (IC-BR-041)

SetReplenishmentParameters()

AddSynonym() / RemoveSynonym()

SetSizeGrid()

SetCA()

SetImage()

Activate() — IC-BR-020

Inactivate(reason) — IC-BR-022

Reactivate(reason) — IC-BR-023

Discard(reason) — IC-BR-024

---

## Factory Methods

Criação sempre via fábricas do Aggregate — construtores nunca expostos:

| Factory | Assinatura | Garantias |
|---------|-----------|-----------|
| `Item.Create` | (companyId, code, description, unitOfMeasureRef, categoryRef, classification, criticality, erpCode?) | Valida unicidade de código/ERP, vigência de unidade e categoria, define Rascunho, `version=1`, publica Item cadastrado (rascunho) |
| `Item.CreateEPI` | (…Create, ca, sizeGridRef?, imageDocumentId?) | Create + INV-IC-06 (CA obrigatório), valida grade (INV-IC-07) e imagem quando exigida (IC-BR-082) |
| `ReplenishmentParameters.Create` | (min, max, orderPoint, leadTimeDays) | Valida ReplenishmentLimits (INV-IC-05) |
| `Synonym.Create` | (term, authorId) | Normaliza o termo (caixa/espaços) e rejeita duplicado no mesmo item |

---

# 11. Repositories

Interfaces de persistência do módulo (implementação em Infrastructure; o domínio não conhece o banco — ADR-009):

| Repositório | Operações |
|-------------|-----------|
| `IItemRepository` | `GetById(id)`, `GetByCode(companyId, code)`, `GetByErpCode(companyId, erpCode)`, `Add(item)`, `Update(item)`, `Search(specification, keyset)` |
| `ISynonymRepository` | (acesso somente via Aggregate; nunca direto) |
| `IReplenishmentParametersRepository` | (acesso somente via Aggregate; nunca direto) |

Regras:

- Todo repositório aplica filtro `company_id` obrigatório (IC-BR-007/061);
- `Update` verifica `version` (optimistic concurrency — IC-BR-042);
- `Search` usa Keyset Pagination (IC-BR-070);
- Não existe `Delete` físico; descarte é soft delete (IC-BR-024).

---

# 12. Specifications

Consultas e validações expressas como especificações combináveis:

| Specification | Propósito | Uso |
|---------------|-----------|-----|
| `ActiveItemsOnly` | Somente itens Ativos na busca operacional | IC-BR-021; consumo por MMS-003/004/005 e PR-001 |
| `ItemByCode` | Filtro por código | UC-IC-006 |
| `ItemByCategory` | Filtro por categoria (ex.: EPI, Fardamento) | UC-IC-006; filtros do almoxarifado (MMS-003 v1.1.0) |
| `ItemByDescription` | Busca por descrição oficial | UC-IC-006 |
| `ItemBySynonym` | Busca por termo alternativo | UC-IC-006 |
| `SimilarDescription` | Candidatos a duplicidade por descrição normalizada | IC-BR-031 (alerta no cadastro) |
| `ActivationEligibility` | Avalia o conjunto de guardas de ativação (IC-BR-001..005/011/080..083) | Activate/Reactivate |
| `ItemsWithoutReplenishment` | Estocáveis sem parâmetros de reposição | KPI de saneamento cadastral |
| `ItemsWithoutMovement` | Itens sem movimento há N dias | KPI de obsolescência (com MMS-004) |
| `ItemsWithinCompanyScope` | Escopo organizacional do usuário | Transversal (IC-BR-062) |

Composição: `And`, `Or`, `Not`.

---

# 13. Domain Services

Operações de domínio que não pertencem naturalmente a uma entidade:

| Serviço | Responsabilidade |
|---------|------------------|
| `ItemUniquenessValidator` | Garante unicidade de código e código ERP por empresa (INV-IC-01/02) |
| `SimilarDescriptionDetector` | Detecta descrições semelhantes e emite alerta não bloqueante (IC-BR-031) |
| `MasterDataVigenceValidator` | Valida vigência de unidade, categoria e grade por data de referência (FD-001-09) |
| `ItemLifecycleService` | Orquestra Activate/Inactivate/Reactivate/Discard: guardas, workflow quando parametrizado (IC-BR-020), eventos, timeline |
| `CatalogReadModelRefresher` | Mantém o cache de leitura do catálogo (IC-BR-071) a partir dos eventos do ciclo de vida |

---

# 14. Policies

Políticas de negócio reativas a eventos:

| Política | Gatilho | Ação |
|----------|---------|------|
| POL-IC-01 | Qualquer evento de domínio | Registrar marco na Timeline (IC-BR-051) |
| POL-IC-02 | Qualquer evento que modifique o Aggregate | Registrar auditoria com anterior/posterior (IC-BR-050) |
| POL-IC-03 | Item ativado / alterado / inativado / parâmetros alterados | Invalidar cache de leitura (IC-BR-071) |
| POL-IC-04 | Item inativado com criticidade alta | Notificar Gerente de Suprimentos (MMS-002-01, gatilhos emergenciais; especificação no MMS-002-10) |
| POL-IC-05 | Unidade/categoria/grade alterada ou inativada no Master Data (evento consumido — FD-001-09) | Avaliar impacto nos itens que a referenciam e registrar alerta (sem alteração retroativa — vigência por data de referência) |

---

# 15. Domain Events Produzidos

Item cadastrado (rascunho)

Item ativado

Item alterado

Item inativado

Item inativado (descarte)

Parâmetros de reposição alterados

Sinônimo incluído/removido

---

# 16. Limites do Aggregate

Fazem parte do Aggregate:

- Item (Aggregate Root)
- Synonym
- ReplenishmentParameters

Não fazem parte (outros Bounded Contexts ou referências externas):

- Saldo e movimentação de estoque (MMS-004);
- Solicitação de material (MMS-003);
- Recebimento (MMS-005);
- Fornecedor, preço e compra (Procurement — PR-001);
- Empresa, unidade e centro de custo (FD-001-02);
- Unidade de medida, categoria e grade de tamanhos (FD-001-09 — referências lógicas);
- Imagem (FD-001-03 — referência por Id);
- Workflow de aprovação cadastral (FD-001-04 — referência por Id).

---

# 17. Dependências

| Dependência | Uso |
|-------------|-----|
| FD-001-01 / FD-001-02 | Autorização, escopo e empresa do item |
| FD-001-03 | Documento da imagem do produto (IC-BR-082) |
| FD-001-04 | Workflow de ativação quando `approval-required=true` (IC-BR-020) |
| FD-001-06 / FD-001-07 | Auditoria e timeline (IC-BR-050/051) |
| FD-001-09 | Vocabulários: unidade, categoria, grade (referências lógicas) |
| FD-001-10 | Parâmetros `materials.item.*` |
| MMS-002-03 | Máquina de estados executada pelo `ItemLifecycleService` |

---

# 18. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação do Domain Model do Item Catalog: Aggregate Root Item com entidades Synonym e ReplenishmentParameters; Aggregate Diagram e relacionamentos UML; Value Objects completos (ItemCode, ErpCode, ItemDescription, MasterDataReference, CertificateNumber, ReplenishmentLimits, Criticality); 11 invariantes formalizadas (INV-IC-01..11) rastreadas às IC-BR; factory methods (incluindo CreateEPI); repositories com company_id, optimistic concurrency e keyset; 10 specifications; 5 domain services; 5 policies; eventos produzidos e limites do Aggregate — no padrão PR-001-04, derivado da visão v1.1.0, das Business Rules v1.1.0 e da State Machine |
