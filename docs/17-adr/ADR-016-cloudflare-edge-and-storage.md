# ADR-016 — Cloudflare como camada de borda; R2 como storage; backend .NET em containers

**Status:** 🟢 Accepted
**Data:** 2026-08-08
**Criticidade:** 🔴 Core
**Origem:** Decisão de hospedagem/robustez do owner ("banco pesado, multiusuário, aprovações concorrentes; hospedar no Cloudflare; quero algo robusto").

---

## Contexto

O sistema terá **banco de dados pesado**, **muitos usuários lançando dados simultaneamente** e **aprovações de compra concorrentes** — um workload transacional que exige integridade forte. O owner indicou hospedagem no **Cloudflare** e prioridade em **robustez**.

Cloudflare é uma plataforma de **borda** (CDN/WAF/DDoS/TLS, Workers, Pages, R2, D1, Hyperdrive), **não** um host de banco relacional pesado:

- **D1** (SQLite serverless) é inadequado para este workload (escritor único, limites de tamanho) — não é o "banco pesado".
- **Workers** executam JavaScript/WASM — **não rodam .NET**.
- O PostgreSQL pesado precisa viver num **Postgres gerenciado**, com o Cloudflare **à frente**.

Havia duas topologias possíveis: (A) Cloudflare como **borda** de um backend .NET em containers; (B) **100% serverless** na borda, exigindo backend TypeScript (Workers) — o que reabriria a stack (ADR-015). O owner escolheu **robustez** → topologia A.

## Decisão

1. **Topologia A adotada (robustez):** mantém-se a arquitetura de ARC-001..006 e a stack de ADR-015 (.NET 9 / Next.js / DDD-Clean). O **backend .NET roda em containers** (Docker, ARC-003), **não** em Cloudflare Workers.

2. **Cloudflare é a camada de borda (perímetro):** CDN, **WAF**, **proteção DDoS**, **TLS 1.3** e rate-limiting de borda — materializa a **Camada 1 do Defense in Depth** (SEC-001 §3) e o ingress do ARC-003 §2/§6. O frontend Next.js pode ser servido por **Cloudflare Pages** (ou container atrás do CDN).

3. **Cloudflare R2 substitui o MinIO** como storage de objetos (S3-compatível). O Document Management (FD-001-03) abstrai o storage, então a troca é de implementação, sem impacto de domínio. Download por URL assinada ≤ 5 min mantido (SEC-001).

4. **PostgreSQL gerenciado é o "banco pesado"**, atrás do Cloudflare, num provedor gerenciado (ex.: Supabase Postgres, Neon, RDS, Cloud SQL). **Cloudflare Hyperdrive** pode ser usado para pool/aceleração de conexões da borda ao Postgres. **Cloudflare D1 não é usado** para dados transacionais.

5. **Mensageria/cache permanecem** RabbitMQ e Redis (ARC-005/ARC-006); **Cloudflare Queues** fica como alternativa avaliável por ADR futura, sem obrigatoriedade.

6. **Robustez é requisito de primeira classe:** as garantias de concorrência, contenção e escala ficam consolidadas em **ARC-006 §12** (novo) — optimistic concurrency, serialização por chave de saldo, reservas, particionamento, keyset, réplica de leitura, RLS.

## Consequências

- **Sem reescrita de arquitetura:** ARC-001..006 permanecem válidos; ARC-003 nomeia o Cloudflare como edge e R2 como storage na próxima revisão; SEC-001 §3 pode referenciar Cloudflare como provedor da Camada 1.
- **MinIO → R2:** a implementação do FD-001-03 usa um cliente S3-compatível apontando para R2; nenhuma mudança de contrato.
- **Ops:** exige operar/observar os containers .NET (o que dá o controle que a robustez pede); Cloudflare cuida de borda/segurança de perímetro e arquivos.
- **Reversibilidade:** migrar para topologia B (serverless/TS) permanece possível por ADR futura (supersedendo ADR-015/016) enquanto o projeto está pré-implementação — mas contraria a prioridade de robustez atual.

## Alternativas consideradas

- **Cloudflare D1 como banco:** rejeitada (SQLite inadequado ao workload).
- **Backend em Cloudflare Workers (TS):** rejeitada agora (priorizou-se robustez/domínio rico em .NET); reavaliável por ADR.
- **Manter MinIO:** válido, mas R2 reduz operação e integra à borda; adotado R2.

## Referências

- ADR-015 (stack .NET/Next.js), ADR-011 (Foundation antes das APIs)
- ARC-003 (Deployment), ARC-005 (eventos), ARC-006 (NFR/robustez §12), SEC-001 (perímetro/Defense in Depth)
- FD-001-03 (Document Management — storage abstraído)
