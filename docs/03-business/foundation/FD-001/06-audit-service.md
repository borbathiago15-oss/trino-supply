# FD-001-06 — Audit Service

| Campo | Valor |
|---|---|
| **Documento** | FD-001-06 |
| **Módulo** | Foundation — Audit Service (FD-BC-006) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-07-30 |
| **Dependências** | FD-001 (Foundation Overview), FD-001-01 (IAM), FD-001-02 (Organization), ADR-009 (Banco como Projeção do Domínio), ADR-010 (Eventos de Negócio), ADR-011 (Padrão de Documentação Foundation), SEC-001/SEC-002/SEC-003 (Segurança), LGPD |
| **Consumidores da trilha** | FD-001-01 IAM, FD-001-02 Organization, FD-001-03 Document Management, FD-001-04 Workflow Engine, FD-001-05 Notification Center e todos os módulos de negócio |

---

## 1. Objetivo

O Audit Service é o domínio do Foundation responsável pela **trilha de auditoria corporativa** do Trino Supply: um registro **append-only, imutável e à prova de adulteração** de todas as ações relevantes executadas no sistema — por usuários, pelo próprio sistema ou por integrações.

Ele responde, a qualquer momento e com evidência verificável:

1. **Quem** fez (ator: usuário, serviço, integração);
2. **O quê** foi feito (ação + antes/depois quando aplicável);
3. **Onde** (entidade afetada: tipo + identificador + organização);
4. **Quando** (timestamp UTC com precisão de milissegundos);
5. **Por que / em qual contexto** (`correlationId`, `causationId`, evento de origem, IP, user-agent).

### 1.1 Princípio central

**Auditoria obrigatória** é princípio do projeto: nenhum módulo "grava seu próprio log". Toda ação auditável é publicada como evento de auditoria e registrada **exclusivamente** pelo Audit Service. Módulos não leem nem alteram a trilha.

### 1.2 O que o Audit Service NÃO é

- **Não é log de aplicação.** Logs técnicos (stack traces, debug) vivem na observabilidade (retentos por 12 meses); a trilha de auditoria é um registro de **negócio** com retenção legal (5 anos) e garantias de integridade.
- **Não é timeline funcional.** A Timeline (FD-001-07) monta visões cronológicas para o usuário final; o Audit Service é a fonte de evidência que a alimenta.
- **Não contém regras de negócio.** Ele registra; quem decide o que é auditável é cada módulo em seu documento.

---

## 2. Conceitos do Domínio

| Conceito | Descrição |
|---|---|
| **AuditEntry** | Registro único e imutável de uma ação auditável. Unidade básica da trilha. |
| **Actor** | Quem executou: `USER` (usuário autenticado), `SERVICE` (processo do sistema, ex.: consumidor de fila), `INTEGRATION` (sistema externo via API) ou `ANONYMOUS_SYSTEM` (rotinas agendadas). |
| **Action** | Verbo auditado (ex.: `purchase-requisition.approve`, `workflow.escalate`, `iam.role.assign`). Vocabulário controlado por catálogo. |
| **EntityRef** | Referência à entidade afetada: `entityType`, `entityId`, `organizationId`, mais `displayLabel` desnormalizado para leitura. |
| **ChangeSet** | Antes/depois de campos alterados (quando aplicável), com valores serializados e campos sensíveis **mascarados**. |
| **HashChain** | Cadeia de integridade: cada AuditEntry carrega `prevHash` + `entryHash` (SHA-256), tornando adulteração retroativa detectável. |
| **RetentionPolicy** | Política de retenção por escopo (padrão: 5 anos — NC-BR-011 / política do Foundation). |
| **LegalHold** | Bloqueio jurídico: impede qualquer purga/pseudonimização de um recorte da trilha até liberação formal. |
| **Pseudonymization** | Substituição de dados pessoais por pseudônimo estável após o prazo legal, conforme LGPD, preservando a integridade estatística da trilha. |
| **AuditExport** | Exportação assíncrona e auditada de um recorte da trilha (arquivo assinado, com hash de verificação). |

