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
| `worker` | Consumidor RabbitMQ (conecta como `trino_worker` — grants mínimos) |
| `backup` | `pg_dump -F c` diário em volume próprio, retenção de 14 dias |
| `alerts` | Vigia: API caiu/voltou + backup ausente → webhook (`ALERT_WEBHOOK_URL`) ou log |

## Variáveis de ambiente (piloto)
| Variável | Para quê | Padrão |
| -------- | -------- | ------ |
| `APP_DB_PASSWORD` / `WORKER_DB_PASSWORD` | Senhas das roles `trino_app` / `trino_worker` | `apppw` / `workerpw` |
| `JWT_SIGNING_SECRET` | Assinatura dos access tokens (≥32 bytes) | valor de piloto |
| `PROVISIONING_KEY` | Header `X-Provisioning-Key` do `POST /companies` | valor de piloto |
| `SMTP_HOST/PORT/USER/PASSWORD/FROM` | E-mail transacional (convite/senha). Vazio = link vai para o log da `api` | vazio |
| `WEB_BASE_URL` | Base dos links de e-mail (`/criar-senha`) | `http://localhost:3000` |
| `ALERT_WEBHOOK_URL` | Webhook de alertas (Slack/Discord/Chat) | vazio (só log) |

> **Troque TODOS os padrões antes de expor a stack fora da máquina local.**

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

## Borda HTTPS (obrigatória para o go-live)
O compose serve HTTP puro nas portas 3000/5098 — **nunca exponha essas portas diretamente à
internet**. Coloque uma borda TLS na frente do `web` (o frontend já faz proxy de `/api`):

- **Opção A — Cloudflare Tunnel (recomendada para o piloto):** sem abrir porta nenhuma no host.
  ```bash
  cloudflared tunnel create trino && cloudflared tunnel route dns trino supply.seudominio.com.br
  cloudflared tunnel run --url http://localhost:3000 trino
  ```
  Defina `WEB_BASE_URL=https://supply.seudominio.com.br` para os links de e-mail. No painel da
  Cloudflare, ative WAF + "Always Use HTTPS"; se quiser restringir a acesso interno, adicione
  Cloudflare Access (SSO) na frente.
- **Opção B — reverse proxy com TLS (Caddy):** em host com portas 80/443 abertas:
  ```
  supply.seudominio.com.br {
      reverse_proxy localhost:3000
  }
  ```
  O Caddy emite e renova o certificado (Let's Encrypt) automaticamente.

Em ambas, o tráfego interno compose (`web → api → db`) permanece na rede privada do Docker.

## Produção (NÃO é isto)
Este compose é para **piloto** — segredos via env, TLS só na borda acima. Para produção: segredos
em cofre (OPS-001), **Cloudflare na borda** (CDN/WAF/DDoS/TLS) e **PostgreSQL gerenciado**
(ARC-016); migrations executadas por um passo privilegiado no pipeline, a app sempre como
`trino_app` e o worker como `trino_worker`.

## Nota sobre o build das imagens
Os `Dockerfile` são multi-stage padrão (restauram pacotes durante o build). Em ambientes cujo
proxy intercepta TLS sem CA confiável no container, o restore de NuGet/npm dentro do build falha
(`UntrustedRoot`) — nesse caso, publique no host e containerize a saída, ou configure a CA/proxy no
build. Em CI/dev com nuget.org e registry.npmjs.org acessíveis, `--build` funciona direto.
