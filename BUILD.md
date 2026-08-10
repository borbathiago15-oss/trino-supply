# Trino Supply — Guia de Build (esqueleto)

> Implementa o **GO-001 — Build Kickoff** (item 0, "esteira"). A documentação em `docs/` é a Single Source of Truth; este código a realiza (ARC-004, ADR-009).

## Pré-requisitos
- **.NET SDK 9.0+**
- **Node 22+** (frontend)
- **Docker** (infra local)

## Estrutura (por Bounded Context — ARC-002/ARC-004)
```
src/
  BuildingBlocks/            kernel técnico (Result, CompanyId, Outbox…)
  Foundation/{Domain,Application,Infrastructure}
host/
  Api/                       Modular Monolith (.NET 9) — composição, endpoints
  Worker/                    consumidores de eventos (Outbox → RabbitMQ)
web/                         frontend Next.js/React/TS
deploy/docker-compose.yml    postgres, redis, rabbitmq, minio(=R2 dev)
.github/workflows/ci.yml     pipeline (OPS-001)
```

## Subir a infra local
```bash
docker compose -f deploy/docker-compose.yml up -d
```

## Build & run (backend)
```bash
dotnet restore TrinoSupply.slnx
dotnet build   TrinoSupply.slnx -c Release
dotnet run --project host/Api        # health: http://localhost:5xxx/health e /api/v1/health
dotnet run --project host/Worker     # OutboxRelayWorker (heartbeat)
```
> Se a sua versão do `dotnet` não abrir `.slnx`, use os projetos diretamente
> (`dotnet build host/Api`) ou gere uma solution clássica com `dotnet new sln` + `dotnet sln add`.

## Frontend
```bash
cd web && npm install && npm run dev
```

## Migração de banco (dev) — EF Core é o SSOT do schema
```bash
docker compose -f deploy/docker-compose.yml up -d postgres

# Aplica as migrations (rode com role privilegiada; NÃO com a role da aplicação).
dotnet tool install --global dotnet-ef --version 9.0.0   # uma vez
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=trino;Username=trino;Password=trino"
dotnet ef database update \
  --project src/Foundation/Infrastructure --startup-project src/Foundation/Infrastructure

# Nova migration após mudar o modelo:
# dotnet ef migrations add <Nome> --project src/Foundation/Infrastructure \
#   --startup-project src/Foundation/Infrastructure --output-dir Persistence/Migrations
```
> A migration `InitialFoundation` cria `company`/`outbox`/`role`/`app_user`, os índices e — via
> `migrationBuilder.Sql` — a função `current_company()` e as **policies RLS** (SEC-004), que o EF
> não gera do modelo. Os arquivos em `deploy/db/` (`001`/`002`) são **referência** (caminho sem EF)
> e `rls.sql` documenta o hardening da role de aplicação (que roda fora das migrations).

## Estado atual (esqueleto)
- ✅ Estrutura da solution por contexto; `BuildingBlocks` (Result, Entity/AggregateRoot,
  eventos de domínio, Outbox, CompanyId/ITenantContext, IClock).
- ✅ `Foundation`: agregado `Company` (tenant) + `FoundationDbContext` (EF Core) que grava
  eventos de domínio no **Outbox** na mesma transação (ARC-005 §3).
- ✅ **Migrations EF Core (SSOT do schema):** `InitialFoundation` + `AuditTrail` criam
  `company`/`outbox`/`role`/`app_user`/`audit_entry` + índices + função `current_company()` +
  policies **RLS** (via `migrationBuilder.Sql`). Aplicadas e validadas com `dotnet ef database update`
  (SEC-004). SQL em `deploy/db/` é referência.
