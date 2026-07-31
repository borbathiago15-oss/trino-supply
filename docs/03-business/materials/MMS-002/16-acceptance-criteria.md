**Documento:** MMS-002-16 — Acceptance Criteria
**Módulo:** MMS-002 — Item Catalog (Materials Domain)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 (Visão do Módulo — NFR e DoD), MMS-002-02 (Business Rules), MMS-002-03 (State Machine), MMS-002-05 (Event Storming), MMS-002-07 (Use Cases), MMS-002-09 (Permissions), MMS-002-10 (Notifications), MMS-002-11 (Database Model), MMS-002-13 (API), MMS-002-14 (UX), MMS-002-15 (Wireframes)
**Referências:** SEC-001, SEC-003, FD-001 (Foundation), GOV-001, ADR-009, ADR-010, PR-001-16 (Acceptance Criteria do Purchase Requisition — padrão de formato)

---

# 1. Objetivo

Este documento consolida os **critérios de aceite formais** do módulo Item Catalog em formato verificável, servindo como contrato de aceitação entre produto, engenharia e qualidade, e como insumo direto para o **MMS-002-17 — Test Scenarios**.

Regras de vínculo:

- Todo critério deste documento deriva de uma fonte documentada (UC, regra de negócio, state machine, permissão, evento, API, UX, wireframe ou NFR). Nenhum critério inventa comportamento.
- Critérios já definidos em documentos anteriores (CA-UX-IC-*, CA-WF-IC-*) são **referenciados, não reescritos**; este documento os incorpora por citação.
- A aprovação do módulo exige 100% dos critérios **P0** verificados (seção 11 — DoD).

## 1.1 Convenções

- **Formato:** Given/When/Then (Dado/Quando/Então).
- **Identificação:** `AC-IC-NNN`, sequencial, imutável após publicação.
- **Prioridade:**
  - **P0** — bloqueante para o MVP; falha impede release;
  - **P1** — obrigatório no MVP, com cenário de exceção tolerado e documentado;
  - **P2** — desejável no MVP, obrigatório na v1.1.
- **Verificação:** cada critério indica a camada de verificação esperada (API, UI, banco, evento, auditoria). A automação é especificada no MMS-002-17.

---

# 2. Critérios Funcionais por Caso de Uso

## 2.1 UC-IC-001 — Criar Item

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IC-001 | P0 | **Dado** um usuário com permissão `items.write` (Catalog Maintainer/Admin), **Quando** cria o item com campos válidos (código, descrição, grupo, unidade), **Então** o sistema persiste em estado Draft, emite EVT-IC-001 e registra auditoria | API + evento + auditoria |
| AC-IC-002 | P0 | **Dado** um usuário sem permissão de criação, **Quando** tenta criar, **Então** o sistema recusa com IC-ERR-900, registra auditoria de negação e não persiste nenhum dado | API + auditoria |
| AC-IC-003 | P0 | **Dado** um código já utilizado por item ativo/inativo da mesma empresa, **Quando** informado, **Então** o sistema recusa com IC-ERR-010 e aponta o item existente | API |
| AC-IC-004 | P0 | **Dado** um código ERP já vinculado a outro item da empresa, **Quando** informado, **Então** o sistema recusa com IC-ERR-011 | API |
| AC-IC-005 | P0 | **Dado** código de item descartado anteriormente, **Quando** reutilizado, **Então** o sistema aceita (unicidade soft-delete-ciente) e mantém o histórico do descartado intacto | API + banco |
| AC-IC-006 | P1 | **Dado** o abandono da criação antes de salvar, **Quando** o usuário sai, **Então** nenhum dado é persistido | API + banco |

## 2.2 UC-IC-002 — Ativar Item

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IC-007 | P0 | **Dado** um item Draft completo (dados + CA válido se EPI), **Quando** ativado com motivo, **Então** transiciona para Active, trava campos estruturais (código/grupo/unidade), emite EVT-IC-003, notifica (IC-NOT-001) e registra auditoria com motivo | API + evento + notificação |
| AC-IC-008 | P0 | **Dado** um item EPI sem CA, **Quando** se tenta ativar, **Então** o sistema recusa com IC-ERR-080 e o gap aparece no checklist de completude | API + UI |
| AC-IC-009 | P0 | **Dado** um item EPI com CA vencido, **Quando** se tenta ativar, **Então** o sistema recusa com IC-ERR-081 | API |
| AC-IC-010 | P0 | **Dado** ativação sem motivo, **Quando** submetida, **Então** o sistema recusa com IC-ERR-091, sem mudança de estado | API |
| AC-IC-011 | P0 | **Dado** um item incompleto (sem descrição/unidade válida), **Quando** se tenta ativar, **Então** o sistema recusa com IC-ERR-020 e a lista de gaps retornada corresponde exatamente ao checklist | API |
| AC-IC-012 | P0 | **Dado** uma ativação com `If-Match` desatualizado, **Quando** processada, **Então** o sistema recusa com IC-ERR-409 e nenhum estado é alterado | API |
| AC-IC-013 | P1 | **Dado** um item ativado, **Quando** o evento é consumido, **Então** MMS-003 e MMS-004 passam a enxergá-lo como operável (contrato EVT-IC-003 v1) | evento |

