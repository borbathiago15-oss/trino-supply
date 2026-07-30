**Documento:** MMS-003 — Material Requisition (Visão do Módulo)
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-001 (Documento Mestre Funcional — seções 6, 8.2, 9, 10, 12, 14, 15.1, 16, 20, 22, 23, 24, 25), ADR-012, FD-001-01, FD-001-02, FD-001-04, FD-001-05, FD-001-06, FD-001-07, FD-001-10
**Referências:** MMS-002 (Item Catalog), MMS-004 (Inventory Management), MMS-005 (Receiving), PR-001 (Purchase Requisition — rota de compra), GOV-001

---

# Objetivo

O **Material Requisition** é o ponto de entrada de toda demanda de material na Materials Management Suite: o módulo pelo qual qualquer usuário solicita itens ao almoxarifado, com justificativa, centro de custo, local de entrega e data necessária.

O módulo existe para substituir o pedido informal (bilhete, e-mail, conversa no corredor) por uma solicitação rastreável, aprovada por workflow e atendida pelo caminho mais barato disponível — **estoque primeiro, compra só do que falta**. Ele herda e reforça o princípio já vigente no domínio de Compras: **o usuário solicita uma necessidade, nunca um fornecedor** (PR-001-12).

---

# Escopo

## O que faz

- **Criação de solicitação de material:** itens pesquisados no Item Catalog (somente itens ativos), quantidades, justificativa obrigatória, centro de custo, local de entrega e data necessária.
- **Workflow de aprovação** via FD-001-04, parametrizável por empresa, unidade, valor estimado e criticidade dos itens (MMS-P-04, MMS-P-06).
- **Validação de estoque obrigatória após a aprovação** (regra 2 do fluxo corporativo, MMS-001 §9): consulta o MMS-004 item a item, considerando somente saldo disponível (MMS-RG-09).
- **Roteamento por item (rota mista):** itens com saldo seguem para reserva/atendimento pelo MMS-004; itens sem saldo geram demanda de compra no PR-001 com referência à solicitação de origem (rastreabilidade bidirecional — regra 3 do fluxo).
- **Acompanhamento consolidado:** o solicitante vê o andamento de todos os itens na mesma solicitação, estejam eles na rota de estoque ou na rota de compra (jornada 10.1, MMS-001).
- **Entrega e confirmação:** registro da entrega pelo almoxarifado (via MMS-004) e confirmação de recebimento pelo solicitante, que conclui a solicitação.
- **Cancelamento** pelo solicitante ou pela gestão, com as restrições de estado definidas na máquina de estados.

## O que NÃO faz

- **Não escolhe fornecedor, não cota, não compra:** isso é domínio do PR-001 (Compras). A solicitação gera apenas a *demanda* (regra do fluxo corporativo e MMS-001 §8.2).
- **Não movimenta estoque:** reserva, separação e entrega são executadas pelo MMS-004; este módulo apenas solicita e acompanha (MMS-P-07: toda movimentação tem documento — a solicitação é o documento de origem).
- **Não valida estoque antes da aprovação:** aprovar não compromete saldo; reservar, sim (regra 2 do fluxo corporativo).
- **Não recebe material comprado:** a entrada física de itens da rota de compra é do MMS-005 (Receiving), que devolve o atendimento à solicitação.
- **Não cadastra itens:** usa exclusivamente o Item Catalog (MMS-002); item inativo não entra em novas solicitações (MMS-RG-08).
- **Não executa pagamento nem financeiro** (fora da suíte).

## Integrações esperadas

| Integração | Direção | Comportamento |
|------------|---------|---------------|
| MMS-002 Item Catalog | Consome | Catálogo de itens ativos para seleção; classificação (estocável/não estocável/sob encomenda) e criticidade orientam workflow e roteamento |
| MMS-004 Inventory | Consome / dispara | Validação de estoque (saldo disponível), reserva, separação e entrega dos itens atendidos pelo estoque |
| PR-001 Purchase Requisition | Produz demanda / acompanha | Itens sem estoque viram requisição de compra com referência à solicitação de origem; status da compra alimenta a visão consolidada |
| MMS-005 Receiving | Consome | Entrada de material comprado retoma o atendimento pendente da solicitação (compra dedicada, quando parametrizada) |
| Foundation (FD-001-01..10) | Consome | Identidade, estrutura organizacional, workflow, notificações, auditoria, timeline, configuração |

