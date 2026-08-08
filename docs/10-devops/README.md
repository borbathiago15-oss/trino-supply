**Documento:** OPS-001 — DevOps & CI/CD
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: 12-Factor App, GitHub Actions, Docker, DORA metrics.
> Documentos relacionados: ARC-003 (Deployment), ARC-006 (observabilidade/resiliência), SEC-003 (gates de segurança), QA-001 (testes), ADR-011/015.

# OPS-001 — DevOps & CI/CD

> Operacionaliza o contrato de CI/CD do ARC-003 §8: pipeline, ambientes, migrations, observabilidade e runbooks. Stack confirmada em ADR-015 (.NET 9/Next.js, Docker, GitHub Actions).

## 1. Pipeline (GitHub Actions)

```text
push/PR ─► build ─► testes (unit → integração → contrato) ─► análise (lint · SAST · secret-scan)
      ─► imagem versionada (imutável) ─► deploy staging ─► smoke/health · E2E
      ─► aprovação manual ─► deploy produção ─► verificação pós-deploy
```

- **Imagens imutáveis e versionadas** (SHA + semver); deploy é troca de imagem, nunca patch em execução (ARC-003 §2).
- **Gates obrigatórios**: testes (QA-001), SAST/secret-scan (SEC-003), diff OpenAPI limpo. Falha barra o merge/deploy.
- **Blue/green ou rolling** com health checks (readiness/liveness) antes de receber tráfego.

## 2. Ambientes

| Ambiente | Origem | Deploy | Dados |
|----------|--------|--------|-------|
| dev | branch de trabalho | Docker Compose local | seeds |
| CI | PR | contêineres efêmeros | efêmeros |
| staging | merge na main | automático | anonimizados |
| produção | tag de release | aprovação manual | reais (LGPD) |

Paridade dev/prod (12-Factor); diferenças só em configuração (FD-001-10).

## 3. Migrations (banco)
- EF Core Migrations por serviço/contexto; aplicadas pelo pipeline, **nunca manual em produção** (ARC-003 §5).
- **Expand-and-contract**: (1) estrutura nova compatível → (2) backfill → (3) remoção da antiga em release posterior; compatível com N-1 (deploy sem downtime).
- `CREATE INDEX CONCURRENTLY`; backfill em lotes; `Down()` testado; migração aplicada **antes** do release da aplicação dependente.

## 4. Configuração e Segredos
Config por ambiente (FD-001-10); segredos em cofre + `SecretRef`, nunca no repo/imagem/log (SEC-001 §12). Rotação de chaves JWT a cada 90 dias (SEC-001 §5).

## 5. Observabilidade (ARC-006 §4)
- **Logs** estruturados com `correlationId`/`companyId` (sem dados sensíveis), centralizados.
- **Métricas**: latência/erro por endpoint, profundidade de fila e taxa de DLQ (RabbitMQ), pool de conexões, cache hit.
- **Health checks** e dependências (Postgres/Redis/RabbitMQ/MinIO).
- **Alertas**: falhas de segurança, crescimento de DLQ, indisponibilidade, p95 acima da meta.
- **DORA**: lead time, frequência de deploy, MTTR, change failure rate.

## 6. Resiliência (ARC-006 §5)
Outbox at-least-once; idempotência; retry/backoff + circuit breaker; DLQ com reprocessamento; API/worker stateless para reinício/escala.

## 7. Runbooks (mínimos)
- Rollback de release (troca para imagem anterior; migrations reversíveis).
- Reprocessamento de DLQ.
- Rebuild da projeção de saldo (`stock_balance`) a partir do razão de movimentos (MMS-004-11 §11).
- Rotação de segredos/chaves.
- Restore de backup (RPO ≤ 5 min, RTO ≤ 4 h — MMS-002-11 §15.13).

## 8. Segurança no Pipeline (SEC-003)
SAST, dependency scanning, secret scanning e verificação de imagem (base mínima, usuário não-root) como gates; falha crítica barra o deploy.

## 9. Rastreabilidade
| Este documento | Origem |
|----------------|--------|
| Contrato de pipeline | ARC-003 §8 |
| Gates de teste | QA-001 |
| Gates de segurança | SEC-003 |
| Observabilidade/resiliência | ARC-006 §4/§5 |
| Stack | ADR-015 |

## 10. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | DevOps & CI/CD: pipeline GitHub Actions com gates (testes QA-001, SAST/secret-scan SEC-003, OpenAPI), ambientes com paridade, migrations expand-and-contract, configuração/segredos, observabilidade (logs/métricas/DORA), resiliência, runbooks (rollback, DLQ, rebuild de saldo, restore) e segurança no pipeline — operacionaliza ARC-003 §8. |
