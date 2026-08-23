**Documento:** MMS-004-17 — Test Scenarios
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-23
**Dependências:** MMS-004 (Visão do Módulo — DoD), MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-05 (Event Storming), MMS-004-07 (Use Cases), MMS-004-09 (Permissions), MMS-004-10 (Notifications), MMS-004-11 (Database Model), MMS-004-13 (API), MMS-004-14 (UX), MMS-004-15 (Wireframes), MMS-004-16 (Acceptance Criteria)
**Referências:** SEC-001, SEC-003, FD-001 (Foundation), ADR-009, ADR-010, GOV-001, MMS-002-17 (Test Scenarios do Item Catalog — padrão de formato), PR-001-17

---

# 1. Objetivo

Este documento é o **catálogo oficial de cenários de teste** do módulo Inventory Management. Ele transforma os 89 critérios de aceite do MMS-004-16 em cenários executáveis, fecha a **matriz de rastreabilidade regra × UC × API × AC × teste** e a **prova de integridade** exigidas pelo DoD (itens 3 e 4), e define a estratégia de automação, dados, ambientes e evidências.

Regras de vínculo:

- Nenhum cenário inventa comportamento: todo cenário deriva de fonte documentada (regra, UC, state machine, evento, permissão, API, UX, wireframe ou NFR).
- Cenários já listados nos casos de uso (TC-IV-xxx-y, MMS-004-07) são **incorporados e formalizados** aqui, com mapeamento para os AC-IV — o MMS-004-07 permanece como origem do resumo funcional.
- Todo critério **P0** possui ao menos um cenário automatizável; toda regra `Obrigatória` possui ao menos um cenário de teste automatizado associado.

---

# 2. Convenções

## 2.1 Identificação dos cenários

| Prefixo | Domínio do cenário | Origem principal |
|---------|-------------------|------------------|
| `TC-IV-xxx-y` | Cenário funcional do caso de uso UC-IV-xxx | MMS-004-07 |
| `TC-TRV-IV-nnn` | Regras transversais (integridade de saldo, autorização, concorrência, multiempresa, state machine, auditoria com saldos) | MMS-004-16, seção 3 |
| `TC-STC-IV-nnn` | Ciclo de vida e estados das 4 entidades | MMS-004-03, MMS-004-16 seção 4 |
| `TC-EVT-IV-nnn` | Eventos e mensageria | MMS-004-05, ADR-010 |
| `TC-NTF-IV-nnn` | Notificações e escalonamentos | MMS-004-10, FD-001-05 |
| `TC-API-IV-nnn` | Contrato de API | MMS-004-13 |
| `TC-UX-IV-nn` | Experiência de usuário (CA-UX-IV) | MMS-004-14 |
| `TC-WFR-IV-nnn` | Fidelidade a wireframes (CA-WF-IV) | MMS-004-15 |
| `TC-UI-IV-nnn` | Integração UI × API | MMS-004-16, seção 7 |
| `TC-SEC-IV-nnn` | Segurança (SoD, anti-IDOR, segregação, LGPD) | SEC-001, SEC-003, MMS-004-09 |
| `TC-PRF-IV-nnn` | Performance, concorrência e observabilidade | MMS-004/NFR |
| `TC-DB-IV-nnn` | Banco de dados, migrations e prova de integridade | MMS-004-11 |

Códigos são imutáveis após publicação; cenários novos recebem o próximo sequencial do prefixo.

## 2.2 Camadas de execução

| Camada | Sigla | Ferramenta-alvo | Escopo |
|--------|-------|-----------------|--------|
| Unidade | UNIT | xUnit (.NET) / Vitest (frontend) | Regras de domínio, StockBalanceService, factories, specifications, value objects |
| Integração | INT | xUnit + Testcontainers (PostgreSQL, Redis, RabbitMQ) | Repositórios, outbox, triggers, constraints, views, serialização por chave de saldo |
| Contrato/API | API | Testes de integração HTTP (WebApplicationFactory) | Endpoints, envelope, erros, headers, idempotência |
| Mensageria | MSG | Testcontainers RabbitMQ | Retry, DLQ, idempotência de consumidor, ordem por chave de saldo, eventos consumidos (MMS-003/005/002) |
| Jobs | JOB | xUnit + relógio controlado | Vencimento de reserva (TMR-IV-001), janela de alerta (TMR-IV-002), SLA de ajuste (TMR-IV-003), rotina diária (TMR-IV-004) |
| Interface | UI | Playwright | Telas, fluxos de usuário, estados de tela, acessibilidade |
| End-to-end | E2E | Playwright + ambiente integrado | Jornadas cross-módulo (recebimento → entrada → reserva → atendimento) |
| Performance | PERF | k6 | Carga de referência, percentis, concorrência de saldo |
| Segurança | SEC | ZAP/Burp + testes automatizados | OWASP ASVS, revisão SEC-001/003, SoD |

## 2.3 Automação e prioridade

- **Automatizado obrigatório:** todo cenário vinculado a AC **P0** e toda regra `Obrigatória`.
- **Automatizado desejável:** cenários de AC **P1**; exceções toleradas exigem registro com dono e prazo (MMS-004-16, seção 10).
- **Manual com evidência formal:** revisões cruzadas (CA-UX-IV-01/02, CA-WF-IV), auditoria de acessibilidade assistida, revisão de segurança e a **verificação de privilégios de banco** da prova de integridade — evidência anexada ao release.
- Prioridade do cenário = prioridade do AC vinculado (P0/P1/P2).

