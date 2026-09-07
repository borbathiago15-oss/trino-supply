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

### 🟠 OPS-D · A aplicação não cabia num celular — **entregue (#113)**

Sua resposta definiu o escopo: *"poucos irão usar o sistema no telefone, em sua
maioria para **aprovar** ou **ver andamento de pedido**; na grande maioria irá
usar o notebook."* Então não era redesenhar o sistema para celular — era fazer
o telefone servir para duas coisas, com o notebook intocado.

**O que eu medi antes de mexer**, com o navegador em 390px:

| | antes | depois |
|---|---:|---:|
| conteúdo útil | **138px** | 390px |
| largura da página | 455–621px (rolava de lado) | 390px |
| botão "Analisar e decidir" | x=**784** | x=50, com 290px |

138px de conteúdo é a tela quebrando uma palavra por linha. E o botão que o
aprovador precisa tocar estava 400px fora do campo de visão, atrás de um
arrasto lateral que ninguém adivinha.

**A casca.** No notebook (≥ `lg`) o menu é a coluna de sempre. Abaixo disso ele
sai do fluxo e vira gaveta atrás de um botão, com sobreposição que fecha ao
toque; trocar de tela fecha a gaveta, porque quem tocou num item quer ver a
tela, não o menu por cima dela.

**A fila de aprovação.** Encolher coluna não resolveu — mesmo escondendo Origem
e Etapa e encurtando o rótulo do botão, a tabela ainda dava 460px numa caixa de
316px. Quatro colunas não cabem em 390px, e insistir seria brigar com o meio.
No celular a fila virou **cartão por processo**: número, etapa, centro de custo,
fornecedor, valor e o botão em largura cheia. No notebook, a tabela de sempre —
as duas formas leem a mesma lista derivada uma vez.

**A rede.** Um teste de componente confere que o cartão traz o vencedor, o valor
e o link do processo; um E2E em 390px confere que a página não rola de lado, que
o menu abre pelo botão e fecha ao navegar, e que no notebook nada mudou.
Conferido de propósito: repondo o menu no fluxo, os três testes falham.

**O que não foi feito, e por quê:** as telas de cadastro, importação e os
dashboards continuam pensados para o notebook. Nenhuma delas é jornada de
telefone, e mexer nas 27 telas por precaução seria trabalho sem demanda.

### ⏸️ OPS-E · Backup — **adiado por decisão sua**

*"Não precisa nesse momento; quando o sistema estiver funcionando ele terá um
banco em outro local."* O banco de produção de hoje é provisório, então testar
restauração dele seria ensaiar sobre o que vai ser trocado.

**Fica registrado para a mudança**, porque é o momento em que a pergunta volta:
quando o banco definitivo entrar, vale confirmar que existe backup automático e
**restaurar um** antes do primeiro dado real. Backup nunca testado é esperança,
não plano.

### 🟠 OPS-H · O mesmo número de O.C. do SENIOR entrava duas vezes — **entregue (#114)**

Este achado nasceu da sua decisão sobre o banco. Como o índice único em
`erp_number` não pode ser aplicado agora — a migration roda na subida e
**abortaria o app** se o banco atual tiver duplicata —, fui ver o que a
aplicação já garantia sozinha. Achei um furo de mão única.

Os dois caminhos registram a O.C. do ERP, e conferiam coisas diferentes:

| Caminho | Conferia | Pegava a repetição? |
|---|---|---|
| tela do pedido | `ErpNumber == numero` | sim, dos dois lados |
| processo de cotação | `Number == numero` | **só a vinda dele mesmo** |

No caminho da cotação o pedido nasce com `Number == ErpNumber`, então olhar só o
número funcionava ali dentro. Mas quando a O.C. foi registrada pela tela do
pedido, o pedido mantém a própria numeração `PO-ano-sequência` e o número do
SENIOR fica **só** em `ErpNumber` — a busca por `Number` não achava nada, e o
mesmo número entrava de novo.

Provado antes de corrigir: o teste registra `OC-9001` pela tela do pedido e
tenta o mesmo número pela cotação. Antes da correção o segundo pedido era
**criado**; depois, é recusado com `RFQ-ERR-041`.

É exatamente o que o índice único pegaria — e é a razão de ele continuar
valendo a pena quando o banco definitivo entrar: a trava na aplicação depende de
todo caminho lembrar de conferir, e este esqueceu.

### 🟡 OPS-F · Três filas ainda sem paginação

Herdado do COR-A e documentado lá: fila de aprovação do fluxo antigo (conjunto
que só diminui), "SCs aguardando pedido" e `TriageService`. São telas de fila,
não de busca — o teto de 100/200 só incomoda se a operação acumular trabalho
parado. Vale medir depois do primeiro mês, com número real, em vez de paginar
por precaução.

### 🟢 OPS-G · Índice único em `erp_number`

**Vale a pena mesmo com a trava do OPS-H em pé** — o furo que o OPS-H fechou é a
prova: a garantia na aplicação depende de cada caminho lembrar de conferir, e um
deles não lembrava. O índice não esquece.

Fica para **quando o banco definitivo entrar**: num banco novo a migration
aplica limpa, sem risco de abortar a subida por duplicata preexistente. No banco
de hoje, o `scripts/verificar-banco.sql` continua sendo o pré-requisito.

---

## 4. Ordem sugerida

| # | Item | Depende de |
|---|---|---|
| 1 | **OPS-A + OPS-B + OPS-C** | nada — **entregue em #112** |
| 2 | **OPS-H** — a O.C. do SENIOR não se repete | nada — **entregue em #114** |
| 3 | **OPS-G** — índice único em `erp_number` | o banco definitivo, ou o script no atual |
| 4 | **OPS-E** — confirmar e testar o backup | **adiado**: volta com o banco definitivo |
| 5 | **OPS-D** — celular | **entregue em #113**, com a sua resposta |
| 6 | **OPS-F** — paginar as três filas | número real do primeiro mês |

O 3 e o 4 esperam o banco definitivo. O 6 depende de dado que ainda não foi
gerado — e paginar antes de medir seria resolver um problema que talvez não
exista.
