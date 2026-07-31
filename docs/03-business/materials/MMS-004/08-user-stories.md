# MMS-004-08 — User Stories

**Documento:** MMS-004-08 — User Stories
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-004 v1.0.0, MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-05 (Event Storming), MMS-004-06 (BPMN), MMS-004-07 (Use Cases)
**Referências:** MMS-002-08 (padrão de formato), PR-001-08, GOV-001

> Especificação das User Stories do módulo Inventory Management.

---

# Epic

EP-IV-001 — Gestão de Estoque e Almoxarifado

---

# Capability

BC-IV-001 — Controlar Saldos, Movimentações, Reservas, Ajustes e Inventários

---

# Feature

FT-IV-001 — Movimentação de Estoque

---

## US-IV-001 — Registrar Entrada por Recebimento

### Persona

Almoxarife

### História

Como almoxarife,

Quero que a entrada no estoque seja gerada automaticamente a partir do recebimento conferido,

Para que o saldo disponível reflita imediatamente o que foi fisicamente recebido, sem redigitação.

### Valor de Negócio

Elimina dupla digitação e divergência entre recebimento e estoque; garante que nenhum item entre no estoque sem conferência (MMS-RG-06) e que todo efeito de saldo venha de documento (MMS-P-08).

### Critérios de Aceite

```gherkin
Scenario: Entrada automática após conferência do recebimento

Given um recebimento conferido no MMS-005 com 2 itens aceitos

When o evento ReceivingConferenceCompleted for publicado

Then um documento de entrada deverá ser criado e confirmado

And o saldo de cada item deverá ser efetivado com saldo anterior e posterior registrados

And o evento StockEntryRegistered deverá ser publicado
```

```gherkin
Scenario: Reenvio idempotente da mesma conferência

Given uma entrada já confirmada para a conferência X

When a mesma mensagem for reprocessada

Then nenhum efeito duplicado deverá ocorrer

And o sistema deverá retornar o documento já registrado
```

### Regras

IV-BR-001

IV-BR-002

IV-BR-010

IV-BR-011

IV-BR-013

IV-BR-090

IV-BR-095

IV-BR-120

### Eventos

StockEntryRegistered (EVT-IV-001)

### Caso de Uso

UC-IV-001

---

## US-IV-002 — Registrar Saída Atendendo Reserva

### Persona

Almoxarife

### História

Como almoxarife,

Quero atender uma reserva da fila com as quantidades efetivamente entregues,

Para baixar o estoque e registrar a entrega ao solicitante com rastreabilidade.

### Valor de Negócio

Garante que toda saída tenha saldo disponível validado (nunca negativo — MMS-RG-04) e que o atendimento seja vinculado à reserva e à solicitação de origem (rastreabilidade ponta a ponta).

### Critérios de Aceite

```gherkin
Scenario: Atendimento total de reserva

Given uma reserva Ativa de 10 unidades do item X no local L

When o almoxarife confirmar a entrega de 10 unidades

Then a saída deverá ser confirmada e o saldo reduzido

And a reserva deverá transicionar para Atendida

And os eventos StockIssueRegistered e ReservationFulfilled deverão ser publicados
```

```gherkin
Scenario: Saldo insuficiente no atendimento

Given saldo disponível de 4 unidades do item X no local L

When o almoxarife tentar confirmar saída de 6 unidades

Then o sistema deverá bloquear com IV-ERR-020 indicando disponível 4 e solicitado 6

And o documento deverá permanecer em Rascunho
```

### Regras

IV-BR-020

IV-BR-021

IV-BR-030..034

IV-BR-070

IV-BR-120

IV-BR-121

### Eventos

StockIssueRegistered (EVT-IV-002)

ReservationFulfilled (EVT-IV-006)

### Caso de Uso

UC-IV-002

---

## US-IV-003 — Registrar Transferência entre Locais

### Persona

Almoxarife

### História

Como almoxarife,

Quero transferir saldo entre depósitos ou endereços com um único documento,

Para reorganizar o estoque fisicamente mantendo o saldo global do item inalterado.

