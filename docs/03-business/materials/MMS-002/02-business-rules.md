**Documento:** MMS-002-02 — Business Rules
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.2.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-002 (Visão do Módulo — v1.2.0), MMS-002-01 (Business Context), MMS-001 (Documento Mestre Funcional — seções 6, 8.1, 14, 23), ADR-013 (Conversão de UoM), ADR-014 (Motor de Regras de Reposição), FD-001-03 (Document Management), FD-001-09 (Master Data), FD-001-10 (Configuration)
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

# 12. Regras de EPI e Fardamento

Família introduzida na revisão 1.1.0 (MMS-002 README v1.1.0, escopo 8): atributos do cadastro que viabilizam o caso de uso de EPI (Equipamento de Proteção Individual) e Fardamento. O grupo EPI/Fardamento **não é um campo novo** — é representado pela categoria do item no Master Data (FD-001-09).

## IC-BR-080 — CA Obrigatório para Itens do Grupo EPI

| Campo | Valor |
|-------|-------|
| Código | IC-BR-080 |
| Nome | CA Obrigatório para Itens do Grupo EPI |
| Descrição | Todo item cuja categoria pertença ao grupo EPI deve informar o CA (Certificado de Aprovação). Fardamento não possui CA. |
| Tipo | Parametrizável |
| Validação | Quando a categoria do item constar na lista de categorias EPI configurada, o campo CA é obrigatório e não vazio; formato alfanumérico conforme máscara configurada. |
| Mensagem de erro | "Informe o CA (Certificado de Aprovação) do EPI." |
| Código do erro | IC-ERR-080 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001/002 |
| API | POST, PATCH /api/v1/items |
| Caso de teste | TC-IC-080 |
| Configuração | Lista de categorias que exigem CA (`materials.item.epi.categories`; padrão: vazia — nenhuma exigência até configurada); máscara do CA (`materials.item.epi.ca-mask`). |
| Observações | Origem: MMS-002 README v1.1.0 (escopo 8). A vigência/validade temporal do CA é funcionalidade futura (roadmap). |

---

## IC-BR-081 — Grade de Tamanhos por Item

| Campo | Valor |
|-------|-------|
| Código | IC-BR-081 |
| Nome | Grade de Tamanhos por Item |
| Descrição | O item pode referenciar uma grade de tamanhos (vocabulário do Master Data — ex.: P/M/G/GG, numérica). Item com grade exige seleção de tamanho na solicitação (MMS-003), e o tamanho acompanha reserva, separação e entrega (MMS-004). Tamanho é atributo de referência — nunca gera item separado no catálogo. |
| Tipo | Obrigatória |
| Validação | Quando informada, a grade deve existir e estar vigente no Master Data, referenciada por `typeCode+code` (FD-001-09, vigência na data de referência). |
| Mensagem de erro | "Grade de tamanhos inválida ou inativa." |
| Código do erro | IC-ERR-081 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001/002 |
| API | POST, PATCH /api/v1/items |
| Caso de teste | TC-IC-081 |
| Configuração | `typeCode` das grades de tamanho (`materials.item.size-grid.type-code`; padrão: `SIZE_GRID`). |
| Observações | Origem: MMS-002 README v1.1.0 (escopo 8). O saldo por tamanho é responsabilidade do MMS-004 (documentado no pacote daquele módulo). |

---

## IC-BR-082 — Imagem do Produto

| Campo | Valor |
|-------|-------|
| Código | IC-BR-082 |
| Nome | Imagem do Produto |
| Descrição | O item pode ter uma imagem (foto do produto), armazenada via Document Management (FD-001-03); por padrão é opcional, podendo ser exigida para categorias configuradas (ex.: EPI e Fardamento). |
| Tipo | Parametrizável |
| Validação | Arquivo conforme as restrições do FD-001-03 (tipo e tamanho permitidos); no máximo uma imagem principal por item; substituição gera nova versão auditada. |
| Mensagem de erro | "Imagem inválida, ausente ou acima do tamanho permitido." |
| Código do erro | IC-ERR-082 |
| Evento | Item alterado |
| Caso de uso | UC-IC-001/002 |
| API | PUT /api/v1/items/{id}/image |
| Caso de teste | TC-IC-082 |
| Configuração | Categorias que exigem imagem (`materials.item.image.required-categories`; padrão: vazia — opcional). |
| Observações | Origem: MMS-002 README v1.1.0 (escopo 8 — imagem antecipada do roadmap 2.0 para o MVP). |

---

## IC-BR-083 — Código Externo do ERP

