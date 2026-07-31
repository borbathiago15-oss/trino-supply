**Documento:** MMS-002-17 — Test Scenarios
**Módulo:** MMS-002 — Item Catalog (Materials Domain)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 (Visão do Módulo — DoD), MMS-002-02 (Business Rules), MMS-002-03 (State Machine), MMS-002-05 (Event Storming), MMS-002-07 (Use Cases), MMS-002-09 (Permissions), MMS-002-10 (Notifications), MMS-002-11 (Database Model), MMS-002-13 (API), MMS-002-14 (UX), MMS-002-15 (Wireframes), MMS-002-16 (Acceptance Criteria)
**Referências:** SEC-001, SEC-003, FD-001 (Foundation), ADR-009, ADR-010, GOV-001, PR-001-17 (Test Scenarios do Purchase Requisition — padrão de formato)

---

# 1. Objetivo

Este documento é o **catálogo oficial de cenários de teste** do módulo Item Catalog. Ele transforma os 72 critérios de aceite do MMS-002-16 em cenários executáveis, fecha a **matriz de rastreabilidade regra × UC × API × AC × teste** exigida pelo DoD (item 4) e define a estratégia de automação, dados, ambientes e evidências.

Regras de vínculo:

- Nenhum cenário inventa comportamento: todo cenário deriva de fonte documentada (regra, UC, state machine, evento, permissão, API, UX, wireframe ou NFR).
- Cenários já listados nos casos de uso (TC-IC-xxx-y, MMS-002-07) são **incorporados e formalizados** aqui, com mapeamento para os AC-IC — o MMS-002-07 permanece como origem do resumo funcional.
- Todo critério **P0** possui ao menos um cenário automatizável; toda regra `Obrigatória` possui ao menos um cenário de teste automatizado associado (DoD itens 2 e 3).

---

# 2. Convenções

## 2.1 Identificação dos cenários

| Prefixo | Domínio do cenário | Origem principal |
|---------|-------------------|------------------|
| `TC-IC-xxx-y` | Cenário funcional do caso de uso UC-IC-xxx | MMS-002-07 |
| `TC-TRV-IC-nnn` | Regras transversais (autorização, concorrência, soft delete, multiempresa, state machine, auditoria, invariante CA) | MMS-002-16, seção 3 |
| `TC-STC-IC-nnn` | Ciclo de vida e estados | MMS-002-03, MMS-002-16 seção 4 |
| `TC-EVT-IC-nnn` | Eventos e mensageria | MMS-002-05, ADR-010 |
| `TC-NTF-IC-nnn` | Notificações | MMS-002-10, FD-001-05 |
| `TC-API-IC-nnn` | Contrato de API | MMS-002-13 |
| `TC-UX-IC-nn` | Experiência de usuário (CA-UX-IC) | MMS-002-14 |
| `TC-WFR-IC-nnn` | Fidelidade a wireframes (CA-WF-IC) | MMS-002-15 |
| `TC-UI-IC-nnn` | Integração UI × API | MMS-002-16, seção 7 |
| `TC-SEC-IC-nnn` | Segurança | SEC-001, SEC-003 |
| `TC-PRF-IC-nnn` | Performance e observabilidade | MMS-002/NFR |
| `TC-DB-IC-nnn` | Banco de dados e migrations | MMS-002-11 |

Códigos são imutáveis após publicação; cenários novos recebem o próximo sequencial do prefixo.

## 2.2 Camadas de execução

| Camada | Sigla | Ferramenta-alvo | Escopo |
|--------|-------|-----------------|--------|
| Unidade | UNIT | xUnit (.NET) / Vitest (frontend) | Regras de domínio, factories, specifications, value objects |
| Integração | INT | xUnit + Testcontainers (PostgreSQL, Redis, RabbitMQ, MinIO) | Repositórios, outbox, triggers, constraints, views |
| Contrato/API | API | Testes de integração HTTP (WebApplicationFactory) | Endpoints, envelope, erros, headers, idempotência |
| Mensageria | MSG | Testcontainers RabbitMQ | Retry, DLQ, idempotência de consumidor, ordem |
| Interface | UI | Playwright | Telas, fluxos de usuário, estados de tela, acessibilidade |
| End-to-end | E2E | Playwright + ambiente integrado | Jornadas completas cross-módulo (catálogo → requisição) |
| Performance | PERF | k6 | Carga de referência, percentis |
| Segurança | SEC | ZAP/Burp + testes automatizados | OWASP ASVS, revisão SEC-001/003 |