## 2.4 Template de especificação

Cada cenário é executado com: **objetivo → pré-condições → dados de teste → passos → resultado esperado → AC/regra vinculada → camada → automação**. As tabelas deste documento trazem a forma resumida (objetivo + vínculos); a forma expandida é gerada no repositório de testes a partir deste catálogo, sem divergir dele.

---

# 3. Estratégia de Testes

## 3.1 Pirâmide

| Nível | Participação esperada | Conteúdo |
|-------|----------------------|----------|
| UNIT/INT | ~60% | StockBalanceService, regras, invariantes, repositórios, constraints, triggers, jobs |
| API/MSG | ~30% | Contratos, eventos publicados e consumidos, notificações, segurança de API |
| UI/E2E | ~10% | Jornadas críticas (fila → atendimento; recebimento → entrada), telas, contagem |

## 3.2 Ambientes

| Ambiente | Uso |
|----------|-----|
| Local (Testcontainers) | UNIT, INT, API, MSG, JOB em pipeline de PR |
| QA integrado | UI, E2E, MSG ponta a ponta com MMS-002/003/005 simulados, i18n |
| Staging (espelho) | PERF (incluindo concorrência de saldo), SEC, revisão de release, migrations reversíveis, reconstrução da projeção |

## 3.3 Dados de teste

- **Massa sintética versionada** no repositório de testes: 2 empresas (isolamento multiempresa); 7 usuários (Operator, Supervisor, Manager, Admin, Auditor, Requester — para testes de negação — e 1 sem papéis); estrutura de locais com 2 almoxarifados × 2 depósitos × endereços (uma empresa com `addressing.level = deposit`, outra `address`); catálogo mínimo de 12 itens (estocável com mínimo parametrizado, item com grade de tamanhos EPI, item inativado com saldo, item sem parâmetros, item de outro contrato — segregação); saldos iniciais **carregados exclusivamente por documentos de carga inicial**; reservas nos 4 estados; ajustes nos 4 estados; 1 inventário por estado; motivos de ajuste no Master Data.
- **Proibição:** dados reais ou pseudonimizados de produção em qualquer ambiente de teste (LGPD, SEC-001).
- Reset por cenário via transação ou seed idempotente; nenhum cenário depende de estado deixado por outro; o relógio dos jobs é controlado (nunca `sleep`).

## 3.4 Critérios de entrada e saída

- **Entrada:** build verde, migrations aplicadas, massa sintética carregada por documentos, secrets de teste no vault de CI.
- **Saída (release):** 100% dos cenários P0 automatizados e verdes; exceções P1 registradas; evidências formais anexadas; revisão SEC sem pendências críticas (AC-IV-086); **prova de integridade verde** (TC-DB-IV-001/002 + TC-TRV-IV-001).

---

# 4. Cenários por Caso de Uso (TC-IV)

Origem: MMS-004-07 (resumo funcional). Aqui cada cenário é vinculado ao seu AC (MMS-004-16). Cenários marcados com ★ foram **acrescentados** neste documento para fechar a cobertura de ACs sem cenário correspondente — não alteram nenhum UC.

## 4.1 UC-IV-001 — Registrar Entrada

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-001-1 | Entrada por recebimento conferido → Confirmado + EVT-IV-001 + saldo efetivado | AC-IV-001 | MSG + INT |
| TC-IV-001-2 | Entrada avulsa válida com referência → idem | AC-IV-002 | API + INT |
| TC-IV-001-3 | Item inativo → IV-ERR-010, permanece Rascunho | AC-IV-003 | API |
| TC-IV-001-4 | Item com grade sem tamanho → IV-ERR-120 | AC-IV-004 | API |
| TC-IV-001-5 | Reenvio idempotente da mesma origem → mesmo documento, sem efeito duplicado | AC-IV-005 | API + INT |
| TC-IV-001-6 | Entrada normaliza alerta de ruptura vigente | AC-IV-006 | MSG + INT |

## 4.2 UC-IV-002 — Registrar Saída (Atendimento)

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-002-1 | Atendimento total → Confirmado + EVT-IV-002 + EVT-IV-006, reserva Atendida | AC-IV-007 | API + MSG |
| TC-IV-002-2 | Atendimento parcial → reserva Ativa com saldo restante e mesmo `expiresAt` | AC-IV-008 | API + INT |
| TC-IV-002-3 | Saldo insuficiente → IV-ERR-020 com disponível × solicitado, sem efeito parcial | AC-IV-009 | API + INT |
| TC-IV-002-4 | Reserva vencida → IV-ERR-030 com orientação de nova reserva | AC-IV-010 | API |
| TC-IV-002-5 | Segregação violada → IV-ERR-070 + auditoria, sem vazamento de disponibilidade | AC-IV-011 | API |
| TC-IV-002-6 | Saída zera saldo com demanda aberta → EVT-IV-016 disparado sem bloquear | AC-IV-014 | MSG |
| TC-IV-002-7 | Saída avulsa bloqueada por configuração → IV-ERR-090; habilitada → motivo + centro de custo + auditoria reforçada | AC-IV-013 | API |
| TC-IV-002-8 ★ | Tamanho da entrega ≠ tamanho reservado → IV-ERR-121 + orientação (liberar + nova reserva) | AC-IV-012 | API |

