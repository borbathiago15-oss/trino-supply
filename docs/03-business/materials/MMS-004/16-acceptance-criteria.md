**Documento:** MMS-004-16 — Acceptance Criteria
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-23
**Dependências:** MMS-004 (Visão do Módulo — NFR e DoD), MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-05 (Event Storming), MMS-004-07 (Use Cases), MMS-004-09 (Permissions), MMS-004-10 (Notifications), MMS-004-11 (Database Model), MMS-004-13 (API), MMS-004-14 (UX), MMS-004-15 (Wireframes)
**Referências:** SEC-001, SEC-003, FD-001 (Foundation), GOV-001, ADR-009, ADR-010, MMS-002-16 (Acceptance Criteria do Item Catalog — padrão de formato), PR-001-16

---

# 1. Objetivo

Este documento consolida os **critérios de aceite formais** do módulo Inventory Management em formato verificável, servindo como contrato de aceitação entre produto, engenharia e qualidade, e como insumo direto para o **MMS-004-17 — Test Scenarios**.

Regras de vínculo:

- Todo critério deste documento deriva de uma fonte documentada (UC, regra de negócio, state machine, permissão, evento, API, UX, wireframe ou NFR). Nenhum critério inventa comportamento.
- Critérios já definidos em documentos anteriores (CA-UX-IV-*, CA-WF-IV-*) são **referenciados, não reescritos**; este documento os incorpora por citação.
- A aprovação do módulo exige 100% dos critérios **P0** verificados (seção 11 — DoD), incluindo a **prova de integridade** de que nenhum caminho altera saldo sem documento (DoD do módulo, item 4).

## 1.1 Convenções

- **Formato:** Given/When/Then (Dado/Quando/Então).
- **Identificação:** `AC-IV-NNN`, sequencial, imutável após publicação.
- **Prioridade:**
  - **P0** — bloqueante para o MVP; falha impede release;
  - **P1** — obrigatório no MVP, com cenário de exceção tolerado e documentado;
  - **P2** — desejável no MVP, obrigatório na v1.1.
- **Verificação:** cada critério indica a camada de verificação esperada (API, UI, banco, evento, auditoria, job). A automação é especificada no MMS-004-17.

---

# 2. Critérios Funcionais por Caso de Uso

## 2.1 UC-IV-001 — Registrar Entrada de Estoque

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-001 | P0 | **Dado** um recebimento conferido (MMS-005) publicado, **Quando** o evento é consumido, **Então** o sistema gera o documento de entrada vinculado, confirma, efetiva o saldo por linha, emite EVT-IV-001 e registra auditoria com saldo anterior/posterior | evento + banco + auditoria |
| AC-IV-002 | P0 | **Dado** um Almoxarife com IV-PERM-001/002, **Quando** registra e confirma entrada avulsa válida com referência externa, **Então** o documento fica Confirmado, o saldo aumenta e EVT-IV-001 é emitido | API + banco + evento |
| AC-IV-003 | P0 | **Dado** um item inativo no catálogo, **Quando** incluído em nova entrada (exceto devolução), **Então** o sistema recusa com IV-ERR-010 e o documento permanece Rascunho | API |
| AC-IV-004 | P0 | **Dado** um item com grade de tamanhos, **Quando** a linha não informa o tamanho, **Então** o sistema recusa com IV-ERR-120 | API |
| AC-IV-005 | P0 | **Dado** o reenvio do mesmo recebimento (mesma chave de origem), **Quando** processado, **Então** o sistema retorna o documento já registrado, sem efeito duplicado no saldo, e audita a ocorrência (FA-IV-005) | API + banco + auditoria |
| AC-IV-006 | P0 | **Dado** um alerta de ruptura Aberto, **Quando** uma entrada restabelece o saldo, **Então** o alerta é normalizado automaticamente e os destinatários originais podem ser notificados (IV-NOT-013) | evento + notificação |

