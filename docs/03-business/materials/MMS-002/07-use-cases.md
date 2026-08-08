# MMS-002-07 — Use Cases

**Documento:** MMS-002-07 — Use Cases
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.1.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-002 v1.2.0, MMS-002-02 v1.2.0 (Business Rules), MMS-002-03 (State Machine), MMS-002-05 (Event Storming), MMS-002-06 (BPMN), ADR-013 (Conversão de UoM), ADR-014 (Motor de Regras de Reposição), FD-001-03, FD-001-09
**Referências:** PR-001-07 (padrão de formato Enterprise), MMS-002-09 Permissions (a produzir), MMS-002-13 API (a produzir), GOV-001

> Especificação dos Casos de Uso do módulo Item Catalog.
> Cada caso de uso segue especificação UML completa: fluxo principal, fluxos alternativos, fluxos de exceção, pré/pós-condições, regras, eventos, mensagens, validações, APIs, permissões e testes relacionados.

---

# 0. Convenções da Especificação

| Campo | Significado |
| ----- | ----------- |
| **APIs** | Endpoints REST sob `/api/v1/items` (contrato detalhado em MMS-002-13). Todas exigem autenticação JWT e propagam `X-Correlation-Id` |
| **Permissões** | Códigos IC-PERM conforme MMS-002-09; toda decisão registra auditoria de autorização |
| **Eventos** | Códigos EVT-IC conforme MMS-002-05 §14 (payload e garantias) |
| **Mensagens** | Mensagens de negócio exibidas ao usuário (chaves i18n `ic.uc.*`) |
| **Testes** | Códigos TC-IC-xxx-y: cenários de teste de aceite vinculados ao caso de uso (detalhados em MMS-002-17) |
| **Erros** | Códigos IC-ERR conforme MMS-002-02 v1.1.0 (Business Rules) |
| **Estados** | ST-IC-001 Rascunho, ST-IC-002 Ativo, ST-IC-003 Inativo, ST-IC-004 Inativo (Descarte) — MMS-002-03 |

---

# UC-IC-001 — Cadastrar Item

## Objetivo

Permitir que o mantenedor do catálogo registre um novo item (EPI, fardamento ou material) em estado Rascunho.

## Atores

- Gerente de Suprimentos / Administrador (primário)
- Sistema (secundário — validações, auditoria, evento)

## Pré-condições

- Usuário autenticado.
- Usuário com permissão IC-PERM-001 no escopo da empresa.
- Unidade de medida, grupo e categoria existentes e ativos no Master Data (FD-001-09).

## Pós-condições

- Item criado em estado **Rascunho** (ST-IC-001).
- Evento EVT-IC-001 `ItemCreated` publicado.
- Registro de auditoria e entrada de Timeline criados.

## Gatilho

O mantenedor identifica a necessidade de disponibilizar um novo item no catálogo (MMS-002-06 §3).

## Fluxo Principal

1. Selecionar "Novo Item".
2. Informar código, descrição, unidade de medida, grupo e categoria.
3. Opcionalmente informar código ERP, CA, grade de tamanhos e imagem.
4. Salvar o item.

**Detalhamento do passo 2:** grupo define comportamento — para categorias de EPI (`materials.item.epi.categories`), o CA torna-se obrigatório antes da ativação (IC-BR-080); em Rascunho o CA pode ficar pendente.

**Detalhamento do passo 4:** o sistema valida os campos (ver Validações), verifica unicidade do código por empresa (IC-BR-001) e do código ERP quando informado (IC-BR-011), persiste o aggregate em Rascunho, publica EVT-IC-001 e registra Timeline/Auditoria.

## Fluxos Alternativos

A1. Cancelar cadastro.
- A1.1. O usuário abandona o formulário sem salvar. Nenhum dado é persistido; nenhum evento é publicado.

A2. Cadastro mínimo para complementação posterior.
- A2.1. O mantenedor salva apenas com os campos mínimos de Rascunho. CA, grade, imagem e parâmetros são complementados no UC-IC-002/007 antes da ativação.

A3. Cópia de item existente.
- A3.1. O mantenedor inicia o cadastro a partir de um item existente ("duplicar"): o sistema pré-preenche os dados, mas exige novo código (e novo código ERP, se houver). O novo item nasce em Rascunho sem vínculo com a origem.

## Fluxos de Exceção

E1. Usuário sem permissão.
- E1.1. Sistema recusa com `IC-ERR-900` (permissão insuficiente); decisão de autorização auditada.

E2. Código já existente na empresa.
- E2.1. Recusa com `IC-ERR-001` e MSG-IC-UC-001-B, indicando o item existente.

E3. Código ERP já vinculado a outro item.
- E3.1. Recusa com `IC-ERR-011`.

E4. Unidade/grupo/categoria inexistente ou inativa.
- E4.1. Recusa com `IC-ERR-003`/`IC-ERR-004`/`IC-ERR-005`.

