**Documento:** SEC-001 — Arquitetura de Segurança
**Versão:** 1.0.0
**Status:** Approved
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: OWASP ASVS 4.0, OWASP Top 10 (2021), OWASP API Security Top 10 (2023), Microsoft Secure Development Lifecycle (SDL), NIST SP 800-207 (Zero Trust), LGPD (Lei 13.709/2018).
> Documentos relacionados: SEC-002 (Threat Model STRIDE), SEC-003 (Padrão de Desenvolvimento Seguro), PR-001-09 (Permissions), FD-001-01 (IAM), FD-001-03 (Document Management).

# SEC-001 — Arquitetura de Segurança

> Este documento define a arquitetura oficial de segurança do Trino Supply. Nenhum módulo, serviço ou integração pode desviar destes controles sem exceção formal registrada em ADR.

---

# 1. Princípios de Segurança

| Princípio | Aplicação no Trino Supply |
| --------- | ------------------------- |
| **Segurança por padrão** | Todo controle ligado por default; desligar exige configuração explícita e auditada |
| **Deny by default** | Ausência de permissão explícita = negação (POL-AUTH-007, PR-001-09) |
| **Least privilege** | Usuários, serviços e contas de infraestrutura recebem o mínimo necessário |
| **Defense in Depth** | Nenhum controle único é ponto único de proteção (Seção 3) |
| **Zero Trust** | Nunca confiar, sempre verificar — mesmo dentro da rede interna (Seção 4) |
| **Auditoria obrigatória** | Toda decisão de segurança e alteração de dados sensíveis é registrada de forma imutável |
| **Fail securely** | Erros fecham acesso, nunca abrem; exceções não vazam informação |
| **Simplicidade** | Controle simples e verificável prevalece sobre controle complexo |

---

# 2. Threat Model (resumo)

O Threat Model completo está em **SEC-002 — Threat Model STRIDE**. Resumo executivo:

| Categoria | Ameaças prioritárias |
| --------- | -------------------- |
| **Spoofing** | Roubo/replay de JWT, credenciais fracas, session hijacking |
| **Tampering** | Alteração de dados fora do domínio, manipulação de eventos, adulteração de anexos |
| **Repudiation** | Negação de aprovações/decisões — mitigada por auditoria imutável |
| **Information Disclosure** | Vazamento cross-tenant, IDOR, exposição de storage, logs com dados sensíveis |
| **Denial of Service** | Abuso de API, queries custosas, flood de eventos |
| **Elevation of Privilege** | Escalação de escopo organizacional, SoD bypass, mass assignment |

Ativos críticos: dados de requisições e decisões de aprovação, credenciais e tokens, anexos (MinIO), registros de auditoria, segredos de infraestrutura.

---

# 3. Defense in Depth

```text
┌────────────────────────────────────────────────────────────┐
│ Camada 1 — Perímetro: TLS 1.3, WAF, rate limiting, DDoS    │
├────────────────────────────────────────────────────────────┤
│ Camada 2 — Identidade: FD-001-01 IAM, JWT curto + refresh  │
│            rotativo, MFA (v2), revogação (jti)             │
├────────────────────────────────────────────────────────────┤
│ Camada 3 — Autorização: RBAC + ABAC + Escopo (PR-001-09),  │
│            SoD, deny by default, avaliação server-side     │
├────────────────────────────────────────────────────────────┤
│ Camada 4 — Aplicação: validação de entrada, output         │
│            encoding, exception handling seguro (SEC-003)   │
├────────────────────────────────────────────────────────────┤
│ Camada 5 — Dados: isolamento por company_id, criptografia  │
│            em repouso/trânsito, soft delete, triggers      │
├────────────────────────────────────────────────────────────┤
│ Camada 6 — Infraestrutura: rede segmentada, segredos em    │
│            cofre, mTLS interno, hardening de containers    │
├────────────────────────────────────────────────────────────┤
│ Camada 7 — Detecção: logs centralizados, auditoria         │
│            imutável, alertas, IDS/IPS                      │
└────────────────────────────────────────────────────────────┘
```

