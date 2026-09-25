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

- **RFQ-ERR-030** — segregação de funções, **no Nível 2**: o diretor não pode ser quem
  escolheu o fornecedor nem quem deu o Nível 1. **No Nível 1 não há segregação** — decisão
  da empresa (2026-09): o comprador (`PurchasingOfficer`) abre SC em **qualquer centro de
  custo**, cota, escolhe e dá o Nível 1 do próprio processo, sem depender da lista de
  aprovadores do centro (que continua valendo para os gestores). O que o separa da compra é
  a segunda alçada, que ele não dá. A fila do Nível 1 e a Central de Avisos contam a própria
  escolha; o impasse do caminho do processo só existe no Nível 2. **O diretor também
  solicita** (`Actor.CanCreate`, `podeCriarSc`): a SC dele segue o caminho de todas — o Nível 1
  é do comprador ou da lista do centro, e o Nível 2 ele mesmo dá, porque a segregação separa
  quem escolhe e quem aprova, não quem pede. Com centros vinculados no cadastro, solicita só
  deles (PR-ERR-021), como o solicitante. O mesmo vale para **material** ao almoxarifado
  (`MaterialRequisitionService.CanRequest`, `podePedirMaterial`); o módulo Material entra no
  padrão do diretor, e usuário já cadastrado precisa dele marcado em Usuários.
- **A compra que a própria área de compras pede não termina na diretoria.** Decisão da empresa
  (2026-09), em `Procurement/AlcadaDoComprador.cs`: a compra do **Gestor de Suprimentos**, que já
  deu o Nível 1, **não tem Nível 2** — a mesma decisão aprova o processo e o pedido nasce ali,
  pronto para a O.C.; a compra de um **comprador** tem como Nível 2 o **gestor responsável por
  ele** (`User.SupplyManagerId`), e não o diretor. Todo o resto segue a régua de sempre.
  Três coisas seguram a regra. **"Compra própria" vale só quando todas as SCs de origem são de
  quem deu o Nível 1** — bastasse uma, juntar a SC de um solicitante à do gestor no mesmo
  processo apagaria a segunda assinatura da compra alheia junto com a dele. **Sem responsável
  cadastrado cai no padrão**, que é crivo mais alto e não menor: travar a fila seria pior, e
  afrouxá-la em silêncio seria inaceitável. E o gestor **não herda** a 2ª alçada de centro sem
  lista só por ter entrado em `CanApproveAsDirector` — a alçada dele é a compra dos compradores
  dele, e `ImpedimentoNivel2Async` é quem separa os dois casos, para a fila da Central continuar
  sendo exatamente o que a pessoa decide.
  **A dispensa não inventa um aprovador**: `DirectorApprovedBy` fica **nulo**, e é por esse nulo
  que todo o resto a reconhece. Foi o que obrigou a acertar três leitores que assumiam a segunda
  assinatura — o funil de tempo de ciclo (contava a etapa só com `DirectorApprovedAt`, e sumiria
  com a compra dispensada em vez de medi-la), o PDF da O.C. (imprimia "Diretoria:  ()", que parece
  assinatura que faltou coletar) e o caminho do processo (mostrava o Nível 2 pendente para sempre,
  cobrando o que a regra não pede — agora a etapa é `dispensada`, que é diferente de pendente e de
  feita).
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
- **O pedido nasce na aprovação do Nível 2, sem O.C., e tudo o que vem depois vive numa
  tela só.** `CriarPedidosAsync` cria um pedido por fornecedor adjudicado no instante em que
  o diretor aprova (`PEDIDO_CRIADO`); a tela do pedido é uma linha do tempo em três passos —
  **1 O.C. → 2 Faturamento → 3 Entrega** — derivada do que o pedido já diz, sem estado próprio.
  O formulário de O.C. na tela do processo ficou só para o processo aprovado antes dessa
  regra (sem pedido nenhum) ou com fornecedor ainda sem pedido; `RegisterErpPurchaseOrderAsync`
  encontra o pedido pendente do fornecedor e aplica a mesma regra. O processo passa a
  `PoIssued` quando o **último** pedido dele tem O.C. ou observação (`OcDoErp.SincronizarProcessoAsync`).
- **A O.C. do ERP pode cobrir parte do pedido, e várias O.C.s convivem no mesmo pedido.**
  `PurchaseOrderErpDocument` é cada O.C. registrada, com o que ela cobre de cada item
  (`PurchaseOrderErpDocumentItem`); sem linhas, cobre tudo o que ainda falta — é o caso comum
  e o que toda chamada antiga significa. A soma nunca passa do pedido (`PO-ERR-059`, que diz
  quanto falta em vez de só recusar), item de outro pedido não entra, e pedido todo coberto
  não aceita outra. A **primeira** O.C. é a do cabeçalho (`erp_number`, `erp_issued_on`,
  `promised_date`): é o que todo relatório que já lia "tem O.C.?" continua lendo. O mesmo
  número de novo no mesmo pedido é correção de data, não repetição. `HasErpPending` diz se
  ainda há saldo sem O.C. **e** sem observação: a observação (PO-BR-011) fecha o restante e
  as O.C.s já registradas ficam. A regra vive em `Procurement/OcDoErp.cs`, chamada pelos dois
  caminhos.