### Valor de Negócio

Efeito atômico (saída na origem + entrada no destino na mesma transação) elimina divergências de "em trânsito" e garante rastreabilidade da movimentação interna.

### Critérios de Aceite

```gherkin
Scenario: Transferência válida entre depósitos

Given saldo disponível de 20 unidades do item X na origem A

When o almoxarife transferir 8 unidades para o destino B

Then a origem A deverá ser reduzida em 8 e o destino B aumentado em 8 na mesma transação

And o evento StockTransferRegistered deverá ser publicado

And o saldo global do item X deverá permanecer inalterado
```

```gherkin
Scenario: Origem igual ao destino

Given o local A

When o almoxarife informar origem A e destino A

Then o sistema deverá bloquear com IV-ERR-050
```

### Regras

IV-BR-020

IV-BR-050..053

IV-BR-060..062

IV-BR-120

### Eventos

StockTransferRegistered (EVT-IV-003)

### Caso de Uso

UC-IV-005

---

## US-IV-004 — Estornar Documento Confirmado

### Persona

Supervisor de Almoxarifado

### História

Como supervisor,

Quero estornar um documento confirmado por engano, com motivo obrigatório,

Para corrigir o estoque sem apagar o registro original, preservando a auditoria.

### Valor de Negócio

Saldo nunca é editado diretamente (MMS-P-08): correções formais via documento de efeito inverso mantêm a trilha completa e a confiabilidade da auditoria.

### Critérios de Aceite

```gherkin
Scenario: Estorno de saída confirmada

Given um documento de saída Confirmado de 5 unidades do item X

When o supervisor estornar com motivo "lançamento em duplicidade"

Then um documento de estorno com efeito inverso deverá ser criado e confirmado

And o documento original deverá transicionar para Estornado

And o evento MovementReversed deverá ser publicado
```

```gherkin
Scenario: Estorno sem motivo

Given um documento Confirmado

When o supervisor tentar estornar sem informar motivo

Then o sistema deverá bloquear com IV-ERR-112
```

### Regras

IV-BR-110..115

MMS-P-08

### Eventos

MovementReversed (EVT-IV-004)

### Caso de Uso

UC-IV-008

---

# Feature

FT-IV-002 — Reservas de Estoque

---

## US-IV-005 — Reserva Automática por Solicitação Aprovada

### Persona

Sistema (em nome do solicitante)

### História

Como sistema,

Quero criar automaticamente a reserva quando uma solicitação de material é aprovada,

Para garantir o estoque do solicitante antes da separação e informar o MMS-003.

### Valor de Negócio

Implementa a validação de estoque após aprovação (MMS-RG-09): o solicitante tem o item garantido e o almoxarifado tem a fila de separação pronta.

### Critérios de Aceite

```gherkin
Scenario: Reserva criada por solicitação aprovada

Given uma solicitação aprovada no MMS-003 com 3 unidades do item X

When o evento RequisitionApproved for consumido

Then uma reserva Ativa deverá ser criada com validade de 72h

And o saldo disponível deverá ser reduzido em 3

And o evento ReservationCreated deverá ser publicado
```

```gherkin
Scenario: Reserva parcial por disponibilidade

Given saldo disponível de 2 unidades para uma solicitação aprovada de 5

When o evento RequisitionApproved for consumido

Then a reserva deverá ser criada com 2 unidades

And o saldo não atendido deverá ser sinalizado ao MMS-003 para rota de compra
```

### Regras

IV-BR-030..032

IV-BR-036

MMS-RG-09

### Eventos

ReservationCreated (EVT-IV-005)

### Caso de Uso

UC-IV-003

---

## US-IV-006 — Atendimento Parcial de Reserva

### Persona

Almoxarife

### História

Como almoxarife,

Quero entregar parcialmente uma reserva quando não houver a quantidade total separada,

Para atender o solicitante com o que há disponível sem perder o saldo restante da reserva.

### Valor de Negócio

Flexibilidade operacional sem perda de controle: a reserva permanece válida pelo saldo restante até o vencimento.

### Critérios de Aceite

