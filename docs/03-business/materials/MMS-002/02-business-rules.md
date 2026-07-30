**Documento:** MMS-002-02 — Business Rules
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 (Visão do Módulo), MMS-002-01 (Business Context), MMS-001 (Documento Mestre Funcional — seções 6, 8.1, 14, 23), FD-001-09 (Master Data), FD-001-10 (Configuration)
**Referências:** MMS-003, MMS-004, MMS-005, PR-001-02 (padrão de formato), FD-001-01, FD-001-02, FD-001-04, FD-001-06, FD-001-07, GOV-001

---

# 1. Objetivo

Este documento formaliza as **regras de negócio** do módulo Item Catalog. Cada regra possui código, nome, descrição, tipo, validação, mensagem e código de erro, evento, caso de uso, API, caso de teste, configuração e observações — no padrão estabelecido pelo PR-001-02.

Regras de vínculo:

- Nenhuma regra inventa comportamento: toda regra deriva da visão aprovada do módulo (MMS-002 README), do Business Context (MMS-002-01) ou das regras gerais da suíte (MMS-001, seção 14 — códigos MMS-RG referenciados explicitamente).
- Casos de uso (`UC-IC-xxx`), endpoints e casos de teste (`TC-IC-xxx`) citados aqui são **referências conceituais** que serão especificados nos documentos MMS-002-07 (Use Cases), MMS-002-13 (API) e MMS-002-17 (Test Scenarios) — sem divergir deste catálogo.
- Conflito entre implementação e este documento resolve-se pela documentação.

---

# 2. Classificação das Regras

| Tipo | Significado | Tratamento |
|------|-------------|------------|
| **Obrigatória** | Inegociável em qualquer implantação | Exige teste automatizado (DoD) |
| **Parametrizável** | Comportamento definido por configuração (FD-001-10) | Valor padrão documentado; teste nos dois modos |
| **Informativa** | Orientação de qualidade que gera indicador, sem bloqueio | Verificada por KPI/relatório |

---

# 3. Convenções deste Documento

- **Regras:** `IC-BR-xxx`, sequenciais por família, imutáveis após publicação;
- **Erros:** `IC-ERR-xxx` (catálogo de erros do módulo, espelhado nas mensagens i18n `ic.*`);
- **Eventos:** nomes funcionais do ciclo de vida (MMS-002 README — Eventos Publicados); a especificação técnica seguirá ADR-010 no documento de eventos do módulo;
- **Casos de uso:** `UC-IC-xxx` (conceituais até o MMS-002-07);
- **Casos de teste:** `TC-IC-xxx` (conceituais até o MMS-002-17);
- **API:** caminhos conceituais sob `/api/v1/items` (especificação no MMS-002-13).

---

# 4. Regras Gerais (Identidade e Cadastro)

## IC-BR-001 — Código Único por Empresa

| Campo | Valor |
|-------|-------|
| Código | IC-BR-001 |
| Nome | Código Único por Empresa |
| Descrição | Todo item possui um código único dentro da empresa, atribuído no cadastro e imutável após a ativação. |
| Tipo | Obrigatória |
| Validação | Unicidade por (empresa, código) garantida por constraint; formato conforme máscara configurada. |
| Mensagem de erro | "Já existe um item com este código nesta empresa." |
| Código do erro | IC-ERR-001 |
| Evento | Item cadastrado (rascunho) |
| Caso de uso | UC-IC-001 |
| API | POST /api/v1/items |
| Caso de teste | TC-IC-001 |
| Configuração | Máscara do código por empresa (`materials.item.code-mask`). |
| Observações | O código é a identidade pública do item em toda a cadeia (MMS-002-01, seção 5). |

---

## IC-BR-002 — Descrição Oficial Obrigatória

| Campo | Valor |
|-------|-------|
| Código | IC-BR-002 |
| Nome | Descrição Oficial Obrigatória |
| Descrição | Todo item possui uma descrição oficial única em seu cadastro, usada em todos os módulos; sinônimos nunca a substituem. |
| Tipo | Obrigatória |
| Validação | Campo obrigatório, tamanho mínimo/máximo parametrizável; normalização de espaços e caixa para comparação. |
| Mensagem de erro | "Informe a descrição oficial do item." |
| Código do erro | IC-ERR-002 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | POST /api/v1/items, PATCH /api/v1/items/{id} |
| Caso de teste | TC-IC-002 |
| Configuração | Tamanhos mínimo/máximo (`materials.item.description.*`). |
| Observações | Descrição oficial em pt-BR; preparado para descrições localizadas (roadmap v2.0). |