- **IC-ERR-023** — EPI/EPC só circula com C.A. válido no par produto-fornecedor.
- **Tamanho é produto, e a grade é o cadastro dele de uma vez.** A bota 38 e a 39 têm código,
  preço e C.A. próprios — são compras diferentes —, então cada tamanho é um `CatalogItem`, e o
  que os mantém juntos é o `BaseCode`. `Catalog/Tamanhos.cs` é a convenção: código `12003-38`,
  descrição com `— Tam. 38`, e a **ordem da grade** (letra pela sequência de vestuário, número
  pelo valor, o resto no fim) — ordem alfabética daria "G, GG, M, P". `CriarGradeAsync` cadastra
  a grade inteira ou nenhuma: meia grade obrigaria a descobrir, tamanho a tamanho, o que
  faltou. Na SC, `ParaEscolhaAsync` devolve **um produto por linha com os tamanhos juntos** —
  e a busca traz a grade **inteira** mesmo quando o termo achou um tamanho só, porque achar o
  39 e esconder o 40 obrigaria a buscar de novo. A tela expande na volta: cada tamanho com
  quantidade vira um item da SC. O C.A. é cobrado **do tamanho pedido**, não do produto.
- **O setor é quem trabalha; o centro de custo é onde o dinheiro cai.** São dimensões
  diferentes e nenhuma substitui a outra: um setor atende vários centros e um centro é
  atendido por vários setores. `Sector` existe para o módulo de melhoria — o ciclo de um
  setor precisa aparecer para o colega do mesmo setor, e sem a entidade essa regra não tem
  como ser escrita. O código é identidade e **não muda** depois de gravado (pessoas e ciclos
  já o carregam); o nome se corrige; setor fora de uso se **inativa**, nunca se apaga. Setor
  inativo **não se vincula** a usuário (`IAM-ERR-023`): inativar é a forma de tirar um setor
  de circulação, e deixá-lo entrar pela porta do vínculo desfaria a decisão do cadastro. O
  vínculo (`User.SectorId`) vale para **qualquer papel** — diz onde a pessoa trabalha, não o
  que ela aprova —, ao contrário do gestor responsável, que só o comprador tem. Manter o
  cadastro é de quem mantém dimensão organizacional: o módulo é o `CENTROS_CUSTO` que já
  existe, e inventar um módulo novo para duas telas irmãs só daria mais uma caixa para o
  administrador esquecer de marcar.
- **O local de entrega tem dois cadastros, e o centro de custo é um deles.** A lista só trazia
  os almoxarifados do estoque, e por isso toda SC parecia ir para a Sede. Mas quem paga e quem
  recebe são perguntas diferentes: a SC é do "Novo Atacarejo PB" e o material desce no
  "Whirlpool PB". `CostCenter.ReceivesMaterial` é a resposta da segunda, e é **do centro, não da
  regional** — dois centros no mesmo endereço podem responder diferente, que é justamente o caso
  que fez a regra existir. `Domain/LocaisDeEntrega.cs` junta os dois cadastros num lugar só, e o
  `kind` de cada local é o que deixa a tela agrupar: sem ele "Whirlpool PB" e "Almoxarifado Sede"
  desceriam na mesma lista como se fossem a mesma coisa. São **duas consultas juntadas em
  memória** e não um `Concat` de LINQ, que sobre entidades diferentes não traduz no Npgsql. O que
  a SC grava continua sendo o texto `CÓDIGO — Nome`, igual para os dois tipos — nada a montante
  precisou mudar. O padrão é **não** receber: um centro que nunca recebeu material não vira
  endereço de entrega por causa de uma migration.
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
- **O sistema indica; quem decide é o comprador.** A grade marca, em toda célula, qual oferta
  é a mais barata do item e quanto as outras estão acima dela — e **não escolhe nada**: a grade
  nasce sem vencedor. A comparação é **dado da célula** (`menorPreco`, `acimaDoMenor` em
  `linhasDaGrade`), não efeito de apertar um botão: antes o menor preço só aparecia ao clicar
  em "melhor preço por item", e clicar **substituía todas** as escolhas já feitas — ver a
  informação custava a decisão. O desvio é **percentual e não seta**, porque a pergunta não é
  "é mais caro?" (a coluna do preço já responde) e sim "caro o suficiente para eu abrir mão do
  prazo deste aqui?" — 2% e 80% pedem decisões diferentes. Só entra na comparação quem **pode
  vencer**: destacar como menor preço uma oferta que a adjudicação vai recusar é apontar para
  porta fechada. Os dois atalhos são pontos de partida e nunca decisões — **preencher com o
  melhor preço** espalha a compra, **levar tudo** a concentra num fornecedor —, e `levarTudoDe`
  leva só o que aquele fornecedor **pode** levar (item não cotado ou impedido fica como estava)
  e **não toca na linha em divisão**, que tem decisão própria com quantidade digitada. O número
  no botão (`levar tudo (8)`) conta a história antes do clique: em doze itens, ele diz que este
  fornecedor não cotou quatro.
