**Documento:** MMS-001 — Materials Management Suite (Documento Mestre Funcional)
**Módulo:** MMS — Materials Management (Gestão de Materiais e Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** FD-001 (Foundation Domain), FD-001-01 a FD-001-10, PR-001 (Purchase Requisition)
**Referências:** PR-001-12 (Business Journey), GOV-001, GOV-002

---

# 1. Visão Geral

A **Materials Management Suite (MMS)** é o domínio do Trino Supply responsável pela gestão completa do ciclo de vida de materiais: do cadastro do item ao controle de estoque, da solicitação interna de material à entrega ao solicitante, e do recebimento físico à entrada no estoque.

A suíte responde à pergunta central de toda operação de suprimentos: **"a necessidade pode ser atendida pelo estoque ou precisa ser comprada?"** — e conduz cada demanda pelo caminho correto, com rastreabilidade total.

Este documento é o **Documento Mestre Funcional** da suíte: define visão, conceitos, módulos, responsabilidades, fluxo corporativo e regras gerais. É a fonte oficial de verdade (Single Source of Truth) para todos os documentos funcionais e técnicos dos módulos de materiais. Nenhum documento de módulo da suíte pode contradizê-lo.

## 1.1 Objetivos

1. Garantir que toda necessidade de material seja atendida primeiro pelo estoque, reduzindo compras desnecessárias.
2. Manter o estoque confiável: saldo correto, localização correta, movimentação rastreada.
3. Integrar-se nativamente ao fluxo de compras (PR-001): quando o estoque não atende, a demanda vira Solicitação de Compra sem retrabalho.
4. Dar visibilidade executiva e operacional sobre posição de estoque, atendimento e rupturas.

## 1.2 Problemas Resolvidos

| Problema | Como a suíte resolve |
|----------|---------------------|
| Compra de item que já existe em estoque | Validação de estoque obrigatória no fluxo da solicitação de material |
| Saldo de estoque divergente do físico | Toda movimentação registrada por documento, com rastreabilidade ponta a ponta |
| Solicitações internas informais (e-mail, papel, WhatsApp) | Material Requisition formal, com workflow, aprovação e timeline |
| Recebimento sem conferência | Receiving com conferência contra documento de origem (pedido ou transferência) |
| Falta de visibilidade para o gestor | KPIs de acuracidade, ruptura, cobertura e giro |

## 1.3 Benefícios e Resultados Esperados

- Redução de compras emergenciais por desconhecimento de saldo.
- Acuracidade de estoque sustentada acima da meta (seção 20).
- Atendimento interno com prazo e responsável definidos.
- Base de dados confiável para os módulos futuros de Purchasing avançado e Analytics.

## Sugestões de Evolução (fora do MVP)

- **Problema identificado:** empresas com múltiplos almoxarifados sofrem com redistribuição manual. **Risco:** excesso em um depósito e ruptura em outro. **Benefício:** redução de capital parado. **Recomendação:** transferência inter-depósito assistida por sugestão de rebalanceamento (v2.0, seção 24).

---

# 2. Objetivos Estratégicos

1. **Estoque como fonte primária de atendimento** — compra é o caminho de exceção, não o padrão.
2. **Confiança no saldo** — decisões automatizadas (reserva, sugestão de compra) só são possíveis com acuracidade alta.
3. **Rastreabilidade absoluta** — toda unidade movimentada tem origem, destino, responsável e documento.
4. **Escalabilidade SaaS** — multiempresa, multiunidade, multialmoxarifado, multidepósito, multicliente e multicontrato desde o desenho.
5. **Parametrização acima de customização** — comportamentos variam por configuração (FD-001-10), nunca por código específico de cliente.

## Sugestões de Evolução (fora do MVP)

- **Problema:** suporte a multimoeda e multiidioma é estrutural e caro de retrofitar. **Risco:** bloqueio de expansão internacional. **Benefício:** abertura de mercado. **Recomendação:** preparar os conceitos de item e valor para localização futura (labels via FD-001-09), sem implementar no MVP.

---

# 3. Posicionamento do Produto

A MMS é uma suíte do domínio de **materiais** da plataforma Trino Supply, posicionada entre a origem da demanda e a execução logística:

```
Necessidade da operação
        │
        ▼
   MMS (esta suíte)  ── sem estoque ──►  PR-001 (Compras)
        │                                    │
        ▼                                    ▼
  Atendimento interno ◄── entrada no estoque ── Recebimento
```

Fronteiras formais (invioláveis):

- **A MMS não compra.** Quando o estoque não atende, a suíte gera demanda para o domínio de Compras (PR-001), que executa seu próprio fluxo já documentado.
- **A MMS não gerencia fornecedores, contratos ou cotações.** Pertencem ao domínio de Procurement (RFQ, Purchase Order, Supplier Management, Contracts — visão do FD-001).
- **A MMS não reimplementa serviços do Foundation.** Workflow, notificações, auditoria, timeline, documentos, identidade, organização, master data e configuração são consumidos de FD-001-01 a FD-001-10.
- **A MMS não produz analytics.** Expõe dados e eventos para o futuro módulo Analytics.

---

# 4. Problemas Resolvidos (detalhamento por público)

| Público | Dor | Resposta da suíte |
|---------|-----|-------------------|
| Operação (técnico, produção, manutenção) | "Peço material e não sei quando chega" | Solicitação formal com status, previsão e notificação |
| Almoxarifado | "Atendo por papel e planilha; o saldo nunca bate" | Fila de atendimento, reserva, separação e baixa por documento |
| Compras | "Compro o que já tem no depósito vizinho" | Visão de saldo antes da compra; demanda só chega após validação de estoque |
| Gestão | "Não sei o valor parado nem onde falta" | KPIs de cobertura, giro, ruptura e acuracidade |
| Auditoria | "Não consigo reconstruir quem movimentou o quê" | Timeline e trilha de auditoria em toda movimentação |

---

# 5. Benefícios

1. **Financeiro:** menos capital parado e menos compras duplicadas.
2. **Operacional:** atendimento interno previsível, com fila e SLA.
3. **Governança:** cada movimentação auditável; nenhuma baixa sem documento.
4. **Estratégico:** base para reposição automática e análise preditiva nas próximas versões.

---

# 6. Arquitetura Funcional

## 6.1 Princípios da suíte (obrigatórios em todos os módulos)

| Código | Princípio |
|--------|-----------|
| MMS-P-01 | Toda movimentação possui rastreabilidade (documento de origem + responsável + data/hora) |
| MMS-P-02 | Toda ação gera auditoria (FD-001-06) |
| MMS-P-03 | Toda alteração relevante gera timeline (FD-001-07) |
| MMS-P-04 | Toda aprovação utiliza workflow (FD-001-04) |
| MMS-P-05 | Toda operação respeita permissões (FD-001-01 + matriz da suíte, seção 23) |
| MMS-P-06 | Todo processo é parametrizável (FD-001-10); nenhum comportamento de cliente em código |
| MMS-P-07 | Nenhuma movimentação ocorre sem documento: entrada, saída, reserva, transferência e ajuste sempre têm um documento de negócio associado |
| MMS-P-08 | Saldo nunca é editado diretamente: só muda por movimentação registrada |

## 6.2 Multiplicidade suportada (desde o desenho)

- Multiempresa (isolamento por empresa em toda consulta e regra);
- Multiunidade e multicentro de custo (estrutura de FD-001-02);
- Multialmoxarifado e multidepósito (hierarquia de locais da suíte, seção 8 — Inventory Management);
- Multicliente e multicontrato (estoque dedicado por cliente/contrato, para operações 3PL e projetos).

## Sugestões de Evolução (fora do MVP)

- **Problema:** estoque por lote/série exige granularidade fina. **Risco:** indústrias reguladas (farma, alimentos) não atendidas. **Benefício:** ampliação de segmento. **Recomendação:** controle por lote e validade na v2.0, preservando o modelo de movimentação por documento (MMS-P-07) — a evolução não quebra o princípio, apenas adiciona dimensão ao item movimentado.

---

# 7. Estrutura da Suíte

A suíte é composta por **quatro módulos próprios** e uma **integração formal** com o domínio de Compras:

| Módulo | ID futuro | Papel na suíte |
|--------|-----------|----------------|
| Item Catalog | MMS-002 (planejado) | Cadastro mestre de itens (materiais e serviços relacionados a estoque) |
| Material Requisition | MMS-003 (planejado) | Solicitação interna de material ao almoxarifado |
| Inventory Management | MMS-004 (planejado) | **O módulo Estoque**: saldos, reservas, movimentações, endereçamento, inventário |
| Receiving | MMS-005 (planejado) | Recebimento físico e conferência de materiais (comprados ou transferidos) |
| *(Purchasing)* | — | **Não é módulo da MMS.** É o domínio PR-001 (existente) + RFQ/PO (planejados no FD-001) |

Os módulos serão documentados individualmente na sequência definida no roadmap (seção 24), sempre respeitando este documento mestre.

---

# 8. Responsabilidade de Cada Módulo

## 8.1 Item Catalog (MMS-002)

- Cadastro único de itens estocáveis: descrição, unidade de medida, categoria, características, criticidade, reposição (ponto de pedido, estoque mínimo/máximo — parametrizável).
- Classificação: estocável, não estocável (compra direta), sob encomenda.
- Vocabulários compartilhados (unidade de medida, categoria) consumidos de FD-001-09 — o catálogo não recria master data, apenas a utiliza.
- **Não faz:** fornecedores e preços (domínio Procurement); estrutura organizacional (FD-001-02).

## 8.2 Material Requisition (MMS-003)

- Solicitação interna de material: o usuário pede itens ao almoxarifado, com justificativa, centro de custo, local de entrega e data necessária.
- Workflow de aprovação (FD-001-04) parametrizável por empresa/unidade/valor/criticidade.
- **Validação de estoque obrigatória** após aprovação: itens com saldo seguem para reserva; itens sem saldo geram demanda de compra (PR-001).
- **Não faz:** escolha de fornecedor, cotação ou pedido — herda o princípio já vigente no PR-001 ("o usuário solicita uma necessidade, nunca um fornecedor", PR-001-12).

## 8.3 Inventory Management (MMS-004) — o módulo Estoque

- Saldos por item × depósito × (opcionalmente) cliente/contrato, sempre derivados de movimentações (MMS-P-08).
- Documentos de movimentação: entrada, saída, reserva, liberação de reserva, transferência, ajuste (com justificativa e aprovação quando parametrizado).
- Endereçamento: almoxarifado → depósito → endereço (parametrizável a granularidade).
- Reserva: bloqueio de saldo para uma solicitação, com expiração parametrizável.
- Inventário (contagem física): cíclico por curva ABC ou geral, com divergências tratadas por ajuste aprovado.
- Alertas: estoque abaixo do mínimo, ruptura iminente, reserva vencendo.
- **Não faz:** valorização contábil/fiscal de estoque (integração futura; o MVP registra custo médio apenas como referência, sem escrituração).

## 8.4 Receiving (MMS-005)

- Recebimento físico contra documento de origem: Pedido de Compra (futuro, domínio Procurement), transferência entre depósitos ou devolução de solicitante.
- Conferência: quantidade recebida × esperada, divergências registradas (falta, excesso, avaria) com destino documentado.
- Gera a **entrada no estoque** (MMS-004) e, quando a origem é compra destinada a uma solicitação, dispara o atendimento pendente (seção 9).
- **Não faz:** inspeção de qualidade certificada (v2.0); pagamento/financeiro (fora da suíte).

## 8.5 Purchasing (fronteira com PR-001)

- Já documentado: **PR-001 Purchase Requisition** (criação, aprovação, fila de compras) e, na visão do FD-001, RFQ → Purchase Order → Supplier Management → Contracts.
- A MMS **produz demanda** para esse domínio (itens sem estoque) e **consome resultado** (entrada de material via Receiving). Nenhuma responsabilidade é duplicada.

## Sugestões de Evolução (fora do MVP)

- **Problema:** inspeção de qualidade no recebimento é exigência de indústrias reguladas. **Risco:** materiais não conformes entram no estoque. **Benefício:** conformidade e menos retrabalho. **Recomendação:** status de quarentena no Receiving v2.0 (recebido → em inspeção → liberado/recusado), reutilizando a state machine de documentos sem criar fluxo paralelo.

---

# 9. Fluxo Corporativo (oficial)

Este é o fluxo oficial da suíte. Todos os módulos, BPMNs e casos de uso futuros devem respeitá-lo.

```
Necessidade
    ↓
Solicitação de Material (MMS-003)
    ↓
Workflow de Aprovação (FD-001-04)
    ↓
Validação de Estoque (MMS-004)
    ├── Existe estoque ──────────────────────────┐
    ↓                                            ↓
Reserva (MMS-004)                     Solicitação de Compra (PR-001)
    ↓                                            ↓
Separação (MMS-004)                          Compras (PR-001 → RFQ/PO)
    ↓                                            ↓
Entrega (MMS-004)                        Pedido de Compra (Procurement)
    ↓                                            ↓
Recebimento pelo solicitante              Recebimento (MMS-005)
    ↓                                            ↓
Conclusão                              Entrada no Estoque (MMS-004)
                                               ↓
                                     Atendimento da Solicitação (retoma à esquerda)
                                               ↓
                                           Conclusão
```

Regras do fluxo:

1. **Rota mista permitida:** uma mesma solicitação pode ter parte dos itens atendida pelo estoque e parte enviada a compras; cada item segue seu caminho e a solicitação conclui quando todos os itens concluem.
2. **Validação de estoque ocorre após a aprovação**, nunca antes — aprovar não compromete saldo; reservar, sim.
3. **Sem estoque, a demanda vira PR-001** com referência à solicitação de material de origem (rastreabilidade bidirecional).
4. **Material comprado para solicitação específica** pode ser parametrizado como "compra dedicada": entra no estoque já reservado para a solicitação de origem.

---

# 10. Jornada do Usuário

## 10.1 Solicitante (operação)

1. Acessa o Requester Workspace e inicia uma Solicitação de Material.
2. Informa itens (buscando no Item Catalog), quantidades, centro de custo, local de entrega, data necessária e justificativa.
3. Envia para aprovação e acompanha o status na timeline.
4. É notificado quando o material é reservado/separado e quando está disponível para retirada ou entrega.
5. Confirma o recebimento; a solicitação é concluída.
6. Se algum item foi comprado, acompanha o andamento da compra pela mesma solicitação (visão consolidada, mesmo a compra ocorrendo no domínio PR-001).

## 10.2 Almoxarife

1. Acessa o Warehouse Workspace: fila de solicitações aprovadas aguardando atendimento.
2. Executa reserva, separação e entrega, registrando cada etapa por documento.
3. Executa recebimentos (conferência) e entradas no estoque.
4. Executa contagens de inventário e trata divergências.

## 10.3 Aprovador

- Recebe solicitações na fila de aprovações (mesma experiência do PR-001), decide com contexto (justificativa, centro de custo, saldo disponível consultável) e parecer.

## 10.4 Gestor

- Acompanha KPIs da suíte no Management Workspace: acuracidade, ruptura, cobertura, tempo de atendimento, valor parado.

---

# 11. Personas

| Persona | Objetivos | Responsabilidades | Necessidades | Permissões esperadas |
|---------|-----------|-------------------|--------------|---------------------|
| **Solicitante** | Obter material no prazo | Criar solicitação, confirmar recebimento | Fluxo guiado, status visível | Criar/consultar próprias solicitações |
| **Almoxarife** | Atender com precisão e rapidez | Reservar, separar, entregar, receber, contar | Fila priorizada, registro rápido | Movimentar estoque, receber, contar |
| **Supervisor de Almoxarifado** | Garantir operação e acuracidade | Aprovar ajustes, gerenciar fila e endereçamento | Visão de fila e divergências | Aprovar ajustes/movimentações restritas |
| **Aprovador (gestor de área)** | Controlar demanda e custo | Aprovar/rejeitar/retornar solicitações | Resumo decisório com saldo | Aprovar solicitações |
| **Comprador** | Comprar só o necessário | Receber demandas sem estoque (via PR-001) | Demanda com origem rastreável | Conforme PR-001 |
| **Gerente de Suprimentos** | Otimizar estoque e serviço | Definir parâmetros (mín/máx, curva ABC) | KPIs e alertas | Configurar parâmetros, exportar |
| **Administrador** | Manter a plataforma | Configurações, workflows, exceções | Conforme Foundation | Administrar |
| **Auditor** | Verificar conformidade | Consultar trilhas e timelines | Evidências exportáveis | Somente leitura + auditoria |

---

# 12. Workspaces

A suíte adota o conceito de **workspaces por papel** (mesma filosofia de UX do PR-001: simples para quem pede, completo para quem opera).

## 12.1 Requester Workspace

- **Objetivo:** solicitar e acompanhar material. **Menus:** Minhas Solicitações, Nova Solicitação, Catálogo de Itens (consulta), Notificações.
- **Experiência:** poucos campos, validações em linha, status sempre visível. **Indicadores:** solicitações em aberto, aguardando retirada, atrasadas.

## 12.2 Warehouse Workspace

- **Objetivo:** operação do almoxarifado. **Menus:** Fila de Atendimento, Recebimentos, Saldos, Movimentações, Inventário (contagens), Endereçamento.
- **Experiência:** ação rápida por documento, scanner-friendly (preparação para código de barras), confirmação em uma etapa. **Indicadores:** solicitações na fila, separações do dia, recebimentos pendentes, divergências de contagem.

## 12.3 Purchasing Workspace

- Não é criado pela MMS: é a fila de Compras do domínio PR-001. A MMS apenas garante que a demanda chegue lá com origem e referência.

## 12.4 Management Workspace

- **Objetivo:** visão gerencial. **Menus:** Visão de Estoque, KPIs, Alertas (ruptura, mínimo, reservas vencendo), Relatórios.
- **Experiência:** drill down do KPI ao documento de origem. **Indicadores:** os KPIs da seção 20.

## 12.5 Administration Workspace

- **Objetivo:** configuração. **Menus:** Parâmetros da suíte (FD-001-10), Workflows (FD-001-04), Catálogos (FD-001-09), Usuários e permissões (FD-001-01).
- **Experiência:** toda alteração com motivo obrigatório e duplo controle onde parametrizado.

---

# 13. Navegação Geral

```
Menu: Materiais e Estoque
 ├── Solicitações de Material
 │    ├── Minhas Solicitações
 │    ├── Nova Solicitação
 │    └── Aprovações (badge de pendentes)
 ├── Almoxarifado
 │    ├── Fila de Atendimento
 │    ├── Recebimentos
 │    ├── Saldos e Endereçamento
 │    ├── Movimentações
 │    └── Inventário
 ├── Catálogo de Itens
 └── Gestão
      ├── Visão de Estoque (KPIs)
      └── Alertas
```

A visibilidade de cada ramo segue a matriz de permissões (seção 23): itens sem permissão são ocultados, nunca apenas desabilitados (mesma regra do PR-001-14).

---

# 14. Regras Gerais da Suíte

| Código | Regra | Justificativa |
|--------|-------|---------------|
| MMS-RG-01 | Nenhuma movimentação sem documento de negócio | Rastreabilidade (MMS-P-07) |
| MMS-RG-02 | Saldo só muda por movimentação registrada; edição direta é proibida | Integridade (MMS-P-08) |
| MMS-RG-03 | Reserva tem validade parametrizável; vencida, libera o saldo automaticamente com notificação | Evitar saldo cativo |
| MMS-RG-04 | Saída maior que o saldo disponível é bloqueada (exceto ajuste aprovado por supervisor) | Prevenção de saldo negativo |
| MMS-RG-05 | Ajuste de estoque exige justificativa e, quando parametrizado, aprovação de supervisor | Controle de divergências |
| MMS-RG-06 | Toda entrada via Receiving exige documento de origem (pedido, transferência ou devolução) | Conferência e auditoria |
| MMS-RG-07 | Divergência de recebimento (falta/excesso/avaria) gera registro com destino documentado | Nenhuma diferença "some" |
| MMS-RG-08 | Item inativo não entra em novas solicitações; saldo remanescente segue movimentável até zerar | Descontinuação controlada |
| MMS-RG-09 | Validação de estoque considera somente saldo disponível (total − reservado) | Não prometer o que está reservado |
| MMS-RG-10 | Estoque dedicado a cliente/contrato não atende demanda de outro cliente/contrato | Segregação contratual |
| MMS-RG-11 | Toda regra operacional variável é parâmetro (FD-001-10): validades, tolerâncias de recebimento, exigência de aprovação de ajuste | Parametrização (MMS-P-06) |
| MMS-RG-12 | Estoque abaixo do mínimo gera alerta ao responsável; ruptura (saldo zero com demanda aberta) gera alerta prioritário | Prevenção de falta |

---

# 15. Estados Corporativos

## 15.1 Solicitação de Material (visão da suíte; detalhe fino no MMS-003)

`Rascunho → Submetida → Em Aprovação → Aprovada → Em Atendimento (Reservado/Separação/Entrega parcial ou total) → Concluída`

Estados finais alternativos: `Rejeitada`, `Cancelada`. Estado intermediário de exceção: `Aguardando Compra` (quando há itens na rota de compra).

## 15.2 Documento de Movimentação (MMS-004)

`Registrado → Confirmado → (estorna: Estornado)` — ajustes seguem o próprio fluxo de aprovação parametrizado.

## 15.3 Recebimento (MMS-005)

`Aguardando → Em Conferência → Concluído` / `Concluído com Divergência` / `Cancelado`

Restrições: nenhum estado é alterado diretamente; toda transição ocorre por ação de negócio documentada, com workflow onde exigido (mesma disciplina da State Machine do PR-001).

---

# 16. Eventos Corporativos (funcionais)

Lista funcional dos fatos de negócio que a suíte torna públicos (a especificação técnica — envelope, payloads — seguirá o padrão ADR-010 quando cada módulo for documentado):

1. Solicitação de Material criada / submetida / aprovada / rejeitada / retornada / cancelada / concluída;
2. Estoque validado para uma solicitação (com resultado por item: atende / não atende);
3. Reserva criada / liberada / vencida;
4. Separação iniciada / concluída;
5. Entrega registrada / recebimento confirmado pelo solicitante;
6. Demanda de compra gerada para o domínio PR-001 (com referência à solicitação de origem);
7. Recebimento registrado / conferido / com divergência;
8. Entrada no estoque / saída / transferência / ajuste;
9. Alerta de estoque mínimo / ruptura;
10. Inventário iniciado / contagem registrada / divergência aprovada.

---

# 17. Timeline

Aparecem na timeline (FD-001-07) de cada entidade: todas as transições de estado, aprovações e pareceres, reservas e liberações, separações, entregas, confirmações de recebimento, vínculos com PR-001 (demanda enviada / compra aprovada / material recebido), ajustes e divergências. Visibilidade por regra (pública na entidade, por papel, somente admin) conforme o padrão do Foundation.

---

# 18. Auditoria

Geram auditoria (FD-001-06), sem exceção: toda criação/alteração/exclusão de solicitação, item de catálogo, movimentação, reserva, recebimento, contagem e ajuste; toda decisão de aprovação; toda alteração de parâmetro; toda negação de acesso. Saldo anterior/posterior consta de toda auditoria de movimentação.

---

# 19. Dashboard Executivo (definição funcional)

Definição funcional apenas (a construção visual pertence ao módulo Analytics):

- **Cards:** valor total em estoque, itens em ruptura, itens abaixo do mínimo, solicitações em aberto, reservas a vencer em 48h;
- **KPIs:** os da seção 20, com drill down até o documento de origem;
- **Filtros:** empresa, unidade, almoxarifado, depósito, categoria, curva ABC, período;
- **Exportações:** CSV/PDF conforme permissão.

---

# 20. KPIs

| KPI | Cálculo | Por que existe |
|-----|---------|----------------|
| Acuracidade de estoque | itens com saldo sistema = físico ÷ itens contados | Meta ≥ 98%; base de toda automação |
| Ruptura | itens com saldo zero e demanda aberta | Mede falha de abastecimento |
| Cobertura (dias) | saldo disponível ÷ consumo médio diário | Dimensiona capital parado × risco |
| Giro | consumo no período ÷ estoque médio | Eficiência do capital |
| Taxa de atendimento pelo estoque | itens atendidos pelo estoque ÷ itens solicitados | Mede o princípio "estoque primeiro" |
| Tempo de atendimento | aprovação → entrega confirmada | SLA operacional do almoxarifado |
| Divergência de recebimento | recebimentos com divergência ÷ total | Qualidade de fornecedores/transporte |
| Obsolescência | itens sem movimento há N dias (parametrizável) | Saneamento do estoque |

---

# 21. Relatórios Corporativos (definição funcional)

| Relatório | Objetivo | Filtros-chave | Periodicidade |
|-----------|----------|---------------|---------------|
| Posição de estoque | Saldo por item/depósito/endereço | empresa, unidade, depósito, categoria | sob demanda |
| Movimentações | Extrato por item/período/documento | item, tipo de movimento, período | sob demanda |
| Curva ABC | Classificação por consumo/valor | período, depósito | mensal |
| Rupturas e mínimos | Itens críticos e histórico | depósito, período | diário |
| Acuracidade de inventário | Resultado das contagens | inventário, depósito | por ciclo |
| Atendimento de solicitações | Prazo, rota (estoque × compra), pendências | solicitante, centro de custo, período | semanal |

Todos com exportação CSV/PDF, ordenação parametrizável e permissão de exportação conforme matriz.

---

# 22. Integrações Funcionais

| Integração | Direção | Comportamento esperado |
|------------|---------|------------------------|
| PR-001 (Compras) | MMS → PR-001 | Demanda de compra com itens, quantidades, centro de custo e referência à solicitação de origem; a MMS acompanha o status |
| PR-001 / Procurement | PR-001 → MMS | Confirmação de pedido e previsão de entrega alimentam a solicitação de material |
| Receiving ↔ Inventory | Interna | Conferência concluída gera entrada no estoque; origem dedicada já entra reservada |
| Foundation (FD-001-01..10) | Consumo | Identidade, organização, documentos, workflow, notificações, auditoria, timeline, comentários, master data, configuração |
| Analytics (futuro) | MMS → Analytics | Dados e eventos para KPIs executivos |

---

# 23. Permissões Gerais

| Funcionalidade | Solicitante | Almoxarife | Sup. Almox. | Aprovador | Gerente | Admin | Auditor |
|----------------|:-----------:|:----------:|:-----------:|:---------:|:-------:|:-----:|:-------:|
| Visualizar catálogo/saldos | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Criar solicitação de material | ✔ | | | | | ✔ | |
| Aprovar solicitação | | | | ✔ | ✔ | ✔ | |
| Reservar/separar/entregar | | ✔ | ✔ | | | ✔ | |
| Receber/conferir | | ✔ | ✔ | | | ✔ | |
| Ajuste de estoque | | | ✔ | | ✔ | ✔ | |
| Manter catálogo de itens | | | | | ✔ | ✔ | |
| Configurar parâmetros | | | | | ✔ | ✔ | |
| Exportar | | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Auditoria (trilha) | | | | | ✔ | ✔ | ✔ |

Regras: *deny by default*; segregação de funções — quem solicita não aprova (mesma SoD do PR-001), quem ajusta não aprova o próprio ajuste; escopo organizacional obrigatório. A matriz detalhada por módulo (RBAC/ABAC) será documentada em cada MMS-00x, seguindo o padrão PR-001-09.

---

# 24. Roadmap

## MVP (v1.0)

1. **MMS-002 Item Catalog** — cadastro de itens com unidades/categorias via FD-001-09;
2. **MMS-004 Inventory Management (núcleo)** — saldos, entradas, saídas, reservas, ajustes com aprovação, endereçamento básico, alertas de mínimo/ruptura;
3. **MMS-003 Material Requisition** — solicitação, workflow, validação de estoque, rota de compra via PR-001, entrega e confirmação;
4. **MMS-005 Receiving (núcleo)** — recebimento contra documento, conferência, divergências, entrada no estoque.

## Versão 1.1

- Transferência entre depósitos com recebimento; inventário cíclico por curva ABC; compra dedicada com reserva automática na entrada; exportações e relatórios completos.

## Versão 2.0

- Controle por lote/validade e série; quarentena/inspeção de qualidade no recebimento; sugestão de rebalanceamento entre depósitos; reposição automática por ponto de pedido (gera PR-001 sem intervenção).

## Melhorias futuras

- Código de barras/QR/RFID na operação; mobile offline para almoxarifado; previsão de demanda (IA); integração fiscal/contábil de valorização de estoque.

---

# 25. Glossário

| Termo | Definição |
|-------|-----------|
| **Item** | Unidade de material cadastrada no Item Catalog; estocável, não estocável ou sob encomenda |
| **Almoxarifado** | Estrutura logística que agrupa depósitos; vinculada a uma unidade organizacional |
| **Depósito** | Local físico de guarda dentro de um almoxarifado |
| **Endereço** | Posição física dentro de um depósito (rua/módulo/nível — granularidade parametrizável) |
| **Saldo disponível** | Saldo total menos saldo reservado; única base válida para validação de estoque |
| **Reserva** | Bloqueio de saldo disponível vinculado a uma solicitação, com validade |
| **Movimentação** | Documento que altera saldo: entrada, saída, reserva, liberação, transferência ou ajuste |
| **Solicitação de Material** | Pedido interno de itens ao almoxarifado (MMS-003) |
| **Rota de compra** | Caminho dos itens sem estoque: viram demanda no domínio PR-001 |
| **Compra dedicada** | Compra vinculada a uma solicitação específica; entrada no estoque já reservada |
| **Recebimento** | Conferência física de material contra documento de origem (MMS-005) |
| **Inventário** | Processo de contagem física e reconciliação de saldos |
| **Acuracidade** | Percentual de itens cujo saldo do sistema confere com o físico |
| **Ruptura** | Saldo zero com demanda em aberto |
| **Curva ABC** | Classificação de itens por representatividade de consumo/valor |

---

# 26. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação do Documento Mestre Funcional da Materials Management Suite: visão, princípios, módulos (Item Catalog, Material Requisition, Inventory Management, Receiving), fluxo corporativo oficial, personas, workspaces, regras gerais, estados, eventos, KPIs, integrações, permissões e roadmap. Base: pacote MASTER PROMPT (anexo do owner), adaptado à documentação oficial existente (FD-001, PR-001) |