## 4.3 UC-IV-003 — Criar Reserva

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-003-1 | Reserva automática por solicitação aprovada → Ativa + EVT-IV-005; validação só pelo disponível | AC-IV-015 | MSG + INT |
| TC-IV-003-2 | Reserva parcial por disponibilidade → não atendido sinalizado ao MMS-003 (rota mista) | AC-IV-016 | MSG |
| TC-IV-003-3 | Sem saldo disponível → IV-ERR-020 | AC-IV-017 | API |
| TC-IV-003-4 | Reserva com tamanho (EPI) → chave de saldo com tamanho; disponível reduzido, físico inalterado | AC-IV-018, AC-IV-019 | API + INT |

## 4.4 UC-IV-004 — Liberar e Vencer Reserva

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-004-1 | Liberação manual → Liberada + EVT-IV-007 + saldo devolvido | AC-IV-020 | API + INT |
| TC-IV-004-2 | Vencimento por timer → Vencida por documento de liberação + EVT-IV-008, em ≤ 15 min | AC-IV-021, AC-IV-088 | JOB + INT |
| TC-IV-004-3 | Cancelamento da solicitação → liberação automática de todas as reservas (POL-IV-08) | AC-IV-022 | MSG |
| TC-IV-004-4 | Alerta de vencimento 24h antes (TMR-IV-002), uma única vez | AC-IV-023 | JOB + NTF |
| TC-IV-004-5 | Liberação de reserva já atendida/liberada/vencida → IV-ERR-090 (terminais) | AC-IV-024 | API |

## 4.5 UC-IV-005 — Transferência entre Locais

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-005-1 | Transferência válida → efeito atômico + EVT-IV-003, saldo global inalterado | AC-IV-025 | API + INT |
| TC-IV-005-2 | Origem sem saldo → IV-ERR-020 | AC-IV-025 | API |
| TC-IV-005-3 | Origem = destino → IV-ERR-050 | AC-IV-026 | API |
| TC-IV-005-4 | Falha técnica → nenhum efeito parcial (rollback completo) | AC-IV-027 | INT |
| TC-IV-005-5 ★ | Saldo dedicado transferido preserva cliente/contrato no destino | AC-IV-028 | INT |

## 4.6 UC-IV-006 — Registrar e Aprovar Ajuste

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-006-1 | Registro → Pendente + EVT-IV-009 + notificação ao aprovador | AC-IV-029 | API + MSG |
| TC-IV-006-2 | Aprovação válida → efeito aplicado por documento + EVT-IV-010 + auditoria completa | AC-IV-030 | API + INT |
| TC-IV-006-3 | SoD violado (aprovador = registrador) → IV-ERR-085 + auditoria + alerta de segurança | AC-IV-031 | API |
| TC-IV-006-4 | Ajuste negativo sem saldo na aprovação → IV-ERR-084, permanece Pendente | AC-IV-032 | API |
| TC-IV-006-5 | Rejeição sem motivo → IV-ERR-086; com motivo → EVT-IV-011 sem efeito | AC-IV-033 | API + MSG |
| TC-IV-006-6 | SLA estourado (24h) → ESC-IV-001 ao Gestor | AC-IV-034 | JOB + NTF |

## 4.7 UC-IV-007 — Executar Inventário

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-007-1 | Abertura → EVT-IV-012 + lista de contagem cega materializada + notificação ao escopo | AC-IV-035, AC-IV-036 | API + MSG |
| TC-IV-007-2 | Contagem sem divergência → fechamento direto + EVT-IV-014 + acuracidade 100% | AC-IV-040 | API + INT |
| TC-IV-007-3 | Divergência → ajuste vinculado gerado; fechamento após aprovação | AC-IV-038 | API + INT |
| TC-IV-007-4 | Fechamento com divergência pendente → IV-ERR-102 com pendências listadas | AC-IV-039 | API |
| TC-IV-007-5 | Lançamento fora do escopo → IV-ERR-100; contagem nunca altera saldo | AC-IV-036, AC-IV-037 | API + INT |
| TC-IV-007-6 | Cancelamento com motivo → ST-IV-033, lançamentos preservados, sem ajustes | AC-IV-041 | API + INT |

## 4.8 UC-IV-008 — Estornar Movimentação

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-008-1 | Estorno de saída → saldo devolvido + EVT-IV-004 + original Estornado; ambos no extrato | AC-IV-042 | API + INT |
| TC-IV-008-2 | Estorno de entrada sem saldo (efeito inverso negativo) → IV-ERR-113 | AC-IV-043 | API |
| TC-IV-008-3 | Duplo estorno / documento não Confirmado → IV-ERR-110 | AC-IV-044 | API |
| TC-IV-008-4 | Estorno sem motivo (ou < 10 caracteres) → IV-ERR-112 | AC-IV-045 | API |
| TC-IV-008-5 | Estorno de ajuste pelo aprovador (SoD) → IV-ERR-085 + auditoria | AC-IV-046 | API |

## 4.9 UC-IV-009 — Gerenciar Locais

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-009-1 | Cadastro válido com hierarquia → persistido + auditado | AC-IV-047 | API + INT |
| TC-IV-009-2 | Ciclo na hierarquia / tipo incompatível → IV-ERR-062 | AC-IV-048 | API |
| TC-IV-009-3 | Inativação com saldo ou reservas → IV-ERR-063 com posição exibida | AC-IV-049 | API |
| TC-IV-009-4 | Código duplicado → IV-ERR-061 | AC-IV-048 | API |
| TC-IV-009-5 ★ | Local inativo referenciado em nova movimentação → IV-ERR-060; histórico consultável | AC-IV-050 | API |

