# FD-001-09 — Master Data

| Campo | Valor |
|---|---|
| **Documento** | FD-001-09 |
| **Módulo** | Foundation — Master Data (FD-BC-009) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-07-30 |
| **Dependências** | FD-001 (Foundation Overview), FD-001-01 (IAM), FD-001-02 (Organization), FD-001-06 (Audit Service), ADR-009 (Banco como Projeção do Domínio), ADR-010 (Eventos de Negócio), ADR-011 (Padrão de Documentação Foundation) |
| **Referências de negócio** | PR-001-05 §14.1 (Mensageria Transversal), FD-001-04 Workflow Engine (routing rules por categoria), PR-001-04 Domain Model (unidade de medida, moeda, categoria) |

---

## 1. Objetivo

O Master Data é o domínio do Foundation responsável pelos **dados de referência corporativos** compartilhados por todos os módulos: catálogos estáveis, governados e versionados que dão significado comum aos dados de negócio — quando uma requisição diz "10 UN", "BRL" ou "Categoria TI-004", todos os módulos entendem a mesma coisa.

Ele entrega:

1. **Catálogos de referência governados** — tipos de dados mestres com ciclo de vida próprio (rascunho → ativo → inativo), validade temporal e localização de rótulos;
2. **Códigos imutáveis** — a identidade de negócio (`code`) nunca muda; rótulos podem evoluir;
3. **Distribuição por eventos** — mudanças publicadas para que módulos e projeções se atualizem;
4. **Leitura de alta performance** — cache com invalidação por evento, sem join em tempo de uso;
5. **Governança e auditoria** — quem mantém cada catálogo, com trilha completa.

### 1.1 Fronteira com a Organization (FD-001-02)

| Pertence à Organization (FD-001-02) | Pertence ao Master Data (FD-001-09) |
|---|---|
| Estrutura organizacional viva: empresas, unidades organizacionais, centros de custo, limites e configurações da organização | Catálogos de referência transversais: unidades de medida, moedas, países/regiões, categorias de compra, condições de pagamento, Incoterms |

**Regra de bolso:** se o dado define **quem/quem pertence a quem** (estrutura), é Organization; se define **vocabulário de negócio reutilizável** (referência), é Master Data.

### 1.2 O que o Master Data NÃO é

- **Não é cadastro de negócio.** Fornecedores, materiais/serviços e clientes pertencem a módulos de negócio próprios (Supplier Management, catálogo de materiais do Inventory — futuro); o Master Data fornece apenas os vocabulários que esses cadastros usam.
- **Não é configuração de sistema.** Parâmetros técnicos e feature flags pertencem ao Configuration (FD-001-10).
- **Não é fonte de cálculo fiscal/contábil.** Códigos fiscais aqui são referência cadastral, não motor de cálculo.

---

## 2. Conceitos do Domínio

| Conceito | Descrição |
|---|---|
| **MasterDataType** | Definição de um catálogo (ex.: `unit-of-measure`, `currency`, `purchase-category`): esquema de atributos, chave natural, governança (papel mantenedor), se é global ou por organização. |
| **MasterDataItem** | Registro de um catálogo: `code` imutável + atributos + rótulos localizados + validade + status. |
| **Code** | Identificador de negócio imutável (ex.: `UN`, `BRL`, `TI-004`); formato validado por regex do tipo. |
| **LocalizedLabel** | Rótulo por idioma (`pt-BR`, `en-US`), com fallback pt-BR. |
| **ValidityWindow** | `validFrom` / `validTo`: permite agendar vigências e aposentar códigos sem quebrar histórico. |
| **Status** | `DRAFT` → `ACTIVE` → `INACTIVE` (inativo preserva histórico e leituras antigas; nunca é apagado fisicamente quando referenciado). |
| **Hierarchy** | Alguns catálogos suportam hierarquia (ex.: categorias de compra em árvore); ciclos são proibidos. |
| **DistributionEvent** | Evento de mudança (MD-EVT-xxx) que propaga criação/alteração/desativação para consumidores e cache. |

---

## 3. Catálogos do MVP