- **Item fora do catálogo cota, mas não é comprado (`RFQ-ERR-026`).** É a mesma lógica do
  fornecedor pré-cadastrado, que cota e só vence homologado: o item digitado entra na cotação e
  vira produto **antes da escolha do vencedor** — sem produto, a compra não tem código para a
  O.C., não entra no histórico de preço e não casa com contrato nem com C.A. O **orçamento** é a
  exceção: ele só levanta preço, fecha com item digitado e é cobrado quando **vira compra**. Quem
  cadastra é o **comprador**, na própria cotação (decisão da empresa, 2026-09): mandar o cadastro
  para uma fila travaria a escolha esperando outra pessoa. `CatalogService.CanRegisterProduct`
  (e `podeCadastrarProduto` na tela) é cadastrar e corrigir produto; importar, excluir e manter
  famílias continuam em `CanMaintain`, do gestor e do administrador — e `podeManterCatalogo`
  também guarda o menu de centros de custo e setores, por isso não foi ele que se alargou.
  `DefinirProdutoDoItemAsync` confere o vínculo **antes** de cadastrar o produto novo (produto não
  nasce à toa para um vínculo recusado), leva código, descrição e família ao item e à SC de origem,
  e mantém **quantidade e unidade** (as propostas foram dadas sobre elas). No orçamento já
  apresentado a **família fica**: a adjudicação por família está feita, e mudar a chave tiraria o
  item do lote que o fornecedor levou. Nos testes, `ItensDaCotacaoNoCatalogo` cadastra os itens
  digitados dos cenários que verificam outra coisa; a regra tem teste próprio sem ele.
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
  reativar é decisão do cadastro. A duplicata que **já existe** no banco não some sozinha: a
  lista marca a linha e o aviso do topo abre o recorte dela (`duplicados=true`), porque número
  sem lista, entre trezentos fornecedores, é caçada. A contagem é só dos **ativos** — inativar
  o repetido é o que resolve o caso, e contar o inativo deixaria a marca acesa depois do
  trabalho feito. O `totalDuplicados` vem do cadastro inteiro, não da página: contá-lo do que
  coube na tela diria "2" onde há sete. **Fundir dois cadastros não existe** — repontar
  cotação, O.C. e contrato é decisão que precisa de dono, não de um botão.
- **O contrato de parceria tem ficha, e a ficha tem memória.** `/contratos/:id` junta o que o
  comprador pergunta antes de renovar ou reajustar: os documentos, os produtos, **todas** as compras
  com o fornecedor (a de antes do contrato mostra quanto se pagava sem ele; cada linha diz se abate
  o saldo, pela mesma `NaVigencia` da lista) e a história. O contrato é regravado inteiro a cada
  edição, então sem registro o preço de antes da renovação sumia: `SupplierContractEvent` guarda,
  na mesma transação, o que mudou — `RetratoDoContrato` compara o antes e o depois e escreve a frase
  ("preço de Luva R$ 1,80 → R$ 1,95"), e salvar sem mudar nada **não** é acontecimento. Reajustes
  entram na mesma linha do tempo, e o contrato cadastrado antes do registro diz que a história está
  incompleta em vez de parecer começar na primeira alteração. O **contrato assinado e o aditivo**
  (`CONTRATO`, `ADITIVO`) vivem com as certidões, mas **não são certidão** (`IsCertificate`): a
  validade deles é a vigência, e o contrato vencer não restringe a homologação nem acende o aviso de
  certidão vencida.
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

**O ciclo de melhoria (PDCA) trata a causa; o plano de ação trata a tarefa.** `Melhoria/` é o
ciclo, e as ações dele são os **mesmos** `ActionItem`, pelos planos que apontam para ele
(`ActionPlan.CycleId`) — duplicar o modelo daria dois lugares para olhar o mesmo trabalho.
Cinco regras não se afrouxam:

- **Várias ferramentas convivem na mesma folha** (`CycleTool`), como no Trino Intelligence: o
  Ishikawa levanta, o Pareto prioriza e o 5W2H organiza a execução — obrigar a escolher uma
  faria a análise contar meia história. São **oito**: 5 Porquês, Ishikawa, Pareto, GUT,
  Brainstorming, 5W2H, Kaizen e Fluxograma. **Uma por tipo**: dois Paretos dariam duas
  respostas para "qual é a causa vital", e a tela mostraria a que carregasse primeiro. A causa
  levantada por uma e priorizada por outra aparece **uma vez** na leitura, e vital vence —
  contá-la duas vezes faria a folha parecer ter o dobro de frentes. E **nem toda ferramenta
  elege**: 5W2H, Kaizen, Fluxograma, Ishikawa e Brainstorming não priorizam, e tratá-las como
  se elegessem faria o sinal de "causa vital sem ação" acender por causa de uma lista de ideias.
