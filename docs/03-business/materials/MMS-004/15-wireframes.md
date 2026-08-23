**Documento:** MMS-004-15 — Wireframes
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-23
**Dependências:** MMS-004-14 (UX), MMS-004-07 (Use Cases), MMS-004-09 (Permissions), MMS-004-13 (API), DS-001 (Design System)
**Referências:** MMS-004-03 (State Machine), MMS-004-12 (Business Journey), MMS-002-15 (Wireframes do Item Catalog — padrão de formato), PR-001-15

---

# 1. Objetivo

Este documento especifica os wireframes oficiais das telas do módulo **Inventory Management**, definindo estrutura, zonas, hierarquia de conteúdo e comportamento responsivo de cada tela listada na arquitetura de informação do **MMS-004-14 (seção 4)**.

Regras de vínculo:

- Os wireframes materializam o MMS-004-14; nenhuma zona, ação ou mensagem pode ser incluída sem correspondência em um UC (MMS-004-07), endpoint (MMS-004-13) ou componente Foundation.
- Wireframes definem **estrutura e comportamento, não estética**: cores, tipografia, espaçamentos e iconografia pertencem ao DS-001 e à implementação.
- A notação ASCII deste documento representa hierarquia e proporção relativa, não medidas em pixel.

Convenções de notação:

| Símbolo | Significado |
|---------|-------------|
| `[ Botão ]` | Ação (botão/link) |
| `{campo}` | Entrada de dados |
| `[x]` / `( )` | Checkbox / rádio |
| `#n` | Zona numerada, descrita na legenda da tela |
| `···` | Conteúdo repetido (linhas de lista/itens) |
| `▲ ▼` | Ordenação / expansão |

---

# 2. Inventário de Wireframes

| Wireframe | Tela (MMS-004-14) | Nome | Breakpoints |
|-----------|-------------------|------|-------------|
| WF-IV-01 | IV-SCR-01 | Visão do Almoxarifado | Desktop / Tablet / Mobile |
| WF-IV-02 | IV-SCR-02 | Posição e Extrato de Estoque | Desktop / Tablet / Mobile |
| WF-IV-03 | IV-SCR-03 | Documento de Movimentação (formulário + detalhe) | Desktop / Tablet / Mobile |
| WF-IV-04 | IV-SCR-04 | Ajustes (registro + fila de aprovação) | Desktop / Tablet |
| WF-IV-05 | IV-SCR-05 | Inventário (abertura, contagem, apuração, fechamento) | Desktop / Tablet / Mobile |
| WF-IV-06 | IV-SCR-06 | Locais de Armazenagem | Desktop |
| WF-IV-07 | IV-SCR-07 | Alertas de Estoque | Desktop / Mobile |
| WF-IV-08 | IV-SCR-08 | Configurações do Módulo | Desktop |

Cada wireframe inclui: layout desktop, variantes responsivas, estados (carregando, vazio, erro, sem permissão) e legenda de zonas. Todo o módulo é invisível ao perfil Requester (IV-BR-097) — os estados "sem permissão" ocorrem apenas por URL direta.

---

# 3. WF-IV-01 — Visão do Almoxarifado (IV-SCR-01)

**Casos de uso:** UC-IV-002, UC-IV-003, UC-IV-004. **Endpoints:** `GET /reservations`, `GET /movements`.

