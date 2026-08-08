# ADR-015 — Confirmação da stack (.NET 9 / Next.js) e avaliação da alternativa Supabase

**Status:** 🟢 Accepted
**Data:** 2026-08-08
**Criticidade:** 🔴 Core
**Origem:** Proposta de arquitetura trazida pelo owner (Supabase + React + Expo), oriunda de exploração com outras LLMs, submetida à análise.

---

## Contexto

O owner apresentou uma arquitetura alternativa para o Trino Supply, sem camada de IA (o que já é coerente — o MVP nunca previu IA, TPES-002), baseada em:

- **Frontend:** React + TypeScript (web) e **Expo / React Native (mobile)**; Zod, TanStack Query, Zustand.
- **Backend/Plataforma:** **Supabase** — PostgreSQL, Auth, Storage, Realtime, Edge Functions.

Essa proposta é pragmática e rápida para MVP, mas **contradiz a stack mandatória vigente** (TPES-002: .NET 9 / ASP.NET Core + Next.js; PostgreSQL/Redis/RabbitMQ/MinIO; Docker) e o estilo arquitetural documentado (DDD + Clean Architecture; ARC-001..007; ADR-009/010/011). Uma mudança dessa magnitude exige decisão registrada (regra do GOV-002: "mudanças de arquitetura exigem ADR"). O owner delegou a decisão à arquitetura ("sem preferência").

## Decisão

1. **A stack e o estilo arquitetural permanecem os de TPES-002 e ARC-001..007:** monólito modular orientado a domínio, **.NET 9 / ASP.NET Core** no backend, **Next.js/React/TypeScript** no frontend, **PostgreSQL + Redis + RabbitMQ + MinIO**, **Docker/GitHub Actions**, DDD + Clean Architecture. A camada de IA permanece **fora** do MVP.

2. **A proposta Supabase é registrada como alternativa avaliada e não adotada como plataforma base**, pelos motivos:
   - O núcleo do produto (integridade de estoque, aprovação, auditoria) recompensa um **domínio rico** (invariantes INV-IC/INV-IV/INV-MR); num BaaS a lógica se dispersa entre RLS, triggers e Edge Functions.
   - Coerência com o corpo documental Approved (Single Source of Truth) e menor retrabalho.
   - Garantias de evento/transação (Outbox, DLQ, at-least-once, ordenação — ADR-010/ARC-005) mais limpas no desenho atual.
   - Portabilidade (Docker) vs. acoplamento de plataforma.

3. **Boas ideias da proposta são adotadas como técnicas de implementação dentro da arquitetura atual:**
   - **RLS (Row Level Security) no PostgreSQL** como camada adicional de isolamento multi-tenant (defense-in-depth), **complementar** ao filtro `company_id` na aplicação — reforça ARC-006 §1 e SEC-001. Não substitui a autorização server-side.
   - **Atualização em tempo real** (fila do almoxarifado, aprovações) via **SignalR/WebSockets** no ASP.NET Core — cobrindo o caso de uso do "Realtime" sem depender da plataforma Supabase.
   - O **mapa funcional** dos diagramas do owner é incorporado como visão de produto, mapeado aos Bounded Contexts oficiais (ARC-002).

4. **Mobile permanece pós-MVP (web-first):** o app mobile (Expo/React Native) fica no roadmap, com a arquitetura preparada para ele (APIs REST versionadas, contratos estáveis), conforme TPES-002. Sua entrada no escopo exigirá revisão de escopo própria.

## Consequências

- Nenhuma alteração nos documentos de negócio/domínio (FD-001, PR-001, MMS-*) — eles são independentes de stack e permanecem válidos.
- ARC-006 §10 passa a listar ADR-015; SEC-001 e ARC-006 §1 podem referenciar RLS como controle complementar de tenancy na próxima revisão.
- Caso, no futuro, a prioridade de time-to-market supere os critérios acima, esta decisão pode ser **revista por nova ADR** que superseda TPES-002 e ARC-001..007 — o momento de menor custo para tal pivô é enquanto o projeto está pré-implementação.
- A exploração do owner fica preservada e rastreável nesta ADR.

## Alternativas consideradas

- **Adotar Supabase como plataforma base:** rejeitada agora (motivos §2); reavaliável por ADR futura.
- **Arquitetura híbrida (Supabase + serviços .NET):** rejeitada por adicionar complexidade de duas plataformas sem ganho claro no MVP.

## Referências

- TPES-002 — Prompt Master (stack mandatória)
- ARC-001..007 — Seção de Arquitetura
- ADR-009 (banco como projeção), ADR-010 (eventos), ADR-011 (Foundation antes das APIs)
- SEC-001 — Arquitetura de Segurança (multi-tenancy, Zero Trust)