## 2.3 Automação e prioridade

- **Automatizado obrigatório:** todo cenário vinculado a AC **P0** e toda regra `Obrigatória` (DoD item 3).
- **Automatizado desejável:** cenários de AC **P1**; exceções toleradas exigem registro com dono e prazo (MMS-002-16, seção 10).
- **Manual com evidência formal:** revisões cruzadas (CA-UX-IC-01/02, CA-WF-IC), auditoria de acessibilidade assistida e revisão de segurança — evidência anexada ao release.
- Prioridade do cenário = prioridade do AC vinculado (P0/P1/P2).

## 2.4 Template de especificação

Cada cenário é executado com: **objetivo → pré-condições → dados de teste → passos → resultado esperado → AC/regra vinculada → camada → automação**. As tabelas deste documento trazem a forma resumida (objetivo + vínculos); a forma expandida é gerada no repositório de testes a partir deste catálogo, sem divergir dele.

---

# 3. Estratégia de Testes

## 3.1 Pirâmide

| Nível | Participação esperada | Conteúdo |
|-------|----------------------|----------|
| UNIT/INT | ~60% | Domínio, regras, repositórios, constraints, views |
| API/MSG | ~30% | Contratos, eventos, notificações, segurança de API |
| UI/E2E | ~10% | Jornadas críticas, UX, telas, componente de busca |

## 3.2 Ambientes

| Ambiente | Uso |
|----------|-----|
| Local (Testcontainers) | UNIT, INT, API, MSG em pipeline de PR |
| QA integrado | UI, E2E, MSG ponta a ponta, i18n |
| Staging (espelho) | PERF, SEC, revisão de release, migrations reversíveis |

## 3.3 Dados de teste

- **Massa sintética versionada** no repositório de testes: 2 empresas (isolamento multiempresa), 5 usuários (Catalog Maintainer, Operational, Auditor, Admin + 1 sem permissões), catálogo de 15 itens (EPI com CA válido, EPI com CA vencendo em 60 dias, EPI com CA vencido, EPI sem CA, Fardamento com grade, item sem grade, item sem imagem, item Inactive, item nunca consumido, item consumido por requisição, item Discarded, itens com sinônimos sobrepostos), domínios Master Data (unidades, SIZE_GRID).
- **Proibição:** dados reais ou pseudonimizados de produção em qualquer ambiente de teste (LGPD, SEC-001).
- Reset por cenário via transação ou seed idempotente; nenhum cenário depende de estado deixado por outro.

## 3.4 Critérios de entrada e saída

- **Entrada:** build verde, migrations aplicadas, massa sintética carregada, secrets de teste no vault de CI.
- **Saída (release):** 100% dos cenários P0 automatizados e verdes; exceções P1 registradas; evidências formais anexadas; revisão SEC sem pendências críticas (AC-IC-070).

---

# 4. Cenários por Caso de Uso (TC-IC)

Origem: MMS-002-07 (resumo funcional). Aqui cada cenário é vinculado ao seu AC (MMS-002-16). Cenários marcados com ★ foram **acrescentados** neste documento para fechar a cobertura de ACs sem cenário correspondente — não alteram nenhum UC.

## 4.1 UC-IC-001 — Criar Item

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IC-001-1 | Criação válida → Draft + EVT-IC-001 + auditoria | AC-IC-001 | API + MSG |
| TC-IC-001-2 | Criação sem permissão → IC-ERR-900 + auditoria de negação, nada persistido | AC-IC-002 | API |
| TC-IC-001-3 | Código duplicado (ativo/inativo) → IC-ERR-010 com referência ao item existente | AC-IC-003 | API |
| TC-IC-001-4 | Código ERP duplicado → IC-ERR-011 | AC-IC-004 | API |
| TC-IC-001-5 | Código de item descartado reutilizado → aceito; histórico do descartado intacto | AC-IC-005 | API + INT |
| TC-IC-001-6 | Abandono antes de salvar → nenhum dado persistido | AC-IC-006 | API + INT |

