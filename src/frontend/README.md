# Trino Supply — frontend React

Frontend novo do Trino Supply, em **React 18 + TypeScript + Vite + Tailwind**.
Ele é buildado para dentro do `wwwroot/app/` do app .NET e servido pelo mesmo
servidor, no prefixo `/app/`. O legado (`wwwroot/index.html`) continua em `/`
enquanto as telas migram, uma a uma.

## Como rodar

```bash
# API .NET no ar em http://127.0.0.1:5099 (veja o README do backend)
cd src/frontend
npm ci
npm run dev        # http://127.0.0.1:5173/app/  (proxy de /api e /assets para o .NET)
```

A sessão é a mesma do legado: os tokens ficam em `sessionStorage`
(`ts.access` / `ts.refresh`), então quem já entrou em `/` entra em `/app/`
sem novo login, na mesma aba.

## Qualidade

```bash
npm run typecheck   # tsc
npm run lint        # eslint
npm test            # vitest (unitários e de componente)
npm run build       # gera src/backend/.../wwwroot/app (ignorado pelo git)
npm run e2e         # Playwright contra a API .NET real (veja e2e/README)
```

## Estrutura

- `src/api/` — cliente HTTP tipado (mesmo contrato do legado: `{data}` / `{error:{code,message}}`,
  renovação automática do token em 401) e um módulo por recurso da API.
- `src/dominio/` — papéis, módulos e capacidades (espelho de `Domain/User.cs`).
- `src/layout/` — casca da aplicação: menu lateral e barra superior.
- `src/paginas/` — telas. Cada tela migrada do legado vira uma pasta aqui.
- `e2e/` — testes de ponta a ponta com Playwright.

## Deploy

O `Dockerfile` da raiz tem um estágio Node que roda `npm ci && npm run build`
antes do `dotnet publish`; o resultado vai junto no container. Nada muda no
Railway.