| Campo | Valor |
|-------|-------|
| Código | IC-BR-083 |
| Nome | Código Externo do ERP |
| Descrição | Quando houver integração com ERP, o item registra o código externo do ERP como atributo de integração, único por empresa quando informado. O código oficial do item segue IC-BR-001; quando parametrizado, o código oficial é o próprio código do ERP. |
| Tipo | Parametrizável |
| Validação | Unicidade por (empresa, código externo) quando preenchido; imutável após a ativação do item. |
| Mensagem de erro | "Já existe um item com este código de ERP nesta empresa." |
| Código do erro | IC-ERR-083 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001/002 |
| API | POST, PATCH /api/v1/items |
| Caso de teste | TC-IC-083 |
| Configuração | Obrigatoriedade do código ERP (`materials.item.erp-code.required`; padrão: false); uso do código ERP como código oficial (`materials.item.erp-code.as-official`; padrão: false). |
| Observações | Origem: MMS-002 README v1.1.0 (escopo 1 — código baseado no ERP). A identidade interna do item é sempre gerada pelo sistema e nunca depende do ERP. |

---

# 13. Regras de Conversão de Unidade de Medida

Família introduzida na revisão 1.2.0 (ADR-013 — Conversão de Unidade de Medida). Trata o caso operacional de **comprar em uma unidade e estocar/consumir em outra** (ex.: comprar em caixa, controlar em unidade). O princípio da fronteira com o Master Data permanece: a UoM continua sendo **vocabulário do FD-001-09**; esta família adiciona a **relação de conversão** entre unidades, não um vocabulário novo.

## IC-BR-090 — Unidade de Estoque (base) Obrigatória

| Campo | Valor |
|-------|-------|
| Código | IC-BR-090 |
| Nome | Unidade de Estoque (base) Obrigatória |
| Descrição | Todo item define uma **Unidade de Estoque (base)** — a UoM na qual o saldo é mantido (MMS-004) e na qual toda quantidade do domínio de materiais é persistida. Substitui, sem ambiguidade, a "unidade de medida" única do item. |
| Tipo | Obrigatória |
| Validação | UoM base existente e vigente no FD-001-09 (IC-BR-003); exatamente uma por item; imutável após a primeira movimentação do item no MMS-004. |
| Mensagem de erro | "Informe a unidade de estoque (base) do item." |
| Código do erro | IC-ERR-112 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | POST, PATCH /api/v1/items |
| Caso de teste | TC-IC-090 |
| Configuração | Não configurável (fronteira formal com FD-001-09). |
| Observações | O saldo (MMS-004) existe exclusivamente na base; não há saldo em unidades alternativas (ADR-013, decisão 1). |

---

## IC-BR-091 — Unidades Alternativas com Fator de Conversão

| Campo | Valor |
|-------|-------|
| Código | IC-BR-091 |
| Nome | Unidades Alternativas com Fator de Conversão |
| Descrição | O item pode registrar zero ou mais **unidades alternativas** com **fator de conversão** para a base (ex.: `1 CX = 12 UN`), cada uma com um **papel** opcional (ex.: unidade de compra padrão, unidade de consumo). |
| Tipo | Parametrizável (existência do recurso; obrigatoriedade da unidade de compra é configurável) |
| Validação | Fator numérico > 0; unicidade da UoM alternativa por item; unicidade de papel por item (no máximo uma unidade de compra padrão); a UoM alternativa é vigente no FD-001-09. |
| Mensagem de erro | "Unidade alternativa ou fator de conversão inválido." |
| Código do erro | IC-ERR-110 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | PUT /api/v1/items/{id}/uom-conversions |
| Caso de teste | TC-IC-091 |
| Configuração | Exigir unidade de compra para itens compráveis (`materials.item.uom.purchase-unit-required`; padrão: `false`). |
| Observações | Papéis alimentam Receiving (MMS-005), movimentação (MMS-004) e a rota de compra (PR-001) — ADR-013, decisão 2/4. |

---

## IC-BR-092 — Conversão na Mesma Categoria de UoM

| Campo | Valor |
|-------|-------|
| Código | IC-BR-092 |
| Nome | Conversão na Mesma Categoria de UoM |
| Descrição | A conversão só é válida entre unidades da **mesma categoria de UoM** do Master Data (ex.: contagem, massa, comprimento); não se converte entre categorias incompatíveis. |
| Tipo | Obrigatória |
| Validação | UoM base e UoM alternativa pertencem à mesma categoria no FD-001-09; caso contrário, a conversão é recusada. |
| Mensagem de erro | "Conversão inválida: as unidades pertencem a categorias diferentes." |
| Código do erro | IC-ERR-111 |
| Evento | Item cadastrado (rascunho) / Item alterado |
| Caso de uso | UC-IC-001, UC-IC-002 |
| API | PUT /api/v1/items/{id}/uom-conversions |
| Caso de teste | TC-IC-092 |
| Configuração | Não configurável (fronteira formal com FD-001-09). |
| Observações | A vigência da UoM é avaliada na data de referência, como em IC-BR-003 (ADR-013, decisão 3). |

