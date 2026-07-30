# MMS-002-08 — User Stories

**Documento:** MMS-002-08 — User Stories
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 v1.1.0, MMS-002-02 v1.1.0 (Business Rules), MMS-002-03 (State Machine), MMS-002-05 (Event Storming), MMS-002-07 (Use Cases)
**Referências:** PR-001-08 (padrão de formato), GOV-001

> Especificação das User Stories do módulo Item Catalog.

---

# Epic

EP-IC-001 — Gestão do Catálogo de Itens

---

# Capability

BC-IC-001 — Gerenciar Catálogo de Itens (EPI, Fardamento e Materiais)

---

# Feature

FT-IC-001 — Cadastro de Itens

---

## US-IC-001 — Cadastrar Item

### Persona

Gerente de Suprimentos (Mantenedor do Catálogo)

### História

Como mantenedor do catálogo,

Quero cadastrar um novo item com código, descrição, unidade, grupo e categoria,

Para disponibilizá-lo formalmente às áreas da empresa.

### Valor de Negócio

Eliminar cadastros informais e descentralizados; garantir fonte única de itens para todos os módulos.

### Critérios de Aceite

```gherkin
Scenario: Cadastrar item com sucesso

Given que o usuário possui permissão IC-PERM-001

When preencher os dados obrigatórios válidos

Then o item deverá ser criado em Rascunho
And o evento ItemCreated deverá ser publicado
```

### Regras

IC-BR-001

IC-BR-002

IC-BR-003

IC-BR-004

IC-BR-005

### Eventos

ItemCreated (EVT-IC-001)

### Caso de Uso

UC-IC-001

---

## US-IC-002 — Cadastrar EPI com CA

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero informar o Certificado de Aprovação (CA) ao cadastrar um EPI,

Para garantir conformidade com as normas de segurança do trabalho.

### Valor de Negócio

Conformidade regulatória (NR): nenhum EPI sem CA válido pode ser disponibilizado aos colaboradores.

### Critérios de Aceite

```gherkin
Scenario: Ativar EPI sem CA

Given um item do grupo EPI em Rascunho sem CA informado

When o mantenedor solicitar a ativação

Then o sistema deverá bloquear com IC-ERR-080
And o item deverá permanecer em Rascunho
```

### Regras

IC-BR-080

IC-BR-081

### Eventos

ItemUpdated (EVT-IC-002, `changedFields: ["ca"]`)

### Caso de Uso

UC-IC-001, UC-IC-003

---

## US-IC-003 — Duplicar Item Existente

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero criar um item a partir de um existente,

Para agilizar o cadastro de itens semelhantes (ex.: mesmo EPI em outra grade).

### Valor de Negócio

Reduzir tempo de cadastro e erros de digitação em itens similares.

### Critérios de Aceite

```gherkin
Scenario: Duplicar item

Given um item existente no catálogo

When o mantenedor acionar "Duplicar" e informar novo código

Then o novo item deverá nascer em Rascunho sem vínculo com a origem
```

### Regras

IC-BR-001

### Eventos

ItemCreated (EVT-IC-001)

### Caso de Uso

UC-IC-001 (A3)

---

# Feature

FT-IC-002 — Manutenção de Itens

---

## US-IC-004 — Editar Item

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero corrigir e atualizar dados de um item,

Para manter o catálogo íntegro e atualizado.

### Valor de Negócio

Qualidade de dados com rastreabilidade completa de alterações (before/after).

### Critérios de Aceite

```gherkin
Scenario: Editar item em uso

Given um item Ativo referenciado por operações abertas

When o mantenedor tentar alterar o grupo do item

Then o sistema deverá bloquear com IC-ERR-021
And alterações descritivas deverão permanecer permitidas
```

### Regras

IC-BR-021

IC-BR-070

### Eventos

ItemUpdated (EVT-IC-002)

### Caso de Uso

UC-IC-002

---

## US-IC-005 — Gerenciar Sinônimos

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero cadastrar sinônimos para os itens,

Para que os colaboradores encontrem o item pelo nome que usam no dia a dia.

### Valor de Negócio

Reduzir falhas de busca e duplicidade de cadastro por nomes diferentes ("capacete" × "capacete de segurança").

### Critérios de Aceite

```gherkin
Scenario: Adicionar sinônimo duplicado

Given um item com o sinônimo "capacete"

When o mantenedor tentar adicionar "Capacete"

Then o sistema deverá recusar com IC-ERR-040 (normalização case-insensitive)
```

### Regras

IC-BR-040

IC-BR-041

### Eventos

SynonymAdded (EVT-IC-007)

SynonymRemoved (EVT-IC-008)

### Caso de Uso

UC-IC-005

---

## US-IC-006 — Gerenciar Imagem do Item

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero associar uma imagem ao item,

Para que solicitantes identifiquem visualmente o produto correto.

