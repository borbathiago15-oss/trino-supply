**Documento:** PR-001-15 — Wireframes
**Módulo:** PR-001 — Purchase Requisition (Solicitação de Compra)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** PR-001-14 (UX), PR-001-07 (Use Cases), PR-001-09 (Permissions), PR-001-13 (API), DS-001 (Design System)
**Referências:** PR-001-03 (State Machine), PR-001-12 (Business Journey)

---

# 1. Objetivo

Este documento especifica os wireframes oficiais das telas do módulo **Purchase Requisition**, definindo estrutura, zonas, hierarquia de conteúdo e comportamento responsivo de cada tela listada na arquitetura de informação do **PR-001-14 (seção 4)**.

Regras de vínculo:

- Os wireframes materializam o PR-001-14; nenhuma zona, ação ou mensagem pode ser incluída sem correspondência em um UC (PR-001-07), endpoint (PR-001-13) ou componente Foundation.
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

| Wireframe | Tela (PR-001-14) | Nome | Breakpoints |
|-----------|------------------|------|-------------|
| WF-01 | UX-SCR-01 | Lista de Requisições | Desktop / Tablet / Mobile |
| WF-02 | UX-SCR-02 | Formulário Criar/Editar PR | Desktop / Mobile |
| WF-03 | UX-SCR-03 | Detalhe da PR | Desktop / Tablet / Mobile |
| WF-04 | UX-SCR-04 | Fila de Aprovações | Desktop / Mobile |
| WF-05 | UX-SCR-05 | Detalhe de Aprovação | Desktop / Mobile |
| WF-06 | UX-SCR-06 | Centro de Notificações | Desktop / Mobile |
| WF-07 | UX-SCR-07 | Configurações do Módulo | Desktop |

Cada wireframe inclui: layout desktop, variantes responsivas, estados (carregando, vazio, erro, sem permissão) e legenda de zonas.

---

# 3. WF-01 — Lista de Requisições (UX-SCR-01)

**Casos de uso:** UC-008. **Endpoint:** `GET /v1/purchase-requisitions` (keyset).

## 3.1 Desktop (≥ 1280 px)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Requisições                                    [ Nova Requisição ] #2 │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Filtros                                                                │
│ {texto livre: número/justificativa} {status ▼} {período ▼}                │
│ {centro de custo ▼} {solicitante ▼}                    [ Limpar filtros ] │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 Tabela                                                                 │
│ Número ▲ │ Justificativa │ Centro de Custo │ Data │ Valor est. │ Status  │
│ PR-0042  │ ···           │ ···             │ ···  │ ···        │ ● Rasc. │
│ PR-0041  │ ···           │ ···             │ ···  │ ···        │ ● Subm. │
│ ···                                                                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #5 {N} resultados                                    [ Carregar mais ]    │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Título** — rótulo conforme contexto de navegação ("Minhas Requisições", "Todas as Requisições", "Fila de Compras" com filtro Aprovada travado).
- **#2 Ação primária** — visível apenas com PR-PERM de criação (Requester/Admin); oculta caso contrário (CA-UX-03).
- **#3 Filtros** — todos opcionais e combináveis; aplicados no servidor; "Limpar filtros" restaura o padrão do contexto.
- **#4 Tabela** — ordenação padrão mais recentes primeiro; coluna Status com rótulo textual + cor (nunca só cor); clique na linha abre WF-03.
- **#5 Paginação keyset** — botão "Carregar mais" (cursor), nunca paginação numerada; contador reflete o total do filtro.

## 3.2 Tablet (768–1279 px)

Colunas mantidas: Número, Justificativa, Data, Status. Centro de custo e valor estimado migram para o detalhe. Filtros colapsam em painel `[ Filtros ▼ ]`.

## 3.3 Mobile (< 768 px)