Nenhuma camada confia na anterior: mesmo uma requisição autenticada (camada 2) passa por autorização (3), validação (4) e filtro de dados (5).

---

# 4. Zero Trust

Conforme NIST SP 800-207:

| Pilar | Implementação |
| ----- | ------------- |
| **Identidade verificada sempre** | Toda chamada (externa ou serviço-a-serviço) autenticada; não existe "rede confiável" |
| **Menor privilégio por requisição** | Autorização avaliada por recurso/ação/contexto a cada requisição (PR-001-09 §16) |
| **Assumir violação** | Segmentação por módulo; blast radius limitado por tenant e por serviço; logs assumem que o emissor pode estar comprometido |
| **Verificação contínua** | Tokens curtos (15 min), refresh rotativo com detecção de reuso, cache de decisão ≤ 60 s com invalidação em mudança de papel/escopo |
| **Contexto de dispositivo/sessão** | Sessões inventariadas e revogáveis (Seção 10); sinais de risco (troca de IP/dispositivo) forçam reautenticação em ações sensíveis (roadmap v2) |

---

# 5. Autenticação

Responsável: **FD-001-01 IAM**.

| Controle | Especificação |
| -------- | ------------- |
| Protocolo | OAuth2/OIDC; emissor interno do Trino Supply |
| Senha | BCrypt ou Argon2id (custo ≥ 12 / parâmetros OWASP); política mínima 12 caracteres; verificação contra listas de senhas vazadas |
| JWT | RS256/ES256 (assimétrico); `exp` ≤ 15 min; claims conforme PR-001-09 §12.4; sem dados sensíveis no payload |
| Refresh Token | Rotativo a cada uso; detecção de reuso revoga a cadeia inteira; armazenado hasheado; vida útil ≤ 30 dias |
| MFA | Preparado para v2 (TOTP); obrigatório para System Administrator no lançamento do v2 |
| Login | Resposta uniforme para usuário inexistente/senha inválida (anti-enumeração); bloqueio progressivo após 5 falhas; CAPTCHA após 3 falhas (público) |
| Performance | Autenticação < 2 s (RNF FD-001-01) |
| Rotação de chaves | Chaves de assinatura JWT rotacionadas a cada 90 dias com overlap de validação (kid) |

---

# 6. Autorização

Responsável: motor de autorização do módulo + escopo do Foundation. Especificação completa em **PR-001-09**:

- RBAC (papel + escopo) → ABAC (SoD, alçada, estado, titularidade) → Delegação → deny by default.
- Avaliação **server-side em 100% das requisições**; o frontend apenas reflete.
- Recurso fora do escopo → **404** (anti-IDOR), nunca 403 com confirmação de existência.
- Toda decisão (permit/deny) auditada.

---

# 7. Criptografia

| Contexto | Controle |
| -------- | -------- |
| Trânsito externo | TLS 1.3 (mínimo 1.2 desabilitado); HSTS `max-age=31536000; includeSubDomains; preload` |
| Trânsito interno | mTLS entre serviços (service mesh ou certificados internos); Redis e RabbitMQ com TLS habilitado |
| Repouso — PostgreSQL | Criptografia de volume (LUKS/cloud KMS) + `pgcrypto` para campos sensíveis quando aplicável |
| Repouso — MinIO | SSE (server-side encryption) com chaves gerenciadas em KMS |
| Repouso — Backups | AES-256 (SEC-001 §19 / PR-001-11 §15.13) |
| Senhas | BCrypt/Argon2id (Seção 5) |
| Segredos em trânsito de mensagem | Payload de eventos sem segredos; dados sensíveis por referência (IDs), nunca por valor |
| Gestão de chaves | KMS/cofre central; rotação anual ou sob incidente; acesso a chaves auditado |