## 4.10 UC-IV-010 — Tratar Alertas

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-010-1 | Saída atinge mínimo → EVT-IV-015 + notificação, sem bloquear a movimentação | AC-IV-051 | MSG + NTF |
| TC-IV-010-2 | Saída zera saldo com demanda → EVT-IV-016 (prioridade alta) | AC-IV-053 | MSG + NTF |
| TC-IV-010-3 | Deduplicação: segundo cruzamento com alerta Aberto → sem novo alerta | AC-IV-052 | MSG + INT |
| TC-IV-010-4 | Entrada normaliza alerta automaticamente | AC-IV-054 | MSG + INT |
| TC-IV-010-5 | Ruptura 48h sem normalização → ESC-IV-004 ao Gestor | AC-IV-055 | JOB + NTF |

## 4.11 UC-IV-011 — Consultar Posição e Extrato

| Cenário | Objetivo (resumo) | AC | Camada |
|---------|-------------------|-----|--------|
| TC-IV-011-1 | Posição por item/local → físico/reservado/disponível corretos e consistentes | AC-IV-056 | API + INT |
| TC-IV-011-2 | Extrato com trilha até documento de origem + saldos anterior/posterior por linha | AC-IV-057 | API |
| TC-IV-011-3 | Visão do almoxarifado com todos os filtros IV-BR-097 | AC-IV-056 + CA-UX-IV-07 | API + UI |
| TC-IV-011-4 | Fallback de cache → consulta ao banco sem erro; degradação registrada | AC-IV-059 | API + INT |
| TC-IV-011-5 | Paginação keyset: 2 páginas sem duplicidade nem omissão | AC-IV-077 | API |
| TC-IV-011-6 | Perfil restrito não vê consumo por colaborador (LGPD) | AC-IV-060 | API |
| TC-IV-011-7 ★ | Perfil Requester → 404 na posição/extrato/fila + alerta de segurança | AC-IV-058 | API + SEC |

---

# 5. Cenários de Regras Transversais (TC-TRV-IV)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-TRV-IV-001 | **Prova de integridade (parte funcional):** varredura de todos os endpoints, jobs e consumidores de evento — nenhum caminho altera `stock_balance` sem documento confirmado; testes negativos de escrita direta | AC-IV-061 | API + INT | Automatizado (P0) |
| TC-TRV-IV-002 | Toda confirmação referencia origem verificável; documento sem origem → recusado | AC-IV-062 | API | Automatizado (P0) |
| TC-TRV-IV-003 | Invariante contínuo: `total ≥ 0`, `reservado ≥ 0`, `reservado ≤ total`, `disponível = total − reservado` após qualquer sequência de operações (property-based) | AC-IV-063 | UNIT + INT | Automatizado (P0) |
| TC-TRV-IV-004 | Fluxo de autorização completo: escopo(404) → RBAC → ABAC → delegação(403), deny by default em cada operação de escrita | AC-IV-064 | API | Automatizado (P0) |
| TC-TRV-IV-005 | Concorrência de saldo: N confirmações simultâneas na mesma chave → serializadas; saldo final = soma exata dos efeitos; concorrente recebe IV-ERR-409 orientado | AC-IV-065 | INT + PERF | Automatizado (P0) |
| TC-TRV-IV-006 | Filtro obrigatório por `company_id` em toda consulta (inclusive views e projeção); saldo nunca cruza empresas | AC-IV-066 | INT + API | Automatizado (P0) |
| TC-TRV-IV-007 | Auditoria 100%: amostragem exaustiva das confirmações gera registro imutável com correlationId e saldo anterior/posterior por linha; indisponibilidade de auditoria aborta a operação (fail-closed) | AC-IV-067 | INT | Automatizado (P0) |

---

# 6. Cenários de Ciclo de Vida e Estados (TC-STC-IV)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-STC-IV-001 | Matrizes de transição das 4 entidades (documento, reserva, ajuste, inventário): toda transição fora da matriz → impossível por API, banco e job | AC-IV-068 | API + INT | Automatizado (P0) |
| TC-STC-IV-002 | Documento Confirmado imutável: toda edição (API e UPDATE direto) recusada; única saída é o estorno vinculado | AC-IV-069 | API + INT | Automatizado (P0) |
| TC-STC-IV-003 | Transições concorrentes na mesma entidade: uma aceita, outra IV-ERR-409, estado final consistente | AC-IV-070 | API + INT | Automatizado (P1) |

---

# 7. Cenários de Eventos e Mensageria (TC-EVT-IV)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-EVT-IV-001 | Envelope oficial completo em todos os eventos EVT-IV-001..016 | AC-IV-071 | MSG | Automatizado (P0) |
| TC-EVT-IV-002 | Outbox na mesma transação: falha após commit não gera evento órfão nem perde evento; rollback não publica | AC-IV-071 | INT | Automatizado (P0) |
| TC-EVT-IV-003 | Eventos com efeito de saldo carregam `balanceBefore`/`balanceAfter` por linha | AC-IV-072 | MSG | Automatizado (P0) |
| TC-EVT-IV-004 | Retry 5x exponencial; esgotado → `trino.materials.dlq`; evento crítico em DLQ dispara ESC-IV-002 | AC-IV-073 | MSG | Automatizado (P0) |
| TC-EVT-IV-005 | Idempotência de consumidor por (eventId, consumerName): reentrega não duplica efeitos | AC-IV-073 | MSG | Automatizado (P0) |
| TC-EVT-IV-006 | Ordem preservada por aggregateId e por balanceKey (saída + baixa de reserva com correlationId compartilhado) | AC-IV-074 | MSG | Automatizado (P0) |
| TC-EVT-IV-007 | Eventos consumidos (Recebimento conferido, Solicitação aprovada/cancelada, Item inativado): reação correta e idempotente por origem | AC-IV-001, 015, 022 + POL-IV-06 | MSG | Automatizado (P0) |

