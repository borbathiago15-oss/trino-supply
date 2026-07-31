**Documento:** MMS-002-15 — Wireframes
**Módulo:** MMS-002 — Item Catalog (Materials Domain)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002-14 (UX), MMS-002-07 (Use Cases), MMS-002-09 (Permissions), MMS-002-13 (API), DS-001 (Design System)
**Referências:** MMS-002-03 (State Machine), MMS-002-12 (Business Journey), PR-001-15 (Wireframes do Purchase Requisition — padrão de formato)

---

# 1. Objetivo

Este documento especifica os wireframes oficiais das telas do módulo **Item Catalog**, definindo estrutura, zonas, hierarquia de conteúdo e comportamento responsivo de cada tela listada na arquitetura de informação do **MMS-002-14 (seção 4)**.

Regras de vínculo:

- Os wireframes materializam o MMS-002-14; nenhuma zona, ação ou mensagem pode ser incluída sem correspondência em um UC (MMS-002-07), endpoint (MMS-002-13) ou componente Foundation.
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

| Wireframe | Tela (MMS-002-14) | Nome | Breakpoints |
|-----------|-------------------|------|-------------|
| WF-IC-01 | IC-SCR-01 | Lista do Catálogo | Desktop / Tablet / Mobile |
| WF-IC-02 | IC-SCR-02 | Formulário Criar/Editar Item | Desktop / Mobile |
| WF-IC-03 | IC-SCR-03 | Detalhe do Item | Desktop / Tablet / Mobile |
| WF-IC-04 | IC-SCR-04 | Centro de Notificações | Desktop / Mobile |
| WF-IC-05 | IC-SCR-05 | Configurações do Módulo | Desktop |
| WF-IC-06 | Componente transversal | Busca de Item (embutido no MMS-003/MMS-004) | Desktop / Mobile |

Cada wireframe inclui: layout desktop, variantes responsivas, estados (carregando, vazio, erro, sem permissão) e legenda de zonas.

---

# 3. WF-IC-01 — Lista do Catálogo (IC-SCR-01)

**Casos de uso:** UC-IC-007. **Endpoint:** `GET /api/v1/items` (keyset).

## 3.1 Desktop (≥ 1280 px)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Catálogo de Itens                                  [ Novo Item ] #2   │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Busca e filtros                                                        │
│ {buscar: descrição, sinônimo, código ERP…}                                │
│ {status ▼} {grupo ▼} {CA ▼}  [x] somente sem imagem   [ Limpar filtros ]  │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 Tabela                                                                 │
│ Img │ Código ▲ │ Descrição │ Grupo │ UM │ CA │ Status                     │
│ [▣] │ EPI-LUV-001 │ Luva vaqueta │ EPI │ PAR │ ● válido │ ● Ativo        │
│ [▣] │ FAR-CAM-004 │ Camisa farda │ Fardamento │ UN │ N/A │ ● Ativo       │
│ ···                                                                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #5 {N} resultados                                   [ Carregar mais ]     │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Título** — rótulo conforme contexto de navegação ("Todos os Itens", "EPIs", "Fardamentos", "Pendências de CA" com filtro `caStatus` travado).
- **#2 Ação primária** — visível apenas com IC-PERM-001 (`items.write`: Catalog Maintainer/Admin); oculta caso contrário (CA-UX-IC-03).
- **#3 Busca e filtros** — busca `q` única (descrição/sinônimo/código ERP/código); filtros combináveis: status, grupo, CA (válido/vencendo/vencido/ausente/N-A), "somente sem imagem". Aplicados no servidor; "Limpar filtros" restaura o padrão do contexto.
- **#4 Tabela** — ordenação padrão por código ascendente; coluna CA com selo textual + cor (válido/vencendo/vencido/N-A); Status com rótulo textual + cor (nunca só cor); clique na linha abre WF-IC-03.
- **#5 Paginação keyset** — botão "Carregar mais" (cursor), nunca paginação numerada; contador reflete o total do filtro.

## 3.2 Tablet (768–1279 px)

Colunas mantidas: Imagem, Código, Descrição, CA, Status. Grupo e unidade migram para o detalhe. Filtros colapsam em painel `[ Filtros ▼ ]`.

## 3.3 Mobile (< 768 px)