### Valor de Negócio

Reduzir erros de solicitação por identificação incorreta do item.

### Critérios de Aceite

```gherkin
Scenario: Upload de imagem fora da conformidade

Given um item em Rascunho

When o mantenedor enviar imagem com formato não permitido

Then o sistema deverá recusar com IC-ERR-083
And a imagem não deverá ser persistida
```

### Regras

IC-BR-083

### Eventos

ItemUpdated (EVT-IC-002, `changedFields: ["image"]`)

### Caso de Uso

UC-IC-001, UC-IC-002

---

## US-IC-007 — Vincular Grade de Tamanhos

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero vincular uma grade de tamanhos ao item (P/M/G/GG, 38–46 etc.),

Para que o solicitante escolha o tamanho correto na solicitação.

### Valor de Negócio

Eliminar trocas por tamanho errado; base para o controle de estoque por variante (MMS-004).

### Critérios de Aceite

```gherkin
Scenario: Vincular grade inativa

Given uma grade de tamanhos inativada no Master Data

When o mantenedor tentar vinculá-la a um item

Then o sistema deverá recusar com IC-ERR-082
```

### Regras

IC-BR-082

### Eventos

ItemUpdated (EVT-IC-002, `changedFields: ["sizeGrid"]`)

### Caso de Uso

UC-IC-002

---

# Feature

FT-IC-003 — Ciclo de Vida do Item

---

## US-IC-008 — Ativar Item

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero ativar um item após completar seus dados,

Para disponibilizá-lo nas solicitações e movimentações.

### Valor de Negócio

Garantir que somente itens íntegros e validados sejam usados operacionalmente.

### Critérios de Aceite

```gherkin
Scenario: Ativar item válido

Given um item em Rascunho com todos os guards satisfeitos

When o mantenedor ativar o item

Then o status deverá ser Ativo
And o evento ItemActivated deverá ser publicado
And o item deverá aparecer na busca operacional dos módulos
```

### Regras

IC-BR-001..005

IC-BR-080..083

### Eventos

ItemActivated (EVT-IC-003)

### Caso de Uso

UC-IC-003

---

## US-IC-009 — Inativar Item

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero inativar um item que não deve mais ser solicitado,

Para impedir novas operações sem perder o histórico.

### Valor de Negócio

Controle de obsolescência preservando rastreabilidade das operações passadas.

### Critérios de Aceite

```gherkin
Scenario: Inativar item crítico

Given um item Ativo com solicitações em aberto

When o mantenedor inativar com motivo

Then o status deverá ser Inativo
And o almoxarifado deverá ser notificado (POL-IC-04)
```

### Regras

MMS-002-03 (transições)

IC-BR-070

### Eventos

ItemInactivated (EVT-IC-004)

### Caso de Uso

UC-IC-004

---

## US-IC-010 — Reativar Item

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero reativar um item inativado,

Para voltar a disponibilizá-lo quando a condição de inativação deixar de existir.

### Valor de Negócio

Reversibilidade operacional sem recriação de cadastro.

### Critérios de Aceite

```gherkin
Scenario: Reativar item com Master Data invalidado

Given um item Inativo cuja grade de tamanhos foi inativada no Master Data

When o mantenedor solicitar a reativação

Then o sistema deverá bloquear e listar as pendências
And o item deverá permanecer Inativo
```

### Regras

IC-BR-080..083 (guards reexecutados)

### Eventos

ItemActivated (EVT-IC-003)

### Caso de Uso

UC-IC-004 (FA-IC-003)

---

## US-IC-011 — Descartar Item

### Persona

Gerente de Suprimentos / Administrador

### História

Como mantenedor do catálogo,

Quero descartar definitivamente um item inativo sem uso futuro,

Para manter o catálogo limpo de registros obsoletos.

### Valor de Negócio

Higiene do catálogo com preservação do histórico (descarte lógico, nunca físico).

### Critérios de Aceite

```gherkin
Scenario: Descartar item com confirmação

Given um item Inativo sem operações em aberto

When o mantenedor descartar com motivo e confirmação

Then o status deverá ser Inativo (Descarte), terminal e irreversível
And o evento ItemDiscarded deverá ser publicado
```

### Regras

MMS-002-03 (estado terminal)

IC-BR-021

### Eventos

ItemDiscarded (EVT-IC-005)

### Caso de Uso

UC-IC-004

---

# Feature

FT-IC-004 — Consulta ao Catálogo

---

## US-IC-012 — Buscar Item por Sinônimo

### Persona

Solicitante / Almoxarifado

### História

Como solicitante,

Quero encontrar um item pelo nome que uso no dia a dia,

Para registrar minha solicitação sem conhecer o código ou a descrição oficial.

### Valor de Negócio

Menos erros e menos itens duplicados por dificuldade de localização.

### Critérios de Aceite