## 3.1 Desktop (≥ 1280 px)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Visão do Almoxarifado — Almoxarifado Central       [ Nova reserva ] #2 │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 [Reservas (12)] [Rascunhos (3)] [Concluídos do dia]                    │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 Filtros (IV-BR-097)                                                    │
│ {solicitante} {período} {status ▼} {centro de custo ▼} {empresa ▼}        │
│ {tipo: EPI/Fardamento ▼} {número}                     [ Limpar filtros ]  │
├──────────────────────────────────────────────────────────────────────────┤
│ #5 Fila de reservas (ordenada por vencimento ▲)                           │
│ Nº │ Item │ Tam │ Qtd rest. │ Local │ Solicitação │ Vence em │ Ações      │
│ RSV-0815 │ [▣] Capacete seg. │ — │ 6 │ DEP-CENTRAL │ MR-0512 │ ⚠ 18h     │
│                                              [ Atender ] [ Liberar ]     │
│ RSV-0816 │ [▣] Luva vaqueta │ M │ 10 │ DEP-CENTRAL │ MR-0514 │ 2d 4h     │
│                                              [ Atender ] [ Liberar ]     │
│ ···                                                                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #6 {N} reservas ativas · {M} vencendo em 24h        [ Carregar mais ]     │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Título + escopo** — almoxarifado(s) do escopo do usuário (ABAC-IV-05); seletor quando houver mais de um.
- **#2 Ação primária** — reserva manual (IV-PERM-003); oculta sem permissão (CA-UX-IV-03).
- **#3 Abas** — Reservas (padrão, contador de ativas), Rascunhos de documento, Concluídos do dia.
- **#4 Filtros** — todos os filtros da visão do almoxarifado (IV-BR-097), aplicados no servidor; dados de solicitante visíveis apenas aos papéis autorizados (ABAC-IV-02/04).
- **#5 Fila** — ordenação padrão por vencimento ascendente; selo `⚠` nas que vencem em 24h (TMR-IV-002); imagem do item vinda do catálogo; coluna Tam exibida somente quando há itens com grade no resultado; ações conforme matriz MMS-004-09.
- **#6 Rodapé** — contadores + keyset ("Carregar mais", nunca paginação numerada).

## 3.2 Tablet (768–1279 px)

Colunas mantidas: Item, Tam, Qtd restante, Vence em, Ações. Local e solicitação migram para a expansão da linha. Filtros colapsam em `[ Filtros ▼ ]`.

## 3.3 Mobile (< 768 px)

```
┌─────────────────────────────┐
│ #1 Almoxarifado Central     │
│ [Reservas (12)] [Filtros ▼] │
├─────────────────────────────┤
│ ┌─────────────────────────┐ │
│ │ RSV-0815    ⚠ vence 18h │ │
│ │ [▣] Capacete segurança  │ │
│ │ 6 UN · DEP-CENTRAL      │ │
│ │ [ Atender ] [ Liberar ] │ │
│ └─────────────────────────┘ │
│ ···                         │
│ [ Carregar mais ]           │
└─────────────────────────────┘
```

Cartões; item, quantidade, estado e validade nunca omitidos (MMS-004-14, seção 8). Atendimento em campo é cidadão de primeira classe.

## 3.4 Estados

| Estado | Composição |
|--------|------------|
| Carregando | Skeleton de 5 linhas na forma da fila |
| Vazio | "Nenhuma reserva pendente" + resumo dos concluídos do dia |
| Erro | "Não foi possível carregar a fila" + código de suporte + `[ Tentar novamente ]` |
| Sem permissão | Acesso negado com orientação (menu já é ocultado; URL direta) |

---

# 4. WF-IV-02 — Posição e Extrato de Estoque (IV-SCR-02)

**Casos de uso:** UC-IV-011. **Endpoints:** `GET /balances`, `GET /balances/{itemId}/statement`.