## 4.2 UC-IC-002 — Ativar Item

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IC-002-1 | Ativação de item completo → Active + campos estruturais travados + EVT-IC-003 + IC-NOT-001 | AC-IC-007 | API + MSG |
| TC-IC-002-2 | EPI sem CA → IC-ERR-080 + gap no checklist | AC-IC-008 | API + UI |
| TC-IC-002-3 | EPI com CA vencido → IC-ERR-081 | AC-IC-009 | API |
| TC-IC-002-4 | Ativação sem motivo → IC-ERR-091, sem mudança de estado | AC-IC-010 | API |
| TC-IC-002-5 | Item incompleto → IC-ERR-020 com lista de gaps idêntica ao checklist | AC-IC-011 | API |
| TC-IC-002-6 | Ativação com `If-Match` desatualizado → IC-ERR-409, nenhum estado alterado | AC-IC-012 | API |
| TC-IC-002-7 ★ | Evento de ativação consumido → MMS-003/MMS-004 enxergam o item como operável | AC-IC-013 | MSG + E2E |

## 4.3 UC-IC-003 — Editar/Enriquecer Item

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IC-003-1 | Edição em Draft de qualquer campo → persistida + versão incrementada + EVT-IC-002 | AC-IC-014 | API + MSG |
| TC-IC-003-2 | Alteração de código/grupo/unidade em Active → IC-ERR-031 | AC-IC-015 | API |
| TC-IC-003-3 | Sinônimo novo → normalizado e casando na busca `q` | AC-IC-016 | API + INT |
| TC-IC-003-4 | Sinônimo duplicado (normalizado) → IC-ERR-040 | AC-IC-017 | API |
| TC-IC-003-5 | CA em item Fardamento → recusado (não se aplica) | AC-IC-018 | API |
| TC-IC-003-6 | Tamanho fora do SIZE_GRID → IC-ERR-082 | AC-IC-019 | API |
| TC-IC-003-7 | Parâmetros com mínimo > máximo → IC-ERR-060 | AC-IC-020 | API |
| TC-IC-003-8 | Imagem inválida (tipo/tamanho) → IC-ERR-083, nada persistido | AC-IC-021 | API |
| TC-IC-003-9 | Imagem válida → FD-001-03 + EVT-IC-008 + download só por URL assinada ≤ 5 min | AC-IC-022 | API + INT |
| TC-IC-003-10 | Remoção de CA em Active → bloqueada; em Draft/Inactive → permitida com auditoria | AC-IC-023 | API |
| TC-IC-003-11 | Atualização de CA → EVT-IC-006 + reagendamento do alerta de vencimento | AC-IC-024 | MSG |

## 4.4 UC-IC-004 — Inativar Item

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IC-004-1 | Inativação com motivo → Inactive + EVT-IC-004 + IC-NOT-002 + auditoria | AC-IC-025 | API + MSG |
| TC-IC-004-2 | Item Inactive não é oferecido nem aceito em nova requisição/movimentação | AC-IC-026 | API + MSG |
| TC-IC-004-3 | Referências históricas (requisições, saldos) permanecem íntegras após inativação | AC-IC-027 | API + INT |
| TC-IC-004-4 | Inativação sem motivo → IC-ERR-091 | AC-IC-028 | API |

## 4.5 UC-IC-005 — Reativar Item

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IC-005-1 | Reativação de item completo → Active + EVT-IC-005 + IC-NOT-003 + operável | AC-IC-029 | API + MSG |
| TC-IC-005-2 | Reativação com CA vencido → IC-ERR-081 até renovação | AC-IC-030 | API |
| TC-IC-005-3 | Reativação a partir de estado ≠ Inactive → IC-ERR-090 | AC-IC-031 | API |

## 4.6 UC-IC-006 — Descartar Item

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IC-006-1 | Descarte de item nunca consumido → Discarded + EVT-IC-007 + IC-NOT-004 + código liberado | AC-IC-032 | API + INT |
| TC-IC-006-2 | Descarte de item consumido → IC-ERR-070 | AC-IC-033 | API |
| TC-IC-006-3 | Descarte sem `items.admin` → IC-ERR-900 + auditoria | AC-IC-034 | API |
| TC-IC-006-4 | Escrita em item Discarded → IC-ERR-090 (terminal) | AC-IC-035 | API |

## 4.7 UC-IC-007 — Consultar e Buscar Itens

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IC-007-1 | Keyset pagination: 3 páginas consistentes, sem duplicatas nem lacunas | AC-IC-036, AC-IC-060 | API |
| TC-IC-007-2 | Isolamento de escopo: usuário não vê itens de outra empresa | AC-IC-036, AC-IC-046 | API |
| TC-IC-007-3 | Detalhe fora do escopo → 404 (anti-enumeração), nunca 403 | AC-IC-037 | API |
| TC-IC-007-4 | Busca por sinônimo → item retornado com `matchedBy = SYNONYM` | AC-IC-038 | API |
| TC-IC-007-5 | Busca tolerante a acentos e caixa (descrição e sinônimos) | AC-IC-039 | API |
| TC-IC-007-6 | Componente de busca do solicitante retorna apenas ACTIVE | AC-IC-040 | API + UI |
| TC-IC-007-7 | Filtros combinados (status + grupo + caStatus + sem imagem) simultâneos | AC-IC-041 | API |
| TC-IC-007-8 | Timeline em ordem cronológica com keyset | AC-IC-042 | API + UI |

