**Documento:** SEC-003 — Padrão de Desenvolvimento Seguro
**Versão:** 1.0.0
**Status:** Approved
**Escopo:** Todo código do Trino Supply (Frontend Next.js/React/TypeScript/Tailwind, Backend .NET 9/ASP.NET Core, PostgreSQL, integrações)

> Este é o padrão oficial e obrigatório de desenvolvimento seguro do Trino Supply. Deriva de SEC-001 (arquitetura) e SEC-002 (threat model) e operacionaliza o OWASP ASVS 4.0 (nível 2; nível 3 para IAM e Auditoria). Código em desacordo não passa em code review nem em pipeline.
> Regra de ouro: **confie em nada que vem do cliente — valide tudo no servidor.**

# SEC-003 — Padrão de Desenvolvimento Seguro

---

# 1. Regras Universais (valem para todo código)

| Código | Regra |
| ------ | ----- |
| SEC-GEN-01 | Toda entrada é hostil até ser validada no servidor. Validação de cliente é UX, nunca controle |
| SEC-GEN-02 | Deny by default: acesso, campos, tipos, origens — só passa o explicitamente permitido |
| SEC-GEN-03 | Nenhum segredo em código, configuração versionada, logs, exceções, URLs ou comentários |
| SEC-GEN-04 | Toda decisão de autorização é server-side e auditada (PR-001-09 §16) |
| SEC-GEN-05 | Erros nunca expõem internals: sem stack trace, SQL, caminhos, versões ou dados de outros usuários |
| SEC-GEN-06 | Toda data/hora em UTC; toda saída codificada conforme o contexto (HTML, JS, URL, SQL, shell) |
| SEC-GEN-07 | Dependências: somente fontes oficiais, com SCA bloqueante no pipeline; revisão mensal |
| SEC-GEN-08 | Recursos de segurança nunca são opcionais por flag em produção sem exceção em ADR |

---

# 2. Frontend (Next.js / React / TypeScript / Tailwind)

| Código | Regra |
| ------ | ----- |
| SEC-FE-01 | Token de acesso apenas em memória; **proibido** `localStorage`/`sessionStorage` para tokens. Refresh token somente em cookie `HttpOnly; Secure; SameSite=Strict` |
| SEC-FE-02 | Nunca renderizar dado de usuário sem escaping do React. `dangerouslySetInnerHTML` **proibido**; rico/formatado só via sanitizador aprovado (DOMPurify) com allowlist |
| SEC-FE-03 | Autorização de UI apenas reflete o backend: esconder botão não é controle; toda ação é revalidada no servidor |
| SEC-FE-04 | Formulários usam schemas de validação (ex.: Zod) idênticos aos contratos da API; mensagens de erro genéricas para falhas de autenticação |
| SEC-FE-05 | Rotas protegidas verificam sessão no servidor (SSR/middleware) antes de renderizar dados |
| SEC-FE-06 | URLs externas/abertas por parâmetro: validar contra allowlist antes de navegar (anti open-redirect); `target="_blank"` sempre com `rel="noopener noreferrer"` |
| SEC-FE-07 | CSP restritiva entregue pelo servidor; sem `unsafe-inline` para scripts; nonces quando necessário |
| SEC-FE-08 | Sem dados sensíveis em logs de console, analytics ou ferramentas de monitoramento de front |
| SEC-FE-09 | Upload: validação de tipo/tamanho no cliente como UX; validação real é no servidor (Seção 7) |
| SEC-FE-10 | Dependências de front com lockfile íntegro e SCA; proibido carregar scripts de CDNs não aprovadas |

---

# 3. Backend (.NET 9 / ASP.NET Core)