## 2.2 UC-IV-002 — Registrar Saída (Atendimento)

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-007 | P0 | **Dado** uma reserva Ativa, **Quando** a saída vinculada é confirmada pela quantidade total, **Então** o saldo total é reduzido, a reserva transiciona para Atendida e EVT-IV-002 + EVT-IV-006 são emitidos na mesma transação | API + banco + evento |
| AC-IV-008 | P0 | **Dado** um atendimento parcial, **Quando** confirmado, **Então** a reserva permanece Ativa com o saldo restante e o mesmo `expiresAt`; o MMS-003 é informado da entrega parcial | API + evento |
| AC-IV-009 | P0 | **Dado** saldo disponível insuficiente em uma linha, **Quando** a confirmação é tentada, **Então** o sistema recusa com IV-ERR-020 informando disponível × solicitado, sem nenhum efeito parcial (atomicidade) | API + banco |
| AC-IV-010 | P0 | **Dado** uma reserva vencida, **Quando** a saída vinculada é confirmada, **Então** o sistema recusa com IV-ERR-030 e orienta nova reserva | API |
| AC-IV-011 | P0 | **Dado** saldo dedicado a um cliente/contrato, **Quando** uma saída de outro cliente/contrato o referencia, **Então** o sistema bloqueia com IV-ERR-070, sem vazamento de disponibilidade, e audita | API + auditoria |
| AC-IV-012 | P0 | **Dado** um item com grade, **Quando** o tamanho da entrega difere do tamanho reservado, **Então** o sistema bloqueia com IV-ERR-121 e orienta liberação + nova reserva | API |
| AC-IV-013 | P0 | **Dado** `issue.without-reservation = false` (padrão), **Quando** uma saída avulsa é tentada, **Então** o sistema recusa com IV-ERR-090 e orientação; com `true`, exige motivo estruturado + centro de custo com auditoria reforçada | API + configuração |
| AC-IV-014 | P0 | **Dado** uma saída que zera o disponível de item com demanda aberta, **Quando** confirmada, **Então** EVT-IV-016 (ruptura) é emitido com prioridade alta sem bloquear a confirmação | evento + notificação |

## 2.3 UC-IV-003 — Criar Reserva

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-015 | P0 | **Dado** uma solicitação aprovada (MMS-003), **Quando** o evento é consumido, **Então** a validação usa somente o disponível (MMS-RG-09) e a reserva nasce Ativa com `expiresAt = now + reservation.ttl`, emitindo EVT-IV-005 | evento + banco |
| AC-IV-016 | P0 | **Dado** disponível menor que o solicitado, **Quando** a reserva é criada, **Então** o sistema reserva o disponível e sinaliza o não atendido ao MMS-003 (rota mista) | API + evento |
| AC-IV-017 | P0 | **Dado** disponível insuficiente em reserva manual, **Quando** submetida, **Então** o sistema recusa com IV-ERR-020 | API |
| AC-IV-018 | P0 | **Dado** a criação da reserva, **Quando** confirmada, **Então** o disponível diminui e o físico permanece inalterado; múltiplas reservas coexistem enquanto a soma ≤ disponível | banco |
| AC-IV-019 | P0 | **Dado** item com grade, **Quando** a reserva é criada, **Então** o tamanho é obrigatório e a chave de saldo inclui o tamanho | API + banco |

## 2.4 UC-IV-004 — Liberar e Vencer Reserva

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-020 | P0 | **Dado** uma reserva Ativa, **Quando** liberada manualmente, **Então** transiciona para Liberada, o saldo remanescente volta ao disponível e EVT-IV-007 é emitido | API + banco + evento |
| AC-IV-021 | P0 | **Dado** `expiresAt` atingido, **Quando** o job de vencimento processa, **Então** a reserva transiciona para Vencida por documento de liberação (nunca escrita direta), EVT-IV-008 é emitido e MMS-003 + envolvidos são notificados | job + banco + evento |
| AC-IV-022 | P0 | **Dado** uma solicitação cancelada (MMS-003), **Quando** o evento é consumido, **Então** todas as reservas Ativas da solicitação são liberadas automaticamente (POL-IV-08) | evento + banco |
| AC-IV-023 | P0 | **Dado** uma reserva na janela pré-vencimento (24h), **Quando** o timer avalia, **Então** o alerta "reserva vencendo" (IV-NOT-003) é enviado uma única vez, sem interromper o fluxo | job + notificação |
| AC-IV-024 | P0 | **Dado** uma reserva Atendida/Liberada/Vencida, **Quando** qualquer transição é tentada, **Então** o sistema recusa com IV-ERR-090 (estados terminais) | API |