---

# 5. Cenários de Regras Transversais (TC-TRV-IC)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-TRV-IC-001 | Fluxo de autorização completo: escopo(404) → RBAC → ABAC → delegação(403), deny by default em cada operação de escrita | AC-IC-043 | API | Automatizado (P0) |
| TC-TRV-IC-002 | Optimistic concurrency: escrita sem `version`/`If-Match` correspondente → IC-ERR-409 | AC-IC-044 | API + INT | Automatizado (P0) |
| TC-TRV-IC-003 | Soft delete: exclusão sempre lógica (`deleted_at`); verificação de ausência de DELETE físico | AC-IC-045 | INT | Automatizado (P0) |
| TC-TRV-IC-004 | Filtro obrigatório por `company_id` em toda consulta (inclusive views e projeções) | AC-IC-046 | INT + API | Automatizado (P0) |
| TC-TRV-IC-005 | State machine exclusiva: tentativa de transição fora da máquina por API, banco e job → impossível | AC-IC-047 | API + INT | Automatizado (P0) |
| TC-TRV-IC-006 | Auditoria 100%: amostragem exaustiva de todas as operações gera registro imutável com correlationId, autor, motivo e versão | AC-IC-048 | INT | Automatizado (P0) |
| TC-TRV-IC-007 | Invariante CA: nenhum EPI Active sem CA válido; view `vw_epi_sem_ca` sempre vazia em estado consistente | AC-IC-049 | INT | Automatizado (P0) |

---

# 6. Cenários de Ciclo de Vida e Estados (TC-STC-IC)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-STC-IC-001 | Toda ação de ciclo de vida exige motivo + auditoria + evento correspondente (ativar/inativar/reativar/descartar) | AC-IC-050 | API + MSG | Automatizado (P0) |
| TC-STC-IC-002 | Status exibido corresponde ao estado persistido em qualquer consulta | AC-IC-051 | API | Automatizado (P0) |
| TC-STC-IC-003 | Transições concorrentes no mesmo item: uma aceita, outra IC-ERR-409, estado final consistente | AC-IC-052 | API + INT | Automatizado (P1) |

---

# 7. Cenários de Eventos e Mensageria (TC-EVT-IC)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-EVT-IC-001 | Envelope oficial completo em todos os eventos EVT-IC-001..008 (eventId, eventType, version, aggregateId, occurredAt, correlationId, causationId, actorId, companyId, payload) | AC-IC-053 | MSG | Automatizado (P0) |
| TC-EVT-IC-002 | Outbox na mesma transação: falha após commit de domínio não gera evento órfão nem perde evento | AC-IC-053 | INT | Automatizado (P0) |
| TC-EVT-IC-003 | Retry 5x com backoff exponencial em falha de consumo | AC-IC-054 | MSG | Automatizado (P0) |
| TC-EVT-IC-004 | Esgotado o retry → mensagem em `trino.materials.dlq` com contexto preservado | AC-IC-054 | MSG | Automatizado (P0) |
| TC-EVT-IC-005 | Idempotência de consumidor por (eventId, consumerName): reentrega não duplica efeitos | AC-IC-054 | MSG | Automatizado (P0) |
| TC-EVT-IC-006 | Ordem por aggregateId nos eventos críticos (EVT-IC-003/004/005) do mesmo item | AC-IC-055 | MSG | Automatizado (P0) |

---

# 8. Cenários de Notificações (TC-NTF-IC)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-NTF-IC-001 | Despacho exclusivo pelo Notification Center (FD-001-05): nenhuma notificação enviada diretamente pelo módulo | AC-IC-056 | MSG + inspeção | Automatizado (P0) |
| TC-NTF-IC-002 | Falha total de notificação → fluxo do item não bloqueia | AC-IC-057 | MSG | Automatizado (P0) |
| TC-NTF-IC-003 | Alerta de vencimento de CA (IC-NOT-005) disparado nos marcos 90/60/30 dias por agendamento, não em request | AC-IC-058 | MSG | Automatizado (P1) |
| TC-NTF-IC-004 | Conteúdo renderizado no idioma do usuário (pt-BR/en-US) | AC-IC-071 | MSG | Automatizado (P1) |

