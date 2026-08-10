# Deploy — Piloto do Trino Supply

Sobe a stack completa (banco + API + frontend) para o **piloto interno**.

## Rodar
```bash
docker compose -f deploy/docker-compose.pilot.yml up --build
```
Depois abra **http://localhost:3000**. Para criar a primeira empresa + admin:
```bash
curl -X POST http://localhost:5098/api/v1/companies -H 'Content-Type: application/json' \
  -d '{"legalName":"Grupo Trino","taxId":"11.111.111/0001-11",
       "adminSubject":"admin","adminEmail":"admin@trino.com","adminName":"Admin","adminPassword":"troque-esta-senha"}'
```
Faça login no frontend com o `companyId` retornado, `admin@trino.com` e a senha.

## Serviços (`docker-compose.pilot.yml`)
| Serviço | Papel |
| ------- | ----- |
| `db` | PostgreSQL 16 |
| `bootstrap` | Aplica as migrations (scripts idempotentes do EF em `db/gen/`) e provisiona a role `trino_app` (não-superuser, sem BYPASSRLS) — roda e sai |
| `api` | Modular Monolith .NET 9 (conecta como `trino_app` → RLS efetivo) |
| `web` | Frontend Next.js (proxy `/api` → `api`) |

## Migrations (fonte da verdade = EF Core)
O `bootstrap` aplica SQL idempotente gerado do EF. Após criar/alterar migrations, regenere:
```bash
for ctx in Foundation Materials Procurement; do
  low=$(echo $ctx | tr 'A-Z' 'a-z')
  dotnet ef migrations script --idempotent --context ${ctx}DbContext \
    --project src/$ctx/Infrastructure --startup-project src/$ctx/Infrastructure \
    -o deploy/db/gen/${low}.sql
done
```

## Produção (NÃO é isto)
Este compose é para **piloto** — segredos em texto, sem TLS. Para produção: segredos em cofre
(OPS-001), **Cloudflare na borda** (CDN/WAF/DDoS/TLS) e **PostgreSQL gerenciado** (ARC-016);
migrations executadas por um passo privilegiado no pipeline, a app sempre como `trino_app`.

## Nota sobre o build das imagens
Os `Dockerfile` são multi-stage padrão (restauram pacotes durante o build). Em ambientes cujo
proxy intercepta TLS sem CA confiável no container, o restore de NuGet/npm dentro do build falha
(`UntrustedRoot`) — nesse caso, publique no host e containerize a saída, ou configure a CA/proxy no
build. Em CI/dev com nuget.org e registry.npmjs.org acessíveis, `--build` funciona direto.