## 2.5 UC-IV-005 — Transferência entre Locais

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-025 | P0 | **Dado** origem e destino válidos e saldo disponível, **Quando** a transferência é confirmada, **Então** a baixa na origem e a entrada no destino ocorrem na mesma transação, o saldo global do item permanece inalterado e EVT-IV-003 é emitido | API + banco + evento |
| AC-IV-026 | P0 | **Dado** origem igual ao destino, **Quando** submetida, **Então** o sistema recusa com IV-ERR-050 | API |
| AC-IV-027 | P0 | **Dado** falha técnica durante a confirmação, **Quando** ocorre, **Então** nenhum efeito parcial persiste (rollback completo — nunca transferência "pela metade") | banco |
| AC-IV-028 | P0 | **Dado** saldo dedicado, **Quando** transferido, **Então** a segregação cliente/contrato é preservada no destino | banco |

## 2.6 UC-IV-006 — Registrar e Aprovar Ajuste

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-029 | P0 | **Dado** um Supervisor com IV-PERM-004, **Quando** registra ajuste com motivo estruturado e justificativa (mín. 10 caracteres), **Então** o ajuste nasce Pendente, EVT-IV-009 é emitido e os aprovadores são notificados | API + evento + notificação |
| AC-IV-030 | P0 | **Dado** um aprovador ≠ registrador com IV-PERM-005, **Quando** aprova, **Então** o saldo é revalidado, o efeito é aplicado por documento vinculado, EVT-IV-010 é emitido e a auditoria contém justificativa, registrador, aprovador e saldos anterior/posterior | API + banco + auditoria |
| AC-IV-031 | P0 | **Dado** o próprio registrador, **Quando** tenta aprovar seu ajuste, **Então** o sistema recusa com IV-ERR-085 (SoD não desligável), audita e gera alerta de segurança | API + auditoria |
| AC-IV-032 | P0 | **Dado** um ajuste negativo cujo saldo se tornou insuficiente entre registro e decisão, **Quando** aprovado, **Então** a aprovação é bloqueada com IV-ERR-084 e o ajuste permanece Pendente com motivo | API |
| AC-IV-033 | P0 | **Dado** uma rejeição, **Quando** submetida sem motivo, **Então** a decisão não é registrada (IV-ERR-086); com motivo, EVT-IV-011 é emitido, nenhum saldo muda e o registrador é notificado | API + evento |
| AC-IV-034 | P0 | **Dado** um ajuste Pendente além do SLA (24h), **Quando** o timer avalia, **Então** o escalonamento ESC-IV-001 notifica o Gestor e registra na Timeline | job + notificação |

## 2.7 UC-IV-007 — Executar Inventário

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-035 | P0 | **Dado** um Supervisor com IV-PERM-006, **Quando** abre inventário com escopo válido, **Então** a lista de chaves do escopo é materializada, EVT-IV-012 é emitido e o almoxarifado do escopo é notificado | API + evento + notificação |
| AC-IV-036 | P0 | **Dado** a contagem cega padrão (`count.blind`), **Quando** o Almoxarife registra contagens, **Então** o saldo do sistema não é exibido, cada lançamento grava o snapshot sistêmico e EVT-IV-013 é emitido; **nenhum saldo é alterado pela contagem** | API + UI + banco |
| AC-IV-037 | P0 | **Dado** um lançamento fora do escopo, **Quando** submetido, **Então** o sistema recusa com IV-ERR-100 | API |
| AC-IV-038 | P0 | **Dado** divergências acima da tolerância, **Quando** os lançamentos são encerrados, **Então** o sistema gera ajustes vinculados ao inventário que seguem o fluxo completo de aprovação (UC-IV-006) | API + banco |
| AC-IV-039 | P0 | **Dado** divergência sem ajuste concluído nem justificativa, **Quando** o fechamento é tentado, **Então** o sistema recusa com IV-ERR-102 listando as pendências | API |
| AC-IV-040 | P0 | **Dado** o fechamento válido, **Quando** confirmado, **Então** EVT-IV-014 é emitido com sumário e a acuracidade do ciclo é registrada (KPI ≥ 98%) | API + evento |
| AC-IV-041 | P1 | **Dado** um cancelamento com motivo, **Quando** confirmado, **Então** o inventário transiciona para Cancelado, os lançamentos são preservados para auditoria e nenhum ajuste é gerado | API + banco |