## 4.1 Desktop — Posição

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Posição de Estoque                                                     │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 {buscar item…} {local ▼} {tamanho ▼} {cliente/contrato ▼}              │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Tabela de posição                                                      │
│ Item │ Tam │ Local │ Físico │ Reservado │ Disponível │                    │
│ [▣] Capacete seg. │ — │ DEP-CENTRAL │ 36 │ 6 │ 30 │ [ Extrato ]           │
│ [▣] Luva vaqueta │ M │ DEP-CENTRAL │ 120 │ 10 │ 110 │ [ Extrato ]         │
│ ···                                                                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 {N} chaves de saldo                              [ Carregar mais ]     │
└──────────────────────────────────────────────────────────────────────────┘
```

## 4.2 Desktop — Extrato do item

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #5 Extrato — [▣] Capacete de segurança (EPI-CAP-001)                      │
│    Físico: 36 · Reservado: 6 · Disponível: 30                             │
├──────────────────────────────────────────────────────────────────────────┤
│ #6 {período} {local ▼} {tipo de documento ▼}                              │
├──────────────────────────────────────────────────────────────────────────┤
│ #7 Movimentações (confirmadas, ▼ mais recentes)                           │
│ Data │ Documento │ Tipo │ Origem │ Qtd │ Saldo antes → depois              │
│ 23/08 14:02 │ SAI-0210 │ Saída │ MR-0512 │ −6 │ 42 → 36                   │
│ 22/08 09:15 │ ENT-0123 │ Entrada │ REC-0045 │ +50 │ −  → 42               │
│ ···                                                                       │
│ (clique na linha abre o documento — WF-IV-03 detalhe)                     │
├──────────────────────────────────────────────────────────────────────────┤
│ #8 [ Carregar mais ]                          [ Exportar CSV* ] (v1.1)    │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#3 Posição** — três saldos sempre juntos (físico/reservado/disponível); segregação exibe cliente/contrato quando habilitada; saldo de outro contrato **não aparece** (ABAC-IV-03).
- **#7 Extrato** — saldo anterior/posterior por linha (IV-BR-095); origem navegável (recebimento/solicitação/ajuste/inventário/estorno) — trilha ponta a ponta.
- Tablet/Mobile: posição em cartões (item + 3 saldos); extrato em lista cronológica compacta.

## 4.3 Estados

Carregando: skeleton de tabela. Vazio: "Nenhum saldo para os filtros" com orientação. Degradação de cache: dados servidos do banco sem erro ao usuário (MMS-004-14, 6.1).

---

# 5. WF-IV-03 — Documento de Movimentação (IV-SCR-03)

**Casos de uso:** UC-IV-001, UC-IV-002, UC-IV-005, UC-IV-008. **Endpoints:** `POST/PATCH /movements`, `POST …/confirm`, `POST …/cancel`, `POST …/reverse`, `POST /transfers`, `GET /movements/{id}`.

## 5.1 Desktop — Formulário (rascunho)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Nova Saída (ou ENT-0123 — ● Rascunho)          salvo às 14:32 ✓ #2     │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Origem do documento *                                                  │
│ (•) Atendimento de reserva {RSV-0815 ▼}  ( ) Consumo  ( ) Ajuste          │
│ Referência: MR-2026-000512 (somente leitura quando pré-carregado)         │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 Linhas                                                                 │
│ Item │ Tam │ Qtd │ Local origem │ Disponível │                            │
│ [▣] Capacete seg. │ — │ {6} │ DEP-CENTRAL │ 30 ✓ │ [✕]                    │
│ [▣] Luva vaqueta │ {M ▼} │ {10} │ DEP-CENTRAL │ 8 ⚠ insuficiente │ [✕]    │
│ [ + Adicionar linha ]                                                     │
├──────────────────────────────────────────────────────────────────────────┤
│ #5 Pendências                                                             │
│ ✖ Linha 2: disponível 8, solicitado 10 (IV-ERR-020) → (foca a linha)      │
├──────────────────────────────────────────────────────────────────────────┤
│ #6 Ações        [ Cancelar rascunho ]  [ Salvar ]  [ Confirmar saída ]    │
└──────────────────────────────────────────────────────────────────────────┘
```

## 5.2 Desktop — Diálogo de confirmação (IV-UX-004)

```
┌───────────────────────────────────────────────┐
│ Confirmar saída SAI-0210?                     │
│                                               │
│ Capacete seg. · 6 UN · DEP-CENTRAL            │
│   saldo 36 → 30 · reserva RSV-0815 atendida   │
│                                               │
│ Esta ação é definitiva. Correções somente     │
│ por estorno.                                  │
│            [ Voltar ]  [ Confirmar ]          │
└───────────────────────────────────────────────┘
```