---

# 9. Cenários de API (TC-API-IC)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-API-IC-001 | Envelope oficial: sucesso `{data, page, correlationId}`; erro `{error{code, message, details, correlationId}}` | AC-IC-059 | API | Automatizado (P0) |
| TC-API-IC-002 | Keyset pagination com cursor assinado de 15 min; offset rejeitado; cursor inválido/expirado → IC-ERR-400 | AC-IC-060 | API | Automatizado (P0) |
| TC-API-IC-003 | `Idempotency-Key` repetida em 24h → resultado original (idempotentReplay) sem reexecutar efeitos | AC-IC-061 | API | Automatizado (P0) |
| TC-API-IC-004 | PATCH/PUT/DELETE sem `If-Match` → IC-ERR-409 | AC-IC-062 | API | Automatizado (P0) |
| TC-API-IC-005 | Mass assignment: campos fora da allowlist rejeitados/ignorados; calculados read-only | AC-IC-063 | API | Automatizado (P0) |
| TC-API-IC-006 | Rate limit por classe (600/120/60 req-min): excedente → 429 com `Retry-After` + IC-ERR-429 | AC-IC-064 | API | Automatizado (P1) |
| TC-API-IC-007 | Endpoint de completude retorna exatamente os gaps/avisos que a ativação validaria (fonte única) | AC-IC-065 | API | Automatizado (P0) |

---

# 10. Cenários de UX (TC-UX-IC) e Fidelidade a Wireframes (TC-WFR-IC)

Origem: MMS-002-14 (CA-UX-IC) e MMS-002-15 (CA-WF-IC). CA-UX-IC-01 e CA-UX-IC-02 são verificados por **revisão cruzada com evidência formal** (ações × UCs/endpoints; mensagens × catálogo), conforme o MMS-002-14 — não têm cenário automatizado, mas são pré-requisito de release.

| Cenário | Objetivo | Critério | Camada | Automação |
|---------|----------|----------|--------|-----------|
| TC-UX-IC-01 | Ações não permitidas pelo papel são ocultadas (nunca apenas desabilitadas) em todas as telas, por papel | CA-UX-IC-03 / AC-IC-067 | UI | Automatizado (P0) |
| TC-UX-IC-02 | Conformidade WCAG 2.1 AA nas telas IC-SCR-01/02/03 (auditoria automatizada + revisão assistida) | CA-UX-IC-04 | UI | Automatizado + evidência (P0) |
| TC-UX-IC-03 | Autosave de rascunho: queda de conexão não perde dados | CA-UX-IC-05 | UI | Automatizado (P0) |
| TC-UX-IC-04 | Confirmações de ciclo de vida presentes em todas as ações da tabela 6.5 do MMS-002-14 | CA-UX-IC-06 | UI | Automatizado (P0) |
| TC-UX-IC-05 | Estados de tela (carregando, vazio, erro, sem permissão) em todas as telas | CA-UX-IC-07 | UI | Automatizado (P0) |
| TC-UX-IC-06 | Checklist de completude funcional, clicável e focando o campo com gap | CA-UX-IC-08 | UI | Automatizado (P0) |
| TC-UX-IC-07 | Busca por sinônimo retorna item com indicação `matchedBy` na interface | CA-UX-IC-09 | UI | Automatizado (P0) |
| TC-WFR-IC-001 | Revisão cruzada dos 6 wireframes (WF-IC-01..06) × telas/componentes implementados: zonas, estados e variantes | CA-WF-IC-01..06 | UI + revisão | Automatizado + evidência (P0) |

## 10.1 Integração UI × API (TC-UI-IC)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-UI-IC-001 | Erro de API exibido com mensagem do catálogo documentado e código IC-ERR preservado para suporte | AC-IC-066 | UI | Automatizado (P0) |
| TC-UI-IC-002 | Ação indisponível pelo papel → ocultada na renderização (consistência com TC-UX-IC-01 no nível de integração) | AC-IC-067 | UI | Automatizado (P0) |

---

# 11. Cenários de Segurança (TC-SEC-IC)