---

# 8. Cenários de Notificações (TC-NTF-IV)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-NTF-IV-001 | Despacho exclusivo pelo Notification Center (FD-001-05); nenhuma notificação direta do módulo | AC-IV-075 | MSG + inspeção | Automatizado (P0) |
| TC-NTF-IV-002 | Falha total de notificação → confirmação de movimentação não bloqueia (IV-BR-092) | AC-IV-075 | MSG | Automatizado (P0) |
| TC-NTF-IV-003 | Cadeias de escalonamento (ESC-IV-001..004) disparadas nos prazos corretos com referência à etapa anterior | AC-IV-034, 055 | JOB + NTF | Automatizado (P1) |
| TC-NTF-IV-004 | Conteúdo renderizado no idioma do usuário (pt-BR/en-US); dados de consumo por colaborador nunca no corpo (NOT-IV-BR-008) | AC-IV-089 | MSG | Automatizado (P1) |

---

# 9. Cenários de API (TC-API-IV)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-API-IV-001 | Envelope oficial: sucesso `{data, page, correlationId}`; erro `{error{code, message, details, correlationId}}` com números do conflito nos details | AC-IV-076 | API | Automatizado (P0) |
| TC-API-IV-002 | Keyset com cursor assinado de 15 min; offset rejeitado; cursor inválido/expirado → IV-ERR-400 | AC-IV-077 | API | Automatizado (P0) |
| TC-API-IV-003 | `Idempotency-Key` repetida em 24h → resultado original sem reexecutar efeitos de saldo | AC-IV-078 | API | Automatizado (P0) |
| TC-API-IV-004 | Mass assignment: campos fora da allowlist rejeitados/ignorados; `status`, `number`, `balanceBefore/After`, `version` read-only | AC-IV-079 | API | Automatizado (P0) |
| TC-API-IV-005 | Identificador fora do escopo → 404 (anti-enumeração), nunca 403; tentativa auditável | AC-IV-080 | API | Automatizado (P0) |
| TC-API-IV-006 | Rate limit por classe (600/240/60/1200 req-min): excedente → 429 com `Retry-After` + IV-ERR-429 | AC-IV-081 | API | Automatizado (P1) |
| TC-API-IV-007 | Inexistência de endpoints proibidos: nenhuma rota de escrita de saldo, nenhuma rota de vencimento de reserva (contrato OpenAPI diff) | AC-IV-061 | API | Automatizado (P0) |

---

# 10. Cenários de UX (TC-UX-IV) e Fidelidade a Wireframes (TC-WFR-IV)

Origem: MMS-004-14 (CA-UX-IV) e MMS-004-15 (CA-WF-IV). CA-UX-IV-01 e CA-UX-IV-02 são verificados por **revisão cruzada com evidência formal** (ações × UCs/endpoints; mensagens × catálogo) — não têm cenário automatizado, mas são pré-requisito de release.

| Cenário | Objetivo | Critério | Camada | Automação |
|---------|----------|----------|--------|-----------|
| TC-UX-IV-01 | Ações não permitidas ocultadas por papel; menu do Estoque invisível ao Requester | CA-UX-IV-03 / AC-IV-083 | UI | Automatizado (P0) |
| TC-UX-IV-02 | Nenhuma tela permite editar saldo (testes negativos de UI) | CA-UX-IV-04 | UI | Automatizado (P0) |
| TC-UX-IV-03 | Confirmações com resumo de efeito (saldo antes → depois) em todas as ações da tabela 6.6 do MMS-004-14 | CA-UX-IV-05 | UI | Automatizado (P0) |
| TC-UX-IV-04 | Bloqueios de integridade exibem números do conflito e ação corretiva | CA-UX-IV-06 | UI | Automatizado (P0) |
| TC-UX-IV-05 | Fila do almoxarifado: filtros IV-BR-097 completos + ordenação por vencimento + selo 24h | CA-UX-IV-07 | UI | Automatizado (P0) |
| TC-UX-IV-06 | SoD visível: aprovador não vê ação de decisão nos próprios ajustes | CA-UX-IV-08 | UI | Automatizado (P0) |
| TC-UX-IV-07 | Contagem cega: saldo do sistema não exibido durante o lançamento | CA-UX-IV-09 | UI | Automatizado (P0) |
| TC-UX-IV-08 | Conformidade WCAG 2.1 AA nas telas IV-SCR-01/02/03/04/05 (auditoria automatizada + revisão assistida) | CA-UX-IV-10 | UI | Automatizado + evidência (P0) |
| TC-UX-IV-09 | Estados de tela (carregando, vazio, erro, sem permissão, degradação de cache) em todas as telas | CA-UX-IV-11 | UI | Automatizado (P0) |
| TC-WFR-IV-001 | Revisão cruzada dos 8 wireframes (WF-IV-01..08) × telas implementadas: zonas, estados e variantes | CA-WF-IV-01..08 | UI + revisão | Automatizado + evidência (P0) |