- **O formulário só coleta; quem calcula é o servidor.** Cada ferramenta tem o seu
  (`paginas/melhoria/formularios.tsx`) — a escada dos 5 Porquês, a grade dos 6M, a tabela do
  Pareto, a do GUT com as notas de 1 a 5. O GUT mostra o produto ao digitar, porque multiplicar
  é aritmética; **não** mostra quem é vital, porque isso depende de ordenar e do piso de 27, e
  repetir a régua na tela daria dois donos para a mesma conta. Ferramenta sem formulário
  próprio cai no JSON cru, que é honesto — melhor que um campo que não abre.
- **Uma função normaliza a ferramenta de causa** (`FerramentaDeCausa.Normalizar`), e tela,
  leitura e relatório leem dela. Duas leituras do mesmo JSON dariam dois números para a mesma
  análise, e a tela que mostrasse o errado seria a que o usuário acreditou. **Pareto e GUT
  ordenam e calculam no servidor**: quem ordena decide qual é a causa vital, e isso não pode
  depender de em qual tela se olha. O Pareto marca como vital o item cujo acumulado **antes
  dele** é menor que 80% — pelo acumulado depois, o item que *cruza* os 80% ficaria de fora e
  a conta nunca fecharia. O GUT **não elege ninguém** com o maior produto abaixo de 27: o
  maior de uma lista fraca não é prioridade. A causa raiz dos 5 Porquês preenche a do ciclo se
  ninguém escreveu outra — é o mesmo dado, e digitá-lo duas vezes é convite a divergir.
- **A leitura automática é uma função** (`MotorDeLeitura.Ler`) e não inventa nada: diz em
  português o que os campos afirmam, e onde falta dado diz que falta — "0% de avanço" e
  "ninguém mediu ainda" são notícias diferentes. O sinal que dá valor ao módulo é **causa
  vital sem ação**; o elo com a ação é o texto da causa comparado sem acento, caixa nem
  pontuação, senão o sinal acenderia com a ação já escrita.
- **Prazo vencido não encerra nada.** Encerrar é veredito de uma pessoa e exige **três**
  coisas: se a meta foi atingida (`PDCA-ERR-043` — dito, não deduzido do indicador, que pode
  nem ter sido medido), o motivo em texto (`PDCA-ERR-040`) e, havendo ação em aberto, a
  confirmação explícita **com a lista do que sobrou** anexada ao motivo (`PDCA-ERR-041`). O
  ciclo **pode** fechar com pendência — às vezes é a decisão certa; o que não pode é fechar
  sem ninguém assumir isso, e a leitura marca a contradição em vez de escondê-la. Mudar a fase
  para "encerrado" pela edição comum é **recusado** (`PDCA-ERR-044`): era o clique na trilha
  que fechava o ciclo sem registrar quem decidiu. **Reabrir apaga o veredito** — quem, quando e
  por quê não valem mais.
- **Trocar o escopo leva o plano e as ações junto**, e só com destino **sem ambiguidade** (um centro, ou
  nenhum). As ações nasceram apontando para o centro antigo e ficariam lá, contando no painel
  do centro errado; com dois destinos não há para onde mandar cada uma, e escolher por elas
  seria inventar o dado (`PDCA-ERR-052`).

**O achado do Insights abre o ciclo, e o mesmo achado não abre dois.** `CausaDoAchadoService`
é o caminho *achado → causa → contramedida*, que é o do Trino Intelligence (indicador crítico →
análise de causa → plano) com os nomes daqui. O ciclo nasce em Plan com o problema e a evidência
já escritos e **os 5 Porquês começados** — deixar a folha em branco devolveria para quem lê o
trabalho que a tela já tinha feito. A `OriginKey` (`INSIGHT:código:assunto`, normalizado sem
acento nem caixa) é o que impede os ciclos gêmeos: clicar de novo devolve o que existe, e a tela
diz "já tinha o ciclo X" em vez de anunciar um novo que o usuário procuraria em vão. O plano da
contramedida nasce na **mesma transação** — ciclo sem o plano que ele prometeu seria pior que
nenhum dos dois.

**O achado que não passa abre o plano sozinho.** `GatilhoDePlanoService` é a última peça do
caminho do Trino Intelligence, onde o plano nasce quando um indicador fica crítico por vários
períodos. O que conta é **dia diferente, não visita**: contar chamadas faria dez atualizações da
tela na mesma tarde parecerem recorrência, e o gatilho abriria plano no primeiro F5. Três dias
(`DiasParaOGatilho`) é onde o achado deixa de ser acidente do dado de um dia. Só **severidade
alta** dispara — gatilho que dispara com tudo enche a lista de planos que ninguém pediu, e a
primeira coisa que se aprende é a ignorá-la. A `AutoKey` impede o segundo plano do mesmo achado.
O plano nasce **sem responsável**, de propósito: pendurá-lo em quem por acaso abriu a tela seria
dar trabalho a um sorteado — quem recebe o aviso é o **gestor**, que distribui. Como o
escalonamento de prazo e os avisos do ciclo, ele é avaliado **na leitura** do Insights, e lê a
**mesma lista** que a tela mostra (`ReportAsync` devolve as duas): recalcular daria dois
conjuntos de achados na mesma requisição, e o plano poderia nascer de um que a tela não mostrou.