## 5.3 Desktop — Detalhe (confirmado)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #7 SAI-2026-000210 — Saída · ● Confirmado 🔒        [ Estornar ] #8       │
│    Origem: Atendimento MR-0512 · Reserva RSV-0815 · 23/08 14:02 · João    │
├──────────────────────────────────────────────────────────────────────────┤
│ #9 Linhas com efeito                                                      │
│ Item │ Tam │ Qtd │ Local │ Saldo antes → depois                           │
│ Capacete seg. │ — │ 6 │ DEP-CENTRAL │ 42/36 → 36/30 (total/disp.)         │
├──────────────────────────────────────────────────────────────────────────┤
│ #10 Vínculos: [Reserva RSV-0815] [Solicitação MR-0512]                    │
│ #11 [Histórico (timeline)] [Auditoria*]                                   │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#3 Origem** — obrigatória e visível (IV-BR-013); pré-carregada em entrada por recebimento e saída por reserva.
- **#4 Linhas** — disponibilidade da origem consultada na seleção; tamanho revelado quando o item tem grade (IV-BR-120); pendências ancoradas por linha com os números do conflito (IV-UX-002).
- **#8 Estorno** — somente Confirmado, com IV-PERM-007; abre diálogo reforçado: motivo (mín. 10 caracteres) + resumo do efeito inverso; bloqueio IV-ERR-113 exibe o saldo atual.
- **#9 Efeito por linha** — saldo anterior/posterior gravado na confirmação (IV-BR-095); selo 🔒 de imutabilidade.
- Transferência usa o mesmo formulário com origem **e** destino por linha e validação origem ≠ destino (IV-ERR-050).
- Tablet/Mobile: formulário em coluna única; linhas em cartões; confirmação em tela cheia com o mesmo resumo.

## 5.4 Estados e diálogos

| Situação | Comportamento |
|----------|---------------|
| Conflito de versão (`409 IV-ERR-409`) | Modal "alterado por outro usuário" + `[ Recarregar ]`; nunca sobrescreve |
| Concorrência de saldo (`409 IV-ERR-409`) | "Este saldo está sendo movimentado. Tente novamente." + retry |
| Reserva vencida (`422 IV-ERR-030`) | Mensagem + atalho `[ Criar nova reserva ]` (FA-IV-006) |
| Troca de tamanho (IV-BR-121) | MSG-IV-UC-002-C + atalhos `[ Liberar reserva ]` `[ Nova reserva ]` |
| Segregação (`422 IV-ERR-070`) | Bloqueio sem vazamento de disponibilidade de outro contrato |
| Estornado/Cancelado | Faixa de destaque com motivo; somente leitura |

---

# 6. WF-IV-04 — Ajustes (IV-SCR-04)

**Casos de uso:** UC-IV-006. **Endpoints:** `POST /adjustments`, `GET /adjustments`, `POST …/approve`, `POST …/reject`.

