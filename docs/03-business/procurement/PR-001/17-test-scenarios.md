**Documento:** PR-001-17 — Test Scenarios
**Módulo:** PR-001 — Purchase Requisition (Solicitação de Compra)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** PR-001 (Visão do Módulo — DoD), PR-001-02 (Business Rules), PR-001-03 (State Machine), PR-001-05 (Event Storming), PR-001-07 (Use Cases), PR-001-09 (Permissions), PR-001-10 (Notifications), PR-001-11 (Database Model), PR-001-13 (API), PR-001-14 (UX), PR-001-15 (Wireframes), PR-001-16 (Acceptance Criteria)
**Referências:** SEC-001, SEC-003, FD-001 (Foundation), ADR-010, GOV-001

---

# 1. Objetivo

Este documento é o **catálogo oficial de cenários de teste** do módulo Purchase Requisition. Ele transforma os 68 critérios de aceite do PR-001-16 em cenários executáveis, fecha a **matriz de rastreabilidade regra × UC × API × AC × teste** exigida pelo DoD (item 4) e define a estratégia de automação, dados, ambientes e evidências.

Regras de vínculo:

- Nenhum cenário inventa comportamento: todo cenário deriva de fonte documentada (regra, UC, state machine, evento, permissão, API, UX, wireframe ou NFR).
- Cenários já listados nos casos de uso (TC-UC-xxx-y, PR-001-07) são **incorporados e formalizados** aqui, com mapeamento para os AC-PR — o PR-001-07 permanece como origem do resumo funcional.
- Todo critério **P0** possui ao menos um cenário automatizável; toda regra `Obrigatória` possui ao menos um cenário de teste automatizado associado (DoD itens 2 e 3).

---

# 2. Convenções

## 2.1 Identificação dos cenários

| Prefixo | Domínio do cenário | Origem principal |
|---------|-------------------|------------------|
| `TC-001` a `TC-006` | Regras Gerais PR-BR-001..006 (códigos legados, preservados) | PR-001-02, seção 4 |
| `TC-UC-xxx-y` | Cenário funcional do caso de uso UC-xxx | PR-001-07 |
| `TC-TRV-nnn` | Regras transversais (autorização, concorrência, soft delete, multiempresa, state machine, auditoria) | PR-001-16, seção 3 |
| `TC-WF-nnn` | Workflow e estados | PR-001-03, PR-001-16 seção 4 |
| `TC-EVT-nnn` | Eventos e mensageria | PR-001-05, ADR-010 |
| `TC-NTF-nnn` | Notificações | PR-001-10, FD-001-05 |
| `TC-API-nnn` | Contrato de API | PR-001-13 |
| `TC-UX-nn` | Experiência de usuário (CA-UX) | PR-001-14 |
| `TC-WFR-nnn` | Fidelidade a wireframes (CA-WF) | PR-001-15 |
| `TC-UI-nnn` | Integração UI × API | PR-001-16, seção 7 |
| `TC-SEC-nnn` | Segurança | SEC-001, SEC-003 |
| `TC-PRF-nnn` | Performance e observabilidade | PR-001/NFR |
| `TC-DB-nnn` | Banco de dados e migrations | PR-001-11 |

Códigos são imutáveis após publicação; cenários novos recebem o próximo sequencial do prefixo.

## 2.2 Camadas de execução

| Camada | Sigla | Ferramenta-alvo | Escopo |
|--------|-------|-----------------|--------|
| Unidade | UNIT | xUnit (.NET) / Vitest (frontend) | Regras de domínio, factories, specifications, value objects |
| Integração | INT | xUnit + Testcontainers (PostgreSQL, Redis, RabbitMQ, MinIO) | Repositórios, outbox, triggers, constraints |
| Contrato/API | API | Testes de integração HTTP (WebApplicationFactory) | Endpoints, envelope, erros, headers, idempotência |
| Mensageria | MSG | Testcontainers RabbitMQ | Retry, DLQ, idempotência de consumidor, ordem |
| Interface | UI | Playwright | Telas, fluxos de usuário, estados de tela, acessibilidade |
| End-to-end | E2E | Playwright + ambiente integrado | Jornadas completas cross-módulo |
| Performance | PERF | k6 | Carga de referência, percentis |
| Segurança | SEC | ZAP/Burp + testes automatizados | OWASP ASVS, revisão SEC-001/003 |

## 2.3 Automação e prioridade

- **Automatizado obrigatório:** todo cenário vinculado a AC **P0** e toda regra `Obrigatória` (DoD item 3).
- **Automatizado desejável:** cenários de AC **P1**; exceções toleradas exigem registro com dono e prazo (PR-001-16, seção 10).
- **Manual com evidência formal:** revisões cruzadas (CA-UX-01/02, CA-WF), auditoria de acessibilidade assistida e revisão de segurança — evidência anexada ao release.
- Prioridade do cenário = prioridade do AC vinculado (P0/P1/P2).

## 2.4 Template de especificação

