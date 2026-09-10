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
- **A adjudicação é por escopo, e o escopo pode ser o item.** `QuotationAward.QuotationItemId`
  nulo quer dizer a família inteira — é o que toda adjudicação antiga significa e continua
  significando. Preenchido, é aquele item: o papel com um fornecedor e a caneta com outro,
  dentro da mesma família. Tudo a jusante (rateio de frete, itens da O.C., baseline do
  saving) pergunta `ItemsCovered(award)` em vez de assumir a família — foi o que permitiu
  dividir sem refazer conta nenhuma. `RFQ-ERR-023` cobra um vencedor por **item**, e
  `RFQ-ERR-024` só exige que o fornecedor tenha cotado o que o escopo pede.
- **E o escopo pode ser parte do item: a quantidade se divide.** `QuotationAward.Quantity`
  nulo é a quantidade inteira — o que toda adjudicação anterior significa. Preenchido, é
  quanto **daquele item** sai com aquele fornecedor: setecentas botas com um e trezentas com
  outro, porque nenhum dos dois entrega mil. Três coisas seguram a conta: quantidade **só
  junto de `QuotationItemId`** (dividir "a família" em quantidade não quer dizer nada — a
  família tem itens de unidades diferentes), a **soma tem de fechar** a quantidade pedida
  (`RFQ-ERR-025`, que diz quanto falta ou sobra em vez de só recusar), e **"levou a proposta
  inteira" passa a exigir a quantidade toda** — sem isso os dois fornecedores receberiam o
  frete cheio da própria proposta e a compra dividida sairia mais cara que a inteira. A
  forma do pedido é conferida **antes** das referências: "você mandou quantidade para uma
  família" é mais útil que "esta família não existe" quando os dois estão errados. O índice
  único da adjudicação inclui o fornecedor — o mesmo item pode ir a dois deles; o que
  continua proibido é o **mesmo** fornecedor levar o mesmo item duas vezes.
- **Contrato de parceria preenche o preço da proposta, se estiver vigente.**
  `QuotationService.ContractPricesAsync` casa item do processo com `SupplierContractItem`
  **pelo produto do catálogo** (código como segundo caminho), nunca pela descrição — "BOTA
  BIQUEIRA DE PVC" e "BOTA BIQUEIRA DE AÇO" trocariam de preço sem ninguém notar. Fora da
  vigência não preenche nada: preço vencido entrando calado é pior que campo vazio.
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
- **Porque o CNPJ é opcional, ele não pode ser o único guarda contra cadastro repetido.**
  `SUP-ERR-010` (documento igual) deixava passar o caso que de fato acontece: pré-cadastrar
  na cotação e cadastrar "de verdade" depois criava **dois** fornecedores com o mesmo nome —
  o primeiro `PROSPECT` e preso ao convite, o segundo homologado. A tela então dizia "não
  pode vencer" de um fornecedor que o comprador tinha acabado de homologar. A razão social
  fecha essa porta (`SUP-ERR-015`), comparada por `SupplierService.ChaveDoNome`: maiúsculas,
  sem acento, só letras e dígitos — a mesma regra existe em `chaveDoNome` no navegador, e
  divergir faria a tela reaproveitar um e o servidor recusar outro. Ela **não** normaliza
  "LTDA"/"ME"/"EIRELI": recusar "Alfa Ltda" porque existe "Alfa ME" barraria cadastro
  legítimo, e bloqueio que atrapalha o trabalho certo acaba contornado por fora. E o
  pré-cadastro da cotação **reaproveita** quem já existe em vez de recriar: o pedido ali é
  "coloque este fornecedor na cotação", não "crie um registro". Inativo não entra calado —
  reativar é decisão do cadastro.