---

## 3. O que é auditável

Origem obrigatória (catálogo mínimo — cada módulo registra as suas):

| Categoria | Exemplos |
|---|---|
| **Autenticação e acesso** | login (sucesso/falha), logout, refresh token reutilizado (detecção de roubo — SEC-003), troca de senha, bloqueio de conta, MFA |
| **Autorização** | concessão/revogação de papel ou permissão, delegação criada/revogada, acesso negado a ação sensível (deny auditado) |
| **Dados mestres e organização** | criação/alteração/inativação de empresa, unidade, centro de custo, usuário, limites da organização |
| **Workflow** | instanciação, decisões de aprovação (imutáveis — WF), escalonamentos, delegações, compensações, cancelamentos |
| **Notificações** | ciclo completo de entrega (NC-EVT-001..009), supressões, reprocessamentos |
| **Documentos** | upload, download, versionamento, exclusão lógica, acesso a link assinado |
| **Administração** | alteração de configuração, reprocessamento de DLQ, gerenciamento de templates, consultas e exportações da própria auditoria |
| **Negócio** | ações de domínio definidas por cada módulo (ex.: PR-001: criar, submeter, aprovar, rejeitar, devolver, cancelar requisição) |

**Acesso à própria trilha é auditado** (AUD-BR-009): consultas administrativas e exportações geram AuditEntry.

---

## 4. Modelo de Dados do AuditEntry

| Campo | Descrição |
|---|---|
| `auditId` | UUIDv7 (ordenável temporalmente). |
| `occurredAt` | Timestamp UTC (ms) da ação. |
| `recordedAt` | Timestamp UTC (ms) do registro na trilha. |
| `actor` | `{ type: USER\|SERVICE\|INTEGRATION\|ANONYMOUS_SYSTEM, actorId, actorLabel }`. |
| `action` | Verbo do catálogo controlado. |
| `entity` | `{ entityType, entityId, organizationId, displayLabel }`. |
| `changeSet` | (Opcional) `{ field, oldValue, newValue }[]` com mascaramento de sensíveis. |
| `context` | `{ correlationId, causationId, sourceEventId, sourceEventType, ip, userAgent, channel }`. |
| `result` | `SUCCESS` \| `DENIED` \| `FAILURE` (+ `reason`). |
| `prevHash` / `entryHash` | Cadeia SHA-256 de integridade por partição organizacional. |
| `retentionClass` | Classe de retenção aplicada (padrão `LEGAL_5Y`). |

- **FKs lógicas** (ADR-009): nenhuma chave estrangeira física para entidades de negócio.
- **Multiempresa por construção:** `organizationId` é parte da chave de particionamento e de todo filtro de consulta.

---

## 5. Armazenamento e Integridade

### 5.1 Particionamento

- PostgreSQL (ADR-009): tabela `audit.audit_entry` **particionada por mês** (`occurredAt`) com subpartição lógica por `organizationId` no índice.
- Partições futuras criadas por rotina; partições expiradas seguem o ciclo de retenção (seção 6).
- Escrita otimizada: **append-only**, sem `UPDATE`/`DELETE` — constraints + revogação de grants tornam impossível alterar ou remover registros pela aplicação.

### 5.2 Cadeia de hash (tamper evidence)

1. Cada partição organizacional mantém sequência ordenada; cada entrada carrega `prevHash` (hash da entrada anterior) e `entryHash = SHA-256(prevHash ‖ payload canônico)`.
2. Verificação de integridade sob demanda (`AUD-CHECK`): percorre a cadeia e confirma continuidade; quebra gera alerta de segurança (SEC-002 — *Tampering*).
3. Âncoras externas (v2): resumo diário da cadeia publicado em storage imutável (MinIO com object-lock) para evidência fora do banco.

### 5.3 Escrita

- Origem: **consumo de eventos** (ADR-010) — exchange `trino.foundation.audit` (+ eventos de negócio mapeados), com outbox no produtor, at-least-once e **idempotência por `sourceEventId`** (AUD-BR-002).
- Comandos administrativos (LegalHold, retenção, exportação) trafegam por API própria, também auditados.