E5. Imagem fora da conformidade no cadastro.
- E5.1. Recusa com `IC-ERR-083`; item não é persistido.

## Regras

- IC-BR-001 (unicidade de código)
- IC-BR-002 (descrição)
- IC-BR-003 (unidade de medida)
- IC-BR-004/005 (grupo e categoria)
- IC-BR-011 (código ERP)
- IC-BR-080/081 (CA para EPI, quando informado)
- IC-BR-082/083 (grade e imagem, quando informadas)

## Eventos

- ItemCreated (EVT-IC-001)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IC-UC-001-A | "Item {code} cadastrado em rascunho. Complete os dados e ative para disponibilizar." |
| MSG-IC-UC-001-B | "Já existe um item com o código {code} nesta empresa." |
| MSG-IC-UC-001-C | "Para itens do grupo EPI, o CA será obrigatório na ativação." |

## Validações

- Código: obrigatório, único por empresa, formato conforme `materials.item.code.pattern`.
- Descrição: obrigatória, limite conforme IC-BR-002.
- Unidade de medida: obrigatória, ativa no Master Data.
- Grupo/categoria: obrigatórios, válidos e ativos.
- Código ERP: opcional; quando informado, único por empresa (`materials.item.erp-code.unique=true`).
- CA: opcional em Rascunho; obrigatório para EPI na ativação (IC-BR-080); formato/vigência conforme IC-BR-081.
- Imagem: opcional; tipo e tamanho conforme IC-BR-083; upload via FD-001-03.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/items` | Cria o item em Rascunho; retorna `201` com `id` e `code` |
| POST | `/api/v1/items/{id}/image` | Upload da imagem (FD-001-03); retorna `200` com `imageFileId` |

## Permissões

- IC-PERM-001 (Cadastrar item), avaliada no escopo da empresa do usuário.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IC-001-1 | Cadastro completo válido → Rascunho + EVT-IC-001 |
| TC-IC-001-2 | Cadastro sem permissão → IC-ERR-900 + auditoria de negação |
| TC-IC-001-3 | Código duplicado → IC-ERR-001 |
| TC-IC-001-4 | Código ERP duplicado → IC-ERR-011 |
| TC-IC-001-5 | Cadastro de EPI sem CA → permitido em Rascunho com aviso MSG-IC-UC-001-C |
| TC-IC-001-6 | Cancelamento → nenhum dado persistido |

---

# UC-IC-002 — Editar Item

## Objetivo

Permitir a alteração dos dados cadastrais de um item (descrição, unidade, grupo, categoria, código ERP, CA, grade de tamanhos, imagem).

## Atores

- Gerente de Suprimentos / Administrador (primário)

## Pré-condições

- Item existente em estado **Rascunho**, **Ativo** ou **Inativo** (nunca em Descarte — ST-IC-004 é terminal).
- Usuário autenticado com permissão IC-PERM-002.

## Pós-condições

- Alterações persistidas com `changedFields` registrados.
- Evento EVT-IC-002 `ItemUpdated` publicado.
- Timeline e Auditoria com before/after (IC-BR-070).

## Gatilho

O mantenedor seleciona "Editar" em um item.

## Fluxo Principal

1. Abrir o item para edição.
2. Alterar os campos desejados.
3. Salvar.

**Detalhamento do passo 3:** o sistema valida as alterações, aplica a política de edição para itens em uso (IC-BR-021), persiste, publica EVT-IC-002 com `changedFields` e registra auditoria com valores anteriores e novos.

## Fluxos Alternativos

A1. Edição de item em Rascunho.
- A1.1. Todas as alterações são livres, incluindo campos estruturais (grupo, unidade, categoria).

A2. Edição de item Ativo sem uso em operações abertas.
- A2.1. Alterações estruturais permitidas; consumidores são atualizados via projeção (POL-IC-03).

A3. Edição apenas descritiva (sinônimos, imagem, parâmetros).
- A3.1. Sempre livre, mesmo com item em uso — sinônimos seguem UC-IC-005; parâmetros seguem UC-IC-007.

## Fluxos de Exceção

E1. Item em Descarte (ST-IC-004).
- E1.1. Recusa com `IC-ERR-090` (estado terminal não editável).

E2. Alteração estrutural em item com operações abertas.
- E2.1. Recusa com `IC-ERR-021` conforme `materials.item.edit.in-use-policy` (ou exige confirmação reforçada, conforme configuração).

E3. Remoção de CA de item EPI ativo.
- E3.1. Recusa com `IC-ERR-080` — CA não pode ser removido de EPI ativo; apenas substituído por CA válido.

E4. Código ERP duplicado na alteração.
- E4.1. Recusa com `IC-ERR-011`.

E5. Conflito de versão (edição concorrente).
- E5.1. Recusa com `IC-ERR-409`; usuário recarrega os dados.

E6. Usuário sem permissão.
- E6.1. Recusa com `IC-ERR-900`; auditoria de negação.

## Regras

- IC-BR-002..005 (integridade cadastral)
- IC-BR-011 (código ERP)
- IC-BR-021 (edição de item em uso)
- IC-BR-070 (auditoria de alterações)
- IC-BR-080..083 (CA, grade, imagem)

## Eventos

- ItemUpdated (EVT-IC-002)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IC-UC-002-A | "Item {code} atualizado. Alterações: {changedFields}." |
| MSG-IC-UC-002-B | "Este item está em uso em operações abertas. Alterações estruturais não são permitidas." |
| MSG-IC-UC-002-C | "Itens descartados não podem ser editados." |

## Validações

- Campos alterados respeitam as mesmas validações do UC-IC-001.
- Grupo/unidade/categoria/grade: bloqueadas ou restringidas quando item em uso (IC-BR-021).
- CA de EPI ativo: substituível, nunca removível (IC-BR-080/081).
- Concorrência otimista via `version` do aggregate.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| PATCH | `/api/v1/items/{id}` | Edita dados cadastrais; exige `If-Match` (concorrência otimista) |
| PUT | `/api/v1/items/{id}/ca` | Informa/substitui o CA |
| PUT | `/api/v1/items/{id}/size-grid` | Vincula/altera grade de tamanhos |
| POST | `/api/v1/items/{id}/image` | Substitui a imagem |

## Permissões

- IC-PERM-002 (Editar item).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IC-002-1 | Edição válida em Rascunho → EVT-IC-002 com changedFields |
| TC-IC-002-2 | Edição estrutural em item em uso → IC-ERR-021 |
| TC-IC-002-3 | Edição de item descartado → IC-ERR-090 |
| TC-IC-002-4 | Remoção de CA de EPI ativo → IC-ERR-080 |
| TC-IC-002-5 | Conflito de versão → IC-ERR-409 |

---

# UC-IC-003 — Ativar Item

## Objetivo

Disponibilizar um item para seleção pelos módulos consumidores (MMS-003, MMS-004, MMS-005, PR-001).

## Atores

- Gerente de Suprimentos / Administrador (primário)
- Sistema (validação automática de ativação)

## Pré-condições

- Item em estado **Rascunho** (ST-IC-001).
- Usuário autenticado com permissão IC-PERM-003.

## Pós-condições

- Item em estado **Ativo** (ST-IC-002), visível na busca operacional dos consumidores.
- Evento EVT-IC-003 `ItemActivated` publicado (evento crítico — MMS-002-05 §14.3).
- Projeção de leitura do catálogo atualizada (POL-IC-03).

## Gatilho

O mantenedor seleciona "Ativar" em um item em Rascunho.

## Fluxo Principal

1. Usuário seleciona "Ativar".
2. Sistema executa a bateria de guards de ativação (RG-IC-GW-001-A..H).
3. Sistema transiciona o estado para Ativo.
4. Sistema publica EVT-IC-003 e atualiza a projeção de leitura.

**Detalhamento do passo 2:** validações de unicidade, integridade cadastral, Master Data ativo, CA para EPI, grade e imagem — conforme MMS-002-06 §9.1 e MMS-002-05 §7.

## Fluxos Alternativos

A1. Ativação com pendências bloqueantes.
- A1.1. Sistema mantém o item em Rascunho e apresenta a lista exata de pendências (FA-IC-001); nenhum evento de ativação é publicado; tentativa registrada em Timeline/Audit.

A2. Ativação de EPI com CA presente e válido.
- A2.1. O sistema valida formato e vigência do CA (IC-BR-081) e prossegue normalmente.

## Fluxos de Exceção

E1. CA ausente para grupo EPI.
- E1.1. Recusa com `IC-ERR-080` e MSG-IC-UC-003-B.

E2. CA inválido ou vencido.
- E2.1. Recusa com `IC-ERR-081`.

E3. Master Data inativo (unidade, grupo, categoria, grade inativadas após o cadastro).
- E3.1. Recusa com `IC-ERR-003`/`004`/`005`/`082`; orienta ajuste no UC-IC-002.

E4. Falha técnica na validação (timeout TIME-IC-001).
- E4.1. EXC-IC-007 → COMP-IC-001 (até 3 reexecuções; depois, item permanece em Rascunho com motivo técnico).

E5. Usuário sem permissão.
- E5.1. Recusa com `IC-ERR-900` (EXC-IC-003); auditoria de negação e notificação ao Administrador.

## Regras

- IC-BR-001..005, IC-BR-011 (guards cadastrais)
- IC-BR-080..083 (guards EPI/grade/imagem)
- IC-BR-070 (auditoria da transição)

## Eventos

- ItemActivated (EVT-IC-003)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IC-UC-003-A | "Item {code} ativado e disponível para solicitações." |
| MSG-IC-UC-003-B | "Informe o CA (Certificado de Aprovação) para ativar um EPI." |
| MSG-IC-UC-003-C | "Ativação bloqueada. Pendências: {failures}." |

## Validações

- Todas as validações do UC-IC-001 reexecutadas no momento da ativação (estado pode ter ficado inválido por mudança de Master Data — POL-IC-05).
- Permissão de ativação no escopo.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/items/{id}/activate` | Ativa o item; retorna `200` com novo status ou `422` com pendências |