Derivados de SEC-001, SEC-003 e dos pontos de segurança distribuídos nos UCs (referência cruzada para não duplicar catálogo).

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-SEC-IC-001 | JWT ausente/expirado/inválido → 401; refresh rotativo com detecção de reuso (SEC-003) | AC-IC-070 | API | Automatizado (P0) |
| TC-SEC-IC-002 | Anti-enumeração/IDOR: identificador fora do escopo → 404 (referência: TC-IC-007-3) | AC-IC-037, AC-IC-070 | API | Automatizado (P0) |
| TC-SEC-IC-003 | XSS em descrição e sinônimos → sanitização server-side e na renderização | AC-IC-070 | API + UI | Automatizado (P0) |
| TC-SEC-IC-004 | Upload de imagem maliciosa: magic bytes, tipo e tamanho (referência: TC-IC-003-8) | AC-IC-021, AC-IC-070 | API | Automatizado (P0) |
| TC-SEC-IC-005 | Storage privado: nenhuma URL pública; URLs assinadas ≤ 5 min (referência: TC-IC-003-9) | AC-IC-022, AC-IC-070 | API | Automatizado (P0) |
| TC-SEC-IC-006 | Mass assignment e campos calculados read-only (referência: TC-API-IC-005) | AC-IC-063, AC-IC-070 | API | Automatizado (P0) |
| TC-SEC-IC-007 | Ciclo de vida sensível: `items.lifecycle`/`items.admin` + motivo + auditoria (referência: TC-STC-IC-001, TC-IC-006-3) | AC-IC-034, AC-IC-070 | API | Automatizado (P0) |
| TC-SEC-IC-008 | Revisão formal SEC-001 + SEC-003 (ASVS nível 2) sem pendências críticas — evidência anexada ao release | AC-IC-070 | SEC | Manual com evidência (P0) |

---

# 12. Cenários de Performance e Observabilidade (TC-PRF-IC)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-PRF-IC-001 | Listagens/busca `q` ≤ 500 ms e detalhe ≤ 300 ms no percentil 95 sob carga de referência (k6, staging) | AC-IC-068 | PERF | Automatizado (P0) |
| TC-PRF-IC-002 | CorrelationId ponta a ponta: requisição → evento → notificação → auditoria rastreável por um único ID | AC-IC-069 | MSG + INT | Automatizado (P0) |
| TC-PRF-IC-003 | MV `mv_item_status_summary` com refresh agendado e defasagem documentada respeitada | AC-IC-072 | INT | Automatizado (P1) |

---

# 13. Cenários de Banco de Dados e Migrations (TC-DB-IC)

| Cenário | Objetivo | Origem | Camada | Automação |
|---------|----------|--------|--------|-----------|
| TC-DB-IC-001 | Migrations versionadas aplicadas e reversíveis: apply + rollback em banco limpo (staging) | DoD item 7, MMS-002-11 | INT | Automatizado (P0) |
| TC-DB-IC-002 | Unicidade soft-delete-ciente: `(company_id, code)` e `erp_code` — duplicidade bloqueada, reuso pós-descarte permitido (referência: TC-IC-001-3/-5) | MMS-002-11 | INT | Automatizado (P0) |
| TC-DB-IC-003 | Plano de indexação: EXPLAIN das consultas principais usa os índices previstos (company_id-first, parciais) | MMS-002-11 | INT | Automatizado (P1) |
| TC-DB-IC-004 | Triggers TRG-IC-001..003 (`updated_at`/`version`, imutabilidade) disparando conforme especificação | MMS-002-11 | INT | Automatizado (P0) |
| TC-DB-IC-005 | Views `vw_item_catalog`/`vw_item_operational`/`vw_epi_sem_ca` filtrando por company_id e retornando projeções corretas | MMS-002-11 | INT | Automatizado (P1) |

---

# 14. Matriz de Rastreabilidade Completa

## 14.1 Regra × UC × API × AC × Teste (DoD item 4)