| Código | Regra |
| ------ | ----- |
| SEC-BE-01 | `[Authorize]` em **todos** os endpoints por padrão; endpoints anônimos exigem `[AllowAnonymous]` explícito + justificativa em revisão |
| SEC-BE-02 | Model binding apenas para DTOs de entrada dedicados (Seção 13); **proibido** bind direto em entidade de domínio/EF |
| SEC-BE-03 | Injeção de dependência para todo acesso a configuração/segredos; `IConfiguration` nunca logada |
| SEC-BE-04 | `IDataProtector`/criptografia somente com chaves do cofre; proibido algoritmo próprio ou deprecated (MD5, SHA1, DES, RC4) |
| SEC-BE-05 | `HttpClient` com `IHttpClientFactory`, timeouts explícitos (≤ 30 s) e validação de resposta; proibido `HttpClient` estático/manual |
| SEC-BE-06 | `Task`/`async` sem `Result`/`.Wait()` (deadlock); cancellation tokens propagados |
| SEC-BE-07 | Serialização JSON: `JsonSerializerDefaults.Web`, fail on unknown properties, sem `TypeNameHandling` |
| SEC-BE-08 | Tratamento global de exceções via middleware (Seção 9); `try/catch` local apenas com ação concreta |
| SEC-BE-09 | Background services com escopo de DI correto (`IServiceScopeFactory`); nunca capturar `DbContext` de escopo em singleton |
| SEC-BE-10 | Analyzers de segurança .NET habilitados como erro (`CA3xxx`, `S4xxx` Sonar); supressões exigem comentário + ticket |
| SEC-BE-11 | Recursos nativos/arquivos: `Path.GetFullPath` + verificação de prefixo antes de qualquer I/O; proibido concatenação de caminho com entrada do usuário |
| SEC-BE-12 | Comparação de segredos/tokens sempre com `CryptographicOperations.FixedTimeEquals` |

---

# 4. API (contratos e transporte)

| Código | Regra |
| ------ | ----- |
| SEC-API-01 | HTTPS obrigatório; redirecionamento 80→443; HSTS habilitado |
| SEC-API-02 | `X-Correlation-Id` propagado em toda chamada e log; gerado no gateway se ausente |
| SEC-API-03 | Request body ≤ 1 MB (exceto upload); `RequestSizeLimit` explícito por endpoint |
| SEC-API-04 | Content-Type estrito; rejeitar `415` para tipos não suportados; `charset=utf-8` |
| SEC-API-05 | Respostas autenticadas com `Cache-Control: no-store`; sem dados em URL (query) de endpoints sensíveis |
| SEC-API-06 | Paginação somente keyset com cursor assinado/opaco (PR-001-11 §15.9); `pageSize` ∈ [1,100] |
| SEC-API-07 | Mutações sensíveis aceitam `Idempotency-Key` (24 h); retry de cliente não duplica efeito |
| SEC-API-08 | Erros no envelope padrão: `{ code: "PR-ERR-xxx", message, correlationId }`; HTTP semântico (400/401/403/404/409/422) |
| SEC-API-09 | Rate limiting aplicado por política (IP, usuário, tenant); respostas 429 com `Retry-After` |
| SEC-API-10 | Versionamento `/api/v{n}`; deprecação com `Sunset` header e aviso no registry de APIs |

---

# 5. Autenticação, JWT e Refresh Token (implementação)

| Código | Regra |
| ------ | ----- |
| SEC-AUTH-01 | Validação de JWT: assinatura (RS256/ES256), `iss`, `aud`, `exp`, `nbf`, tenant e blacklist `jti` — nunca confiar só na assinatura |
| SEC-AUTH-02 | `exp` ≤ 15 min; clock skew ≤ 1 min; `kid` resolvido de JWKS com cache curto |
| SEC-AUTH-03 | Claims de autorização (roles/perms/scope/alçada) revalidadas no Permission Evaluation Flow; token é entrada, não decisão final |
| SEC-AUTH-04 | Refresh token: rotativo, hasheado em repouso, bound ao dispositivo/sessão, detecção de reuso revoga a cadeia (SEC-001 §5) |
| SEC-AUTH-05 | Logout e troca de senha revogam refresh + inserem `jti` ativos na blacklist (TTL = expiração residual) |
| SEC-AUTH-06 | Senha: BCrypt (custo ≥ 12) ou Argon2id; comparação em tempo constante; resposta uniforme para falha |
| SEC-AUTH-07 | Reset de senha: token aleatório (≥ 128 bits), uso único, expiração ≤ 1 h, invalida sessões anteriores |
| SEC-AUTH-08 | Proibido aceitar tokens de emissores não configurados ou `alg=none`; algoritmo fixo por configuração |

---

# 6. Validação de Entrada