## Permissões

- IC-PERM-003 (Ativar/Reativar item).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IC-003-1 | Ativação válida → Ativo + EVT-IC-003 + projeção atualizada |
| TC-IC-003-2 | EPI sem CA → IC-ERR-080, permanece Rascunho |
| TC-IC-003-3 | Unidade inativada após cadastro → IC-ERR-003 |
| TC-IC-003-4 | Ativação sem permissão → IC-ERR-900 + notificação ao Administrador |
| TC-IC-003-5 | Timeout de validação → COMP-IC-001 com 3 reexecuções |

---

# UC-IC-004 — Inativar, Reativar e Descartar Item

## Objetivo

Controlar a disponibilidade do item: suspender seu uso (inativação), restaurá-lo (reativação) ou encerrar definitivamente seu ciclo de vida (descarte).

## Atores

- Gerente de Suprimentos / Administrador (primário)
- Sistema (validações, eventos, notificações POL-IC-04)

## Pré-condições

- Inativação: item em estado **Ativo**.
- Reativação: item em estado **Inativo**.
- Descarte: item em estado **Inativo**, sem bloqueio de uso (RG-IC-GW-002-C).
- Usuário autenticado com permissão IC-PERM-004.

## Pós-condições

- Inativação: item em **Inativo**; EVT-IC-004 publicado; item sai da busca operacional.
- Reativação: item em **Ativo**; EVT-IC-003 publicado; guards de ativação reexecutados.
- Descarte: item em **Inativo (Descarte)** — terminal e irreversível; EVT-IC-005 publicado.
- Referências históricas preservadas em todos os casos.

