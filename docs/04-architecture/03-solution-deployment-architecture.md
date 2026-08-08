**Documento:** ARC-003 — Solution & Deployment Architecture
**Versão:** 1.0.0
**Status:** Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: 12-Factor App, C4 Model (Deployment), Docker best practices, OWASP ASVS (infra), NIST SP 800-207 (Zero Trust).
> Documentos relacionados: TPES-002 (Stack/Infra), ARC-001, ARC-005, SEC-001, FD-001-03/10.

# ARC-003 — Solution & Deployment Architecture

> Define **onde e como** cada peça do Trino Supply executa: topologia de contêineres, ambientes, rede, dados, escalabilidade e o caminho de evolução para serviços independentes. Complementa a visão lógica do ARC-001 com a visão física.

---

## 1. Princípios de deployment

1. **12-Factor** — configuração por ambiente (variáveis/Secrets), processos stateless, paridade dev/prod.
2. **Imutabilidade** — imagens versionadas; não se altera contêiner em execução, redeploy sempre.
3. **Stateless na API** — nenhum estado de sessão na aplicação; estado vai para Postgres/Redis (permite escala horizontal).
4. **Infra como código** — Docker Compose (dev) e manifests versionados; nenhuma configuração manual não versionada.
5. **Segurança por padrão** — TLS 1.3 na borda, mTLS/rede segmentada interna, segredos em cofre (SEC-001 §12/§4).

---

## 2. Topologia física (C4 — Deployment)

```text
                       Internet
                          │  HTTPS (TLS 1.3)
                 ┌────────▼─────────┐
                 │  Reverse Proxy / │  WAF, rate-limit de borda, TLS termination
                 │  Ingress (Nginx) │
                 └───┬──────────┬───┘
        estático/SSR │          │ /api/*
             ┌───────▼──┐   ┌───▼──────────────┐
             │ Web App  │   │  API (.NET 9)    │  N réplicas stateless
             │ Next.js  │   │  Modular Monolith│  (escala horizontal)
             │ (Node)   │   └───┬───┬────┬───┬─┘
             └──────────┘       │   │    │   │
       ┌──────────────┬─────────┘   │    │   └──────────────┐
 ┌─────▼──────┐ ┌─────▼─────┐ ┌─────▼───┐ ┌────▼─────┐ ┌────▼────────┐
 │ PostgreSQL │ │  Redis    │ │RabbitMQ │ │  MinIO   │ │  Worker(s)  │
 │ primary +  │ │  (cache/  │ │(broker) │ │ (objetos)│ │ consumidores│
 │ (replica*) │ │  sessão)  │ │         │ │          │ │ de eventos  │
 └────────────┘ └───────────┘ └─────────┘ └──────────┘ └─────────────┘
      * replica de leitura = roadmap de escala

  Observabilidade transversal: logs estruturados → coletor central; métricas; health checks.
```

| Nó | Imagem/Runtime | Estado | Escala |
|----|----------------|--------|--------|
| Ingress/Proxy | Nginx (ou equivalente) | Stateless | Horizontal |
| Web App | Node (Next.js) | Stateless | Horizontal |
| API | .NET 9 / ASP.NET Core | **Stateless** | Horizontal |
| Worker(s) | .NET 9 (consumidores RabbitMQ) | Stateless | Horizontal por fila |
| PostgreSQL | PostgreSQL | **Stateful** (SoT) | Vertical + réplica de leitura (roadmap) |
| Redis | Redis | Efêmero (cache) | Cluster (roadmap) |
| RabbitMQ | RabbitMQ | Durável (mensagens) | Cluster (roadmap) |
| MinIO | MinIO | **Stateful** (objetos) | Distribuído (roadmap) |

> A separação **API (síncrona)** × **Worker (assíncrona)** permite escalar processamento de eventos independentemente das requisições HTTP. No MVP ambos podem compartilhar imagem e serem ligados por configuração; o desenho já os trata como processos distintos (12-Factor).

---

## 3. Ambientes

| Ambiente | Propósito | Infra | Dados |
|----------|-----------|-------|-------|
| **dev (local)** | Desenvolvimento | Docker Compose (todos os serviços) | Sintéticos / seeds |
| **CI** | Build + testes automatizados | GitHub Actions (contêineres efêmeros) | Efêmeros por job |
| **staging** | Homologação / pré-produção | Paridade com produção | Anonimizados |
| **produção** | Operação | Orquestração de contêineres | Reais (LGPD) |

Paridade dev/prod é requisito (12-Factor). Toda diferença entre ambientes vive em **configuração** (FD-001-10), nunca em código.

---