---

## IC-BR-003 — Unidade de Medida Vigente

| Campo | Valor |
|-------|-------|
| Código | IC-BR-003 |
| Nome | Unidade de Medida Vigente |
| Descrição | Toda unidade de medida de item é referência lógica ao Master Data (FD-001-09) por `typeCode+code`, vigente na data de referência. |
| Tipo | Obrigatória |
| Validação | Existência e vigência no FD-001-09; nunca rótulo livre; inativação posterior do vocabulário não altera itens existentes retroativamente. |
| Mensagem de erro | "Unidade de medida inválida ou fora de vigência." |
| Código do erro | IC-ERR-003 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | POST /api/v1/items, PATCH /api/v1/items/{id} |
| Caso de teste | TC-IC-003 |
| Configuração | Não configurável (fronteira formal com FD-001-09). |
| Observações | Elimina unidade livre ("CX", "cx", "caixa") — MMS-002-01, seção 3. |

---

## IC-BR-004 — Categoria Vigente

| Campo | Valor |
|-------|-------|
| Código | IC-BR-004 |
| Nome | Categoria Vigente |
| Descrição | Toda categoria de item é referência lógica ao Master Data (FD-001-09) por `typeCode+code`, vigente na data de referência. |
| Tipo | Obrigatória |
| Validação | Existência e vigência no FD-001-09; hierarquia de categoria conforme Master Data. |
| Mensagem de erro | "Categoria inválida ou fora de vigência." |
| Código do erro | IC-ERR-004 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | POST /api/v1/items, PATCH /api/v1/items/{id} |
| Caso de teste | TC-IC-004 |
| Configuração | Não configurável (fronteira formal com FD-001-09). |
| Observações | Categoria alimenta KPI "itens ativos por categoria". |

---

## IC-BR-005 — Classificação Obrigatória

| Campo | Valor |
|-------|-------|
| Código | IC-BR-005 |
| Nome | Classificação Obrigatória |
| Descrição | Todo item é classificado como **estocável**, **não estocável** (compra/consumo direto) ou **sob encomenda**; a classificação orienta roteamento e obrigatoriedades. |
| Tipo | Obrigatória |
| Validação | Valor dentro do conjunto fechado {estocável, não estocável, sob encomenda}. |
| Mensagem de erro | "Informe a classificação do item." |
| Código do erro | IC-ERR-005 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | POST /api/v1/items, PATCH /api/v1/items/{id} |
| Caso de teste | TC-IC-005 |
| Configuração | Não configurável (conjunto fechado definido pela suíte). |
| Observações | Itens não estocáveis não exigem parâmetros de reposição (IC-BR-011). |

---

## IC-BR-006 — Estado Inicial Rascunho

| Campo | Valor |
|-------|-------|
| Código | IC-BR-006 |
| Nome | Estado Inicial Rascunho |
| Descrição | Todo novo item nasce em estado **Rascunho** e só passa a ser utilizável pelos demais módulos após a ativação. |
| Tipo | Obrigatória |
| Validação | Estado inicial fixo na factory do Aggregate. |
| Mensagem de erro | — (não aplicável; automático) |
| Código do erro | — |
| Evento | Item cadastrado (rascunho) |
| Caso de uso | UC-IC-001 |
| API | POST /api/v1/items |
| Caso de teste | TC-IC-006 |
| Configuração | Não configurável. |
| Observações | Conforme ciclo de vida da visão do módulo (Rascunho → Ativo → Inativo). |

---

## IC-BR-007 — Empresa Obrigatória

| Campo | Valor |
|-------|-------|
| Código | IC-BR-007 |
| Nome | Empresa Obrigatória |
| Descrição | Todo item pertence a exatamente uma empresa; o catálogo é isolado por empresa em toda consulta, regra e evento. |
| Tipo | Obrigatória |
| Validação | Empresa existente e ativa no Organization (FD-001-02); usuário autorizado para a empresa. |
| Mensagem de erro | "Empresa inválida, inativa ou não autorizada." |
| Código do erro | IC-ERR-007 |
| Evento | Item cadastrado (rascunho) |
| Caso de uso | UC-IC-001 |
| API | POST /api/v1/items |
| Caso de teste | TC-IC-007 |
| Configuração | Não configurável. |
| Observações | Multiplicidade desde o desenho (MMS-001, seção 6.2). |