Cada cenário é executado com: **objetivo → pré-condições → dados de teste → passos → resultado esperado → AC/regra vinculada → camada → automação**. As tabelas deste documento trazem a forma resumida (objetivo + vínculos); a forma expandida é gerada no repositório de testes a partir deste catálogo, sem divergir dele.

---

# 3. Estratégia de Testes

## 3.1 Pirâmide

| Nível | Participação esperada | Conteúdo |
|-------|----------------------|----------|
| UNIT/INT | ~60% | Domínio, regras, repositórios, constraints |
| API/MSG | ~30% | Contratos, eventos, notificações, segurança de API |
| UI/E2E | ~10% | Jornadas críticas, UX, telas |

## 3.2 Ambientes

| Ambiente | Uso |
|----------|-----|
| Local (Testcontainers) | UNIT, INT, API, MSG em pipeline de PR |
| QA integrado | UI, E2E, MSG ponta a ponta, i18n |
| Staging (espelho) | PERF, SEC, revisão de release, migrations reversíveis |

## 3.3 Dados de teste

- **Massa sintética versionada** no repositório de testes: 2 empresas (isolamento multiempresa), 3 unidades, 5 centros de custo (1 inativo), 6 usuários (Requester, Approver nível 1, Approver nível 2, Buyer, Manager, Admin) + 1 sem permissões, 1 delegação vigente e 1 expirada, workflows de 1 e 2 níveis (sequencial e paralelo ANY), catálogo de 20 itens.
- **Proibição:** dados reais ou pseudonimizados de produção em qualquer ambiente de teste (LGPD, SEC-001).
- Reset por cenário via transação ou seed idempotente; nenhum cenário depende de estado deixado por outro.

## 3.4 Critérios de entrada e saída

- **Entrada:** build verde, migrations aplicadas, massa sintética carregada, secrets de teste no vault de CI.
- **Saída (release):** 100% dos cenários P0 automatizados e verdes; exceções P1 registradas; evidências formais anexadas; revisão SEC sem pendências críticas (AC-PR-067).

---

# 4. Cenários das Regras Gerais (TC-001 a TC-006)

Códigos legados definidos no PR-001-02 (seção 4) e referenciados pelo DoD. Cada um cobre uma regra `Obrigatória` da seção 4 do PR-001-02.

| Cenário | Objetivo | Regra | AC vinculado | Camada | Automação |
|---------|----------|-------|--------------|--------|-----------|
| TC-001 | Identificador único: UUID na criação e número sequencial único por empresa (constraint) | PR-BR-001 | AC-PR-001, AC-PR-005 | UNIT + INT | Automatizado (P0) |
| TC-002 | Empresa obrigatória, existente, ativa e autorizada; recusa com PR-ERR-002 | PR-BR-002 | AC-PR-002, AC-PR-045 | API | Automatizado (P0) |
| TC-003 | Unidade obrigatória e consistente com a empresa; centro de custo ativo para a unidade | PR-BR-003 | AC-PR-003 | API | Automatizado (P0) |
| TC-004 | Solicitante registrado a partir do usuário autenticado; nunca informado por payload | PR-BR-004 | AC-PR-001, AC-PR-061 | API | Automatizado (P0) |
| TC-005 | Data de criação gerada pelo servidor (UTC), imutável | PR-BR-005 | AC-PR-001, AC-PR-043 | INT | Automatizado (P0) |
| TC-006 | Estado inicial fixo Draft na factory do Aggregate | PR-BR-006 | AC-PR-001, AC-PR-046 | UNIT | Automatizado (P0) |

---

# 5. Cenários por Caso de Uso (TC-UC)

Origem: PR-001-07 (resumo funcional). Aqui cada cenário é vinculado ao seu AC (PR-001-16). Cenários marcados com ★ foram **acrescentados** neste documento para fechar a cobertura de ACs sem cenário correspondente — não alteram nenhum UC.

## 5.1 UC-001 — Criar Solicitação de Compra

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-001-1 | Criação com todos os campos válidos → Draft + número único + EVT-001 + MSG-UC-001-A | AC-PR-001 | API + UI |
| TC-UC-001-2 | Criação sem permissão → PR-ERR-001 + auditoria de negação, nada persistido | AC-PR-002 | API |
| TC-UC-001-3 | Centro de custo inativo/inválido → PR-ERR-021 + MSG-UC-001-B | AC-PR-003 | API + UI |
| TC-UC-001-4 | Abandono antes de salvar → nenhum dado persistido | AC-PR-004 | API + INT |
| TC-UC-001-5 | Duas criações concorrentes na mesma empresa → números únicos e sequenciais | AC-PR-005 | INT |

## 5.2 UC-002 — Adicionar Item

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-002-1 | Adição válida → item persistido + EVT-003 + total recalculado | AC-PR-006 | API |
| TC-UC-002-2 | Quantidade zero → PR-ERR-010 + MSG-UC-002-A ancorada no campo | AC-PR-007 | API + UI |
| TC-UC-002-3 | Adição em estado Submitted → PR-ERR-040 + MSG-UC-002-C | AC-PR-008 | API |
| TC-UC-002-4 | Item sem centro de custo próprio → herda do cabeçalho | AC-PR-009 | API + INT |
| TC-UC-002-5 | Três itens em sequência → sequences 1, 2, 3 | AC-PR-010 | API |