**A visibilidade do ciclo é um predicado só** (`CicloDeMelhoriaService.VisiveisAsync`), que a
lista e a abertura consultam igual — e quem não enxerga recebe 404, não 403. Enxerga quem criou,
quem é dono, quem foi marcado em "quem mais acompanha", quem responde por alguma ação e quem é
do **setor** do ciclo; o Gestor de Suprimentos soma os centros vinculados a ele e o que a equipe
dele abriu. Três ausências são deliberadas: **centro vinculado não abre ciclo** para perfil
operacional (o ciclo de um centro pode tratar de assunto que não é de todo mundo), **"gestão"
não é público** (o plano da casa entrava na lista de qualquer um só por não ter centro), e a
visibilidade **sobe, não desce** — o ciclo do gestor só aparece para quem ele marcou. Quem
conduz (dono, criador, admin) é quem marca o acompanhamento, porque para os demais **essa lista
é o acesso**; e quem tem setor **não escolhe** o setor do ciclo: é o dele.

**O plano de ação é o projeto; a ação é o como.** `Acoes/` guarda o `ActionPlan` e, **dentro
dele**, os `ActionItem` — é o desenho do Trino Intelligence, que a operação já conhece. A ação
solta não contava a história: "trocar o filme do palete" não diz qual problema resolve, quanto
custa nem o que se aprendeu. Por isso **toda ação tem plano** (`PlanId` obrigatório), e o plano
carrega o problema, o porquê, os responsáveis (vários — um plano de verdade atravessa áreas),
os riscos, a causa raiz, as lições e o dinheiro.

- **Ação suspensa não está atrasada.** Ela está parada por decisão, e o relógio não corre contra
  quem foi mandado parar (`PlanoDeAcao.Atrasada`). Contá-la como atraso transformaria decisão da
  gestão em falha da equipe — e é o tipo de número que faz o time parar de confiar no painel
  inteiro. Ela continua **aberta**, só não corre. **Suspender e cancelar exigem motivo**
  (`AC-ERR-014`), por rota própria.
- **O responsável é chave estrangeira**, nunca texto. No Trino Intelligence ele é `String(200)`,
  e a própria documentação de lá lista isso como o erro a não repetir: dois "João Silva" e um
  "J. Silva" viram três pessoas, e "o que está pendente com o João" deixa de ter resposta.
- **A situação do plano é derivada, e a ordem importa**: cancelado vence tudo, concluído vence
  atrasado, atrasado vence "em andamento". Fosse outra ordem, um plano cancelado apareceria como
  atrasado e a lista cobraria trabalho que ninguém mais vai fazer.
- **Encerrar é decisão, não consequência do progresso.** Um plano a 100% continua ativo até
  alguém dizer que acabou, e um a 40% pode fechar — com quem decidiu, quando e a **evidência**.
  **Plano encerrado não se edita** (`AP-ERR-020`): deixá-lo aceitar edição faria o registro do
  encerramento mentir sobre o que estava fechado. Reabrir é do gestor ou do administrador, e
  apaga quem encerrou — mas **não** a evidência, que é o registro do que foi feito.
- **A severidade do risco é probabilidade × impacto**, calculada e nunca escolhida: dois riscos
  marcados "alto" que significam coisas diferentes é o que faz a matriz deixar de servir para
  priorizar.
- **Sem investimento o ROI é nulo, não zero.** Dividir por zero daria um número que parece ótimo
  e não quer dizer nada — e é o tipo de número que vai para a apresentação antes de alguém
  conferir. O **progresso dos itens** (média) e o **`Completion` digitado** convivem: respondem
  perguntas diferentes, e divergir é informação, não erro.
- O acesso é o módulo `PLANO_ACAO`, que o administrador concede, e ele **não entra em padrão de
  papel nenhum** de propósito: é ferramenta de qualquer área, não de um cargo.

**Filho de associação entra pelo `DbSet`, não só pela navegação.** As entidades deste projeto
nascem com `Id` preenchido, e o EF lê chave não vazia em filho anexado pela navegação de um pai
já rastreado como "já existe" — o registro ia ao banco como *update* de linha que nunca esteve
lá. A navegação se preenche sozinha pelo *fixup*: adicioná-la também deixaria o filho duas vezes
na resposta.

**`TabelaDeRotasTests` monta a própria tabela.** Grupo de rotas novo precisa ser mapeado **lá
também**, e não só no `Program.cs` — senão o inventário passa sem cobrir nada dele, que foi o que
aconteceu com as quatro rotas do plano de ação.

**A fila da Central é exatamente o que a pessoa pode decidir.** `ImpedimentoNivel1Async` e
`ImpedimentoNivel2Async` (em `QuotationService.Alcadas.cs`) são a régua única: a lista do
nível no centro, o gerente do centro sem lista, o diretor vinculado e a segregação. A decisão
e `PendingApprovalsAsync` perguntam à mesma função — uma fila com o que a pessoa não pode
aprovar é uma fila que mente, e foi o que o diretor viu: processos de todos os centros e
"RFQ-ERR-032" no botão. Quem quer ver a compra da empresa inteira tem o painel.