```gherkin
Scenario: Entrega parcial mantém reserva ativa

Given uma reserva Ativa de 10 unidades

When o almoxarife entregar 6 unidades

Then a saída de 6 deverá ser confirmada

And a reserva deverá permanecer Ativa com saldo restante de 4 e mesmo vencimento
```

### Regras

IV-BR-033

IV-BR-034

### Eventos

StockIssueRegistered (EVT-IV-002)

### Caso de Uso

UC-IV-002

---

## US-IV-007 — Liberar Reserva Manualmente

### Persona

Almoxarife / Supervisor

### História

Como almoxarife,

Quero liberar uma reserva que não será mais atendida,

Para devolver o saldo ao disponível para outras necessidades.

### Valor de Negócio

Evita estoque "travado" em reservas sem demanda real; a liberação também ocorre automaticamente quando a solicitação é cancelada no MMS-003.

### Critérios de Aceite

```gherkin
Scenario: Liberação manual

Given uma reserva Ativa de 4 unidades

When o almoxarife liberar a reserva

Then a reserva deverá transicionar para Liberada

And as 4 unidades deverão retornar ao saldo disponível

And o evento ReservationReleased deverá ser publicado
```

```gherkin
Scenario: Liberação automática por cancelamento da solicitação

Given uma reserva Ativa vinculada à solicitação S

When o evento RequisitionCancelled de S for consumido

Then a reserva deverá ser liberada automaticamente sem intervenção do almoxarife
```

### Regras

IV-BR-035

IV-BR-037

### Eventos

ReservationReleased (EVT-IV-007)

### Caso de Uso

UC-IV-004

---

## US-IV-008 — Vencimento Automático de Reserva

### Persona

Sistema

### História

Como sistema,

Quero vencer automaticamente as reservas que atingirem o prazo sem atendimento,

Para liberar o saldo e reabrir a necessidade do solicitante.

### Valor de Negócio

Reserva com validade (MMS-RG-03) impede bloqueio indefinido de estoque; o alerta 24h antes dá chance de providência antes do vencimento.

### Critérios de Aceite

```gherkin
Scenario: Alerta de reserva vencendo

Given uma reserva Ativa com vencimento em 24 horas

When o timer de janela de alerta disparar

Then uma notificação "reserva vencendo" deverá ser enviada aos envolvidos

And a reserva deverá permanecer Ativa
```

```gherkin
Scenario: Vencimento sem atendimento

Given uma reserva Ativa cujo expiresAt foi atingido

When o timer de vencimento disparar

Then a reserva deverá transicionar para Vencida

And o saldo deverá retornar ao disponível

And o evento ReservationExpired deverá ser publicado e consumido pelo MMS-003
```

### Regras

IV-BR-036

IV-BR-037

MMS-RG-03

### Eventos

ReservationExpired (EVT-IV-008)

### Caso de Uso

UC-IV-004

---

# Feature

FT-IV-003 — Ajustes de Estoque

---

## US-IV-009 — Registrar Ajuste com Motivo Estruturado

### Persona

Supervisor de Almoxarifado

### História

Como supervisor,

Quero registrar um ajuste de saldo com tipo, motivo estruturado e itens afetados,

Para corrigir formalmente divergências identificadas fora de inventário (avaria, perda, erro de lançamento).

### Valor de Negócio

Todo ajuste fica pendente de aprovação com motivo rastreável (Master Data), eliminando correções informais de saldo.

### Critérios de Aceite

```gherkin
Scenario: Registro de ajuste negativo por avaria

Given saldo de 12 unidades do item X no local L

When o supervisor registrar ajuste negativo de 2 unidades com motivo "avaria"

Then o ajuste deverá ser criado em Pendente

And o evento AdjustmentRegistered deverá ser publicado

And os aprovadores deverão ser notificados

And o saldo não deverá ser alterado antes da aprovação
```

### Regras

IV-BR-080..083

### Eventos

AdjustmentRegistered (EVT-IV-009)

### Caso de Uso

UC-IV-006

---

## US-IV-010 — Aprovar ou Rejeitar Ajuste com SoD