## 5.3 UC-003 — Enviar para Aprovação

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-003-1 | Envio válido → Submitted + EVT-005/006/007/008 + cadeia instanciada + notificação | AC-PR-011 | API + MSG |
| TC-UC-003-2 | Envio sem itens → PR-ERR-020 + pendências em MSG-UC-003-B | AC-PR-012 | API + UI |
| TC-UC-003-3 | Workflow inexistente → EXC-001 + notificação ao Administrador + requisição não avança | AC-PR-013 | API + MSG |
| TC-UC-003-4 | Reenvio após Returned → ciclo incrementado + nova cadeia, histórico preservado | AC-PR-014 | API |
| TC-UC-003-5 | Envio com `If-Match` desatualizado → PR-ERR-409, nenhum estado alterado | AC-PR-015 | API |
| TC-UC-003-6 | Falha de validação no envio → EVT-011 com lista de pendências | AC-PR-016 | MSG |

## 5.4 UC-004 — Aprovar Solicitação

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-004-1 | Aprovação de nível único → Approved + EVT-009 + fila de Compras | AC-PR-017 | API + MSG |
| TC-UC-004-2 | Aprovação de nível intermediário → próximo nível ativado e notificado | AC-PR-018 | API + MSG |
| TC-UC-004-3 | SoD: solicitante tenta aprovar → EXC-006 + PR-ERR-041 + auditoria | AC-PR-019 | API |
| TC-UC-004-4 | Alçada insuficiente → PR-ERR-042 + escalonamento do workflow | AC-PR-020 | API + MSG |
| TC-UC-004-5 | Aprovação por delegado vigente → auditoria com `delegatedBy` | AC-PR-021 | API + INT |
| TC-UC-004-6 | Paralelismo ANY: primeira decisão conclui nível, demais atribuições canceladas | AC-PR-022 | API + INT |

## 5.5 UC-005 — Rejeitar Solicitação

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-005-1 | Rejeição com motivo → Rejected + EVT-010 + níveis encerrados + notificação | AC-PR-023 | API + MSG |
| TC-UC-005-2 | Rejeição sem motivo → PR-ERR-031 + MSG-UC-005-B, sem mudança de estado | AC-PR-024 | API + UI |
| TC-UC-005-3 | Rejeição encerra níveis pendentes (atribuições e timers cancelados) | AC-PR-023, AC-PR-051 | INT + MSG |
| TC-UC-005-4 | Rejeição após estado final → PR-ERR-040 | AC-PR-025 | API |
| TC-UC-005-5 ★ | Requisição rejeitada permanece visível em somente leitura, motivo destacado, sem edição | AC-PR-026 | UI + API |

## 5.6 UC-006 — Retornar para Ajustes

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-006-1 | Retorno pelo aprovador → Returned + EVT-011 + notificação ao solicitante | AC-PR-027 | API + MSG |
| TC-UC-006-2 | Retorno pela validação automática com lista de falhas | AC-PR-016, AC-PR-027 | API |
| TC-UC-006-3 | Edição em Returned e reenvio → nova cadeia (ciclo 2) | AC-PR-014 | API |
| TC-UC-006-4 | Retorno sem descrição → PR-ERR-031 + MSG-UC-006-B | AC-PR-028 | API + UI |
| TC-UC-006-5 ★ | Em Returned somente campos liberados são alteráveis; motivo do retorno visível | AC-PR-029 | UI + API |

## 5.7 UC-007 — Cancelar Solicitação

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-007-1 | Cancelamento em Draft → Cancelled + evento + auditoria | AC-PR-030 | API |
| TC-UC-007-2 | Cancelamento com workflow ativo → compensação + aprovadores notificados | AC-PR-030, AC-PR-051 | API + MSG |
| TC-UC-007-3 | Cancelamento com Pedido de Compra vinculado → bloqueio conforme política | AC-PR-031 | API |
| TC-UC-007-4 | Cancelamento sem motivo → validação bloqueia | AC-PR-030 | API + UI |

## 5.8 UC-008 — Consultar Requisições

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-008-1 | Keyset pagination: 3 páginas consistentes, sem duplicatas nem lacunas | AC-PR-032, AC-PR-058 | API |
| TC-UC-008-2 | Filtros combinados (status + centro de custo + período + texto) simultâneos | AC-PR-034 | API |
| TC-UC-008-3 | Isolamento de escopo: usuário não vê registros de outra empresa/unidade (ABAC-05) | AC-PR-032, AC-PR-045 | API |
| TC-UC-008-4 | Cursor inválido/expirado → PR-ERR-400 | AC-PR-058 | API |
| TC-UC-008-5 | Detalhe fora do escopo → 404 (anti-enumeração), nunca 403 | AC-PR-033 | API |