```
┌─────────────────────────────┐
│ #1 Minhas Requisições       │
│ [+ Nova]  [ Filtros ▼ ]     │
├─────────────────────────────┤
│ ┌─────────────────────────┐ │
│ │ PR-0042      ● Rascunho │ │
│ │ Justificativa ···       │ │
│ │ 28/07/2026   R$ ···     │ │
│ └─────────────────────────┘ │
│ ┌─────────────────────────┐ │
│ │ PR-0041     ● Submetida │ │
│ │ ···                     │ │
│ └─────────────────────────┘ │
│ ···                         │
│ [ Carregar mais ]           │
└─────────────────────────────┘
```

Cartões em vez de tabela; número, status, data e valor estimado nunca omitidos (PR-001-14, seção 8).

## 3.4 Estados

| Estado | Composição |
|--------|------------|
| Carregando | Skeleton de 5 linhas na forma da tabela/cartões |
| Vazio | Ilustração neutra + "Nenhuma requisição encontrada" + `[ Nova Requisição ]` (se permitido) |
| Erro | "Não foi possível carregar as requisições" + código de suporte + `[ Tentar novamente ]` |
| Sem permissão | Tela de acesso negado com orientação (o item de menu é ocultado; estado ocorre apenas por URL direta) |

---

# 4. WF-02 — Formulário Criar/Editar PR (UX-SCR-02)

**Casos de uso:** UC-001, UC-002, UC-006 (edição em Returned). **Endpoints:** `POST/PATCH /v1/purchase-requisitions`, `POST/PATCH/DELETE .../items`.

## 4.1 Desktop

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Nova Requisição (ou Requisição {número} — ● Rascunho)                 │
│    salvo às 14:32 ✓  ← #2 indicador de autosave                          │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Motivo do retorno (SOMENTE em Returned)                                │
│ ⚠ "Descreva os ajustes necessários: ···" — motivo registrado pelo aprov. │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 Cabeçalho                                                              │
│ {Justificativa *}                                                         │
│ {Centro de custo * ▼}   {Data necessária *}   {Origem da necessidade ▼}   │
├──────────────────────────────────────────────────────────────────────────┤
│ #5 Itens                                                                  │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ # │ Produto/Serviço │ Descrição/Escopo │ Qtde │ UM │ Data nec. │ [✕] │ │
│ │ 1 │ ···             │ ···              │ ···  │···│ ···       │ [✕] │ │
│ │ ···                                                                  │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
│ [ + Adicionar item ]                                                      │
├──────────────────────────────────────────────────────────────────────────┤
│ #6 Anexos (FD-001-03, UC-010)                                             │
│ [ Anexar documento ]   contrato.pdf ✓   especificacao.docx ✓              │
├──────────────────────────────────────────────────────────────────────────┤
│ #7 Alertas inteligentes (não bloqueantes)                                 │
│ ⚠ Item semelhante solicitado recentemente (PR-0039)                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #8 Resumo de pendências (exibido ao tentar enviar com falhas)             │
│ ● Centro de custo obrigatório → (foca o campo)   ● ···                    │
├──────────────────────────────────────────────────────────────────────────┤
│ #9 Ações                                                                  │
│                                    [ Excluir rascunho ]  [ Salvar ]       │
│                                    [ Enviar para aprovação ]              │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Título + status** — em edição exibe número e status; o formulário só é editável em Draft e Returned (demais status redirecionam para WF-03 em leitura).
- **#2 Autosave (PR-UX-006)** — três estados: "salvando...", "salvo às HH:mm ✓", "erro ao salvar — [tentar novamente]".
- **#3 Faixa de retorno** — somente em Returned; destaca o motivo registrado no UC-006; apenas os campos liberados permanecem editáveis.
- **#4 Cabeçalho** — campos de submissão (PR-BR-020) marcados com `*`; em rascunho a exigência é relaxada (UC-001/A2).
- **#5 Itens** — edição em grade em linha; validação por campo no blur; exclusão de item com confirmação simples.
- **#6 Anexos** — upload com progresso; erro de tipo/tamanho exibe mensagem do FD-001-03.
- **#7 Alertas inteligentes** — avisos do PR-001-12 (seção 7), nunca bloqueiam o envio.
- **#8 Resumo de pendências** — lista clicável (MSG-UC-003-B) que move o foco ao campo com problema.
- **#9 Ações** — "Excluir rascunho" (somente Draft, confirmação PR-UX-004); "Enviar para aprovação" abre diálogo de confirmação com resumo (nº de itens, centro de custo) e, em sucesso, redireciona para WF-03 com MSG-UC-003-A.