---

## IC-BR-093 — Fator Imutável para Movimentos e Arredondamento Parametrizável

| Campo | Valor |
|-------|-------|
| Código | IC-BR-093 |
| Nome | Fator Imutável para Movimentos e Arredondamento Parametrizável |
| Descrição | Alterar o fator de conversão de um item **não reprocessa** saldos nem documentos históricos; cada movimento guarda o fator aplicado no momento. A precisão e a regra de arredondamento da conversão são parametrizáveis e determinísticas. |
| Tipo | Obrigatória (imutabilidade); Parametrizável (arredondamento) |
| Validação | Movimentos do MMS-004 registram quantidade base + (quantidade informada, UoM informada, fator aplicado); arredondamento conforme regra configurada, sem comportamento implícito. |
| Mensagem de erro | — (regra de integridade; sem entrada direta de usuário) |
| Código do erro | IC-ERR-112 |
| Evento | Item alterado (fator) |
| Caso de uso | UC-IC-002 |
| API | PUT /api/v1/items/{id}/uom-conversions |
| Caso de teste | TC-IC-093 |
| Configuração | Precisão e arredondamento (`materials.item.uom.rounding`; `materials.item.uom.precision`). |
| Observações | Mesma disciplina de vigência do FD-001-09 e de imutabilidade de movimento do MMS-004 (ADR-013, decisões 5/6). |

---

# 14. Regras de Reposição — Parâmetros da Regra

Família introduzida na revisão 1.2.0 (ADR-014 — Motor de Regras de Reposição, Accepted). O Item Catalog guarda os **parâmetros da regra** por item (e, opcionalmente, por depósito); a **avaliação** da regra e a fila de sugestões pertencem ao MMS-004 (v1.1) e a execução automática à v2.0. O catálogo nunca compra nem cria saldo (fronteira MMS-001 §3).

## IC-BR-100 — Política de Quantidade Sugerida de Reposição

| Campo | Valor |
|-------|-------|
| Código | IC-BR-100 |
| Nome | Política de Quantidade Sugerida de Reposição |
| Descrição | O item define a política que determina **quanto** repor quando o gatilho de reposição (ponto de pedido/mínimo) é atingido: **repor até o máximo**, **múltiplo de embalagem** (fator de UoM, IC-BR-091) ou **lote econômico fixo**. |
| Tipo | Parametrizável |
| Validação | Valor dentro do conjunto fechado {ate_maximo, multiplo_embalagem, lote_fixo}; `lote_fixo` exige quantidade > 0; `multiplo_embalagem` exige unidade de compra com fator (IC-BR-091). |
| Mensagem de erro | "Política de reposição inválida ou incompleta." |
| Código do erro | IC-ERR-120 |
| Evento | Parâmetros de reposição alterados |
| Caso de uso | UC-IC-007 |
| API | PUT /api/v1/items/{id}/replenishment |
| Caso de teste | TC-IC-100 |
| Configuração | Política padrão da empresa (`materials.replenishment.default-policy`; padrão: `ate_maximo`). |
| Observações | A quantidade é calculada pelo MMS-004 sobre o saldo disponível; o catálogo apenas parametriza (ADR-014, decisão 1; Resolução do aceite). |

---

## IC-BR-101 — Rota de Suprimento por Item

| Campo | Valor |
|-------|-------|
| Código | IC-BR-101 |
| Nome | Rota de Suprimento por Item |
| Descrição | O item define a **rota de reposição** preferencial: **transferência** de outro depósito, **compra** (demanda para PR-001) ou **manual** (sem sugestão automática). A regra nunca executa compra/movimentação por conta própria — apenas orienta a sugestão do MMS-004. |
| Tipo | Parametrizável |
| Validação | Valor dentro do conjunto fechado {transferencia, compra, manual}; item `sob encomenda`/`não estocável` assume `manual`/`compra` conforme classificação (IC-BR-005). |
| Mensagem de erro | "Rota de suprimento inválida para a classificação do item." |
| Código do erro | IC-ERR-121 |
| Evento | Parâmetros de reposição alterados |
| Caso de uso | UC-IC-007 |
| API | PUT /api/v1/items/{id}/replenishment |
| Caso de teste | TC-IC-101 |
| Configuração | Rota padrão (`materials.replenishment.default-route`; padrão: `compra`). |
| Observações | Reposição por compra passa sempre por PR-001; por transferência gera documento no MMS-004 (MMS-P-07). Rastreabilidade bidirecional preservada (ADR-014, decisões 3/5). |

---