- ✅ **Auditoria imutável (SEC-001/SEC-002):** `IAuditLog` grava "quem fez o quê" na MESMA transação
  da ação (atomicidade); trilha **append-only** reforçada por **trigger** (bloqueia UPDATE/DELETE,
  inclusive superuser) + grants sem update/delete + RLS por tenant. Endpoint `GET /api/v1/audit`
  (`audit.read`). Comprovado: 6 ações registradas, adulteração negada nos dois níveis.
- ✅ **Enforcement do RLS (SEC-004):** `TenantConnectionInterceptor` define `app.current_company`
  por conexão, **fail-closed** (sem tenant → nega tudo). A `outbox`/`company` não usam RLS (infra/
  catálogo — isolamento por role).
- ✅ `host/Api`: health check + **AuthN JWT fail-closed** (validação estrita; recusa iniciar em
  produção sem AuthN) + `HttpTenantContext` (tenant do token) + endpoint protegido `/api/v1/whoami`.
- ✅ **IdP local (SEC-001) — Fase 1, fatia 5 (validada):** o próprio Trino **emite** tokens. Login
  por e-mail/senha (hash **PBKDF2-HMAC-SHA256**), access token curto (15 min) + **refresh token com
  rotação** (guarda só o hash; reuso do antigo é negado), logout revoga. **Key-ring** com rotação de
  chave de assinatura (ativa assina, todas validam por `kid`) — token de chave antiga ainda valida na
  janela; chave fora do ring → 401. Endpoints `/api/v1/auth/{login,refresh,logout}`.
- ✅ **`host/Worker` — relay do Outbox real (Fase 4, ARC-005):** a cada ciclo drena as mensagens
  pendentes e as entrega ao `IEventPublisher` (inicial = log; troca por RabbitMQ sem mudar o relay).
  At-least-once, ordenado por ocorrência, retry/backoff e **dead-letter** após N tentativas.
  Validado por integração (Testcontainers): provisionar gera eventos no Outbox → relay publica e
  marca; falha do publisher incrementa retry sem marcar. No piloto (`worker` no compose).
- ✅ **IAM (FD-001-01) — Fase 1 (validada em Postgres real):** agregados `User` e `Role`,
  catálogo de permissões, autorização **deny-by-default** (`IPermissionChecker`), provisionamento de
  empresa com admin, e **gestão de papéis por API** — criar papel, conceder/revogar permissão e
  atribuir/remover papel de usuário (`/api/v1/roles`, `/api/v1/users/{id}/roles`). Concessão e
  revogação **mudam o acesso dinamicamente** (comprovado). **Métricas de uso** (`IUsageMetrics`)
  por tenant para o case de sucesso. RLS em `role`/`app_user` (`deploy/db/002_iam.sql`); isolamento
  entre tenants provado **sem filtro na aplicação** (só RLS).
- ✅ **Materials (MMS-002) — Fase 2, fatia 1 (validada):** bounded context próprio (schema
  `materials`, projetos `src/Materials/{Domain,Application,Infrastructure}`). Catálogo de **itens** +
  **unidades de medida com conversão** (ADR-013, com checagem de dimensão), endpoints protegidos
  (`materials.read`/`materials.manage`), RLS por tenant reusando o interceptor do Foundation.
  Comprovado: conversão 2 kg→2000 g / 1500 g→1.5 kg; cross-dimensão → 400; isolamento por RLS;
  deny-by-default cobrindo o módulo novo.