---

# 5. Regras de Parâmetros de Reposição

## IC-BR-010 — Conjunto de Parâmetros

| Campo | Valor |
|-------|-------|
| Código | IC-BR-010 |
| Nome | Parâmetros de Reposição |
| Descrição | O item pode registrar estoque mínimo, estoque máximo, ponto de pedido e lead time de referência — base dos alertas do MMS-004. |
| Tipo | Obrigatória (existência do recurso) |
| Validação | Valores numéricos não negativos; lead time em dias. |
| Mensagem de erro | "Parâmetro de reposição inválido." |
| Código do erro | IC-ERR-010 |
| Evento | Parâmetros de reposição alterados |
| Caso de uso | UC-IC-007 |
| API | PUT /api/v1/items/{id}/replenishment |
| Caso de teste | TC-IC-010 |
| Configuração | Não configurável (conjunto de campos definido pela suíte). |
| Observações | O catálogo guarda parâmetros, nunca saldos (fronteira com MMS-004). |

---

## IC-BR-011 — Obrigatoriedade para Itens Estocáveis

| Campo | Valor |
|-------|-------|
| Código | IC-BR-011 |
| Nome | Parâmetros Obrigatórios para Estocáveis |
| Descrição | Para itens **estocáveis**, o preenchimento dos parâmetros de reposição é esperado; a obrigatoriedade (bloqueio na ativação) é parametrizável. |
| Tipo | Parametrizável |
| Validação | Quando `materials.item.replenishment.required=true`, a ativação de item estocável sem parâmetros é recusada; quando `false`, a ausência alimenta o indicador de saneamento. |
| Mensagem de erro | "Informe os parâmetros de reposição para ativar este item estocável." |
| Código do erro | IC-ERR-011 |
| Evento | Item ativado / Parâmetros de reposição alterados |
| Caso de uso | UC-IC-003, UC-IC-007 |
| API | POST /api/v1/items/{id}/activate |
| Caso de teste | TC-IC-011 |
| Configuração | `materials.item.replenishment.required` (padrão: `false` — saneamento por KPI). |
| Observações | KPI "itens sem parâmetro de reposição" (MMS-002-01, seção 19). |

---

## IC-BR-012 — Consistência dos Parâmetros

| Campo | Valor |
|-------|-------|
| Código | IC-BR-012 |
| Nome | Consistência dos Parâmetros |
| Descrição | Quando informados, os parâmetros devem ser consistentes: estoque mínimo não superior ao estoque máximo e ponto de pedido dentro da faixa mínimo–máximo. |
| Tipo | Obrigatória |
| Validação | `mínimo ≤ máximo`; `mínimo ≤ ponto de pedido ≤ máximo` (quando os três informados). |
| Mensagem de erro | "Parâmetros de reposição inconsistentes: verifique mínimo, máximo e ponto de pedido." |
| Código do erro | IC-ERR-012 |
| Evento | Parâmetros de reposição alterados |
| Caso de uso | UC-IC-007 |
| API | PUT /api/v1/items/{id}/replenishment |
| Caso de teste | TC-IC-012 |
| Configuração | Não configurável. |
| Observações | Regra de integridade dos campos documentados na visão do módulo. |

---

## IC-BR-013 — Criticidade Parametrizável

| Campo | Valor |
|-------|-------|
| Código | IC-BR-013 |
| Nome | Criticidade Parametrizável |
| Descrição | A criticidade do item (baixa/média/alta, conjunto parametrizável) orienta workflow de aprovação e priorização na suíte. |
| Tipo | Parametrizável |
| Validação | Valor dentro do conjunto configurado na empresa. |
| Mensagem de erro | "Criticidade inválida." |
| Código do erro | IC-ERR-013 |
| Evento | Item alterado |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | POST /api/v1/items, PATCH /api/v1/items/{id} |
| Caso de teste | TC-IC-013 |
| Configuração | Conjunto de níveis (`materials.item.criticality.levels`; padrão: baixa/média/alta). |
| Observações | Usada pelo MMS-003 no roteamento do workflow de aprovação. |