## 5.9 UC-009 — Visualizar Timeline

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-009-1 | Timeline após ciclo criar→enviar→aprovar: entradas em ordem cronológica, paginadas | AC-PR-035 | API + UI |
| TC-UC-009-2 | Filtro por tipo de entrada | AC-PR-035 | API |
| TC-UC-009-3 | Entradas restritas (ACTOR_ONLY/ROLE_SCOPED/ADMIN_ONLY) omitidas sem enquadramento | AC-PR-036 | API |
| TC-UC-009-4 | Imutabilidade: alteração direta de registro → rejeitada por trigger no banco | AC-PR-047 | INT |

## 5.10 UC-010 — Anexar Documento

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-010-1 | Upload de PDF válido → EVT-014 + metadados persistidos | AC-PR-037 | API + INT |
| TC-UC-010-2 | Executável disfarçado (.exe → .pdf) → bloqueio por magic bytes | AC-PR-038, AC-PR-067 | API |
| TC-UC-010-3 | Arquivo acima do limite → PR-ERR-071 | AC-PR-038 | API |
| TC-UC-010-4 | Download somente por URL assinada ≤ 5 min + auditoria de acesso | AC-PR-037 | API + INT |
| TC-UC-010-5 | Nenhuma URL pública em nenhuma resposta da API | AC-PR-037, AC-PR-067 | API |

## 5.11 UC-011 — Inserir Comentário

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-UC-011-1 | Comentário público → EVT-015 + notificação aos participantes | AC-PR-039 | API + MSG |
| TC-UC-011-2 | Comentário interno invisível fora de {Approver, Buyer, Manager, Admin} | AC-PR-040 | API + UI |
| TC-UC-011-3 | Payload com script (XSS) → sanitizado server-side e na renderização | AC-PR-039, AC-PR-067 | API + UI |
| TC-UC-011-4 | Autor não recebe notificação do próprio comentário | AC-PR-039 | MSG |
| TC-UC-011-5 ★ | Menção a usuário sem acesso à entidade → bloqueada, nunca concede acesso | AC-PR-041 | API |

---

# 6. Cenários de Regras Transversais (TC-TRV)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-TRV-001 | Fluxo de autorização completo: escopo(404) → RBAC → ABAC → delegação(403), deny by default em cada operação de escrita | AC-PR-042 | API | Automatizado (P0) |
| TC-TRV-002 | Optimistic concurrency: escrita sem `version`/`If-Match` correspondente → recusada | AC-PR-043 | API + INT | Automatizado (P0) |
| TC-TRV-003 | Soft delete: exclusão sempre lógica (`deleted_at`); verificação de ausência de DELETE físico | AC-PR-044 | INT | Automatizado (P0) |
| TC-TRV-004 | Filtro obrigatório por `company_id` em toda consulta (inclusive relatórios e projeções) | AC-PR-045 | INT + API | Automatizado (P0) |
| TC-TRV-005 | State machine exclusiva: tentativa de transição fora da máquina por API, banco e job → impossível | AC-PR-046 | API + INT | Automatizado (P0) |
| TC-TRV-006 | Auditoria 100%: amostragem exaustiva de todas as operações gera registro imutável com correlationId | AC-PR-047 | INT | Automatizado (P0) |

---

# 7. Cenários de Workflow e Estados (TC-WF)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-WF-001 | Pin da definição: alterar a configuração do workflow após a submissão não afeta a cadeia em andamento | AC-PR-048 | INT | Automatizado (P0) |
| TC-WF-002 | Instância única: em qualquer estado existe no máximo uma instância de workflow ativa por requisição | AC-PR-049 | INT | Automatizado (P0) |
| TC-WF-003 | Timers e escalonamentos: expiração de SLA de nível dispara WF-TMR/WF-ESC com notificações | AC-PR-050 | MSG | Automatizado (P1) |
| TC-WF-004 | Compensações: rejeição/cancelamento executa WF-COMP encerrando atribuições e timers pendentes | AC-PR-051 | INT + MSG | Automatizado (P1) |

---

# 8. Cenários de Eventos e Mensageria (TC-EVT)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-EVT-001 | Envelope oficial completo em todos os eventos (eventId, eventType, version, aggregateId, occurredAt, correlationId, causationId, actorId, companyId, payload) | AC-PR-052 | MSG | Automatizado (P0) |
| TC-EVT-002 | Outbox na mesma transação: falha após commit de domínio não gera evento órfão nem perde evento | AC-PR-052 | INT | Automatizado (P0) |
| TC-EVT-003 | Retry 5x com backoff exponencial em falha de consumo | AC-PR-053 | MSG | Automatizado (P0) |
| TC-EVT-004 | Esgotado o retry → mensagem em `trino.procurement.dlq` com contexto preservado | AC-PR-053 | MSG | Automatizado (P0) |
| TC-EVT-005 | Idempotência de consumidor por (eventId, consumerName): reentrega não duplica efeitos | AC-PR-053 | MSG | Automatizado (P0) |
| TC-EVT-006 | Ordem por aggregateId: eventos da mesma requisição processados em ordem | AC-PR-053 | MSG | Automatizado (P0) |

---