---

# 8. OWASP — Cobertura

## 8.1 OWASP Top 10 (2021)

| Risco | Controle principal |
| ----- | ------------------ |
| A01 Broken Access Control | PR-001-09 completo; 404 anti-IDOR; testes de autorização obrigatórios |
| A02 Cryptographic Failures | Seção 7 |
| A03 Injection | EF Core parametrizado; proibido SQL dinâmico; validação de entrada (SEC-003) |
| A04 Insecure Design | Este documento + SEC-002 + SEC-003; ADRs de segurança |
| A05 Security Misconfiguration | Hardening de containers, headers de segurança, infra como código revisada |
| A06 Vulnerable Components | SCA no pipeline (SCA + dependabot); atualização mensal de dependências |
| A07 Auth Failures | Seção 5 |
| A08 Data Integrity Failures | Outbox transacional, triggers de imutabilidade, auditoria append-only |
| A09 Logging Failures | Seções 12–13; logs de segurança com alerta |
| A10 SSRF | Allowlist de destinos; proibido fetch arbitrário de URL fornecida pelo usuário |

## 8.2 OWASP API Security Top 10 (2023)

| Risco | Controle |
| ----- | -------- |
| API1 BOLA/IDOR | Filtro de escopo em toda query + 404; testes TC-UC-008-5 |
| API2 Broken Authentication | Seção 5; revogação por `jti` |
| API3 Object Property Level | DTOs de saída explícitos; nunca serializar entidade de domínio; comentários `internal` filtrados por papel |
| API4 Unrestricted Resource Consumption | Rate limiting por usuário/IP/tenant; `page_size` ≤ 100; `statement_timeout` |
| API5 BFLA | Permission Evaluation Flow (PR-001-09 §16) em todo endpoint |
| API6 Business Flow Abuse | SoD, alçada, limites de ciclo; rate limit em ações sensíveis |
| API7 SSRF | Seção 8.1 A10 |
| API8 Misconfiguration | Seção 8.1 A05 |
| API9 Improper Inventory | Catálogo de APIs no registry; versionamento `/api/v1`; deprecação formal |
| API10 Unsafe Consumption | Validação de payloads de eventos; schema de envelope (PR-001-05 §14.1) |

## 8.3 OWASP ASVS 4.0 — Nível-alvo

**Alvo: ASVS Level 2** para todos os módulos (Level 3 para IAM e Auditoria). Checklist operacionalizado em SEC-003 §17; verificação por release (SAST + DAST + revisão manual dos itens não automatizáveis).

---

# 9. LGPD

| Tema | Implementação |
| ---- | ------------- |
| **Base legal** | Execução de contrato e legítimo interesse (dados corporativos de suprimentos) |
| **Dados pessoais tratados** | Identificação de usuários (nome, e-mail corporativo), trilhas de decisão (solicitante/aprovador) |
| **Minimização** | Nenhum dado pessoal além do necessário ao processo; anexos são responsabilidade do tenant (DPA) |
| **Papel da plataforma** | Operadora (o cliente/tenant é o controlador); contrato de tratamento (DPA) obrigatório |
| **Direitos do titular** | Acesso e correção via administração do tenant; eliminação atendida por soft delete + purge programado conforme retenção |
| **Retenção** | Auditoria de negócio: 5 anos (obrigação legal corporativa); logs técnicos: 12 meses; após o prazo, anonimização/purge |
| **Registro de acesso** | Toda leitura em perfil Auditor e toda exportação são auditadas (quem, o quê, quando) |
| **Incidentes** | Plano de resposta a incidente com notificação à ANPD e ao controlador conforme art. 48 |
| **Transferência** | Dados em região contratada pelo tenant; sem transferência internacional por padrão |

---

# 10. Session Management

