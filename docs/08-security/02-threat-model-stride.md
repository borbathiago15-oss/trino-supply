**Documento:** SEC-002 — Threat Model (STRIDE)
**Versão:** 1.0.0
**Status:** Approved
**Escopo:** Plataforma Trino Supply — Foundation + módulo PR-001 (modelo de referência para os demais módulos)

> Metodologia: STRIDE (Microsoft) por elemento de fluxo, com avaliação de risco por Probabilidade × Impacto e priorização P1–P4.
> Documentos relacionados: SEC-001 (Arquitetura de Segurança), SEC-003 (Padrão de Desenvolvimento Seguro), PR-001-09 (Permissions).

# SEC-002 — Threat Model (STRIDE)

> Este documento identifica ativos, atores, vetores de ataque, ameaças STRIDE, mitigações e prioridades de tratamento. Deve ser revisado a cada mudança estrutural (novo módulo, nova integração, novo dado sensível).

---

# 1. Escopo e Premissas

| Item | Definição |
| ---- | --------- |
| Sistema | Trino Supply — SaaS multi-tenant de gestão de suprimentos |
| Fronteiras | Internet→WAF; Frontend→Gateway; Gateway→Serviços; Serviços→Dados (SEC-001 §21) |
| Fora de escopo | Comprometimento físico do datacenter/cloud; ataques ao endpoint do usuário (responsabilidade compartilhada, tratada por boas práticas de sessão) |
| Premissas | TLS íntegro; cofre de segredos íntegro; código segue SEC-003; dados do tenant são responsabilidade do controlador (LGPD) |

---

# 2. Ativos

| Código | Ativo | Classificação | Por que importa |
| ------ | ----- | ------------- | --------------- |
| AST-001 | Dados de requisições e itens | Confidencial | Núcleo do negócio do tenant; estratégico/competitivo |
| AST-002 | Decisões de aprovação e trilha de auditoria | Crítico | Evidência legal; anti-repúdio; compliance |
| AST-003 | Credenciais e tokens (senhas, JWT, refresh) | Crítico | Comprometimento = impersonação total |
| AST-004 | Anexos (MinIO) | Confidencial | Documentos de negócio, possíveis dados pessoais |
| AST-005 | Segredos de infraestrutura (chaves, conexões) | Crítico | Comprometimento = movimento lateral total |
| AST-006 | Eventos de negócio (RabbitMQ/outbox) | Interno | Integridade do fluxo; consumidores confiam no envelope |
| AST-007 | Configurações de workflow/permissões/delegações | Confidencial | Alteração indevida = bypass de controle de negócio |
| AST-008 | Logs e telemetria | Interno | Detecção e forense; contêm metadados sensíveis |
| AST-009 | Numeração e integridade referencial | Interno | Fraude documental se manipulável |
| AST-010 | Disponibilidade da plataforma | Operacional | SLA contratual com tenants |

---

# 3. Atores

| Ator | Descrição | Motivação |
| ---- | --------- | --------- |
| **Atacante externo anônimo** | Sem credenciais, via internet | Credenciais, dados, ransomware, defacement |
| **Usuário malicioso autenticado** | Conta válida de um tenant (insider ou conta roubada) | Fraude em aprovações, exfiltração, sabotagem |
| **Atacante cross-tenant** | Usuário de um tenant tentando acessar outro | Espionagem competitiva |
| **Administrador malicioso/comprometido** | Conta privilegiada | Fraude, apagão de evidências |
| **Bot/automação abusiva** | Credential stuffing, scraping, enumeração | Acesso em massa, DoS |
| **Integração comprometida (roadmap)** | Sistema externo consumidor | Injeção de eventos falsos, exfiltração via webhook |
| **Erro interno (não malicioso)** | Operador ou software com defeito | Perda/corrupção acidental — tratada por integridade e backup |

---

# 4. Vetores de Ataque

| Código | Vetor | Entrada |
| ------ | ----- | ------- |
| VEC-001 | Endpoints REST públicos | Internet → WAF → Gateway |
| VEC-002 | Endpoint de autenticação (login/refresh) | Internet |
| VEC-003 | Upload de arquivos | API autenticada |
| VEC-004 | Parâmetros de consulta/IDs (IDOR, injection) | API autenticada |
| VEC-005 | Tokens (roubo, replay, reuso de refresh) | Cliente/rede |
| VEC-006 | Mensageria interna (RabbitMQ) | Rede interna/serviços |
| VEC-007 | Console de administração e configuração | Usuários privilegiados |
| VEC-008 | Pipeline CI/CD e repositório | Supply chain |
| VEC-009 | Backups e exports | Armazenamento/operador |
| VEC-010 | Logs e telemetria | Injeção via dados controlados pelo usuário |

