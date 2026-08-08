**Documento:** ARC-006 — Cross-Cutting Concerns & NFRs
**Versão:** 1.0.0
**Status:** Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: ISO/IEC 25010, 12-Factor App, OWASP ASVS, NIST SP 800-207.
> Documentos relacionados: ARC-001..005, SEC-001/002/003, FD-001-06/07/10, ADR-009/010/011/012.

# ARC-006 — Cross-Cutting Concerns & NFRs

> Consolida as preocupações **transversais** a todos os contextos e os **requisitos não-funcionais (NFR)** com metas. Onde ARC-001..005 descrevem estrutura, este documento descreve as qualidades que a estrutura precisa garantir e como são medidas. Fecha a seção de arquitetura e traz o índice de rastreabilidade e ADRs.

---

## 1. Multi-tenancy

| Aspecto | Decisão |
|---------|---------|
| Modelo | Multi-tenant **lógico**; isolamento por `company_id` em toda tabela, índice e consulta (ADR-009). |
| Fronteira | Nenhum dado cruza tenant; recurso fora do escopo → 404 (SEC-001 §6, anti-IDOR). |
| Escopo organizacional | Além do tenant, escopo por unidade/filial (FD-001-02) refina a visibilidade. |
| Cache | Chaves de cache incluem `company_id`; nunca vazamento cross-tenant no Redis. |

---

## 2. Segurança (transversal)

Autoridade normativa: **SEC-001/002/003**. Contrato transversal resumido:

- AuthN por JWT curto (≤ 15 min) + refresh rotativo; chaves rotacionadas a cada 90 dias.
- AuthZ **server-side em 100% das requisições**: RBAC + ABAC + Escopo, deny by default, SoD.
- TLS 1.3 na borda; segredos em cofre; senhas BCrypt/Argon2id.
- Validação de entrada, output encoding, tratamento seguro de exceção (SEC-003).
- **Auditoria obrigatória** de toda operação crítica e decisão de segurança.

---

## 3. Auditoria e rastreabilidade

| Preocupação | Realização |
|-------------|------------|
| Trilha imutável | Audit Service append-only particionado, cadeia de hash, retenção 5 anos (FD-001-06). |
| Origem semântica | Business Domain Events (ADR-010) alimentam a auditoria. |
| Linha do tempo | Timeline Service reconstruível por entidade (FD-001-07). |
| Rastreamento fim-a-fim | `correlationId`/`causationId` no envelope de evento (ARC-005 §8). |

---

## 4. Observabilidade

| Pilar | Padrão |
|-------|--------|
| **Logs** | Estruturados (JSON), com `correlationId`, `companyId`, `userId`; **sem dados sensíveis** (SEC-001). Centralizados. |
| **Métricas** | Latência, throughput, erros por endpoint; profundidade de fila e taxa de DLQ (RabbitMQ); pool de conexões. |
| **Tracing** | Correlação por `correlationId` propagado entre API, workers e eventos (roadmap: tracing distribuído). |
| **Health checks** | Liveness/readiness na API e workers; checagem de dependências (Postgres/Redis/RabbitMQ/MinIO) para orquestração (ARC-003). |
| **Alertas** | Sobre falhas de segurança, crescimento de DLQ, indisponibilidade de dependência. |

---

## 5. Resiliência e disponibilidade

| Preocupação | Padrão |
|-------------|--------|
| Consistência estado↔evento | Transactional Outbox (ARC-005 §3). |
| Duplicatas | Idempotência de consumidores e comandos (ARC-005 §6). |
| Falha de dependência | Timeouts, retry com backoff, circuit breaker onde couber; **fail securely**. |
| Mensagens problemáticas | DLQ + reprocessamento controlado (ARC-005 §5). |
| Degradação graciosa | Leitura servida por cache quando a origem oscila; escrita nunca "finge" sucesso. |
| Sem estado na API | Réplicas stateless permitem reinício/escala sem perda (ARC-003). |

---

## 6. Desempenho e escalabilidade