## 4.2 Mobile

Coluna única: cabeçalho → itens (cada item vira um cartão editável) → anexos → ações fixas no rodapé (`[ Salvar ]` / `[ Enviar ]`). Criação com muitos itens é otimizada para desktop (PR-001-14, seção 8).

## 4.3 Estados e diálogos

| Situação | Comportamento |
|----------|---------------|
| Conflito de versão (PR-ERR-409) | Modal: "Este registro foi alterado por outro usuário" + `[ Recarregar ]`; nunca sobrescreve |
| Workflow inexistente (EXC-001) | Banner + MSG-UC-003-C |
| Erro de campo (422) | Mensagem em linha ancorada + resumo de pendências |
| Carregando (edição) | Skeleton do formulário completo |

---

# 5. WF-03 — Detalhe da PR (UX-SCR-03)

**Casos de uso:** UC-008, UC-009, UC-010, UC-011. **Endpoints:** `GET /v1/purchase-requisitions/{id}` + sub-recursos.

## 5.1 Desktop

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 PR-0042                                    ● Submetida                 │
│    Solicitante: ··· │ Unidade: ··· │ Centro de custo: ··· │ Criada: ···  │
│ #2 Ações (conforme status + permissões)                                   │
│    [ Editar ] [ Enviar ] [ Cancelar ] [ Exportar ]                        │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Workflow de aprovação (FD-001-04)                                      │
│    Supervisor ✓ ──► Gerente ● (em andamento) ──► Diretor ○ ──► Compras ○  │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 Abas                                                                   │
│ [Itens] [Anexos (2)] [Comentários (3)] [Histórico] [Auditoria*]           │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ Aba ativa: Itens — grade somente leitura com totais                  │ │
│ │ Aba Anexos: lista com download (URL assinada) e envio (se permitido) │ │
│ │ Aba Comentários: thread FD-001-08; internos só p/ Approver/Buyer/    │ │
│ │   Manager/Admin (POL-AUTH-005); menções com autocomplete             │ │
│ │ Aba Histórico: timeline FD-001-07 (UC-009), paginação keyset         │ │
│ │ Aba Auditoria*: trilha FD-001-06 — somente PR-PERM-013               │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Cabeçalho fixo** — número, status (rótulo + cor) e dados-chave sempre visíveis.
- **#2 Ações por status** — Draft: Editar/Enviar/Excluir; Returned: Editar; Submetida/Em aprovação: Cancelar (se permitido, UC-007, com confirmação + motivo); qualquer estado: Exportar (conforme matriz). Ações não permitidas são ocultadas.
- **#3 Cadeia de aprovação** — níveis com estado visual (concluído/em andamento/pendente); delegação vigente indicada ("agindo como delegado de {gestor}").
- **#4 Abas** — contadores em Anexos e Comentários; aba Auditoria oculta sem PR-PERM-013.

## 5.2 Tablet e Mobile

Tablet: cabeçalho e cadeia mantidos; abas viram navegação de segundo nível. Mobile: cabeçalho compacto, cadeia em lista vertical com marcos, abas em acordeão; timeline em cartões cronológicos.

## 5.3 Estados

Rejeitada: faixa de destaque com o motivo da rejeição; tela toda somente leitura; ação oferecida = `[ Nova Requisição ]` (fluxo manual UC-005/A2). Cancelada: mesma regra, com o motivo do cancelamento.

---

# 6. WF-04 — Fila de Aprovações (UX-SCR-04)

**Casos de uso:** UC-004, UC-005, UC-006. **Endpoint:** `GET /v1/approvals/pending`.