## Gatilho

O mantenedor seleciona "Inativar", "Reativar" ou "Descartar" conforme o estado atual do item.

## Fluxo Principal (Inativação)

1. Usuário seleciona "Inativar".
2. Sistema solicita motivo (obrigatório).
3. Sistema transiciona para Inativo, publica EVT-IC-004 e avalia POL-IC-04 (item crítico → notificação).

## Fluxo Principal (Reativação)

1. Usuário seleciona "Reativar".
2. Sistema reexecuta a bateria completa de guards de ativação (FA-IC-003).
3. Em sucesso, transiciona para Ativo e publica EVT-IC-003.

## Fluxo Principal (Descarte)

1. Usuário seleciona "Descartar".
2. Sistema exige motivo e confirmação reforçada (ação irreversível).
3. Sistema verifica bloqueio de uso (IC-BR-021).
4. Sistema transiciona para Inativo (Descarte) e publica EVT-IC-005.

## Fluxos Alternativos

A1. Inativação de item crítico.
- A1.1. Se o item está abaixo do ponto de reposição ou possui solicitações em aberto (MMS-003), o sistema alerta antes da confirmação e, após a inativação, notifica Gerente de Suprimentos e Almoxarifado (POL-IC-04).

A2. Reativação com pendências.
- A2.1. Se algum guard falhar (ex.: grade inativada durante o período inativo), o item permanece Inativo com a lista de pendências (MSG-IC-UC-004-C).

## Fluxos de Exceção

E1. Transição inválida (ex.: descartar item Ativo diretamente).
- E1.1. Recusa com `IC-ERR-090`; mensagem orienta o caminho correto (inativar primeiro).

E2. Descarte bloqueado por operações em aberto.
- E2.1. EXC-IC-004: recusa com `IC-ERR-021`; item permanece Inativo.

E3. Motivo ausente.
- E3.1. Recusa com `IC-ERR-091` (motivo obrigatório para inativação/descarte).

E4. Usuário sem permissão.
- E4.1. Recusa com `IC-ERR-900`; auditoria de negação.

E5. Falha na publicação do evento crítico.
- E5.1. TIME-IC-002 → COMP-IC-002 (republicação via outbox; persistindo, DLQ + reconciliação).

## Regras

- MMS-002-03 (transições permitidas e estado terminal)
- IC-BR-021 (restrição por uso)
- IC-BR-070 (auditoria)
- IC-BR-080..083 (guards na reativação)

## Eventos

- ItemInactivated (EVT-IC-004)
- ItemActivated (EVT-IC-003, na reativação)
- ItemDiscarded (EVT-IC-005)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IC-UC-004-A | "Item {code} inativado. Solicitações existentes não são afetadas." |
| MSG-IC-UC-004-B | "Item {code} descartado definitivamente. Esta ação não pode ser desfeita." |
| MSG-IC-UC-004-C | "Reativação bloqueada. Pendências: {failures}." |
| MSG-IC-UC-004-D | "Este item possui solicitações em aberto. O almoxarifado será notificado." |