## 2.3 UC-IC-003 — Editar/Enriquecer Item

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IC-014 | P0 | **Dado** um item em Draft, **Quando** qualquer campo é editado, **Então** a alteração é persistida com incremento de versão e EVT-IC-002 | API + evento |
| AC-IC-015 | P0 | **Dado** um item Active, **Quando** se tenta alterar código, grupo ou unidade, **Então** o sistema recusa com IC-ERR-031 | API |
| AC-IC-016 | P0 | **Dado** um sinônimo novo, **Quando** adicionado, **Então** é persistido normalizado (sem acento, caixa baixa) e passa a casar na busca `q` | API + banco |
| AC-IC-017 | P0 | **Dado** um sinônimo já existente para o item (após normalização), **Quando** adicionado novamente, **Então** o sistema recusa com IC-ERR-040 | API |
| AC-IC-018 | P0 | **Dado** um CA informado para item do grupo Fardamento, **Quando** submetido, **Então** o sistema recusa (CA não se aplica) | API |
| AC-IC-019 | P0 | **Dado** uma grade com tamanho fora do SIZE_GRID, **Quando** submetida, **Então** o sistema recusa com IC-ERR-082 | API |
| AC-IC-020 | P0 | **Dado** parâmetros de reposição com mínimo > máximo, **Quando** submetidos, **Então** o sistema recusa com IC-ERR-060 | API |
| AC-IC-021 | P0 | **Dado** uma imagem fora das regras (tipo/tamanho), **Quando** enviada, **Então** o sistema recusa com IC-ERR-083 e nada é persistido | API + storage |
| AC-IC-022 | P0 | **Dado** uma imagem válida, **Quando** enviada, **Então** é armazenada via FD-001-03, o item referencia o anexo, EVT-IC-008 é emitido e o download ocorre somente por URL assinada (≤ 5 min) | API + storage + evento |
| AC-IC-023 | P0 | **Dado** a remoção de CA de item Active, **Quando** tentada, **Então** o sistema recusa (bloqueio IC-BR-081); em Draft/Inactive, a remoção é permitida com auditoria | API + auditoria |
| AC-IC-024 | P1 | **Dado** a atualização de CA, **Quando** persistida, **Então** EVT-IC-006 é emitido e o alerta de vencimento (IC-NOT-005) é reagendado conforme a nova data | evento + notificação |

## 2.4 UC-IC-004 — Inativar Item

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IC-025 | P0 | **Dado** um item Active, **Quando** inativado com motivo, **Então** transiciona para Inactive, emite EVT-IC-004, notifica (IC-NOT-002) e registra auditoria com motivo | API + evento + notificação |
| AC-IC-026 | P0 | **Dado** um item Inactive, **Quando** consumido por nova requisição ou movimentação, **Então** o sistema bloqueia (IC-BR-050) — MMS-003/MMS-004 não o oferecem nem aceitam | API + evento |
| AC-IC-027 | P0 | **Dado** um item Inactive, **Quando** consultado em históricos, saldos e requisições antigas, **Então** todas as referências permanecem íntegras e legíveis | API + banco |
| AC-IC-028 | P0 | **Dado** inativação sem motivo, **Quando** submetida, **Então** o sistema recusa com IC-ERR-091 | API |

## 2.5 UC-IC-005 — Reativar Item

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IC-029 | P0 | **Dado** um item Inactive completo (CA válido se EPI), **Quando** reativado com motivo, **Então** transiciona para Active, emite EVT-IC-005, notifica (IC-NOT-003) e volta a ser operável | API + evento + notificação |
| AC-IC-030 | P0 | **Dado** um item Inactive com CA vencido, **Quando** se tenta reativar, **Então** o sistema recusa com IC-ERR-081 até a renovação do CA | API |
| AC-IC-031 | P0 | **Dado** reativação a partir de estado que não Inactive, **Quando** tentada, **Então** o sistema recusa com IC-ERR-090 (transição inválida) | API |

