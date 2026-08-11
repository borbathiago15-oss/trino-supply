# Trino Supply — Checklist de Go-Live

Data da avaliação: 2026-08-11 · Escopo: piloto interno do grupo Trino (multi-tenant desde o início).

## Veredito: **APTO — condicionado aos 4 itens de implantação abaixo**

O código, o banco e os testes estão prontos. O que resta **não é desenvolvimento**: são passos de
implantação/configuração que só podem ser feitos no ambiente real, no dia do go-live.

### Condições de implantação (fazer no ambiente real)
| # | Item | Como |
|---|------|------|
| 1 | **Borda HTTPS** — nunca expor 3000/5098 direto | Cloudflare Tunnel ou Caddy (ver `deploy/README.md` § Borda HTTPS) |
| 2 | **Trocar todos os segredos padrão** | `APP_DB_PASSWORD`, `WORKER_DB_PASSWORD`, `JWT_SIGNING_SECRET` (≥32 bytes), `PROVISIONING_KEY`, senha do Postgres |
| 3 | **SMTP real** para convite/recuperação de senha | `SMTP_HOST/PORT/USER/PASSWORD/FROM` + `WEB_BASE_URL` com o domínio público |
| 4 | **Webhook de alertas** | `ALERT_WEBHOOK_URL` (Slack/Discord/Google Chat) |

---

## O que foi verificado (e onde está garantido)

### Segurança
- ✅ **Isolamento multi-tenant fail-closed**: RLS `FORCE` em TODAS as tabelas de negócio,
  incluindo `outbox` e `password_setup_token`; sem GUC de tenant → 0 linhas. App conecta como
  `trino_app` (NOSUPERUSER, NOBYPASSRLS); worker como `trino_worker` (grants mínimos).
- ✅ **AuthN/AuthZ**: JWT curto + refresh token rotacionado (hash em banco); permissões
  deny-by-default; escopo por centro de custo (Master Junior) aplicado no servidor (403 fora do escopo).
- ✅ **Anti-força-bruta**: 5 falhas/15 min → conta trava (429); auditoria de `login`/`login_failed`.
- ✅ **Provisionamento fechado**: `POST /companies` exige `X-Provisioning-Key` (fail-closed em produção).
- ✅ **Bloqueio de usuário** revoga refresh tokens; troca de senha revoga sessões antigas.
- ✅ **Senhas**: PBKDF2; convite/recuperação por token de uso único (24h, hash em banco, anti-enumeração).

### Confiabilidade e operação
- ✅ **Migrations**: EF Core é a fonte da verdade; bootstrap aplica SQL idempotente gerado.
- ✅ **Outbox + RabbitMQ** com relay idempotente (`processed_event`).
- ✅ **Backup diário** (`pg_dump -F c`, retenção 14 dias) + **vigia** de saúde da API e do frescor
  do backup com notificação por webhook.
- ✅ **Observabilidade**: OpenTelemetry, correlationId em toda resposta de erro (500 nunca vaza stack).
- ✅ **Paginação** em todas as listagens (limite com teto) — sem "SELECT * da vida inteira".

### Qualidade
- ✅ **62 testes de domínio + 41 de integração** (Testcontainers com Postgres real: RLS, escopo,
  lockout, fluxo de senha, roteamento estoque/compra, outbox) — todos verdes.
- ✅ **E2E de navegador** (Playwright) dos fluxos v2 — 8/8 no último ciclo.

### Funcional (v2 validado com o piloto)
- ✅ Pedido único (2 níveis de aprovação) com roteamento pós-aprovação: estoque interno → baixa
  atômica; sem saldo → rota de compra (OC + PDF).
- ✅ Central de Aprovação (aprovar/reprovar com motivo), dashboards de Suprimentos e de Estoque,
  Estoque do Almox (entrada/saída, lote via planilha), Entregas EPI (baixa por colaborador +
  Ficha de EPI em PDF), Cadastros (CNPJ↔centro, fornecedor, usuários com perfis e centros).

---

## Recomendações pós-go-live (não bloqueiam)
1. **Cloudflare Access/SSO** na frente do piloto enquanto for uso interno.
2. **Teste de restauração de backup** agendado (mensal): `pg_restore` num banco vazio.
3. **Postgres gerenciado + segredos em cofre** quando virar SaaS externo (ARC-016/OPS-001).
4. Saída avulsa de estoque pedindo **centro de custo** também fora do fluxo de entrega (hoje o
   centro é obrigatório na entrega/baixa por colaborador e no pedido).
5. Dashboards analíticos (consumo por colaborador/centro, tempo de ciclo, previsão) — backlog v3.
