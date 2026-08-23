# Deploy do Trino Supply no Railway

Guia de implantação do serviço **Foundation — Identidade (login)**, o primeiro incremento executável do Trino Supply.

## O que sobe

Um único serviço (.NET 9) que serve:

- a **tela de login** em `/` (pt-BR);
- a **API de autenticação** em `/api/v1/auth/*` (login, refresh rotativo, logout, me);
- o **health check** em `/health`.

O banco é PostgreSQL. As migrations rodam automaticamente na inicialização.

## Passo a passo (Railway)

1. **Crie o projeto** no [railway.app](https://railway.app) (ou use o existente).
2. **Adicione um serviço PostgreSQL**: `+ New` → `Database` → `PostgreSQL`.
3. **Adicione o serviço da aplicação**: `+ New` → `GitHub Repo` → selecione `borbathiago15-oss/trino-supply` (branch `main`). O Railway detecta o `Dockerfile` e o `railway.json` na raiz automaticamente.
4. **Configure as variáveis** do serviço da aplicação (aba **Variables**):

   | Variável | Valor | Observação |
   |---|---|---|
   | `DATABASE_URL` | `${{Postgres.DATABASE_URL}}` | Referência ao serviço PostgreSQL do mesmo projeto |
   | `JWT_SECRET` | um segredo forte com **32+ caracteres** | Gere com `openssl rand -base64 48`; nunca reutilize |
   | `ADMIN_EMAIL` | seu e-mail de acesso | **Este será o usuário do login** |
   | `ADMIN_PASSWORD` | senha forte com **12+ caracteres** | **Esta será a senha do login** |
   | `ADMIN_NAME` | seu nome (opcional) | Exibido após o login |

5. **Gere o domínio público**: serviço → `Settings` → `Networking` → `Generate Domain`.
6. Acesse o domínio: a tela de login do Trino Supply aparece. **Entre com o `ADMIN_EMAIL` e o `ADMIN_PASSWORD` que você definiu no passo 4** — as credenciais são suas, definidas por você, e nunca ficam no código.

## Como o acesso inicial funciona

- Na primeira inicialização, se **nenhum usuário existir**, o sistema cria o administrador a partir de `ADMIN_EMAIL`/`ADMIN_PASSWORD` (senha armazenada só como hash PBKDF2 — nunca em claro).
- Se as variáveis não estiverem definidas, o serviço sobe, mas o login responde `503` com orientação — defina as variáveis e faça **Redeploy**.
- Depois que o primeiro usuário existe, o seed **nunca mais roda** — alterar `ADMIN_PASSWORD` na variável não troca a senha de um usuário já criado.
- Sessão: access token JWT de 15 minutos + refresh token rotativo de 7 dias (reuso de token rotacionado revoga a cadeia inteira — proteção contra replay).

## Rodando localmente

```bash
docker compose up --build
# http://localhost:8080 — credenciais definidas no docker-compose.yml (troque-as)
```

Sem Docker:

```bash
cd src/backend
dotnet test                        # 9 testes do serviço de autenticação
cd Foundation/TrinoSupply.Foundation.Api
DATABASE_URL=postgresql://postgres:devpass@localhost:55432/trino_supply \
JWT_SECRET=um-segredo-local-de-32-caracteres-ou-mais \
ADMIN_EMAIL=admin@exemplo.com ADMIN_PASSWORD='SenhaForte#123' \
dotnet run
```

## Solução de problemas

| Sintoma | Causa provável | Ação |
|---|---|---|
| Login responde 503 `IAM-ERR-503` | `ADMIN_EMAIL`/`ADMIN_PASSWORD` ausentes na primeira subida | Defina as variáveis e Redeploy |
| Serviço não sobe e o log cita `JWT_SECRET` | Segredo ausente ou com menos de 32 caracteres | Defina `JWT_SECRET` forte |
| Log cita `Nenhuma conexão de banco configurada` | `DATABASE_URL` não referenciada | Use `${{Postgres.DATABASE_URL}}` |
| 429 no login | Rate limit (10 tentativas/min por IP) | Aguarde 1 minuto |
| "Checking your browser" ao abrir a URL | Proteção de tráfego do Railway | Normal em navegador; resolve sozinho |

## Roadmap de implementação

Este serviço é o primeiro passo da ordem oficial (ADR-011: Foundation antes das APIs). Próximos incrementos: demais domínios do Foundation (organização, workflow, auditoria…), depois PR-001, MMS-002 e MMS-004 — todos já 100% especificados em `docs/`.
