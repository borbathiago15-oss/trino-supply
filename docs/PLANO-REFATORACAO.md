# Plano de Refatoração — Trino Supply

Auditoria de leitura feita em 2026-09-06 sobre `main` (`0ad64f7`), sem alterar
código. Este documento é o insumo para os PRs seguintes: cada item vira um PR,
na ordem de prioridade, com o ritual de sempre (build → testes → PR → CI →
merge → conferência em produção).

Os três eixos combinados: **melhor interface, melhor segurança, melhor
inteligência.**

---

## 1. Resumo executivo

O sistema está mais saudável do que a impressão inicial sugere. A segurança de
acesso é real — não encontrei nenhuma rota aberta por engano, nem leitura de
documento alheio. O que existe é **risco estrutural**: as proteções são feitas
uma a uma, à mão, em 120 endpoints, e nada impede que o 121º nasça sem elas.

O achado mais sério não é de segurança e sim de **correção**: o sistema trunca
listas silenciosamente. Todas as consultas de lista têm um teto fixo
(`Take(100)`, `Take(200)`, `Take(500)`) e **não existe paginação em lugar
nenhum**. Hoje, com pouco volume, ninguém percebe. Quando o volume crescer,
processos vão sumir de filas de aprovação sem aviso.

### Números medidos

| | |
|---|---|
| Endpoints | 120, sendo 119 autenticados e 1 público (`/health`, correto) |
| `Program.cs` | 2.797 linhas, todos os 120 endpoints |
| `QuotationService.cs` | 1.108 linhas |
| Telas React | 27, cada uma com seu próprio tratamento de carregando/erro |
| Paginação na API | **nenhuma** |
| Regras no motor de insights | 4 → **7** |
| Tabelas | 32 em 3 schemas, referências entre schemas sem FK (deliberado) |

---

## 2. Segurança — o que a auditoria encontrou

### Não encontrei

Verifiquei e **não** encontrei: rota aberta por esquecimento, segredo no
código, leitura de documento de outro usuário, escalonamento de papel pela API,
ou o token do portal do fornecedor alcançando dado interno.

O `/api/v1/documents/{id}` tem controle de dono correto, inclusive o caso do
fornecedor. O `GET /purchase-requisitions/{id}` passa o ator ao serviço, que
decide o que aquele ator enxerga. Os seis endpoints de analytics checam papel e
módulo.

### 🟠 SEC-A · A proteção é por convenção, não por estrutura

Dez grupos de rota usam `AddEndpointFilter(RequireModules(...))` ou
`RejectSupplierRole()`. O grupo `analytics` **não usa nenhum** — os seis
endpoints dele repetem a checagem à mão, dentro do handler. Todos acertam hoje.
O problema é o amanhã: nada obriga o próximo endpoint a lembrar.

**Proposta:** filtro padrão no grupo `analytics`, e um teste que falha se
alguma rota `/api` nascer sem autorização declarada. Vira uma rede, não uma
lembrança.

**Entregue.** O grupo `analytics` recebeu `RejectSupplierRole()` e um
`RequireModules(...)` com a união dos módulos que os seis endpoints já exigiam
— um piso, com cada handler mantendo a checagem específica dele.

**E a conferência passou a ser sobre a tabela de rotas em execução.** Depois do
ARQ-A cada módulo expõe o seu `Map…`, então `TabelaDeRotasTests` monta a tabela
chamando todos eles num app vazio — sem migration, sem banco, sem servidor — e
confere o que o ASP.NET **registrou**, não o que o texto sugere: toda rota
`/api` exige autenticação; as únicas públicas são as quatro portas de entrada,
cada uma com o motivo escrito; a gestão de usuários só abre para o
administrador; e o inventário bate com o arquivo versionado.

Duas coisas só apareceram quando o teste passou a ler a tabela de verdade:
`/api/v1/auth/logout` é público (é seguro — revoga o refresh token de quem o
apresenta, e exigir token válido impediria sair com o acesso já expirado), e
`/api/v1/users/pickers` não pertence ao grupo de usuários, é o seletor de
responsável das telas de cadastro. As duas estão registradas com a razão.

Sobrou em `RotasProtegidasTests` uma invariante que os metadados não mostram: os
filtros de grupo são embrulhados no delegate e não viram metadado, então a
conferência de `RejectSupplierRole` / `RequireModules` continua sendo sobre o
texto da fonte.

