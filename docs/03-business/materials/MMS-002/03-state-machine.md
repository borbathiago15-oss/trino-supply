**Documento:** MMS-002-03 — State Machine
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 (Visão do Módulo — v1.1.0, fluxo macro), MMS-002-02 (Business Rules — v1.1.0, IC-BR-006/020–024/040–042/080–083), MMS-001 (Documento Mestre Funcional — §15, MMS-RG-08), FD-001-04 (Workflow), FD-001-06 (Audit), FD-001-07 (Timeline), FD-001-10 (Configuration)
**Referências:** PR-001-03 (padrão de formato), MMS-003, MMS-004, MMS-005, PR-001 (consumidores do catálogo), GOV-001

---

# 1. Objetivo

Definir todos os estados possíveis de um **Item do Catálogo**, suas transições, restrições e eventos associados, garantindo que todo item evolua de forma controlada, previsível, auditável e compatível com as regras do módulo (MMS-002-02).

Nenhuma transição poderá ocorrer fora das regras definidas neste documento.

---

# 2. Princípios

- Todo item possui exatamente um estado atual.
- O estado inicial de todo item é **Rascunho** (IC-BR-006).
- Toda mudança de estado gera auditoria (IC-BR-050), evento de domínio e marco na Timeline (IC-BR-051).
- Nenhum estado pode ser alterado diretamente no banco de dados — toda transição é **ação de negócio** executada por serviço de domínio (MMS-001 §15).
- Item **nunca é excluído fisicamente**; inativação e descarte são lógicos (restrição arquitetural 4 da visão).
- Somente itens **Ativos** são utilizáveis por novos documentos dos módulos consumidores (IC-BR-021, MMS-RG-08).

---

# 3. Estados Oficiais

| Código | Estado | Final |
|--------|--------|-------|
| ST-IC-001 | Rascunho | Não |
| ST-IC-002 | Ativo | Não |
| ST-IC-003 | Inativo | Não (reativável — IC-BR-023) |
| ST-IC-004 | Descartado | Sim |

---

# 4. Especificação dos Estados

Cada estado define: Entry Actions, Exit Actions, Eventos aceitos, Eventos rejeitados, Guard Conditions, Side Effects, SLA, Responsável e Auditoria.

---

## ST-IC-001 — Rascunho

Situação inicial de todo item (IC-BR-006). O item está em preparação cadastral e **não é visível** na busca operacional dos módulos consumidores (IC-BR-021).

Características:

- Todos os campos editáveis (matriz de editabilidade — seção 16);
- Não referenciável por MMS-003, MMS-004, MMS-005 ou PR-001;
- Pode ser ativado ou descartado, nunca inativado diretamente.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Criar Aggregate; validar unicidade de código por empresa (IC-BR-001) e de código externo do ERP quando informado (IC-BR-083); registrar `created_at`/`created_by`; inicializar `version = 1` |
| Exit Actions | Validar invariantes do destino: ativação exige o conjunto completo de regras (ver Guard Conditions) |
| Eventos aceitos | CreateItem, UpdateItem, SetReplenishmentParameters, ManageSynonyms, SetSizeGrid, SetCA, SetImage, ActivateItem, DiscardItem, AddComment |
| Eventos rejeitados | InactivateItem, ReactivateItem, qualquer referência de módulo consumidor |
| Guard Conditions | **ActivateItem:** IC-BR-001..005 (identidade, descrição, unidade e categoria vigentes, classificação), IC-BR-011 (parâmetros de reposição, quando ativa), IC-BR-080 (CA preenchido para categoria do grupo EPI), IC-BR-081 (grade válida e vigente), IC-BR-082 (imagem presente quando a categoria a exige), IC-BR-083 (código ERP quando obrigatório); quando `materials.item.activation.approval-required=true`, a ativação ocorre somente após aprovação no workflow (FD-001-04) — o item permanece em Rascunho até a decisão · **DiscardItem:** zero referências externas (IC-BR-024) |
| Side Effects | Item cadastrado (rascunho) publicado na criação; Timeline e auditoria a cada alteração; item invisível à busca operacional |
| SLA | Livre (sem SLA); KPI "tempo médio de cadastro/ativação" medido da criação à primeira ativação |
| Responsável | Gerente de Suprimentos (mantenedor do catálogo) |
| Auditoria | Criação e toda alteração de campos, parâmetros, sinônimos, grade, CA e imagem, com valores anterior/posterior |