## Validações

- Motivo obrigatório para inativação e descarte (mínimo 10 caracteres).
- Estado de origem válido conforme State Machine.
- Reativação: todos os guards do UC-IC-003.
- Descarte: verificação de operações em aberto (RG-IC-GW-002-C).

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/items/{id}/inactivate` | Inativa; exige `reason` no corpo |
| POST | `/api/v1/items/{id}/reactivate` | Reativa; retorna `200` ou `422` com pendências |
| POST | `/api/v1/items/{id}/discard` | Descarta (terminal); exige `reason` e `confirmation` |

## Permissões

- IC-PERM-004 (Inativar/Descartar item); reativação usa IC-PERM-003.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IC-004-1 | Inativação válida → Inativo + EVT-IC-004 |
| TC-IC-004-2 | Inativação de item crítico → notificação POL-IC-04 disparada |
| TC-IC-004-3 | Reativação válida → Ativo + EVT-IC-003 (guards reexecutados) |
| TC-IC-004-4 | Reativação com grade inativada → permanece Inativo + pendências |
| TC-IC-004-5 | Descarte direto de item Ativo → IC-ERR-090 |
| TC-IC-004-6 | Descarte com operações abertas → IC-ERR-021 |
| TC-IC-004-7 | Descarte sem motivo → IC-ERR-091 |

---

# UC-IC-005 — Gerenciar Sinônimos

## Objetivo

Manter os sinônimos de busca de um item, ampliando sua localização pelos usuários dos módulos consumidores.

## Atores

- Gerente de Suprimentos / Administrador (primário)

## Pré-condições

- Item existente em estado **Rascunho**, **Ativo** ou **Inativo**.
- Usuário autenticado com permissão IC-PERM-005.

## Pós-condições

- Sinônimo incluído ou removido.
- EVT-IC-007 `SynonymAdded` ou EVT-IC-008 `SynonymRemoved` publicado.
- Índice de busca da projeção atualizado (POL-IC-03).

## Gatilho

O mantenedor seleciona "Adicionar sinônimo" ou "Remover sinônimo" na tela do item.

## Fluxo Principal (Inclusão)

1. Usuário informa o termo sinônimo.
2. Sistema valida não duplicidade para o mesmo item (IC-BR-040).
3. Sistema persiste, publica EVT-IC-007 e atualiza o índice de busca.

## Fluxo Principal (Remoção)

1. Usuário seleciona o sinônimo a remover.
2. Sistema remove, publica EVT-IC-008 e atualiza o índice de busca.

## Fluxos Alternativos

A1. Normalização automática.
- A1.1. O sistema normaliza o termo (trim, case-insensitive) antes da verificação de duplicidade e da indexação.

A2. Sinônimo igual à descrição do item.
- A2.1. Permitido, mas o sistema emite aviso de redundância (sem bloqueio).

## Fluxos de Exceção

E1. Sinônimo duplicado para o mesmo item.
- E1.1. Recusa com `IC-ERR-040`.

E2. Item em Descarte.
- E2.1. Recusa com `IC-ERR-090`.

E3. Usuário sem permissão.
- E3.1. Recusa com `IC-ERR-900`; auditoria de negação.

## Regras

- IC-BR-040 (não duplicidade)
- IC-BR-041 (remoção permitida apenas em estados editáveis)
- IC-BR-070 (auditoria)

## Eventos

- SynonymAdded (EVT-IC-007)
- SynonymRemoved (EVT-IC-008)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IC-UC-005-A | "Sinônimo \"{term}\" adicionado ao item {code}." |
| MSG-IC-UC-005-B | "Este sinônimo já existe para este item." |
| MSG-IC-UC-005-C | "Sinônimo removido. A busca foi atualizada." |

## Validações

- Termo: obrigatório, 2–100 caracteres, normalizado.
- Duplicidade avaliada por (itemId, termo normalizado).

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/items/{id}/synonyms` | Adiciona sinônimo; retorna `201` |
| DELETE | `/api/v1/items/{id}/synonyms/{synonymId}` | Remove sinônimo; retorna `204` |

## Permissões

- IC-PERM-005 (Gerenciar sinônimos).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IC-005-1 | Inclusão válida → EVT-IC-007 + índice atualizado |
| TC-IC-005-2 | Duplicidade normalizada ("Capacete" vs "capacete") → IC-ERR-040 |
| TC-IC-005-3 | Remoção válida → EVT-IC-008 |
| TC-IC-005-4 | Inclusão em item descartado → IC-ERR-090 |

---

# UC-IC-006 — Consultar Catálogo

## Objetivo

Permitir a busca, filtragem e visualização de itens do catálogo, incluindo localização por sinônimos, com recorte por perfil de acesso.

## Atores