---

## 6. Retenção, LGPD e Legal Hold

| Política | Regra |
|---|---|
| **Retenção padrão** | 5 anos (`LEGAL_5Y`), alinhado à política do Foundation (NC-BR-011) e à legislação societária/fiscal. |
| **Logs técnicos** | 12 meses (observabilidade) — fora do escopo deste módulo. |
| **Pseudonimização (LGPD)** | Após o prazo legal, dados pessoais em `actor.actorLabel` e `changeSet` são substituídos por pseudônimo estável; `entryHash` é recalculado sobre payload **canônico original preservado em cofre selado** ou mantido com verificação por HMAC com chave rotacionada — detalhamento em ADR própria antes da v2 (ver Decisões Abertas). |
| **Eliminação** | Somente por processo jurídico ou política de retenção expirada **sem** LegalHold; executada por rotina administrativa, ela própria auditada em partição separada. |
| **LegalHold** | Suspende purga e pseudonimização do recorte (por entidade, período ou processo); aplicação e liberação exigem permissão dedicada e são auditadas. |
| **Exportação** | Gera arquivo assinado com manifesto (quantidade, período, hash de verificação); exportações são auditadas e têm link com expiração ≤ 5 minutos (MinIO, SEC-001). |

---

## 7. Regras de Negócio

| Código | Regra |
|---|---|
| **AUD-BR-001** | A trilha é append-only: nenhuma operação de atualização ou exclusão é possível pela aplicação ou por APIs públicas. |
| **AUD-BR-002** | O registro é idempotente por `sourceEventId`: evento reprocessado nunca duplica AuditEntry. |
| **AUD-BR-003** | Toda AuditEntry carrega `correlationId`; entradas do mesmo fluxo são rastreáveis ponta a ponta. |
| **AUD-BR-004** | Campos sensíveis (senha, token, dados pessoais além do necessário) são proibidos; o pipeline mascara/descarta antes de persistir. |
| **AUD-BR-005** | A cadeia de hash é contínua por partição organizacional; quebra de continuidade gera incidente de segurança. |
| **AUD-BR-006** | Isolamento multiempresa: nenhuma consulta, exportação ou métrica cruza fronteira organizacional. |
| **AUD-BR-007** | Retenção padrão de 5 anos; purga somente por rotina administrativa auditada e nunca sob LegalHold. |
| **AUD-BR-008** | Pseudonimização LGPD não destrói a verificabilidade da cadeia (mecanismo formalizado por ADR antes da v2). |
| **AUD-BR-009** | Ler a trilha também é auditado: consultas administrativas, verificações de integridade e exportações geram AuditEntry próprias. |
| **AUD-BR-010** | Negar acesso (result `DENIED`) a ações sensíveis é auditável tanto quanto permitir. |
| **AUD-BR-011** | O catálogo de `action` é controlado: novos verbos entram por configuração versionada, nunca por string livre. |
| **AUD-BR-012** | Timestamps são UTC do servidor do Audit Service (`recordedAt`); o `occurredAt` do produtor é preservado, e divergência > 5 minutos gera flag de anomalia. |
| **AUD-BR-013** | Módulos de negócio não leem a trilha: leituras funcionais (timeline, histórico) consomem projeções próprias ou o FD-001-07. |
| **AUD-BR-014** | Falha na gravação de auditoria em ação crítica de segurança (autenticação, autorização, administração) bloqueia a operação (*fail-closed*); demais ações seguem *fail-open* com alerta e reenvio garantido por outbox. |

---

## 8. Eventos

### 8.1 Publicados pelo Audit Service

