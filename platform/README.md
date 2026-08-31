# Trino Platform — monorepo (Fases F0 a F4)

Base multi-tenant da plataforma Trino: schemas `core` e `auditoria` no Postgres,
Prisma com multi-schema, isolamento de tenant imposto por extensão do client
(F0); API NestJS com autenticação real, escopo por token e auditoria global
(F1); catálogo de materiais, fornecedores e elegibilidade nos schemas
`catalogo` e `fornecimento` (F2); alçadas e aprovação no schema `compras`,
com política de domínio pura e decisão sob transação serializável (F3); e a
requisição de compra com máquina de estados determinística (F4).
Convive com o app .NET do Trino Supply (`../src`) sem tocar nele.

## Estrutura

```
platform/
  packages/db/          @trino/db — Prisma + extensão de tenant
    prisma/schema.prisma        core, auditoria, catalogo, fornecimento e compras (fiel ao DDL)
    prisma/migrations/          init_core_and_audit, init_catalogo_e_fornecimento,
                                init_alcadas_e_aprovacao, init_requisicao_compra
    src/tenant-extension.js     forTenant(prisma, tenantId)
    src/limpeza-testes.js       limparBancoDeTestes (ordem das FKs, só testes)
    test/tenant-isolation.test.js
  apps/api/             @trino/api — API NestJS (F1 + F2)
    src/auth/                   login argon2 + JWT, JwtAuthGuard, TenantGuard
    src/audit/                  AuditInterceptor global + @Auditar
    src/usuarios/               CRUD de usuários do tenant (rotas auditadas)
    src/catalogo/               família → tipo → SKU base → variante, ROP e saldo
    src/fornecedores/           fornecedores, documentos, CA e elegibilidade
    src/aprovacoes/             alçadas, instâncias e decisão de etapas
      dominio/                  política PURA (sem I/O): faixas + assertPodeAprovar
      aprovar-etapa.usecase.ts  transação serializável + SELECT ... FOR UPDATE
    src/requisicoes/            esteira da requisição de compra
      dominio/maquina-estados.ts  transições T01–T10 PURAS + guardas
      casos-de-uso.ts           Submeter (R9), AssumirTriagem (TTO), DevolverAjuste (SLA)
    scripts/seed.js             bootstrap de tenant + admin
    test/politica-aprovacao.test.js  unitário puro da política (CT-01..CT-08)
    test/maquina-estados.test.js     unitário puro das transições T01..T10
    test/e2e.test.js            e2e da F1 contra Postgres real
    test/e2e-f2.test.js         e2e da F2 contra Postgres real
    test/e2e-f3.test.js         e2e da F3 contra Postgres real
    test/e2e-f4.test.js         e2e da F4 contra Postgres real
```

## Rodando

```bash
cd platform && npm install

# banco
cp packages/db/.env.example packages/db/.env   # aponte para o seu Postgres
cd packages/db
npx prisma migrate dev        # aplica migrations + gera o client (dev)
npx prisma migrate deploy     # produção: só aplica, nunca gera diff
node test/tenant-isolation.test.js

# API
cd ../../apps/api
cp .env.example .env          # DATABASE_URL + JWT_SECRET (obrigatório)
SEED_ADMIN_SENHA='...' npm run seed
npm run build && npm start    # http://127.0.0.1:3001/api/v1
npm test                      # unitários + e2e de todas as fases
npm run test:unit             # só o domínio puro — não precisa de banco
```

Login: `POST /api/v1/auth/login` com `{ cnpj, email, senha }` devolve
`tokenAcesso` (Bearer). As demais rotas exigem `Authorization: Bearer <token>`.

## Regras da fundação

- **Todo acesso de aplicação usa `forTenant(prisma, tenantId)`** — o filtro de
  tenant é injetado em todas as operações; registro de outro tenant se comporta
  como inexistente (`TENANT-ERR-404`). O client base fica para bootstrap,
  jobs administrativos e o catálogo global de permissões.
- **`$queryRaw`/`$executeRaw` não passam pela extensão**: SQL cru precisa
  filtrar `tenant_id` explicitamente e passa por revisão.
