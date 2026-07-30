**Documento:** PR-001-16 — Acceptance Criteria
**Módulo:** PR-001 — Purchase Requisition (Solicitação de Compra)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** PR-001 (Visão do Módulo — NFR e DoD), PR-001-02 (Business Rules), PR-001-03 (State Machine), PR-001-05 (Event Storming), PR-001-07 (Use Cases), PR-001-09 (Permissions), PR-001-10 (Notifications), PR-001-11 (Database Model), PR-001-13 (API), PR-001-14 (UX), PR-001-15 (Wireframes)
**Referências:** SEC-001, SEC-003, FD-001 (Foundation), GOV-001

---

# 1. Objetivo

Este documento consolida os **critérios de aceite formais** do módulo Purchase Requisition em formato verificável, servindo como contrato de aceitação entre produto, engenharia e qualidade, e como insumo direto para o **PR-001-17 — Test Scenarios**.

Regras de vínculo:

- Todo critério deste documento deriva de uma fonte documentada (UC, regra de negócio, state machine, permissão, evento, API, UX, wireframe ou NFR). Nenhum critério inventa comportamento.
- Critérios já definidos em documentos anteriores (CA-UX-*, CA-WF-*) são **referenciados, não reescritos**; este documento os incorpora por citação.
- A aprovação do módulo exige 100% dos critérios **P0** verificados (seção 11 — DoD).

## 1.1 Convenções

- **Formato:** Given/When/Then (Dado/Quando/Então).
- **Identificação:** `AC-PR-NNN`, sequencial, imutável após publicação.
- **Prioridade:**
  - **P0** — bloqueante para o MVP; falha impede release;
  - **P1** — obrigatório no MVP, com cenário de exceção tolerado e documentado;
  - **P2** — desejável no MVP, obrigatório na v1.1.
- **Verificação:** cada critério indica a camada de verificação esperada (API, UI, banco, evento, auditoria). A automação é especificada no PR-001-17.

---

# 2. Critérios Funcionais por Caso de Uso

## 2.1 UC-001 — Criar Solicitação de Compra

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-001 | P0 | **Dado** um usuário com permissão de criação (Requester/Admin) e centro de custo ativo, **Quando** cria a solicitação com campos válidos, **Então** o sistema persiste em estado Draft, gera número único sequencial por empresa, emite EVT-001 e exibe MSG-UC-001-A | API + evento + UI |
| AC-PR-002 | P0 | **Dado** um usuário sem permissão de criação, **Quando** tenta criar, **Então** o sistema recusa com PR-ERR-001, registra auditoria de negação e não persiste nenhum dado | API + auditoria |
| AC-PR-003 | P0 | **Dado** um centro de custo inativo ou inválido para a unidade, **Quando** é informado, **Então** o sistema recusa com PR-ERR-021 e MSG-UC-001-B | API + UI |
| AC-PR-004 | P1 | **Dado** o cancelamento da criação, **Quando** o usuário abandona antes de salvar, **Então** nenhum dado é persistido | API + banco |
| AC-PR-005 | P0 | **Dado** duas criações concorrentes na mesma empresa, **Quando** concluídas, **Então** os números gerados são únicos e sequenciais, sem duplicidade nem salto indevido | banco |

## 2.2 UC-002 — Adicionar Item

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-006 | P0 | **Dado** uma requisição em Draft, **Quando** um item válido é adicionado, **Então** o item é persistido com sequência incremental, o total é recalculado e EVT-003 é emitido | API + evento |
| AC-PR-007 | P0 | **Dado** quantidade igual a zero, **Quando** o item é submetido, **Então** o sistema recusa com PR-ERR-010 e MSG-UC-002-A ancorada no campo | API + UI |
| AC-PR-008 | P0 | **Dado** uma requisição fora de estado editável (ex.: Submitted), **Quando** se tenta adicionar item, **Então** o sistema recusa com PR-ERR-040 e MSG-UC-002-C | API |
| AC-PR-009 | P1 | **Dado** item sem centro de custo próprio, **Quando** adicionado, **Então** herda o centro de custo do cabeçalho | API + banco |
| AC-PR-010 | P1 | **Dado** três itens adicionados em sequência, **Quando** consultados, **Então** as sequências são 1, 2 e 3 | API |