- Qualquer usuário autenticado com permissão IC-PERM-006 (mantenedor, solicitante, almoxarifado, gestor, auditor)
- Módulos consumidores (consulta operacional via projeção)

## Pré-condições

- Usuário autenticado.

## Pós-condições

- Nenhuma alteração de estado (operação somente-leitura).

## Gatilho

O usuário acessa a tela de catálogo ou aciona a busca de itens em um módulo consumidor.

## Fluxo Principal

1. Usuário informa termo de busca e/ou filtros.
2. Sistema consulta a projeção de leitura do catálogo (IC-BR-071).
3. Sistema retorna os itens conforme o recorte de visibilidade do perfil.

**Detalhamento do passo 2:** a busca cobre código, descrição e sinônimos (normalizados); filtros disponíveis: status, grupo, categoria, unidade, com/sem CA, com/sem imagem.

**Detalhamento do passo 3:** perfis operacionais (solicitante, almoxarifado) veem por padrão apenas itens **Ativos**; mantenedor e auditor visualizam todos os estados com filtro explícito.

## Fluxos Alternativos

A1. Busca por sinônimo.
- A1.1. O termo informado corresponde a um sinônimo; o item é retornado com destaque do termo encontrado.

A2. Visualização de detalhe.
- A2.1. O usuário abre o item: dados cadastrais, CA, grade, imagem, parâmetros de reposição (conforme perfil) e timeline completa.

A3. Consulta operacional por módulo consumidor.
- A3.1. MMS-003/MMS-004/PR-001 consultam a projeção local, que só contém itens Ativos (IC-BR-021); itens inativados durante o preenchimento de uma operação são sinalizados ao usuário.

## Fluxos de Exceção

E1. Nenhum resultado.
- E1.1. Retorno vazio com MSG-IC-UC-006-B e sugestão de revisar filtros ou termo.

E2. Falha da projeção (cache indisponível).
- E2.1. Fallback automático para consulta ao banco transacional (fonte de verdade); degradação registrada.

E3. Usuário sem permissão de consulta.
- E3.1. Recusa com `IC-ERR-900`.

## Regras

- IC-BR-021 (consumidores operam apenas com itens Ativos)
- IC-BR-071 (projeção de leitura; cache nunca é fonte de verdade)

## Eventos

- Nenhum (operação somente-leitura).

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IC-UC-006-A | "{count} item(ns) encontrado(s)." |
| MSG-IC-UC-006-B | "Nenhum item encontrado. Revise o termo ou os filtros." |
| MSG-IC-UC-006-C | "Este item foi inativado e não pode ser utilizado em novas operações." |

## Validações

- Termo de busca: até 100 caracteres; paginação por keyset (cursor) obrigatória para listas.
- Filtros restritos aos valores válidos de Master Data.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| GET | `/api/v1/items` | Lista com busca, filtros e paginação keyset (`?search=&status=&groupId=&cursor=`) |
| GET | `/api/v1/items/{id}` | Detalhe completo do item conforme perfil |
| GET | `/api/v1/items/{id}/timeline` | Timeline do item (perfis autorizados) |

## Permissões

- IC-PERM-006 (Consultar catálogo); visibilidade de estados e campos sensíveis varia por perfil (MMS-002-09).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IC-006-1 | Busca por descrição → itens Ativos retornados |
| TC-IC-006-2 | Busca por sinônimo → item retornado com destaque |
| TC-IC-006-3 | Perfil operacional não vê itens Inativos/Rascunho por padrão |
| TC-IC-006-4 | Fallback de cache → consulta ao banco sem erro ao usuário |
| TC-IC-006-5 | Paginação keyset: 2 páginas sem duplicidade nem omissão |

---

# UC-IC-007 — Gerenciar Parâmetros de Reposição

## Objetivo

Definir e manter os parâmetros de reposição do item (ponto de reposição, estoque mínimo/máximo, lead time), que alimentam os indicadores de estoque crítico (MMS-005) e as rotinas de reposição.

## Atores

- Gerente de Suprimentos / Administrador (primário)

## Pré-condições

- Item existente em estado **Rascunho**, **Ativo** ou **Inativo**.
- Usuário autenticado com permissão IC-PERM-007.

## Pós-condições

- Parâmetros persistidos com before/after auditado.
- EVT-IC-006 `ReplenishmentParametersChanged` publicado.
- Projeção atualizada para indicadores de estoque crítico (MMS-005).

## Gatilho

O mantenedor seleciona "Parâmetros de reposição" na tela do item.

## Fluxo Principal

1. Usuário informa ponto de reposição, estoque mínimo, estoque máximo, lead time e, opcionalmente, a **política de quantidade sugerida** (repor até o máximo / múltiplo de embalagem / lote fixo) e a **rota de suprimento** (transferência / compra / manual) — ADR-014.
2. Sistema valida a coerência dos parâmetros (IC-BR-050..052) e da regra de reposição (IC-BR-100/101 — `lote_fixo` exige quantidade > 0; `multiplo_embalagem` exige unidade de compra com fator).
3. Sistema persiste, publica EVT-IC-006 e registra auditoria.