### Persona

Gestor de Suprimentos

### História

Como gestor,

Quero aprovar ou rejeitar ajustes pendentes da minha equipe, sem poder aprovar os que eu mesmo registrei,

Para garantir controle e segregação de funções sobre as correções de saldo.

### Valor de Negócio

Segregation of Duties (IV-BR-085) como controle anti-fraude; rejeição com motivo obrigatório fecha o ciclo de feedback ao registrador.

### Critérios de Aceite

```gherkin
Scenario: Aprovação de ajuste por gestor distinto do registrador

Given um ajuste Pendente registrado pelo supervisor S

When o gestor G (diferente de S) aprovar

Then o efeito de saldo deverá ser aplicado

And o ajuste deverá transicionar para Aprovado

And o evento AdjustmentApproved deverá ser publicado
```

```gherkin
Scenario: Tentativa de auto-aprovação

Given um ajuste Pendente registrado pelo gestor G

When o próprio G tentar aprovar

Then o sistema deverá bloquear com IV-ERR-085

And a tentativa deverá ser auditada
```

```gherkin
Scenario: Rejeição com motivo

Given um ajuste Pendente

When o gestor rejeitar informando o motivo

Then nenhum efeito de saldo deverá ocorrer

And o evento AdjustmentRejected deverá ser publicado
```

### Regras

IV-BR-084

IV-BR-085

IV-BR-086

### Eventos

AdjustmentApproved (EVT-IV-010)

AdjustmentRejected (EVT-IV-011)

### Caso de Uso

UC-IV-006

---

# Feature

FT-IV-004 — Inventário (Contagem Física)

---

## US-IV-011 — Executar Inventário Cíclico

### Persona

Supervisor de Almoxarifado

### História

Como supervisor,

Quero abrir inventários cíclicos por classe de giro (A=30d, B=90d, C=180d),

Para manter a acuracidade do estoque acima de 98% sem paralisar a operação.

### Valor de Negócio

Inventário cíclico distribui o esforço de conferência ao longo do ano e sustenta o KPI de acuracidade (MMS-004-01) sem inventário geral traumático.

### Critérios de Aceite

```gherkin
Scenario: Abertura de inventário cíclico classe A

Given itens classe A com última contagem há mais de 30 dias

When o supervisor abrir o inventário cíclico

Then o inventário deverá ser criado em Aberto com o escopo sugerido

And o evento InventoryCountStarted deverá ser publicado

And a lista de contagem deverá ser gerada sem exibir o saldo sistêmico (contagem cega)
```

### Regras

IV-BR-100

IV-BR-103

### Eventos

InventoryCountStarted (EVT-IV-012)

### Caso de Uso

UC-IV-007

---

## US-IV-012 — Contagem, Divergências e Fechamento

### Persona

Almoxarife

### História

Como almoxarife,

Quero registrar as quantidades contadas por item e local e ver as divergências apuradas,

Para que os ajustes sejam gerados automaticamente e o inventário feche com acuracidade registrada.

### Valor de Negócio

Apuração automática com tolerância configurável (padrão zero) e ajustes vinculados com aprovação obrigatória fecham o ciclo de correção com governança.

### Critérios de Aceite

```gherkin
Scenario: Contagem com divergência gera ajuste

Given um inventário Em Contagem com o item X (sistêmico 10, contado 8)

When o supervisor encerrar os lançamentos

Then uma divergência de -2 deverá ser apurada

And um ajuste vinculado deverá ser gerado para aprovação

And o inventário só deverá fechar após a conclusão do ajuste
```

```gherkin
Scenario: Contagem sem divergência fecha direto

Given um inventário Em Contagem sem divergências acima da tolerância

When o supervisor fechar o inventário

Then o inventário deverá transicionar para Fechado

And o evento InventoryCountClosed deverá ser publicado com a acuracidade registrada
```

### Regras

IV-BR-101

IV-BR-102

### Eventos

CountEntryRegistered (EVT-IV-013)

InventoryCountClosed (EVT-IV-014)