## 6.1 Desktop — Registro

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Novo Ajuste                                                            │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 {Motivo * ▼ (Master Data)}   (•) Negativo  ( ) Positivo                │
│ {Justificativa * (mín. 10 caracteres)………………………………} 42/1000                │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Linhas                                                                 │
│ Item │ Tam │ Local │ Δ Qtd │ Saldo atual → resultante                     │
│ [▣] Luva vaqueta │ M │ DEP-CENTRAL │ {−3} │ 120 → 117                     │
│ [ + Adicionar linha ]                                                     │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 ℹ Quem registra não aprova o próprio ajuste (SoD)                      │
│ #5                       [ Cancelar ]  [ Registrar para aprovação ]       │
└──────────────────────────────────────────────────────────────────────────┘
```

## 6.2 Desktop — Fila de aprovação

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #6 Ajustes  [Pendentes (4)] [Decididos]        {período} {registrador ▼}  │
├──────────────────────────────────────────────────────────────────────────┤
│ #7 ┌────────────────────────────────────────────────────────────────────┐│
│    │ ADJ-0042 · Divergência de inventário · há 6h  (SLA 24h)            ││
│    │ Registrado por: Maria (Supervisora) · Inventário INV-0007          ││
│    │ Luva vaqueta M · DEP-CENTRAL · −3 (120 → 117)                      ││
│    │ "Contagem física confirmada em recontagem…"                        ││
│    │                       [ Rejeitar ] [ Aprovar ]                     ││
│    └────────────────────────────────────────────────────────────────────┘│
│    ┌────────────────────────────────────────────────────────────────────┐│
│    │ ADJ-0043 · Avaria · há 26h  ⚠ SLA estourado (ESC-IV-001)           ││
│    │ Registrado por: VOCÊ — aguardando outro aprovador (SoD)            ││
│    │ (sem ações de decisão)                                             ││
│    └────────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#2 Motivo estruturado** — combo do Master Data (IV-BR-081), nunca lista fixa; justificativa com contador (mín. 10).
- **#3 Efeito proposto** — saldo atual → resultante por linha; ajuste negativo valida IV-BR-084 no registro **e** na aprovação.
- **#7 SoD visível** — cartões do próprio usuário sem ações de decisão e com selo (CA-UX-IV-08); rejeição abre modal com motivo obrigatório (IV-ERR-086); aprovação abre modal com efeito por linha + comentário opcional.
- Tablet: cartões em coluna única; decisão pelos mesmos modais.

---

# 7. WF-IV-05 — Inventário (IV-SCR-05)

**Casos de uso:** UC-IV-007. **Endpoints:** `POST /counts`, `POST …/start`, `POST …/entries`, `GET …/divergences`, `POST …/close`, `POST …/cancel`.

## 7.1 Desktop — Abertura

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Novo Inventário                                                        │
│ (•) Cíclico  classes: [x] A [ ] B [ ] C   ( ) Geral                       │
│ Locais: [x] DEP-CENTRAL [ ] DEP-OBRA-14      Prazo: {26/08/2026}          │
│ Responsável: {Maria ▼}                                                    │
│ ℹ 120 chaves de saldo no escopo                                           │
│                                  [ Cancelar ]  [ Abrir inventário ]       │
└──────────────────────────────────────────────────────────────────────────┘
```

## 7.2 Desktop — Contagem (cega)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #2 INV-0007 — ● Em Contagem      Progresso: 87/120 chaves     Prazo 26/08 │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Lista de contagem (agrupada por local; SEM saldo do sistema)           │
│ DEP-CENTRAL                                                               │
│  Capacete de segurança │ — │ contado: {36}  [ Registrar ]                 │
│  Luva vaqueta │ M │ contado: {117} [ Registrar ]  ✓ registrado 14:05      │
│ ···                                                                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #4        [ Cancelar inventário ]      [ Encerrar lançamentos ]           │
└──────────────────────────────────────────────────────────────────────────┘
```

## 7.3 Desktop — Apuração e fechamento

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #5 Apuração de divergências (tolerância: 0)                               │
│ Item │ Tam │ Local │ Sistêmico │ Contado │ Divergência │ Tratamento       │
│ Luva vaqueta │ M │ DEP-CENTRAL │ 120 │ 117 │ −3 │ ADJ-0042 ● Aprovado     │
│ Capacete seg. │ — │ DEP-CENTRAL │ 36 │ 36 │ 0 │ —                         │
│ ···                                                                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #6 Pendências de fechamento (IV-ERR-102)                                  │
│ ✖ 1 divergência com ajuste pendente → (abre ADJ-0044)                     │
│ #7                                     [ Fechar inventário ]              │
│    Fechamento exibe: contadas 120 · divergentes 3 · acuracidade 97,5%     │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#3 Contagem cega** — saldo do sistema oculto (`count.blind`, CA-UX-IV-09); teclado numérico otimizado; progresso do escopo sempre visível.
- **#6 Fechamento condicionado** — lista clicável de pendências (ajuste não concluído/sem justificativa); botão habilitado apenas com tudo tratado.
- Cancelamento: confirmação reforçada + motivo + aviso de preservação dos lançamentos.
- Mobile: contagem em cartões por local, um item por cartão — operação em campo de primeira classe (MMS-004-14, seção 8).

---

# 8. WF-IV-06 — Locais de Armazenagem (IV-SCR-06)

**Casos de uso:** UC-IV-009. **Endpoints:** `GET/POST/PATCH /locations`, `POST …/inactivate`.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Locais de Armazenagem                            [ Novo local ]        │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 Árvore                                                                 │
│ ▼ ALMOX-01 Almoxarifado Central                     ● Ativo               │
│    ▼ DEP-CENTRAL Depósito Central (saldo: 214 itens) ● Ativo  [Editar]    │
│       ▸ END-A-01 Corredor A / Prateleira 01          ● Ativo  [Editar]    │
│       ▸ END-A-02 ···                                                      │
│    ▸ DEP-OBRA-14 Depósito Obra 14 (saldo: 0)  ● Ativo [Editar][Inativar]  │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Formulário (modal): {Código *} {Descrição *} {Tipo ▼} {Local pai ▼}    │
│ #4 Inativação bloqueada exibe posição: "12 chaves com saldo, 3 reservas"  │
└──────────────────────────────────────────────────────────────────────────┘
```