## 6.1 Desktop

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Aprovações Pendentes ({n})                                             │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 Ordenação: [ Prazo/SLA ▼ ]   Filtro: {unidade ▼} {texto livre}         │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Lista                                                                  │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ PR-0042 │ Nível: Gerente │ Solicitante: ··· │ CC: ··· │ Valor: ···   │ │
│ │ SLA: vence em 8h ▲ │ aguardando há 1d 4h                             │ │
│ ├──────────────────────────────────────────────────────────────────────┤ │
│ │ ···                                                                  │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
│ #4 [ Carregar mais ]                                                      │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Título com total** — igual ao badge do menu (PR-001-14, 4.3).
- **#2 Ordenação padrão por prazo/SLA** — itens próximos do vencimento primeiro, com indicador de urgência.
- **#3 Cartões de fila** — resumo decisório mínimo (solicitante, centro de custo, valor estimado, nível, SLA); clique abre WF-05. Indicação visual quando o usuário atua como delegado.
- Sem estado "sem permissão": quem não é aprovador não vê o menu; URL direta → acesso negado.

## 6.2 Mobile

Cartões em coluna única com as mesmas informações; decisão acontece apenas dentro de WF-05 (nunca em ação rápida na lista, para preservar o parecer documentado).

## 6.3 Estados

Vazio: "Nenhuma aprovação pendente" (estado positivo, sem ação). Carregando/erro conforme padrão da seção 3.4.

---

# 7. WF-05 — Detalhe de Aprovação (UX-SCR-05)

**Casos de uso:** UC-004, UC-005, UC-006. **Endpoints:** `POST .../approve | /reject | /return`.

## 7.1 Desktop

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Aprovação — PR-0042                    Nível: Gerente (2 de 4)         │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 Resumo decisório                                                       │
│    Solicitante: ··· │ Unidade: ··· │ Centro de custo: ···                 │
│    Valor estimado: ··· │ Data necessária: ···                             │
│    Justificativa: ···                                                     │
│ #3 Itens (grade resumida) ···                                             │
│ #4 Anexos: contrato.pdf [baixar] ···                                      │
│ #5 Histórico resumido (últimos marcos da timeline)                        │
├──────────────────────────────────────────────────────────────────────────┤
│ #6 Parecer                                                                │
│    {parecer — opcional para aprovar; obrigatório para rejeitar/retornar}  │
├──────────────────────────────────────────────────────────────────────────┤
│ #7 Ações                                                                  │
│         [ Retornar para ajustes ]  [ Rejeitar ]  [ Aprovar ]              │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#2–#5 Resumo decisório** — contexto completo em uma dobra (persona Gestor Aprovador, PR-001-01).
- **#6 Parecer** — obrigatoriedade dinâmica: exigido ao escolher Rejeitar (MSG-UC-005-B) ou Retornar (MSG-UC-006-B).
- **#7 Ações** — cada decisão abre modal de confirmação (PR-UX-004) com o parecer; sucesso → mensagem do UC (MSG-UC-004-A/B, 005-A, 006-A) e retorno à fila WF-04.
- **SoD/alçada** — botões desabilitados com explicação acessível MSG-UC-004-C (ABAC-01/02); indicação de delegação vigente quando aplicável.

## 7.2 Mobile

Coluna única; resumo em acordeões (Itens, Anexos, Histórico); ações fixas no rodapé. Aprovação por mobile é suportada integralmente (PR-001-14, seção 8).

---

# 8. WF-06 — Centro de Notificações (UX-SCR-06)