---

# 5. Ameaças STRIDE

## 5.1 Spoofing (falsificação de identidade)

| ID | Ameaça | Ativo | Vetor | Mitigação | Risco residual |
| -- | ------ | ----- | ----- | --------- | -------------- |
| STR-S-01 | Credential stuffing / brute force no login | AST-003 | VEC-002 | Bloqueio progressivo, CAPTCHA, senhas vazadas bloqueadas, resposta uniforme (SEC-001 §5), IPS | Baixo |
| STR-S-02 | Roubo e replay de JWT | AST-003 | VEC-005 | Token curto (15 min), blacklist `jti` (Redis), TLS, `auth_time` em ações sensíveis | Baixo |
| STR-S-03 | Reuso de refresh token roubado | AST-003 | VEC-005 | Rotação a cada uso + detecção de reuso revoga a cadeia | Baixo |
| STR-S-04 | Forja de token com chave vazada | AST-003/005 | VEC-008 | Chaves em KMS, rotação 90 dias, assinatura assimétrica, `kid` | Muito baixo |
| STR-S-05 | Impersonação serviço-a-serviço | AST-006 | VEC-006 | mTLS + service accounts RabbitMQ por vhost (SEC-001 §19) | Baixo |

## 5.2 Tampering (adulteração de dados)

| ID | Ameaça | Ativo | Vetor | Mitigação | Risco residual |
| -- | ------ | ----- | ----- | --------- | -------------- |
| STR-T-01 | Alteração direta em banco fora do domínio | AST-001/002 | VEC-008/009 | Contas mínimas sem superuser, triggers de imutabilidade (decisões, version), auditoria de DDL, `log_statement='ddl'` | Baixo |
| STR-T-02 | Manipulação de evento em trânsito | AST-006 | VEC-006 | TLS no broker, envelope com `eventId` único, idempotência por deduplicação | Baixo |
| STR-T-03 | Alteração de configuração de workflow para bypass | AST-007 | VEC-007 | Alteração auditada, aprovação de mudança por papel distinto (SOD-005), SoD configuração×operação | Médio→Baixo |
| STR-T-04 | Adulteração de anexo após upload | AST-004 | VEC-003 | Versioning no bucket, metadados com hash/tamanho, sem URL pública | Baixo |
| STR-T-05 | Mass assignment alterando campos proibidos | AST-001/007 | VEC-004 | DTOs explícitos, fail on unknown properties, campos de servidor nunca bindados (SEC-003 §13) | Baixo |
| STR-T-06 | Replay de comando (duplo submit/aprovação) | AST-001/002 | VEC-001 | `Idempotency-Key`, optimistic concurrency (`version`), transições de estado idempotentes | Baixo |

## 5.3 Repudiation (repúdio)

| ID | Ameaça | Ativo | Vetor | Mitigação | Risco residual |
| -- | ------ | ----- | ----- | --------- | -------------- |
| STR-R-01 | Aprovador nega ter aprovado | AST-002 | — | Approval imutável (trigger), auditoria append-only, `correlationId` ponta-a-ponta, actor resolvido server-side | Muito baixo |
| STR-R-02 | Usuário nega alteração de dados | AST-002 | — | Auditoria de toda mutação na mesma transação; timeline imutável | Muito baixo |
| STR-R-03 | Delegante nega decisão do delegado | AST-002 | — | `delegatedBy` + vigência registrados na decisão e na delegação | Muito baixo |
| STR-R-04 | Operador nega acesso à auditoria/exportação | AST-008 | VEC-007 | Acesso à auditoria e exportações auditados (quem/quando/o quê) | Baixo |

## 5.4 Information Disclosure (exposição de informação)