- **CHECKs, índices parciais e o particionamento do `audit_log`** não são
  expressáveis no schema.prisma: vivem nas migrations SQL (editadas via
  `--create-only`). São eles: o particionamento e os 6 CHECKs da F0; os 8 CHECKs
  da F2 mais `ux_variante_ean` (EAN único por tenant só quando preenchido) e
  `ix_documento_validade` (parcial em `obrigatorio = TRUE`); os 14 CHECKs da F3,
  o `EXCLUDE USING gist` `ex_regra_nivel_vigencia` (que exige a extensão
  `btree_gist`) e os parciais `ux_instancia_pendente` e
  `ux_etapa_um_aprovador_por_instancia`; os 6 CHECKs da F4, a sequência
  `compras.seq_requisicao` e o parcial `ix_requisicao_fracionamento`. Ao gerar uma
  migration nova, **revise o SQL antes de aplicar**: o Prisma não enxerga essas
  cláusulas e pode propor DROPs. Crie as partições anuais do `audit_log` antes
  da virada do ano (a partição DEFAULT segura o que escapar).
- **A ordem de exclusão dos testes mora em `limparBancoDeTestes`** (`@trino/db`):
  toda fase que acrescentar tabelas atualiza essa lista, e as suítes das fases
  anteriores continuam rodando.

## Regras da autenticação (F1)

- **Não existe usuário assumido.** O login valida CNPJ do tenant + `(tenant_id,
  email)` em `core.usuario` + `argon2.verify` contra `senha_hash`. Usuário
  inexistente, inativo ou tenant inativo caem no mesmo `401 AUTH-ERR-001`, e o
  caminho de falha ainda paga um verify de sacrifício para não denunciar contas
  pelo tempo de resposta.
- **`JWT_SECRET` é obrigatório**: sem ele a API não sobe (falha no bootstrap).
- **O `tenantId` vem exclusivamente do payload do token.** O `TenantGuard` só lê
  `request.user` (montado pela `JwtStrategy` a partir do token assinado) e
  publica `request.tenantId` e `request.db = forTenant(...)`. Headers como
  `x-tenant-id`, body e query string nunca são consultados — os controllers não
  recebem tenant por parâmetro e por isso não têm como aceitar um forjado.
- **Serviços recebem `request.db`, nunca o client base.** Um service de
  aplicação não vê `tenantId` e não consegue vazar dados de outro grupo.
- **Senhas e segredos nunca saem**: `senha_hash` e `mfa_secret` ficam fora das
  respostas da API e fora dos snapshots de auditoria.

## Auditoria (F1)

- `AuditInterceptor` é global (`APP_INTERCEPTOR`) e cobre qualquer módulo,
  presente ou futuro. Ele age em handlers **mutantes** (POST/PUT/PATCH/DELETE)
  marcados com `@Auditar('entidade')` — a marcação é o contrato explícito do que
  é mutação crítica.
- Cada registro em `auditoria.audit_log` leva ator (do token), entidade,
  `entity_id`, ação, `before_json`/`after_json` (JSONB), IP, user-agent e
  `correlation_id` (o do header `x-correlation-id`, se vier em formato UUID, ou
  um novo).
- O snapshot **antes** é lido pelo próprio client escopado antes do handler; o
  **depois** é a resposta do handler (nulo em DELETE).
- A escrita do log é aguardada dentro da requisição: se a auditoria falhar, a
  requisição falha. Auditoria de mutação crítica não é melhor-esforço.

## Catálogo e fornecimento (F2)

Hierarquia do catálogo: **família → tipo de produto → SKU base → variante**.
A variante é a unidade que tem saldo, parâmetro de ROP, CA e preço de
fornecedor. `sku_base.exige_ca = true` marca o item de EPI.

- `GET|POST /catalogo/familias`, `/catalogo/tipos`, `/catalogo/skus`,
  `/catalogo/variantes` (+ `PATCH /:id`); os `GET` aceitam filtro pelo pai.
- `PUT /catalogo/parametros-estoque` define o ROP do par (variante, centro de
  custo) — um só por par, redefinir atualiza.
- `GET|POST /catalogo/saldos` e `PATCH /catalogo/saldos/:id` **com bloqueio
  otimista**: o corpo informa a `version` lida; se o saldo mudou nesse meio
  tempo a resposta é `409 EST-ERR-409` e ninguém sobrescreve leitura velha.
- Validação por **class-validator** em todos os payloads, com `whitelist` e
  `forbidNonWhitelisted` globais: campo desconhecido é `400`, não algo
  silenciosamente ignorado. Query params de id também são validados como UUID.

Fornecedores: `GET|POST /fornecedores`, `GET /fornecedores/:id` (traz
documentos, CAs e SKUs), `PATCH /fornecedores/:id`, `PATCH
/fornecedores/:id/status`, `POST /fornecedores/:id/documentos`,
`POST /fornecedores/:id/certificados`, `POST /fornecedores/:id/skus`.
Sair de HOMOLOGADO exige motivo. O arquivo do documento vive fora do banco: o
que guardamos é a `arquivoUri`.