---

# 6. Regras do Ciclo de Vida

## IC-BR-020 — Ativação de Item

| Campo | Valor |
|-------|-------|
| Código | IC-BR-020 |
| Nome | Ativação de Item |
| Descrição | Item em Rascunho, válido em todas as regras, pode ser ativado pelo papel mantenedor; a ativação não exige workflow no MVP, mas a exigência é parametrizável. |
| Tipo | Parametrizável |
| Validação | Estado = Rascunho; regras IC-BR-001..005 e IC-BR-011 (se ativa) satisfeitas; quando `materials.item.activation.approval-required=true`, a ativação ocorre via workflow (FD-001-04). |
| Mensagem de erro | "O item não está apto à ativação." |
| Código do erro | IC-ERR-020 |
| Evento | Item ativado |
| Caso de uso | UC-IC-003 |
| API | POST /api/v1/items/{id}/activate |
| Caso de teste | TC-IC-020 |
| Configuração | `materials.item.activation.approval-required` (padrão: `false`). |
| Observações | Toda ativação é auditada (IC-BR-050). |

---

## IC-BR-021 — Somente Ativo é Utilizável

| Campo | Valor |
|-------|-------|
| Código | IC-BR-021 |
| Nome | Somente Ativo é Utilizável |
| Descrição | Somente itens **Ativos** entram em novas solicitações, movimentações, recebimentos e compras (MMS-RG-08). |
| Tipo | Obrigatória |
| Validação | Verificação do estado do item na referência por qualquer módulo consumidor. |
| Mensagem de erro | "Item inativo ou inexistente no catálogo." |
| Código do erro | IC-ERR-021 |
| Evento | — (regra de consumo) |
| Caso de uso | UC-IC-006 |
| API | GET /api/v1/items (somente Ativos na busca operacional) |
| Caso de teste | TC-IC-021 |
| Configuração | Não configurável. |
| Observações | A busca operacional exibe apenas Ativos; Rascunho/Inativo visíveis apenas ao mantenedor e à auditoria. |

---

## IC-BR-022 — Inativação Lógica com Motivo

| Campo | Valor |
|-------|-------|
| Código | IC-BR-022 |
| Nome | Inativação Lógica com Motivo |
| Descrição | A inativação é sempre lógica, exige motivo registrado e não impede a movimentação do saldo remanescente até zerar (MMS-RG-08). |
| Tipo | Obrigatória |
| Validação | Estado = Ativo; motivo obrigatório; nenhuma exclusão física. |
| Mensagem de erro | "Informe o motivo da inativação." |
| Código do erro | IC-ERR-022 |
| Evento | Item inativado |
| Caso de uso | UC-IC-004 |
| API | POST /api/v1/items/{id}/inactivate |
| Caso de teste | TC-IC-022 |
| Configuração | Não configurável. |
| Observações | Item inativo sai das novas solicitações imediatamente; saldo segue movimentável. |

---

## IC-BR-023 — Reativação Auditada

| Campo | Valor |
|-------|-------|
| Código | IC-BR-023 |
| Nome | Reativação Auditada |
| Descrição | Item inativo pode ser reativado pelo mantenedor, com motivo obrigatório e auditoria completa (tratativa de inativação por engano — MMS-002-01, gatilhos emergenciais). |
| Tipo | Obrigatória |
| Validação | Estado = Inativo; motivo obrigatório; item deve satisfazer as regras vigentes de ativação. |
| Mensagem de erro | "Informe o motivo da reativação." |
| Código do erro | IC-ERR-023 |
| Evento | Item ativado |
| Caso de uso | UC-IC-004 |
| API | POST /api/v1/items/{id}/activate |
| Caso de teste | TC-IC-023 |
| Configuração | Não configurável. |
| Observações | Reativação segue as mesmas validações da ativação (IC-BR-020). |

---

## IC-BR-024 — Descarte de Rascunho