| ID | Ameaça | Ativo | Vetor | Mitigação | Risco residual |
| -- | ------ | ----- | ----- | --------- | -------------- |
| STR-I-01 | IDOR — acesso a requisição de outro escopo/tenant | AST-001 | VEC-004 | Filtro de escopo em toda query + 404 (anti-IDOR), testes TC-UC-008-5, RLS avaliado v2 | Baixo |
| STR-I-02 | Vazamento cross-tenant em listagem/relatório | AST-001 | VEC-004 | `company_id` primeiro em todo índice/filtro (PR-001-11), isolamento absoluto (INH-005) | Baixo |
| STR-I-03 | Exposição de storage (bucket público/URL vazada) | AST-004 | VEC-003 | Buckets privados, URL assinada ≤ 5 min, sem inline de tipos ativos, auditoria de download | Muito baixo |
| STR-I-04 | Dados sensíveis em logs | AST-008 | VEC-010 | Proibição de segredos/tokens em logs, mascaramento, revisão de campos de log | Baixo |
| STR-I-05 | Erro detalhado vazando internals (stack/SQL) | AST-008 | VEC-001 | Exception handling padronizado com `PR-ERR-xxx`, sem internals (SEC-003 §9) | Muito baixo |
| STR-I-06 | Enumeração de usuários (login/registro) | AST-003 | VEC-002 | Resposta uniforme, timing constante | Baixo |
| STR-I-07 | Comentário interno visível ao solicitante | AST-001 | VEC-004 | Filtro server-side por papel (ABAC-04), DTO de saída dedicado | Muito baixo |
| STR-I-08 | Exfiltração via exportação em massa | AST-001 | VEC-001 | Exportação auditada, rate limit, escopo obrigatório, alerta por volume anômalo | Médio→Baixo |

## 5.5 Denial of Service

| ID | Ameaça | Ativo | Vetor | Mitigação | Risco residual |
| -- | ------ | ----- | ----- | --------- | -------------- |
| STR-D-01 | Flood de requisições na API | AST-010 | VEC-001 | Rate limit por IP/usuário/tenant, WAF, autoscaling | Baixo |
| STR-D-02 | Query custosa / paginação abusiva (OFFSET profundo) | AST-010 | VEC-004 | Keyset pagination obrigatória, `page_size` ≤ 100, `statement_timeout=30s`, índices company-first | Muito baixo |
| STR-D-03 | Upload em massa exaurindo storage | AST-004/010 | VEC-003 | Limites por arquivo/requisição, quota por tenant (config), AV em quarentena | Baixo |
| STR-D-04 | Flood de eventos / poison message travando consumidor | AST-006/010 | VEC-006 | Retry limitado + DLQ + alerta (PR-001-05 §14), consumers idempotentes | Baixo |
| STR-D-05 | Credential stuffing degradando autenticação | AST-010 | VEC-002 | Bloqueio progressivo, CAPTCHA, IPS, cache de decisão curto | Baixo |

## 5.6 Elevation of Privilege

| ID | Ameaça | Ativo | Vetor | Mitigação | Risco residual |
| -- | ------ | ----- | ----- | --------- | -------------- |
| STR-E-01 | Bypass de SoD (solicitante aprova a própria) | AST-002/007 | VEC-001 | ABAC-01 server-side, EXC-006, auditoria obrigatória; exceção só por política auditada | Muito baixo |
| STR-E-02 | Aprovação acima da alçada | AST-002 | VEC-001 | ABAC-02 (`approvalLimit`), escalonamento automático | Muito baixo |
| STR-E-03 | Escalação de escopo (unidade → empresa) | AST-001/007 | VEC-004 | Avaliação de escopo a cada requisição; INH-001..004 (específico prevalece; interseção) | Baixo |
| STR-E-04 | Abuso de delegação (vigência, redelegação) | AST-002/007 | VEC-007 | DEL-001..007 (sem redelegação, vigência obrigatória, revogação imediata) | Baixo |
| STR-E-05 | Mass assignment promovendo papel/status | AST-007 | VEC-004 | Ver STR-T-05; status só muda via transições da State Machine | Muito baixo |
| STR-E-06 | Escalonamento via serviço interno confiável demais | AST-005/006 | VEC-006 | Zero trust interno: mTLS, contas mínimas, sem "chamada privilegiada" implícita | Baixo |
| STR-E-07 | Container escape / supply chain malicioso | AST-005 | VEC-008 | Imagens assinadas + scan, non-root, read-only FS, seccomp; SCA bloqueante | Médio→Baixo |

---

# 6. Avaliação de Risco e Prioridade