- O *saving* tem **três réguas**, que convivem porque respondem perguntas diferentes,
  e cada uma é nula quando não se aplica:
  - **negociação** — contra a **primeira** proposta do fornecedor vencedor;
  - **concorrência** — contra a **maior proposta completa** do BID; nula quando houve
    um proponente só, para não contar disputa que não existiu;
  - **orçamento** — contra o valor que o solicitante informou na SC (`budget`); só
    existe quando **todas** as SCs do processo informaram o seu, senão o total fechado
    seria comparado a um orçamento parcial.
- **O prazo de resposta é de cada convite, não do processo.** `QuotationSupplier.ResponseDeadline`
  nulo cai no prazo do processo — é o que mantém legível todo convite anterior à regra. Ele
  existe porque os convites não saem no mesmo dia: com um prazo só, quem foi chamado na quinta
  recebe a folga dada a quem foi chamado na segunda. Vencido o prazo **daquele convite**, a
  proposta é barrada (`RFQ-ERR-020`) e o comprador tem duas saídas, ambas com efeito real:
  **novo prazo** (`RFQ-ERR-071` recusa data no passado ou que encurte; a contagem de
  prorrogações fica) e **seguir sem o fornecedor** (`RFQ-ERR-072`, motivo obrigatório). Seguir
  sem ele **não apaga o convite** — "chamei três e um não veio" é uma história diferente de "só
  chamei dois", e é ela que explica um BID com menos proponentes. Quem já respondeu não se
  dispensa: proposta na mesa é o oposto de ausência, e recusá-la é decisão de adjudicação.
  Reconvidar o dispensado reabre o convite, em vez de criar um segundo.
- **O score multicritério informa; a régua dele é da empresa.** Ele compara as propostas
  vigentes por preço, entrega, pagamento, OTIF e risco, aparece na tela do processo e
  **nunca decide nem bloqueia** (decisão C5): a escolha continua do comprador, com
  justificativa. Os pesos vivem em `ScoreWeights`, uma linha só, editável pelo administrador
  — `MultiCriteriaScore.Padrao` é apenas o ponto de partida de quem nunca configurou. Três
  coisas andam juntas e não se separam: os pesos **somam 100** (`SCR-ERR-010`), a régua
  publicada na tela é a **mesma** que entra na conta — publicar uma e calcular por outra
  faria a explicação parecer conferida —, e **peso 0 desliga o critério** em vez de deixá-lo
  no denominador puxando o score para baixo. Critério **sem dado** também sai da conta:
  fornecedor novo não é punido por ser novo.

## Segurança que vale para o app inteiro

Não são regras de uma tela: valem para toda resposta e todo upload, e estão no
pipeline justamente para uma rota nova não nascer sem elas.

- **DOC-ERR-004 — o upload é conferido pelo conteúdo, não pelo que declara.** O
  `Content-Type` é escrito por quem envia; a lista de tipos aceitos, sozinha, só barra
  quem é honesto. `Domain/AssinaturaDeArquivo.cs` decide pelos primeiros bytes. Formato
  novo na lista de aceitos **precisa** entrar também na tabela de assinaturas — há teste
  que falha se um ficar sem o outro. Texto (CSV) não tem começo obrigatório: tem começo
  proibido (binário conhecido e `<`, que é como HTML, SVG e XML abrem).
- **Cabeçalhos de segurança em toda resposta** (`Infrastructure/Seguranca.cs`): CSP sem
  `unsafe-inline` em script — o `index.html` não tem script embutido, e é isso que faz
  XSS injetado não executar —, `nosniff`, `frame-ancestors 'none'`, `Referrer-Policy` e
  `Permissions-Policy`. Resposta de `/api` sai com `no-store`.
- **HTTPS atrás do proxy.** O Railway termina o TLS na borda e o contêiner recebe HTTP:
  o esquema verdadeiro vem no `X-Forwarded-Proto`. A decisão de redirecionar e de mandar
  HSTS acontece **antes** do `UseForwardedHeaders`, que consome esse cabeçalho — lendo
  depois dele, o valor já não existe. Sem o cabeçalho não se redireciona nada, que é o
  que evita laço em desenvolvimento e no healthcheck.