# 9. Cenários de Notificações (TC-NTF)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-NTF-001 | Despacho exclusivo pelo Notification Center (FD-001-05): nenhuma notificação enviada diretamente pelo módulo | AC-PR-054 | MSG + inspeção | Automatizado (P0) |
| TC-NTF-002 | Falha total de notificação (Notification Center indisponível) → fluxo da requisição não bloqueia | AC-PR-055 | MSG | Automatizado (P0) |
| TC-NTF-003 | Preferências do destinatário: canal e idioma respeitados dentro dos limites da organização | AC-PR-056 | MSG | Automatizado (P1) |
| TC-NTF-004 | Conteúdo renderizado no idioma do usuário (pt-BR/en-US) | AC-PR-068 | MSG | Automatizado (P1) |

---

# 10. Cenários de API (TC-API)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-API-001 | Envelope oficial: sucesso `{data, page, correlationId}`; erro `{error{code, message, details, correlationId}}` | AC-PR-057 | API | Automatizado (P0) |
| TC-API-002 | Keyset pagination (created_at, id) DESC com cursor assinado de 15 min; parâmetros de offset rejeitados | AC-PR-058 | API | Automatizado (P0) |
| TC-API-003 | `Idempotency-Key` repetida em 24h → resultado original (idempotentReplay) sem reexecutar efeitos | AC-PR-059 | API | Automatizado (P0) |
| TC-API-004 | PATCH/DELETE sem `If-Match` → PR-ERR-060 | AC-PR-060 | API | Automatizado (P0) |
| TC-API-005 | Mass assignment: campos fora da allowlist rejeitados/ignorados; calculados read-only | AC-PR-061 | API | Automatizado (P0) |
| TC-API-006 | Rate limit por classe (600/120/60 req-min): excedente → 429 com cabeçalhos de retry | AC-PR-062 | API | Automatizado (P1) |

---

# 11. Cenários de UX (TC-UX) e Fidelidade a Wireframes (TC-WFR)

Origem: PR-001-14 (CA-UX) e PR-001-15 (CA-WF). CA-UX-01 e CA-UX-02 são verificados por **revisão cruzada com evidência formal** (ações × UCs/endpoints; mensagens × catálogo MSG-UC), conforme o PR-001-14 — não têm cenário automatizado, mas são pré-requisito de release.

| Cenário | Objetivo | Critério | Camada | Automação |
|---------|----------|----------|--------|-----------|
| TC-UX-01 | Ações não permitidas pelo papel são ocultadas (nunca apenas desabilitadas) em todas as telas, por papel | CA-UX-03 / AC-PR-064 | UI | Automatizado (P0) |
| TC-UX-02 | Conformidade WCAG 2.1 AA nas 5 telas principais (auditoria automatizada + revisão assistida) | CA-UX-04 | UI | Automatizado + evidência (P0) |
| TC-UX-03 | Autosave de rascunho: queda de conexão não perde dados | CA-UX-05 | UI | Automatizado (P0) |
| TC-UX-04 | Confirmações destrutivas presentes em todas as ações da tabela 6.5 do PR-001-14 | CA-UX-06 | UI | Automatizado (P0) |
| TC-UX-05 | Estados de tela (carregando, vazio, erro, sem permissão) em todas as telas | CA-UX-07 | UI | Automatizado (P0) |
| TC-UX-06 | Layout funcional em desktop, tablet e mobile (3 faixas do PR-001-14, seção 8) | CA-UX-08 | UI | Automatizado (P0) |
| TC-WFR-001 | Revisão cruzada dos 7 wireframes (WF-01..07) × telas implementadas: zonas, estados e variantes | CA-WF-01..05 | UI + revisão | Automatizado + evidência (P0) |

## 11.1 Integração UI × API (TC-UI)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-UI-001 | Erro de API exibido com mensagem do catálogo MSG-UC (ou chave i18n derivada) e código PR-ERR preservado | AC-PR-063 | UI | Automatizado (P0) |
| TC-UI-002 | Ação indisponível pelo papel → ocultada na renderização (consistência com TC-UX-01 no nível de integração) | AC-PR-064 | UI | Automatizado (P0) |

---

# 12. Cenários de Segurança (TC-SEC)

Derivados de SEC-001, SEC-003 e dos pontos de segurança distribuídos nos UCs (referência cruzada para não duplicar catálogo).

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-SEC-001 | JWT ausente/expirado/inválido → 401; refresh rotativo com detecção de reuso (SEC-003) | AC-PR-067 | API | Automatizado (P0) |
| TC-SEC-002 | Anti-enumeração/IDOR: identificador fora do escopo → 404 (referência: TC-UC-008-5) | AC-PR-033, AC-PR-067 | API | Automatizado (P0) |
| TC-SEC-003 | XSS em comentários e campos de texto → sanitização server-side (referência: TC-UC-011-3) | AC-PR-067 | API + UI | Automatizado (P0) |
| TC-SEC-004 | Upload malicioso: magic bytes, tipo e tamanho (referência: TC-UC-010-2/-3) | AC-PR-038, AC-PR-067 | API | Automatizado (P0) |
| TC-SEC-005 | Storage privado: nenhuma URL pública; URLs assinadas ≤ 5 min (referência: TC-UC-010-4/-5) | AC-PR-037, AC-PR-067 | API | Automatizado (P0) |
| TC-SEC-006 | Mass assignment e campos calculados read-only (referência: TC-API-005) | AC-PR-061, AC-PR-067 | API | Automatizado (P0) |
| TC-SEC-007 | SoD: solicitante ≠ aprovador; ajuste de permissões auditado (referência: TC-UC-004-3) | AC-PR-019, AC-PR-067 | API | Automatizado (P0) |
| TC-SEC-008 | Revisão formal SEC-001 + SEC-003 (ASVS nível 2) sem pendências críticas — evidência anexada ao release | AC-PR-067 | SEC | Manual com evidência (P0) |