Escala: **Probabilidade** (1–4) × **Impacto** (1–4) = **Risco** (1–16). Prioridade: P1 ≥ 12; P2 8–11; P3 4–7; P4 < 4. Valores consideram as mitigações já implementadas (risco residual).

| Ameaça | Prob. | Impacto | Risco | Prioridade | Ação |
| ------ | ----- | ------- | ----- | ---------- | ---- |
| STR-I-01 IDOR cross-escopo | 2 | 4 | 8 | **P2** | Testes de autorização em toda release; RLS v2 |
| STR-I-02 Vazamento cross-tenant | 1 | 4 | 4 | P3 | Manter isolamento; teste de regressão multi-tenant |
| STR-E-01 SoD bypass | 1 | 4 | 4 | P3 | Auditoria contínua de exceções de política |
| STR-T-03 Config de workflow adulterada | 2 | 3 | 6 | P3 | Aprovação dupla de mudança crítica (roadmap) |
| STR-I-08 Exfiltração via exportação | 2 | 3 | 6 | P3 | Alerta por volume; quota de exportação |
| STR-E-07 Supply chain | 2 | 4 | 8 | **P2** | Assinatura de imagens, SCA, revisão de dependências |
| STR-S-01 Credential stuffing | 3 | 2 | 6 | P3 | MFA v2 para papéis privilegiados |
| STR-D-01 Flood na API | 3 | 2 | 6 | P3 | Tuning contínuo de rate limit |
| STR-T-01 Alteração direta em banco | 1 | 4 | 4 | P3 | Revisão trimestral de contas e grants |
| STR-R-01..04 Repúdio | 1 | 4 | 4 | P3 | Verificação periódica da imutabilidade |
| STR-D-04 Poison message | 2 | 2 | 4 | P3 | Runbook de DLQ |
| Demais ameaças | ≤2 | ≤2 | ≤4 | P4 | Controles existentes suficientes; monitorar |

**Nenhuma ameaça residual em P1.** Itens P2 possuem plano de tratamento com dono e data: registrados no roadmap de segurança e revisados trimestralmente.

---

# 7. Matriz Ameaça × Ativo × Controle (resumo)

| Ameaça principal | Ativos | Controle âncora | Documento |
| ---------------- | ------ | --------------- | --------- |
| IDOR / cross-tenant | AST-001, AST-002 | Escopo + 404 + índices company-first | PR-001-09, PR-001-11 |
| Roubo/replay de credencial | AST-003 | JWT curto + refresh rotativo + blacklist | SEC-001 §5, §10 |
| Repúdio de decisões | AST-002 | Auditoria append-only + triggers | PR-001-11 §15.4 |
| Abuso de upload | AST-004, AST-010 | Allowlist + magic bytes + quarentena + sem URL pública | FD-001-03, SEC-001 §16 |
| Bypass SoD/alçada | AST-002, AST-007 | ABAC-01/02 + EXC-006 + auditoria | PR-001-09 §12–14 |
| Injection / mass assignment | AST-001, AST-007 | Parametrização + DTOs + fail-unknown | SEC-003 §10, §13 |
| DoS / abuso de recursos | AST-010 | Rate limit + keyset + timeouts + quotas | SEC-001 §14–15 |
| Supply chain | AST-005 | SCA + imagens assinadas + secret scan | SEC-001 §22 |
| Vazamento por logs/erros | AST-008 | Sanitização + erros padronizados | SEC-003 §9, SEC-001 §12 |
| Eventos falsos/poison | AST-006 | mTLS + envelope + idempotência + DLQ | PR-001-05 §14 |

---

# 8. Requisitos de Revisão

1. **Gatilhos de revisão deste documento:** novo módulo, nova integração externa, novo dado pessoal/sensível, mudança de autenticação/autorização, incidente de segurança relevante.
2. Revisão ordinária **trimestral** (riscos P2/P3) e **semestral** (modelo completo).
3. Novas ameaças entram com: ID STR-x-nn, vetor, ativo, mitigação, risco, prioridade e dono.
4. Itens P1 (se surgirem) bloqueiam release até mitigação.

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquitetura Trino | Versão inicial: escopo, ativos (AST-001..010), atores, vetores (VEC-001..010), 30 ameaças STRIDE com mitigações e risco residual, avaliação Probabilidade×Impacto com prioridades P1–P4, matriz ameaça×ativo×controle e requisitos de revisão |