### 🟡 SEC-B · Rate limiting só na autenticação

Existiam só as políticas `auth` e `auth-refresh`. O resto da API não tinha
limite — inclusive os endpoints de upload e os de relatório, que são caros.

**Entregue.** Duas políticas novas, ambas **por usuário e não por IP**, para o
escritório inteiro atrás do mesmo IP não dividir a mesma cota: `upload`
(30/min, nos oito endpoints que aceitam arquivo) e `relatorio` (120/min, no
grupo `analytics`).

As cotas são folgadas de propósito. O que se quer barrar é a repetição
automática, não o uso humano — apertar até encostar no uso normal troca uma
proteção contra abuso por uma tela que quebra na mão de quem trabalha. A
primeira calibragem, em 60/min, foi afrouxada depois de ver a suíte E2E inteira
consumir a cota.

### 🟡 SEC-C · Upload sem limite declarado de tamanho e tipo

Oito endpoints de anexo gravam `bytea` no banco. Fui conferir os três pontos:

- **limite de bytes:** existe, 10 MB, em todos ✅
- **tipos aceitos:** todos checam ✅ — a lista comum em `StoredDocument`, e a
  foto do produto com a sua própria (PNG/JPG/WEBP). O download serve com
  `Content-Disposition: attachment` e a lista não tem HTML nem SVG, então não há
  caminho para script guardado
- **arquivo grande:** era o furo. O servidor lia o arquivo **inteiro** para só
  então a aplicação recusar pelo tamanho — 30 MB de banda e memória gastos antes
  de dizer "não"

**Entregue.** Os oito endpoints declaram um teto de requisição de 12 MB
(`IRequestSizeLimitMetadata`), acima dos 10 MB da regra para o arquivo legítimo
continuar recebendo a mensagem da aplicação em vez de um 413 seco. Conferido
contra a API: 15 MB devolve **413** sem ler o corpo; 1 MB com tipo errado
devolve **DOC-ERR-003**, que é a regra da aplicação falando.

### 🟢 SEC-D · Aviso de build não resolvido — **entregue**

Eram dois, e os dois saíram: o `CS9113` do parâmetro `inventory` injetado sem
uso no `MaterialRequisitionService`, e o `EF1002` do `SqlQueryRaw` com string
interpolada no `QuotationService` — nada ali vinha de fora, mas escrever os
dois literais em vez de interpolar o nome da sequência tira o aviso em vez de
suprimi-lo. **O build fica em zero avisos**, que é o que faz o próximo aviso
ser notado.

---

## 3. Correção e confiabilidade — o achado principal

### 🔴 COR-A · Listas truncadas em silêncio, sem paginação