| Controle | Especificação |
| -------- | ------------- |
| Modelo | Stateless JWT + refresh token rotativo (Seção 5); sem sessão server-side para APIs |
| Cookies (frontend) | `HttpOnly; Secure; SameSite=Strict` para refresh; token de acesso em memória (nunca localStorage) |
| Inventário | Sessões ativas listáveis pelo usuário e pelo administrador; revogação individual e global |
| Revogação | Por `jti` (blacklist Redis com TTL = expiração residual) e por cadeia de refresh |
| Logout | Revoga refresh + insere `jti` na blacklist; front descarta tokens |
| Timeout | Inatividade: refresh expira em 30 dias; ações sensíveis (aprovação acima de alçada, administração) podem exigir reautenticação recente (`auth_time` ≤ 15 min) |

---

# 11. Gestão de Segredos

| Controle | Especificação |
| -------- | ------------- |
| Cofre | Segredos em gerenciador dedicado (ex.: Azure Key Vault, AWS Secrets Manager, Vault); **nunca** em código, `.env` versionado ou variáveis de CI em texto claro |
| Injeção | Variáveis de ambiente injetadas pelo orquestrador (Docker/K8s secrets) com montagem em memória quando possível |
| Rotação | Credenciais de banco/broker/storage rotacionadas a cada 90 dias; rotação de emergência sob incidente |
| Acesso | Leitura de segredos por identidade de serviço (least privilege); todo acesso auditado |
| Scan | Pipeline com secret scanning (gitleaks/trufflehog) bloqueante; histórico do Git incluído |
| Proibições | Sem segredo em logs, exceções, URLs, payloads de evento ou comentários de código |

---

# 12. Logs de Segurança

| Controle | Especificação |
| -------- | ------------- |
| Centralização | Logs estruturados (JSON) enviados a coletor central; retenção 12 meses |
| Eventos obrigatórios | Login (sucesso/falha), logout, revogação, deny de autorização (passos 2–5 do fluxo PR-001-09 §16), alteração de papel/escopo/delegação, alteração de configuração de segurança, acesso a auditoria, exportações, downloads de anexos, bloqueios de upload, falhas de validação repetidas |
| Conteúdo | `timestamp (UTC), correlationId, actorId, tenantId, action, result, sourceIp, userAgent, resourceId` |
| Proibições | Sem senhas, tokens, segredos, payloads completos de anexos ou dados pessoais excessivos; mascaramento de e-mails em logs de rotina |
| Integridade | Trilha de auditoria de negócio em storage append-only (Seção 13); logs técnicos com assinatura/encadeamento quando exigido por compliance |
| Alertas | Regras de detecção sobre o stream: brute force, enumeração (404 em massa), negação repetida de autorização, acesso fora de horário a auditoria |

---

# 13. Auditoria

| Controle | Especificação |
| -------- | ------------- |
| Append-only | Registros de auditoria e timeline imutáveis (trigger de bloqueio de UPDATE/DELETE — PR-001-11 §15.4) |
| Cobertura | Toda decisão de autorização, toda mutação de aggregate, toda decisão de aprovação (com `delegatedBy` quando aplicável), todo acesso privilegiado |
| Rastreabilidade | `correlationId` conecta requisição → comando → evento → notificação (PR-001-05 §14.1) |
| Acesso | Leitura restrita (PR-PERM-013); acesso à auditoria é ele mesmo auditado |
| Retenção | 5 anos (LGPD §9) |
| Proteção contra repúdio | Registro atômico na mesma transação da mutação; actor resolvido server-side, nunca informado pelo cliente |

---

# 14. WAF, IDS e IPS

| Componente | Função no Trino Supply |
| ---------- | ---------------------- |
| **WAF** | Edge/ingress com OWASP Core Rule Set; regras custom: bloqueio de path traversal, métodos não usados, payloads anômalos em upload; modo bloqueante em produção |
| **Rate limiting** | Por IP, por usuário e por tenant; limites mais estritos em login e endpoints de escrita |
| **IDS** | Detecção sobre logs centralizados (Seção 12): padrões de ataque (SQLi/XSS probing, enumeração, credential stuffing) geram alerta |
| **IPS** | Bloqueio automático temporário de origem em brute force/credential stuffing; lista de bloqueio compartilhada com o WAF |
| **Resposta** | Runbook de incidente: detecção → contenção (bloqueio/revogação) → erradicação → lições; severidade e comunicação conforme plano de resposta |

