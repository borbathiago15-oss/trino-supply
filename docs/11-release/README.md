**Documento:** REL-001 — Gestão de Releases
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply

> Documentos relacionados: OPS-001 (10-devops), QA-001 (09-testing), ARC-003, FD-001-10, SEC-003.

# REL-001 — Gestão de Releases

## 1. Versionamento (SemVer)
`MAJOR.MINOR.PATCH`: MAJOR = quebra de contrato (API `v2`); MINOR = capacidade nova compatível; PATCH = correção. Contratos de API só quebram em nova major (ARC-006 §versionamento; `*-13`). Eventos versionados por evento (ADR-010).

## 2. Estratégia de Release
- **Trunk-based** com feature branches curtas; merge na `main` dispara o pipeline (OPS-001).
- **Feature flags** (FD-001-10) para desacoplar deploy de release: código em produção desligado até a ativação.
- **Blue/green ou rolling** com health checks antes de receber tráfego.

## 3. Rollout e Rollback
- Rollout gradual quando aplicável; monitoramento de métricas/erros pós-deploy (ARC-006 §4).
- **Rollback**: troca para a imagem anterior (imutável) + migrations reversíveis (`Down()` testado — OPS-001 §3). Dados irreversíveis exigem aprovação explícita.

## 4. Release Notes e Changelog
- **CHANGELOG** por versão (Keep a Changelog): Added/Changed/Fixed/Security.
- **Release notes** por onda (PRD-001), com IDs de documentos e ADRs impactados.
- Toda release referencia os itens do GOV-002 que a compõem.

## 5. Definition of Done (release)
- [ ] Todos os gates verdes (testes QA-001, SAST/secret-scan SEC-003, OpenAPI).
- [ ] Migrations expand-and-contract compatíveis com N-1.
- [ ] Feature flags configuradas; plano de rollback pronto.
- [ ] Observabilidade e alertas ativos (ARC-006 §4).
- [ ] Documentação/registry atualizados.

## 6. Cadência e Ambientes
`dev → staging (homologação) → produção (aprovação manual)` (OPS-001 §2). Releases de produção com tag SemVer; hotfix segue fluxo acelerado com o mesmo conjunto de gates.

## 7. Conformidade
Retenção e trilha de release auditáveis (FD-001-06); dados reais em produção sob LGPD (SEC-001, ARC-006 §8).

## 8. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Gestão de Releases: SemVer e política de contratos, estratégia (trunk-based, feature flags, blue/green), rollout/rollback, release notes/changelog, DoD de release, cadência de ambientes e conformidade — operacionaliza OPS-001/QA-001 no ciclo de entrega. |