| Consulta | Teto na auditoria | Hoje |
|---|---|---|
| `RequisitionService.ListAsync` (Meus Pedidos) | 100 | página + busca + `total` |
| `QuotationService.ListAsync` (Processos) | 200 | página + busca + `total` |
| `PurchaseOrderService.ListAsync` (Pedidos) | 100 | página + busca + `total` (#90) |
| `SupplierService` | 500 | página + busca + `total` (#90) |
| `QuotationService` fila de aprovação | 200 | filtra no banco, sem teto (#89) |
| `RequisitionService` fila de aprovação | 100 | pendente — fila do fluxo antigo, conjunto que só diminui |
| `PurchaseOrderService` SCs aguardando pedido | 100 | pendente |
| `TriageService` | 200 | pendente |

Nenhuma dessas rotas aceita página, deslocamento ou cursor. Duas consequências
concretas:

1. **`/my-approvals` perde trabalho.** O endpoint chama `ListAsync()` — que já
   corta em 200 — e **filtra em memória** quem aprova o quê. Passando de 200
   cotações, um processo aguardando aprovação simplesmente não aparece na fila
   do aprovador. Ninguém é avisado.
2. **A busca mente.** `PedidosLista` e `Fornecedores` filtram no navegador,
   sobre a lista já truncada. Procurar um pedido antigo devolve *"Nenhum pedido
   corresponde ao filtro"* — que é falso: o pedido existe, só não veio.

**Proposta:** paginação na API, começando pelas quatro listas que o usuário
mais usa, e busca no servidor nas telas que filtram no navegador. A interface
muda o mínimo: o mesmo campo de busca, consultando o servidor.

**Entregue nas quatro listas principais.** Cada uma devolve `{items, total}`,
com busca e filtro de situação feitos no banco (`ILike`, sem diferenciar
maiúsculas), teto de página de 500 e "Carregar mais" na tela. O `total` é o que
permite dizer *"Mostrando 50 de 640"* em vez de deixar a lista terminar sem
explicação.

A busca por `ILike` não roda no provedor em memória dos testes unitários, então
o que os testes cobrem é a paginação e o `total`; os caminhos de busca foram
conferidos endpoint a endpoint contra um Postgres real. As três consultas
restantes na tabela alimentam telas de fila, não de procura, e ficam para
depois — a mais sensível delas, a fila de aprovação de cotação, já foi
corrigida em #89.

---

## 4. Arquitetura

### 🟠 ARQ-A · `Program.cs` com 2.797 linhas e 120 endpoints

Um arquivo concentrava a definição de todos os endpoints. Crescia a cada tela
nova e não havia como dois trabalhos mexerem em módulos diferentes sem se
cruzarem.

**Entregue.** O `Program.cs` ficou com o que é de fato inicialização —
configuração, migração, seed, o middleware da senha provisória, o `/health` e
a autenticação — e passa a **chamar um `Map…` por módulo**:

| Arquivo | Linhas | O que leva |
|---|---|---|
| `Program.cs` | 401 | configuração, pipeline, `/health`, autenticação e as chamadas dos módulos |
| `Rotas/CotacaoRotas.cs` | 501 | RFQ-001 e o Portal do Fornecedor |
| `Rotas/SolicitacaoRotas.cs` | 308 | PR-001 e os anexos da SC |
| `Rotas/EstoqueRotas.cs` | 288 | MMS-003/004/005 |
| `Rotas/CatalogoRotas.cs` | 276 | MMS-002, famílias e locais de entrega |
| `Rotas/AvisoRotas.cs` | 257 | a central de avisos |
| `Rotas/PedidoRotas.cs` | 253 | PO-001, anexos de O.C./NF e o PDF |
| `Rotas/CadastroRotas.cs` | 220 | centros de custo, triagem e empresas |
| `Rotas/FornecedorRotas.cs` | 200 | SUP-001 |
| `Rotas/AnalyticsRotas.cs` | 118 | os seis dashboards |
| `Rotas/DocumentoRotas.cs` | 80 | download autorizado de anexo |
| `Rotas/UsuarioRotas.cs` | 65 | gestão de usuários |
| `Rotas/Api.cs` | 108 | envelope, leitura do token e os filtros de grupo |
| `Rotas/Vistas.cs` | 58 | vistas de resposta de mais de um módulo |
| `Rotas/Anexos.cs` | 41 | gravação de anexo, com o limite e os tipos aceitos |

O que destravou o recorte foi tirar os helpers de dentro do `Program.cs`: eram
funções locais, alcançáveis só de lá. Viraram `Rotas/Api.cs`, importado com
`using static`, então **os pontos de chamada continuam escritos igual**.

A rede que tornou o corte seguro é o inventário em `fixtures/rotas-da-api.txt`:
as 116 rotas `/api`, conferidas a cada build. Nenhum dos oito cortes alterou o
inventário — é isso que sustenta a afirmação de que nada se perdeu no caminho.

**O que isto destrava:** com cada módulo expondo o seu `Map…`, dá para montar a
tabela de rotas num teste sem subir o banco, e trocar a conferência de texto do
SEC-A pela versão em execução.

### 🟢 ARQ-B · `QuotationService.cs` com 1.222 linhas — **entregue**

Concentrava abertura, convite, proposta, negociação, adjudicação, alçadas e O.C.
num arquivo só. Virou cinco arquivos da **mesma classe** (`partial`), cortados
pelas etapas do processo, sem uma linha de chamada mudando de lugar:

| Arquivo | Linhas | O que responde |
|---|---:|---|
| `QuotationService.cs` | 272 | capacidades por papel, consultas, fila e infra |
| `QuotationService.Cotacao.cs` | 342 | abertura, convite, propostas, negociação |
| `QuotationService.Adjudicacao.cs` | 254 | escolha do vencedor, mapa por família, rateio |
| `QuotationService.Alcadas.cs` | 158 | Nível 1, Nível 2 e a segregação de funções |
| `QuotationService.OrdemDeCompra.cs` | 254 | registro da O.C. do ERP, contrato, cancelamento |

`partial` foi escolha deliberada: separar em classes distintas obrigaria a
injetar uma na outra e a inventar fronteiras que o domínio não tem — as etapas
compartilham `TransitionAsync`, `AddEvent` e o mesmo agregado. O corte é para
achar o método, não para desacoplar o que não é acoplável.

**A rede desta vez foi mecânica:** remontei os cinco arquivos na ordem original
e comparei com a versão anterior — idênticos, linha a linha. Só então build,
217 testes e E2E.

### 🟢 ARQ-C · Referências entre schemas sem FK

Deliberado, para desacoplar os módulos, e já documentado. Fica como está — o
`scripts/verificar-banco.sql` é a contrapartida.

---

## 5. Interface

### 🟠 INT-A · 27 telas, 27 tratamentos de carregando/erro — **premissa corrigida**

A auditoria dizia que cada tela repetia o estado "com variações" e propunha um
componente `<Conteudo>` para padronizar. **Fui conferir antes de mexer em 18
arquivos, e a premissa não se sustentou.**

O formato é o mesmo em todas, na mesma ordem:

```tsx
{erro && <Erro>{erro}</Erro>}
{carregando && !dados && <Carregando />}
{dados && !lista.length && <Vazio>…</Vazio>}
```

O que varia é a mensagem de vazio — e ela **deve** variar: "Nenhum pedido de
compra ainda" e "Nenhum fornecedor com atividade no período" dizem coisas
diferentes. Extrair um componente economizaria duas linhas por tela e cobraria
uma indireção justamente onde a mensagem específica é o que ajuda quem lê.

**O defeito real era outro, e estava escondido pela contagem.** Uma tela — a de
Usuários — não tinha estado vazio nenhum: com a lista vazia, o painel mostrava
o título e mais nada, e quem chegava ali não sabia se estava carregando,
quebrado ou realmente vazio. Corrigido, com teste.

Fica a lição sobre a própria auditoria: contar repetição encontra padrão, não
defeito. O que faltava não era uniformidade — era uma tela que não seguia o
padrão que as outras dezessete seguiam.

### 🟢 INT-B · `ProcessoDetalhe.tsx` com 455 linhas — **entregue**

A maior tela do sistema: convite, mapa, propostas, negociação, aprovação e O.C.
Foi para **254 linhas**, com três painéis saindo para arquivos vizinhos:

- `CabecalhoDoProcesso.tsx` (78) — identificação, próximo passo e itens. Só lê.
- `PainelDeConvidados.tsx` (111) — convidados, o convite e o texto copiado.
- `PainelDeNegociacao.tsx` (104) — o ganho apurado e o formulário que o registra.

O critério do corte foi **de quem é o estado**. Os dois painéis com formulário
levaram junto o `useState` que só eles usavam — e o de convidados levou também
a leitura dos fornecedores do cadastro, que a tela carregava para uso de um
`<select>` só. O que sobrou em `ProcessoDetalhe` é o que pertence à tela: a
leitura do processo, a régua de ações por papel e etapa, e os diálogos de
decisão.

Nenhuma mudança de layout, de rota ou de comportamento; os testes existentes
passaram sem alteração, exceto o `import` de `textoDoConvite`, que mudou de
arquivo junto com a função.

### 🟢 INT-C · Regras que a tela ainda não antecipa — **concluído**

O trabalho de #85 e #88 cobriu segregação de funções, EPI sem C.A. e O.C. sem
número. A varredura seguinte listou os **160 códigos de erro** do backend e
separou os que são regra de negócio dos que são autorização (`-900`, já
resolvidos pela visibilidade do menu) ou 404.

**O que a varredura achou de pior — a escolha do fornecedor.** A tela de
seleção listava *todas* as propostas vigentes com um botão de rádio. O servidor
recusa três casos que a tela não mostrava:

| Código | Regra | O que a tela fazia |
|---|---|---|
| `RFQ-ERR-024` | só leva a família quem cotou a **família inteira** | oferecia quem cotou **um** item dela |
| `SUP-ERR-030` | só fornecedor **homologado** vence | oferecia prospect, em homologação e restrito |
| `RFQ-ERR-040` | fornecedor **inativo** não vence | oferecia igual |

Nos três, o comprador marcava o fornecedor, preenchia a justificativa
obrigatória e só então tomava o erro — e a justificativa se perdia.

**O achado incômodo.** O backend já tinha o endpoint que responde exatamente a
pergunta da tela — `GET /quotations/{id}/family-map`, "quem pode levar cada
família" —, e **nenhuma tela o consumia**. O React refazia a conta por conta
própria, com `some` onde a regra é `every`. A correção foi ligar a tela ao
endpoint que já existia, e completar o endpoint com a situação do fornecedor no
cadastro (`homologation`, `active`, `canWin`).

Entregue em #107: `FamilyMapAsync` no serviço, `canWin` na oferta, o "mais
barato" recalculado entre os que podem vencer (destacar como melhor oferta
quem o servidor vai recusar é a mesma armadilha), e as duas telas de escolha
— vencedor único e adjudicação por família — dizendo o impedimento na linha.
Falha na leitura do mapa não esconde ninguém: sem ele, quem barra é a API.

**Parte 2, entregue em #108 — administração e data da SC.**

`IAM-ERR-015/016`: a tela de Usuários oferecia "Inativar" no próprio usuário e
no único administrador ativo. Os dois só falhavam no clique, e o segundo é o
mais perigoso: quem tentasse e não lesse o erro poderia achar que ficou sem
administração. Agora a ação aparece **desabilitada e com o motivo** — e a
edição do único administrador avisa que trocar o papel dele é recusado, sem
travar o resto do formulário, que continua editável.

`PR-ERR-050`: a data de necessidade no passado só era recusada no **envio**, não
na criação. Nos dois formulários de criação o campo passou a ter `min` de hoje.
Em "Meus Pedidos" a escolha foi outra, de propósito: ali a data pode ter vindo
de um rascunho antigo, e travar o campo impediria de salvar qualquer outra
correção — então a tela **avisa** que com aquela data o envio é recusado.

**Parte 3, entregue em #110 — o resto da varredura, e uma correção da própria lista.**

`PO-ERR-056` **não era um caso**: a tela de pedidos já recusa antes de chamar a
API (`"Informe o que chegou."`). Eu tinha listado sem conferir o código —
mesmo erro do INT-A, em escala menor: **listar um código de erro não é o mesmo
que constatar que a tela não o antecipa.**

`IV-ERR-010` era, e era o pior dos três. Item inativado no catálogo depois da
emissão do pedido não pode dar entrada em estoque; o almoxarife preenchia a
entrega inteira e o servidor recusava tudo. Agora a leitura do pedido traz
`inactiveCatalogCodes` e a linha do item diz o que houve — com o cuidado de
barrar **só a entrada**: a devolução ao fornecedor não mexe em estoque e
continua aberta, e os demais itens do pedido são recebidos normalmente.

`SUP-ERR-020` era o mais simples: o fim da vigência do contrato agora tem `min`
no início. Vigência que termina antes de começar deixou de ser possível.

### 🟠 INT-D · O menu é um mapa de módulos, e o processo não anda por módulos

Este item entrou por observação sua: para o Master e para o Administrador o
menu é confuso — "ele entra em menu X e depois, para a sequência, vai no Y".

**O que medi.** O menu tem 6 grupos e, para quem enxerga tudo, 19 telas. O
ciclo completo de uma compra tem 9 passos. Estes são os passos e o grupo em
que cada um mora hoje:

| # | Passo | Grupo no menu |
|---|---|---|
| 1 | Criar a SC | Solicitações de Compra |
| 2 | Aprovar a SC | Solicitações de Compra |
| 3 | Triar e atribuir a demanda | Compras |
| 4 | Abrir a cotação | Compras › Cotações |
| 5 | Receber propostas e escolher o vencedor | Compras › Cotações |
| 6 | **Aprovar Nível 1 e Nível 2** | **Solicitações de Compra** |
| 7 | Registrar a O.C. do ERP | Compras |
| 8 | Acompanhar pedido, NF e entrega | Compras |
| 9 | Receber o material | Estoque |

A sequência de grupos é **1 → 1 → 4 → 4 → 4 → 1 → 4 → 4 → 3**. O passo 6 é o
salto que dói: quem acabou de escolher o fornecedor dentro de "Compras" precisa
voltar a "Solicitações de Compra" para aprovar. E como o menu lateral é um
acordeão que mantém **um grupo aberto por vez**, esse salto custa fechar
"Compras", abrir "Solicitações de Compra" e achar o item — sem nada na tela
dizendo que era para ir ali.

**A Central de Aprovação está arquivada no lugar errado.** Ela decide três
fluxos — SC, requisição de material e aprovação de cotação — mas mora dentro de
um deles. É uma caixa de entrada transversal guardada dentro de uma das três
caixas que ela atende.

**Dois defeitos de estado no acordeão** (`layout/Sidebar.tsx`):

- `grupoAberto` é inicializado com `useState` e nunca mais acompanha a rota.
  Navegando por link — inclusive pelos links da Central de Avisos — você chega
  na tela com o grupo dela fechado e outro grupo aberto. O menu deixa de
  responder "onde eu estou".
- `grupoAtual` procura o grupo com `!ehSubgrupo(i)`, então as telas que vivem
  dentro de um subgrupo (Inclusão de SC, Abrir Cotação, Processos de Cotação)
  **nunca** abrem o grupo, nem no carregamento direto da URL.

**O vocabulário colide.** O Master vê ao mesmo tempo:

| Rótulo | O que é de verdade |
|---|---|
| Meus Pedidos | minhas **solicitações de compra** |
| Pedidos de Compra | os **pedidos/O.C.** com o fornecedor |
| Minhas Solicitações | minhas requisições de **material** |
| Gestão de Solicitações | **triagem** das demandas de compra |

"Pedido" significa SC num item e O.C. no outro; "Solicitação" significa três
coisas. Para quem só usa um fluxo isso passa; para o Master, que vê os quatro
rótulos na mesma barra, é ruído.

**Uma inconsistência menor de permissão:** `Pedidos de Compra` está com
`mostrar: sempre`, enquanto o domínio tem `podeVerPedidos`. Hoje o módulo
`COMPRAS` segura a porta, mas quem receber o módulo sem papel de compra vê o
item e leva 403 da API.

#### Oportunidades

| Id | Oportunidade | Custo | Resolve |
|---|---|---|---|
| **D1** | **Trilha do processo no painel** — uma faixa com os 9 passos, cada um com a contagem do que está parado ali e link para a tela. É a resposta literal a "qual a sequência" e usa os avisos que a API já devolve. | médio | a queixa |
| **D2** | **Próximo passo na própria tela** — ao escolher o vencedor, link para a aprovação; ao dar o Nível 2, link para registrar a O.C.; ao registrar a O.C., link para o pedido. O usuário atravessa o zigue-zague sem passar pelo menu. | baixo | a queixa |
| **D3** | **Central de Aprovação para o topo**, fora de "Solicitações de Compra" — ela atende três fluxos, não um. | baixo | passo 6 |
| **D4** | **Contadores no menu** ao lado de Central de Aprovação, Gestão de Solicitações e Fila de Atendimento, com o mesmo número dos avisos. O menu passa a dizer onde há trabalho. | baixo | orientação |
| **D5** | **Corrigir o acordeão**: abrir o grupo da rota atual, inclusive para telas dentro de subgrupo, e acompanhar a navegação. | baixo | defeito |
| **D6** | **Ordenar os grupos na sequência do processo**: Solicitações de Compra → Compras → Material → Estoque → Cadastros. Hoje Estoque vem antes de Compras, contra o fluxo. | baixo | leitura |
| **D7** | **Separar o vocabulário**: "Meus Pedidos" → "Minhas Solicitações de Compra"; "Minhas Solicitações" → "Minhas Requisições de Material". | baixo | ruído |
| **D8** | Trocar `mostrar: sempre` de Pedidos de Compra por `podeVerPedidos`. | trivial | coerência |

**Recomendação:** D5 + D3 + D6 + D8 primeiro — são correção e arrumação, sem
tela nova e sem decisão sua. Depois D2, que é o que de fato tira o usuário do
menu. D1 e D4 em seguida. D7 fica por último porque mexe em rótulo que a sua
equipe já decorou: é a única que precisa da sua palavra.


---

## 6. Inteligência

Hoje o motor tem **quatro regras**, todas com evidência e severidade:

| Código | O que detecta |
|---|---|
| INS-01 | Sobrepreço |
| INS-02 | Fracionamento (compras divididas para escapar de alçada) |
| INS-03 | Excesso de emergenciais |
| INS-04 | Concentração em fornecedor |

É uma base boa e o formato `Insight(Code, Kind, Severity, Title, Evidence)` já
é o certo. O que falta é o salto de **descritivo para prescritivo**.

### INTEL-A · Insight que diz o que fazer — **entregue**

Cada achado passa a carregar, além da evidência, **a providência e a tela onde
se age**. O destino usa o mesmo vocabulário dos avisos (`buy-orders`, `triage`,
`suppliers`, `quotations`, `scorecard`), então a interface resolve para a rota
com o `enderecoDoId` que já existia — nada de mapa novo.

Descrever um problema sem dizer o que fazer devolve o trabalho para quem lê. Um
teste do serviço exige `Action` em todo achado, e o destino, quando existe,
precisa ser uma tela conhecida.

### INTEL-B · Regras novas com o dado que já existe — **entregue**

Três, todas sobre dado que já estava no banco:

| Código | O que encontra | Por que importa |
|---|---|---|
| **INS-05** | 3+ compras fechadas sem O.C. do ERP no período | a observação é a exceção prevista no PO-BR-011; virando rotina, o relatório de O.C. deixa de descrever a operação |
| **INS-06** | fornecedor que atrasou 3+ vezes, com o percentual sobre as entregas medidas | o OTIF já era medido por pedido; aqui vira padrão de comportamento, que é o que muda uma decisão |
| **INS-07** | processos decididos com uma proposta só | não é irregular, mas sem concorrência o preço não tem contra o que ser medido |

### INTEL-C · O insight chega até quem decide — **entregue**

Achado de severidade **alta** entra na Central de Avisos como qualquer outro
aviso, com o texto do achado e a providência, e leva para a tela onde se age.
Quem decide deixa de precisar abrir Insights & Executivo para descobrir que
existe um problema.

Um cuidado: o achado **não entra no contador do menu**. Ele é constatação, não
fila — contá-lo faria o menu dizer "Pedidos 3" com a lista de pedidos vazia,
que é a mesma incoerência corrigida quando o aviso de acompanhamento saiu da
contagem. O aviso carrega `counts: false`, e a regra está escrita nos dois
lados.

---

## 7. Banco de dados

Estado bom. 37 migrations aplicadas, sem desvio de modelo, tipos consistentes
(todos os 56 timestamps são `timestamptz`, os 44 monetários são
`numeric(18,4)`), chaves de negócio com índice único onde precisam.

### Pendente de você

`erp_number` não tem índice único. A regra é garantida em código desde o #86.
Para fechar no banco, é preciso antes rodar contra a produção:

```bash
psql "$DATABASE_URL" -f scripts/verificar-banco.sql
```

Com a linha *"número de O.C. do ERP repetido"* em zero, a migration é segura.
Com valor diferente de zero, decidimos juntos o que fazer com os registros
existentes antes de criar o índice.

### Regra para toda alteração de schema

1. Só por migration EF Core, nunca SQL manual em produção;
2. Preservar os dados existentes — nada destrutivo sem necessidade comprovada;
3. **Não criar constraint sobre dado que não posso inspecionar.** Se a produção
   violar a regra, a migration derruba o deploy.

---

## 8. Ordem de execução proposta

### Prioridade 1 — corrigir o que já perde trabalho

| # | Item | Eixo |
|---|---|---|
| 1 | **COR-A** — paginação nas quatro listas principais e busca no servidor | correção + interface · **entregue** |
| 2 | **SEC-A** — filtro no grupo `analytics` + teste sobre a tabela de rotas | segurança · **entregue** |
| 3 | **INT-D · D5/D3/D6/D8** — acordeão do menu, Central de Aprovação no topo, ordem dos grupos e permissão de Pedidos | interface · **entregue (#92)** |

### Prioridade 2 — tirar o risco estrutural

| # | Item | Eixo |
|---|---|---|
| 4 | **INT-D · D2** — "próximo passo" na tela, atravessando o zigue-zague do processo | interface · **entregue (#93)** |
| 5 | **ARQ-A** — recortar `Program.cs` em rotas por módulo | arquitetura · **entregue** |
| 6 | **SEC-B / SEC-C** — rate limiting fora do login e limites de upload | segurança · **entregue** |

### Prioridade 3 — a inteligência que você pediu

| # | Item | Eixo |
|---|---|---|
| 7 | **INT-D · D1/D4** — trilha do processo no painel e contadores no menu | interface + inteligência · **entregue** |
| 8 | **INTEL-A** — insight com ação e link para a tela onde se age | inteligência · **entregue** |
| 9 | **INTEL-B** — regras novas sobre o dado que já existe | inteligência · **entregue** |
| 10 | **INTEL-C** — insight de severidade alta chega ao painel e aos avisos | inteligência + interface · **entregue** |

### Prioridade 4 — acabamento

| # | Item | Eixo |
|---|---|---|
| 11 | **INT-A** — componente único de estado de tela | interface · **premissa corrigida**; corrigida a tela sem estado vazio |
| 12 | **INT-B / ARQ-B** — quebrar as duas maiores unidades | arquitetura · **entregue** — 1.222 → 5 arquivos e 455 → 254 linhas (#109) |
| 13 | **INT-C** — varredura das regras que a tela ainda não antecipa | interface · **concluído** — #107, #108 e #110 |
| 14 | **INT-D · D7** — vocabulário do menu (precisa da sua decisão) | interface |
| 15 | **SEC-D** — zerar o aviso de build | qualidade · **entregue** |

---

## 9. Matriz de resultados

| Área | Problema | Criticidade | Solução | Status | Impacto |
|---|---|---|---|---|---|
| Correção | Listas truncadas sem paginação; fila de aprovação perde processo | 🔴 CRÍTICO | Paginação + busca no servidor | **Entregue** — fila (#89), Pedidos e Fornecedores (#90), SCs e Cotações | Alto |
| Segurança | Autorização por convenção; `analytics` sem filtro de grupo | 🟠 ALTO | Filtro no grupo + teste sobre a tabela registrada | **Entregue** | Alto |
| Arquitetura | `Program.cs` com 120 endpoints | 🟠 ALTO | Rotas por módulo | **Entregue** — 401 linhas, 12 arquivos de rota, inventário como rede | Médio |
| Segurança | Rate limiting só no login | 🟡 MÉDIO | Cotas por usuário em upload e relatório | **Entregue** | Médio |
| Segurança | Upload sem limite declarado | 🟡 MÉDIO | Teto de requisição nos oito endpoints | **Entregue** | Médio |
| Inteligência | Insight descreve mas não age | 🟡 MÉDIO | Ação + link, três regras novas, achado grave no painel | **Entregue** | Alto |
| Interface | Menu por módulo, processo em zigue-zague; acordeão não segue a rota | 🟠 ALTO | Trilha do processo, próximo passo na tela, menu corrigido | D1–D6 e D8 entregues (#92, #93); falta D7, o vocabulário | Alto |
| Interface | 27 telas repetem estado | 🟡 MÉDIO | ~~Componente único~~ — o formato já era uniforme; o defeito era uma tela sem estado vazio | **Corrigido** | Baixo |
| Arquitetura | `QuotationService` com 1.108 linhas | 🟡 MÉDIO | Separar alçadas | A executar | Médio |
| Banco | `erp_number` sem índice único | 🟡 MÉDIO | Migration após conferir a produção | **Bloqueado em você** | Médio |
| Qualidade | Avisos CS9113 e EF1002 | 🟢 BAIXO | Parâmetro removido; SQL literal em vez de interpolado | **Entregue** — build em 0 avisos | Baixo |

---

## 10. Riscos e limites desta auditoria

- **Não tenho acesso ao banco de produção.** A análise de banco cobre schema,
  migrations e o banco local. Sobre os **dados reais** de produção eu não posso
  afirmar nada — daí a pendência do `erp_number`.
- **Os tetos de lista podem já estar sendo atingidos.** Não sei o volume da
  produção. Se já houver mais de 100 pedidos ou 200 cotações, o item COR-A
  deixou de ser prevenção e virou defeito ativo. O
  `scripts/verificar-banco.sql` responde isso na mesma rodada.
- **Cobertura de teste desigual.** 199 xUnit e 260 vitest são bons números, mas
  não medi cobertura por endpoint. A refatoração de ARQ-A se apoia neles.