---

# 15. API Security

| Controle | Especificação |
| -------- | ------------- |
| Versionamento | `/api/v1`; breaking changes só em nova major; deprecação com sunset header |
| Contratos | OpenAPI por módulo; validação de request/response contra schema no gateway (request validation bloqueante) |
| Payloads | Limites de tamanho (JSON ≤ 1 MB fora upload); content-type estrito; rejeição de campos desconhecidos (`fail on unknown properties`) |
| Headers de segurança | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy` restritiva, `Cache-Control: no-store` em respostas autenticadas |
| CORS | Allowlist explícita de origens por ambiente; sem `*` com credenciais |
| Correlation | `X-Correlation-Id` obrigatório/propagado (PR-001-05) |
| Erros | Formato padronizado com código `PR-ERR-xxx`; sem stack trace, SQL ou dados internos |
| Idempotência | Mutações sensíveis aceitam `Idempotency-Key` (24 h) para retry seguro de cliente |

---

# 16. Upload e Download de Arquivos

Responsável: FD-001-03 Document Management + controles do módulo.

## Upload

| Controle | Especificação |
| -------- | ------------- |
| Tipos permitidos | PDF, DOCX, XLSX, PNG, JPEG, WebP (allowlist; constraint em banco — PR-001-11 §15.1) |
| Magic bytes | Validação de assinatura real do arquivo; extensão divergente do conteúdo → bloqueio |
| Tamanho | ≤ 25 MB por arquivo (`attachment.max-size.mb`); ≤ 10 anexos por requisição |
| Sanitização | Nome de arquivo normalizado; path traversal impossível (storage_key gerado pelo servidor) |
| Antivírus/quarentena | Quando habilitado (`attachment.av.enabled`): arquivo em bucket de quarentena até veredito; indisponível para download até liberação |
| Execução | Nenhum arquivo servido a partir de diretório executável; content-type forçado + `X-Content-Type-Options: nosniff` |

## Download

| Controle | Especificação |
| -------- | ------------- |
| Autorização | Mesma avaliação de escopo da requisição; 404 fora do escopo |
| URL assinada | Emissão sob demanda, expiração ≤ 5 min, single-purpose; **nunca URL pública** (FD-001-03) |
| Auditoria | Todo download registrado (quem, arquivo, quando) |
| Headers | `Content-Disposition: attachment` com nome sanitizado; sem inline rendering de tipos ativos |

---

# 17. Storage — MinIO

| Controle | Especificação |
| -------- | ------------- |
| Exposição | Sem acesso público a buckets; política `private` obrigatória; acesso exclusivo via aplicação com URLs assinadas |
| Credenciais | Service account por aplicação, least privilege (bucket por domínio: `trino-docs-prod`); chaves no cofre, rotação 90 dias |
| Criptografia | SSE com KMS; TLS obrigatório |
| Versionamento | Versioning habilitado nos buckets de documentos (proteção contra sobrescrita/ransomware) |
| Ciclo de vida | Quarentena → ativo → arquivamento; purge conforme retenção legal |
| Rede | MinIO não exposto à internet; acesso apenas pela rede interna dos serviços |

---

# 18. Redis

| Controle | Especificação |
| -------- | ------------- |
| Autenticação | `requirepass` + ACL por serviço (comandos mínimos); usuário default desabilitado |
| Transporte | TLS obrigatório |
| Conteúdo | Apenas dados voláteis (cache, blacklist de `jti`, rate limit); **nunca** fonte de verdade nem segredos |
| TTL | Todo dado com expiração; blacklist de token com TTL = expiração residual do JWT |
| Comandos | `FLUSHALL`, `CONFIG`, `KEYS` desabilitados via ACL/renomeação |
| Persistência | RDB/AOF conforme necessidade (blacklist: AOF everysec); backup não obrigatório para cache puro |

---

# 19. RabbitMQ

| Controle | Especificação |
| -------- | ------------- |
| Autenticação | Usuário por serviço; vhost por domínio (`/procurement`); permissões mínimas por exchange/fila |
| Transporte | TLS obrigatório; painel de gestão restrito à rede de operações |
| Mensagens | Payload sem segredos nem dados pessoais excessivos; envelope assinado pela aplicação quando consumido por integrações externas (roadmap) |
| DLQ | Monitorada com alerta; reprocessamento operacional autenticado e auditado (PR-001-05 §14, PR-001-10 §14.3) |
| Gestão | Políticas de fila via definições declaradas (infra como código); sem criação ad-hoc em produção |

---

# 20. PostgreSQL

| Controle | Especificação |
| -------- | ------------- |
| Contas | Conta por serviço, sem superuser; permissões mínimas (CRUD no schema do módulo); conta de migration separada, usada só no pipeline |
| Isolamento | Filtro `company_id` obrigatório em toda query (PR-001-11 §9); RLS (Row-Level Security) avaliado como hardening v2 |
| Transporte | TLS obrigatório; `sslmode=verify-full` |
| SQL | 100% parametrizado via EF Core; SQL dinâmico proibido (SEC-003 §10) |
| Auditoria de banco | `log_connections`, `log_disconnections`, `log_statement = 'ddl'`; logs a coletor central |
| Backup | Conforme PR-001-11 §15.13 (PITR, criptografia, teste mensal) |
| Rede | Sem exposição pública; security group/ACL restrito aos serviços |

---

# 21. Diagramas C4 (segurança)

## 21.1 Nível 1 — Contexto

```text
                         ┌──────────────┐
                         │   Usuários    │
                         │ (Solicitante, │
                         │ Gestor, etc.) │
                         └──────┬───────┘
                                │ HTTPS/TLS 1.3
                                ▼