| Campo | Valor |
|-------|-------|
| Código | IC-BR-024 |
| Nome | Descarte de Rascunho |
| Descrição | Item em Rascunho nunca referenciado pode ser descartado (exclusão lógica) pelo mantenedor, com auditoria; após a primeira referência de outro módulo, nenhum item é excluído — apenas inativado. |
| Tipo | Obrigatória |
| Validação | Estado = Rascunho; zero referências externas; soft delete. |
| Mensagem de erro | "O item não pode ser descartado: já possui referências ou está ativo." |
| Código do erro | IC-ERR-024 |
| Evento | Item inativado (descarte) |
| Caso de uso | UC-IC-002 |
| API | DELETE /api/v1/items/{id} |
| Caso de teste | TC-IC-024 |
| Configuração | Não configurável. |
| Observações | Restrição arquitetural 4 da visão do módulo. |

---

# 7. Regras de Sinônimos e Prevenção de Duplicidade

## IC-BR-030 — Sinônimos como Auxílio de Busca

| Campo | Valor |
|-------|-------|
| Código | IC-BR-030 |
| Nome | Sinônimos como Auxílio de Busca |
| Descrição | Sinônimos são nomes alternativos usados pela operação para encontrar o item; nunca substituem a descrição oficial nem criam identidade nova. |
| Tipo | Obrigatória |
| Validação | Lista de termos por item; normalização para busca; sem duplicidade de sinônimo dentro do mesmo item. |
| Mensagem de erro | "Sinônimo inválido ou duplicado para este item." |
| Código do erro | IC-ERR-030 |
| Evento | Sinônimo incluído/removido |
| Caso de uso | UC-IC-005 |
| API | PUT /api/v1/items/{id}/synonyms |
| Caso de teste | TC-IC-030 |
| Configuração | Quantidade máxima de sinônimos por item (`materials.item.synonyms.max`). |
| Observações | Um mesmo sinônimo pode existir em itens diferentes (a busca mostra todos os candidatos). |

---

## IC-BR-031 — Alerta de Descrição Semelhante

| Campo | Valor |
|-------|-------|
| Código | IC-BR-031 |
| Nome | Alerta de Descrição Semelhante |
| Descrição | No cadastro ou edição, o sistema alerta o mantenedor quando já existe item com descrição ou sinônimo semelhante na empresa, para prevenção de duplicidade. |
| Tipo | Obrigatória (o alerta); Informativa (a decisão) |
| Validação | Similaridade calculada sobre descrições e sinônimos normalizados; o alerta não bloqueia — a confirmação é registrada. |
| Mensagem de erro | — (alerta, não erro) |
| Código do erro | — |
| Evento | — (alerta em tela; confirmação auditada) |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | GET /api/v1/items/similar?description= |
| Caso de teste | TC-IC-031 |
| Configuração | Limiar de similaridade (`materials.item.similarity.threshold`). |
| Observações | KPI "taxa de duplicidade detectada" mede alertas confirmados (MMS-002-01, seção 19). |

---

# 8. Regras de Alteração

## IC-BR-040 — Motivo em Alterações Sensíveis

| Campo | Valor |
|-------|-------|
| Código | IC-BR-040 |
| Nome | Motivo em Alterações Sensíveis |
| Descrição | Inativação, reativação e mudança de unidade de medida exigem motivo registrado, que consta na auditoria e na timeline. |
| Tipo | Obrigatória |
| Validação | Motivo não vazio nas operações sensíveis. |
| Mensagem de erro | "Informe o motivo da alteração." |
| Código do erro | IC-ERR-040 |
| Evento | Item alterado / Item inativado / Item ativado |
| Caso de uso | UC-IC-002, UC-IC-004 |
| API | PATCH /api/v1/items/{id}, POST /api/v1/items/{id}/activate, /inactivate |
| Caso de teste | TC-IC-040 |
| Configuração | Lista de operações que exigem motivo (`materials.item.reason-required.operations`; padrão: inativação, reativação, mudança de unidade). |
| Observações | Definido nas entradas do processo (MMS-002-01, seção 16). |

---

## IC-BR-041 — Edição por Estado

| Campo | Valor |
|-------|-------|
| Código | IC-BR-041 |
| Nome | Edição por Estado |
| Descrição | Em Rascunho todos os campos são editáveis; em Ativo, a edição de código é proibida e a de unidade de medida exige motivo; em Inativo, somente a reativação é permitida. |
| Tipo | Obrigatória |
| Validação | Matriz de editabilidade por estado aplicada em toda escrita. |
| Mensagem de erro | "Campo não editável no estado atual do item." |
| Código do erro | IC-ERR-041 |
| Evento | Item alterado |
| Caso de uso | UC-IC-002 |
| API | PATCH /api/v1/items/{id} |
| Caso de teste | TC-IC-041 |
| Configuração | Não configurável. |
| Observações | Código imutável após ativação (IC-BR-001). |