## 10.1 Integração UI × API (TC-UI-IV)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-UI-IV-001 | Erro de API exibido com mensagem do catálogo (números do conflito incluídos) e código IV-ERR preservado | AC-IV-082 | UI | Automatizado (P0) |
| TC-UI-IV-002 | Ação indisponível pelo papel → ocultada na renderização (consistência com TC-UX-IV-01) | AC-IV-083 | UI | Automatizado (P0) |

---

# 11. Cenários de Segurança (TC-SEC-IV)

Derivados de SEC-001, SEC-003, MMS-004-09 e dos pontos de segurança distribuídos nos UCs (referência cruzada para não duplicar catálogo).

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-SEC-IV-001 | JWT ausente/expirado/inválido → 401; refresh rotativo com detecção de reuso (SEC-003) | AC-IV-086 | API | Automatizado (P0) |
| TC-SEC-IV-002 | Anti-enumeração/IDOR: recurso fora do escopo → 404 (referência: TC-API-IV-005, TC-IV-011-7) | AC-IV-080, 086 | API | Automatizado (P0) |
| TC-SEC-IV-003 | SoD não desligável: aprovação e estorno do próprio ajuste bloqueados para a pessoa, mesmo via delegação (DEL-IV-004) | AC-IV-031, 046, 086 | API | Automatizado (P0) |
| TC-SEC-IV-004 | Segregação sem vazamento: saldo dedicado de outro contrato tratado como inexistente em busca, validação e erro (referência: TC-IV-002-5) | AC-IV-011, 086 | API | Automatizado (P0) |
| TC-SEC-IV-005 | Negação do Requester à visão do almoxarifado gera alerta de segurança além da auditoria (referência: TC-IV-011-7) | AC-IV-058, 086 | API + SEC | Automatizado (P0) |
| TC-SEC-IV-006 | Mass assignment e campos calculados read-only (referência: TC-API-IV-004) | AC-IV-079, 086 | API | Automatizado (P0) |
| TC-SEC-IV-007 | Credenciais de serviço (MMS-003/005) restritas aos scopes mínimos; escalada de escopo recusada | AC-IV-086 | API | Automatizado (P0) |
| TC-SEC-IV-008 | Exportação de extrato auditada com recorte LGPD por perfil (referência: TC-IV-011-6) | AC-IV-060, 086 | API | Automatizado (P1) |
| TC-SEC-IV-009 | Revisão formal SEC-001 + SEC-003 (ASVS nível 2) sem pendências críticas — evidência anexada ao release | AC-IV-086 | SEC | Manual com evidência (P0) |

---

# 12. Cenários de Performance e Observabilidade (TC-PRF-IV)

| Cenário | Objetivo | AC | Camada | Automação |
|---------|----------|-----|--------|-----------|
| TC-PRF-IV-001 | Validação de disponibilidade e posição < 2s ponta a ponta (API p95 ≤ 500 ms); extrato ≤ 300 ms; confirmação ≤ 1s sob carga de referência (k6, staging) | AC-IV-084 | PERF | Automatizado (P0) |
| TC-PRF-IV-002 | Concorrência sob carga: 100 confirmações simultâneas em 10 chaves de saldo → zero inconsistência, lock waits monitorados | AC-IV-065 | PERF | Automatizado (P0) |
| TC-PRF-IV-003 | CorrelationId ponta a ponta: recebimento → entrada → alerta → notificação → auditoria rastreável por um único ID | AC-IV-085 | MSG + INT | Automatizado (P0) |
| TC-PRF-IV-004 | Job de vencimento processa reservas expiradas em ≤ 15 min mesmo com backlog | AC-IV-088 | JOB + PERF | Automatizado (P1) |

---

# 13. Cenários de Banco de Dados e Migrations (TC-DB-IV)

| Cenário | Objetivo | Origem | Camada | Automação |
|---------|----------|--------|--------|-----------|
| TC-DB-IV-001 | **Prova de integridade (parte estrutural):** triggers TRG-IV-003/004/005 e forbid-delete bloqueiam UPDATE de saldo sem documento novo, edição de documento confirmado e DELETE físico | AC-IV-061 | INT | Automatizado (P0) |
| TC-DB-IV-002 | **Prova de integridade (privilégios):** apenas `trino_inventory_app` possui escrita em `stock_balance`; verificação de grants no pipeline | AC-IV-061 | INT + evidência | Automatizado (P0) |
| TC-DB-IV-003 | Migrations versionadas aplicadas e reversíveis: apply + rollback em banco limpo (staging); rollback nunca afeta documentos confirmados | MMS-004-11 §15.12 | INT | Automatizado (P0) |
| TC-DB-IV-004 | Constraints de saldo: `total ≥ 0`, `reservado ≤ total`, `available` gerado; violação recusada no banco | AC-IV-063 | INT | Automatizado (P0) |
| TC-DB-IV-005 | Unicidades: número por empresa, origem idempotente, reserva ativa por chave, alerta aberto por chave, código de local | MMS-004-11 §15.1 | INT | Automatizado (P0) |
| TC-DB-IV-006 | Reconstrução da projeção (COMP-IV-004): rebuild a partir dos documentos = projeção corrente | AC-IV-087 | INT | Automatizado (P0) |
| TC-DB-IV-007 | Plano de indexação: EXPLAIN das consultas críticas usa os índices previstos (chave de saldo, expiring, extrato) | MMS-004-11 §15.10 | INT | Automatizado (P1) |
| TC-DB-IV-008 | Views VW-IV-001..003 e MV-IV-001 filtrando por company_id e retornando projeções corretas | MMS-004-11 §15.5/15.6 | INT | Automatizado (P1) |
| TC-DB-IV-009 | Numeração sequencial por empresa/tipo sem lacunas nem duplicatas sob concorrência | MMS-004-11 §15.7 | INT | Automatizado (P0) |

