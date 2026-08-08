**Documento:** ARC-006 — Cross-Cutting Concerns & NFRs
**Versão:** 1.1.0
**Status:** Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: ISO/IEC 25010, 12-Factor App, OWASP ASVS, NIST SP 800-207.
> Documentos relacionados: ARC-001..005, SEC-001/002/003, FD-001-06/07/10, ADR-009/010/011/012/015/016.

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
| ADR-016 | Cloudflare como camada de borda; R2 como storage; backend .NET em containers | Accepted |

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

---

## 12. Concorrência, Contenção e Escala (robustez)

> Consolida, em um único ponto, as garantias que sustentam **banco pesado, muitos usuários lançando dados em paralelo e aprovações concorrentes** (ADR-016). Onde os módulos definem as regras, esta seção mostra o sistema de garantias que elas formam.

### 12.1 Escrita concorrente (dois usuários, mesmo registro)
- **Optimistic concurrency** em toda escrita: `version` + `If-Match`; conflito → `409` sem sobrescrever (IC-BR-042, IV-BR-012, MR-BR-091, RC-BR-030).
- Triggers de banco garantem incremento estrito de `version` (`TRG-*-002`), barrando retrocesso.

### 12.2 Contenção sobre o mesmo saldo (o ponto mais crítico)
- **Confirmação serializada por chave de saldo** (item×tamanho×local×segregação) — `IV-BR-012`, aplicada pelo `StockBalanceService` (MMS-004-04 §12; MMS-004-11 §10). Duas movimentações no mesmo saldo não se atropelam.
- **Reserva impede prometer o mesmo saldo duas vezes**: validação usa apenas o disponível = total − reservado (`IV-BR-005/020`; INV-IV-05/06).
- **Saldo é projeção, nunca editado direto** (ADR-009 / `IV-BR-001`); reconstruível a partir do razão de movimentos (rebuild — MMS-004-11 §11).
- **Transferência atômica** (saída+entrada na mesma transação — `IV-BR-030`).

### 12.3 Aprovações concorrentes
- **Workflow Engine** (FD-001-04) coordena a decisão; **SoD** inviolável (solicitante ≠ aprovador — `MR-BR-032`; registrante ≠ aprovador de ajuste — `IV-BR-041`).
- **Aprovação parcial por item** com trilha do decisor (`MR-BR-031`); toda decisão **auditada** (FD-001-06).

### 12.4 Confiabilidade sob carga
- **Transactional Outbox** (estado + evento na mesma transação) + RabbitMQ **at-least-once**, **DLQ** e **idempotência de consumidor** (ARC-005); **Idempotency-Key** nas escritas de efeito externo — sem efeito duplicado sob reenvio/pico.

### 12.5 Banco pesado — escala e leitura
- **Particionamento** com gatilhos definidos por tabela (ex.: `item` > 5M linhas/empresa; `stock_movement` por hash — MMS-002-11 §15.8; padrão replicável ao schema `materials`).
- **Keyset pagination** em 100% das listagens (sem `OFFSET`); índices **`company_id`-first** e parciais (`WHERE deleted_at IS NULL`).
- **Cache Redis** para leitura quente com invalidação por evento; **réplica de leitura** do PostgreSQL no roadmap de escala (ARC-003 §7).
- **Hyperdrive** (Cloudflare) para pool/aceleração de conexões da borda ao Postgres (ADR-016).

### 12.6 Isolamento multi-tenant sob carga
- Filtro `company_id` obrigatório em toda consulta + **RLS (Row Level Security)** no PostgreSQL como defesa em profundidade (ADR-015 §3); recurso fora do escopo → **404** (anti-enumeração — SEC-001 §6).

### 12.7 Borda (perímetro) — ADR-016
- **Cloudflare** provê CDN, **WAF**, **DDoS** e **TLS 1.3** (Camada 1 do Defense in Depth — SEC-001 §3), absorvendo picos e ataques antes de chegarem à aplicação; **R2** como storage de objetos (FD-001-03).

### 12.8 Metas de robustez (referência)
| Aspecto | Meta |
|---------|------|
| Consulta/validação de saldo | p95 < 2 s (IV-BR-110) |
| Conflito de escrita | Detectado (409), nunca sobrescrita silenciosa |
| Duplicação de efeito | Zero (idempotência + serialização) |
| Perda de evento de negócio | Zero (Outbox no Postgres) |
| Isolamento entre empresas | Total (company_id + RLS) |
| Recuperação | RPO ≤ 5 min, RTO ≤ 4 h (MMS-002-11 §15.13) |
