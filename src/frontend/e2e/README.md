# E2E — Trino Supply

Roda contra a API .NET de verdade, que também serve o build do React na raiz.

```bash
# 1) API no ar (Postgres + variáveis; veja src/backend)
# 2) build do React dentro do wwwroot
npm run build
# 3) testes — o globalSetup faz o login único e monta o cenário da execução
API_URL=http://127.0.0.1:5099 ADMIN_EMAIL=... ADMIN_PASSWORD=... npm run e2e
```

O cenário (fornecedor, local de estoque, centro de custo, família, produto e um
pedido em aberto) é montado a cada execução pelo `globalSetup`, então a suíte
roda em banco novo sem passo de seed à parte.