| Código | Regra |
| ------ | ----- |
| SEC-VAL-01 | Validação server-side em 100% dos campos: tipo, formato, tamanho, faixa e regra de negócio (PR-001-02) |
| SEC-VAL-02 | Allowlist, nunca blocklist: enums por código, MIME por lista, unidades por tabela |
| SEC-VAL-03 | Strings: tamanho máximo explícito; normalização Unicode (NFC); trimming; rejeição de caracteres de controle |
| SEC-VAL-04 | IDs/UUIDs: parse estrito; GUID inválido → 400, nunca 500 |
| SEC-VAL-05 | Números/datas: faixas explícitas (ex.: `quantity > 0`, `pageSize ≤ 100`); datas ISO-8601; fuso sempre UTC |
| SEC-VAL-06 | Arquivos: extensão + MIME + **magic bytes** + tamanho (Seção 7); nome sanitizado |
| SEC-VAL-07 | JSON: fail on unknown properties; profundidade/tamanho máximo; sem `__proto__`/chaves perigosas |
| SEC-VAL-08 | Validações centralizadas (FluentValidation/attrs); proibida validação espalhada em controllers |

---

# 7. Upload e Download (implementação)

| Código | Regra |
| ------ | ----- |
| SEC-FILE-01 | Upload apenas via endpoint dedicado com `[RequestSizeLimit]`; stream para o storage, nunca buffer completo em memória |
| SEC-FILE-02 | `storage_key` gerado pelo servidor (UUID); proibido usar nome/caminho do cliente como chave |
| SEC-FILE-03 | Magic bytes verificados contra a extensão (SEC-001 §16); divergência → bloqueio + log de segurança |
| SEC-FILE-04 | AV/quarentena quando habilitado: arquivo indisponível para download até veredito |
| SEC-FILE-05 | Download: autorização de escopo → URL assinada ≤ 5 min; resposta com `Content-Disposition: attachment`, `nosniff`, content-type do metadado |
| SEC-FILE-06 | Proibido servir uploads como conteúdo inline executável; nenhum arquivo do MinIO é exposto por rota estática |
| SEC-FILE-07 | Todo upload/download auditado (quem, arquivo, tamanho, resultado) |

---

# 8. Logs (implementação)

| Código | Regra |
| ------ | ----- |
| SEC-LOG-01 | Logs estruturados (JSON) com campos fixos: `timestamp, level, correlationId, actorId, tenantId, action, result` |
| SEC-LOG-02 | Proibido logar: senhas, tokens (qualquer parte), segredos, payloads de anexos, dados pessoais excessivos, connection strings |
| SEC-LOG-03 | E-mail e identificadores pessoais mascarados em logs de rotina (`t***@dominio.com`); completos apenas em auditoria protegida |
| SEC-LOG-04 | Eventos de segurança (login, deny, revogação, config) sempre em nível `Information`+ e enviados ao coletor central |
| SEC-LOG-05 | Log injection: valores de usuário sanitizados (sem quebra de linha/ANSI) antes de logar |
| SEC-LOG-06 | Exceções logadas uma única vez (middleware), com `correlationId`; sem re-log em cada camada |

---

# 9. Exception Handling

| Código | Regra |
| ------ | ----- |
| SEC-EXC-01 | Middleware global converte exceções no envelope de erro padrão (SEC-API-08) com `correlationId` |
| SEC-EXC-02 | Exceções de negócio usam códigos `PR-ERR-xxx` (PR-001-02); exceções técnicas → `500` genérico ao cliente |
| SEC-EXC-03 | Proibido vazar: stack trace, mensagens de SQL, caminhos, versões de framework, IDs internos de infra |
| SEC-EXC-04 | Falha de autorização: 401 (não autenticado), 403 (sem permissão), **404** (fora de escopo — anti-IDOR) |
| SEC-EXC-05 | Fail closed: exceção durante autorização ou validação de segurança = acesso negado |
| SEC-EXC-06 | Concorrência otimista: conflito de `version` → `409 PR-ERR-409`, sem retry automático silencioso |

---

# 10. SQL e Acesso a Dados

| Código | Regra |
| ------ | ----- |
| SEC-SQL-01 | 100% das consultas via EF Core parametrizado ou SQL com parâmetros nomeados; **proibida** concatenação/interpolação de entrada do usuário em SQL |
| SEC-SQL-02 | `FromSqlRaw` proibido; `FromSqlInterpolated` apenas com revisão; ordenação dinâmica por allowlist de colunas |
| SEC-SQL-03 | Toda query de leitura filtra `company_id` (e escopo); repositories não expõem métodos sem filtro de tenant |
| SEC-SQL-04 | Conta da aplicação sem DDL/superuser; migrations por conta separada no pipeline (SEC-001 §20) |
| SEC-SQL-05 | `AsNoTracking` em consultas de leitura; projeções explícitas (sem `SELECT *` em listagens) |
| SEC-SQL-06 | Transações curtas; outbox na mesma transação do aggregate; nunca I/O externo dentro de transação |
| SEC-SQL-07 | `statement_timeout`, `lock_timeout` e pool limit configurados (PR-001-11 §15.11) |