## 2.3 UC-003 — Enviar para Aprovação

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-011 | P0 | **Dado** uma requisição válida em Draft, **Quando** enviada, **Então** transiciona para Submitted, emite EVT-005/006/007/008, instancia a cadeia de aprovação, notifica o primeiro nível e exibe MSG-UC-003-A | API + evento + notificação |
| AC-PR-012 | P0 | **Dado** uma requisição sem itens, **Quando** enviada, **Então** o sistema recusa com PR-ERR-020 e lista a pendência em MSG-UC-003-B | API + UI |
| AC-PR-013 | P0 | **Dado** ausência de workflow configurado, **Quando** enviada, **Então** ocorre EXC-001, o Administrador é notificado e MSG-UC-003-C é exibida; a requisição não avança | evento + notificação + UI |
| AC-PR-014 | P0 | **Dado** uma requisição em Returned reenviada, **Quando** aceita, **Então** o ciclo é incrementado e uma nova cadeia é instanciada, preservando o histórico | API + evento |
| AC-PR-015 | P0 | **Dado** um envio com `If-Match` desatualizado, **Quando** processado, **Então** o sistema recusa com PR-ERR-409 (conflito de versão) e nenhum estado é alterado | API |
| AC-PR-016 | P1 | **Dado** falha de validação no envio, **Quando** registrada, **Então** EVT-011 é emitido com a lista de pendências | evento |

## 2.4 UC-004 — Aprovar Solicitação

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-017 | P0 | **Dado** um aprovador do nível pendente de workflow de nível único, **Quando** aprova, **Então** a requisição transiciona para Approved, emite EVT-009, entra na fila de Compras e MSG-UC-004-A é exibida | API + evento |
| AC-PR-018 | P0 | **Dado** workflow multinível, **Quando** um nível intermediário aprova, **Então** o próximo nível é ativado e notificado, e MSG-UC-004-B é exibida | API + notificação |
| AC-PR-019 | P0 | **Dado** o solicitante tentando aprovar a própria requisição (SoD, ABAC-01), **Quando** tenta, **Então** ocorre EXC-006 com PR-ERR-041, a decisão é bloqueada e a tentativa é auditada | API + auditoria |
| AC-PR-020 | P0 | **Dado** aprovador com alçada insuficiente (ABAC-02), **Quando** tenta aprovar, **Então** o sistema recusa com PR-ERR-042 e aplica o escalonamento previsto no workflow | API + evento |
| AC-PR-021 | P1 | **Dado** uma delegação vigente, **Quando** o delegado aprova, **Então** a decisão é registrada com `delegatedBy` na auditoria | auditoria |
| AC-PR-022 | P1 | **Dado** nível paralelo ANY, **Quando** o primeiro aprovador decide, **Então** o nível é concluído e as demais atribuições pendentes são canceladas | evento + banco |

## 2.5 UC-005 — Rejeitar Solicitação

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-023 | P0 | **Dado** um aprovador do nível pendente, **Quando** rejeita com motivo, **Então** a requisição transiciona para Rejected, emite EVT-010, encerra os níveis pendentes e o solicitante é notificado | API + evento + notificação |
| AC-PR-024 | P0 | **Dado** tentativa de rejeição sem motivo, **Quando** submetida, **Então** o sistema recusa com PR-ERR-031 e MSG-UC-005-B, sem mudança de estado | API + UI |
| AC-PR-025 | P0 | **Dado** uma requisição em estado final, **Quando** se tenta rejeitar, **Então** o sistema recusa com PR-ERR-040 | API |
| AC-PR-026 | P1 | **Dado** uma requisição rejeitada, **Quando** consultada, **Então** permanece visível em somente leitura com o motivo destacado e não pode ser editada | UI + API |

## 2.6 UC-006 — Retornar para Ajustes

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-027 | P0 | **Dado** um aprovador do nível pendente, **Quando** retorna com descrição dos ajustes, **Então** a requisição transiciona para Returned, emite EVT-011 e o solicitante é notificado | API + evento + notificação |
| AC-PR-028 | P0 | **Dado** retorno sem descrição dos ajustes, **Quando** submetido, **Então** o sistema recusa com PR-ERR-031 e MSG-UC-006-B | API + UI |
| AC-PR-029 | P0 | **Dado** uma requisição em Returned, **Quando** o solicitante a edita, **Então** somente os campos liberados pelo UC-006 são alteráveis e o motivo do retorno permanece visível | UI + API |