- ✅ **OC / Ordem de Compra — Fase 8, fatia 1 (backend, validado em Postgres real):** modelo de dados
  para emitir a OC no formato do grupo Trino (modelo OC 664). Novidades:
  - **Cadastro de empresas pagadoras** (`procurement.paying_company`): registro de **vários CNPJs** do
    grupo (razão social, CNPJ, Inscr. Estadual, endereço, bairro, cidade/UF, CEP, fone, e-mail). Na
    emissão da OC escolhe-se **qual empresa/CNPJ é a responsável pelo pagamento** → vira o cabeçalho
    comprador do documento. Tenant-scoped com **RLS forçada** + código único por tenant.
  - **Fornecedor com dados fiscais** (`supplier` estendido): endereço, bairro, cidade/UF, CEP, fone,
    e-mail, **Cond. Pgto** e **Forma Pgto** — o vencedor da concorrência/BID é o fornecedor da OC.
  - **Pedido com preços e número de OC** (`purchase_order`/`order_line` estendidos): **nº sequencial
    por tenant** (índice único), empresa pagadora, snapshot de Cond./Forma Pgto, totais de cabeçalho
    (IPI/ICMS/descontos/outras despesas/frete) e, por linha, **valor unitário + %IRRF/%ISS + data de
    entrega**; valores de serviço/IRRF/ISS/produtos/líquido calculados no domínio.
  - **Item na requisição**: além do lote atual, `POST /requisitions/{id}/lines` acrescenta itens a um
    rascunho (base do cadastro **manual na tela** e da **importação em lote**, próximas fatias).
  - Endpoints REST: CRUD de `paying-companies` e `suppliers` (leitura sob `purchases.read`, escrita sob
    `purchases.order`); emissão `POST /requisitions/{id}/order` agora recebe pagadora + fornecedor +
    preços por linha + totais. **Validado:** solução compila; **34/34** testes de unidade; migration EF
    aplicada em **Postgres 16 real** — nova tabela + colunas + índices únicos + **RLS forçada** em todas
    as tabelas de `procurement` (script idempotente `deploy/db/gen/procurement.sql` regenerado).
- ✅ **OC — Fase 8, fatia 2: cadastro de itens em lote via planilha Excel (validado por integração).**
  `GET /api/v1/purchases/requisitions/import-template` gera o **modelo .xlsx** (aba "Itens" com cabeçalho
  Código do Item / Quantidade / Unidade + aba de instruções) e `POST /requisitions/import` recebe a
  planilha preenchida (multipart), **lê linha a linha com validação** (código vazio, quantidade
  inválida/negativa → erro por linha; linhas vazias ignoradas) e cria a requisição (rascunho) com os
  itens. Biblioteca **ClosedXML** (100% gerenciada, sem dependência de cripto do sistema). Também
  `POST /requisitions/{id}/lines` para acrescentar **item manual** a um rascunho. **Validado por
  integração (Testcontainers Postgres, role `trino_app`):** download do template abre como planilha com
  o cabeçalho certo; **fluxo completo** — importa 2 itens da planilha → requisição → envia → aprova com
  **SoD (aprovador ≠ requisitante)** → emite a **OC** selecionando empresa pagadora + fornecedor
  vencedor + preços → confere nº da OC, pagadora, snapshot de Cond. Pgto e **totais calculados**
  (produtos = Σ qtd×preço). `dotnet test` → 34 unidade + **9 integração**.
- ✅ **OC — Fase 8, fatia 3: geração do PDF da Ordem de Compra + download (validado por integração).**
  `GET /api/v1/purchases/orders/{id}/pdf` monta a OC no **formato do modelo do grupo Trino (OC 664)** e
  devolve um **PDF** (via **QuestPDF**, licença Community): cabeçalho da **empresa pagadora** (comprador)
  com CNPJ/IE/endereço/fone/e-mail, nº da OC e data; bloco do **fornecedor vencedor** (código, CNPJ,
  endereço, Cond./Forma Pgto); **tabela de itens** (Qtd, U.M., código, descrição, data entrega,
  Vlr.Unit, Vlr.Serviço, %IRRF/%ISS, Vlr.IRRF/Vlr.ISS); **totais** (Produtos/IPI/ICMS/Descontos/Outras
  Despesas/Frete/Valor Líquido) e **valor por extenso** (conversor pt-BR próprio); rodapé com comprador
  e data de emissão. **Validado:** 7 testes de unidade do PDF/extenso (assinatura `%PDF-`, casos de
  extenso) + o fluxo de integração baixa a OC pelo endpoint real e confere `application/pdf` + `%PDF-`.
  `dotnet test` → 41 unidade + 9 integração (10 no total do projeto de integração, incluindo os do PDF).