---

## IC-BR-042 — Concorrência Otimista

| Campo | Valor |
|-------|-------|
| Código | IC-BR-042 |
| Nome | Concorrência Otimista |
| Descrição | Toda escrita em item exige a versão corrente (`version`/`If-Match`); conflito de versão recusa a operação sem efeito colateral. |
| Tipo | Obrigatória |
| Validação | Versão enviada = versão persistida. |
| Mensagem de erro | "O item foi alterado por outro usuário. Recarregue e tente novamente." |
| Código do erro | IC-ERR-042 |
| Evento | — |
| Caso de uso | Todos os de escrita |
| API | Todos os endpoints de escrita |
| Caso de teste | TC-IC-042 |
| Configuração | Não configurável. |
| Observações | NFR da visão do módulo (optimistic concurrency). |

---

# 9. Regras de Auditoria e Timeline

## IC-BR-050 — Auditoria de 100% das Alterações

| Campo | Valor |
|-------|-------|
| Código | IC-BR-050 |
| Nome | Auditoria de 100% das Alterações |
| Descrição | Toda criação, alteração, ativação, inativação, descarte, mudança de parâmetros e de sinônimos gera registro de auditoria imutável com valores anterior/posterior, autor, data/hora e correlationId (MMS-P-02). |
| Tipo | Obrigatória |
| Validação | Registro no FD-001-06 na mesma transação da operação. |
| Mensagem de erro | — (falha de auditoria aborta a operação) |
| Código do erro | — |
| Evento | Todos os eventos do módulo |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-IC-050 |
| Configuração | Não configurável. |
| Observações | MMS-001, seção 18: sem exceção. |

---

## IC-BR-051 — Timeline do Ciclo de Vida

| Campo | Valor |
|-------|-------|
| Código | IC-BR-051 |
| Nome | Timeline do Ciclo de Vida |
| Descrição | Marcos do item (cadastro, alterações, ativação, inativação, reativação, mudança de parâmetros) aparecem na timeline (FD-001-07), com visibilidade conforme regra do Foundation (MMS-P-03). |
| Tipo | Obrigatória |
| Validação | Entrada de timeline por marco, com paginação keyset. |
| Mensagem de erro | — (não bloqueia o fluxo) |
| Código do erro | — |
| Evento | Todos os eventos do módulo |
| Caso de uso | UC-IC-006 |
| API | GET /api/v1/items/{id}/timeline |
| Caso de teste | TC-IC-051 |
| Configuração | Não configurável. |
| Observações | Falha de timeline nunca bloqueia a operação principal. |

---

# 10. Regras de Segurança

## IC-BR-060 — Permissões por Papel

| Campo | Valor |
|-------|-------|
| Código | IC-BR-060 |
| Nome | Permissões por Papel |
| Descrição | Consulta é ampla aos papéis autorizados; manutenção (criar, editar, ativar, inativar, parametrizar) é restrita ao Gerente de Suprimentos e Admin; deny by default; itens de menu sem permissão são ocultados (MMS-001, seção 23). |
| Tipo | Obrigatória |
| Validação | Fluxo de autorização do Foundation (escopo → RBAC → ABAC), com negação auditada. |
| Mensagem de erro | "Você não tem permissão para esta operação." |
| Código do erro | IC-ERR-060 |
| Evento | — (negação auditada) |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-IC-060 |
| Configuração | Não configurável (matriz detalhada no MMS-002-09). |
| Observações | Segregação: mantenedor não aprova a própria ativação quando o workflow cadastral estiver ativo (IC-BR-020). |

---

## IC-BR-061 — Isolamento Multiempresa

