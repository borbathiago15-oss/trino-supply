**Documento:** ARC-000 — Architecture Overview (Índice)
**Versão:** 1.0.0
**Status:** Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: C4 Model (Simon Brown), Domain-Driven Design (Evans/Vernon), Clean Architecture (Martin), 12-Factor App, ISO/IEC 25010 (atributos de qualidade), Microsoft .NET Application Architecture Guides.
> Documentos relacionados: TPES-002 (Prompt Master), GOV-002 (Document Registry), FD-001 (Foundation), PR-001 (Procurement), MMS-001 (Materials Suite), SEC-001 (Arquitetura de Segurança), ADR-009/010/011/012.

# ARC-000 — Seção de Arquitetura

> Esta seção (`docs/04-architecture/`) é a **fonte única de verdade arquitetural** do Trino Supply. Nenhum modelo de banco (`06-database`), API (`07-api`), backend ou frontend pode divergir do que está aqui aprovado. Mudanças de arquitetura exigem **ADR** em `docs/17-adr/`.

---

## 1. Posição na metodologia TPE

A metodologia **Trino Product Engineering** (TPES-002) exige a ordem:

```text
Product Engineering → Business Engineering → ARQUITETURA → UX → Database → APIs → Backend → Frontend → Testes
```

A camada de **Arquitetura** consolida as decisões estruturais que já estavam distribuídas em documentos de negócio, ADRs e no pacote de segurança, formalizando-as antes que Database e API avancem para os demais módulos. Ela **não redefine** o domínio — ela declara como o domínio (03-business) é realizado tecnicamente.

---

## 2. Documentos da seção

| ID | Documento | Responde à pergunta | Caminho |
|----|-----------|---------------------|---------|
| ARC-001 | System Architecture Overview | Como o sistema se organiza em alto nível (C4 L1–L2)? | `01-system-architecture-overview.md` |
| ARC-002 | Bounded Contexts & Context Map | Quais são os domínios e como se relacionam? | `02-bounded-contexts-context-map.md` |
| ARC-003 | Solution & Deployment Architecture | Onde e como cada peça roda (infra/deployment)? | `03-solution-deployment-architecture.md` |
| ARC-004 | Application Architecture | Como cada serviço é estruturado por dentro (camadas)? | `04-application-architecture.md` |
| ARC-005 | Integration & Eventing Architecture | Como os contextos se comunicam (síncrono/assíncrono)? | `05-integration-eventing.md` |
| ARC-006 | Cross-Cutting Concerns & NFRs | Como garantimos os atributos de qualidade transversais? | `06-cross-cutting-nfr.md` |

Leia na ordem ARC-001 → ARC-006. ARC-001 é a âncora; os demais o aprofundam.

---

## 3. Princípios arquiteturais (resumo executivo)

Derivados do TPES-002 (§ Princípios do Produto) e das ADRs vigentes:

1. **Orientação ao domínio** — organização por Bounded Context, nunca por Controller/Service/Repository (TPES-002).
2. **Baixo acoplamento, alta coesão** — módulos independentes, integrados por contratos explícitos.
3. **Foundation primeiro** — capacidades compartilhadas vivem no Foundation (ADR-011); módulos consomem, não reimplementam.
4. **Domínio manda, banco implementa** — o banco é projeção do domínio (ADR-009).
5. **Eventos de negócio como linguagem oficial** — integração semântica, nunca técnica (ADR-010).
6. **Segurança e auditoria por padrão** — todo controle ligado por default (SEC-001).
7. **Configuração acima de customização** — comportamento parametrizável (FD-001-10), sem forks de código.
8. **Preparado para evolução, sem depender dela** — IA, ERP e mobile são roadmap; o MVP não depende deles (TPES-002).

---

## 4. Rastreabilidade

Cada documento ARC declara explicitamente sua rastreabilidade para os artefatos de negócio (FD-001, PR-001, MMS-*) e para as ADRs. A matriz consolidada está em **ARC-006 §9**.

---

## 5. Registro

Todos os documentos desta seção estão registrados em **GOV-002 — Document Registry**, seção "Arquitetura (04-architecture)". Nenhum é oficial fora do registry.