- ✅ **OC — Fase 8, fatia 4: UI da Ordem de Compra (validada em navegador real).** Tela **Cadastros**
  (empresas pagadoras/CNPJs + fornecedores com campos fiscais), **Nova requisição** com **item manual**
  (montar item a item) e **importação em lote** (baixar modelo .xlsx + enviar planilha), e na aprovação
  um painel **Emitir OC** que **seleciona a empresa pagadora + o fornecedor vencedor + preços por linha**
  (com total ao vivo) e, na lista de **OCs emitidas** (nº, pagadora, fornecedor, valor líquido), o botão
  **Baixar OC (PDF)**. Novo item de menu "Cadastros", cliente de API com download/upload autenticados,
  componente `Select`. `npx tsc` limpo e `next build` OK. **E2E (Playwright, stack real API+Postgres) 8/8:**
  admin cadastra pagadora + fornecedor pela tela → baixa o modelo Excel → cria requisição com item manual
  → envia → **aprovador distinto aprova (SoD)** → admin emite a OC selecionando pagadora + fornecedor +
  preço → **baixa o PDF da OC** (assinatura `%PDF-`).
- ✅ **Publisher RabbitMQ real (Fase 4, fatia 2):** `RabbitMqEventPublisher` (exchange topic durável,
  mensagem persistente, `MessageId=EventId` p/ idempotência). Selecionado por configuração
  (`RabbitMq:Host`); sem broker, cai no publisher de log. Piloto: serviço `rabbitmq` no compose +
  Worker apontando pra ele. **Validado por integração (Testcontainers Postgres + RabbitMQ):**
  provisionar → Outbox → relay publica → fila recebe o `CompanyRegistered`. `dotnet test` → 7/7 integração.
- ✅ **Materials — Fase 2, fatia 2 (validada): estoque com saldo, ledger e concorrência.**
  Saldo como **projeção** atualizada por um **ledger append-only** de movimentos (entrada/saída);
  saída além do saldo → 400. **Serialização por chave de saldo** (`SELECT … FOR UPDATE` + version
  otimista) — comprovado com **50 lançamentos simultâneos** (líquido +30): saldo final exato, zero
  atualização perdida. Endpoints `/materials/items/{code}/{movements,balance}`.
- ✅ **Materials — Fase 2, fatia 3 (validada): motor de reposição (ADR-014).** Política mín/máx por
  item; sugestões `necessidade = máx − saldo` apenas para itens no ponto de reposição, ordenadas por
  prioridade e reagindo ao saldo. Endpoints `/materials/items/{code}/replenishment` e
  `/materials/replenishment/suggestions`. **Fase 2 (Materials) concluída.**
- ✅ **Procurement (PR-001) — Fase 3, fatia 1 (validada):** bounded context próprio (schema
  `procurement`). Requisição de compra com linhas, fluxo Draft→Submitted→Approved/Rejected e
  **Segregation of Duties** (o requisitante não aprova a própria requisição — 403 mesmo com a
  permissão). Requisitar e aprovar são permissões distintas. Decisão com **concorrência otimista**
  (2 approves simultâneos → uma só decisão vinga). **Ponte reposição→requisição** (from-suggestions)
  fecha o ciclo estoque baixo → compra. RLS por tenant nas duas tabelas.
- ✅ **Procurement — Fase 3, fatia 2 (validada): fornecedor + pedido de compra.** Cadastro de
  fornecedor e **emissão de pedido a partir de requisição APROVADA** (copia as linhas, um pedido por
  requisição via índice único). Guardas: pedido antes de aprovar → 400; segundo pedido da mesma
  requisição → 409. **Fase 3 (Procurement) conclui o procure-to-pay:** estoque baixo → sugestão →
  requisição → aprovação (SoD) → pedido ao fornecedor. RLS por tenant nas tabelas novas.