## 2.7 UC-007 — Cancelar Solicitação

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-030 | P0 | **Dado** usuário autorizado e requisição em estado cancelável conforme `pr.cancel.policy`, **Quando** cancela com confirmação e motivo, **Então** transiciona para Cancelled, emite o evento correspondente e registra auditoria | API + evento + auditoria |
| AC-PR-031 | P0 | **Dado** requisição em estado não cancelável ou usuário sem política, **Quando** tenta cancelar, **Então** o sistema recusa conforme POL-AUTH-004 e a negação é auditada | API + auditoria |

## 2.8 UC-008 — Consultar Requisições

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-032 | P0 | **Dado** qualquer papel autenticado, **Quando** consulta a lista, **Então** recebe apenas registros dentro do seu escopo organizacional e de titularidade (ABAC-05), paginados por keyset | API |
| AC-PR-033 | P0 | **Dado** um identificador fora do escopo do usuário, **Quando** acessado diretamente, **Então** o sistema responde 404 (anti-enumeração), nunca 403, e a tentativa é auditável | API + auditoria |
| AC-PR-034 | P1 | **Dado** filtros combinados (status, período, centro de custo, solicitante, texto), **Quando** aplicados, **Então** os resultados respeitam todos os filtros simultaneamente | API |

## 2.9 UC-009 — Visualizar Timeline

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-035 | P0 | **Dado** usuário com acesso à requisição, **Quando** abre a timeline, **Então** os marcos são exibidos em ordem cronológica conforme FD-001-07, com paginação keyset | API + UI |
| AC-PR-036 | P1 | **Dado** entradas com visibilidade restrita (ACTOR_ONLY/ROLE_SCOPED/ADMIN_ONLY), **Quando** consultadas por usuário sem enquadramento, **Então** são omitidas da projeção | API |

## 2.10 UC-010 — Anexar Documento

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-037 | P0 | **Dado** usuário com permissão de edição, **Quando** anexa um arquivo dentro das regras do FD-001-03 (tipo/tamanho), **Então** o anexo é associado à requisição e o download ocorre somente por URL assinada (≤ 5 min) | API + storage |
| AC-PR-038 | P0 | **Dado** arquivo fora das regras, **Quando** enviado, **Então** o sistema recusa com a mensagem correspondente do FD-001-03 e nada é persistido | API |

## 2.11 UC-011 — Inserir Comentário

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-PR-039 | P0 | **Dado** usuário com acesso à requisição, **Quando** comenta, **Então** o comentário é persistido via FD-001-08 com sanitização server-side e aparece na aba Comentários | API + UI |
| AC-PR-040 | P1 | **Dado** comentário interno, **Quando** consultado por papel fora de {Approver, Buyer, Manager, Admin}, **Então** não é exibido (POL-AUTH-005) | API + UI |
| AC-PR-041 | P1 | **Dado** menção a usuário sem acesso à entidade, **Quando** inserida, **Então** é bloqueada pelo FD-001-08 e nunca concede acesso | API |

## 2.12 UC-012 a UC-014 (Roadmap v1.1)

Critérios de aceite de **duplicação (UC-012)**, **anexos por item (UC-013)** e **exportação de detalhe (UC-014)** serão adicionados a este documento na revisão v1.1, junto com os wireframes correspondentes (PR-001-15, seção 12). Não fazem parte do escopo P0 do MVP.

---

# 3. Critérios de Regras de Negócio Transversais

| Código | Prioridade | Critério | Origem |
|--------|-----------|----------|--------|
| AC-PR-042 | P0 | **Dado** qualquer operação de escrita, **Quando** executada, **Então** respeita o fluxo de autorização escopo(404) → RBAC → ABAC → delegação(403), com deny by default (POL-AUTH-007) | PR-001-09 |
| AC-PR-043 | P0 | **Dado** qualquer alteração em entidade versionada, **Quando** persistida sem `version`/`If-Match` correspondente, **Então** é recusada (optimistic concurrency) | PR-001/NFR |
| AC-PR-044 | P0 | **Dado** qualquer exclusão, **Quando** executada, **Então** é lógica (soft delete); nunca ocorre exclusão física | PR-001/NFR |
| AC-PR-045 | P0 | **Dado** qualquer consulta, **Quando** executada, **Então** aplica filtro obrigatório por `company_id` (multiempresa) | PR-001/NFR |
| AC-PR-046 | P0 | **Dado** qualquer transição de estado, **Quando** ocorre, **Então** segue exclusivamente a State Machine oficial; nenhuma transição fora dela é possível por nenhum caminho (API, banco, job) | PR-001-03 |
| AC-PR-047 | P0 | **Dado** 100% das transições e alterações, **Quando** ocorrem, **Então** geram registro de auditoria imutável com correlationId | PR-001/NFR + FD-001-06 |