| Código | Evento | Payload (resumo) | Consumidores |
|---|---|---|---|
| **AUD-EVT-001** | AuditEntryRecorded | auditId, action, entityRef, result, correlationId | FD-001-07 Timeline, analytics |
| **AUD-EVT-002** | AuditIntegrityCheckFailed | partition, firstBrokenAuditId, detectedAt | Segurança/SRE (incidente) |
| **AUD-EVT-003** | AuditLegalHoldApplied | holdId, scope, appliedBy | Governança |
| **AUD-EVT-004** | AuditLegalHoldReleased | holdId, releasedBy, reason | Governança |
| **AUD-EVT-005** | AuditRetentionExecuted | partition, purgedCount, executedBy (rotina) | Governança |
| **AUD-EVT-006** | AuditPseudonymizationExecuted | partition, affectedCount, executedBy | Governança/LGPD |
| **AUD-EVT-007** | AuditExportCompleted | exportId, requestedBy, scope, manifestHash, expiresAt | Solicitante (via NC) |

Envelope e garantias conforme PR-001-05 §14.1 / ADR-010: outbox, at-least-once, versionamento `v1`, `correlationId`.

### 8.2 Consumidos

| Origem | Uso |
|---|---|
| Exchange `trino.foundation.audit` | Verbo dedicado de auditoria de qualquer domínio do Foundation. |
| Eventos de negócio mapeados (ex.: WF-EVT-*, NC-EVT-*, eventos PR-*) | Projeção automática em AuditEntry conforme mapa de configuração (configuração acima de customização). |

---

## 9. APIs

| Método | Endpoint | Descrição | Permissão |
|---|---|---|---|
| `GET` | `/api/v1/admin/audit?entityType=&entityId=&actorId=&action=&from=&to=&cursor=` | Consulta a trilha (keyset pagination) | `audit.read` |
| `GET` | `/api/v1/admin/audit/{auditId}` | Detalhe de uma entrada | `audit.read` |
| `POST` | `/api/v1/admin/audit/integrity-check` | Dispara verificação de cadeia (recorte) | `audit.integrity` |
| `POST` | `/api/v1/admin/audit/exports` | Solicita exportação assíncrona | `audit.export` |
| `GET` | `/api/v1/admin/audit/exports/{exportId}` | Status + link assinado (≤ 5 min) | `audit.export` |
| `POST` | `/api/v1/admin/audit/legal-holds` | Aplica LegalHold | `audit.legalhold` |
| `DELETE` | `/api/v1/admin/audit/legal-holds/{holdId}` | Libera LegalHold (com `reason`) | `audit.legalhold` |
| `GET` | `/api/v1/admin/audit/catalog` | Catálogo de actions | `audit.read` |

- Todas as chamadas geram AuditEntry (AUD-BR-009); envelope e erros padrão do Foundation; escopo de organização obrigatório.

---

## 10. Integrações com o Foundation

| Domínio | Integração |
|---|---|
| **FD-001-01 IAM** | Atores, permissões dedicadas (`audit.*`), eventos de autenticação/autorização como fonte primária. |
| **FD-001-02 Organization** | Particionamento multiempresa, escopo de consulta, retenção por organização (se diferenciada). |
| **FD-001-03 Document Management** | Armazenamento de exportações e âncoras de integridade (MinIO, object-lock). |
| **FD-001-04 Workflow Engine** | Fonte de eventos de decisão, escalonamento e compensação. |
| **FD-001-05 Notification Center** | Fonte do ciclo de entrega; destino de alertas (AUD-EVT-002, AUD-EVT-007). |
| **FD-001-07 Timeline Service** *(planejado)* | Consome AUD-EVT-001 para visões cronológicas funcionais. |

---

## 11. Requisitos Não Funcionais