---

## ST-IC-002 — Ativo

O item está oficialmente vigente no catálogo e **utilizável** por todos os módulos consumidores (IC-BR-021, MMS-RG-08).

Características:

- Visível na busca operacional e referenciável por solicitações, movimentações, recebimentos e compras;
- Edição restrita: código e código ERP imutáveis; unidade de medida somente com motivo (IC-BR-040/041);
- Pode ser inativado, nunca descartado (após referência, nenhum item é excluído — IC-BR-024).

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar Item ativado; registrar `activated_at`/`activated_by`; invalidar cache de leitura (IC-BR-071); disponibilizar na busca operacional |
| Exit Actions | Validar motivo obrigatório da inativação (IC-BR-022) |
| Eventos aceitos | UpdateItem (somente campos permitidos — seção 16), SetReplenishmentParameters, ManageSynonyms, SetSizeGrid, SetCA, SetImage, InactivateItem, AddComment |
| Eventos rejeitados | ActivateItem (já ativo), DiscardItem, edição de código/código ERP, edição de unidade sem motivo |
| Guard Conditions | **UpdateItem:** matriz de editabilidade (IC-BR-041); motivo nas operações sensíveis (IC-BR-040); unidade e categoria vigentes na data de referência (IC-BR-003/004); IC-BR-080/081/082/083 nas alterações de EPI · **SetReplenishmentParameters:** consistência mín ≤ PP ≤ máx (IC-BR-012) · **InactivateItem:** motivo obrigatório (IC-BR-022) |
| Side Effects | Item alterado / Parâmetros de reposição alterados / Sinônimo incluído-removido publicados; invalidação de cache; Timeline e auditoria; alerta de descrição semelhante reavaliado na alteração de descrição (IC-BR-031) |
| SLA | Alterações com efeito imediato; propagação ao cache de leitura < 5s |
| Responsável | Gerente de Suprimentos (mantenedor) |
| Auditoria | Toda alteração com valores anterior/posterior; motivo obrigatório registrado nas operações sensíveis |

---

## ST-IC-003 — Inativo

O item foi descontinuado do catálogo operacional. **Não entra em novas solicitações** (MMS-RG-08), mas o saldo remanescente segue movimentável até zerar (IC-BR-022).

Características:

- Invisível à busca operacional; visível ao mantenedor e à auditoria;
- Referências históricas (solicitações, movimentações, recebimentos, compras) permanecem íntegras e consultáveis;
- Nenhum campo editável; única ação possível é a reativação.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar Item inativado; registrar motivo, `inactivated_at`/`inactivated_by`; remover da busca operacional imediatamente; invalidar cache de leitura |
| Exit Actions | Validar reativação: motivo obrigatório e regras vigentes de ativação (IC-BR-023 → IC-BR-020) |
| Eventos aceitos | ReactivateItem, AddComment, consultas de leitura (mantenedor/auditoria) |
| Eventos rejeitados | UpdateItem, SetReplenishmentParameters, ManageSynonyms, SetSizeGrid, SetCA, SetImage, InactivateItem (já inativo), DiscardItem, qualquer uso em novos documentos |
| Guard Conditions | **ReactivateItem:** motivo obrigatório; item deve satisfazer as regras vigentes de ativação (IC-BR-001..005, IC-BR-011 se ativa, IC-BR-080..083) |
| Side Effects | Movimentação de saldo remanescente permanece permitida no MMS-004 até zerar; Timeline e auditoria |
| SLA | Exclusão da busca operacional imediata (efeito da inativação); sem SLA de permanência |
| Responsável | Gerente de Suprimentos (mantenedor) |
| Auditoria | Inativação com motivo, usuário, data/hora; tentativas de uso bloqueadas registradas quando oriundas de operação |