| Regra | UC | API | AC | Teste(s) |
|-------|-----|-----|-----|----------|
| IC-BR-001 Consumo só de ACTIVE | UC-IC-007 | GET /items | AC-IC-040 | TC-IC-007-6 |
| IC-BR-010 Unicidade de código | UC-IC-001 | POST /items | AC-IC-003, 005 | TC-IC-001-3, TC-IC-001-5, TC-DB-IC-002 |
| IC-BR-011 Unicidade de código ERP | UC-IC-001 | POST /items | AC-IC-004 | TC-IC-001-4, TC-DB-IC-002 |
| IC-BR-020 Completude — descrição | UC-IC-002 | /activate, /completeness | AC-IC-011 | TC-IC-002-5, TC-API-IC-007 |
| IC-BR-021 Completude — grupo | UC-IC-001/002 | POST, /activate | AC-IC-001, 011 | TC-IC-001-1, TC-IC-002-5 |
| IC-BR-022 Completude — unidade | UC-IC-001/002 | POST, /activate | AC-IC-001, 011 | TC-IC-001-1, TC-IC-002-5 |
| IC-BR-023 Checklist fonte única | UC-IC-002 | /completeness | AC-IC-065 | TC-API-IC-007, TC-UX-IC-06 |
| IC-BR-030 Motivo de ativação | UC-IC-002 | /activate | AC-IC-010, 050 | TC-IC-002-4, TC-STC-IC-001 |
| IC-BR-031 Campos estruturais imutáveis | UC-IC-003 | PATCH /items/{id} | AC-IC-015 | TC-IC-003-2 |
| IC-BR-040 Sinônimo — cadastro | UC-IC-003 | POST .../synonyms | AC-IC-016 | TC-IC-003-3 |
| IC-BR-041 Sinônimo — unicidade normalizada | UC-IC-003 | POST .../synonyms | AC-IC-017 | TC-IC-003-4 |
| IC-BR-042 Busca tolerante (normalização) | UC-IC-007 | GET /items?q= | AC-IC-039 | TC-IC-007-5 |
| IC-BR-043 Sinônimo casa na busca | UC-IC-007 | GET /items?q= | AC-IC-038 | TC-IC-007-4 |
| IC-BR-050 Bloqueio de consumo de INACTIVE | UC-IC-004 | GET /items (consumidores) | AC-IC-026, 027 | TC-IC-004-2, TC-IC-004-3 |
| IC-BR-060 Parâmetros de reposição — cadastro | UC-IC-003 | PUT .../replenishment-parameters | AC-IC-020 | TC-IC-003-7 |
| IC-BR-061 Coerência mínimo ≤ máximo | UC-IC-003 | PUT .../replenishment-parameters | AC-IC-020 | TC-IC-003-7 |
| IC-BR-070 Descarte só de nunca consumido | UC-IC-006 | /discard | AC-IC-032, 033 | TC-IC-006-1, TC-IC-006-2 |
| IC-BR-080 CA obrigatório p/ EPI | UC-IC-002 | /activate | AC-IC-008, 049 | TC-IC-002-2, TC-TRV-IC-007 |
| IC-BR-081 CA válido e não vencido | UC-IC-002/003/005 | /activate, /reactivate, .../ca | AC-IC-009, 023, 030 | TC-IC-002-3, TC-IC-003-10, TC-IC-005-2 |
| IC-BR-082 Grade restrita ao SIZE_GRID | UC-IC-003 | PUT .../size-grid | AC-IC-019 | TC-IC-003-6 |
| IC-BR-083 Imagem via FD-001-03 | UC-IC-003 | POST .../image | AC-IC-021, 022 | TC-IC-003-8, TC-IC-003-9 |
| Transversal — autorização deny by default | Todos | Todos | AC-IC-043 | TC-TRV-IC-001 |
| Transversal — multiempresa (company_id) | UC-IC-007 | GET /items | AC-IC-046 | TC-TRV-IC-004, TC-IC-007-2 |
| Transversal — soft delete | UC-IC-001 | DELETE /items/{id} | AC-IC-045 | TC-TRV-IC-003 |
| Transversal — state machine exclusiva | Todos | Todos | AC-IC-047 | TC-TRV-IC-005 |
| Transversal — auditoria obrigatória | Todos | Todos | AC-IC-048 | TC-TRV-IC-006 |
| Transversal — concorrência otimista | UC-IC-002/003 | PATCH, /activate | AC-IC-044 | TC-TRV-IC-002, TC-IC-002-6 |

**Cobertura:** 27/27 regras com teste associado (100%). Todas as regras `Obrigatória` possuem cenário automatizado (DoD item 3).

## 14.2 AC × Teste (consolidado)