- **Dependência vulnerável trava o CI** no que vai para produção (`src/frontend` e o
  projeto .NET). O monorepo `platform/` não é implantado e fica fora do gate.

## Torre de Controle: o que é derivado, e de onde

A Torre não grava estado próprio — etapa, situação, atraso, faturamento e exceção
saem do que solicitação, cotação e pedido já dizem. Duas derivações valem registro
porque não são óbvias:

- **"Em faturamento" e "aguardando recebimento" são filas diferentes**, e o que as
  separa é a **nota fiscal**: O.C. emitida sem nenhuma NF significa que quem deve agir
  é o fornecedor; NF lançada e material não recebido passa a bola ao almoxarifado.
  Antes as duas viviam no mesmo número e o comprador não sabia para quem cobrar.
- **"Exceção" sai do que o sistema já grava como fora do padrão** — pedido cancelado,
  saldo encerrado sem entrega completa, material devolvido, e o fechamento sem O.C. do
  ERP justificado (PO-BR-011). O fluxo de exceção genérico do documento não existe;
  inventar um registro vazio daria um KPI que não conta nada. A regra vive em
  `TorreDeControleService.ExcecaoDe`, e a linha mostra **o motivo**, não só a marca.

**Aviso e contagem são coisas diferentes, e as duas ficam.** A Central de Avisos é
**derivada**: conta o que está aberto e o número muda sozinho quando o trabalho anda — serve
para "o que há para eu fazer agora". `UserNotice` é o outro lado: **fato datado, com dono e
com lido**, porque um contador não avisa que a SC *passou* a ser sua (quando você olha, ele
já é outro número). Cinco pontos emitem — SC enviada → gestores; atribuída → comprador;
Nível 1; Nível 2; aprovado → comprador. Regras que não se afrouxam: **um aviso tem um dono
só** (cada aprovador recebe o seu, senão marcar como lido apagaria o recado dos outros); a
`DedupeKey` impede o mesmo aviso de nascer duas vezes; e **nada aqui interrompe o fluxo** —
aviso não gravado nunca pode impedir uma aprovação, então a emissão entra na transação de
quem a chama e nunca lança. O **escalonamento** ao gestor é avaliado quando ele abre a caixa:
o projeto não tem agendador, e um relógio de servidor entregaria o mesmo recado com uma peça
a mais que pode falhar em silêncio.

**O prazo é por tipo de solicitação, e o tipo é cadastro.** `RequestType` dá identidade ao que
era texto livre em `NeedType` ("Tipo SC") — um campo que a API aceitava e **nenhuma tela
preenchia**, e que solto daria "EPI", "epi" e "E.P.I." como três tipos que nunca somam. O
código é a identidade e **não muda** depois de gravado (SCs já criadas o carregam); o nome se
corrige; tipo fora de uso se **inativa**, nunca se apaga. O `StageSla` ganhou `RequestType`
nulo = padrão, e o fallback é **por etapa**: um tipo que só aperta a aprovação define aquela e
**herda** o resto — mudar o padrão move junto quem herdou, e voltar a herdar **apaga a
exceção** em vez de copiar o número, que a congelaria. SC sem tipo, ou com tipo fora do
cadastro, cai no padrão — que é o que já valia antes de os tipos existirem.

**Cuidado com a palavra "pedido" (D7).** "Tipo de pedido" no vocabulário do usuário é o tipo
da **solicitação**; no do sistema, "pedido" é a O.C. O teste do menu pegou o rótulo errado — é
para isso que ele existe.

**O prazo é da etapa, e o relógio é o da espera.** `StageSla` guarda quanto tempo cada etapa
pode levar (editável pelo administrador; `PrazoDaEtapaService.Padrao` é só o ponto de partida)
e `Avaliar` julga contra o **mesmo número que a linha mostra** — medir contra a criação da SC
cobraria da aprovação o tempo que o item passou em cotação. Ele **mede e expõe, nunca
bloqueia**, como o Compliance Score: estourado aparece na linha, entra no filtro e conta no
card, mas não impede aprovar, cotar nem fechar. **Zero desliga** a cobrança da etapa, e a
atenção chega a 80% do prazo — avisar no dia do vencimento é avisar tarde para agir. "Atrasado"
e "prazo estourado" são **perguntas diferentes**: o primeiro é sobre a data prometida ao
solicitante, o segundo sobre o tempo da etapa.