```
┌─────────────────────────────┐
│ #1 Catálogo de Itens        │
│ [+ Novo]  [ Filtros ▼ ]     │
│ {buscar…}                   │
├─────────────────────────────┤
│ ┌─────────────────────────┐ │
│ │ [▣] EPI-LUV-001  ● Ativo│ │
│ │ Luva vaqueta            │ │
│ │ EPI · PAR · CA válido   │ │
│ └─────────────────────────┘ │
│ ┌─────────────────────────┐ │
│ │ [▣] FAR-CAM-004  ● Ativo│ │
│ │ ···                     │ │
│ └─────────────────────────┘ │
│ ···                         │
│ [ Carregar mais ]           │
└─────────────────────────────┘
```

Cartões em vez de tabela; imagem, código, descrição, status e selo de CA nunca omitidos (MMS-002-14, seção 8).

## 3.4 Estados

| Estado | Composição |
|--------|------------|
| Carregando | Skeleton de 5 linhas na forma da tabela/cartões |
| Vazio | Ilustração neutra + "Nenhum item encontrado" + `[ Novo Item ]` (se permitido); em contexto de busca sem resultado: "Nenhum item corresponde à busca" + sugestão de revisar filtros |
| Erro | "Não foi possível carregar o catálogo" + código de suporte + `[ Tentar novamente ]` |
| Sem permissão | Tela de acesso negado com orientação (o item de menu é ocultado; estado ocorre apenas por URL direta) |

---

# 4. WF-IC-02 — Formulário Criar/Editar Item (IC-SCR-02)

**Casos de uso:** UC-IC-001, UC-IC-003. **Endpoints:** `POST/PATCH /api/v1/items`, `GET …/completeness`, `PUT …/ca`, `PUT …/size-grid`, `POST …/image`, `PUT …/replenishment-parameters`, `POST/DELETE …/synonyms`.

## 4.1 Desktop

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Novo Item (ou Item {código} — ● Rascunho)                              │
│    salvo às 14:32 ✓  ← #2 indicador de autosave                           │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Dados básicos                                                          │
│ {Código *}            {Código ERP}                                        │
│ {Descrição *}                                                             │
│ {Grupo * ▼}           {Unidade * ▼}                                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 CA — Certificado de Aprovação (SOMENTE grupo EPI)                      │
│ {Número do CA *}   {Emitido em *}   {Válido até *}                        │
│ ⚠ CA vence em menos de 90 dias (aviso não bloqueante)                     │
├──────────────────────────────────────────────────────────────────────────┤
│ #5 Grade de tamanhos                                                      │
│ [x] PP [x] P [x] M [ ] G [ ] GG [ ] XG   (valores do SIZE_GRID)           │
├──────────────────────────────────────────────────────────────────────────┤
│ #6 Imagem (FD-001-03, IC-BR-083)                                          │
│ [ Enviar imagem ]   luva_vaqueta.jpg ✓                                    │
├──────────────────────────────────────────────────────────────────────────┤
│ #7 Sinônimos                                                              │
│ luva vaqueta [✕]   luva de couro [✕]   {adicionar sinônimo} [ + ]         │
├──────────────────────────────────────────────────────────────────────────┤
│ #8 Parâmetros de reposição (opcional)                                     │
│ {Ponto de reposição} {Estoque mínimo} {Estoque máximo} {Estoque segur.}   │
├──────────────────────────────────────────────────────────────────────────┤
│ #9 Checklist de completude (sempre visível)                               │
│ ✔ Descrição preenchida   ✔ Grupo e unidade válidos                        │
│ ✖ CA ausente (obrigatório p/ EPI) → (foca o campo)                        │
│ ⚠ EPI sem imagem (recomendado)                                            │
├──────────────────────────────────────────────────────────────────────────┤
│ #10 Alertas inteligentes (não bloqueantes)                                │
│ ⚠ Descrição semelhante ao item EPI-LUV-002 "Luva vaqueta reforçada"       │
├──────────────────────────────────────────────────────────────────────────┤
│ #11 Ações                                                                 │
│                              [ Excluir rascunho ]  [ Salvar ]             │
│                              [ Ativar item ]                              │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Título + status** — em edição exibe código e status; o formulário só é totalmente editável em Draft (após ACTIVE, campos estruturais ficam bloqueados com explicação — IC-BR-031).
- **#2 Autosave (IC-UX-005)** — três estados: "salvando...", "salvo às HH:mm ✓", "erro ao salvar — [tentar novamente]".
- **#3 Dados básicos** — campos de criação (IC-BR-020) marcados com `*`; em rascunho a exigência de completude é relaxada e cobrada na ativação.
- **#4 Bloco CA** — exibido apenas quando grupo = EPI (IC-BR-080); oculto para Fardamento; aviso de vencimento próximo não bloqueante.
- **#5 Grade de tamanhos** — opções vindas do Master Data SIZE_GRID (FD-001-09, IC-BR-082); nunca lista fixa.
- **#6 Imagem** — upload com progresso; erro de tipo/tamanho exibe mensagem do FD-001-03 (IC-ERR-083).
- **#7 Sinônimos** — chips removíveis; duplicidade exibe IC-ERR-040 em linha (IC-BR-041).
- **#8 Parâmetros de reposição** — validação de coerência em linha (mínimo ≤ máximo — IC-BR-061, IC-ERR-060).
- **#9 Checklist de completude** — alimentado por `GET …/completeness`; gaps (✖) clicáveis movem o foco ao campo; avisos (⚠) não bloqueiam.
- **#10 Alertas inteligentes** — duplicata provável por descrição semelhante; nunca bloqueiam.
- **#11 Ações** — "Excluir rascunho" (somente Draft, confirmação IC-UX-004); "Ativar item" abre modal com motivo + resumo do checklist (UC-IC-002); em sucesso, redireciona para WF-IC-03 com status ACTIVE.