## 2.8 UC-IV-008 — Estornar Movimentação

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-042 | P0 | **Dado** um documento Confirmado não estornado, **Quando** estornado com motivo (mín. 10 caracteres) por usuário com IV-PERM-007, **Então** um novo documento de efeito inverso nasce Confirmado, o original transiciona para Estornado, EVT-IV-004 é emitido e ambos permanecem consultáveis no extrato | API + banco + evento |
| AC-IV-043 | P0 | **Dado** um estorno cujo efeito inverso geraria saldo negativo, **Quando** tentado, **Então** o sistema recusa com IV-ERR-113 indicando o saldo atual | API |
| AC-IV-044 | P0 | **Dado** um documento já estornado ou não Confirmado, **Quando** o estorno é tentado, **Então** o sistema recusa com IV-ERR-110 | API |
| AC-IV-045 | P0 | **Dado** um estorno sem motivo, **Quando** submetido, **Então** o sistema recusa com IV-ERR-112 | API |
| AC-IV-046 | P0 | **Dado** um ajuste aprovado, **Quando** o próprio aprovador tenta estorná-lo, **Então** o sistema recusa com IV-ERR-085 (SoD IV-BR-114) e audita | API + auditoria |

## 2.9 UC-IV-009 — Gerenciar Locais

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-047 | P0 | **Dado** um usuário com IV-PERM-008, **Quando** cadastra local com código único e hierarquia válida (tipo compatível com o pai, sem ciclos), **Então** o local é persistido e auditado | API + banco |
| AC-IV-048 | P0 | **Dado** um código duplicado na empresa, **Quando** submetido, **Então** o sistema recusa com IV-ERR-061; hierarquia inválida recusa com IV-ERR-062 | API |
| AC-IV-049 | P0 | **Dado** um local com saldo ou reservas ativas, **Quando** a inativação é tentada, **Então** o sistema recusa com IV-ERR-063 exibindo a posição atual | API |
| AC-IV-050 | P0 | **Dado** um local inativo, **Quando** referenciado em nova movimentação, **Então** o sistema recusa com IV-ERR-060; o histórico do local permanece consultável | API |

## 2.10 UC-IV-010 — Tratar Alertas

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-051 | P0 | **Dado** um efeito de saldo que cruza o mínimo do item, **Quando** avaliado (assíncrono), **Então** EVT-IV-015 é emitido, o alerta nasce Aberto e os destinatários configurados são notificados — sem bloquear a movimentação | evento + notificação |
| AC-IV-052 | P0 | **Dado** um alerta Aberto para a mesma chave de saldo, **Quando** novo cruzamento ocorre, **Então** nenhum alerta duplicado é criado (deduplicação IV-BR-091) | evento + banco |
| AC-IV-053 | P0 | **Dado** disponível zero com demanda aberta, **Quando** avaliado, **Então** EVT-IV-016 é emitido com prioridade alta (IV-NOT-001) | evento + notificação |
| AC-IV-054 | P0 | **Dado** uma entrada que restabelece o saldo, **Quando** confirmada, **Então** o alerta correspondente é Normalizado automaticamente | evento + banco |
| AC-IV-055 | P1 | **Dado** uma ruptura sem normalização em 48h, **Quando** o prazo estoura, **Então** ESC-IV-004 escala ao Gestor com posição e consumo recente | job + notificação |

## 2.11 UC-IV-011 — Consultar Posição e Extrato

| Código | Prioridade | Critério | Verificação |
|--------|-----------|----------|-------------|
| AC-IV-056 | P0 | **Dado** um usuário com IV-PERM-009, **Quando** consulta a posição, **Então** recebe físico/reservado/disponível por chave, apenas da sua empresa e escopo, com `disponível = físico − reservado` sempre consistente | API + banco |
| AC-IV-057 | P0 | **Dado** o extrato de um item, **Quando** consultado, **Então** cada linha exibe saldo anterior/posterior e a trilha navegável até o documento de origem (recebimento, solicitação, ajuste, inventário, estorno) | API |
| AC-IV-058 | P0 | **Dado** o perfil Requester, **Quando** tenta acessar posição, extrato ou visão do almoxarifado, **Então** o sistema recusa (404/403 conforme o passo) e a tentativa gera alerta de segurança (IV-BR-097) | API + auditoria |
| AC-IV-059 | P0 | **Dado** a indisponibilidade do cache, **Quando** a posição é consultada, **Então** o fallback ao banco responde sem erro ao usuário e a degradação é registrada | API |
| AC-IV-060 | P1 | **Dado** um perfil sem acesso a dados sensíveis, **Quando** consulta extrato/exportação, **Então** os campos de consumo por colaborador são omitidos (ABAC-IV-04 — LGPD) | API |