# 15. Matriz de Rastreabilidade

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
| IC-BR-080 | MMS-002 README v1.1.0 (escopo 8) | UC-IC-001/002 | POST, PATCH /items | Item cadastrado/alterado | TC-IC-080 |
| IC-BR-081 | MMS-002 README v1.1.0 (escopo 8) | UC-IC-001/002 | POST, PATCH /items | Item cadastrado/alterado | TC-IC-081 |
| IC-BR-082 | MMS-002 README v1.1.0 (escopo 8) | UC-IC-001/002 | PUT .../image | Item alterado | TC-IC-082 |
| IC-BR-083 | MMS-002 README v1.1.0 (escopo 1) | UC-IC-001/002 | POST, PATCH /items | Item cadastrado/alterado | TC-IC-083 |
| IC-BR-090 | ADR-013 (decisão 1) | UC-IC-001/002 | POST, PATCH /items | Item cadastrado/alterado | TC-IC-090 |
| IC-BR-091 | ADR-013 (decisão 2) | UC-IC-001/002 | PUT .../uom-conversions | Item cadastrado/alterado | TC-IC-091 |
| IC-BR-092 | ADR-013 (decisão 3) | UC-IC-001/002 | PUT .../uom-conversions | Item cadastrado/alterado | TC-IC-092 |
| IC-BR-093 | ADR-013 (decisões 5/6) | UC-IC-002 | PUT .../uom-conversions | Item alterado (fator) | TC-IC-093 |
| IC-BR-100 | ADR-014 (decisão 1) | UC-IC-007 | PUT .../replenishment | Parâmetros alterados | TC-IC-100 |
| IC-BR-101 | ADR-014 (decisões 3/5) | UC-IC-007 | PUT .../replenishment | Parâmetros alterados | TC-IC-101 |

**Cobertura:** 38/38 regras com origem documentada e teste associado (100%).

---

# 16. Dependências

| Dependência | Uso |
|-------------|-----|
| MMS-002 (Visão) / MMS-002-01 (Business Context) | Origem de todas as regras |
| ADR-013 — Conversão de UoM | Origem da família IC-BR-090..093 |
| ADR-014 — Motor de Regras de Reposição | Origem da família IC-BR-100..101 (parâmetros); avaliação no MMS-004 |
| MMS-004 — Inventory Management | Saldo na UoM base; avaliação da regra de reposição e fila de sugestões |
| MMS-001 (Documento Mestre) | Regras MMS-RG-08, MMS-RG-11 e princípios MMS-P-01..08 herdados |
| FD-001-01 / FD-001-02 | Autorização, escopo e estrutura organizacional |
| FD-001-03 | Imagem do produto (IC-BR-082) |
| FD-001-04 | Workflow cadastral quando parametrizado (IC-BR-020) |
| FD-001-06 / FD-001-07 | Auditoria e timeline (IC-BR-050/051) |
| FD-001-09 | Unidades de medida, categorias (IC-BR-003/004), categorias EPI (IC-BR-080) e grades de tamanho (IC-BR-081) |
| FD-001-10 | Todos os parâmetros `materials.item.*` |
| MMS-002-07 / MMS-002-13 / MMS-002-17 | Especificação de UCs, API e testes (documentos seguintes do pacote) |

---

# 17. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação das Business Rules do Item Catalog: 28 regras codificadas (IC-BR-001..071) nas famílias gerais, parâmetros de reposição, ciclo de vida, sinônimos/duplicidade, alteração, auditoria/timeline, segurança e performance, com validação, erros (IC-ERR), eventos, UCs/API/testes conceituais, configurações `materials.item.*` e matriz de rastreabilidade 100% — derivadas da visão do módulo e das regras da suíte (MMS-RG), no padrão PR-001-02 |
| 1.1.0 | 2026-07-30 | Incorporação do caso EPI/Fardamento (MMS-002 README v1.1.0): nova família "Regras de EPI e Fardamento" com 4 regras (IC-BR-080 CA obrigatório para grupo EPI, IC-BR-081 grade de tamanhos, IC-BR-082 imagem do produto via FD-001-03, IC-BR-083 código externo do ERP); matriz de rastreabilidade ampliada para 32/32; seções 12–14 renumeradas para 13–15 |
| 1.2.0 | 2026-08-08 | Incorporação de ADR-013 e ADR-014: nova família "Regras de Conversão de Unidade de Medida" (IC-BR-090 unidade de estoque base, IC-BR-091 unidades alternativas com fator, IC-BR-092 conversão na mesma categoria, IC-BR-093 fator imutável para movimentos + arredondamento parametrizável) e nova família "Regras de Reposição — Parâmetros da Regra" (IC-BR-100 política de quantidade sugerida, IC-BR-101 rota de suprimento); matriz ampliada para 38/38; seções Matriz/Dependências/Histórico renumeradas para 15/16/17. Avaliação da regra de reposição e fila de sugestões pertencem ao MMS-004 (v1.1) |