---

# 14. Matriz de Rastreabilidade Completa

## 14.1 Regra × UC × API × AC × Teste

| Regra(s) | UC | API | AC | Teste(s) |
|----------|-----|-----|-----|----------|
| IV-BR-001/002 (chave de saldo; efeito por linha) | Todos | Escrita | AC-IV-061, 063 | TC-TRV-IV-001/003, TC-DB-IV-001/004 |
| IV-BR-010 (item ativo) | UC-IV-001/003 | POST /movements, /reservations | AC-IV-003 | TC-IV-001-3 |
| IV-BR-011 (quantidade > 0) | Todos mov. | Escrita | AC-IV-002 | TC-IV-001-2, TC-DB-IV-004 |
| IV-BR-013 (referência de origem) | UC-IV-001 | POST /movements | AC-IV-062 | TC-IV-001-2, TC-TRV-IV-002 |
| IV-BR-020/021 (saldo disponível; efeito por linha) | UC-IV-002/005 | POST /movements, /transfers | AC-IV-009 | TC-IV-002-3, TC-IV-005-2 |
| IV-BR-030..034 (reserva: criação, coexistência, vínculo, limites) | UC-IV-002/003 | POST /reservations, /movements | AC-IV-007, 008, 010, 015..019 | TC-IV-002-1/2/4, TC-IV-003-1..4 |
| IV-BR-035..037 (liberação, validade, devolução) | UC-IV-004 | POST …/release; job | AC-IV-020..024 | TC-IV-004-1..5 |
| IV-BR-050..053 (transferência atômica) | UC-IV-005 | POST /transfers | AC-IV-025..028 | TC-IV-005-1..5 |
| IV-BR-060..063 (locais, hierarquia, inativação) | UC-IV-009 | /locations | AC-IV-047..050 | TC-IV-009-1..5 |
| IV-BR-070 (segregação; MMS-RG-10) | UC-IV-002/003/005 | Escrita | AC-IV-011, 028 | TC-IV-002-5, TC-IV-005-5, TC-SEC-IV-004 |
| IV-BR-080..086 (ajuste: motivo, SoD, saldo, rejeição) | UC-IV-006 | /adjustments | AC-IV-029..034 | TC-IV-006-1..6 |
| IV-BR-090 (idempotência; concorrência) | Todos | Escrita | AC-IV-005, 065, 078 | TC-IV-001-5, TC-TRV-IV-005, TC-API-IV-003 |
| IV-BR-091/092 (alertas: dedup, não bloqueio) | UC-IV-010 | /alerts | AC-IV-051..054, 075 | TC-IV-010-1..4, TC-NTF-IV-002 |
| IV-BR-095 (auditoria com saldos) | Todos | Todos | AC-IV-067, 072 | TC-TRV-IV-007, TC-EVT-IV-003 |
| IV-BR-096 (projeção nunca fonte de verdade) | UC-IV-011 | GET /balances | AC-IV-059, 087 | TC-IV-011-4, TC-DB-IV-006 |
| IV-BR-097 (visão do almoxarifado exclusiva) | UC-IV-011 | GET /movements, /reservations | AC-IV-058 | TC-IV-011-3/7, TC-SEC-IV-005 |
| IV-BR-098 (LGPD/retenção) | UC-IV-011 | GET/export | AC-IV-060 | TC-IV-011-6, TC-SEC-IV-008 |
| IV-BR-100..103 (inventário: escopo, apuração, tolerância, fechamento) | UC-IV-007 | /counts | AC-IV-035..041 | TC-IV-007-1..6 |
| IV-BR-110..115 (estorno) | UC-IV-008 | POST …/reverse | AC-IV-042..046 | TC-IV-008-1..5 |
| IV-BR-120/121 (grade/tamanho) | Todos mov. | Escrita (sizeCode) | AC-IV-004, 012, 019 | TC-IV-001-4, TC-IV-002-8, TC-IV-003-4 |
| Transversal — autorização deny by default | Todos | Todos | AC-IV-064 | TC-TRV-IV-004 |
| Transversal — multiempresa | Todos | Todos | AC-IV-066 | TC-TRV-IV-006 |
| Transversal — state machine exclusiva | Todos | Todos | AC-IV-068, 069 | TC-STC-IV-001/002 |

**Cobertura:** 100% das regras IV-BR referenciadas pelos casos de uso (matriz MMS-004-07 §8) com ao menos um teste automatizado associado; 16/16 eventos EVT-IV cobertos (seções 4 e 7).

## 14.2 AC × Teste (consolidado)

