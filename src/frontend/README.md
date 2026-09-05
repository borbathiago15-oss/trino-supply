# Trino Supply — frontend

Frontend do Trino Supply, em **React 18 + TypeScript + Vite + Tailwind**. Ele é
buildado para dentro do `wwwroot/` do app .NET e servido pelo mesmo servidor, na
raiz. As 23 telas internas e o Portal do Fornecedor vivem aqui — o sistema
clássico (`wwwroot/index.html` e `portal.html`) foi removido depois que a última
tela migrou.

URLs antigas em `/app/...` continuam valendo: o .NET redireciona cada uma para a
mesma tela na raiz, para não quebrar link guardado nem favorito.

## Como rodar

```bash
# API .NET no ar em http://127.0.0.1:5099 (veja o README do backend)
cd src/frontend
npm ci
npm run dev        # http://127.0.0.1:5173/  (proxy de /api e /assets para o .NET)
```

A sessão fica em `sessionStorage` (`ts.access` / `ts.refresh`), por aba. O Portal
do Fornecedor tem sessão própria (`portal.token`, CNPJ + chave de acesso) e não
compartilha nada com a do time interno.

## Qualidade

```bash
npm run typecheck   # tsc
npm run lint        # eslint
npm test            # vitest (unitários e de componente)
npm run build       # gera src/backend/.../wwwroot (ignorado pelo git)
npm run e2e         # Playwright contra a API .NET real (veja e2e/README)
```

## Estrutura

- `src/api/` — cliente HTTP tipado (`{data}` / `{error:{code,message}}`, renovação
  automática do token em 401) e um módulo por recurso da API.
- `src/dominio/` — papéis, módulos e capacidades (espelho de `Domain/User.cs`).
- `src/layout/` — casca da aplicação: menu lateral e barra superior.
- `src/paginas/` — uma pasta por tela.
- `public/` — estáticos servidos como estão (a marca em `assets/brand/`).
- `e2e/` — testes de ponta a ponta com Playwright.

## Deploy

O `Dockerfile` da raiz tem um estágio Node que roda `npm ci && npm run build`
antes do `dotnet publish`; o `wwwroot` gerado vai junto no container. Nada muda
no Railway.