---

# 11. XSS (Cross-Site Scripting)

| Código | Regra |
| ------ | ----- |
| SEC-XSS-01 | Escaping automático do React em toda interpolação; proibido `dangerouslySetInnerHTML` (SEC-FE-02) |
| SEC-XSS-02 | Conteúdo de usuário armazenado como **texto puro**; sanitização na entrada de campos ricos (quando existirem) com DOMPurify |
| SEC-XSS-03 | CSP restritiva (script-src 'self' + nonce); `X-Content-Type-Options: nosniff` em todas as respostas |
| SEC-XSS-04 | Dados de usuário em atributos/URLs/JS: encoding específico do contexto; proibido montar JS com dados de usuário |
| SEC-XSS-05 | Testes: payload XSS (`<script>`, `onerror=`, `javascript:`) nos testes de comentário/mensagem (TC-UC-011-3) |

---

# 12. CSRF (Cross-Site Request Forgery)

| Código | Regra |
| ------ | ----- |
| SEC-CSRF-01 | APIs autenticadas por `Authorization: Bearer` são imunes a CSRF por design; **proibido** autenticar API por cookie de sessão |
| SEC-CSRF-02 | Onde houver cookie (refresh token, BFF): `SameSite=Strict` + token anti-CSRF (double submit) nas mutações baseadas em cookie |
| SEC-CSRF-03 | CORS por allowlist de origens; `Access-Control-Allow-Credentials` apenas com origens explícitas |
| SEC-CSRF-04 | Mutações exigem `Content-Type: application/json` (preflight obrigatório) |

---

# 13. SSRF, IDOR e Mass Assignment

## 13.1 SSRF

| Código | Regra |
| ------ | ----- |
| SEC-SSRF-01 | Proibido fetch de URL fornecida pelo usuário sem allowlist de domínios/destinos |
| SEC-SSRF-02 | Integrações externas (roadmap): destinos cadastrados por administrador; resolução DNS validada (sem IPs privados/loopback); timeout ≤ 10 s |
| SEC-SSRF-03 | Webhooks de saída (roadmap): payload assinado, sem dados internos, URL validada no cadastro |

## 13.2 IDOR / Broken Object Level Authorization

| Código | Regra |
| ------ | ----- |
| SEC-IDOR-01 | Todo acesso a objeto passa pelo Permission Evaluation Flow com filtro de escopo **antes** de qualquer leitura |
| SEC-IDOR-02 | Objeto fora do escopo → **404**; proibido distinguir "existe mas sem acesso" de "não existe" |
| SEC-IDOR-03 | IDs previsíveis são irrelevantes: a autorização nunca depende do sigilo do ID |
| SEC-IDOR-04 | Testes obrigatórios de IDOR por endpoint (acesso cruzado de usuário/tenant — TC-UC-008-3/5) |

## 13.3 Mass Assignment

| Código | Regra |
| ------ | ----- |
| SEC-MASS-01 | Binding somente em DTOs de entrada por caso de uso; campos do servidor (`id`, `status`, `version`, `companyId`, `createdBy`, `number`) **nunca** vêm do cliente |
| SEC-MASS-02 | Serialização com fail on unknown properties (SEC-BE-07) |
| SEC-MASS-03 | Atualizações parciais (PATCH) por lista explícita de campos permitidos |
| SEC-MASS-04 | Mudança de status somente via transições da State Machine (PR-001-03); proibido update direto de `status` |

---

# 14. Broken Access Control (prevenção sistemática)

| Código | Regra |
| ------ | ----- |
| SEC-BAC-01 | Middleware de autorização executa o Permission Evaluation Flow em 100% das rotas autenticadas |
| SEC-BAC-02 | SoD e alçada avaliados no domínio (ABAC-01/02), nunca só na UI |
| SEC-BAC-03 | Function-level: endpoints de aprovação/admin verificam a permissão específica (PR-PERM-xxx), não apenas "estar logado" |
| SEC-BAC-04 | Metadata-level: campos restritos (comentário interno, auditoria) filtrados por papel na projeção de saída |
| SEC-BAC-05 | Testes de autorização automatizados por papel × endpoint × escopo na suíte de regressão |