| Tipo | Escopo | Uso principal |
|---|---|---|
| `unit-of-measure` | Global | Quantidades em itens de requisição/pedido/estoque futuro |
| `currency` | Global | Valores, alçadas por valor (routing rules do workflow) |
| `country` / `region` | Global | Endereços, origem de fornecedores, Incoterms |
| `purchase-category` | Por organização | Routing rules de aprovação (FD-001-04), relatórios de spend |
| `payment-term` | Por organização | Condições de pagamento em pedidos/contratos futuros |
| `incoterm` | Global | Termos de entrega internacional |

- **Globais:** mantidos pela administração da plataforma; iguais para todas as organizações (ex.: ISO 4217 para moedas, UN/ECE Rec 20 para UoM como referência inicial).
- **Por organização:** mantidos pelo administrador da organização dentro dos tipos habilitados.
- Novos tipos entram por **configuração versionada** (MD-BR-010) — sem código novo para catálogos padronizados.

---

## 4. Modelo de Dados do MasterDataItem

| Campo | Descrição |
|---|---|
| `itemId` | UUIDv7. |
| `typeCode` | Catálogo ao qual pertence (ex.: `purchase-category`). |
| `code` | Chave natural imutável (única por tipo + organização). |
| `labels` | `{ "pt-BR": "…", "en-US": "…" }`. |
| `attributes` | JSON validado pelo esquema do tipo (ex.: `{ "symbol": "R$", "decimals": 2 }` para moeda). |
| `parentCode` | (Opcional) hierarquia dentro do mesmo tipo (ciclos proibidos — MD-BR-007). |
| `organizationId` | Nulo em catálogos globais; obrigatório em catálogos por organização. |
| `validFrom` / `validTo` | Janela de vigência (agendável). |
| `status` | `DRAFT` / `ACTIVE` / `INACTIVE`. |
| `createdBy` / `updatedBy` / `version` | Governança e controle otimista de concorrência. |

- Índice principal: `(type_code, organization_id, code)`; leitura funcional por `(type_code, organization_id, status, valid_from, valid_to)`.
- FKs lógicas (ADR-009): módulos referenciam por `typeCode + code`, nunca por FK física cruzada.

---

## 5. Distribuição e Cache

1. **Referência por código:** documentos de negócio gravam `typeCode + code` (nunca o rótulo como identidade).
2. **Leitura funcional:** módulos resolvem rótulos/atributos via cache compartilhado (Redis) alimentado pelo Master Data; chave `md:{type}:{org|global}:{code}`.
3. **Invalidação por evento:** MD-EVT-xxx invalida/atualiza o cache; fallback = consulta direta com repovoamento.
4. **Snapshot em documentos:** para rótulos exibidos em documentos históricos, o módulo consumidor pode desnormalizar o rótulo no momento da gravação (padrão já adotado na Timeline), garantindo que mudança de rótulo não reescreva o passado.
5. **Vigência em validações:** validar se `code` está `ACTIVE` e vigente na **data de referência do documento** (não na data de hoje) — MD-BR-005.

---

## 6. Regras de Negócio

| Código | Regra |
|---|---|
| **MD-BR-001** | `code` é imutável após a criação; correção de código exige novo item e desativação do anterior. |
| **MD-BR-002** | `code` é único por (`typeCode`, `organizationId`); formato validado pela regex do tipo. |
| **MD-BR-003** | Item nunca é excluído fisicamente quando referenciado; aposentadoria = `INACTIVE` + `validTo`. |
| **MD-BR-004** | Somente itens `ACTIVE` e vigentes podem ser usados em novos documentos de negócio. |
| **MD-BR-005** | A vigência é avaliada na data de referência do documento consumidor, não na data da consulta. |
| **MD-BR-006** | Rótulos são localizados com fallback pt-BR; identidade nunca depende de rótulo. |
| **MD-BR-007** | Hierarquias não admitem ciclos nem mudança de pai que viole a regra; profundidade máxima configurável por tipo. |
| **MD-BR-008** | Toda criação, alteração, ativação e desativação publica evento e gera trilha de auditoria (FD-001-06). |
| **MD-BR-009** | Isolamento multiempresa: catálogos por organização são invisíveis a outras organizações; globais são somente leitura para mantenedores de organização. |
| **MD-BR-010** | Novos tipos de catálogo entram por configuração versionada (esquema + governança), sem código novo. |
| **MD-BR-011** | Manutenção exige o papel mantenedor definido no tipo (ex.: `masterdata.org.manage` para catálogos de organização, `masterdata.global.manage` para globais); segregação de funções com o aprovador de negócio quando a organização exigir. |
| **MD-BR-012** | Mudanças em itens usados por routing rules do workflow (ex.: categoria) não alteram workflows já instanciados — definições e referências são pinadas no momento da instanciação (FD-001-04). |