## 4.2 Mobile

Coluna única: dados básicos → CA (se EPI) → tamanhos → imagem → sinônimos → parâmetros → checklist → ações fixas no rodapé (`[ Salvar ]` / `[ Ativar ]`). Cadastro completo é otimizado para desktop (MMS-002-14, seção 8).

## 4.3 Estados e diálogos

| Situação | Comportamento |
|----------|---------------|
| Conflito de versão (`409 IC-ERR-409`) | Modal: "Este registro foi alterado por outro usuário" + `[ Recarregar ]`; nunca sobrescreve |
| Completude insuficiente (`422 IC-ERR-020`) | Checklist destacado com os gaps retornados; foco no primeiro gap |
| Código duplicado (`409 IC-ERR-010`) / ERP duplicado (`409 IC-ERR-011`) | Mensagem em linha no campo + link para o item existente |
| Erro de campo (400/422) | Mensagem em linha ancorada |
| Carregando (edição) | Skeleton do formulário completo |

---

# 5. WF-IC-03 — Detalhe do Item (IC-SCR-03)

**Casos de uso:** UC-IC-002, UC-IC-004, UC-IC-005, UC-IC-006, UC-IC-007. **Endpoints:** `GET /api/v1/items/{id}` + ações de ciclo de vida + sub-recursos.

## 5.1 Desktop

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 [▣] EPI-LUV-001 — Luva de segurança vaqueta        ● Ativo             │
│    Grupo: EPI │ Unidade: PAR │ ERP: 1002345 │ Criado: ··· │ Versão: 4    │
│ #2 Ações de ciclo de vida (conforme status + permissões)                  │
│    [ Ativar ] [ Inativar ] [ Reativar ] [ Descartar ] [ Editar ]          │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Selo de CA (somente EPI)                                               │
│    CA 12345 · válido até 14/01/2031 · ● válido (ou ⚠ vence em 62 dias)    │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 Abas                                                                   │
│ [Dados] [Sinônimos (2)] [Tamanhos (4)] [Reposição] [Histórico] [Auditoria*]│
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ Aba Dados: descrição, grupo, unidade, ERP, imagem em destaque        │ │
│ │ Aba Sinônimos: chips + adicionar/remover (se permitido)              │ │
│ │ Aba Tamanhos: grade visual P M G GG                                  │ │
│ │ Aba Reposição: ponto/mínimo/máximo/segurança + edição (se permitido) │ │
│ │ Aba Histórico: timeline FD-001-07 (UC-IC-007), paginação keyset      │ │
│ │ Aba Auditoria*: trilha FD-001-06 — somente IC-PERM-010               │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Cabeçalho fixo** — imagem, código, descrição, status (rótulo + cor) e dados-chave sempre visíveis.
- **#2 Ações por status** — Draft: Ativar/Editar/Excluir; Active: Inativar/Editar; Inactive: Reativar/Editar/Descartar (Admin); Discarded: nenhuma ação (somente leitura). Cada ação de ciclo de vida abre modal com **motivo obrigatório** (IC-ERR-091) e confirmação (IC-UX-004, tabela 6.5 do MMS-002-14). Ações não permitidas são ocultadas.
- **#3 Selo de CA** — estado visual do certificado; vencendo → destaque de aviso; vencido → destaque crítico com orientação.
- **#4 Abas** — contadores em Sinônimos e Tamanhos; aba Auditoria oculta sem IC-PERM-010.