| Categoria | Requisito |
|---|---|
| **Integridade** | Cadeia SHA-256 por partição + âncora externa (v2); verificação sob demanda com SLA de varredura definido por volume. |
| **Desempenho de escrita** | Ingestão assíncrona via fila; pico absorvido sem perda (outbox + retry); idempotência garantida. |
| **Desempenho de leitura** | Consultas administrativas com keyset pagination; filtros cobertos por índices `(organization_id, occurred_at DESC)` e `(entity_type, entity_id, occurred_at DESC)`. |
| **Disponibilidade** | Fail-closed para ações críticas de segurança (AUD-BR-014); demais fluxos fail-open com reenvio garantido. |
| **Segurança** | SEC-001/003: grants sem UPDATE/DELETE, mTLS interno, segredos em cofre, exportação com link expirável, mascaramento de sensíveis (AUD-BR-004). |
| **Conformidade** | LGPD (pseudonimização, minimização, base legal), retenção societária/fiscal 5 anos, evidência verificável para auditorias externas. |
| **Observabilidade** | Métricas de ingestão (lag de fila, taxa de gravação), de verificação de cadeia e de crescimento de partições; alerta em lag e em quebra de integridade. |

---

## 12. Restrições Arquiteturais

1. **Append-only absoluto:** banco revoga UPDATE/DELETE da role da aplicação; exceções operacionais (purga/pseudonimização) rodam com role dedicada, auditadas em partição separada.
2. **Leitura restrita a administradores:** nenhum endpoint de leitura é exposto a fluxos funcionais de usuário comum (AUD-BR-013).
3. **Configuração acima de customização:** novos verbos e novos mapeamentos evento→auditoria entram por catálogo versionado, sem código novo.
4. **Sem lógica de negócio:** o Audit Service não interpreta decisões; registra evidências.
5. **Multiempresa por construção:** `organizationId` em toda chave, índice e filtro.

---

## 13. Critérios de Conclusão do Módulo (MVP)

- [ ] Tabela particionada por mês com grants append-only e cadeia de hash operacional.
- [ ] Consumidor do exchange `trino.foundation.audit` + mapeamento configurável dos eventos WF-EVT e NC-EVT.
- [ ] Catálogo de actions versionado carregado.
- [ ] APIs de consulta (keyset), detalhe, integrity-check e exportação funcionando com permissões dedicadas.
- [ ] Auditoria do próprio acesso (AUD-BR-009) ativa.
- [ ] Rotina de retenção e mecanismo de LegalHold implementados.
- [ ] Testes: imutabilidade (tentativa de UPDATE/DELETE bloqueada), idempotência por `sourceEventId`, continuidade de cadeia, isolamento multiempresa, fail-closed em ação crítica, exportação com manifesto.

---

## 14. Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Descrição |
|---|---|---|
| FD-001-01 IAM | Obrigatória | Atores, permissões `audit.*`, fonte de eventos de acesso. |
| FD-001-02 Organization | Obrigatória | Particionamento e escopo multiempresa. |
| FD-001-03 Document Management | Obrigatória | Exportações e âncoras de integridade (MinIO). |
| FD-001-04 Workflow Engine | Consumo de eventos | Decisões, escalonamentos, compensações. |
| FD-001-05 Notification Center | Bidirecional | Fonte (ciclo de entrega) e destino (alertas, exportações). |
| FD-001-07 Timeline Service | Produção de eventos | AUD-EVT-001 alimenta a timeline funcional. |
| Módulos de negócio (PR-001, …) | Produção de eventos | Ações de domínio auditáveis. |

---

## 15. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Trilha append-only particionada, cadeia de hash, ingestão por eventos, catálogo de actions, APIs administrativas, retenção 5 anos, LegalHold, exportação assinada, auditoria do próprio acesso. |
| **v2.0** | Âncoras externas diárias em MinIO object-lock; pseudonimização LGPD operacional (pós-ADR); verificação de integridade agendada; dashboards de conformidade. |
| **v3.0** | Trilha certificada para auditoria externa (pacote de evidências), retenção diferenciada por organização/classe, arquivamento frio com restore sob demanda. |

---

## 16. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Versão inicial aprovada: objetivo, conceitos, escopo auditável, modelo do AuditEntry, armazenamento particionado com cadeia de hash, retenção/LGPD/LegalHold, regras AUD-BR-001..014, eventos AUD-EVT-001..007, APIs administrativas, integrações, NFRs, restrições, critérios de conclusão, matriz de dependência e roadmap. | Arquitetura Trino |