**Cada card do topo abre exatamente a lista que ele contou.** Não é detalhe de tela: o card é
um número que promete uma lista, e as duas coisas precisam sair da mesma pergunta. Por isso
`PrecisaDeVoce` é contado por `AcaoDe` (a soma de etapas deixava de fora a exceção que volta ao
comprador) e `FiltroTorre.Invoicing` separa as duas filas do recebimento (as duas caíam no
mesmo filtro de etapa, e "Em faturamento: 3" abria dez linhas). Clicar troca o **recorte
inteiro** — resto de um filtro anterior faria a lista ser outra — e rola até a tabela, que fica
abaixo dos cards e das faixas.

**A situação diz a etapa; a espera diz de quem ela depende.** `EsperaDe` responde a outra
metade da pergunta — *aguardando aprovação de quem, e há quanto tempo* — a partir do que o
sistema já grava: os aprovadores do nível pendente no centro de custo, e os convidados sem
proposta na cotação (com o atraso de cada um). Duas coisas mantêm o número honesto: **cada
etapa conta pelo seu próprio relógio** (o Nível 2 conta desde a aprovação do Nível 1, não
desde a escolha do vencedor) e **etapa sem marca de entrada fica sem data**, em vez de usar a
criação da SC e contar como espera um tempo em que a etapa nem existia. Centro **sem aprovador
cadastrado** diz isso na linha: fila parada porque ninguém pode aprovar é o defeito que
precisa aparecer, não passar por demora normal.

A **ação da linha e a fila prioritária saem da mesma regra** (`AcaoDe`): a pergunta
"o que falta fazer aqui, e de quem é" é uma só. Fossem duas regras, o botão e a fila
discordariam no primeiro caso de canto e o comprador não confiaria em nenhum dos dois.
O rótulo vem do servidor; o destino é do navegador (`destinoDaAcao`), que é quem
conhece as rotas. **Exceção volta ao comprador em qualquer etapa.**

**A triagem mora dentro da Torre.** Atribuir e liberar responsável se faz na própria
Torre, porque a demanda que chega para o comprador *é* a etapa de Solicitação dela —
sair da tela para atribuir e voltar para acompanhar era o caminho longo para a mesma
coisa. As chamadas continuam sendo as de `/api/v1/triage`: a regra de quem pode receber
demanda vive num lugar só, no servidor.

**A atribuição é da SC, e a Torre é por item.** Marcar um item marca a solicitação
inteira, e a seleção é deduplicada por `requisitionId` — sem isso, uma SC de cinco itens
iria cinco vezes no mesmo lote. A tela diz isso em vez de deixar o comprador descobrir.

**A Torre é uma tela só, e a de compra não se divide em duas.** Ela já foi um subgrupo,
com a Torre e a triagem lado a lado, e era um erro: as duas listavam a mesma SC por
caminhos diferentes e o comprador tinha de escolher em qual acreditar. O que só existia
na outra veio para cá — o **tempo na fila** por faixa (0–2, 3–5, 6–10, +10 dias, contando
só quem ainda espera) e a **mudança de prioridade** na própria linha. O diálogo de
prioridade é compartilhado (`paginas/triagem/DialogoDePrioridade.tsx`): duplicá-lo faria
uma das telas aceitar urgência sem impacto, e a auditoria ficaria com metade da história.

A triagem de **material** não veio: é outro ciclo, com outras etapas e outro atendente.
Tem tela própria, no grupo Material, e lista só `MR`. O endpoint `/api/v1/triage` continua
servindo os dois tipos porque a Torre usa as mesmas chamadas de atribuição — o recorte é
de quem lê.

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