---

## ST-IC-004 — Descartado

Estado terminal. Rascunho descartado antes de qualquer referência externa (IC-BR-024).

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Publicar Item inativado (descarte); executar soft delete; registrar motivo, usuário, data/hora |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta de auditoria) |
| Eventos rejeitados | Todos |
| Guard Conditions | — (a guarda ocorre na origem: somente Rascunho sem referências pode ser descartado) |
| Side Effects | Timeline final; indicadores de saneamento cadastral (taxa de descarte de rascunhos) |
| SLA | — |
| Responsável | Sistema (a partir da ação do mantenedor) |
| Auditoria | Descarte com motivo, usuário, IP e dispositivo |

---

# 5. Fluxo Principal

Rascunho

↓ (ActivateItem — IC-BR-020)

Ativo

↓ (InactivateItem — IC-BR-022)

Inativo

---

# 6. Fluxos Alternativos

Rascunho

↓ (DiscardItem — IC-BR-024)

Descartado

---

Inativo

↓ (ReactivateItem — IC-BR-023)

Ativo

---

Rascunho com `approval-required=true`:

Rascunho → (workflow FD-001-04 aprova) → Ativo

Rascunho → (workflow rejeita) → permanece Rascunho

---

# 7. Matriz de Transições

| Origem | Destino | Permitido | Guard Condition | Evento |
|--------|---------|-----------|-----------------|--------|
| Rascunho | Ativo | Sim | IC-BR-001..005, IC-BR-011 (se ativa), IC-BR-080..083; workflow quando parametrizado (IC-BR-020) | Item ativado |
| Rascunho | Descartado | Sim | Zero referências externas (IC-BR-024) | Item inativado (descarte) |
| Ativo | Inativo | Sim | Motivo obrigatório (IC-BR-022) | Item inativado |
| Inativo | Ativo | Sim | Motivo obrigatório + regras vigentes de ativação (IC-BR-023) | Item ativado |
| Rascunho | Inativo | Não | — | — |
| Ativo | Descartado | Não | — | — |
| Inativo | Descartado | Não | — | — |
| Descartado | qualquer | Não | — | — |

---

# 8. Eventos de Domínio

Conforme o catálogo funcional da visão do módulo (MMS-002 README — Eventos Publicados); a especificação técnica (envelope, payload) seguirá ADR-010 no documento de eventos do módulo (MMS-002-05):

| Evento | Transição/ação de origem |
|--------|--------------------------|
| Item cadastrado (rascunho) | CreateItem |
| Item ativado | Rascunho→Ativo; Inativo→Ativo |
| Item alterado | UpdateItem, SetSizeGrid, SetCA, SetImage (em Rascunho ou Ativo) |
| Item inativado | Ativo→Inativo |
| Item inativado (descarte) | Rascunho→Descartado |
| Parâmetros de reposição alterados | SetReplenishmentParameters |
| Sinônimo incluído/removido | ManageSynonyms |

---

# 9. Restrições

Não permitido:

- Ativar item fora do estado Rascunho (reativação é ação distinta, exclusiva do estado Inativo);
- Inativar item em Rascunho (o destino de um rascunho encerrado é o descarte);
- Editar qualquer campo de item Inativo;
- Descartar item Ativo, Inativo ou referenciado;
- Alterar código ou código ERP após a ativação;
- Alterar unidade de medida sem motivo em item Ativo;
- Referenciar item Rascunho, Inativo ou Descartado em novos documentos (IC-BR-021);
- Excluir fisicamente qualquer item (restrição arquitetural 4).

---

# 10. Auditoria

Cada transição de estado registra (FD-001-06, IC-BR-050):

- usuário;
- data e hora;
- estado anterior e novo estado;
- motivo (obrigatório em inativação, reativação e descarte);
- valores anterior/posterior dos campos alterados;
- IP e dispositivo;
- observações.