## 2.6 UC-IC-006 — Descartar Item

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IC-032 | P0 | **Dado** um item nunca consumido (sem requisições, saldos ou movimentações), **Quando** descartado por Admin com motivo, **Então** transiciona para Discarded (terminal), emite EVT-IC-007, notifica (IC-NOT-004), libera o código para reuso e preserva o histórico | API + evento + banco |
| AC-IC-033 | P0 | **Dado** um item já consumido pela operação, **Quando** se tenta descartar, **Então** o sistema recusa com IC-ERR-070 | API |
| AC-IC-034 | P0 | **Dado** um usuário sem `items.admin`, **Quando** tenta descartar, **Então** o sistema recusa com IC-ERR-900 e a tentativa é auditada | API + auditoria |
| AC-IC-035 | P0 | **Dado** um item Discarded, **Quando** qualquer operação de escrita é tentada, **Então** o sistema recusa com IC-ERR-090 (estado terminal) | API |

## 2.7 UC-IC-007 — Consultar e Buscar Itens

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IC-036 | P0 | **Dado** qualquer papel autenticado com `items.read`, **Quando** consulta a lista, **Então** recebe apenas itens da sua empresa (`company_id`), paginados por keyset | API |
| AC-IC-037 | P0 | **Dado** um identificador fora do escopo do usuário, **Quando** acessado diretamente, **Então** o sistema responde 404 (anti-enumeração), nunca 403, e a tentativa é auditável | API + auditoria |
| AC-IC-038 | P0 | **Dado** busca `q` por termo de sinônimo, **Quando** executada, **Então** o item é retornado com `matchedBy = SYNONYM` | API |
| AC-IC-039 | P0 | **Dado** busca `q` com acentos e caixa mista, **Quando** executada, **Então** casa descrição e sinônimos normalizados (IC-BR-042) | API |
| AC-IC-040 | P0 | **Dado** o componente de busca no contexto do solicitante, **Quando** executada, **Então** apenas itens Active são retornados (IC-BR-001) | API |
| AC-IC-041 | P1 | **Dado** filtros combinados (status, grupo, caStatus, sem imagem), **Quando** aplicados, **Então** os resultados respeitam todos os filtros simultaneamente | API |
| AC-IC-042 | P1 | **Dado** a timeline do item, **Quando** consultada, **Então** os marcos aparecem em ordem cronológica conforme FD-001-07, com paginação keyset | API + UI |

---

# 3. Critérios de Regras de Negócio Transversais

| Código | Prioridade | Critério | Origem |
|--------|-----------|----------|--------|
| AC-IC-043 | P0 | **Dado** qualquer operação de escrita, **Quando** executada, **Então** respeita o fluxo de autorização escopo(404) → RBAC → ABAC → delegação(403), com deny by default (POL-IC-AUTH) | MMS-002-09 |
| AC-IC-044 | P0 | **Dado** qualquer alteração em entidade versionada, **Quando** persistida sem `version`/`If-Match` correspondente, **Então** é recusada com IC-ERR-409 (optimistic concurrency) | MMS-002/NFR |
| AC-IC-045 | P0 | **Dado** qualquer exclusão, **Quando** executada, **Então** é lógica (soft delete); nunca ocorre exclusão física | MMS-002/NFR |
| AC-IC-046 | P0 | **Dado** qualquer consulta, **Quando** executada, **Então** aplica filtro obrigatório por `company_id` (multiempresa) | MMS-002-11 |
| AC-IC-047 | P0 | **Dado** qualquer transição de estado, **Quando** ocorre, **Então** segue exclusivamente a State Machine oficial (ST-IC-001..004); nenhuma transição fora dela é possível por nenhum caminho (API, banco, job) | MMS-002-03 |
| AC-IC-048 | P0 | **Dado** 100% das transições e alterações, **Quando** ocorrem, **Então** geram registro de auditoria imutável com correlationId, autor, motivo e versão | MMS-002/NFR + FD-001-06 |
| AC-IC-049 | P0 | **Dado** todo item do grupo EPI em Active, **Quando** verificado, **Então** possui CA válido e não vencido (invariante contínuo, monitorado pela view `vw_epi_sem_ca`) | IC-BR-080/081 |

---