---

# 4. Critérios de Workflow e Estados

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-PR-048 | P0 | **Dado** uma submissão, **Quando** a cadeia é instanciada, **Então** a definição de workflow aplicada é a pinada no momento da instanciação (mudanças posteriores de configuração não afetam a cadeia em andamento) |
| AC-PR-049 | P0 | **Dado** uma requisição em qualquer estado, **Quando** consultada, **Então** existe no máximo uma instância de workflow ativa associada |
| AC-PR-050 | P1 | **Dado** um nível com timer de SLA, **Quando** o prazo expira, **Então** os timers e escalonamentos documentados (WF-TMR/WF-ESC) são disparados e notificados |
| AC-PR-051 | P1 | **Dado** uma rejeição ou cancelamento, **Quando** ocorre, **Então** as compensações documentadas (WF-COMP) encerram atribuições e timers pendentes |

---

# 5. Critérios de Eventos e Notificações

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-PR-052 | P0 | **Dado** qualquer evento de negócio do módulo, **Quando** publicado, **Então** segue o envelope oficial (eventId, eventType, version, aggregateId, occurredAt, correlationId, causationId, actorId, companyId, payload) e é persistido na outbox na mesma transação |
| AC-PR-053 | P0 | **Dado** falha de consumo, **Quando** ocorre, **Então** aplica retry 5x exponencial e, esgotado, envia para `trino.procurement.dlq`; consumidores são idempotentes por (eventId, consumerName) |
| AC-PR-054 | P0 | **Dado** qualquer notificação do módulo, **Quando** disparada, **Então** é despachada exclusivamente pelo Notification Center (FD-001-05); o módulo nunca notifica diretamente |
| AC-PR-055 | P0 | **Dado** falha total de notificação, **Quando** ocorre, **Então** o fluxo principal da requisição não é bloqueado (NFR Disponibilidade) |
| AC-PR-056 | P1 | **Dado** destinatário com preferências configuradas, **Quando** notificado, **Então** canal e idioma respeitam as preferências dentro dos limites da organização |

---

# 6. Critérios de API

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-PR-057 | P0 | **Dado** qualquer resposta da API, **Quando** emitida, **Então** usa o envelope oficial (sucesso: data/page/correlationId; erro: error{code,message,details,correlationId}) |
| AC-PR-058 | P0 | **Dado** qualquer listagem, **Quando** paginada, **Então** usa keyset (created_at, id) DESC com cursor assinado de 15 min; parâmetros de offset são rejeitados |
| AC-PR-059 | P0 | **Dado** requisição de escrita com `Idempotency-Key` repetida em 24h, **Quando** recebida, **Então** retorna o resultado original (idempotentReplay) sem reexecutar efeitos |
| AC-PR-060 | P0 | **Dado** PATCH/DELETE sem `If-Match`, **Quando** recebido, **Então** é recusado com PR-ERR-060 |
| AC-PR-061 | P0 | **Dado** payload com campos fora da allowlist (mass assignment), **Quando** recebido, **Então** os campos são rejeitados/ignorados e campos calculados permanecem read-only |
| AC-PR-062 | P1 | **Dado** cliente que excede o rate limit (600/120/60 req-min conforme classe), **Quando** excede, **Então** recebe 429 com cabeçalhos de retry |

---

# 7. Critérios de UX e Wireframes (incorporados por referência)

| Origem | Incorporação |
|--------|--------------|
| CA-UX-01 a CA-UX-08 (PR-001-14, seção 13) | Obrigatórios no MVP; verificação conforme TC-UX-01 a TC-UX-06 (PR-001-17) |
| CA-WF-01 a CA-WF-05 (PR-001-15, seção 11) | Obrigatórios na entrega das telas; verificação por revisão cruzada e testes de UI |

Critério adicional de integração UI×API:

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-PR-063 | P0 | **Dado** qualquer erro de API, **Quando** exibido na interface, **Então** a mensagem apresentada corresponde ao catálogo MSG-UC (ou chave i18n derivada) e o código PR-ERR é preservado para suporte |
| AC-PR-064 | P0 | **Dado** qualquer ação indisponível pelo papel do usuário, **Quando** a tela renderiza, **Então** a ação é ocultada, nunca apenas desabilitada (CA-UX-03) |

