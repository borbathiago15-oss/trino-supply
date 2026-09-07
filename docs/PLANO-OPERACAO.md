# Plano de Operação — pronto para o primeiro mês

Levantamento feito em 2026-09-07 sobre `main` (`0206956`), depois do Plano de
Refatoração fechado (21 PRs, #91–#111).

A fase anterior perguntava *"o sistema está bem construído?"*. Esta pergunta
outra coisa: **"o sistema aguenta gente de verdade, e quando quebrar alguém
consegue descobrir por quê?"** O produto está completo; o que falta é o que só
aparece quando o primeiro usuário fora da equipe entra.

---

## 1. O que a operação já tem

Vale registrar antes de listar o que falta, porque é mais do que parece:

- **Deploy automático** a partir de `main`, por Dockerfile, com healthcheck e
  política de reinício declarados no `railway.json`.
- **Migrations automáticas** na subida — schema e código sobem juntos, e um
  desvio entre eles é pego por `dotnet ef migrations has-pending-model-changes`.
- **Auditoria de processo** — cada transição de cotação, pedido e solicitação
  grava evento imutável com quem fez e quando.
- **Rate limiting** por usuário no upload e nos relatórios, por IP no login.
- **Correlação por requisição** já viajava no envelope de toda resposta.
- **222 testes de backend + 316 de tela + 45 de ponta a ponta**, no CI a cada PR.

---

## 2. O que faltava, medido

### 🟠 OPS-A · O healthcheck não checava nada — **entregue (#112)**

`/health` recebia o `AppDbContext` por injeção e **não perguntava nada a ele**.
Respondia `healthy` com o banco fora do ar — a única coisa que este endpoint
precisava saber.

Agora ele chama `CanConnectAsync` e devolve **503** quando não alcança o banco.
Conferido contra a API rodando: com o Postgres no ar, `200` e `"database":"up"`;
com o container parado, `503` e `"database":"down"`; religando, volta a `200`.

**A ressalva honesta:** isto não impede um deploy quebrado. As migrations rodam
na inicialização, então um banco inalcançável já **aborta a subida** antes de
qualquer requisição — o serviço nem chega a responder `/health`. O valor real é
outro, e menor: quando o banco cai **depois** que o app subiu, quem perguntar
"está tudo bem?" passa a receber a resposta verdadeira.

### 🟠 OPS-B · Falha inesperada não deixava rastro — **entregue (#112)**

Não havia tratador global de exceção. Uma falha não prevista devolvia **500 com
corpo vazio**, fora do envelope `{error:{code,message,correlationId}}` que toda
a API usa. Consequência dos dois lados:

- quem usava via *"o servidor não respondeu a esta operação"* — sem número
  nenhum para relatar;
- quem fosse investigar tinha o `TraceIdentifier` no log e **nada** que ligasse
  aquela linha à queixa recebida.

Agora a falha responde no envelope, com código `SYS-ERR-500`, e **a correlação
aparece dentro da mensagem** — é o número que a pessoa vai ditar no telefone. A
mesma correlação vai para o log, com o stack trace, na linha `Falha não tratada
em {método} {caminho}`.

Vale em Development também, de propósito: comportamento que só existe em
produção é o que ninguém testa.

**Duas redes, e uma delas quase nasceu furada.** `EnvelopeDeFalhaTests` monta um
servidor com uma rota que estoura e confere o contrato. `RotasProtegidasTests`
confere que o `Program.cs` registra o tratador — e **antes** do resto, porque
middleware registrado depois não vê o que quebrou antes dele. A primeira versão
desse segundo teste procurava a chamada com `IndexOf`: passava com a linha
comentada. Só apareceu porque comentei a linha de propósito para ver o teste
falhar — e ele passou. Corrigido para conferir linha a linha.

### 🟡 OPS-C · O guia de deploy descrevia outro sistema — **entregue (#112)**

O `DEPLOY.md` dizia "o **primeiro incremento executável**", "a tela de login em
`/`", "9 testes do serviço de autenticação". Quem fosse implantar hoje seguia um
mapa de meses atrás. Reescrito para o que existe: a SPA inteira, o Portal do
Fornecedor, as 117 rotas, como conferir que subiu de pé e como rastrear uma
falha pelo código de correlação.

---

## 3. O que ficou aberto, e por quê

### 🟠 OPS-D · A aplicação não cabe num celular

`AppLayout` põe a `Sidebar` de **252px fixos** (`shrink-0`) ao lado do conteúdo,
sem nenhum tratamento responsivo — nenhum `sm:`, `md:` ou `lg:` nos dois
arquivos. Num telefone de 360px o menu ocupa 70% da largura e sobra pouco mais
de 100px para a tela.

**Não decidi sozinho porque a resposta depende de quem usa o quê.** Se o
almoxarife confirma entrega no galpão e o solicitante abre SC em campo, isto é
bloqueador de lançamento. Se todo mundo trabalha sentado num desktop, é melhoria
para depois. É pergunta de operação, não de código.

### 🟡 OPS-E · Backup do banco não está verificado

O Railway faz backup do PostgreSQL conforme o plano da conta, mas **isso não
está no repositório e eu não tenho como conferir daqui**. Duas perguntas que
precisam de resposta antes do primeiro dado real entrar: existe backup
automático, e alguém já **restaurou** um para ver se funciona? Backup nunca
testado é esperança, não plano.

### 🟡 OPS-F · Três filas ainda sem paginação

Herdado do COR-A e documentado lá: fila de aprovação do fluxo antigo (conjunto
que só diminui), "SCs aguardando pedido" e `TriageService`. São telas de fila,
não de busca — o teto de 100/200 só incomoda se a operação acumular trabalho
parado. Vale medir depois do primeiro mês, com número real, em vez de paginar
por precaução.

### 🟢 OPS-G · Índice único em `erp_number`

Continua esperando o `scripts/verificar-banco.sql` rodar em produção. Se a
contagem de O.C. repetida vier zero, a migration é imediata.

---

## 4. Ordem sugerida

| # | Item | Depende de |
|---|---|---|
| 1 | **OPS-A + OPS-B + OPS-C** | nada — **entregue em #112** |
| 2 | **OPS-G** — índice único em `erp_number` | rodar o script em produção |
| 3 | **OPS-E** — confirmar e testar o backup | acesso ao painel do Railway |
| 4 | **OPS-D** — celular | decidir quem usa o sistema no telefone |
| 5 | **OPS-F** — paginar as três filas | número real do primeiro mês |

Os itens 2, 3 e 4 dependem de informação que só existe fora do repositório. O 5
depende de dado que ainda não foi gerado — e paginar antes de medir seria
resolver um problema que talvez não exista.