---

# 13. Cenários de Performance e Observabilidade (TC-PRF)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-PRF-001 | Listagens paginadas < 2s no percentil 95 sob carga de referência (k6, staging) | AC-PR-065 | PERF | Automatizado (P0) |
| TC-PRF-002 | CorrelationId ponta a ponta: requisição → evento → notificação → auditoria rastreável por um único ID | AC-PR-066 | MSG + INT | Automatizado (P0) |

---

# 14. Cenários de Banco de Dados e Migrations (TC-DB)

| Cenário | Objetivo | Origem | Camada | Automação |
|---------|----------|--------|--------|-----------|
| TC-DB-001 | Migrations versionadas aplicadas e reversíveis: apply + rollback em banco limpo (staging) | DoD item 7, PR-001-11 | INT | Automatizado (P0) |
| TC-DB-002 | Numeração sequencial por empresa sob concorrência sem duplicidade nem salto (referência: TC-UC-001-5) | PR-001-11 (`fn_next_pr_number`) | INT | Automatizado (P0) |
| TC-DB-003 | Plano de indexação: EXPLAIN das consultas principais usa os índices previstos (company_id-first, parciais) | PR-001-11 | INT | Automatizado (P1) |
| TC-DB-004 | Triggers de `updated_at`/`version` e imutabilidade de registros de aprovação (referência: TC-UC-009-4) | PR-001-11 | INT | Automatizado (P0) |

---

# 15. Matriz de Rastreabilidade Completa

## 15.1 Regra × UC × API × AC × Teste (DoD item 4)

| Regra | UC | API | AC | Teste(s) |
|-------|-----|-----|-----|----------|
| PR-BR-001 Identificador Único | UC-001 | POST /purchase-requisitions | AC-PR-001, 005 | TC-001, TC-UC-001-1, TC-UC-001-5 |
| PR-BR-002 Empresa | UC-001 | POST /purchase-requisitions | AC-PR-002, 045 | TC-002, TC-TRV-004 |
| PR-BR-003 Unidade | UC-001 | POST /purchase-requisitions | AC-PR-003 | TC-003, TC-UC-001-3 |
| PR-BR-004 Solicitante | UC-001 | POST /purchase-requisitions | AC-PR-001, 061 | TC-004, TC-API-005 |
| PR-BR-005 Data de Criação | UC-001 | POST /purchase-requisitions | AC-PR-001, 043 | TC-005, TC-TRV-002 |
| PR-BR-006 Status Inicial | UC-001 | POST /purchase-requisitions | AC-PR-001, 046 | TC-006, TC-TRV-005 |
| PR-BR-010 Quantidade | UC-002 | POST .../items | AC-PR-007 | TC-UC-002-2 |
| PR-BR-011 Unidade de Medida | UC-002 | POST .../items | AC-PR-006 | TC-UC-002-1 |
| PR-BR-012 Descrição | UC-002 | POST .../items | AC-PR-006 | TC-UC-002-1 |
| PR-BR-013 Categoria | UC-002 | POST .../items | AC-PR-006 | TC-UC-002-1 |
| PR-BR-014 Centro de Custo por Item | UC-002 | POST .../items | AC-PR-009 | TC-UC-002-4 |
| PR-BR-015 Projeto | UC-002 | POST .../items | AC-PR-006 | TC-UC-002-1 |
| PR-BR-020 Campos Obrigatórios | UC-001, UC-003 | POST, /submit | AC-PR-012, 016 | TC-UC-003-2, TC-UC-003-6 |
| PR-BR-021 Pelo Menos Um Item | UC-003 | /submit | AC-PR-012 | TC-UC-003-2 |
| PR-BR-022 Justificativa | UC-001 | POST /purchase-requisitions | AC-PR-001, 016 | TC-UC-001-1, TC-UC-003-6 |
| PR-BR-023 Anexos | UC-010 | POST .../attachments | AC-PR-037, 038 | TC-UC-010-1, TC-UC-010-3 |
| PR-BR-030 Workflow | UC-003 | /submit | AC-PR-013, 048 | TC-UC-003-3, TC-WF-001 |
| PR-BR-031 Aprovação Automática | UC-003, UC-004 | /submit, /approve | AC-PR-017, 048 | TC-UC-004-1, TC-WF-001 |
| PR-BR-032 Múltiplos Aprovadores | UC-004 | /approve | AC-PR-018, 022 | TC-UC-004-2, TC-UC-004-6 |
| PR-BR-033 Aprovação Sequencial | UC-004 | /approve | AC-PR-018 | TC-UC-004-2 |
| PR-BR-034 Aprovação Paralela | UC-004 | /approve | AC-PR-022 | TC-UC-004-6 |
| PR-BR-035 Delegação | UC-004 | /approve | AC-PR-021 | TC-UC-004-5 |
| PR-BR-040 Edição | UC-002, UC-006 | PATCH | AC-PR-008, 029 | TC-UC-002-3, TC-UC-006-5 |
| PR-BR-041 Alteração Após Aprovação | UC-004 | PATCH | AC-PR-046 | TC-TRV-005 |
| PR-BR-042 Reenvio | UC-006 | /submit | AC-PR-014 | TC-UC-006-3 |
| PR-BR-050 Cancelamento | UC-007 | /cancel | AC-PR-030 | TC-UC-007-1 |
| PR-BR-051 Justificativa de Cancelamento | UC-007 | /cancel | AC-PR-030 | TC-UC-007-4 |
| PR-BR-060 Histórico | UC-009 | GET .../history | AC-PR-035 | TC-UC-009-1 |
| PR-BR-061 Timeline | UC-009 | GET .../timeline | AC-PR-035, 036 | TC-UC-009-1, TC-UC-009-2, TC-UC-009-3 |
| PR-BR-062 Imutabilidade | UC-009 | — | AC-PR-047 | TC-UC-009-4 |
| PR-BR-070 Permissões | Todos | Todos | AC-PR-042 | TC-TRV-001 |
| PR-BR-071 Multiempresa | UC-008 | GET /purchase-requisitions | AC-PR-045 | TC-TRV-004, TC-UC-008-3 |
| PR-BR-072 Segregação (SoD) | UC-004 | /approve | AC-PR-019 | TC-UC-004-3 |
| PR-BR-080 Pesquisa | UC-008 | GET /purchase-requisitions | AC-PR-034 | TC-UC-008-2 |
| PR-BR-081 Paginação | UC-008 | GET /purchase-requisitions | AC-PR-058 | TC-UC-008-1 |
| PR-BR-082 Filtros Combináveis | UC-008 | GET /purchase-requisitions | AC-PR-034 | TC-UC-008-2 |