## Fluxos Alternativos

A1. Definição parcial.
- A1.1. O mantenedor define apenas alguns parâmetros (ex.: apenas estoque mínimo); campos não informados permanecem nulos e o item não participa dos indicadores correspondentes.

A2. Limpeza de parâmetros.
- A2.1. O mantenedor zera os parâmetros de um item (volta a não ter parametrização); EVT-IC-006 publicado com `newParameters` nulos.

## Fluxos de Exceção

E1. Incoerência: mínimo > máximo, ou ponto de reposição fora da faixa mínimo–máximo.
- E1.1. Recusa com `IC-ERR-050`.

E2. Valores negativos ou acima do teto configurado.
- E2.1. Recusa com `IC-ERR-051`.

E3. Lead time inválido (negativo ou acima do teto).
- E3.1. Recusa com `IC-ERR-052`.

E4. Item em Descarte.
- E4.1. Recusa com `IC-ERR-090`.

E5. Usuário sem permissão.
- E5.1. Recusa com `IC-ERR-900`; auditoria de negação.

## Regras

- IC-BR-050 (coerência mínimo ≤ ponto ≤ máximo)
- IC-BR-051 (faixas de valores)
- IC-BR-052 (lead time)
- IC-BR-100 (política de quantidade sugerida)
- IC-BR-101 (rota de suprimento)
- IC-BR-070 (auditoria)

## Eventos

- ReplenishmentParametersChanged (EVT-IC-006)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IC-UC-007-A | "Parâmetros de reposição atualizados para o item {code}." |
| MSG-IC-UC-007-B | "O ponto de reposição deve estar entre o estoque mínimo e o máximo." |

## Validações

- Estoque mínimo ≥ 0; estoque máximo ≥ mínimo; ponto de reposição entre mínimo e máximo (quando ambos informados).
- Lead time em dias, ≥ 0, teto conforme `materials.item.replenishment.max-lead-time-days`.
- Concorrência otimista via `version`.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| PUT | `/api/v1/items/{id}/replenishment-parameters` | Define/substitui parâmetros; exige `If-Match` |

## Permissões

- IC-PERM-007 (Gerenciar parâmetros de reposição).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IC-007-1 | Parametrização válida → EVT-IC-006 + auditoria before/after |
| TC-IC-007-2 | Mínimo > máximo → IC-ERR-050 |
| TC-IC-007-3 | Lead time negativo → IC-ERR-052 |
| TC-IC-007-4 | Limpeza de parâmetros → EVT-IC-006 com newParameters nulos |

---

# UC-IC-008 — Gerenciar Unidades Alternativas e Conversões

## Objetivo

Manter a **unidade de estoque (base)** e as **unidades alternativas com fator de conversão** de um item (ex.: `1 CX = 12 UN`), viabilizando comprar em uma unidade e estocar/consumir em outra (ADR-013).

## Atores

- Gerente de Suprimentos / Administrador (primário)

## Pré-condições

- Item existente em estado **Rascunho**, **Ativo** ou **Inativo** (nunca em Descarte).
- Usuário autenticado com permissão IC-PERM-002 (edição cadastral).

## Pós-condições

- Unidade alternativa incluída, alterada ou removida; ou unidade base definida.
- Evento EVT-IC-002 `ItemUpdated` publicado com `changedFields` (uom).
- Timeline e Auditoria com before/after.

## Gatilho

O mantenedor acessa "Unidades e conversões" na tela do item.

## Fluxo Principal (Incluir unidade alternativa)

1. Usuário informa a unidade alternativa (Master Data), o fator de conversão para a base e, opcionalmente, o papel (unidade de compra padrão / de consumo).
2. Sistema valida: mesma categoria de UoM da base (IC-BR-092), fator > 0 e unicidade de UoM e de papel por item (IC-BR-091).
3. Sistema persiste, publica EVT-IC-002 e registra auditoria.

## Fluxos Alternativos

A1. Definir/alterar a unidade base.
- A1.1. Permitido enquanto o item **não tem movimentação** no MMS-004 (INV-IC-12); após a primeira movimentação, a base é imutável.

A2. Remover unidade alternativa.
- A2.1. Remoção lógica; movimentos históricos que usaram a unidade preservam o fator aplicado no momento (IC-BR-093).

## Fluxos de Exceção

E1. Fator inválido (≤ 0).
- E1.1. Recusa com `IC-ERR-110`.

E2. Unidade alternativa de categoria diferente da base.
- E2.1. Recusa com `IC-ERR-111`.

E3. Papel já atribuído a outra unidade do item.
- E3.1. Recusa com `IC-ERR-110` (unicidade de papel).