## 4. Configuração e segredos

| Item | Mecanismo | Referência |
|------|-----------|------------|
| Parâmetros de aplicação | Configuration Service tipado, com escopos e precedência | FD-001-10 |
| Feature flags | FD-001-10 (feature flags) | FD-001-10 |
| Segredos (conexões, chaves JWT) | Cofre de segredos + `SecretRef`; nunca em código/log | FD-001-10, SEC-001 §12 |
| Rotação de chaves JWT | 90 dias com overlap (`kid`) | SEC-001 §5 |

Nenhum segredo em repositório, imagem ou variável de ambiente em claro em logs.

---

## 5. Dados e persistência

- **PostgreSQL** é a fonte de verdade transacional. Um **schema por Bounded Context** (ARC-002 §5). Isolamento multi-tenant por `company_id` em toda tabela e índice `company_id`-first (ADR-009, `*-11-database`).
- **Migrations** versionadas e auditáveis são obrigatórias; schema evolui só por migration (ADR-009).
- **Redis**: cache de leitura, cache de decisão de autorização (TTL ≤ 60 s), rate limiting, sessão/refresh hints. Nunca fonte de verdade.
- **MinIO**: anexos/documentos via Foundation Document Management (FD-001-03); a aplicação guarda apenas referências/metadados no Postgres.
- **RabbitMQ**: mensagens duráveis; o estado de negócio nunca depende de mensagem — o Outbox no Postgres é a origem (ARC-005).

### 5.1 Backup e retenção

| Dado | Estratégia | Retenção |
|------|-----------|----------|
| PostgreSQL | Backup full + WAL (PITR) | Conforme política; auditoria 5 anos (FD-001-06) |
| MinIO | Versionamento de objetos + backup | Conforme classificação (FD-001-03) |
| Trilha de auditoria | Append-only particionado | 5 anos (SEC-001, FD-001-06) |

---

## 6. Rede e segurança de infraestrutura

Alinhado ao Defense in Depth do SEC-001 (§3) e Zero Trust (§4):

- **Borda**: TLS 1.3 obrigatório, WAF, rate limiting, proteção DDoS.
- **Interna**: rede segmentada; API↔dependências em rede privada; **nenhuma rede é "confiável"** por padrão.
- **Serviço-a-serviço**: autenticação mesmo interna; mTLS interno como alvo (SEC-001 §4).
- **Hardening**: contêineres com usuário não-root, imagens mínimas, superfície reduzida, sem portas de administração expostas.
- **Blast radius**: isolamento por tenant e por contexto limita o impacto de uma violação.

---

## 7. Escalabilidade e evolução

| Estágio | Topologia | Gatilho |
|---------|-----------|---------|
| MVP | Monólito modular + workers; réplicas stateless da API | Lançamento |
| Escala de leitura | Réplica de leitura Postgres; Redis cluster | Volume de consulta |
| Escala de eventos | Workers por fila/contexto; RabbitMQ cluster | Volume de eventos |
| Extração de serviço | Um Bounded Context vira serviço próprio (mesmo domínio, novo processo) | Necessidade de escala/deploy independente — **exige ADR** |

Como a costura entre contextos já é por **evento/contrato** (ARC-002, ARC-005), extrair um contexto para serviço independente **não reescreve o domínio** — muda apenas topologia e transporte. É a razão de o monólito modular ser um ponto de partida seguro (ARC-001 §2).

---

## 8. CI/CD (visão de arquitetura)

Pipeline em **GitHub Actions** (TPES-002). O detalhamento operacional pertence a `docs/10-devops`; aqui fica o contrato arquitetural:

```text
push/PR → build → testes (unit/integração) → análise (lint/SAST/secret-scan) →
          → imagem versionada → deploy staging → smoke/health → aprovação → deploy produção
```

- Imagens **imutáveis e versionadas**; deploy é troca de imagem, não patch em execução.
- Migrations aplicadas de forma controlada e reversível antes do release da aplicação dependente.
- Gates de segurança (SAST, secret scanning) integram o pipeline (SEC-003).

---

## 9. Rastreabilidade

| Este documento | Deriva de / realiza |
|----------------|---------------------|
| Stack e infra (Docker, GH Actions, Postgres/Redis/RabbitMQ/MinIO) | TPES-002 |
| Persistência/multi-tenant | ADR-009, `*-11-database` |
| Anexos em MinIO | FD-001-03 |
| Configuração/segredos | FD-001-10, SEC-001 |
| Rede/hardening/Zero Trust | SEC-001 §3/§4/§12 |
| Extração de contexto | ARC-002, ARC-005 |