**Cobertura:** 36/36 regras com teste associado (100%). Todas as regras `Obrigatória` possuem cenário automatizado (DoD item 3).

## 15.2 AC × Teste (consolidado)

| AC | Teste(s) | AC | Teste(s) |
|----|----------|----|----------|
| AC-PR-001 | TC-UC-001-1 | AC-PR-035 | TC-UC-009-1, TC-UC-009-2 |
| AC-PR-002 | TC-UC-001-2 | AC-PR-036 | TC-UC-009-3 |
| AC-PR-003 | TC-UC-001-3 | AC-PR-037 | TC-UC-010-1, TC-UC-010-4, TC-UC-010-5 |
| AC-PR-004 | TC-UC-001-4 | AC-PR-038 | TC-UC-010-2, TC-UC-010-3 |
| AC-PR-005 | TC-UC-001-5, TC-DB-002 | AC-PR-039 | TC-UC-011-1, TC-UC-011-3, TC-UC-011-4 |
| AC-PR-006 | TC-UC-002-1 | AC-PR-040 | TC-UC-011-2 |
| AC-PR-007 | TC-UC-002-2 | AC-PR-041 | TC-UC-011-5 |
| AC-PR-008 | TC-UC-002-3 | AC-PR-042 | TC-TRV-001 |
| AC-PR-009 | TC-UC-002-4 | AC-PR-043 | TC-TRV-002, TC-UC-003-5 |
| AC-PR-010 | TC-UC-002-5 | AC-PR-044 | TC-TRV-003 |
| AC-PR-011 | TC-UC-003-1 | AC-PR-045 | TC-TRV-004, TC-UC-008-3 |
| AC-PR-012 | TC-UC-003-2 | AC-PR-046 | TC-TRV-005 |
| AC-PR-013 | TC-UC-003-3 | AC-PR-047 | TC-TRV-006, TC-UC-009-4 |
| AC-PR-014 | TC-UC-003-4, TC-UC-006-3 | AC-PR-048 | TC-WF-001 |
| AC-PR-015 | TC-UC-003-5 | AC-PR-049 | TC-WF-002 |
| AC-PR-016 | TC-UC-003-6, TC-UC-006-2 | AC-PR-050 | TC-WF-003 |
| AC-PR-017 | TC-UC-004-1 | AC-PR-051 | TC-WF-004, TC-UC-005-3, TC-UC-007-2 |
| AC-PR-018 | TC-UC-004-2 | AC-PR-052 | TC-EVT-001, TC-EVT-002 |
| AC-PR-019 | TC-UC-004-3 | AC-PR-053 | TC-EVT-003, TC-EVT-004, TC-EVT-005, TC-EVT-006 |
| AC-PR-020 | TC-UC-004-4 | AC-PR-054 | TC-NTF-001 |
| AC-PR-021 | TC-UC-004-5 | AC-PR-055 | TC-NTF-002 |
| AC-PR-022 | TC-UC-004-6 | AC-PR-056 | TC-NTF-003 |
| AC-PR-023 | TC-UC-005-1, TC-UC-005-3 | AC-PR-057 | TC-API-001 |
| AC-PR-024 | TC-UC-005-2 | AC-PR-058 | TC-API-002, TC-UC-008-1, TC-UC-008-4 |
| AC-PR-025 | TC-UC-005-4 | AC-PR-059 | TC-API-003 |
| AC-PR-026 | TC-UC-005-5 | AC-PR-060 | TC-API-004 |
| AC-PR-027 | TC-UC-006-1, TC-UC-006-2 | AC-PR-061 | TC-API-005 |
| AC-PR-028 | TC-UC-006-4 | AC-PR-062 | TC-API-006 |
| AC-PR-029 | TC-UC-006-5 | AC-PR-063 | TC-UI-001 |
| AC-PR-030 | TC-UC-007-1, TC-UC-007-2, TC-UC-007-4 | AC-PR-064 | TC-UI-002, TC-UX-01 |
| AC-PR-031 | TC-UC-007-3 | AC-PR-065 | TC-PRF-001 |
| AC-PR-032 | TC-UC-008-1, TC-UC-008-3 | AC-PR-066 | TC-PRF-002 |
| AC-PR-033 | TC-UC-008-5 | AC-PR-067 | TC-SEC-001..008 |
| AC-PR-034 | TC-UC-008-2 | AC-PR-068 | TC-NTF-004 |

