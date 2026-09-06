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
| Regras no motor de insights | 4 |
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
— um piso, com cada handler mantendo a checagem específica dele. E
`RotasProtegidasTests` confere quatro invariantes: todo grupo `/api` exige
autenticação; os únicos grupos públicos são `auth` e `portal`; toda rota `/api`
fora de grupo declara `RequireAuthorization`; e todo grupo interno tem algo que
o token do Portal do Fornecedor não satisfaz.

A conferência é sobre o **texto** do `Program.cs`, não sobre a tabela de rotas
em execução. O motivo é o custo: o app roda as migrations na inicialização, e
montá-lo num teste exigiria um Postgres no job de testes unitários, que hoje
roda em sete segundos sem banco nenhum. O teste é embutido como recurso, para
não depender do diretório de trabalho, e tem uma asserção que falha se o
recurso sumir — sem ela os outros passariam sobre o vazio. Depois do **ARQ-A**,
a versão em execução fica barata e substitui esta.

### 🟡 SEC-B · Rate limiting só na autenticação

Existem as políticas `auth` e `auth-refresh`. O resto da API não tem limite —
inclusive os endpoints de upload e os de relatório, que são caros.

### 🟡 SEC-C · Upload sem limite declarado de tamanho e tipo

Cinco endpoints de anexo gravam `bytea` no banco. Vale checar limite de bytes,
lista de tipos aceitos e o que acontece com um arquivo grande.

### 🟢 SEC-D · Aviso de build não resolvido

`CS9113: Parameter 'inventory' is unread` — parâmetro injetado e não usado.
Ruído que esconde avisos futuros.

---

## 3. Correção e confiabilidade — o achado principal

### 🔴 COR-A · Listas truncadas em silêncio, sem paginação

| Consulta | Teto |
|---|---|
| `RequisitionService.ListAsync` (Meus Pedidos) | 100 |
| `RequisitionService` fila de aprovação | 100 |
| `PurchaseOrderService.ListAsync` (Pedidos) | 100 |
| `PurchaseOrderService` SCs aguardando pedido | 100 |
| `QuotationService.ListAsync` (Processos) | 200 |
| `QuotationService` fila | 200 |
| `TriageService` | 200 |
| `SupplierService` | 500 |

Nenhuma dessas rotas aceita página, deslocamento ou cursor. Duas consequências
concretas:

1. **`/my-approvals` perde trabalho.** O endpoint chama `ListAsync()` — que já
   corta em 200 — e **filtra em memória** quem aprova o quê. Passando de 200
   cotações, um processo aguardando aprovação simplesmente não aparece na fila
   do aprovador. Ninguém é avisado.
2. **A busca mente.** `PedidosLista` e `Fornecedores` filtram no navegador,
   sobre a lista já truncada. Procurar um pedido antigo devolve *"Nenhum pedido
   corresponde ao filtro"* — que é falso: o pedido existe, só não veio.

**Proposta:** paginação por cursor na API, começando pelas quatro listas que o
usuário mais usa, e busca no servidor nas duas telas que filtram no navegador.
A interface muda o mínimo: o mesmo campo de busca, consultando o servidor.

---

## 4. Arquitetura

### 🟠 ARQ-A · `Program.cs` com 2.797 linhas e 120 endpoints

Um arquivo concentra o roteamento inteiro, as vinte e poucas funções de
serialização (`PrView`, `PoView`, `QuotationView`, `UserView`…) e os `record` de
request. Efeitos práticos: conflito em qualquer trabalho paralelo, difícil achar
o endpoint, e a serialização longe da entidade que ela descreve.

**Proposta:** extrair um arquivo de rotas por módulo
(`Procurement/ProcurementEndpoints.cs`, `Materials/…`), no padrão de extension
method que o .NET já usa. Movimento mecânico, sem mudar comportamento, com os
testes existentes como rede. **Não é reescrita** — é recorte.

### 🟡 ARQ-B · `QuotationService.cs` com 1.108 linhas

Concentra abertura, convite, proposta, negociação, adjudicação, alçadas e O.C.
Candidato natural a separar a parte de alçadas/decisão do resto.

### 🟢 ARQ-C · Referências entre schemas sem FK

Deliberado, para desacoplar os módulos, e já documentado. Fica como está — o
`scripts/verificar-banco.sql` é a contrapartida.

---

## 5. Interface

### 🟠 INT-A · 27 telas, 27 tratamentos de carregando/erro

Toda tela repete `{carregando && <Carregando/>}` e `{erro && <Erro>}` com
variações. Não há um componente que padronize o estado de uma tela.

**Proposta:** um componente `<Conteudo carregando erro vazio>` que encapsula os
três estados. Reduz repetição e faz as telas se comportarem igual. **Sem mudar
o visual** — o mesmo `Carregando` e o mesmo `Erro` por dentro.

### 🟡 INT-B · `ProcessoDetalhe.tsx` com 453 linhas