- ✅ **Frontend web (Fase 5, fatia 1 — validado em navegador real):** app **Next.js 15 / React 19 /
  TypeScript** (`web/`) com Tailwind, TanStack Query, Zustand e Zod. Login (IdP local), shell
  autenticado e telas **Painel / Materiais / Reposição / Compras** consumindo a API via proxy
  same-origin (`/api` → backend). `npm run build` OK; **smoke E2E (Playwright) 6/6** — login pela UI →
  painel com dados reais → sugestão de reposição.
- ✅ **Frontend — refino de UX (Fase 5, fatia 2):** **notificações (toasts)** de sucesso/erro em todas
  as ações, e **definição de política de reposição (mín/máx) pela UI** (fechava a lacuna: sem isso as
  sugestões só saíam via API). E2E (Playwright) 4/4: definir política pela tela → toast → sugestão
  aparece em Reposição.
- ✅ **UI ciente de papéis (Fase 5, fatia 3):** endpoint `GET /api/v1/me` (identidade + permissões
  efetivas); o frontend esconde navegação e ações que o usuário não pode executar. E2E (Playwright)
  5/5: um "aprovador" (só `purchases.approve/read`) vê apenas Compras + o botão Aprovar — sem
  Materiais nem cadastro de fornecedor; o admin vê tudo. Corrigido também vazamento de cache entre
  sessões (limpa o cache de queries no login/logout).
- ✅ **Testes automatizados (Fase 6, fatia 1 — QA-001):** projeto xUnit `tests/TrinoSupply.Domain.Tests`
  com **29 testes de unidade** das invariantes de domínio (SoD aprovação/rejeição, guard de saldo
  negativo, conversão de UoM e checagem de dimensão, cálculo de reposição, máquina de estados da
  requisição, hash de senha PBKDF2). Rodam no CI (`dotnet test TrinoSupply.slnx`). `dotnet test` → 29/29.
- ✅ **Testes de integração (Fase 6, fatia 2 — SEC-004 §8):** `tests/TrinoSupply.Integration.Tests`
  sobe **PostgreSQL real (Testcontainers)** + aplica os scripts de bootstrap + roda a API via
  `WebApplicationFactory` como `trino_app`. **4 testes** pela stack HTTP completa: **isolamento por
  RLS** entre tenants, **deny-by-default** (403), **SoD** (403), e **estoque sob concorrência**
  (15 lançamentos paralelos → saldo exato). `dotnet test` → 4/4.
- ✅ `docker-compose` (Postgres/Redis/RabbitMQ/MinIO) e pipeline CI.
- ✅ **Piloto containerizado (Fase 7, fatia 1):** `deploy/docker-compose.pilot.yml` sobe banco +
  API + frontend com um comando. **`bootstrap`** aplica migrations (scripts idempotentes do EF em
  `deploy/db/gen/`) e provisiona a role `trino_app` (não-superuser, sem BYPASSRLS). Dockerfiles
  multi-stage (API .NET, web Next.js). **Validado:** bootstrap cria 19 tabelas + 14 policies RLS +
  role endurecida (idempotente); API em config de produção conecta como `trino_app` e serve
  (provisão + login 200). Ver `deploy/README.md`.
- ⏳ **Próximo (GO-001 · sprint 1):** migrations EF Core; policies RLS aplicadas às tabelas de
  negócio reais; publisher Outbox→RabbitMQ real com role dedicada; IAM (usuários/papéis) e
  Auditoria; testes de integração de isolamento (Testcontainers — QA-001 / SEC-004 §8).

> Nota: este esqueleto foi escrito seguindo as convenções do .NET 9, porém **não foi compilado
> no ambiente de geração** (sem SDK .NET). Rode `dotnet build` localmente para validar.