---

# 8. Critérios Não Funcionais

| Código | Prioridade | Critério | Origem (PR-001/NFR) |
|--------|-----------|----------|---------------------|
| AC-PR-065 | P0 | **Dado** qualquer listagem paginada, **Quando** medida sob carga de referência, **Então** responde em < 2s no percentil 95 | Performance |
| AC-PR-066 | P0 | **Dado** qualquer operação, **Quando** trafegada, **Então** carrega correlationId ponta a ponta (requisição → evento → notificação → auditoria) | Observabilidade |
| AC-PR-067 | P0 | **Dado** a superfície do módulo, **Quando** submetida à revisão de segurança, **Então** atende SEC-001 e SEC-003 sem pendências críticas | Segurança |
| AC-PR-068 | P1 | **Dado** as notificações do módulo, **Quando** renderizadas, **Então** apresentam conteúdo no idioma do usuário | Internacionalização |

---

# 9. Matriz de Rastreabilidade (Resumo)

| Fonte | Cobertura neste documento |
|-------|---------------------------|
| UC-001 a UC-011 | AC-PR-001 a AC-PR-041 |
| UC-012 a UC-014 | Roadmap v1.1 (seção 2.12) |
| Business Rules (PR-001-02) | Cobertas via UCs + AC-PR-042 a AC-PR-047 (cada regra `Obrigatória` tem ao menos um AC P0) |
| State Machine (PR-001-03) | AC-PR-046, AC-PR-048 a AC-PR-051 |
| Event Storming (PR-001-05) | AC-PR-052, AC-PR-053 |
| Permissions (PR-001-09) | AC-PR-002, AC-PR-019, AC-PR-020, AC-PR-031, AC-PR-033, AC-PR-040, AC-PR-042 |
| Notifications (PR-001-10) | AC-PR-054 a AC-PR-056, AC-PR-068 |
| API (PR-001-13) | AC-PR-057 a AC-PR-062 |
| UX/Wireframes (PR-001-14/15) | CA-UX-*, CA-WF-* + AC-PR-063, AC-PR-064 |
| NFR (PR-001 README) | AC-PR-043 a AC-PR-047, AC-PR-065 a AC-PR-067 |

A matriz completa **regra × UC × API × AC × teste** é preenchida no PR-001-17 (Test Scenarios), que referencia cada AC-PR em seus cenários — requisito do DoD (item 4).

---

# 10. Regras de Aceitação

1. Todo critério **P0** deve ter verificação automatizada ou evidência formal registrada antes do release.
2. Critérios **P1** com exceção tolerada exigem registro da exceção, dono e prazo de regularização.
3. Critérios **P2** não bloqueiam o MVP, mas bloqueiam a v1.1.
4. Nenhum critério pode ser alterado após o início da implementação sem atualização deste documento e nova aprovação (conflito → documentação vence).
5. Falha em critério de segurança (AC-PR-067) bloqueia o release independentemente das demais verificações.

---

# 11. Alinhamento com o DoD do Módulo

Este documento atende aos itens do **Critérios de Conclusão do Módulo (DoD)** do PR-001 README:

| Item DoD | Atendimento |
|----------|-------------|
| 1. Documentos PR-001-01 a 17 Approved | PR-001-16 é o penúltimo; falta PR-001-17 |
| 2/3. Cobertura de testes e regras obrigatórias com teste | Cada AC-PR referencia a fonte; a automação é especificada no PR-001-17 |
| 4. Matriz de rastreabilidade 100% | Estruturada na seção 9; completada no PR-001-17 |
| 5. Revisão de segurança | AC-PR-067 |
| 6. Eventos validados | AC-PR-052, AC-PR-053 |
| 7. Migrations reversíveis | Verificado na implementação contra PR-001-11 |

---

# 12. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | AC-PR-001 a AC-PR-068 + CA-UX/CA-WF por referência |
| **v1.1** | Critérios de UC-012, UC-013 e UC-014; promoção dos P2 |
| **v2.0** | Critérios de fila de Compras enriquecida, webhooks e bulk-approve (PR-001-13, roadmap) |

---

# 13. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação dos Acceptance Criteria do módulo: 68 critérios formais em Given/When/Then cobrindo UCs, regras transversais, workflow, eventos, notificações, API, UX e NFRs, com prioridades P0/P1/P2, matriz de rastreabilidade e alinhamento ao DoD |