---

# 3. Critérios de Regras de Negócio Transversais

| Código | Prioridade | Critério | Origem |
|--------|-----------|----------|--------|
| AC-IV-061 | P0 | **Dado** qualquer caminho do sistema (API, job, evento consumido, banco), **Quando** um saldo muda, **Então** existe um documento de movimentação confirmado correspondente — **não existe nenhum caminho de escrita direta de saldo** (prova de integridade do DoD, verificada por testes negativos + verificação de privilégios) | MMS-P-08 / INV-IV-01 |
| AC-IV-062 | P0 | **Dado** qualquer documento, **Quando** confirmado, **Então** referencia origem verificável (recebimento, solicitação, devolução, ajuste, inventário, carga inicial ou documento externo) | MMS-P-07 |
| AC-IV-063 | P0 | **Dado** qualquer saldo (total, reservado, disponível), **Quando** verificado a qualquer momento, **Então** nunca é negativo e `reservado ≤ total` (invariante contínuo, incluindo constraints de banco) | MMS-RG-04 / INV-IV-04 |
| AC-IV-064 | P0 | **Dado** qualquer operação de escrita, **Quando** executada, **Então** respeita o fluxo de autorização escopo(404) → RBAC → ABAC → delegação(403), com deny by default (POL-IV-AUTH-011) | MMS-004-09 |
| AC-IV-065 | P0 | **Dado** confirmações concorrentes sobre a mesma chave de saldo, **Quando** processadas, **Então** são serializadas: apenas uma aplica o efeito por vez; a concorrente aguarda ou recebe IV-ERR-409 com orientação de retry, e o saldo final é consistente | MMS-004-11 §15.9 |
| AC-IV-066 | P0 | **Dado** qualquer consulta, **Quando** executada, **Então** aplica filtro obrigatório por `company_id` (multiempresa); saldo nunca é compartilhado entre empresas | MMS-004-11 |
| AC-IV-067 | P0 | **Dado** 100% das confirmações com efeito de saldo, **Quando** ocorrem, **Então** geram registro de auditoria imutável com correlationId, autor, documento e **saldo anterior/posterior por linha**; falha de auditoria aborta a operação | IV-BR-095 + FD-001-06 |

---

# 4. Critérios de Ciclo de Vida e Estados

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-IV-068 | P0 | **Dado** qualquer entidade do módulo (documento, reserva, ajuste, inventário), **Quando** transiciona, **Então** segue exclusivamente as matrizes da State Machine (MMS-004-03 §10); nenhuma transição fora delas é possível por nenhum caminho |
| AC-IV-069 | P0 | **Dado** um documento Confirmado, **Quando** qualquer edição é tentada (API ou banco), **Então** é recusada — a única saída de Confirmado é Estornado via estorno vinculado |
| AC-IV-070 | P1 | **Dado** transições concorrentes sobre a mesma entidade, **Quando** processadas, **Então** apenas uma é aceita (a outra recebe IV-ERR-409) e o estado final é consistente |

---

# 5. Critérios de Eventos e Notificações

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-IV-071 | P0 | **Dado** qualquer evento do módulo (EVT-IV-001..016), **Quando** publicado, **Então** segue o envelope oficial (eventId, eventType, version, aggregateId, occurredAt, correlationId, causationId, actorId, companyId, payload) e é persistido na outbox na mesma transação |
| AC-IV-072 | P0 | **Dado** eventos que afetam saldo (EVT-IV-001..008, 010), **Quando** publicados, **Então** carregam `balanceBefore`/`balanceAfter` por linha afetada |
| AC-IV-073 | P0 | **Dado** falha de consumo, **Quando** ocorre, **Então** aplica retry 5x exponencial e, esgotado, envia para `trino.materials.dlq`; consumidores são idempotentes por (eventId, consumerName); evento crítico em DLQ dispara ESC-IV-002 |
| AC-IV-074 | P0 | **Dado** eventos do mesmo agregado ou da mesma chave de saldo, **Quando** publicados, **Então** a ordenação é preservada por aggregateId/balanceKey |
| AC-IV-075 | P0 | **Dado** qualquer notificação do módulo (IV-NOT-001..013), **Quando** disparada, **Então** é despachada exclusivamente pelo Notification Center (FD-001-05); falha total de notificação nunca bloqueia a movimentação principal (IV-BR-092) |