---

# 15. Mensageria e Integrações Internas

| Código | Regra |
| ------ | ----- |
| SEC-MSG-01 | Consumers validam o envelope (campos obrigatórios, tipos, `version`) antes de processar (PR-001-05 §14.1) |
| SEC-MSG-02 | Consumers idempotentes por `(eventId, consumerName)`; nenhum efeito colateral em reentrega |
| SEC-MSG-03 | Payload sem segredos nem dados pessoais excessivos; referências por ID |
| SEC-MSG-04 | DLQ monitorada; reprocessamento apenas por operação autenticada e auditada |
| SEC-MSG-05 | Conexões com broker/cache/storage via service accounts mínimas e TLS (SEC-001 §18–19) |

---

# 16. Pipeline e Supply Chain

| Código | Regra |
| ------ | ----- |
| SEC-PIPE-01 | Gates bloqueantes: build, testes, SAST (analyzers + SonarQube), SCA, secret scanning, scan de imagem |
| SEC-PIPE-02 | Secret scanning (gitleaks/trufflehog) no diff e no histórico; vazamento = revogação imediata + post-mortem |
| SEC-PIPE-03 | Imagens Docker: base oficial assinada, non-root, read-only filesystem, sem shell quando possível, scan de CVEs |
| SEC-PIPE-04 | Lockfiles obrigatórios (NuGet `packages.lock.json`, npm `package-lock.json`); restore com integridade |
| SEC-PIPE-05 | Variáveis de CI com segredos mascarados; produção só via pipeline com aprovação; sem deploy manual |
| SEC-PIPE-06 | DAST (OWASP ZAP) em staging a cada release; relatório anexado ao gate |

---

# 17. OWASP ASVS 4.0 — Checklist de Verificação (nível 2)

Verificado a cada release (automatizado onde possível; manual com evidência onde não):

| Domínio ASVS | Cobertura neste padrão | Verificação |
| ------------ | ---------------------- | ----------- |
| V1 Architecture | SEC-001, SEC-002, ADRs | Revisão arquitetural por release |
| V2 Authentication | Seção 5 | Testes de auth + DAST |
| V3 Session Management | Seção 5 / SEC-001 §10 | Testes de sessão/revogação |
| V4 Access Control | Seções 13–14, PR-001-09 | Suíte de autorização |
| V5 Validation | Seção 6 | Testes de validação/fuzzing leve |
| V6 Stored Cryptography | SEC-001 §7 | Revisão de config + testes |
| V7 Error & Logging | Seções 8–9 | Testes de erro + inspeção de logs |
| V8 Data Protection | SEC-001 §7/§9, Seção 4 | Testes de headers/cache |
| V9 Communications | SEC-001 §7 | Scan TLS (testssl) |
| V10 Malicious Code | Seção 16, SEC-FE-10 | SCA + assinatura de build |
| V11 Business Logic | PR-001-02/03/09, Seção 14 | Testes de fluxo/SoD |
| V12 File & Resources | Seção 7 | Testes de upload |
| V13 API & Web Service | Seção 4 | DAST + contrato OpenAPI |
| V14 Configuration | Seção 16, SEC-001 §8.1 A05 | Scan de config/IaC |

Itens de nível 3 aplicáveis a **IAM e Auditoria**: verificação manual semestral com evidência registrada.

---

# 18. Definição de Pronto (segurança) para qualquer PR

1. Validação server-side de toda nova entrada (Seção 6) com testes.
2. Autorização avaliada e testada para todo novo endpoint (Seção 14).
3. Erros no envelope padrão; nada de internals (Seção 9).
4. Logs sem dados proibidos (Seção 8).
5. Sem segredos/introdução de dependência não aprovada (Seções 1, 16).
6. Testes de segurança relevantes ao caso (IDOR, XSS, mass assignment) incluídos.
7. Documentação de referência atualizada quando o PR altera contrato, permissão ou regra (governança GOV-002).

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquitetura Trino | Versão inicial: regras universais, frontend, backend .NET, API, JWT/refresh token, validação, upload/download, logs, exception handling, SQL, XSS, CSRF, SSRF, IDOR, mass assignment, broken access control, mensageria, pipeline/supply chain, checklist OWASP ASVS nível 2 e definição de pronto de segurança |