┌─────────┐   WAF/rate   ┌─────────────────────────┐
│ Internet│ ───────────▶ │      Trino Supply        │
└─────────┘              │  (Frontend + APIs)       │
                         └───────────┬─────────────┘
                                     │
        ┌────────────────────────────┼─────────────────────────────┐
        ▼                            ▼                             ▼
┌───────────────┐           ┌────────────────┐            ┌────────────────┐
│ IdP interno    │           │ Infra interna   │            │ Integrações     │
│ (FD-001-01 IAM)│           │ (PG/Redis/MQ/   │            │ (roadmap: ERP,  │
│                │           │ MinIO — mTLS)   │            │ webhooks —      │
│                │           │                 │            │ assinadas)      │
└───────────────┘           └────────────────┘            └────────────────┘
```

## 21.2 Nível 2 — Containers com fronteiras de confiança

```text
        ┌───────────────── ZONA PÚBLICA ─────────────────┐
        │  CDN/WAF ──▶ Frontend (Next.js, BFF opcional)  │
        └──────────────────────┬─────────────────────────┘
                               │ TLS 1.3 + JWT
        ┌───────────────── ZONA APLICAÇÃO ────────────────▼──┐
        │  API Gateway (auth, rate limit, request validation) │
        │      │                                             │
        │      ├──▶ IAM Service (FD-001-01)                  │
        │      ├──▶ PR-001 Service (Procurement)             │
        │      ├──▶ Workflow Engine                          │
        │      ├──▶ Notification Service                     │
        │      └──▶ Document Service (FD-001-03)             │
        └───────────────┬─────────────────────────────────────┘
                        │ mTLS, service accounts, least privilege
        ┌───────────────▼──── ZONA DADOS ───────────────────┐
        │  PostgreSQL │ Redis │ RabbitMQ │ MinIO            │
        │  (sem exposição pública; backups criptografados)  │
        └───────────────────────────────────────────────────┘
                        │
        ┌───────────────▼─── ZONA OBSERVABILIDADE ─────────┐
        │  Logs centrais │ Auditoria append-only │ Alertas │
        └───────────────────────────────────────────────────┘