---

# Objetivos do Negócio

1. **Atender pelo estoque sempre que possível** — maximizar a taxa de atendimento pelo estoque antes de gerar compra (KPI da suíte, MMS-001 §20).
2. **Dar previsibilidade ao solicitante** — status visível de ponta a ponta, inclusive quando o item foi para compra.
3. **Controlar a demanda antes do gasto** — toda solicitação passa por aprovação com contexto decisório (justificativa, centro de custo, saldo consultável).
4. **Rastrear 100% da demanda** — da necessidade à entrega, com documento, responsável e data/hora em cada etapa (MMS-P-01).

## Quem utiliza

| Papel | Uso principal |
|-------|---------------|
| Solicitante | Cria e acompanha solicitações; confirma recebimento (Requester Workspace, MMS-001 §12.1) |
| Aprovador (gestor de área) | Aprova/rejeita/retorna solicitações com parecer (fila de aprovações — mesma experiência do PR-001) |
| Almoxarife | Atende as solicitações aprovadas via MMS-004 (fila de atendimento do Warehouse Workspace) |
| Gerente de Suprimentos | Acompanha KPIs de atendimento e parametriza regras |
| Administrador | Configura workflows e parâmetros |
| Auditor | Consulta trilhas e timelines (somente leitura) |

---

# Objetivos do MVP

Conforme o roadmap oficial (MMS-001 §24), o MVP do módulo entrega:

1. Criação de solicitação com itens do catálogo, justificativa, centro de custo, local de entrega e data necessária;
2. Workflow de aprovação parametrizável (FD-001-04);
3. Validação de estoque após aprovação e roteamento por item (estoque × compra);
4. Geração de demanda de compra no PR-001 com referência bidirecional;
5. Acompanhamento consolidado das duas rotas na mesma solicitação;
6. Entrega e confirmação de recebimento, concluindo a solicitação;
7. Cancelamento com regras de estado.

---

# Problemas que Resolve

| Problema atual | Como o módulo resolve |
|----------------|----------------------|
| Pedidos informais sem rastro | Solicitação documentada, com timeline e auditoria (MMS-P-01/02/03) |
| Compra do que já existe em estoque | Validação obrigatória no MMS-004 antes de gerar demanda de compra |
| Solicitante sem visibilidade | Status consolidado das rotas de estoque e de compra na mesma tela |
| Gasto sem controle prévio | Workflow de aprovação parametrizável com contexto decisório |
| Demanda sem dono | Toda demanda de compra carrega a referência da solicitação de origem |

---

# Atores

| Ator | Tipo | Interação |
|------|------|-----------|
| Solicitante | Humano | Cria, acompanha, cancela, confirma recebimento |
| Aprovador | Humano | Decide com parecer no workflow |
| MMS-004 Inventory | Sistema | Valida estoque, reserva, separa, entrega |
| PR-001 Compras | Sistema | Recebe demanda de compra e informa status |
| MMS-005 Receiving | Sistema | Sinaliza entrada de material da rota de compra |
| FD-001-04 Workflow | Sistema | Executa o fluxo de aprovação |
| FD-001-05 Notifications | Sistema | Notifica mudanças de estado e pendências |

---

# Fluxo Macro

Recorte do fluxo corporativo oficial (MMS-001 §9) sob a ótica deste módulo:

```
Solicitante cria Solicitação (Rascunho)
        ↓
Submete → Workflow de Aprovação (FD-001-04)
        ↓
   ┌────┴─────┐
Rejeitada  Aprovada
              ↓
     Validação de Estoque (MMS-004 — saldo disponível, MMS-RG-09)
              ↓
   ┌──────────┴───────────┐
Item com saldo        Item sem saldo
   ↓                       ↓
Reserva/Separação      Demanda de compra (PR-001, com referência de origem)
/Entrega (MMS-004)         ↓
   ↓                  Aguardando Compra → Recebimento (MMS-005) → retoma atendimento
   └──────────┬───────────┘
              ↓
   Confirmação do solicitante → Concluída
```

Regras herdadas do fluxo corporativo que este módulo impõe:

1. **Rota mista por item:** cada item segue seu caminho; a solicitação só conclui quando todos os itens concluem.
2. **Validação após aprovação, nunca antes.**
3. **Demanda de compra sempre com referência à solicitação de origem** (rastreabilidade bidirecional).
4. **Compra dedicada parametrizável:** material comprado para a solicitação pode entrar no estoque já reservado para ela (configuração por empresa).

---

# Entradas

| Entrada | Origem | Observação |
|---------|--------|------------|
| Itens e quantidades | Item Catalog (MMS-002) | Somente itens ativos (MMS-RG-08) |
| Justificativa | Solicitante | Obrigatória |
| Centro de custo | FD-001-02 (Organization) | Escopo organizacional obrigatório |
| Local de entrega | Estrutura organizacional / parametrização | Unidade, almoxarifado ou endereço de entrega |
| Data necessária | Solicitante | Usada em priorização e alertas de atraso |
| Resultado da validação de estoque | MMS-004 | Por item: atende / não atende |
| Status da compra | PR-001 | Alimenta a visão consolidada |
| Confirmação de recebimento | Solicitante | Encerra o ciclo do item/solicitação |

---

# Saídas

| Saída | Destino | Observação |
|-------|---------|------------|
| Solicitação aprovada com itens roteados | MMS-004 / PR-001 | Cada item com sua rota definida |
| Demanda de compra | PR-001 | Itens, quantidades, centro de custo e referência à solicitação de origem |
| Confirmação de entrega | MMS-004 | Baixa do atendimento pelo estoque |
| Eventos de negócio | Barramento (FD-001) | Ver "Eventos Publicados" |
| Notificações | FD-001-05 | Mudanças de estado, reserva, disponibilidade para retirada, atrasos |
| Registros de timeline e auditoria | FD-001-07 / FD-001-06 | Todas as transições e decisões |

---

# Dependências do Foundation

| Domínio | Uso no módulo |
|---------|---------------|
| FD-001-01 Identity & Access | Autenticação, papéis, permissões, escopo organizacional (MMS-P-05) |
| FD-001-02 Organization | Empresa, unidade, centro de custo do solicitante e da solicitação |
| FD-001-04 Workflow | Aprovação parametrizável por empresa/unidade/valor/criticidade (MMS-P-04) |
| FD-001-05 Notifications | Todos os avisos ao solicitante/aprovador/almoxarifado — o módulo nunca notifica por conta própria |
| FD-001-06 Audit | Auditoria de toda criação, alteração, decisão e cancelamento (MMS-P-02) |
| FD-001-07 Timeline | Linha do tempo da solicitação, incluindo vínculos com PR-001 (MMS-P-03) |
| FD-001-10 Configuration | Parâmetros `materials.requisition.*` (MMS-P-06, MMS-RG-11) |

Parâmetros previstos (nomes conceituais, definição fina no documento de configuração do módulo):

| Parâmetro | Efeito |
|-----------|--------|
| `materials.requisition.approval.required` | Exigência de aprovação e critérios (valor, criticidade) |
| `materials.requisition.purchase.dedicated` | Compra dedicada: entrada já reservada para a solicitação de origem |
| `materials.requisition.cancel.allowed-states` | Estados em que o cancelamento é permitido |
| `materials.requisition.due-date.alert-days` | Antecedência dos alertas de data necessária |

---

# Eventos Publicados (funcionais)

Conforme o catálogo corporativo (MMS-001 §16); a especificação técnica (envelope, payloads) seguirá o padrão ADR-010 quando o pacote funcional do módulo for detalhado:

