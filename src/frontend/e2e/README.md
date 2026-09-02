# E2E — Pedidos de Compra (React)

Roda contra a API .NET de verdade, que também serve o build do React em `/app/`.

```bash
# 1) API no ar (Postgres + variáveis; veja src/backend)
# 2) build do React dentro do wwwroot
npm run build
# 3) cenário: fornecedor, local de estoque e um pedido em aberto
API_URL=http://127.0.0.1:5099 ADMIN_EMAIL=... ADMIN_PASSWORD=... npm run e2e:seed
# 4) testes
API_URL=http://127.0.0.1:5099 ADMIN_EMAIL=... ADMIN_PASSWORD=... npm run e2e
```
