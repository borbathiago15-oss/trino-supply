# Trino Supply

Plataforma corporativa de suprimentos do Grupo Trino: solicitação, cotação,
aprovação por alçada, pedido de compra, recebimento e estoque.

- **Backend**: .NET 9 minimal API em `src/backend/Foundation/TrinoSupply.Foundation.Api`,
  EF Core + Npgsql, testes xUnit em `src/backend/tests/`.
- **Frontend**: React 18 + TypeScript + Vite + Tailwind em `src/frontend/`,
  testes vitest ao lado de cada tela e E2E Playwright em `src/frontend/e2e/`.
- **Deploy**: Railway, automático a partir de `main`, via Dockerfile na raiz.
  O build do Vite gera o `wwwroot` que a API serve.

## Regra de frontend: React, sempre

**Toda mudança ou melhoria de interface, a partir de agora, é escrita em React.**

O frontend HTML/JS que existia em `wwwroot/index.html` foi migrado por completo
e removido. Não existe mais "versão clássica" para manter em paralelo.

Na prática:

- Tela nova, ajuste de tela existente, correção visual, campo novo num formulário
  — tudo em `src/frontend/src/`, em TypeScript, com componente React.
- **Não** voltar a servir HTML montado no servidor, template Razor, string de
  markup no C# ou `<script>` solto no `wwwroot`. O que o backend entrega para o
  navegador é JSON pela API e os arquivos que o Vite gerou.
- O `wwwroot` é **saída de build**, não fonte: está no `.gitignore` e é
  reconstruído a cada deploy. Editar arquivo lá é trabalho perdido.
- Toda tela nova entra pelo roteador em `src/frontend/src/App.tsx` e, quando faz
  parte da navegação, pelo menu em `src/frontend/src/layout/menu.ts`.
- Tela nova nasce com teste: vitest para a lógica e o comportamento da tela,
  E2E quando o fluxo atravessa mais de uma tela.

A única exceção é o `index.html` de entrada do Vite em `src/frontend/index.html`,
que é a casca da aplicação — não é lugar de lógica nem de conteúdo.

## Autoridade sobre o banco

O schema do PostgreSQL pertence ao projeto .NET e às suas **EF Core Migrations**
em `Infrastructure/Migrations`. Mudança de schema se faz com migration nova
(`dotnet ef migrations add`), nunca com SQL manual em produção nem com
ferramenta de outro ecossistema. As tabelas vivem em três schemas —
`foundation`, `materials` e `procurement`.

Antes de abrir PR que toca o modelo, conferir que não há desvio entre o código e
a última migration:

```bash
dotnet ef migrations has-pending-model-changes
```

## Regras de negócio que o código não pode contornar

Estão no backend, com teste, e a interface deve **antecipá-las** em vez de deixar
o usuário descobrir no erro do servidor:

- **RFQ-ERR-030** — segregação de funções: quem escolheu o fornecedor não aprova
  a própria escolha; quem deu o Nível 1 não dá o Nível 2.
- **RFQ-ERR-040/041** — a O.C. nunca é emitida pelo sistema. Ela é fechada no ERP
  SENIOR e aqui só se registra o número, depois das duas aprovações.
- **PO-BR-011** — **sem O.C. gerada no ERP, a compra não fecha.** A única exceção
  é a observação dizendo por que a O.C. não foi gerada: com ela (mínimo 10
  caracteres, em `no_erp_reason`) o fechamento segue normalmente e a justificativa
  fica na auditoria. O sistema **nunca** inventa um número de O.C.: nesse caso o
  pedido usa a própria numeração `PO-ano-sequência` e `erp_number` continua nulo —
  é o que mantém honesto todo relatório que conta O.C. do ERP. A O.C. que chegar
  depois deixa de ser exceção e limpa a observação. Vale nos dois caminhos:
  `RFQ-ERR-043` no processo de cotação e `PO-ERR-054` na tela do pedido.
- **IC-ERR-023** — EPI/EPC só circula com C.A. válido no par produto-fornecedor.
- **SEC-004** — senha definida por outra pessoa é provisória. Usuário criado pelo
  cadastro, admin semeado pelo ambiente e senha redefinida pelo administrador
  nascem com `must_change_password`; enquanto a marca existe, o middleware do
  `Program.cs` recusa toda rota `/api` fora de `/auth/{change-password,me,logout,refresh,login}`
  com **IAM-ERR-022**. A política de senha vive em `Auth/PasswordPolicy.cs` e vale
  para os três caminhos que gravam senha.
- **SUP-ERR-013/014** — o pré-cadastro do fornecedor. Cotar vem antes de cadastrar:
  o mínimo para entrar numa cotação é **razão social + telefone** (`SUP-ERR-014`), e
  o CPF/CNPJ é **opcional** — o comprador pede preço por telefone e nessa hora não o
  tem. O fornecedor nasce `PROSPECT` e concorre em pé de igualdade; o documento é
  exigido para **homologar** (`SUP-ERR-013`), e a homologação é o que permite vencer
  o BID (`SUP-ERR-030`). Gravado uma vez, o CNPJ é identidade e a edição não o troca.
- O *saving* tem **três réguas**, que convivem porque respondem perguntas diferentes,
  e cada uma é nula quando não se aplica:
  - **negociação** — contra a **primeira** proposta do fornecedor vencedor;
  - **concorrência** — contra a **maior proposta completa** do BID; nula quando houve
    um proponente só, para não contar disputa que não existiu;
  - **orçamento** — contra o valor que o solicitante informou na SC (`budget`); só
    existe quando **todas** as SCs do processo informaram o seu, senão o total fechado
    seria comparado a um orçamento parcial.

## Verificação antes de entregar

```bash
# backend
dotnet build src/backend/Foundation/TrinoSupply.Foundation.Api -c Release
dotnet test  src/backend/tests/TrinoSupply.Foundation.Tests -c Release

# frontend
cd src/frontend
npx eslint src e2e --max-warnings 0
npm run build          # typecheck + Vite
npx vitest run
npx playwright test    # exige a API local em http://127.0.0.1:5099
```

Integridade do banco: `scripts/verificar-banco.sql` roda só leitura e devolve
uma linha por checagem — qualquer contagem diferente de zero é uma inconsistência
para investigar.

## Ambiente local

```bash
docker run -d --name ts-pg -e POSTGRES_PASSWORD=devpass \
  -p 127.0.0.1:55432:5432 postgres:16
docker exec ts-pg psql -U postgres -c "CREATE DATABASE trino_supply"

cd src/backend/Foundation/TrinoSupply.Foundation.Api
DATABASE_URL=postgresql://postgres:devpass@localhost:55432/trino_supply \
JWT_SECRET=um-segredo-local-de-32-caracteres-ou-mais \
ADMIN_EMAIL=admin@trinosupply.com.br ADMIN_PASSWORD='TrinoSupply@2026!' \
ASPNETCORE_URLS=http://127.0.0.1:5099 dotnet run
```

`JWT_SECRET` é obrigatório fora de Development e precisa de 32 caracteres ou
mais — sem ele a aplicação aborta na inicialização, de propósito.