| ACs | Teste(s) |
|-----|----------|
| AC-IV-001..006 (UC-IV-001) | TC-IV-001-1 .. TC-IV-001-6 |
| AC-IV-007..014 (UC-IV-002) | TC-IV-002-1 .. TC-IV-002-8 |
| AC-IV-015..019 (UC-IV-003) | TC-IV-003-1 .. TC-IV-003-4 |
| AC-IV-020..024 (UC-IV-004) | TC-IV-004-1 .. TC-IV-004-5 |
| AC-IV-025..028 (UC-IV-005) | TC-IV-005-1 .. TC-IV-005-5 |
| AC-IV-029..034 (UC-IV-006) | TC-IV-006-1 .. TC-IV-006-6 |
| AC-IV-035..041 (UC-IV-007) | TC-IV-007-1 .. TC-IV-007-6 |
| AC-IV-042..046 (UC-IV-008) | TC-IV-008-1 .. TC-IV-008-5 |
| AC-IV-047..050 (UC-IV-009) | TC-IV-009-1 .. TC-IV-009-5 |
| AC-IV-051..055 (UC-IV-010) | TC-IV-010-1 .. TC-IV-010-5 |
| AC-IV-056..060 (UC-IV-011) | TC-IV-011-1 .. TC-IV-011-7 |
| AC-IV-061..067 (transversais) | TC-TRV-IV-001 .. TC-TRV-IV-007 (+ TC-DB-IV-001/002/004/006, TC-API-IV-007) |
| AC-IV-068..070 (ciclo de vida) | TC-STC-IV-001 .. TC-STC-IV-003 |
| AC-IV-071..075 (eventos/notificações) | TC-EVT-IV-001..007, TC-NTF-IV-001..004 |
| AC-IV-076..081 (API) | TC-API-IV-001 .. TC-API-IV-007 |
| AC-IV-082..083 (UI×API) | TC-UI-IV-001, TC-UI-IV-002 (+ TC-UX-IV-01) |
| AC-IV-084..089 (NFR/segurança/i18n) | TC-PRF-IV-001..004, TC-SEC-IV-001..009, TC-DB-IV-006, TC-NTF-IV-004 |

Critérios por referência: **CA-UX-IV-01/02** (revisão cruzada com evidência), **CA-UX-IV-03..11** (TC-UX-IV-01..09), **CA-WF-IV-01..08** (TC-WFR-IV-001).

**Cobertura:** 89/89 ACs com ao menos um cenário (100%). 100% dos P0 com cenário automatizável ou evidência formal definida.

---

# 15. Alinhamento com o DoD do Módulo

| Item DoD (MMS-004 README) | Atendimento neste documento |
|--------------------------|------------------------------|
| 1. Visão aprovada no registry | Concluído (MMS-004 v1.0.0) |
| 2. Documentos funcionais aprovados | MMS-004-17 é o último documento do pacote |
| 3. Matriz regra × movimentação × evento preenchida | Seção 14.1 (regras × UC × API × AC × teste; eventos 16/16) |
| 4. Prova de integridade: nenhum caminho altera saldo sem documento | TC-TRV-IV-001 (funcional) + TC-DB-IV-001 (triggers) + TC-DB-IV-002 (privilégios) + TC-API-IV-007 (contrato) + TC-DB-IV-006 (reconstrução) |

---

# 16. Gestão de Evidências e Falhas

1. **Evidências automatizadas:** relatórios de pipeline (xUnit/Playwright/k6) anexados ao release; nenhum release com cenário P0 vermelho.
2. **Evidências formais (manuais):** CA-UX-IV-01/02, TC-WFR-IV-001 (parte de revisão), TC-SEC-IV-009 e o relatório de verificação de privilégios (TC-DB-IV-002) — assinados pelo responsável e arquivados com o release.
3. **Falhas:** todo cenário reprovado gera defeito classificado **P0–P4** (padrão de QA do projeto); P0/P1 bloqueiam release, P2 com plano de correção, P3/P4 backlog. Falha na prova de integridade é sempre P0.
4. **Regressão:** cenários deste catálogo entram na suíte de regressão; nenhum cenário é removido sem revisão deste documento (conflito → documentação vence).
5. **Manutenção:** alteração de comportamento documentado exige atualização prévia da fonte (regra/UC/AC) e deste catálogo na mesma revisão.

---

# 17. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | TC-IV-001..011 (58 cenários + 4 ★), TC-TRV-IV, TC-STC-IV, TC-EVT-IV, TC-NTF-IV, TC-API-IV, TC-UX-IV, TC-WFR-IV, TC-UI-IV, TC-SEC-IV, TC-PRF-IV, TC-DB-IV — 100% dos AC-IV-001..089 |
| **v1.1** | Cenários de transferência em trânsito, inventário cíclico automático, compra dedicada com reserva automática, exportação CSV e relatórios; promoção dos cenários P2 |
| **v2.0** | Cenários de lote/validade/série, quarentena, rebalanceamento, reposição automática por ponto de pedido e operação por código de barras (roadmaps MMS-004-13/14) |

---

# 18. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-23 | Criação dos Test Scenarios do módulo: catálogo completo de cenários (62 TC-IV por caso de uso, transversais com prova de integridade em três frentes — funcional, estrutural e privilégios, ciclo de vida das 4 entidades, eventos com saldos por linha e camada de jobs com relógio controlado, notificações/escalonamentos, API, UX, wireframes, UI×API, segurança com SoD, performance com concorrência de saldo e banco com reconstrução da projeção), estratégia de automação por camada, dados carregados exclusivamente por documentos, matriz de rastreabilidade 100% (regra × UC × API × AC × teste, 89/89 ACs, 16/16 eventos) e alinhamento integral ao DoD — **fecha o pacote MMS-004 (17/17 documentos)** |