## 5.2 Tablet e Mobile

Tablet: cabeçalho e selo de CA mantidos; abas viram navegação de segundo nível. Mobile: cabeçalho compacto com imagem, abas em acordeão, ações de ciclo de vida em menu de contexto fixo no rodapé; timeline em cartões cronológicos.

## 5.3 Estados

Discarded: faixa de destaque com o motivo do descarte; tela toda somente leitura; nenhuma ação oferecida. Inactive: faixa informativa com motivo da inativação e data; ação primária = `[ Reativar ]` (se permitido).

---

# 6. WF-IC-04 — Centro de Notificações (IC-SCR-04)

**Serviço:** FD-001-05 (Notification Center). **Escopo no módulo:** entrada pela campainha do shell; itens IC-NOT linkam para WF-IC-03.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Notificações                    [ Marcar todas como lidas ]            │
│ #2 Filtro: ( ) Todas  ( ) Não lidas        {tipo ▼}                       │
├──────────────────────────────────────────────────────────────────────────┤
│ #3 Lista                                                                  │
│ ● "CA 12345 do item EPI-LUV-001 vence em 30 dias"    há 2h       [abrir]  │
│ ○ "Item FAR-CAM-004 ativado por Maria Silva"          ontem      [abrir]  │
│ ···                                                                       │
│ #4 [ Carregar mais ]                                                      │
└──────────────────────────────────────────────────────────────────────────┘
```

- **#3** — não lidas com marcador; clique marca como lida e navega ao detalhe do item; paginação keyset; preferências de canal respeitam FD-001-05 e MMS-002-10 (a tela não altera preferências, apenas consome).
- Mobile: mesma estrutura em coluna única.

---

# 7. WF-IC-05 — Configurações do Módulo (IC-SCR-05)

**Serviço:** FD-001-10. **Acesso:** somente Admin.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 Configurações — Materiais / Catálogo de Itens                          │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 Grupos de parâmetros (materials.item.*)                                │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ Chave                                │ Escopo      │ Valor │ [Editar]│ │
│ │ materials.item.ca.expiring-days      │ Organização │ 90    │ [Editar]│ │
│ │ materials.item.rate-limit.*          │ Organização │ ···   │ [Editar]│ │
│ │ ···                                                                 │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
│ #3 Edição: modal com valor validado pelo schema da definição +            │
│    {motivo da alteração *} (reason obrigatório)                           │
└──────────────────────────────────────────────────────────────────────────┘
```

- Valores sensíveis exibidos como referência (SecretRef), nunca em claro (FD-001-10).
- Chaves com `requiresDualControl` exibem indicação de duplo controle.
- Sem criação de chaves pela tela: definições são versionadas no Foundation.

---

# 8. WF-IC-06 — Busca de Item (componente embutido, MMS-003/MMS-004)

**Casos de uso:** UC-IC-007 (consumo). **Endpoint:** `GET /api/v1/items?q=&status=ACTIVE`. **Uso:** seleção de item na requisição (MMS-003) e em movimentações de estoque (MMS-004); comportamento idêntico em todos os consumidores (MMS-002-14, 4.2).

## 8.1 Desktop / Mobile