| NFR | Meta de referência | Como |
|-----|--------------------|------|
| Autenticação | < 2 s | FD-001-01 / SEC-001 |
| Leitura paginada | Estável sob volume | **Keyset pagination**, índices `company_id`-first (`*-11-database`) |
| Cache de leitura | Hit em dados quentes | Redis com invalidação por evento |
| Escala horizontal | API/worker stateless | ARC-003 §7 |
| Escala de eventos | Workers por fila/contexto | ARC-003 §7, ARC-005 |

> Metas quantitativas por módulo (p95/p99 por endpoint) vivem nos respectivos `*-16-acceptance-criteria`/NFR e `*-17-test-scenarios` (camada de performance). Este documento fixa o **padrão**; os módulos fixam os **números**.

---

## 7. Configurabilidade

- Comportamento parametrizável por **Configuration Service** tipado, com escopos e precedência, feature flags e `ConfigSnapshot` (FD-001-10).
- **Configuração acima de customização** (TPES-002): variação de cliente é config, não fork de código.
- Namespaces já usados: `materials.item.*`, `materials.inventory.*`, `procurement.*` — padrão mantido para novos módulos.

---

## 8. Privacidade e conformidade (LGPD)

| Preocupação | Realização |
|-------------|------------|
| Dados pessoais | Minimização em logs/eventos; referências por ID (SEC-001). |
| Retenção | Políticas por classe de dado; auditoria 5 anos (FD-001-06). |
| Pseudonimização | Mecanismo compatível com cadeia de hash — **decisão aberta** no GOV-002 §10 (exige ADR antes da v2 do Audit). |
| Soft delete + versionamento | Exigidos por TPES-002 (Segurança). |

---

## 9. Matriz de rastreabilidade da seção de arquitetura

| Preocupação transversal | ARC | Autoridade normativa |
|-------------------------|-----|----------------------|
| Estilo/contêineres | ARC-001 | TPES-002 |
| Contextos/fronteiras | ARC-002 | TPES-002, ADR-011/012 |
| Deployment/infra | ARC-003 | TPES-002, SEC-001 |
| Camadas/domínio | ARC-004 | `*-04-domain-model`, ADR-009 |
| Eventos/integração | ARC-005 | ADR-010, `*-05-event-storming` |
| Multi-tenancy | ARC-006 §1 | ADR-009 |
| Segurança | ARC-006 §2 | SEC-001/002/003 |
| Auditoria/Timeline | ARC-006 §3 | FD-001-06/07, ADR-010 |
| Observabilidade | ARC-006 §4 | SEC-001, 12-Factor |
| Resiliência | ARC-006 §5 | ARC-005 |
| Performance | ARC-006 §6 | `*-11/16/17` |
| Configurabilidade | ARC-006 §7 | FD-001-10 |
| LGPD | ARC-006 §8 | SEC-001, FD-001-06 |

---

## 10. Índice de ADRs vigentes (arquitetura)

| ADR | Decisão | Status |
|-----|---------|--------|
| ADR-009 | Banco de dados como projeção do domínio | Accepted |
| ADR-010 | Eventos de negócio como linguagem oficial | Accepted |
| ADR-011 | Foundation antes das APIs | Accepted |
| ADR-012 | Posicionamento da Materials Management Suite no roadmap | Accepted |
| ADR-013 | Conversão de Unidade de Medida (compra × estoque) no Item Catalog | Accepted |
| ADR-014 | Motor de Regras de Reposição (configurável) | Accepted |
| ADR-015 | Confirmação da stack (.NET/Next.js) e avaliação da alternativa Supabase | Accepted |

**Decisões que exigirão ADR futura** (registradas em GOV-002 §10 e sinalizadas nesta seção): extração de um Bounded Context para serviço independente (ARC-003 §7); bancos de leitura dedicados / CQRS com store separado (ARC-004 §5); cada integração externa via ACL (ARC-005 §10); mecanismo de pseudonimização LGPD (ARC-006 §8).

---

## 11. Rastreabilidade

| Este documento | Deriva de / realiza |
|----------------|---------------------|
| NFRs transversais | ISO/IEC 25010, TPES-002 (Princípios) |
| Segurança/LGPD | SEC-001/002/003, FD-001-06 |
| Observabilidade/resiliência | 12-Factor, ARC-003/005 |
| Configurabilidade | FD-001-10 |
| Índice de ADRs | 17-adr, GOV-002 |