| Campo | Valor |
|-------|-------|
| Código | IC-BR-061 |
| Nome | Isolamento Multiempresa |
| Descrição | Nenhuma consulta, regra, evento ou relatório cruza empresas; o código do item é único por empresa e pode se repetir entre empresas distintas. |
| Tipo | Obrigatória |
| Validação | Filtro obrigatório por `company_id` em toda operação. |
| Mensagem de erro | — (registro fora do escopo responde 404) |
| Código do erro | IC-ERR-404 |
| Evento | — |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-IC-061 |
| Configuração | Não configurável. |
| Observações | Acesso direto a item de outra empresa responde 404 (anti-enumeração, padrão PR-001). |

---

## IC-BR-062 — Escopo Organizacional

| Campo | Valor |
|-------|-------|
| Código | IC-BR-062 |
| Nome | Escopo Organizacional |
| Descrição | A manutenção do catálogo respeita o escopo organizacional do mantenedor (empresa/unidade autorizadas, FD-001-01/02); a consulta operacional segue o escopo do usuário. |
| Tipo | Obrigatória |
| Validação | Escopo do usuário avaliado em toda operação. |
| Mensagem de erro | "Operação fora do seu escopo organizacional." |
| Código do erro | IC-ERR-062 |
| Evento | — (negação auditada) |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-IC-062 |
| Configuração | Não configurável. |
| Observações | MMS-001, seção 23: escopo organizacional obrigatório. |

---

# 11. Regras de Performance e Disponibilidade

## IC-BR-070 — Busca Performática com Keyset

| Campo | Valor |
|-------|-------|
| Código | IC-BR-070 |
| Nome | Busca Performática com Keyset |
| Descrição | A busca do catálogo (código, descrição, sinônimo, categoria) é paginada por keyset e responde em menos de 2 segundos na carga de referência. |
| Tipo | Obrigatória |
| Validação | Índices de busca; paginação keyset; medição de percentil 95. |
| Mensagem de erro | — (NFR) |
| Código do erro | — |
| Evento | — |
| Caso de uso | UC-IC-006 |
| API | GET /api/v1/items |
| Caso de teste | TC-IC-070 |
| Configuração | Tamanho de página padrão (`materials.item.search.page-size`). |
| Observações | NFR da visão do módulo. |

---

## IC-BR-071 — Consulta Nunca Bloqueia a Operação

| Campo | Valor |
|-------|-------|
| Código | IC-BR-071 |
| Nome | Consulta Nunca Bloqueia a Operação |
| Descrição | A consulta ao catálogo usa cache de leitura com invalidação por evento; indisponibilidade de escrita no módulo não interrompe a operação de estoque e recebimento. |
| Tipo | Obrigatória |
| Validação | Cache de leitura para os dados de consulta pública (código, descrição, unidade, classificação, parâmetros). |
| Mensagem de erro | — (NFR) |
| Código do erro | — |
| Evento | Invalidação por eventos do ciclo de vida |
| Caso de uso | UC-IC-006 |
| API | GET /api/v1/items |
| Caso de teste | TC-IC-071 |
| Configuração | TTL de cache (`materials.item.cache.ttl`). |
| Observações | NFR da visão do módulo (disponibilidade de leitura). |

---

# 12. Matriz de Rastreabilidade