---

# 6. Critérios de API

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-IV-076 | P0 | **Dado** qualquer resposta da API, **Quando** emitida, **Então** usa o envelope oficial (sucesso: data/page/correlationId; erro: error{code,message,details,correlationId}) e as mensagens correspondem ao catálogo IV-ERR |
| AC-IV-077 | P0 | **Dado** qualquer listagem, **Quando** paginada, **Então** usa keyset com cursor assinado de 15 min; parâmetros de offset são rejeitados; cursor inválido/expirado retorna IV-ERR-400 |
| AC-IV-078 | P0 | **Dado** requisição de escrita com `Idempotency-Key` repetida em 24h, **Quando** recebida, **Então** retorna o resultado original (idempotentReplay) sem reexecutar efeitos de saldo |
| AC-IV-079 | P0 | **Dado** payload com campos fora da allowlist (mass assignment), **Quando** recebido, **Então** os campos são rejeitados/ignorados; campos calculados (`status`, `number`, `balanceBefore/After`, `version`, timestamps) permanecem read-only |
| AC-IV-080 | P0 | **Dado** um identificador fora do escopo do usuário, **Quando** acessado diretamente, **Então** o sistema responde 404 (anti-enumeração), nunca 403, e a tentativa é auditável |
| AC-IV-081 | P1 | **Dado** cliente que excede o rate limit da sua classe (600/240/60/1200 req-min), **Quando** excede, **Então** recebe 429 com `Retry-After` e corpo IV-ERR-429 |

---

# 7. Critérios de UX e Wireframes (incorporados por referência)

| Origem | Incorporação |
|--------|--------------|
| CA-UX-IV-01 a CA-UX-IV-11 (MMS-004-14, seção 13) | Obrigatórios no MVP; verificação conforme TC-UX-IV-01 a TC-UX-IV-09 (MMS-004-17) |
| CA-WF-IV-01 a CA-WF-IV-08 (MMS-004-15, seção 12) | Obrigatórios na entrega das telas; verificação por revisão cruzada e testes de UI |

Critérios adicionais de integração UI×API:

| Código | Prioridade | Critério |
|--------|-----------|----------|
| AC-IV-082 | P0 | **Dado** qualquer erro de API, **Quando** exibido na interface, **Então** a mensagem apresentada corresponde ao catálogo documentado (MMS-004-07/13), com os números do conflito (disponível × solicitado) quando aplicável, e o código IV-ERR é preservado para suporte |
| AC-IV-083 | P0 | **Dado** qualquer ação indisponível pelo papel do usuário, **Quando** a tela renderiza, **Então** a ação é ocultada, nunca apenas desabilitada; o menu do Estoque é invisível ao Requester (CA-UX-IV-03) |

---

# 8. Critérios Não Funcionais

| Código | Prioridade | Critério | Origem |
|--------|-----------|----------|--------|
| AC-IV-084 | P0 | **Dado** a validação de estoque (MMS-003) e a consulta de posição, **Quando** medidas sob carga de referência, **Então** respondem em < 2s ponta a ponta (API p95 ≤ 500 ms; extrato ≤ 300 ms; confirmação ≤ 1s) | MMS-004 README / MMS-004-13 |
| AC-IV-085 | P0 | **Dado** qualquer operação, **Quando** trafegada, **Então** carrega correlationId ponta a ponta (requisição → efeito → evento → notificação → auditoria) | Observabilidade |
| AC-IV-086 | P0 | **Dado** a superfície do módulo, **Quando** submetida à revisão de segurança, **Então** atende SEC-001 e SEC-003 sem pendências críticas (incluindo SoD e anti-IDOR) | Segurança |
| AC-IV-087 | P0 | **Dado** a reconstrução da projeção de saldo a partir dos documentos confirmados (COMP-IV-004), **Quando** executada, **Então** o resultado é idêntico à projeção corrente (fonte de verdade são os documentos) | MMS-004-11 |
| AC-IV-088 | P1 | **Dado** o job de vencimento de reservas, **Quando** uma reserva expira, **Então** o processamento ocorre em até 15 minutos após o vencimento (SLA MMS-004-03) | job |
| AC-IV-089 | P1 | **Dado** as notificações do módulo, **Quando** renderizadas, **Então** apresentam conteúdo no idioma do usuário (pt-BR/en-US) | Internacionalização |