A maior tela do sistema, com convite, mapa, propostas, aprovação, O.C. e
anexos. Separável em blocos sem tocar no layout.

### 🟡 INT-C · Regras que a tela ainda não antecipa

O trabalho de #85 e #88 cobriu segregação de funções, EPI sem C.A. e O.C. sem
número. Falta varrer as demais regras do backend e ver quais ainda só aparecem
como erro depois do formulário preenchido.

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

### INTEL-A · Insight que diz o que fazer

Hoje o insight descreve. Falta a ação: qual processo abrir, qual fornecedor
renegociar, qual contrato revisar — com link para a tela onde se age.

### INTEL-B · Regras novas com o dado que já existe

O banco já guarda o necessário para, por exemplo: fornecedor com entregas
atrasadas em sequência (OTIF por fornecedor existe), contrato perto de vencer
(`contract_valid_until` existe), documento de homologação vencido
(`supplier_document.valid_until` existe), SC parada sem responsável (`aging`
existe), item comprado repetidamente sem contrato.

### INTEL-C · O insight chega até quem decide

Hoje ele mora numa tela que a pessoa precisa abrir. Levar os de severidade alta
para a Central de Avisos e para o painel faz o sistema avisar em vez de esperar.

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
| 1 | **COR-A** — paginação nas quatro listas principais e busca no servidor | correção + interface |
| 2 | **SEC-A** — filtro no grupo `analytics` + teste que varre as rotas | segurança · **entregue** |
| 3 | **INT-D · D5/D3/D6/D8** — acordeão do menu, Central de Aprovação no topo, ordem dos grupos e permissão de Pedidos | interface · **entregue (#92)** |

### Prioridade 2 — tirar o risco estrutural

| # | Item | Eixo |
|---|---|---|
| 4 | **INT-D · D2** — "próximo passo" na tela, atravessando o zigue-zague do processo | interface · **entregue (#93)** |
| 5 | **ARQ-A** — recortar `Program.cs` em rotas por módulo | arquitetura |
| 6 | **SEC-B / SEC-C** — rate limiting fora do login e limites de upload | segurança |

### Prioridade 3 — a inteligência que você pediu

| # | Item | Eixo |
|---|---|---|
| 7 | **INT-D · D1/D4** — trilha do processo no painel e contadores no menu | interface + inteligência · **entregue** |
| 8 | **INTEL-A** — insight com ação e link para a tela onde se age | inteligência |
| 9 | **INTEL-B** — regras novas sobre o dado que já existe | inteligência |
| 10 | **INTEL-C** — insight de severidade alta chega ao painel e aos avisos | inteligência + interface |

### Prioridade 4 — acabamento

| # | Item | Eixo |
|---|---|---|
| 11 | **INT-A** — componente único de estado de tela | interface |
| 12 | **INT-B / ARQ-B** — quebrar as duas maiores unidades | arquitetura |
| 13 | **INT-C** — varredura das regras que a tela ainda não antecipa | interface |
| 14 | **INT-D · D7** — vocabulário do menu (precisa da sua decisão) | interface |
| 15 | **SEC-D** — zerar o aviso de build | qualidade |

---

## 9. Matriz de resultados

| Área | Problema | Criticidade | Solução | Status | Impacto |
|---|---|---|---|---|---|
| Correção | Listas truncadas sem paginação; fila de aprovação perde processo | 🔴 CRÍTICO | Paginação + busca no servidor | Fila de aprovação (#89), Pedidos e Fornecedores (#90) entregues; faltam SCs e Cotações | Alto |
| Segurança | Autorização por convenção; `analytics` sem filtro de grupo | 🟠 ALTO | Filtro no grupo + teste que varre as rotas | **Entregue** | Alto |
| Arquitetura | `Program.cs` com 120 endpoints | 🟠 ALTO | Rotas por módulo | A executar | Médio |
| Segurança | Rate limiting só no login | 🟡 MÉDIO | Política por grupo | A executar | Médio |
| Segurança | Upload sem limite declarado | 🟡 MÉDIO | Limite de bytes e tipos | A executar | Médio |
| Inteligência | Insight descreve mas não age | 🟡 MÉDIO | Ação + link + regras novas | A executar | Alto |
| Interface | Menu por módulo, processo em zigue-zague; acordeão não segue a rota | 🟠 ALTO | Trilha do processo, próximo passo na tela, menu corrigido | D1–D6 e D8 entregues (#92, #93); falta D7, o vocabulário | Alto |
| Interface | 27 telas repetem estado | 🟡 MÉDIO | Componente único | A executar | Médio |
| Arquitetura | `QuotationService` com 1.108 linhas | 🟡 MÉDIO | Separar alçadas | A executar | Médio |
| Banco | `erp_number` sem índice único | 🟡 MÉDIO | Migration após conferir a produção | **Bloqueado em você** | Médio |
| Qualidade | Aviso CS9113 | 🟢 BAIXO | Remover parâmetro | A executar | Baixo |

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