```gherkin
Scenario: Buscar por sinônimo

Given um item Ativo com sinônimo "capacete branco"

When o usuário buscar "capacete branco"

Then o item deverá ser retornado com destaque do termo
```

### Regras

IC-BR-021 (somente Ativos na busca operacional)

### Eventos

—

### Caso de Uso

UC-IC-006

---

## US-IC-013 — Filtrar Catálogo

### Persona

Gerente de Suprimentos / Auditor

### História

Como mantenedor ou auditor,

Quero filtrar o catálogo por status, grupo, categoria e presença de CA,

Para gerenciar e fiscalizar o cadastro.

### Valor de Negócio

Gestão e auditoria do catálogo (ex.: listar EPIs sem CA pendente de regularização).

### Critérios de Aceite

```gherkin
Scenario: Filtrar EPIs sem CA

Given itens do grupo EPI em Rascunho sem CA

When o usuário filtrar por "grupo=EPI" e "sem CA"

Then apenas esses itens deverão ser listados
```

### Regras

IC-BR-071 (projeção de leitura)

### Eventos

—

### Caso de Uso

UC-IC-006

---

## US-IC-014 — Visualizar Timeline do Item

### Persona

Gerente de Suprimentos / Auditor

### História

Como auditor,

Quero visualizar toda a linha do tempo de um item,

Para rastrear quem fez o quê e quando.

### Valor de Negócio

Auditoria obrigatória (princípio do projeto) e transparência do ciclo de vida.

### Critérios de Aceite

```gherkin
Scenario: Visualizar timeline

Given um item com histórico de cadastro, edição e ativação

When o auditor abrir a timeline

Then todos os eventos deverão aparecer em ordem cronológica com ator e timestamp
```

### Regras

IC-BR-070

### Eventos

—

### Caso de Uso

UC-IC-006 (A2)

---

# Feature

FT-IC-005 — Parâmetros de Reposição

---

## US-IC-015 — Definir Parâmetros de Reposição

### Persona

Gerente de Suprimentos

### História

Como mantenedor do catálogo,

Quero definir ponto de reposição, estoque mínimo/máximo e lead time por item,

Para alimentar os alertas de estoque crítico e a reposição.

### Valor de Negócio

Base paramétrica para os indicadores de ruptura e cobertura do MMS-005.

### Critérios de Aceite

```gherkin
Scenario: Definir parâmetros incoerentes

Given um item Ativo

When o mantenedor definir estoque mínimo maior que o máximo

Then o sistema deverá recusar com IC-ERR-050
```

### Regras

IC-BR-050

IC-BR-051

IC-BR-052

### Eventos

ReplenishmentParametersChanged (EVT-IC-006)

### Caso de Uso

UC-IC-007

---

# Matriz de Rastreabilidade

| Story | UC | Regra | Evento |
|-------|----|-------|--------|
| US-IC-001 | UC-IC-001 | IC-BR-001..005 | ItemCreated (EVT-IC-001) |
| US-IC-002 | UC-IC-001/003 | IC-BR-080/081 | ItemUpdated (EVT-IC-002) |
| US-IC-003 | UC-IC-001 | IC-BR-001 | ItemCreated (EVT-IC-001) |
| US-IC-004 | UC-IC-002 | IC-BR-021/070 | ItemUpdated (EVT-IC-002) |
| US-IC-005 | UC-IC-005 | IC-BR-040/041 | SynonymAdded/Removed (EVT-IC-007/008) |
| US-IC-006 | UC-IC-001/002 | IC-BR-083 | ItemUpdated (EVT-IC-002) |
| US-IC-007 | UC-IC-002 | IC-BR-082 | ItemUpdated (EVT-IC-002) |
| US-IC-008 | UC-IC-003 | IC-BR-001..005/080..083 | ItemActivated (EVT-IC-003) |
| US-IC-009 | UC-IC-004 | State Machine / IC-BR-070 | ItemInactivated (EVT-IC-004) |
| US-IC-010 | UC-IC-004 | IC-BR-080..083 | ItemActivated (EVT-IC-003) |
| US-IC-011 | UC-IC-004 | State Machine / IC-BR-021 | ItemDiscarded (EVT-IC-005) |
| US-IC-012 | UC-IC-006 | IC-BR-021 | — |
| US-IC-013 | UC-IC-006 | IC-BR-071 | — |
| US-IC-014 | UC-IC-006 | IC-BR-070 | — |
| US-IC-015 | UC-IC-007 | IC-BR-050/051/052 | ReplenishmentParametersChanged (EVT-IC-006) |

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: 15 User Stories (US-IC-001..015) em 5 Features (Cadastro, Manutenção, Ciclo de Vida, Consulta, Parâmetros de Reposição), com persona, valor de negócio, critérios Gherkin, regras, eventos, UC vinculado e matriz de rastreabilidade completa. |