---

## 7. Eventos

### 7.1 Publicados pelo Master Data

| Código | Evento | Payload (resumo) | Consumidores |
|---|---|---|---|
| **MD-EVT-001** | MasterDataItemCreated | typeCode, code, organizationId, labels, validFrom | Cache, módulos, Audit |
| **MD-EVT-002** | MasterDataItemUpdated | typeCode, code, changedAttributes, version | Cache, módulos, Audit |
| **MD-EVT-003** | MasterDataItemActivated | typeCode, code, validFrom | Cache, módulos |
| **MD-EVT-004** | MasterDataItemDeactivated | typeCode, code, validTo, reason? | Cache, módulos, Audit |
| **MD-EVT-005** | MasterDataTypeRegistered | typeCode, scope, schemaVersion | Plataforma, Audit |
| **MD-EVT-006** | MasterDataHierarchyChanged | typeCode, code, oldParent, newParent | Cache, Audit |

Envelope e garantias conforme PR-001-05 §14.1 / ADR-010: outbox, at-least-once, idempotência por `eventId + consumer`, versionamento `v1`, `correlationId`.

### 7.2 Consumidos

| Origem | Uso |
|---|---|
| (MVP: nenhum) | Catálogos são mantidos por API administrativa; na v2, importação de fontes externas (ex.: tabelas ISO atualizadas) poderá automatizar criação via job auditado. |

---

## 8. APIs

| Método | Endpoint | Descrição | Permissão |
|---|---|---|---|
| `GET` | `/api/v1/master-data/{typeCode}?cursor=&status=&q=` | Lista itens do catálogo (leitura funcional, com cache) | autenticado |
| `GET` | `/api/v1/master-data/{typeCode}/{code}` | Detalhe + rótulos + vigência | autenticado |
| `GET` | `/api/v1/master-data/{typeCode}/{code}/resolve?date=` | Resolve item na data de referência (MD-BR-005) | autenticado |
| `GET` | `/api/v1/master-data/types` | Catálogos registrados e seus esquemas | autenticado |
| `POST` | `/api/v1/admin/master-data/{typeCode}` | Cria item (DRAFT) | mantenedor do tipo |
| `PATCH` | `/api/v1/admin/master-data/{typeCode}/{code}` | Altera rótulos/atributos/vigência (version + 409 em conflito) | mantenedor do tipo |
| `POST` | `/api/v1/admin/master-data/{typeCode}/{code}/activate` | Ativa item | mantenedor do tipo |
| `POST` | `/api/v1/admin/master-data/{typeCode}/{code}/deactivate` | Desativa com `validTo` (+ `reason`) | mantenedor do tipo |
| `POST` | `/api/v1/admin/master-data/types` | Registra novo tipo (configuração versionada) | `masterdata.type.manage` |

- Erros padronizados: `MD-ERR-001` (code duplicado), `MD-ERR-002` (formato inválido), `MD-ERR-003` (item inativo/vencido na data), `MD-ERR-004` (ciclo na hierarquia), `MD-ERR-005` (conflito de versão), `MD-ERR-006` (escopo proibido — global por mantenedor de organização).

---

## 9. Integrações com o Foundation

| Domínio | Integração |
|---|---|
| **FD-001-01 IAM** | Papéis mantenedores (`masterdata.*`), autenticação das APIs. |
| **FD-001-02 Organization** | Escopo por organização, fronteira estrutura × vocabulário (§1.1). |
| **FD-001-04 Workflow Engine** | Categorias e moedas usadas em routing rules; pin na instanciação (MD-BR-012). |
| **FD-001-06 Audit Service** | Trilha de toda manutenção de catálogos (MD-BR-008). |
| **FD-001-10 Configuration** *(planejado)* | Feature toggles de tipos habilitados por organização. |
| **Módulos de negócio** | Referência por `typeCode + code` em itens, valores, categorias e condições. |