```
┌──────────────────────────────────────────────────────────────────────────┐
│ #1 {Buscar item: nome, sinônimo, código ERP…}                             │
├──────────────────────────────────────────────────────────────────────────┤
│ #2 Resultados (apenas ACTIVE, IC-BR-001)                                  │
│ ┌──────────────────────────────────────────────────────────────────────┐ │
│ │ [▣] Luva de segurança vaqueta — EPI-LUV-001 · PAR                    │ │
│ │     encontrado por sinônimo "luva vaqueta"                           │ │
│ ├──────────────────────────────────────────────────────────────────────┤ │
│ │ [▣] ···                                                              │ │
│ └──────────────────────────────────────────────────────────────────────┘ │
│ #3 Seleção de tamanho (somente se o item tem grade, IC-BR-082)            │
│    ( ) P  (•) M  ( ) G  ( ) GG          {Quantidade}                      │
├──────────────────────────────────────────────────────────────────────────┤
│ #4 Estado vazio                                                           │
│    "Nenhum item encontrado. Não encontrou? Solicite o cadastro ao         │
│     mantenedor do catálogo."                                              │
└──────────────────────────────────────────────────────────────────────────┘
```

Legenda:

- **#1 Campo único** — tolerante a acentos e caixa (IC-BR-042); debounce de digitação; mínimo de 2 caracteres.
- **#2 Resultados** — imagem, descrição oficial, código, unidade; selo `matchedBy` (descrição/sinônimo/ERP/código) evidenciando como casou; apenas itens ACTIVE.
- **#3 Tamanho e quantidade** — grade exibida somente quando o item possui tamanhos; seleção obrigatória nesse caso; itens sem grade não exigem tamanho.
- **#4 Estado vazio** — orientação de escalonamento ao mantenedor (Jornada B, MMS-002-12, seção 5).
- Mobile: resultados em cartões empilhados; seleção de tamanho em chips.

---

# 9. Matriz de Rastreabilidade

| Wireframe | UCs | Endpoints | Componentes Foundation | Critérios UX |
|-----------|-----|-----------|------------------------|--------------|
| WF-IC-01 | UC-IC-007 | `GET /api/v1/items` | — | CA-UX-IC-01/03/07 |
| WF-IC-02 | UC-IC-001, UC-IC-003 | CRUD item + ca + size-grid + image + synonyms + replenishment + completeness | FD-001-03, FD-001-09 | CA-UX-IC-01/05/06/08 |
| WF-IC-03 | UC-IC-002/004/005/006/007 | detalhe + activate/inactivate/reactivate/discard + history/timeline | FD-001-06/07 | CA-UX-IC-01/03/06 |
| WF-IC-04 | notificações IC-NOT | endpoints FD-001-05 | FD-001-05 | CA-UX-IC-07 |
| WF-IC-05 | — | endpoints FD-001-10 | FD-001-10 | CA-UX-IC-03 |
| WF-IC-06 | UC-IC-007 (consumo) | `GET /api/v1/items?q=` | FD-001-09 | CA-UX-IC-07/09 |

Todos os wireframes atendem CA-UX-IC-02 (mensagens do catálogo), CA-UX-IC-04 (WCAG 2.1 AA) e responsividade (MMS-002-14, seção 8), verificados nos cenários do MMS-002-17.

---

# 10. Critérios de Aceite de Wireframes

| Código | Critério |
|--------|----------|
| CA-WF-IC-01 | Todo wireframe corresponde a uma tela do MMS-002-14 (seção 4.1) ou ao componente de busca (seção 4.2), sem telas adicionais |
| CA-WF-IC-02 | Toda zona/ação rastreável a UC, endpoint ou componente Foundation (matriz da seção 9) |
| CA-WF-IC-03 | Estados carregando/vazio/erro/sem permissão definidos para todas as telas de lista e formulário |
| CA-WF-IC-04 | Variantes responsivas definidas conforme os breakpoints do MMS-002-14 (seção 8) |
| CA-WF-IC-05 | Nenhuma definição estética (cor, fonte, espaçamento) — delegada ao DS-001 |
| CA-WF-IC-06 | Bloco de CA e seleção de tamanho condicionais ao grupo/grade conforme IC-BR-080/082 |

---

# 11. Roadmap

| Versão | Escopo |
|--------|--------|
| **v1.0 (MVP)** | WF-IC-01 a WF-IC-06 conforme este documento |
| **v1.1** | Wireframes da importação em lote do ERP (relatório por linha), exportação CSV e configurações enriquecidas |
| **v2.0** | Wireframes do painel de higiene do catálogo, sugestão de duplicatas assistida e bulk lifecycle (MMS-002-14, seção 14) |

---

# 12. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação dos wireframes do módulo: 6 telas/componentes com layout, zonas, variantes responsivas, estados, matriz de rastreabilidade e critérios de aceite |
