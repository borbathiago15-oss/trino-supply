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

**Proposta:** filtro padrão no grupo `analytics`, e um teste que percorre a
tabela de rotas e falha se algum endpoint `/api/v1` não tiver política de
autorização declarada. Vira uma rede, não uma lembrança.

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
| 2 | **SEC-A** — filtro no grupo `analytics` + teste que varre a tabela de rotas | segurança |

### Prioridade 2 — tirar o risco estrutural

| # | Item | Eixo |
|---|---|---|
| 3 | **ARQ-A** — recortar `Program.cs` em rotas por módulo | arquitetura |
| 4 | **SEC-B / SEC-C** — rate limiting fora do login e limites de upload | segurança |

### Prioridade 3 — a inteligência que você pediu

| # | Item | Eixo |
|---|---|---|
| 5 | **INTEL-A** — insight com ação e link para a tela onde se age | inteligência |
| 6 | **INTEL-B** — regras novas sobre o dado que já existe | inteligência |
| 7 | **INTEL-C** — insight de severidade alta chega ao painel e aos avisos | inteligência + interface |

### Prioridade 4 — acabamento

| # | Item | Eixo |
|---|---|---|
| 8 | **INT-A** — componente único de estado de tela | interface |
| 9 | **INT-B / ARQ-B** — quebrar as duas maiores unidades | arquitetura |
| 10 | **INT-C** — varredura das regras que a tela ainda não antecipa | interface |
| 11 | **SEC-D** — zerar o aviso de build | qualidade |

---

## 9. Matriz de resultados

| Área | Problema | Criticidade | Solução | Status | Impacto |
|---|---|---|---|---|---|
| Correção | Listas truncadas sem paginação; fila de aprovação perde processo | 🔴 CRÍTICO | Paginação por cursor + busca no servidor | A executar | Alto |
| Segurança | Autorização por convenção; `analytics` sem filtro de grupo | 🟠 ALTO | Filtro no grupo + teste da tabela de rotas | A executar | Alto |
| Arquitetura | `Program.cs` com 120 endpoints | 🟠 ALTO | Rotas por módulo | A executar | Médio |
| Segurança | Rate limiting só no login | 🟡 MÉDIO | Política por grupo | A executar | Médio |
| Segurança | Upload sem limite declarado | 🟡 MÉDIO | Limite de bytes e tipos | A executar | Médio |
| Inteligência | Insight descreve mas não age | 🟡 MÉDIO | Ação + link + regras novas | A executar | Alto |
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