1. Solicitação de Material criada / submetida / aprovada / rejeitada / retornada / cancelada / concluída;
2. Estoque validado para a solicitação (repasse do resultado por item: atende / não atende);
3. Demanda de compra gerada para o PR-001 (com referência à solicitação de origem);
4. Item roteado (estoque × compra) — fato de roteamento por item.

# Eventos Consumidos (funcionais)

| Evento | Origem | Reação do módulo |
|--------|--------|------------------|
| Reserva criada / liberada / vencida | MMS-004 | Atualiza status do item e notifica o solicitante |
| Separação iniciada / concluída | MMS-004 | Atualiza progresso do atendimento |
| Entrega registrada | MMS-004 | Habilita confirmação de recebimento pelo solicitante |
| Compra aprovada / pedido confirmado / previsão de entrega | PR-001 | Alimenta a visão consolidada da rota de compra |
| Material recebido (rota de compra) | MMS-005 | Retoma o atendimento pendente da solicitação |

---

# Requisitos Não Funcionais

| Categoria | Requisito |
|-----------|-----------|
| Multiempresa | Isolamento por empresa em toda consulta, regra e evento (MMS-001 §6.2) |
| Escopo organizacional | Solicitante opera dentro de seu escopo; aprovador dentro do dele (FD-001-01) |
| Performance | Listagens com paginação keyset; validação de estoque assíncrona quando a solicitação tiver muitos itens |
| Confiabilidade | Demanda de compra nunca é perdida: geração via outbox na mesma transação do roteamento (padrão ADR-010) |
| Auditabilidade | 100% das transições e decisões auditadas (MMS-P-02) |
| Segurança | Deny by default; itens de menu sem permissão são ocultados; SoD conforme seção de permissões |
| Internacionalização | Mensagens via catálogo i18n do Foundation (padrão PR-001-14) |

---

# Restrições Arquiteturais

1. **Documentação é a fonte da verdade:** qualquer conflito entre implementação e este documento (ou o MMS-001) resolve-se pela documentação.
2. **Fronteira de domínio inviolável:** este módulo não cria tabelas nem lógica de estoque, catálogo, compra ou recebimento — consome e dispara via contratos/eventos (baixo acoplamento, ADR-012).
3. **Nenhuma regra operacional em código:** comportamentos variáveis são parâmetros FD-001-10 (MMS-P-06, MMS-RG-11).
4. **Aprovação somente via FD-001-04:** nenhum fluxo de aprovação paralelo (MMS-P-04).
5. **Notificação somente via FD-001-05** e nunca como bloqueio do fluxo (padrão PR-001).
6. **Rastreabilidade bidirecional obrigatória** entre solicitação e demanda de compra (regra 3 do fluxo corporativo).
7. **Máquina de estados única:** nenhuma transição fora das previstas em MMS-001 §15.1 e na máquina detalhada deste módulo.

---

# Matriz de Dependência com Outros Módulos

| Módulo | Tipo de dependência | Natureza |
|--------|--------------------|----------|
| MMS-001 | Documento mestre | Regras, fluxo e estados herdados (obrigatória) |
| MMS-002 Item Catalog | Forte | Sem catálogo ativo não há solicitação |
| MMS-004 Inventory | Forte | Sem validação/reserva/entrega não há atendimento pelo estoque |
| PR-001 Purchase Requisition | Forte (inter-domínio) | Sem rota de compra, itens sem estoque ficam sem caminho |
| MMS-005 Receiving | Média | Necessária para retomar atendimento de itens comprados (pode operar degradado até o MMS-005 existir) |
| FD-001-01/02/04/05/06/07/10 | Forte | Infraestrutura funcional do Foundation |

---

# KPIs

