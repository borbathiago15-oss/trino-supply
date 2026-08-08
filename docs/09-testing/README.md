**Documento:** QA-001 — Estratégia de Testes Corporativa
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: Testing Pyramid, ISO/IEC 25010, xUnit/Playwright/k6, Testcontainers.
> Documentos relacionados: ARC-003 (CI/CD), ARC-004 (camadas), ARC-005 (eventos), SEC-003, `*-17-test-scenarios` de cada módulo.

# QA-001 — Estratégia de Testes

> Consolida a estratégia corporativa de testes, unificando os `*-17-test-scenarios` dos módulos (PR-001, MMS-002..005) sob um padrão único, alinhado à arquitetura (ARC-004) e ao pipeline (ARC-003).

## 1. Pirâmide de Testes

```text
        ╱ E2E ╲            poucos, críticos (Playwright)
      ╱ Contrato ╲         API/OpenAPI + eventos (contrato entre contextos)
    ╱ Integração  ╲        repositórios, outbox, Testcontainers (PostgreSQL/RabbitMQ/Redis)
  ╱   Unit (base)   ╲      domínio: invariantes INV-*, VOs, factories, policies
```

Base larga em **unit** (domínio rico — Clean Arch/ARC-004); topo estreito em **E2E**.

## 2. Camadas e Metas

| Camada | Escopo | Ferramenta | Meta |
|--------|--------|-----------|------|
| Unit | Aggregates, invariantes (INV-IC/IV/MR/RC), VOs, specifications, policies | xUnit (.NET) | Cobertura de regras 100% (DoD); rápido (< 5 min) |
| Integração | Repositórios EF Core, Outbox, projeção de saldo, serialização por chave | Testcontainers (PostgreSQL, RabbitMQ, Redis) | Sem mocks de infra crítica |
| Contrato/API | Endpoints REST vs. OpenAPI 3.1; catálogos de erro (IC/IV/MR/RC-ERR) | Testes de contrato + Pact-like para eventos | Diff OpenAPI limpo |
| E2E | Fluxos por papel ponta a ponta | Playwright | Cenários P0 dos `*-17` |
| Segurança | AuthZ (escopo→RBAC→ABAC→delegação), SoD, anti-enumeração (404), saldo protegido | Suíte dedicada (SEC-003) | 100% dos P0 de segurança |
| Performance | Saldo < 2 s (IV-BR-110), listagens keyset, filas | k6 | Metas NFR por módulo |

## 3. Princípios Corporativos

1. **Rastreabilidade 100%**: todo `*-BR` e todo `AC-*` tem ≥ 1 cenário `TC-*` (verificado no DoD do módulo).
2. **Prova de invariantes estruturais**: testes que provam "saldo nunca é escrito diretamente" (IV-BR-001), "conclusão gera entrada única" (INV-RC-04), "aprovador ≠ registrante/solicitante" (SoD).
3. **Idempotência**: todo endpoint de escrita com efeito externo tem teste de replay (Idempotency-Key).
4. **Eventos**: contrato de evento testado (envelope ARC-005 §8), at-least-once + idempotência de consumidor, DLQ.
5. **Multi-tenant**: todo teste de leitura/escrita valida isolamento por `company_id` (404 anti-enumeração).
6. **Determinismo**: sem dependência de relógio/ordem; dados por fixture/seed idempotente.

## 4. Ambientes e Dados
- CI: contêineres efêmeros por job (ARC-003 §3); banco por Testcontainers.
- Dados: seeds idempotentes de referência (`materials.*`, `procurement.*`); dados sintéticos para volume.
- Staging: réplica anonimizada para E2E/performance de pré-produção.

## 5. Gates no Pipeline (ARC-003 §8)
`build → unit → integração → contrato → análise (lint/SAST/secret-scan) → E2E (staging) → performance (agendado)`. Falha em qualquer gate obrigatório barra o merge. Cobertura de regras e AC é gate de DoD.

## 6. Definition of Done (teste) por módulo
- [ ] 100% das regras `*-BR` com cenário `TC-*`.
- [ ] 100% dos `AC-*` P0 automatizados.
- [ ] Invariantes estruturais provadas (saldo protegido, SoD, idempotência).
- [ ] Contrato OpenAPI e de eventos verdes.
- [ ] Segurança (AuthZ/SoD/tenant) e performance (NFR) no verde.

## 7. Rastreabilidade
Os catálogos por módulo — PR-001-17, MMS-002-17, MMS-003-17, MMS-004-17, MMS-005-17 — são as instâncias desta estratégia; este documento é o padrão que todos seguem.

## 8. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Estratégia de testes corporativa: pirâmide, camadas e metas (unit/integração/contrato/E2E/segurança/performance), princípios (rastreabilidade 100%, prova de invariantes, idempotência, eventos, multi-tenant, determinismo), ambientes/dados, gates no pipeline (ARC-003) e DoD de teste — unifica os `*-17-test-scenarios` dos módulos. |