# 4. Critérios de Ciclo de Vida e Estados

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-IC-050 | P0 | **Dado** qualquer ação de ciclo de vida (ativar/inativar/reativar/descartar), **Quando** executada, **Então** exige motivo obrigatório, gera auditoria e emite o evento correspondente — nenhuma ocorre "silenciosamente" |
| AC-IC-051 | P0 | **Dado** um item em qualquer estado, **Quando** consultado, **Então** o status exibido corresponde exatamente ao estado persistido (ST-IC-001..004) |
| AC-IC-052 | P1 | **Dado** transições concorrentes sobre o mesmo item, **Quando** processadas, **Então** apenas uma é aceita (a outra recebe IC-ERR-409) e o estado final é consistente |

---

# 5. Critérios de Eventos e Notificações

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-IC-053 | P0 | **Dado** qualquer evento do módulo (EVT-IC-001..008), **Quando** publicado, **Então** segue o envelope oficial (eventId, eventType, version, aggregateId, occurredAt, correlationId, causationId, actorId, companyId, payload) e é persistido na outbox na mesma transação |
| AC-IC-054 | P0 | **Dado** falha de consumo, **Quando** ocorre, **Então** aplica retry 5x exponencial e, esgotado, envia para `trino.materials.dlq`; consumidores são idempotentes por (eventId, consumerName) |
| AC-IC-055 | P0 | **Dado** os eventos críticos (EVT-IC-003/004/005), **Quando** publicados, **Então** preservam ordenação por aggregateId | 
| AC-IC-056 | P0 | **Dado** qualquer notificação do módulo (IC-NOT-001..006), **Quando** disparada, **Então** é despachada exclusivamente pelo Notification Center (FD-001-05); o módulo nunca notifica diretamente |
| AC-IC-057 | P0 | **Dado** falha total de notificação, **Quando** ocorre, **Então** o fluxo principal do item (ativação, inativação etc.) não é bloqueado |
| AC-IC-058 | P1 | **Dado** um CA a 90/60/30 dias do vencimento, **Quando** o marco é atingido, **Então** IC-NOT-005 é disparado para o mantenedor conforme agendamento (não em tempo de request) |

---

# 6. Critérios de API

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-IC-059 | P0 | **Dado** qualquer resposta da API, **Quando** emitida, **Então** usa o envelope oficial (sucesso: data/page/correlationId; erro: error{code,message,details,correlationId}) |
| AC-IC-060 | P0 | **Dado** qualquer listagem, **Quando** paginada, **Então** usa keyset com cursor assinado de 15 min; parâmetros de offset são rejeitados; cursor inválido/expirado retorna IC-ERR-400 |
| AC-IC-061 | P0 | **Dado** requisição de escrita com `Idempotency-Key` repetida em 24h, **Quando** recebida, **Então** retorna o resultado original (idempotentReplay) sem reexecutar efeitos |
| AC-IC-062 | P0 | **Dado** PATCH/PUT/DELETE sem `If-Match` em recurso versionado, **Quando** recebido, **Então** é recusado com IC-ERR-409 |
| AC-IC-063 | P0 | **Dado** payload com campos fora da allowlist (mass assignment), **Quando** recebido, **Então** os campos são rejeitados/ignorados e campos calculados (`status`, `version`, timestamps) permanecem read-only |
| AC-IC-064 | P1 | **Dado** cliente que excede o rate limit (600/120/60 req-min conforme classe), **Quando** excede, **Então** recebe 429 com `Retry-After` e corpo IC-ERR-429 |
| AC-IC-065 | P0 | **Dado** o endpoint de completude, **Quando** consultado, **Então** retorna exatamente os gaps e avisos que a ativação validaria (fonte única, sem duplicação de regra) |

---

# 7. Critérios de UX e Wireframes (incorporados por referência)

| Origem | Incorporação |
|--------|--------------|
| CA-UX-IC-01 a CA-UX-IC-09 (MMS-002-14, seção 13) | Obrigatórios no MVP; verificação conforme TC-UX-IC-01 a TC-UX-IC-07 (MMS-002-17) |
| CA-WF-IC-01 a CA-WF-IC-06 (MMS-002-15, seção 10) | Obrigatórios na entrega das telas; verificação por revisão cruzada e testes de UI |

Critérios adicionais de integração UI×API:

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-IC-066 | P0 | **Dado** qualquer erro de API, **Quando** exibido na interface, **Então** a mensagem apresentada corresponde ao catálogo documentado (MMS-002-07/13) e o código IC-ERR é preservado para suporte |
| AC-IC-067 | P0 | **Dado** qualquer ação indisponível pelo papel do usuário, **Quando** a tela renderiza, **Então** a ação é ocultada, nunca apenas desabilitada (CA-UX-IC-03) |

---

# 8. Critérios Não Funcionais