- **#2 Árvore** — três níveis (IV-BR-060); nível endereço exibido apenas quando `addressing.level = address`; `[Inativar]` visível somente em local com saldo zero e sem reservas (IV-BR-063 — caso contrário a zona #4 explica o bloqueio).
- **#3 Hierarquia** — tipo do pai validado; ciclo recusado com IV-ERR-062; código duplicado com IV-ERR-061 em linha.

---

# 9. WF-IV-07 — Alertas de Estoque (IV-SCR-07)

**Casos de uso:** UC-IV-010. **Endpoints:** `GET /alerts`, `POST …/acknowledge`.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Alertas de Estoque   [Abertos (7)] [Normalizados]  {tipo ▼} {local ▼}  │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 ┌────────────────────────────────────────────────────────────────────┐│
│    │ 🔴 RUPTURA · Capacete de segurança · DEP-CENTRAL · há 3h           ││
│    │ Disponível: 0 · Demanda aberta: 2 solicitações (8 UN)              ││
│    │ ⚠ escala ao Gestor em 45h se não normalizar (ESC-IV-004)           ││
│    │ [ Ver posição ] [ Registrar ciência ]                              ││
│    └────────────────────────────────────────────────────────────────────┘│
│    ┌────────────────────────────────────────────────────────────────────┐│
│    │ 🟡 MÍNIMO · Luva vaqueta M · DEP-CENTRAL · há 1d                   ││
│    │ Disponível: 8 · Mínimo: 10 · Déficit: 2                            ││
│    │ ciência: João, 22/08 "compra em andamento PR-0033"                 ││
│    │ [ Ver posição ] [ Registrar ciência ]                              ││
│    └────────────────────────────────────────────────────────────────────┘│
│ ···                                          [ Carregar mais ]           │
└──────────────────────────────────────────────────────────────────────────┘
```

- Ruptura sempre no topo (prioridade), com ícone + rótulo textual (nunca só cor); demanda aberta com solicitações navegáveis.
- Ciência com comentário opcional; o alerta permanece Aberto até a normalização por entrada de saldo (POL-IV-09); ciência auditada.
- Mobile: mesma estrutura em cartões de coluna única.

---

# 10. WF-IV-08 — Configurações do Módulo (IV-SCR-08)

**Serviço:** FD-001-10. **Acesso:** somente Admin (IV-PERM-011).

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Configurações — Materiais / Estoque                                    │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 Grupos de parâmetros (materials.inventory.*)                           │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ Chave                                     │ Escopo      │ Valor │    │ │
│ │ materials.inventory.reservation.ttl.hours │ Organização │ 72    │ [E]│ │
│ │ materials.inventory.expiring-window.hours │ Organização │ 24    │ [E]│ │
│ │ materials.inventory.adjustment.approval-… │ Organização │ true  │ [E]│ │
│ │ materials.inventory.addressing.level      │ Organização │ deposit│[E]│ │
│ │ materials.inventory.count.tolerance       │ Organização │ 0     │ [E]│ │
│ │ materials.inventory.alerts.recipients.*   │ Organização │ ···   │ [E]│ │
│ │ ···                                                                  │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
│ #3 Edição: modal com valor validado pelo schema + descrição do efeito     │
│    operacional + {motivo da alteração *}                                  │
│ #4 ⚠ Mudança de addressing.level exige migração assistida (UC-IV-009 A2)  │
└──────────────────────────────────────────────────────────────────────────┘
```