| ACs | Teste(s) |
|-----|----------|
| AC-IC-001..006 (UC-IC-001) | TC-IC-001-1 .. TC-IC-001-6 |
| AC-IC-007..013 (UC-IC-002) | TC-IC-002-1 .. TC-IC-002-7 |
| AC-IC-014..024 (UC-IC-003) | TC-IC-003-1 .. TC-IC-003-11 |
| AC-IC-025..028 (UC-IC-004) | TC-IC-004-1 .. TC-IC-004-4 |
| AC-IC-029..031 (UC-IC-005) | TC-IC-005-1 .. TC-IC-005-3 |
| AC-IC-032..035 (UC-IC-006) | TC-IC-006-1 .. TC-IC-006-4 |
| AC-IC-036..042 (UC-IC-007) | TC-IC-007-1 .. TC-IC-007-8 |
| AC-IC-043..049 (transversais) | TC-TRV-IC-001 .. TC-TRV-IC-007 (+ TC-IC-002-6, TC-IC-007-2) |
| AC-IC-050..052 (ciclo de vida) | TC-STC-IC-001 .. TC-STC-IC-003 |
| AC-IC-053..055 (eventos) | TC-EVT-IC-001 .. TC-EVT-IC-006 |
| AC-IC-056..058 (notificações) | TC-NTF-IC-001 .. TC-NTF-IC-003 |
| AC-IC-059..065 (API) | TC-API-IC-001 .. TC-API-IC-007 |
| AC-IC-066..067 (UI×API) | TC-UI-IC-001, TC-UI-IC-002 (+ TC-UX-IC-01) |
| AC-IC-068..070 (NFR/segurança) | TC-PRF-IC-001, TC-PRF-IC-002, TC-SEC-IC-001..008 |
| AC-IC-071..072 (i18n/MV) | TC-NTF-IC-004, TC-PRF-IC-003 |

Critérios por referência: **CA-UX-IC-01/02** (revisão cruzada com evidência), **CA-UX-IC-03..09** (TC-UX-IC-01..07), **CA-WF-IC-01..06** (TC-WFR-IC-001).

**Cobertura:** 72/72 ACs com ao menos um cenário (100%). 100% dos P0 com cenário automatizável ou evidência formal definida.

---

# 15. Alinhamento com o DoD do Módulo

| Item DoD (MMS-002 README) | Atendimento neste documento |
|--------------------------|------------------------------|
| 1. Documentos MMS-002-01 a 17 Approved | MMS-002-17 é o último documento do pacote |
| 2. Cobertura de testes de todas as regras obrigatórias | Seção 14.1 (27/27 regras) |
| 3. Toda regra `Obrigatória` com teste automatizado | Seção 14.1 — coluna de testes, todos automatizados |
| 4. Matriz regra × UC × API × teste 100% preenchida | Seções 14.1 e 14.2 |
| 5. Revisão de segurança SEC-001/003 sem pendências críticas | TC-SEC-IC-008 + cenários TC-SEC-IC-001..007 |
| 6. Eventos validados (payload, idempotência, DLQ) | Seção 7 (TC-EVT-IC-001..006) |
| 7. Migrations versionadas e reversíveis | TC-DB-IC-001 |

---

# 16. Gestão de Evidências e Falhas

1. **Evidências automatizadas:** relatórios de pipeline (xUnit/Playwright/k6) anexados ao release; nenhum release com cenário P0 vermelho.
2. **Evidências formais (manuais):** CA-UX-IC-01/02, TC-WFR-IC-001 (parte de revisão), TC-SEC-IC-008 — relatório assinado pelo responsável e arquivado com o release.
3. **Falhas:** todo cenário reprovado gera defeito classificado **P0–P4** (padrão de QA do projeto); P0/P1 bloqueiam release, P2 com plano de correção, P3/P4 backlog.
4. **Regressão:** cenários deste catálogo entram na suíte de regressão; nenhum cenário é removido sem revisão deste documento (conflito → documentação vence).
5. **Manutenção:** alteração de comportamento documentado exige atualização prévia da fonte (regra/UC/AC) e deste catálogo na mesma revisão.

---

# 17. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | TC-IC-001..007 (44 cenários + 1 ★), TC-TRV-IC, TC-STC-IC, TC-EVT-IC, TC-NTF-IC, TC-API-IC, TC-UX-IC, TC-WFR-IC, TC-UI-IC, TC-SEC-IC, TC-PRF-IC, TC-DB-IC — 100% dos AC-IC-001..072 |
| **v1.1** | Cenários de importação em lote do ERP, exportação CSV e sugestão de duplicatas; promoção dos cenários P2 |
| **v2.0** | Cenários de painel de higiene do catálogo, webhooks e bulk lifecycle (roadmaps MMS-002-13/14) |

---

# 18. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação dos Test Scenarios do módulo: catálogo completo de cenários (45 TC-IC por caso de uso, transversais, ciclo de vida, eventos, notificações, API, UX, wireframes, UI×API, segurança, performance e banco), estratégia de automação por camada, dados e ambientes, matriz de rastreabilidade 100% (regra × UC × API × AC × teste e AC × teste) e alinhamento integral ao DoD — **fecha o pacote MMS-002 (17/17 documentos)** |