| Regra | Origem (documento) | UC | API (conceitual) | Evento | Teste |
|-------|--------------------|-----|------------------|--------|-------|
| IC-BR-001 | MMS-002 README (escopo 1) | UC-IC-001 | POST /items | Item cadastrado | TC-IC-001 |
| IC-BR-002 | MMS-002 README (escopo 1) | UC-IC-001/002 | POST, PATCH /items | Item cadastrado/alterado | TC-IC-002 |
| IC-BR-003 | MMS-002 README (NÃO faz) | UC-IC-001/002 | POST, PATCH /items | Item cadastrado/alterado | TC-IC-003 |
| IC-BR-004 | MMS-002 README (NÃO faz) | UC-IC-001/002 | POST, PATCH /items | Item cadastrado/alterado | TC-IC-004 |
| IC-BR-005 | MMS-002 README (escopo 2) | UC-IC-001/002 | POST, PATCH /items | Item cadastrado/alterado | TC-IC-005 |
| IC-BR-006 | MMS-002 README (escopo 5) | UC-IC-001 | POST /items | Item cadastrado | TC-IC-006 |
| IC-BR-007 | MMS-001 §6.2 | UC-IC-001 | POST /items | Item cadastrado | TC-IC-007 |
| IC-BR-010 | MMS-002 README (escopo 3) | UC-IC-007 | PUT .../replenishment | Parâmetros alterados | TC-IC-010 |
| IC-BR-011 | MMS-002-01 §14/§19 | UC-IC-003/007 | POST .../activate | Item ativado | TC-IC-011 |
| IC-BR-012 | MMS-002 README (escopo 3) | UC-IC-007 | PUT .../replenishment | Parâmetros alterados | TC-IC-012 |
| IC-BR-013 | MMS-002 README (escopo 4) | UC-IC-001/002 | POST, PATCH /items | Item alterado | TC-IC-013 |
| IC-BR-020 | MMS-002 README (fluxo macro) | UC-IC-003 | POST .../activate | Item ativado | TC-IC-020 |
| IC-BR-021 | MMS-RG-08 | UC-IC-006 | GET /items | — | TC-IC-021 |
| IC-BR-022 | MMS-RG-08 | UC-IC-004 | POST .../inactivate | Item inativado | TC-IC-022 |
| IC-BR-023 | MMS-002-01 §13 (emergenciais) | UC-IC-004 | POST .../activate | Item ativado | TC-IC-023 |
| IC-BR-024 | MMS-002 README (restrição 4) | UC-IC-002 | DELETE /items/{id} | Item inativado | TC-IC-024 |
| IC-BR-030 | MMS-002 README (escopo 7) | UC-IC-005 | PUT .../synonyms | Sinônimo incluído/removido | TC-IC-030 |
| IC-BR-031 | MMS-002 README (problemas) | UC-IC-001/002 | GET /items/similar | — | TC-IC-031 |
| IC-BR-040 | MMS-002-01 §16 | UC-IC-002/004 | PATCH, activate, inactivate | Item alterado/inativado | TC-IC-040 |
| IC-BR-041 | MMS-002 README (fluxo macro) | UC-IC-002 | PATCH /items/{id} | Item alterado | TC-IC-041 |
| IC-BR-042 | MMS-002 README (NFR) | Todos | Escrita | — | TC-IC-042 |
| IC-BR-050 | MMS-P-02 / MMS-001 §18 | Todos | Todos | Todos | TC-IC-050 |
| IC-BR-051 | MMS-P-03 | UC-IC-006 | GET .../timeline | Todos | TC-IC-051 |
| IC-BR-060 | MMS-001 §23 | Todos | Todos | — | TC-IC-060 |
| IC-BR-061 | MMS-001 §6.2 | Todos | Todos | — | TC-IC-061 |
| IC-BR-062 | MMS-001 §23 | Todos | Todos | — | TC-IC-062 |
| IC-BR-070 | MMS-002 README (NFR) | UC-IC-006 | GET /items | — | TC-IC-070 |
| IC-BR-071 | MMS-002 README (NFR) | UC-IC-006 | GET /items | — | TC-IC-071 |

**Cobertura:** 28/28 regras com origem documentada e teste associado (100%).

---

# 13. Dependências

| Dependência | Uso |
|-------------|-----|
| MMS-002 (Visão) / MMS-002-01 (Business Context) | Origem de todas as regras |
| MMS-001 (Documento Mestre) | Regras MMS-RG-08, MMS-RG-11 e princípios MMS-P-01..08 herdados |
| FD-001-01 / FD-001-02 | Autorização, escopo e estrutura organizacional |
| FD-001-04 | Workflow cadastral quando parametrizado (IC-BR-020) |
| FD-001-06 / FD-001-07 | Auditoria e timeline (IC-BR-050/051) |
| FD-001-09 | Unidades de medida e categorias (IC-BR-003/004) |
| FD-001-10 | Todos os parâmetros `materials.item.*` |
| MMS-002-07 / MMS-002-13 / MMS-002-17 | Especificação de UCs, API e testes (documentos seguintes do pacote) |

---

# 14. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação das Business Rules do Item Catalog: 28 regras codificadas (IC-BR-001..071) nas famílias gerais, parâmetros de reposição, ciclo de vida, sinônimos/duplicidade, alteração, auditoria/timeline, segurança e performance, com validação, erros (IC-ERR), eventos, UCs/API/testes conceituais, configurações `materials.item.*` e matriz de rastreabilidade 100% — derivadas da visão do módulo e das regras da suíte (MMS-RG), no padrão PR-001-02 |