- Cada parâmetro exibe a descrição do efeito operacional (IV-UX-008); alterações auditadas; `addressing.level` com aviso de migração assistida.

---

# 11. Matriz de Rastreabilidade

| Wireframe | UCs | Endpoints | Componentes Foundation | Critérios UX |
|-----------|-----|-----------|------------------------|--------------|
| WF-IV-01 | UC-IV-002/003/004 | `GET /reservations`, `GET /movements` | FD-001-05 (badges) | CA-UX-IV-01/03/07 |
| WF-IV-02 | UC-IV-011 | `GET /balances`, `GET …/statement` | FD-001-06 | CA-UX-IV-01/04 |
| WF-IV-03 | UC-IV-001/002/005/008 | movements + confirm/cancel/reverse + transfers | FD-001-06/07, MMS-002 (busca de item) | CA-UX-IV-01/04/05/06 |
| WF-IV-04 | UC-IV-006 | adjustments + approve/reject | FD-001-09 (motivos), FD-001-04 | CA-UX-IV-05/08 |
| WF-IV-05 | UC-IV-007 | counts + entries/divergences/close/cancel | FD-001-05 | CA-UX-IV-05/09 |
| WF-IV-06 | UC-IV-009 | locations + inactivate | FD-001-02 | CA-UX-IV-01/06 |
| WF-IV-07 | UC-IV-010 | alerts + acknowledge | FD-001-05 | CA-UX-IV-01/06 |
| WF-IV-08 | — | endpoints FD-001-10 | FD-001-10 | CA-UX-IV-03 |

Todos os wireframes atendem CA-UX-IV-02 (mensagens do catálogo), CA-UX-IV-10 (WCAG 2.1 AA) e responsividade (MMS-004-14, seção 8), verificados nos cenários do MMS-004-17.

---

# 12. Critérios de Aceite de Wireframes

| Código | Critério |
|--------|----------|
| CA-WF-IV-01 | Todo wireframe corresponde a uma tela do MMS-004-14 (seção 4.1), sem telas adicionais |
| CA-WF-IV-02 | Toda zona/ação rastreável a UC, endpoint ou componente Foundation (matriz da seção 11) |
| CA-WF-IV-03 | Estados carregando/vazio/erro/sem permissão definidos para todas as telas de lista e formulário |
| CA-WF-IV-04 | Variantes responsivas definidas conforme os breakpoints do MMS-004-14 (seção 8), com atendimento e contagem de primeira classe em mobile |
| CA-WF-IV-05 | Nenhuma definição estética (cor, fonte, espaçamento) — delegada ao DS-001 |
| CA-WF-IV-06 | Nenhuma zona permite edição de saldo; diálogos de confirmação exibem o efeito por linha (saldo antes → depois) |
| CA-WF-IV-07 | Seleção de tamanho condicional à grade do item (IV-BR-120); contagem cega sem saldo do sistema |
| CA-WF-IV-08 | Cartões de ajuste do próprio usuário sem ações de decisão (SoD visível) |

---

# 13. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | WF-IV-01 a WF-IV-08 conforme este documento |
| **v1.1** | Wireframes de transferência em trânsito, abertura automática de inventário cíclico (sugestão de escopo), exportação CSV do extrato e relatórios (posição, curva ABC, acuracidade) |
| **v2.0** | Wireframes de operação por código de barras/QR (atendimento e contagem), lote/validade/série nas linhas e painel de rebalanceamento |

---

# 14. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-23 | Criação dos wireframes do módulo: 8 telas (fila do almoxarifado por vencimento, posição/extrato com saldos antes→depois, documento com diálogo de efeito e estorno, ajustes com SoD visível, inventário com contagem cega e fechamento condicionado, locais, alertas com ruptura priorizada, configurações) com layout, zonas, variantes responsivas, estados, matriz de rastreabilidade e critérios de aceite CA-WF-IV |