### Elegibilidade — a regra que precede a compra

`GET /fornecedores/:id/elegibilidade?varianteId=…` responde com **todos** os
motivos de bloqueio de uma vez, para o comprador resolver tudo numa ida:

| código | bloqueio |
| --- | --- |
| `FOR-ELG-001` | fornecedor não está `HOMOLOGADO` |
| `FOR-ELG-002` | documento **obrigatório** vencido |
| `FOR-ELG-003` | certidão exigida nunca cadastrada |
| `FOR-ELG-004` | item exige CA (EPI) e não há CA válido do fornecedor para a variante |

Detalhes que valem contrato:

- **Vale o documento mais recente de cada tipo**: reemitir a certidão substitui
  a anterior, sem apagar histórico.
- Documento com `obrigatorio = false` vencido **não** bloqueia.
- Certidões exigidas por padrão: `CND_FEDERAL`, `FGTS`, `TRABALHISTA`,
  ajustável com `FORNECEDOR_CERTIDOES_EXIGIDAS`. Lista vazia desliga a
  exigência de presença — nunca a de validade.
- `POST /fornecedores/:id/skus` (vínculo em processo de compra) chama
  `exigirElegivel` e devolve `409 FOR-ELG-409` com os motivos. Qualquer módulo
  de compras futuro deve usar o mesmo `ElegibilidadeService`, exportado pelo
  `FornecedoresModule`.

## Alçadas e aprovação (F3)

Três degraus configuráveis por faixa de valor. **Convenção de fronteira**
(o DDL não a fixa, então está decidida e testada aqui): a faixa é
`valor_min <= valor < valor_max`, com `valor_max` nulo significando "sem teto".
Com 0–5.000, 5.000–25.000 e 25.000+, isso dá R$ 4.999,99 → nível 1,
R$ 5.000,00 → nível 2, R$ 24.999,99 → nível 2 e R$ 25.000,00 → nível 3. É a
única leitura em que faixas adjacentes não disputam o valor da fronteira.

Rotas: `GET|POST /aprovacoes/regras`, `/aprovacoes/aprovadores`,
`/aprovacoes/delegacoes` (+ `PATCH /delegacoes/:id` para encerrar),
`POST /aprovacoes/instancias`, `GET /aprovacoes/instancias/:id` e
`POST /aprovacoes/etapas/:id/decisao`.

### A política é domínio puro

`src/aprovacoes/dominio/politica-aprovacao.ts` não conhece Prisma, Nest nem o
relógio do sistema: tudo entra pelo contexto, inclusive o `agora`. Por isso ela
roda em teste unitário sem banco e é a MESMA regra em qualquer caminho de
entrada (API, job, importação futura).

| código | bloqueio |
| --- | --- |
| `APV-B1` | o solicitante não aprova a própria requisição |
| `APV-B2` | o comprador responsável não aprova |
| `APV-B3` | delegação inativa, fora de vigência, de outro centro de custo, ou usada para burlar B1/B2 |
| `APV-B4` | o mesmo usuário não decide dois níveis da mesma instância (vale também para o delegante) |
| `APV-B5` | quem decide precisa de alçada **vigente** naquele nível e naquele centro de custo — inclusive o delegante, porque ninguém delega o que não tem |

Erros de estado saem separados: `APV-ERR-001` (instância encerrada),
`APV-ERR-002` (etapa já decidida), `APV-ERR-003` (nível anterior ainda pendente),
`APV-ERR-004` (rejeição sem comentário).

O banco repete B1–B4 em CHECKs e índices parciais. A política existe para dar a
resposta certa ANTES, com código e motivo; as constraints são a rede de
segurança para qualquer caminho que escape dela.

### A decisão acontece sob lock

`AprovarEtapaUseCase` roda tudo dentro de uma transação **SERIALIZABLE** que
começa travando a instância com `SELECT ... FOR UPDATE`: dois cliques no mesmo
segundo são serializados, e o segundo enxerga o estado já decidido em vez de
decidir sobre leitura velha (provado no e2e). Conflito de serialização vira
`409 APV-ERR-409`, que o cliente pode repetir.

O `AuditLog` é gravado **dentro** da mesma transação, com snapshot antes/depois
incluindo o estado da instância — por isso a rota de decisão é a única que NÃO
usa `@Auditar`: auditar de fora, após o commit, abriria brecha entre decidir e
registrar.

### Detalhes que valem contrato