| KPI | Cálculo | Meta/observação |
|-----|---------|-----------------|
| Taxa de atendimento pelo estoque | itens atendidos pelo estoque ÷ itens solicitados | Mede o princípio "estoque primeiro" (MMS-001 §20) |
| Tempo de atendimento | aprovação → entrega confirmada | SLA operacional do almoxarifado |
| Ruptura originada em solicitação | itens sem saldo com demanda aberta | Alimenta alerta prioritário (MMS-RG-12) |
| Ciclo de aprovação | submissão → decisão | Eficiência do workflow |
| Taxa de cancelamento/rejeição | solicitações canceladas+rejeitadas ÷ submetidas | Qualidade da demanda |

---

# Critérios de Qualidade

- Toda solicitação referencia somente itens ativos do catálogo (MMS-RG-08);
- Toda decisão de aprovação registra decisor, data/hora e parecer;
- Todo item roteado tem rota explícita (estoque ou compra) — nenhum item fica "sem dono";
- Toda demanda de compra carrega a referência da solicitação de origem;
- Toda transição de estado é ação de negócio documentada — nunca edição direta (MMS-001 §15);
- Toda notificação passa pelo FD-001-05; toda auditoria pelo FD-001-06.

---

# Critérios de Conclusão do Módulo (DoD)

O módulo MMS-003 estará concluído (documentação + implementação MVP) quando:

1. Esta visão estiver aprovada e registrada no GOV-002;
2. O pacote funcional detalhado existir: regras de negócio, máquina de estados fina, casos de uso, permissões (RBAC/ABAC, padrão PR-001-09), eventos (padrão ADR-010), API, UX/wireframes e critérios de aceite — seguindo o padrão do pacote PR-001;
3. O fluxo criar → aprovar → validar → rotear → atender/comprar → entregar → confirmar estiver operacional com as integrações MMS-002, MMS-004 e PR-001;
4. Rastreabilidade bidirecional solicitação ↔ requisição de compra verificável na timeline;
5. Auditoria, timeline e notificações ativas em 100% das transições;
6. KPIs de atendimento calculáveis a partir dos dados do módulo.

---

# Roadmap

## Versão 1 (MVP)

Escopo dos "Objetivos do MVP": solicitação, workflow, validação de estoque, rota mista, demanda de compra via PR-001, acompanhamento consolidado, entrega e confirmação, cancelamento.

## Versão 1.1

- Compra dedicada com reserva automática na entrada (integração plena MMS-005);
- Solicitações recorrentes e modelos (templates) por área;
- Sugestão de quantidade com base em consumo histórico;
- Exportações e relatório de atendimento de solicitações completo (MMS-001 §21).

## Versão 2.0

- Reposição automática por ponto de pedido gerando solicitação/PR-001 sem intervenção (MMS-001 §24);
- Aprovação por aplicativo móvel e operação scanner-friendly;
- Previsão de demanda (IA) alimentando sugestões de solicitação.

---

# Funcionalidades Futuras

Registradas para visibilidade — **fora do compromisso de qualquer versão**: catálogo de favoritos do solicitante, integração com projetos/ordens de manutenção como origem da necessidade, devolução de material pelo solicitante com reentrada no estoque (integra MMS-005), reserva antecipada opcional antes da aprovação (quebra controlada da regra 2, sujeita a ADR).

---

# Versionamento do Módulo

- **Versão do documento:** 1.0.0 (visão do módulo — primeira emissão).
- **Esquema:** SemVer; mudanças de escopo/regra exigem nova versão e entrada no histórico.
- **Pacote funcional detalhado:** será versionado documento a documento, seguindo o padrão do PR-001 (01-business-context … 17-test-scenarios) quando iniciado.

---

# Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação da visão do módulo Material Requisition: solicitação interna ao almoxarifado com workflow (FD-001-04), validação de estoque após aprovação (MMS-RG-09), rota mista por item, demanda de compra via PR-001 com rastreabilidade bidirecional, acompanhamento consolidado, entrega e confirmação; fronteiras com MMS-002/004/005 e PR-001; NFRs, KPIs, DoD e roadmap. Fontes: MMS-001 (§8.2, §9, §15.1, §16, §22, §23, §24) e princípio do PR-001-12 |