```

**Fronteiras de confiança:** (1) Internet→WAF, (2) Frontend→Gateway, (3) Gateway→Serviços, (4) Serviços→Dados, (5) Qualquer→Auditoria. Toda travessia exige autenticação e gera telemetria.

## 21.3 Fluxo — Requisição autenticada

```text
Cliente
  │ 1. POST /api/v1/purchase-requisitions (JWT)
  ▼
WAF ── 2. regras OWASP + rate limit
  ▼
Gateway ── 3. valida JWT (assinatura, exp, blacklist jti)
  ▼
PR-001 ── 4. Permission Evaluation Flow (escopo→RBAC→ABAC→delegação)
  ▼
Domínio ── 5. validações de negócio + invariantes
  ▼
PostgreSQL ── 6. transação: aggregate + outbox (mesma TX)
  ▼
RabbitMQ ── 7. relay outbox → consumidores (workflow, notificação, timeline, auditoria)
  ▼
Auditoria ── 8. registro imutável da decisão e da mutação
```

## 21.4 Fluxo — Upload de anexo

```text
Cliente ──▶ WAF ──▶ Gateway (JWT) ──▶ PR-001 (autorização + estado)
   │                                   │
   │                                   ▼
   │                     Validação: tipo, magic bytes, tamanho
   │                                   │
   │                                   ▼
   │                     Document Service (FD-001-03)
   │                        ├── AV habilitado? → quarentena → veredito
   │                        └── MinIO (bucket privado, SSE, key gerada)
   │                                   │
   │                                   ▼
   │                     Metadados em PostgreSQL + EVT-014 + auditoria
   ▼
Download: solicitação autenticada → URL assinada ≤ 5 min → auditoria de acesso
```

---

# 22. Boas Práticas Microsoft (SDL) adotadas

| Prática | Aplicação |
| ------- | --------- |
| Threat modeling por design | SEC-002 revisado a cada mudança estrutural |
| Requisitos de segurança por release | Gate de release: SAST + SCA + DAST + secret scan + ASVS L2 |
| Ferramentas aprovadas | Somente bibliotecas/imagens de fontes verificadas; imagens Docker assinadas e com scan de vulnerabilidades |
| Análise estática | Analyzers .NET de segurança + regras customizadas do projeto no pipeline (bloqueante) |
| Análise dinâmica | DAST (OWASP ZAP) em staging a cada release |
| Revisão de superfície de ataque | Novos endpoints/integrações exigem atualização de SEC-002 e revisão arquitetural |
| Resposta a incidentes | Plano de resposta com severidades, contenção e comunicação (LGPD art. 48) |
| Padrões de codificação segura | SEC-003 como padrão oficial obrigatório |

---

# 23. Exceções e Governança

1. Qualquer desvio deste documento exige **exceção formal**: justificativa, compensação, validade (≤ 6 meses) e aprovação de Arquitetura + Segurança, registrada em ADR.
2. Este documento é revisado a cada release estrutural ou incidente relevante.
3. Conflito entre código e este documento → **o documento está correto**; o código deve ser corrigido (governança GOV-002).

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquitetura Trino | Versão inicial: princípios, threat model (resumo), defense in depth, zero trust, autenticação, autorização, criptografia, OWASP (Top 10, API Security, ASVS), LGPD, session management, segredos, logs, auditoria, WAF/IDS/IPS, API security, upload/download, MinIO, Redis, RabbitMQ, PostgreSQL, diagramas C4, fluxos, boas práticas Microsoft SDL e governança de exceções |