**O que o InMemory aceita, o Npgsql pode não traduzir.** A suíte roda em EF InMemory, que
executa qualquer LINQ; o Postgres não. Filtrar, ordenar ou comparar sobre um **record
projetado no meio da consulta** (`Compras(db).Where(x => ids.Contains(x.CatalogItemId))`)
passa nos testes e dá 500 em produção — foi a aprovação da diretoria, que monta o pedido e
pergunta o último preço pago. Regra: filtro e ordenação na **entidade**, projeção **por
último**, e `Contains` com **array**. `MigrationsTests.Consultas_dos_servicos_traduzem_no_Postgres_real`
roda as consultas dos caminhos de gravação num Postgres de verdade — consulta nova que só
roda ao gravar entra lá. E **nenhum `SaveChanges` no meio de uma decisão**: o de
`EnsureAwardsAsync` gravou o processo como aprovado sem pedido quando o passo seguinte
falhou, e a segunda tentativa respondeu "não está aguardando aprovação".

**Toda tela tem Manual e Suporte no topo, e os dois são daquela tela.** Os botões moram no
cabeçalho da casca (`layout/BarraDeAjuda.tsx`), e não em cada página, para nenhuma tela nova
nascer sem eles; a tela é descoberta pela rota (`manual/telas.ts`, `telaDaRota`): a rota exata do
menu vence, depois o detalhe (`/cotacoes/:id` tem manual próprio, não o da lista), depois a raiz.
**Tela nova precisa de manual** em `manual/conteudo.ts` — o teste `manual/telas.test.ts` lê as
rotas do `App.tsx` e falha com rota ou item de menu sem manual, e também com manual de tela que
não existe mais. O manual fica **no código**, não num cadastro: a tela e o texto que a explica
mudam no mesmo commit, e manual que descreve botão que não existe ensina errado com cara de
oficial. Ele cita o **código do erro** quando a regra tem um, porque é o que a pessoa leu na
mensagem.

**O chamado de suporte** (`Suporte/`, rotas `/api/v1/support`) grava a **tela de origem** sem a
pessoa digitar — "não consigo aprovar" quer dizer coisas diferentes na Central e no processo.
**Abrir não pede módulo nem papel**: pedir ajuda é a única porta que não pode depender de
permissão. **Atender** é do administrador e de quem recebeu o módulo `SUPORTE`, que, como o
plano de ação, não entra em padrão de papel nenhum. Quem não atende vê só os seus, e o chamado
alheio responde **404**. A situação diz **de quem é a vez** (`AGUARDANDO_SUPORTE`,
`AGUARDANDO_USUARIO`, `RESOLVIDO`), a mesma pergunta que a Torre responde: a mensagem do suporte
passa a vez a quem abriu, a de quem abriu a devolve. **Responder é assumir** (o primeiro atendente
que responde fica com o chamado), **o suporte não resolve em silêncio** (`CH-ERR-021`), quem
abriu encerra sem texto, e **responder o resolvido reabre** e apaga o veredito — "não resolveu"
dito dentro do chamado vale mais que um chamado novo sem a história. No próprio chamado, quem
atende fala como quem pediu, e a fila do cabeçalho não o conta. O print vai em `stored_document`
(`SUPPORT_TICKET`), e o download segue a mesma régua da leitura.

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

**O cockpit da TV é uma vista da Torre, não uma segunda conta.** `/cockpit` é tela de parede
da sala de suprimentos: sem menu, sem cabeçalho do app, fora do `AppLayout` — ninguém navega
nela e cada pixel é área de leitura. Etapa, atraso, exceção e prazo saem das **mesmas funções**
que a Torre do comprador usa (`ProcessStatus.Of`, `EtapaDe`, `ExcecaoDe`, `PrazoDaEtapaService`),
e há teste que compara os dois números: a TV fica na sala onde o comprador trabalha, e se a
parede dissesse "8 atrasados" com a Torre dele dizendo 6, as duas perderiam a autoridade no
mesmo instante. Por isso a conta **não** é agregação SQL pura — etapa e exceção não existem em
coluna, e agregá-las em SQL seria reimplementar as regras num segundo lugar.
Quatro decisões que a tela de parede impõe e a de mesa não: **o risco é união, não soma** (o item
atrasado *e* com prazo estourado é um só — somar daria mais itens em risco que o backlog inteiro);
**o gargalo da esteira é o item mais antigo da etapa**, não a média, que esconderia justamente o
que está parado há três dias; **OTIF sem entrega medida é nulo e aparece como traço**, porque 0%
diria "todo mundo atrasou"; e **a falha de uma atualização mantém os números na parede**, com um
aviso discreto, em vez de apagar o painel por causa de um timeout. A rotação de unidades abre pela
**visão geral** — quem passa e olha três segundos precisa ver a empresa, não a unidade da vez — e
com uma unidade só não gira, que mostraria o mesmo número duas vezes. `MetaSavingMensal` é
constante com dono declarado, como `PrazoDaEtapaService.Padrao`: é ponto de partida até alguém
configurar a da empresa.

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

