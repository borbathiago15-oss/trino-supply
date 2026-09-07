# Deploy do Trino Supply no Railway

Guia de implantação do Trino Supply — a plataforma de suprimentos inteira, num
serviço só.

## O que sobe

Um único serviço (.NET 9) que serve:

- a **aplicação React** (SPA) em `/`, com o ciclo completo: solicitação, triagem,
  cotação, aprovação por alçada, registro da O.C. do ERP, recebimento e estoque;
- o **Portal do Fornecedor** em `/portal`, com acesso por CNPJ + chave;
- a **API** em `/api/v1/*` — 117 rotas, todas autenticadas exceto as quatro portas
  de entrada (login interno, refresh, logout e login do portal);
- o **health check** em `/health`, que fala com o banco antes de responder.

O banco é PostgreSQL, em três schemas (`foundation`, `materials`, `procurement`).
As migrations rodam automaticamente na inicialização. O `wwwroot` que o serviço
entrega é **saída do build do Vite** — é o Dockerfile que o gera, não o repositório.

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
dotnet test                        # 222 testes do backend
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
| `/health` responde **503** com `"database":"down"` | O serviço subiu mas não alcança o PostgreSQL | Confira o serviço Postgres do projeto e a `DATABASE_URL` |
| Tela mostra "Falha inesperada. Informe o código X ao suporte." | Erro não previsto no servidor | Procure o código X no log do Railway: a linha `Falha não tratada` traz o stack trace |

## Conferir que subiu de pé

```bash
curl -s https://SEU-DOMINIO/health
# {"status":"healthy", ..., "database":"up", "setupComplete":true}
```

`database` é o que importa: o endpoint pergunta ao PostgreSQL antes de responder,
e devolve **503** quando não o alcança. `setupComplete:false` quer dizer que o
administrador ainda não foi semeado — confira `ADMIN_EMAIL`/`ADMIN_PASSWORD`.

## Quando algo quebra em produção

Toda falha não prevista responde no mesmo envelope do resto da API:

```json
{ "error": { "code": "SYS-ERR-500",
             "message": "Falha inesperada no servidor. Informe o código 0HN7… ao suporte.",
             "correlationId": "0HN7…" } }
```

O usuário lê esse código na tela; o mesmo código aparece no log do Railway, na
linha `Falha não tratada em GET /api/… (correlação 0HN7…)`, junto do stack trace.
É por ele que se liga a queixa de quem usou à causa.

## Estado do produto

O ciclo de compras está completo e em produção. O que a plataforma faz, as regras
que ela não deixa contornar e a verificação antes de entregar estão no
`CLAUDE.md`; o histórico de refatoração e as decisões, em
`docs/PLANO-REFATORACAO.md`.