**Serviço:** FD-001-05 (Notification Center). **Escopo no módulo:** entrada pela campainha do shell; itens PR-NTF linkam para WF-03/WF-05.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Notificações                    [ Marcar todas como lidas ]            │
│ #2 Filtro: ( ) Todas  ( ) Não lidas        {tipo ▼}                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Lista                                                                  │
│ ● "Solicitação PR-0042 retornada para ajustes"      há 2h        [abrir]  │
│ ○ "Solicitação PR-0041 aprovada e liberada p/ Compras" ontem     [abrir]  │
│ ···                                                                       │
│ #4 [ Carregar mais ]                                                      │
└──────────────────────────────────────────────────────────────────────────┘
```

- **#3** — não lidas com marcador; clique marca como lida e navega ao detalhe da PR; paginação keyset; preferências de canal respeitam FD-001-05 (a tela não altera preferências, apenas consome).
- Mobile: mesma estrutura em coluna única.

---

# 9. WF-07 — Configurações do Módulo (UX-SCR-07)

**Serviço:** FD-001-10. **Acesso:** somente Admin.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Configurações — Suprimentos / Requisições                              │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 Grupos de parâmetros (procurement.pr.*)                                │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ Chave                          │ Escopo      │ Valor        │ [Editar]│ │
│ │ procurement.pr.cancel.policy   │ Organização │ ···          │ [Editar]│ │
│ │ ···                                                                 │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
│ #3 Edição: modal com valor validado pelo schema da definição +            │
│    {motivo da alteração *} (CF-BR, reason obrigatório)                    │
└──────────────────────────────────────────────────────────────────────────┘
```

- Valores sensíveis exibidos como referência (SecretRef), nunca em claro (FD-001-10).
- Chaves com `requiresDualControl` exibem indicação de duplo controle.
- Sem criação de chaves pela tela: definições são versionadas no Foundation.

---

# 10. Matriz de Rastreabilidade

| Wireframe | UCs | Endpoints | Componentes Foundation | Critérios UX |
|-----------|-----|-----------|------------------------|--------------|
| WF-01 | UC-008 | `GET /v1/purchase-requisitions` | — | CA-UX-01/03/07 |
| WF-02 | UC-001, UC-002, UC-006, UC-010 | CRUD requisição + items + attachments | FD-001-03 | CA-UX-01/05/06 |
| WF-03 | UC-008, UC-009, UC-010, UC-011 | detalhe + timeline + attachments + comments | FD-001-03/04/06/07/08 | CA-UX-01/03 |
| WF-04 | UC-004/005/006 | `GET /v1/approvals/pending` | FD-001-04 | CA-UX-03/07 |
| WF-05 | UC-004/005/006 | approve/reject/return | FD-001-04 | CA-UX-03/06 |
| WF-06 | notificações PR-NTF | endpoints FD-001-05 | FD-001-05 | CA-UX-07 |
| WF-07 | — | endpoints FD-001-10 | FD-001-10 | CA-UX-03 |

Todos os wireframes atendem CA-UX-02 (mensagens do catálogo), CA-UX-04 (WCAG 2.1 AA) e CA-UX-08 (responsividade), verificados nos cenários do PR-001-17.

---

# 11. Critérios de Aceite de Wireframes

| Código | Critério |
|--------|----------|
| CA-WF-01 | Todo wireframe corresponde a uma tela do PR-001-14 (seção 4.1), sem telas adicionais |
| CA-WF-02 | Toda zona/ação rastreável a UC, endpoint ou componente Foundation (matriz da seção 10) |
| CA-WF-03 | Estados carregando/vazio/erro/sem permissão definidos para todas as telas de lista e formulário |
| CA-WF-04 | Variantes responsivas definidas conforme os breakpoints do PR-001-14 (seção 8) |
| CA-WF-05 | Nenhuma definição estética (cor, fonte, espaçamento) — delegada ao DS-001 |

---

# 12. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | WF-01 a WF-07 conforme este documento |
| **v1.1** | Wireframes da duplicação de requisição (UC-012), anexos por item (UC-013) e exportação de detalhe (UC-014) |
| **v2.0** | Wireframes da fila de Compras enriquecida e do painel de métricas de UX (PR-001-14, seção 14) |

---

# 13. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação dos wireframes do módulo: 7 telas com layout, zonas, variantes responsivas, estados, matriz de rastreabilidade e critérios de aceite |