AdjustmentRegistered (EVT-IV-009, por divergência)

### Caso de Uso

UC-IV-007

---

# Feature

FT-IV-005 — Consulta, Visão do Almoxarifado e Alertas

---

## US-IV-013 — Consultar Posição e Extrato de Estoque

### Persona

Gestor / Auditor

### História

Como gestor,

Quero consultar a posição (físico, reservado, disponível) e o extrato de movimentações por item e local,

Para acompanhar o estoque e rastrear qualquer saldo até o documento de origem.

### Valor de Negócio

Transparência total: o extrato reconstrói a trilha documento → origem (recebimento, solicitação, ajuste, inventário, estorno), sustentando auditoria e análise de consumo.

### Critérios de Aceite

```gherkin
Scenario: Posição por item e local

Given o item X com físico 20 e reservado 5 no local L

When o gestor consultar a posição

Then o sistema deverá exibir físico 20, reservado 5 e disponível 15
```

```gherkin
Scenario: Extrato com trilha de origem

Given um item com movimentações de entrada por recebimento e saída por solicitação

When o auditor consultar o extrato

Then cada linha deverá exibir documento, efeito, saldo anterior/posterior e origem (recebimento ou solicitação)
```

### Regras

IV-BR-096

IV-BR-098

### Eventos

— (somente leitura)

### Caso de Uso

UC-IV-011

---

## US-IV-014 — Visão do Almoxarifado com Filtros Operacionais

### Persona

Almoxarife

### História

Como almoxarife,

Quero uma fila única de todas as movimentações e reservas com filtros por solicitante, período, status, centro de custo, empresa, tipo de produto e número,

Para operar a separação e a entrega sem alternar de tela.

### Valor de Negócio

Tela operacional exclusiva do almoxarifado (solicitantes não acessam) que concentra as ações do fluxo — requisito funcional originado do negócio (IV-BR-097).

### Critérios de Aceite

```gherkin
Scenario: Filtro combinado da fila operacional

Given movimentações e reservas de vários solicitantes e centros de custo

When o almoxarife filtrar por status "Aprovado", centro de custo CC-100 e tipo "EPI"

Then apenas os registros correspondentes deverão ser exibidos

And o almoxarife deverá poder iniciar a separação a partir da linha
```

```gherkin
Scenario: Solicitante não acessa a visão do almoxarifado

Given um usuário com perfil de solicitante

When tentar acessar a visão do almoxarifado

Then o sistema deverá negar acesso e registrar a tentativa
```

### Regras

IV-BR-097

### Eventos

— (somente leitura)

### Caso de Uso

UC-IV-011

---

## US-IV-015 — Tratar Alerta de Ruptura

### Persona

Supervisor / Gestor

### História

Como supervisor,

Quero ser alertado imediatamente quando um item zerar o saldo ou cruzar o mínimo,

Para acionar a reposição antes do impacto nas solicitações.

### Valor de Negócio

Alertas de mínimo e ruptura (MMS-RG-12) com deduplicação, prioridade alta na ruptura e escalonamento em 48h sem normalização evitam desabastecimento silencioso.

### Critérios de Aceite

```gherkin
Scenario: Ruptura dispara alerta de alta prioridade

Given saldo do item X em 1 unidade no local L

When uma saída de 1 unidade for confirmada

Then o evento StockoutAlerted deverá ser publicado

And uma notificação de prioridade alta deverá ser enviada aos destinatários configurados
```

```gherkin
Scenario: Normalização automática por entrada

Given um alerta de ruptura Aberto para o item X no local L

When uma entrada de saldo for confirmada

Then o alerta deverá ser normalizado automaticamente

And os envolvidos deverão ser informados da normalização
```

```gherkin
Scenario: Deduplicação de alerta aberto

Given um alerta de mínimo Aberto para o item X no local L

When nova saída mantiver o saldo abaixo do mínimo

Then nenhum novo alerta deverá ser criado para a mesma chave
```

### Regras

IV-BR-090..092

MMS-RG-12

### Eventos

StockMinimumAlerted (EVT-IV-015)