- `regra_snapshot` congela a regra usada na abertura **e** o `centroCustoId`
  (a tabela de instância não tem essa coluna, e a política precisa dele para
  checar vigência). Mudar a alçada depois não reescreve o que já está em curso.
- As etapas 1..nível exigido nascem PENDENTES e são decididas em ordem.
- Rejeição em qualquer nível encerra a instância inteira na hora; aprovação só
  encerra quando o último nível exigido decide.
- `ux_instancia_pendente` garante uma única instância PENDENTE por requisição.

## Requisição de compra (F4)

A esteira vive numa **máquina de estados determinística**
(`src/requisicoes/dominio/maquina-estados.ts`), também domínio puro. A tabela
`TRANSICOES` é a especificação: cada transição declara de quais estados sai e
para qual leva. O que não está na tabela lança `TransicaoNaoPermitidaException`
— não existe caminho implícito, e uma transição esquecida vira erro em vez de
comportamento silencioso.

| transição | de → para |
| --- | --- |
| `T01_CRIAR` | (nova) → RASCUNHO |
| `T02_EDITAR` | RASCUNHO → RASCUNHO |
| `T03_SUBMETER` | RASCUNHO → SUBMETIDA |
| `T04_CANCELAR` | RASCUNHO, SUBMETIDA, EM_TRIAGEM, DEVOLVIDA_AJUSTE → CANCELADA |
| `T05_ASSUMIR_TRIAGEM` | SUBMETIDA → EM_TRIAGEM |
| `T06_DEVOLVER_AJUSTE` | EM_TRIAGEM → DEVOLVIDA_AJUSTE |
| `T07_EDITAR_EM_AJUSTE` | DEVOLVIDA_AJUSTE → DEVOLVIDA_AJUSTE |
| `T08_REENVIAR` | DEVOLVIDA_AJUSTE → SUBMETIDA |
| `T09_REJEITAR` | SUBMETIDA, EM_TRIAGEM, DEVOLVIDA_AJUSTE → REJEITADA |
| `T10_ENVIAR_COTACAO` | EM_TRIAGEM → EM_COTACAO |

`GET /requisicoes/:id` devolve `transicoesDisponiveis`, então a UI não precisa
duplicar essa tabela para saber quais botões mostrar.

### Guardas

`REQ-ERR-001` justificativa obrigatória para submeter/reenviar ·
`REQ-ERR-002` ao menos um item ativo · `REQ-ERR-003` comprador ausente na
triagem · `REQ-ERR-004` o solicitante não assume a própria triagem ·
`REQ-ERR-005` devolução sem motivo · `REQ-ERR-006` rejeição sem motivo ·
`REQ-ERR-007` cotação sem comprador · `REQ-ERR-008` centro de custo sem
orçamento do exercício · `REQ-ERR-009` saldo orçamentário insuficiente (R9) ·
`REQ-ERR-010` itens só mudam em RASCUNHO ou DEVOLVIDA_AJUSTE ·
`REQ-ERR-409` conflito de versão · `REQ-ERR-TRANSICAO` transição ilegal.

### Decisões que valem contrato

- **`valor_estimado` é derivado**, nunca digitado: toda mudança de item
  recalcula a soma de quantidade × preço de referência dos itens ATIVOS. É o
  que mantém a checagem de saldo honesta.
- **R9 valida, não reserva.** A submissão confere
  `orcado - comprometido - realizado >= valor_estimado` no exercício corrente e
  barra quem não tem orçamento definido. Comprometer e estornar pertencem ao
  pedido, que precisa de caminho de volta em cancelamento e rejeição — meia
  reserva seria pior que nenhuma.
- **TTO** (tempo até o atendimento) vai da submissão até `triagem_em`,
  descontando o tempo congelado; volta na resposta do `assumir-triagem`.
- **O SLA congela na devolução** (`sla_pausado_em`): enquanto a bola está com o
  solicitante, o relógio não corre contra compras. O reenvio soma o tempo
  parado em `sla_segundos_pausados` e destrava — e preserva a `submetida_em`
  original, sem reiniciar o relógio.
- **Bloqueio otimista pela coluna `version`**: o UPDATE só acerta a linha se a
  versão ainda for a lida; zero linhas afetadas vira `409` em vez de
  sobrescrever. Transição ilegal responde "ilegal" mesmo com versão velha —
  é a informação útil para quem chamou.
- Cada transição grava um `AuditLog` com snapshot antes/depois **dentro da
  mesma transação**, com a ação nomeada pelo código da transição (inclusive a
  criação, auditada como `T01_CRIAR`).