| Código | Prioridade | Critério | Origem |
|--------|-----------|----------|--------|
| AC-IC-068 | P0 | **Dado** qualquer listagem/busca paginada, **Quando** medida sob carga de referência, **Então** responde em ≤ 500 ms no percentil 95 (detalhe ≤ 300 ms) | MMS-002-13/NFR |
| AC-IC-069 | P0 | **Dado** qualquer operação, **Quando** trafegada, **Então** carrega correlationId ponta a ponta (requisição → evento → notificação → auditoria) | Observabilidade |
| AC-IC-070 | P0 | **Dado** a superfície do módulo, **Quando** submetida à revisão de segurança, **Então** atende SEC-001 e SEC-003 sem pendências críticas | Segurança |
| AC-IC-071 | P1 | **Dado** as notificações do módulo, **Quando** renderizadas, **Então** apresentam conteúdo no idioma do usuário (pt-BR/en-US) | Internacionalização |
| AC-IC-072 | P1 | **Dado** a MV `mv_item_status_summary`, **Quando** consultada, **Então** reflete os totais por status com defasagem documentada e refresh agendado | MMS-002-11 |

---

# 9. Matriz de Rastreabilidade (Resumo)

| Fonte | Cobertura neste documento |
|-------|---------------------------|
| UC-IC-001 a UC-IC-007 | AC-IC-001 a AC-IC-042 |
| Business Rules (MMS-002-02) | Cobertas via UCs + AC-IC-043 a AC-IC-049 (cada regra obrigatória tem ao menos um AC P0) |
| State Machine (MMS-002-03) | AC-IC-047, AC-IC-050 a AC-IC-052 |
| Event Storming (MMS-002-05) | AC-IC-053 a AC-IC-055 |
| Permissions (MMS-002-09) | AC-IC-002, AC-IC-034, AC-IC-037, AC-IC-043 |
| Notifications (MMS-002-10) | AC-IC-056 a AC-IC-058, AC-IC-071 |
| Database (MMS-002-11) | AC-IC-005, AC-IC-046, AC-IC-049, AC-IC-072 |
| API (MMS-002-13) | AC-IC-059 a AC-IC-065 |
| UX/Wireframes (MMS-002-14/15) | CA-UX-IC-*, CA-WF-IC-* + AC-IC-066, AC-IC-067 |
| NFR (MMS-002 README) | AC-IC-044 a AC-IC-048, AC-IC-068 a AC-IC-070 |

A matriz completa **regra × UC × API × AC × teste** é preenchida no MMS-002-17 (Test Scenarios), que referencia cada AC-IC em seus cenários — requisito do DoD (item 4).

---

# 10. Regras de Aceitação

1. Todo critério **P0** deve ter verificação automatizada ou evidência formal registrada antes do release.
2. Critérios **P1** com exceção tolerada exigem registro da exceção, dono e prazo de regularização.
3. Critérios **P2** não bloqueiam o MVP, mas bloqueiam a v1.1.
4. Nenhum critério pode ser alterado após o início da implementação sem atualização deste documento e nova aprovação (conflito → documentação vence).
5. Falha em critério de segurança (AC-IC-070) bloqueia o release independentemente das demais verificações.

---

# 11. Alinhamento com o DoD do Módulo

Este documento atende aos itens dos **Critérios de Conclusão do Módulo (DoD)** do MMS-002 README:

| Item DoD | Atendimento |
|----------|-------------|
| 1. Documentos MMS-002-01 a 17 Approved | MMS-002-16 é o penúltimo; falta MMS-002-17 |
| 2/3. Cobertura de testes e regras obrigatórias com teste | Cada AC-IC referencia a fonte; a automação é especificada no MMS-002-17 |
| 4. Matriz de rastreabilidade 100% | Estruturada na seção 9; completada no MMS-002-17 |
| 5. Revisão de segurança | AC-IC-070 |
| 6. Eventos validados | AC-IC-053 a AC-IC-055 |
| 7. Migrations reversíveis | Verificado na implementação contra MMS-002-11 |

---

# 12. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | AC-IC-001 a AC-IC-072 + CA-UX-IC/CA-WF-IC por referência |
| **v1.1** | Critérios de importação em lote do ERP, exportação CSV e sugestão de duplicatas; promoção dos P2 |
| **v2.0** | Critérios de painel de higiene do catálogo, webhooks e bulk lifecycle (MMS-002-13/14, roadmaps) |

---

# 13. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação dos Acceptance Criteria do módulo: 72 critérios formais em Given/When/Then cobrindo UCs, regras transversais, ciclo de vida, eventos, notificações, API, UX e NFRs, com prioridades P0/P1/P2, matriz de rastreabilidade e alinhamento ao DoD |