Critérios por referência: **CA-UX-01/02** (revisão cruzada com evidência), **CA-UX-03..08** (TC-UX-01..06), **CA-WF-01..05** (TC-WFR-001).

**Cobertura:** 68/68 ACs com ao menos um cenário (100%). 100% dos P0 com cenário automatizável ou evidência formal definida.

---

# 16. Alinhamento com o DoD do Módulo

| Item DoD (PR-001 README) | Atendimento neste documento |
|--------------------------|------------------------------|
| 1. Documentos PR-001-01 a 17 Approved | PR-001-17 é o último documento do pacote |
| 2. Cobertura de testes dos casos TC-001 a TC-006 e de todas as regras obrigatórias | Seção 4 (TC-001..006) + seção 15.1 (36/36 regras) |
| 3. Toda regra `Obrigatória` com teste automatizado | Seção 15.1 — coluna de testes, todos automatizados |
| 4. Matriz regra × UC × API × teste 100% preenchida | Seções 15.1 e 15.2 |
| 5. Revisão de segurança SEC-001/003 sem pendências críticas | TC-SEC-008 + cenários TC-SEC-001..007 |
| 6. Eventos validados (payload, idempotência, DLQ) | Seção 8 (TC-EVT-001..006) |
| 7. Migrations versionadas e reversíveis | TC-DB-001 |

---

# 17. Gestão de Evidências e Falhas

1. **Evidências automatizadas:** relatórios de pipeline (xUnit/Playwright/k6) anexados ao release; nenhum release com cenário P0 vermelho.
2. **Evidências formais (manuais):** CA-UX-01/02, TC-WFR-001 (parte de revisão), TC-SEC-008 — relatório assinado pelo responsável e arquivado com o release.
3. **Falhas:** todo cenário reprovado gera defeito classificado **P0–P4** (padrão de QA do projeto); P0/P1 bloqueiam release, P2 com plano de correção, P3/P4 backlog.
4. **Regressão:** cenários deste catálogo entram na suíte de regressão; nenhum cenário é removido sem revisão deste documento (conflito → documentação vence).
5. **Manutenção:** alteração de comportamento documentado exige atualização prévia da fonte (regra/UC/AC) e deste catálogo na mesma revisão.

---

# 18. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | TC-001..006, TC-UC-001..011 (52 cenários + 3 ★), TC-TRV, TC-WF, TC-EVT, TC-NTF, TC-API, TC-UX, TC-WFR, TC-UI, TC-SEC, TC-PRF, TC-DB — 100% dos AC-PR-001..068 |
| **v1.1** | Cenários de UC-012 (duplicação), UC-013 (anexos por item), UC-014 (exportação de detalhe); promoção dos cenários P2 |
| **v2.0** | Cenários de fila de Compras enriquecida, webhooks e bulk-approve (roadmap PR-001-13) |

---

# 19. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação dos Test Scenarios do módulo: catálogo completo de cenários (Regras Gerais TC-001..006, 55 cenários TC-UC, transversais, workflow, eventos, notificações, API, UX, wireframes, UI×API, segurança, performance e banco), estratégia de automação por camada, dados e ambientes, matriz de rastreabilidade 100% (regra × UC × API × AC × teste e AC × teste) e alinhamento integral ao DoD — **fecha o pacote PR-001 (17/17 documentos)** |