---

# 11. Timeline

Cada transição gera automaticamente (FD-001-07, IC-BR-051):

- marco na Timeline do item;
- registro de auditoria;
- evento de domínio;
- notificação quando aplicável (ex.: item crítico inativado — MMS-002-01, gatilhos emergenciais; especificação no MMS-002-10).

---

# 12. APIs Relacionadas (conceituais — especificação no MMS-002-13)

POST /api/v1/items

PATCH /api/v1/items/{id}

POST /api/v1/items/{id}/activate

POST /api/v1/items/{id}/inactivate

DELETE /api/v1/items/{id}

PUT /api/v1/items/{id}/replenishment

PUT /api/v1/items/{id}/synonyms

PUT /api/v1/items/{id}/image

GET /api/v1/items/{id}/timeline

---

# 13. Regras Relacionadas

IC-BR-006 (estado inicial Rascunho)

IC-BR-020 (ativação; workflow parametrizável)

IC-BR-021 (somente Ativo é utilizável — MMS-RG-08)

IC-BR-022 (inativação lógica com motivo)

IC-BR-023 (reativação auditada)

IC-BR-024 (descarte de rascunho)

IC-BR-040 (motivo em alterações sensíveis)

IC-BR-041 (editabilidade por estado)

IC-BR-042 (optimistic concurrency)

IC-BR-080..083 (guardas de EPI na ativação/alteração)

---

# 14. Casos de Uso Relacionados (conceituais — especificação no MMS-002-07)

UC-IC-001 (Cadastrar item)

UC-IC-002 (Editar item / descartar rascunho)

UC-IC-003 (Ativar item)

UC-IC-004 (Inativar / reativar item)

UC-IC-005 (Gerenciar sinônimos)

UC-IC-006 (Consultar catálogo / timeline)

UC-IC-007 (Parametrizar reposição)

---

# 15. Casos de Teste (conceituais — especificação no MMS-002-17)

TC-IC-020 (ativação válida e inválida)

TC-IC-021 (uso bloqueado de item não ativo)

TC-IC-022 (inativação com/sem motivo)

TC-IC-023 (reativação com/sem motivo)

TC-IC-024 (descarte permitido/bloqueado)

TC-IC-041 (matriz de editabilidade por estado)

TC-IC-080..083 (guardas de EPI nas transições)

---

# 16. Matriz de Editabilidade por Estado (IC-BR-041)

| Campo | Rascunho | Ativo | Inativo |
|-------|----------|-------|---------|
| Código | Editável | Proibido | — |
| Código externo do ERP | Editável | Proibido | — |
| Descrição / descrição detalhada | Editável | Editável | — |
| Unidade de medida | Editável | Editável **com motivo** (IC-BR-040) | — |
| Categoria | Editável | Editável (vigência FD-001-09) | — |
| Classificação / criticidade | Editável | Editável | — |
| Grade de tamanhos | Editável | Editável (IC-BR-081) | — |
| CA | Editável | Editável (IC-BR-080) | — |
| Imagem | Editável | Editável (IC-BR-082) | — |
| Características | Editável | Editável | — |
| Sinônimos | Editável | Editável | — |
| Parâmetros de reposição | Editável | Editável (IC-BR-012) | — |
| Motivo da operação | — | Obrigatório nas sensíveis | Obrigatório na reativação |

---

# 17. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação da State Machine do Item Catalog: 4 estados (Rascunho, Ativo, Inativo, Descartado) com entry/exit actions, eventos aceitos/rejeitados, guard conditions (IC-BR-020..024/040–042/080–083), side effects, SLA, responsável e auditoria por estado; fluxo principal e alternativos; matriz de transições (8 combinações avaliadas); matriz de editabilidade por estado (IC-BR-041); rastreabilidade com regras, UCs, APIs e testes conceituais — no padrão PR-001-03, derivada da visão do módulo (v1.1.0) e das Business Rules (v1.1.0) |