---

## 10. Requisitos Não Funcionais

| Categoria | Requisito |
|---|---|
| **Desempenho** | Leitura funcional via cache com hit ratio alvo ≥ 98%; P95 ≤ 50 ms por resolução. |
| **Consistência** | Cache invalidado por evento; fallback com repovoamento; sem leitura stale além da janela de propagação (≤ 5 s P95). |
| **Confiabilidade** | Outbox na publicação; consumidores idempotentes. |
| **Segurança** | Manutenção segregada por papel; SoD opcional com aprovador de negócio (MD-BR-011); auditoria completa. |
| **Observabilidade** | Métricas de hit ratio, invalidações, itens por tipo, lag de eventos. |
| **Internacionalização** | Rótulos pt-BR/en-US com fallback; atributos regionais (símbolo de moeda, decimais) no esquema. |

---

## 11. Restrições Arquiteturais

1. **Vocabulário, não cadastro:** dados com ciclo de vida de negócio (fornecedor, material, cliente) ficam fora — fronteira formal em §1.1/§1.2.
2. **Referência sempre por código:** proibido persistir rótulo como identidade ou fazer join físico entre schemas (ADR-009).
3. **Sem efeito retroativo:** mudanças de catálogo não reescrevem documentos nem workflows já instanciados (MD-BR-005/012).
4. **Configuração acima de customização:** tipos novos por configuração versionada (MD-BR-010).
5. **Multiempresa por construção:** escopo global × organização explícito no modelo.

---

## 12. Critérios de Conclusão do Módulo (MVP)

- [ ] Seis catálogos do MVP registrados (3 globais + 3 por organização) com esquemas e governança.
- [ ] CRUD administrativo com DRAFT/ACTIVE/INACTIVE, validade agendável e controle otimista de versão.
- [ ] Hierarquia com bloqueio de ciclos (categorias).
- [ ] Cache Redis com invalidação por MD-EVT e fallback.
- [ ] Endpoint `resolve?date=` implementando MD-BR-005.
- [ ] Auditoria completa da manutenção (MD-BR-008).
- [ ] Testes: unicidade de code, imutabilidade, vigência por data de referência, ciclo de hierarquia, escopo global × organização, isolamento multiempresa, invalidação de cache.

---

## 13. Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Descrição |
|---|---|---|
| FD-001-01 IAM | Obrigatória | Papéis mantenedores, autenticação. |
| FD-001-02 Organization | Obrigatória | Escopo por organização, fronteira de domínio. |
| FD-001-04 Workflow Engine | Consumo | Routing rules por categoria/valor (pin na instanciação). |
| FD-001-06 Audit Service | Produção de eventos | Trilha de manutenção. |
| PR-001 Purchase Requisition | Consumo | UoM, moeda, categoria nos itens. |
| RFQ / Purchase Order / Inventory (futuros) | Consumo | Vocabulários compartilhados. |

---

## 14. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Seis catálogos do MVP, ciclo de vida DRAFT/ACTIVE/INACTIVE, validade temporal, hierarquia sem ciclos, cache com invalidação, eventos, auditoria, APIs administrativas e funcionais. |
| **v2.0** | Importação/exportação CSV auditada, seeds ISO (UoM UN/ECE, moedas, países), bulk operations, en-US completo, aprovação de mudanças em catálogo crítico (workflow de governança). |
| **v3.0** | Sincronização com ERP externo (conector), catálogos compartilhados entre organizações do grupo (escopo `group`), versionamento de esquema de atributos com migração. |

---

## 15. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Versão inicial aprovada: objetivo, fronteira com Organization, conceitos, catálogos do MVP, modelo do MasterDataItem, distribuição e cache, regras MD-BR-001..012, eventos MD-EVT-001..006, APIs, integrações, NFRs, restrições, critérios de conclusão, matriz de dependência e roadmap. | Arquitetura Trino |