E4. Alteração da base após a primeira movimentação.
- E4.1. Recusa com `IC-ERR-112` (base imutável — INV-IC-12).

E5. Usuário sem permissão.
- E5.1. Recusa com `IC-ERR-900`; auditoria de negação.

## Regras

- IC-BR-090 (unidade de estoque base)
- IC-BR-091 (unidades alternativas com fator e papel)
- IC-BR-092 (conversão na mesma categoria)
- IC-BR-093 (fator imutável para movimentos; arredondamento parametrizável)

## Eventos

- ItemUpdated (EVT-IC-002)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IC-UC-008-A | "Unidade {uom} adicionada com fator {factor} para o item {code}." |
| MSG-IC-UC-008-B | "A unidade alternativa deve pertencer à mesma categoria da unidade base." |
| MSG-IC-UC-008-C | "A unidade base não pode ser alterada após a primeira movimentação de estoque." |

## Validações

- Fator de conversão > 0 (`NUMERIC(18,6)`), arredondamento conforme `materials.item.uom.rounding`.
- Categoria da unidade alternativa = categoria da base (Master Data, FD-001-09).
- Unicidade de UoM e de papel por item.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| PUT | `/api/v1/items/{id}/base-unit` | Define/altera a unidade de estoque (base); bloqueado após 1ª movimentação |
| GET | `/api/v1/items/{id}/uom-conversions` | Lista as unidades alternativas do item |
| POST | `/api/v1/items/{id}/uom-conversions` | Inclui unidade alternativa (uom, fator, papel); retorna `201` |
| DELETE | `/api/v1/items/{id}/uom-conversions/{uomId}` | Remove unidade alternativa; retorna `204` |

## Permissões

- IC-PERM-002 (Editar item).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IC-008-1 | Inclusão válida (`1 CX = 12 UN`) → EVT-IC-002 + auditoria |
| TC-IC-008-2 | Fator ≤ 0 → IC-ERR-110 |
| TC-IC-008-3 | Unidade de categoria diferente → IC-ERR-111 |
| TC-IC-008-4 | Papel duplicado (2 unidades de compra) → IC-ERR-110 |
| TC-IC-008-5 | Alterar base após movimentação → IC-ERR-112 |

---

# 8. Matriz de Rastreabilidade UC × Regras × Eventos × APIs × Permissões

| UC | Regras IC-BR | Eventos | Endpoints | Permissões |
| -- | ------------ | ------- | --------- | ---------- |
| UC-IC-001 Cadastrar | 001, 002, 003, 004, 005, 011, 080, 081, 082, 083 | EVT-IC-001 | POST /items; POST /items/{id}/image | IC-PERM-001 |
| UC-IC-002 Editar | 002..005, 011, 021, 070, 080..083 | EVT-IC-002 | PATCH /items/{id}; PUT /items/{id}/ca; PUT /items/{id}/size-grid; POST /items/{id}/image | IC-PERM-002 |
| UC-IC-003 Ativar | 001..005, 011, 070, 080..083 | EVT-IC-003 | POST /items/{id}/activate | IC-PERM-003 |
| UC-IC-004 Inativar/Reativar/Descartar | 021, 070, 080..083, State Machine | EVT-IC-003/004/005 | POST /items/{id}/inactivate; /reactivate; /discard | IC-PERM-004 (003 p/ reativar) |
| UC-IC-005 Sinônimos | 040, 041, 070 | EVT-IC-007/008 | POST/DELETE /items/{id}/synonyms | IC-PERM-005 |
| UC-IC-006 Consultar | 021, 071 | — | GET /items; GET /items/{id}; GET /items/{id}/timeline | IC-PERM-006 |
| UC-IC-007 Parâmetros | 050, 051, 052, 100, 101, 070 | EVT-IC-006 | PUT /items/{id}/replenishment-parameters | IC-PERM-007 |
| UC-IC-008 Unidades/Conversões | 090, 091, 092, 093 | EVT-IC-002 | PUT /items/{id}/base-unit; GET/POST/DELETE /items/{id}/uom-conversions | IC-PERM-002 |

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: 7 casos de uso (UC-IC-001..007) em especificação UML completa — fluxo principal, fluxos alternativos, fluxos de exceção, pré/pós-condições, regras IC-BR, eventos EVT-IC, mensagens i18n, validações, APIs `/api/v1/items`, permissões IC-PERM e testes TC-IC; matriz de rastreabilidade consolidada. |
| 1.1.0 | 2026-08-08 | Arquiteto Principal | Incorporação de ADR-013 e ADR-014: novo **UC-IC-008 — Gerenciar Unidades Alternativas e Conversões** (unidade base + unidades alternativas com fator, endpoints `/base-unit` e `/uom-conversions`, regras IC-BR-090..093); **UC-IC-007** estendido com política de quantidade sugerida e rota de suprimento (IC-BR-100/101); matriz de rastreabilidade ampliada (UC-IC-008 + regras novas). |