**Cada papel começa o dia onde o trabalho dele está** (`paginaInicial` em `dominio/papeis.ts`):
quem aprova abre a Central; o solicitante abre as próprias SCs e não vê o painel do comprador;
o resto abre o painel. **A Central de Aprovação é um card de decisão, não uma lista.** O que
sustenta a decisão está no card — quem pediu e por quê, a escolha do comprador **contra a
proposta mais barata** (`comparacaoDaEscolha`, só propostas vigentes), a justificativa dele,
orçamento, compliance, contrato, quem deu o Nível 1 e há quanto tempo espera — e os três
botões decidem ali mesmo pela alçada da etapa (`alcadaDe`). O processo completo é o segundo
caminho. O resumo vem do servidor (`ResumoDaDecisao`, na rota `my-approvals`): os fatos da SC
se consolidam como no saving de orçamento (orçamento só quando **todas** as SCs informaram) e
a espera conta pelo relógio da alçada. "Suas decisões recentes" (`my-decisions`) sai dos
eventos do processo, o mesmo registro que a auditoria lê.

**O solicitante vê a SC como uma linha do tempo, não como um status.** `AcompanhamentoDaSc`
(backend) deriva, da SC, do processo e do pedido, seis passos — Enviada → Com o comprador →
Em cotação → Aprovação → Pedido fechado → Entregue — mais a frase que diz **com quem está,
desde quando e quando chega** ("Aguardando aprovação de Gustavo — Nível 1", "Pedido fechado
com Alfa, aguardando o faturamento. Chega até 28/09"). Sai da mesma consulta que a etiqueta
(`ProcessStatus`), por isso as duas nunca discordam. Devolvida e rejeitada param a linha e
mostram o motivo; devolvida devolve a bola ao solicitante (`precisaDoSolicitante`) e os botões
viram "Corrigir" e "Reenviar". Centro sem aprovador cadastrado é dito na frase, não escondido
em "aguardando ninguém". O formulário de nova SC pergunta o essencial primeiro (itens,
justificativa, centro, data, prioridade) e recolhe o resto em "Mais detalhes". **O produto se
escolhe buscando, não rolando**: o campo de sugestões com o acervo inteiro dentro virou um
seletor que pede família ou duas letras antes de consultar (`SeletorDeProduto`). **A busca é a
única porta do produto na SC**: o campo de texto na linha parecia uma segunda busca e deixava o
produto cadastrado entrar como texto solto. Clicar num resultado abre a **ficha**
(`FichaDoProduto` — foto, código, família, unidade, fornecedores com C.A.), e é dela que se usa o
produto; o item **fora do catálogo** sai da própria busca ("não achou?"), com o termo buscado como
descrição — pedir o que ninguém cadastrou continua possível, mas depois de procurar. As
**famílias** das telas de pedido (SC, lote, material) são as **ativas do cadastro**
(`listarFamilias`), não os nomes que aparecem nos produtos: a lista derivada mostrava família
inativa e escondia a ativa ainda sem produto. A **empresa** da SC é um dos CNPJs ativos do
cadastro (`PR-ERR-023`), gravada com o nome oficial: texto livre dava a mesma empresa escrita de
três jeitos, e a Torre e o cockpit, que agrupam por ela, contavam três. Sem nenhum CNPJ
**ativo** a regra não se aplica — a tela cai no nome do padrão da O.C., e a régua do servidor é
a mesma que a tela consegue enxergar (a primeira versão contava os inativos e recusava toda SC).

**A SC diz se é orçamento ou compra, e orçamento para antes da aprovação.** `FinalidadeDaSc` é
obrigatória na rota de criação (`PR-ERR-024`) e **sem valor pré-marcado** na tela — um "compra"
de fábrica faria todo orçamento esquecido chegar ao Nível 1. O serviço trata vazio como compra
(é a porta interna e o acervo antigo); a obrigação é da porta de quem cria. Corrigir a finalidade
vale **só antes de a SC entrar em cotação** (`PR-ERR-025`), para quem pediu no rascunho e para
quem conduz compra. Orçamento e compra **não se misturam** no mesmo processo (`RFQ-ERR-063`):
um processo ou para ou segue. O processo de SCs de orçamento nasce com `Quotation.IsBudget`, e a
escolha do vencedor o leva a `BudgetPresented` ("orçamento apresentado") **em vez** do Nível 1 —
avisa quem pediu e para. `ConverterOrcamentoEmCompraAsync` é a única saída para a aprovação
(`RFQ-ERR-064`), do comprador, com quem e quando; `IsBudget` **fica** verdadeiro, e é por ele que o
card da Central diz "nasceu como orçamento". Na Torre ele fica na etapa Cotação, com a ação
"aguardando decisão do solicitante" **fora da fila do comprador**, e o prazo da etapa **não
corre** (`DiasQueOPrazoCobra`): a vez é de quem pediu. O status novo é `9` no enum: o único lugar
que compara status por ordem (`CaminhoDoProcesso`, "cotação feita") responde certo para ele, e
comparação nova por ordem precisa lembrar que 9 não é "depois da O.C.".

**Excluir de verdade é só para o que nunca circulou.** Produto que entrou numa SC, cotação,
pedido, contrato, solicitação de material ou no estoque é histórico, e apagá-lo deixaria esses
registros apontando para nada (`IC-ERR-030`, que diz **onde** ele circulou); o caminho é
inativar. A família só sai vazia, **contando os inativos** (`IC-ERR-031`): a família é texto no
produto, e apagá-la com produto dentro os deixaria numa família que o cadastro não conhece — a
próxima edição deles seria recusada. A foto do produto é do catálogo, e **todo papel interno** a
baixa: quem pede precisa ver o que está pedindo.

**A diretoria tem página própria (`/diretoria`), e o relatório abre em três frases.** A Visão da
diretoria põe na ordem em que um diretor pergunta: cinco números com tendência (gasto, saving,
o que espera a minha aprovação, exceções, OTIF), o relatório em três frases, a fila de decisão
(que se decide na Central, não ali) e os achados do Insights. Três períodos em vez de dez
filtros. Para o diretor ela substitui o painel do comprador e o Insights no menu; o
administrador vê os três. Os Relatórios ficaram em cinco abas — Visão geral, Saving,
Fornecedores, Demanda e prazos, Exceções — com os mesmos catorze blocos numerados, a
explicação de cada um recolhida em "como é calculado" e bloco vazio em uma linha.
`resumoExecutivo` gera as três frases dos mesmos números dos blocos: a frase nunca diz uma coisa
e a tabela outra.

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

**O comprador da linha é quem conduziu a compra, não quem foi atribuído nem quem aprovou.**
`TorreDeControleService.CompradorDe` responde, nesta ordem: quem escolheu o vencedor, quem abriu
a cotação e, só antes de existir cotação, o responsável da triagem. A coluna caía antes na
atribuição e depois em quem **emitiu o pedido** — e o pedido nasce na aprovação do Nível 2, então
a Torre mostrava o gestor ou o diretor como comprador de uma compra que outra pessoa cotou.
Coluna, filtro (que por isso é **derivado**) e a produtividade do cockpit perguntam à mesma
função. A linha mostra o **nome** do centro de custo; o código fica na dica.

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

**Não dependa de Docker.** No ambiente de desenvolvimento deste projeto o daemon não
sobe, e o Postgres e o Redis já estão instalados nativamente — foi assumir o contrário
que fez sessões inteiras declararem o E2E e os testes da plataforma "impossíveis de
rodar aqui". Se `docker` falhar, o caminho não é desistir: é este.

```bash
# Postgres — os binários do servidor ficam fora do PATH, e o initdb recusa rodar
# como root: o cluster nasce sob o usuário `postgres`, que já existe.
PGBIN=/usr/lib/postgresql/16/bin
mkdir -p /var/lib/postgresql/tsdata && chown -R postgres:postgres /var/lib/postgresql
su postgres -c "$PGBIN/initdb -D /var/lib/postgresql/tsdata -U postgres --auth=trust -E UTF8"
su postgres -c "$PGBIN/pg_ctl -D /var/lib/postgresql/tsdata \
  -o '-p 55432 -c listen_addresses=127.0.0.1' -l /tmp/pg.log start"
psql -h 127.0.0.1 -p 55432 -U postgres -c "CREATE DATABASE trino_supply"

cd src/backend/Foundation/TrinoSupply.Foundation.Api
DATABASE_URL=postgresql://postgres@localhost:55432/trino_supply \
JWT_SECRET=um-segredo-local-de-32-caracteres-ou-mais \
ADMIN_EMAIL=admin@trinosupply.com.br ADMIN_PASSWORD='TrinoSupply@2026!' \
dotnet run -c Release --no-launch-profile --urls http://127.0.0.1:5099
```

`JWT_SECRET` é obrigatório fora de Development e precisa de 32 caracteres ou
mais — sem ele a aplicação aborta na inicialização, de propósito.

**`--no-launch-profile` não é enfeite.** O `Properties/launchSettings.json` vence o
`ASPNETCORE_URLS` do ambiente e sobe a API na porta dele; o Playwright então bate em
`127.0.0.1:5099` e não acha ninguém. Passar `--urls` sem desligar o perfil não resolve —
é o perfil que precisa sair da frente.

Com a API no ar, `npx playwright test` em `src/frontend` roda os E2E inteiros.

Para os testes da **plataforma** (`platform/`), que precisam de Redis e de um banco
próprio:

```bash
redis-server --port 6379 --daemonize yes --save ''
psql -h 127.0.0.1 -p 55432 -U postgres -c "CREATE DATABASE trino_ci"
```

O `--save ''` evita o `dump.rdb` aparecendo como arquivo não rastreado no diretório de
onde o Redis foi iniciado.

### Rodar o CI inteiro sem o GitHub Actions

Os cinco jobs do `ci.yml` são reproduzíveis aqui, e vale saber disso quando o Actions
estiver indisponível — o deploy do Railway sai do `main` por conta própria e **não**
passa pelo Actions, então o que falta nessa hora é a verificação, não a entrega. Além
dos comandos de "Verificação antes de entregar", o job de dependências é:

```bash
cd src/frontend && npm audit --omit=dev --audit-level=moderate
dotnet list src/backend/Foundation/TrinoSupply.Foundation.Api package \
  --vulnerable --include-transitive
```