---

# 9. Matriz de Rastreabilidade (Resumo)

| Fonte | Cobertura neste documento |
|-------|---------------------------|
| UC-IV-001 a UC-IV-011 | AC-IV-001 a AC-IV-060 |
| Business Rules (MMS-004-02) | Cobertas via UCs + AC-IV-061 a AC-IV-067 (cada regra obrigatória tem ao menos um AC P0) |
| State Machine (MMS-004-03) | AC-IV-024, AC-IV-068 a AC-IV-070 |
| Event Storming (MMS-004-05) | AC-IV-071 a AC-IV-074 |
| Permissions (MMS-004-09) | AC-IV-031, AC-IV-046, AC-IV-058, AC-IV-064, AC-IV-080, AC-IV-083 |
| Notifications (MMS-004-10) | AC-IV-006, AC-IV-023, AC-IV-034, AC-IV-051..055, AC-IV-075, AC-IV-089 |
| Database (MMS-004-11) | AC-IV-061, AC-IV-063, AC-IV-065, AC-IV-066, AC-IV-087 |
| API (MMS-004-13) | AC-IV-076 a AC-IV-081 |
| UX/Wireframes (MMS-004-14/15) | CA-UX-IV-*, CA-WF-IV-* + AC-IV-082, AC-IV-083 |
| NFR (MMS-004 README) | AC-IV-063, AC-IV-065, AC-IV-084 a AC-IV-088 |

A matriz completa **regra × UC × API × AC × teste** é preenchida no MMS-004-17 (Test Scenarios), que referencia cada AC-IV em seus cenários — requisito do DoD.

---

# 10. Regras de Aceitação

1. Todo critério **P0** deve ter verificação automatizada ou evidência formal registrada antes do release.
2. Critérios **P1** com exceção tolerada exigem registro da exceção, dono e prazo de regularização.
3. Critérios **P2** não bloqueiam o MVP, mas bloqueiam a v1.1.
4. Nenhum critério pode ser alterado após o início da implementação sem atualização deste documento e nova aprovação (conflito → documentação vence).
5. Falha em critério de segurança (AC-IV-086) ou na prova de integridade (AC-IV-061) bloqueia o release independentemente das demais verificações.

---

# 11. Alinhamento com o DoD do Módulo

Este documento atende aos itens dos **Critérios de Conclusão do Módulo (DoD)** do MMS-004 README:

| Item DoD | Atendimento |
|----------|-------------|
| 1. Visão aprovada no registry | Concluído (MMS-004 v1.0.0) |
| 2. Documentos funcionais aprovados | MMS-004-16 é o penúltimo; falta MMS-004-17 |
| 3. Matriz regra × movimentação × evento | Estruturada na seção 9; completada no MMS-004-17 |
| 4. Prova de integridade (nenhum caminho altera saldo sem documento) | AC-IV-061 + AC-IV-087 (testes negativos + verificação de privilégios + reconstrução da projeção) |

---

# 12. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | AC-IV-001 a AC-IV-089 + CA-UX-IV/CA-WF-IV por referência |
| **v1.1** | Critérios de transferência em trânsito, inventário cíclico automático, compra dedicada com reserva automática e relatórios; promoção dos P2 |
| **v2.0** | Critérios de lote/validade/série, quarentena, rebalanceamento e reposição automática por ponto de pedido (MMS-004 README, roadmap) |

---

# 13. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-23 | Criação dos Acceptance Criteria do módulo: 89 critérios formais em Given/When/Then cobrindo UC-IV-001..011, regras transversais (incluindo a prova de integridade AC-IV-061), ciclo de vida das 4 entidades, eventos com saldos por linha, notificações, API, UX e NFRs, com prioridades P0/P1/P2, matriz de rastreabilidade e alinhamento ao DoD |