StockoutAlerted (EVT-IV-016)

### Caso de Uso

UC-IV-010

---

## US-IV-016 — Gerenciar Locais de Armazenagem

### Persona

Supervisor de Almoxarifado / Administrador

### História

Como supervisor,

Quero cadastrar a estrutura de locais (almoxarifado, depósito, endereço) e inativar locais sem saldo,

Para manter o endereçamento do estoque organizado e coerente com a operação.

### Valor de Negócio

Endereçamento estruturado e parametrizável (IV-BR-060..063) é a base da chave de saldo e da acuracidade das contagens.

### Critérios de Aceite

```gherkin
Scenario: Cadastro de endereço dentro de depósito

Given o depósito D ativo no almoxarifado A

When o supervisor cadastrar o endereço E-01 com pai D

Then o local deverá ser persistido com a hierarquia A/D/E-01
```

```gherkin
Scenario: Inativação bloqueada com saldo

Given o endereço E-01 com saldo físico de 3 unidades

When o supervisor tentar inativar E-01

Then o sistema deverá bloquear com IV-ERR-063 indicando a posição atual
```

### Regras

IV-BR-060..063

### Eventos

— (v1.1: LocationChanged)

### Caso de Uso

UC-IV-009

---

# Matriz de Rastreabilidade

| Story | UC | Regra | Evento |
|-------|----|-------|--------|
| US-IV-001 | UC-IV-001 | IV-BR-001/002/010/011/013/090/095/120 | StockEntryRegistered (EVT-IV-001) |
| US-IV-002 | UC-IV-002 | IV-BR-020/021/030..034/070/120/121 | StockIssueRegistered (EVT-IV-002), ReservationFulfilled (EVT-IV-006) |
| US-IV-003 | UC-IV-005 | IV-BR-020/050..053/060..062/120 | StockTransferRegistered (EVT-IV-003) |
| US-IV-004 | UC-IV-008 | IV-BR-110..115 / MMS-P-08 | MovementReversed (EVT-IV-004) |
| US-IV-005 | UC-IV-003 | IV-BR-030..032/036 / MMS-RG-09 | ReservationCreated (EVT-IV-005) |
| US-IV-006 | UC-IV-002 | IV-BR-033/034 | StockIssueRegistered (EVT-IV-002) |
| US-IV-007 | UC-IV-004 | IV-BR-035/037 | ReservationReleased (EVT-IV-007) |
| US-IV-008 | UC-IV-004 | IV-BR-036/037 / MMS-RG-03 | ReservationExpired (EVT-IV-008) |
| US-IV-009 | UC-IV-006 | IV-BR-080..083 | AdjustmentRegistered (EVT-IV-009) |
| US-IV-010 | UC-IV-006 | IV-BR-084/085/086 | AdjustmentApproved (EVT-IV-010), AdjustmentRejected (EVT-IV-011) |
| US-IV-011 | UC-IV-007 | IV-BR-100/103 | InventoryCountStarted (EVT-IV-012) |
| US-IV-012 | UC-IV-007 | IV-BR-101/102 | CountEntryRegistered (EVT-IV-013), InventoryCountClosed (EVT-IV-014), AdjustmentRegistered (EVT-IV-009) |
| US-IV-013 | UC-IV-011 | IV-BR-096/098 | — |
| US-IV-014 | UC-IV-011 | IV-BR-097 | — |
| US-IV-015 | UC-IV-010 | IV-BR-090..092 / MMS-RG-12 | StockMinimumAlerted (EVT-IV-015), StockoutAlerted (EVT-IV-016) |
| US-IV-016 | UC-IV-009 | IV-BR-060..063 | — |

**Cobertura:** 16/16 stories rastreadas a UC; 16/16 eventos EVT-IV cobertos; todas as famílias de regras IV-BR contempladas.

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: 16 User Stories (US-IV-001..016) em 5 Features (Movimentação, Reservas, Ajustes, Inventário, Consulta/Visão/Alertas), com persona, valor de negócio, critérios Gherkin, regras, eventos, UC vinculado e matriz de rastreabilidade completa. |
