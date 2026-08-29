# Análise de Evolução do TRINO SUPPLY — Trino Supply × Procurement Management System

> Engenharia de evolução do produto, conforme o prompt mestre de 2026-08-29.
> **Documento 1 (fonte oficial da versão atual):** `prompt-sistema-trino-supply.md`, validado contra o
> **código em produção** (branch `main`) — que é a fonte última de verdade.
> **Documento 2 (fonte de melhorias):** proposta "Procurement Management System". O PDF original não
> foi anexado à sessão; o seu conteúdo foi analisado por duas vias que o representam: (a) a enumeração
> detalhada de funcionalidades feita no próprio prompt mestre e (b) o rascunho "TRINO SUPPLY v2"
> gerado por outra IA a partir dos dois documentos (`geminicode…md`). Onde só o rascunho afirma algo,
> isso está sinalizado.

---

## 1. Resumo executivo

O Trino Supply atual é um **sistema operacional de compras completo e íntegro**: solicitação → aprovação → cotação/BID → negociação com saving → duas alçadas por centro de custo → registro da O.C. do SENIOR → faturamento → entrega, com almoxarifado, cadastros, dashboards e timeline auditável. O Procurement Management System não muda esse esqueleto — ele o **envolve com uma camada de gestão**: fornecedores homologados e avaliados, contratos com teto e saldo, compliance medido por processo, SLA/aging operacional, risco e insights.

**Conclusões principais:**

1. **Nenhuma funcionalidade atual precisa ser removida.** Toda a proposta é incorporável por adição — novas tabelas, novos campos anuláveis, novos módulos — sem quebrar as 12 regras críticas.
2. **Dois conflitos estruturais reais** exigem decisão: a **cotação consolidando itens de várias SCs** (hoje a cotação nasce de uma única SC) e a **máquina de estados por item persistida** (hoje o status do processo é derivado, o que é uma força, não uma fraqueza). Recomendações no §5.
3. **O maior ganho imediato custa pouco:** justificativa obrigatória de urgência, aging na triagem, OTIF (os dados já existem), saving de referência (último preço pago) e teto/saldo no contrato de parceria destravam quase toda a camada de compliance e avaliação de fornecedores.
4. **O rascunho v2 do Gemini não serve como substituto do prompt oficial**: ele perde funcionalidades existentes (menu, foto do produto, importação com grade, painel de atendimentos, prazos-meta com meta×realizado, preencher proposta pelo contrato, referência de API…) e **troca o significado de códigos de erro em uso** (`PR-ERR-041`, `RFQ-ERR-020`). Serve como insumo, não como versão oficial. O Prompt Oficial V2 correto está em `prompt-sistema-trino-supply-v2.md`, construído como **superconjunto** do documento atual.
5. Itens de IA/insights foram classificados (§10): quatro detecções já são possíveis com dados atuais como regras determinísticas; o resto precisa de histórico, novos dados ou integração externa.

As quatro perguntas estratégicas ficam respondíveis assim:

| Pergunta | O que responde hoje | O que falta (e onde está no backlog) |
|---|---|---|
| Estamos comprando bem? | Saving de negociação, rankings de spend | Saving de referência, TCO, spend por categoria (P2/P4) |
| Com eficiência? | Prazos meta×realizado por família, tempos médios | SLA por etapa com %, aging, produtividade composta (P2) |
| Com controle? | Alçadas, segregação, timeline, códigos de erro | Compliance Score e seus controles (P2, após P1) |
| Reduzindo riscos? | — (só o C.A. de EPI) | Homologação, certidões, scorecard, risco (P2/P3) |

---

## 2. Matriz de comparação

Situação: **EXISTENTE** · **PARCIALMENTE EXISTENTE** · **NÃO EXISTE** · **CONFLITANTE** · **EVOLUÇÃO FUTURA**. Prioridade: **CRÍTICA/ALTA/MÉDIA/BAIXA**.

### 2.1 Gestão estratégica

| Área | Funcionalidade | TRINO SUPPLY atual | Procurement Management System | Situação | Conflito | Recomendação | Prioridade |
|---|---|---|---|---|---|---|---|
| Saving | Saving de negociação | 1ª proposta do vencedor − valor fechado; imutável, na timeline e no dashboard | Mesma base, entre as várias propostas | EXISTENTE | Não | Manter como está (regra crítica 5) | — |
| Saving | Saving de referência | Não existe | Último preço pago − valor fechado | NÃO EXISTE | Não | Incorporar como **métrica complementar**, nunca substituta; base = último preço pago do item (pedidos anteriores) | ALTA |
| Saving | Outras bases (orçamento, tabela, histórico) | Não existe | Propõe múltiplas bases | CONFLITANTE | Sim (§5-C2) | Não multiplicar bases oficiais; tabela de preços = contrato de parceria (já existe como referência) | BAIXA |
| Saving | Cost Avoidance | Não existe | Reajuste pleiteado − reajuste fechado | NÃO EXISTE | Não | Exige registrar o pleito de reajuste do fornecedor (dado novo, processo novo) | FUTURA |
| Spend | Spend Analysis | Rankings por fornecedor/família/CC/regional/gerente/cliente com filtros | Análise por categoria, classificação estratégica/tática/operacional | PARCIALMENTE | Não | Adicionar dimensão **Categoria** (agrupador de famílias) e a classificação da compra | MÉDIA |
| Spend | Spend Under Management | Não existe | % do spend coberto por contrato | NÃO EXISTE | Não | Deriva de contratos com teto + O.C.s vinculadas | MÉDIA |
| Custo | TCO | Não existe | Custo total de aquisição | EVOLUÇÃO FUTURA | Não | Precisa de impostos/outros custos na proposta primeiro | FUTURA |
| Controle | Compliance Score | Não existe | Índice 0–100 por processo com penalizações | NÃO EXISTE | Não | Incorporar depois das dependências (urgência justificada, homologação, contrato×categoria) | ALTA (fase 2) |
| Risco | Supplier Risk Score | Não existe | 0–100: concentração, dependência, docs, atrasos, single source | NÃO EXISTE | Não | Concentração e single-source já calculáveis; dependência financeira exige dado externo | MÉDIA |

### 2.2 Gestão operacional

| Área | Funcionalidade | TRINO SUPPLY atual | PMS | Situação | Conflito | Recomendação | Prioridade |
|---|---|---|---|---|---|---|---|
| SLA | Prazos por etapa | Meta por **família** (4 etapas) × realizado no dashboard | SLA por etapa **por processo**, % dentro/fora, mediana | PARCIALMENTE | Não | Evoluir o painel atual: medir cada processo contra a meta da família dos seus itens; % dentro/fora e mediana | ALTA |
| Lead time | Lead time total | Soma das 4 etapas (meta e realizado) | Idem + mediana | PARCIALMENTE | Não | Junto com o item acima | ALTA |
| Aging | Faixas de aging na triagem | Data de criação visível; sem faixas | Faixas 0-2 / 3-5 / 6-10 / >10 dias, configuráveis | PARCIALMENTE | Não | Adicionar faixa colorida por item na Gestão de Solicitações + filtro | ALTA |
| Backlog | Visão de backlog | Filas existem (triagem, cotação, aprovação) | Dashboard dedicado com aging e volume | PARCIALMENTE | Não | Dashboard de Backlog (§9.8) | MÉDIA |
| Produtividade | Por comprador | Ranking por valor comprado | Score composto: produtividade, SLA, saving, backlog, qualidade | PARCIALMENTE | Não | Painel do comprador com as 5 dimensões; score composto só quando OTIF/qualidade existirem | MÉDIA |
| Emergencial | Compra urgente | Prioridade NORMAL/URGENTE | URGENTE exige justificativa + impacto da não compra | PARCIALMENTE | Não | **Tornar obrigatórios os 2 campos quando URGENTE** — pré-requisito do compliance | CRÍTICA |
| Retrabalho | Medição de devoluções/ajustes | Eventos existem na timeline | Indicador consolidado | EVOLUÇÃO FUTURA | Não | Contar eventos AJUSTES_SOLICITADOS/devoluções por processo | FUTURA |

### 2.3 Gestão de demandas

| Área | Funcionalidade | TRINO SUPPLY atual | PMS | Situação | Conflito | Recomendação | Prioridade |
|---|---|---|---|---|---|---|---|
| Demandas | Gestão por item | Gestão de Solicitações com uma linha por item, filtros por solicitante/comprador/status | Idem | EXISTENTE | Não | Manter | — |
| Demandas | Status independente por item | 8 status **derivados** por SC (todos os itens da SC seguem o processo) | Máquina de estados persistida por item | CONFLITANTE | Sim (§5-C3) | Manter derivação; granularidade por item vem naturalmente com o agrupamento (abaixo) | — |
| Demandas | Agrupar itens de várias SCs numa cotação | Cotação nasce de **uma** SC (`SourcePrId`) | Consolidação multi-SC com rastreabilidade individual | CONFLITANTE | Sim (§5-C4) | Incorporar com modelo item-a-item; maior mudança estrutural da evolução | ALTA |
| Demandas | Redistribuição entre compradores | Designação individual na triagem (reatribuível) | Transferência em lote | PARCIALMENTE | Não | Seleção múltipla + designar em lote | MÉDIA |
| Demandas | Alterar prioridade com justificativa | Prioridade fixada pelo solicitante | Compras altera com justificativa (evento) | NÃO EXISTE | Não | Incorporar (evento na timeline) | MÉDIA |
| Demandas | Pendência cadastral | Item de descrição livre já é permitido na SC | Flag formal "pendência cadastral" + fila de catalogação | PARCIALMENTE | Não | Marcar visualmente item livre; ação "criar produto a partir do item" | BAIXA |
| Demandas | Sugestão automática de agrupamento | Não existe | Sugerir SCs com o mesmo item | NÃO EXISTE | Não | Determinístico (mesmo `catalogItemId` em aberto); depende do agrupamento | FUTURA |

### 2.4 Fornecedores

| Área | Funcionalidade | TRINO SUPPLY atual | PMS | Situação | Conflito | Recomendação | Prioridade |
|---|---|---|---|---|---|---|---|
| Fornecedor | Cadastro + portal + contrato de parceria | Completo (CNPJ único, chave do portal, contrato com preços/prazos fixos) | Idem + ciclo de vida | EXISTENTE | Não | Manter | — |
| Homologação | Ciclo de vida | `Active` true/false | PROSPECT → EM_HOMOLOGACAO → HOMOLOGADO → RESTRITO → BLOQUEADO → INATIVO | NÃO EXISTE | Sim (§5-C6) | Incorporar com migração conservadora (ativos atuais = HOMOLOGADO) | ALTA |
| Documentos | Certidões com validade | Não existe | CND Federal, FGTS, CNDT, contrato social; alertas 30/15/0; vencida → RESTRITO | NÃO EXISTE | Não | Incorporar sobre `stored_document` + tabela de documentos do fornecedor | ALTA |
| Avaliação | Supplier Scorecard A/B/C/D | Não existe | Score mensal: OTIF, qualidade, lead time, competitividade | NÃO EXISTE | Não | Fase 1: OTIF automático; fase 2: score com classes | MÉDIA |
| OTIF | Pontualidade e integridade | **Dados já existem**: data da O.C. + prazo de entrega da proposta + entregas por item | OTIF % por fornecedor | PARCIALMENTE | Não | Calcular: On-Time = entrega ≤ data O.C.+prazo; In-Full = recebido = pedido. Sem coleta nova | ALTA |
| Qualidade | Devolução/rejeição | Não existe registro de devolução | Taxas de devolução/cancelamento | NÃO EXISTE | Não | Exige registrar devolução no recebimento (dado novo) | MÉDIA |

### 2.5 Contratos

| Área | Funcionalidade | TRINO SUPPLY atual | PMS | Situação | Conflito | Recomendação | Prioridade |
|---|---|---|---|---|---|---|---|
| Contrato | Vigência + itens com preço/prazos fixos + preencher proposta em 1 clique | Existe (contrato de parceria) | Idem | EXISTENTE | Não | Manter — é a base | — |
| Contrato | Teto financeiro + saldo consumido | Não existe | O.C. abate o saldo em tempo real | NÃO EXISTE | Não | Campo teto + consumo derivado das O.C.s do fornecedor no período de vigência | ALTA |
| Contrato | Índice de reajuste, gestor, documentos | Só número/vigência/observação | Campos formais + anexo do contrato | NÃO EXISTE | Não | Adicionar campos + anexo | MÉDIA |
| Contrato | Alertas 90/60/30 e vencidos | Badge VIGENTE/FORA DA VIGÊNCIA | Alertas proativos | PARCIALMENTE | Não | Central de Avisos (interno) já; e-mail quando houver Notification Center | MÉDIA |
| Contrato | Spend sob contrato / compras sem contrato | Não existe | Indicadores | NÃO EXISTE | Não | Deriva do vínculo O.C.×contrato + categoria tabelada | MÉDIA |

### 2.6 Compliance e risco

| Área | Funcionalidade | TRINO SUPPLY atual | PMS | Situação | Conflito | Recomendação | Prioridade |
|---|---|---|---|---|---|---|---|
| Compliance | Compra sem cotação competitiva | Nº de propostas na cotação (dado existe) | Penaliza −25% | PARCIALMENTE | Não | Detectável hoje (1 proposta) | ALTA (fase 2) |
| Compliance | Compra emergencial | Prioridade existe; justificativa não | −20% | PARCIALMENTE | Não | Depende do item CRÍTICO (2.2) | ALTA (fase 2) |
| Compliance | Fornecedor não homologado | Não existe conceito | −30% | NÃO EXISTE | Não | Depende da homologação | ALTA (fase 2) |
| Compliance | Sem contrato em categoria tabelada | Não existe | −15% | NÃO EXISTE | Não | Depende de contratos+categorias | MÉDIA |
| Compliance | Criada após a necessidade | `neededBy` e `createdAt` existem | −20% | PARCIALMENTE | Não | Calculável hoje | ALTA (fase 2) |
| Compliance | Compra acima da alçada | **Alçada atual não tem limite de valor** | Penalização | NÃO EXISTE | Sim (§5-C8) | Criar limite de valor opcional por nível no CC antes de medir | MÉDIA |
| Compliance | Compra sem aprovação | **Impossível por construção** (O.C. só após as 2 alçadas) | Controle | EXISTENTE | Não | Já garantido por `RFQ-ERR-040`; reportar como "0 por construção" | — |
| Risco | Concentração de spend / single source | Dados existem (spend por fornecedor/família) | Alertas ≥40% da categoria | PARCIALMENTE | Não | Regra determinística no Insights | MÉDIA |
| Risco | Dependência financeira do fornecedor | Não existe (faturamento do fornecedor é dado externo) | Estimativa | EVOLUÇÃO FUTURA | Não | Exige dado externo/declarado | FUTURA |

### 2.7 Cotação, notificações, cadastros e plataforma

| Área | Funcionalidade | TRINO SUPPLY atual | PMS | Situação | Conflito | Recomendação | Prioridade |
|---|---|---|---|---|---|---|---|
| Cotação | Impostos e outros custos na proposta | Itens + frete − desconto | Custo total com impostos/outros | NÃO EXISTE | Não | 2 campos novos no total e no mapa | ALTA |
| Cotação | Score multicritério (40/20/20/10/10) com justificativa se não escolher o melhor | Escolha livre com critérios + justificativa obrigatória | Score obrigatório | CONFLITANTE | Sim (§5-C5) | Score **informativo** quando existirem OTIF/qualidade/risco; obrigatoriedade é decisão posterior | FUTURA |
| Cotação | Reaprovação após alteração material | Após a seleção nada muda (propostas imutáveis; ajustes devolvem ao comprador zerando N1) | Alteração invalida etapa e volta ao N1 | EXISTENTE | Não | Já é o comportamento; formalizar no prompt V2 | — |
| Notificações | Central de eventos (16 tipos) | Central de Avisos no dashboard (escopo por alçada/CC) | Central dedicada + e-mail; futuro Teams/WhatsApp | PARCIALMENTE | Não | Ampliar avisos internos (fase 1); e-mail depende do Notification Center (fase 2) | MÉDIA |
| Catálogo | Categoria → Família → Produto | Família → Produto | 3 níveis | CONFLITANTE | Sim (§5-C7) | Categoria como **agrupador opcional de famílias** (aditivo, nada quebra) | MÉDIA |
| SC | Empresa de faturamento no cabeçalho | O CNPJ vem do centro de custo | Campo direto na SC | PARCIALMENTE | Não | Manter derivação pelo CC (menos campo para o leigo); override opcional | BAIXA |
| Perfis | Módulos CONTRATOS, COMPLIANCE, INSIGHTS | 9 módulos atuais | 12 módulos | NÃO EXISTE | Não | Adicionar os 3 módulos | ALTA (junto das features) |
| Perfis | Requester dá baixa de entrega (habilitado) | Entrega é do comprador/estoque | Opcional por usuário | NÃO EXISTE | Não | Flag por usuário; útil em obra | BAIXA |
| Erros | Catálogo de códigos | Códigos estáveis em uso | Rascunho v2 **ressignifica** códigos | CONFLITANTE | Sim (§5-C9) | Códigos são imutáveis; novos conceitos = códigos novos | CRÍTICA (regra) |

---

## 3. Consolidação por situação

**Existentes (preservar tal como estão):** todo o fluxo SC→aprovação→cotação→negociação/saving→2 alçadas→registro O.C.→faturamento→entrega; almoxarifado com faltante→SC; gestão por item na triagem; contrato de parceria com preços fixos; C.A. por fornecedor; prazos-meta por família; dashboards atuais; timeline; portal do fornecedor; importação com validação; foto do produto; painel de atendimentos clicável.

**Parcialmente existentes (ampliar):** SLA/lead time (de meta×realizado por família → medição por processo com %), aging (de data → faixas), spend (de rankings → categoria/classificação), notificações (de avisos internos → central + e-mail), produtividade do comprador (de valor → multidimensional), pendência cadastral, compliance "após a necessidade" e "sem cotação competitiva" (dados prontos), OTIF (dados prontos).

**Novos:** homologação e certidões, scorecard/classes A-D, risco, teto/saldo contratual, compliance score, saving de referência, cost avoidance, impostos na proposta, urgência justificada, agrupamento multi-SC, redistribuição em lote, prioridade alterável, categoria de produtos, insights, dashboards executivo/compliance/contratos/backlog.

---

## 4. Funcionalidades novas — detalhe do que cada uma significa no Trino Supply

(Resumo operacional; a especificação completa está no Prompt V2.)

1. **Urgência justificada** — SC com prioridade URGENTE exige `justificativa de urgência` e `impacto da não compra`; ambos aparecem na triagem, na cotação e na timeline. Erro novo `PR-ERR-050`.
2. **Aging na triagem** — cada item exibe a faixa (0-2, 3-5, 6-10, >10 dias, configuráveis) contada da entrada na fila; filtro por faixa; contadores no topo.
3. **OTIF** — por entrega: data prometida = data da O.C. + prazo de entrega da proposta vencedora; On-Time = entrega concluída até a prometida; In-Full = todas as quantidades recebidas. Consolidação por fornecedor (mensal e acumulado).
4. **Saving de referência** — no registro da O.C., o sistema compara o preço unitário fechado com o **último preço pago** do mesmo item de catálogo (última O.C. não cancelada) e grava a diferença como métrica separada do saving de negociação.
5. **Teto e saldo contratual** — contrato ganha `valor contratado`; cada O.C. de fornecedor com contrato vigente abate o saldo; painel mostra consumo/spend sob contrato; aviso quando saldo < 20%.
6. **Homologação** — estados PROSPECT/EM_HOMOLOGACAO/HOMOLOGADO/RESTRITO/BLOQUEADO/INATIVO; certidões com validade e alertas; vencida → RESTRITO automático; PROSPECT pode ser convidado e propor, mas a **seleção do vencedor** exige homologado (`SUP-ERR-030`); migração marca ativos atuais como HOMOLOGADO.
7. **Agrupamento multi-SC** — o comprador seleciona itens aprovados de várias SCs (mesmo item/categoria) e abre uma cotação única; cada item da cotação referencia o item de SC de origem; status, aviso e rastreabilidade continuam por solicitante; a O.C. registra o rateio.
8. **Compliance Score** — 100 − penalizações (§2.6) por processo, com o detalhamento de cada penalidade na tela do processo; média por comprador/CC/período no dashboard.
9. **Insights determinísticos** — sobrepreço (>15% sobre o histórico do item), fracionamento (mesmo solicitante+item/categoria em janela curta), emergenciais recorrentes por CC, concentração ≥40% da categoria.
10. **Categoria** — cadastro simples que agrupa famílias; usada em spend, contratos ("categoria tabelada") e insights.

---

## 5. Conflitos encontrados (Etapa 4)

### C1 — Emissão × registro da O.C.
- **Regra atual:** a O.C. é fechada no ERP SENIOR; o Trino Supply **registra** número/data/anexo após as duas alçadas (`RFQ-ERR-040/041`).
- **Regra proposta:** o PMS genérico fala em "emissão de O.C.".
- **Conflito:** frontal com a regra crítica 6. **Impacto técnico:** reintroduzir numeração própria e PDF oficial; **operacional:** duplicaria o financeiro do SENIOR; **risco:** O.C. divergente entre sistemas.
- **Recomendação/decisão sugerida:** **manter o registro**. O rascunho v2 já converge para isso. Emissão própria só se o SENIOR sair de cena — decisão de negócio, não técnica.

### C2 — Base de cálculo do saving
- **Atual:** saving = 1ª proposta do fornecedor vencedor − valor fechado (imutável, auditável).
- **Proposta:** múltiplas bases (último preço pago, orçamento, tabela, histórico).
- **Conflito:** múltiplas "verdades" para o mesmo número destroem a rastreabilidade (regra crítica 5). **Risco:** inflar saving trocando a base a posteriori.
- **Decisão sugerida:** o **saving de negociação permanece a métrica oficial e única com esse nome**. Adicionar **Saving de Referência** (último preço pago) como métrica separada, com nome próprio, calculada e congelada no registro da O.C. **Cost Avoidance** é uma terceira métrica futura (precisa do pleito de reajuste registrado). Orçamento inicial como base: rejeitado (o preço estimado da SC é do solicitante leigo — base frágil).

### C3 — Máquina de estados por item persistida
- **Atual:** a SC tem 7 estados persistidos; os **8 status do processo são derivados** de (SC, cotação, pedido) — nunca dessincronizam.
- **Proposta (rascunho v2):** máquina única persistida por item com ~15 estados (RASCUNHO→…→ENTREGUE_TOTAL).
- **Conflito:** persistir o que hoje é derivado cria dupla fonte de verdade. **Risco:** status fantasma após qualquer bug de sincronização; migração de dados arriscada.
- **Decisão sugerida:** **manter a derivação** e apenas **enriquecer o rótulo derivado** (ex.: distinguir FATURADO_PARCIAL de AGUARDANDO_ENTREGA dentro do atual OC_FATURAMENTO). Com o agrupamento (C4), a derivação passa a ser calculada por item de SC — sem coluna de status nova.

### C4 — Cotação consolidando várias SCs
- **Atual:** `Quotation.SourcePrId` único; aprovação da SC de origem, triagem e `ProcessStatus` assumem 1:1.
- **Proposta:** agrupar itens de várias SCs numa cotação, mantendo rastreabilidade individual.
- **Conflito:** estrutural (o maior da evolução). **Impacto técnico:** itens da cotação passam a referenciar o item de SC de origem; `MarkSourcePrApproved`, triagem, avisos e `ProcessStatus` passam a operar por item/SC de origem; a O.C. rateia por origem. **Impacto operacional:** grande ganho (menos cotações repetidas, mais poder de negociação). **Risco:** regressão no fluxo mais usado do sistema, se feito sem cuidado.
- **Decisão sugerida:** **incorporar** (prioridade ALTA), em entrega própria e isolada, preservando o caminho 1:1 como caso particular (uma cotação de uma SC continua funcionando idêntico). A regra crítica 1 é generalizada sem se perder: *"a cotação nasce somente de itens de SC aprovados"*.

### C5 — Score multicritério obrigatório na escolha
- **Atual:** escolha livre com critérios marcados + justificativa obrigatória; segregação nas alçadas.
- **Proposta:** score 0–100 (Preço 40%, Prazos 20%, OTIF 20%, Qualidade 10%, Risco 10%); fugir do melhor score exige justificativa.
- **Conflito:** 3 dos 5 componentes (OTIF, qualidade, risco) **ainda não existem** — um score obrigatório hoje seria pseudociência e travaria a operação.
- **Decisão sugerida:** fase 1 **informativo** (aparece no mapa quando os componentes existirem), sem obrigar; a obrigatoriedade vira decisão do gestor quando o dado estiver maduro. Não alterar a regra atual de justificativa.

### C6 — Homologação bloqueante × fornecedores atuais
- **Atual:** fornecedor ativo participa de tudo. **Proposta:** só homologado fecha processo; certidão vencida restringe.
- **Conflito:** aplicar o bloqueio de imediato **pararia todas as compras** (ninguém está "homologado").
- **Decisão sugerida:** migração marca todo fornecedor ativo como **HOMOLOGADO** (grandfathering); o bloqueio `SUP-ERR-030` atua na **seleção do vencedor** (não no convite); RESTRITO automático só quando o cadastro de certidões daquele fornecedor estiver preenchido. Adoção progressiva sem parar a operação.

### C7 — Categoria acima de Família
- **Atual:** Família → Produto (famílias com prazos-meta, código automático, importação). **Proposta:** Categoria → Família → Produto.
- **Conflito:** menor; risco de duplicar conceitos.
- **Decisão sugerida:** Categoria = **atributo opcional da família** (tabela nova + FK anulável). Nada da família muda; spend/contratos/insights ganham a dimensão.

### C8 — "Compra acima da alçada"
- **Atual:** a alçada é **quem** aprova (listas N1/N2 por CC), sem limite de valor. O controle proposto pressupõe limite por valor.
- **Decisão sugerida:** adicionar **limite de valor opcional por nível no CC** (acima do limite do N2, exigir… não há N3 — então o limite serve para **medir e alertar**, não bloquear, até existir política formal). Sem limite cadastrado, o controle não penaliza.

### C9 — Ressignificação de códigos de erro (rascunho v2)
- **Atual:** `PR-ERR-041` = SC já com comprador/cotação (peça a alteração a ele); `RFQ-ERR-020` = transição inválida de estado; `RFQ-ERR-022` = valor negociado não é ganho.
- **Rascunho v2:** `PR-ERR-041` = "exclusão de SC com cotação vinculada"; `RFQ-ERR-020` = "inclusão de fornecedor em cotação finalizada"; cria `RFQ-ERR-001` para regra já coberta.
- **Conflito:** códigos estão em produção, em testes e em documentação. **Risco:** quebra de contrato de API e de suporte.
- **Decisão:** **códigos de erro são imutáveis**. Conceito novo ⇒ código novo (faixas reservadas no V2). O rascunho não deve ser adotado literalmente.

### C10 — O rascunho v2 como substituto do prompt oficial
- **Conflito:** o rascunho omite funcionalidades existentes (menu completo, foto do produto, importação com grade de tamanhos, painel de atendimentos clicável, meta×realizado, "preencher pelo contrato", janela de alteração da SC, empresas/CNPJs, referência de API) — adotá-lo violaria a regra de preservação.
- **Decisão:** usar como **insumo**; o Prompt Oficial V2 é o superconjunto construído neste trabalho.

---

## 6. Backlog de Evolução do TRINO SUPPLY (Etapa 5)

### PRIORIDADE 1 — CRÍTICA (controle e integridade; pré-requisitos baratos)
1. **Urgência justificada** (SC urgente exige justificativa + impacto) — destrava compliance de emergenciais.
2. **Imutabilidade dos códigos de erro** formalizada no prompt (regra de engenharia, custo zero).
3. **Aging na triagem** (faixas + filtro) — controle imediato do backlog com dado existente.
4. **OTIF** (cálculo automático por entrega e consolidação por fornecedor) — dado existente, base de scorecard/risco/score de cotação.

### PRIORIDADE 2 — ALTA (impacto operacional/financeiro forte)
5. **Saving de referência** (último preço pago, congelado no registro da O.C.).
6. **Impostos e outros custos na proposta** (e no mapa de cotação).
7. **Teto financeiro e saldo do contrato** + spend sob contrato.
8. **Homologação de fornecedores + certidões com validade** (com grandfathering, `SUP-ERR-030` na seleção).
9. **SLA por processo** (% dentro/fora, mediana, por etapa — evolução do painel meta×realizado).
10. **Agrupamento de demandas multi-SC** (C4) — entrega isolada, com testes de regressão do fluxo 1:1.
11. **Compliance Score** (após 1, 8; componentes calculáveis primeiro).
12. **Módulos novos** `CONTRATOS`, `COMPLIANCE`, `INSIGHTS` na matriz de autorização.

### PRIORIDADE 3 — MÉDIA
13. Categoria de produtos (agrupador de famílias) + spend por categoria.
14. Supplier Scorecard mensal com classes A/B/C/D (usa OTIF + competitividade; qualidade entra quando houver devolução).
15. Registro de devolução/rejeição no recebimento (habilita "qualidade").
16. Redistribuição de demandas em lote; alteração de prioridade com justificativa.
17. Alertas de contrato (90/60/30/vencido) e de certidão na Central de Avisos.
18. Painel do comprador multidimensional (volume, SLA, saving, backlog).
19. Insights determinísticos: sobrepreço, fracionamento, emergenciais recorrentes, concentração.
20. Limite de valor opcional por nível no CC (medição, não bloqueio).
21. Dashboards: Executivo, Compliance, Contratos, Backlog (§9).

### PRIORIDADE 4 — FUTURA (preparar arquitetura, não desenvolver agora)
22. Cost Avoidance (exige registrar pleito de reajuste).
23. TCO (depende de impostos/custos acumulados).
24. Supplier Risk Score completo (dependência financeira = dado externo).
25. Score multicritério na escolha (após OTIF/qualidade/risco maduros).
26. Notificações por e-mail/Teams/WhatsApp (Notification Center FD-001-05).
27. Sugestão automática de agrupamento; previsão de demanda; sazonalidade; assistente em linguagem natural.
28. Score composto de compradores (após SLA+OTIF maduros).

---

## 7. Impacto na arquitetura (Etapa 6) — por item do backlog P1/P2

| Item | Banco (tabelas/campos) | APIs | Fluxos/Status | Front | Segurança/Perfis |
|---|---|---|---|---|---|
| Urgência justificada | `purchase_requisition`: `urgency_reason`, `urgency_impact` (anuláveis) | `POST/PATCH /purchase-requisitions` validam quando URGENTE (`PR-ERR-050`) | Sem novo status; timeline registra | 2 campos condicionais na Inclusão de SC; exibição na triagem/cotação | — |
| Aging | Nada (usa `created_at`/designação); parâmetro de faixas em config | `GET /triage` devolve `agingDays`/faixa | — | Badge de faixa + filtro + contadores | — |
| OTIF | `purchase_order`: `promised_date` (congelada no registro); leitura de entregas existentes | `GET /analytics/suppliers/{id}` (novo), campos em `PoView` | — | Coluna OTIF no detalhe do pedido e no dashboard de fornecedores | — |
| Saving de referência | `purchase_order_item`: `last_paid_unit_price`, `reference_saving` (congelados) | Calculado no `register-po` | — | Linha no painel de saving | — |
| Impostos/custos | `proposal`: `tax_value`, `other_costs` | `ProposalInput` +2 campos | Total = itens+frete+impostos+outros−desconto | Mapa de cotação +2 linhas | — |
| Contrato teto/saldo | `supplier`: `contract_value_limit`; consumo derivado (view/consulta) | `PUT /suppliers/{id}/contract` +campo; `GET` devolve saldo | Aviso saldo <20% | Painel do contrato mostra consumo/saldo | — |
| Homologação | `supplier`: `homologation_status`; nova `supplier_document` (tipo, validade, doc) | `POST /suppliers/{id}/documents`, `PATCH …/homologation` | `SUP-ERR-030` na seleção; RESTRITO automático por certidão vencida (job/na leitura) | Aba Homologação no fornecedor; badge de estado | `SupplyManager` homologa |
| SLA por processo | Nada novo (timestamps existem) | `GET /analytics/sla` | — | Painel evolui para % dentro/fora + mediana | — |
| Agrupamento multi-SC | `quotation_item`: `source_pr_item_id` (FK); `quotation.source_pr_id` → anulável (legado preservado) | `POST /quotations` aceita lista de itens de SC; triagem com seleção múltipla | Derivação de status por item de SC; aviso por solicitante | Seleção múltipla na Gestão de Solicitações; origem visível no processo/O.C. | — |
| Compliance Score | Derivado (sem coluna); parâmetros de penalização em config | `GET /analytics/compliance`; detalhe no processo | — | Selo no processo + dashboard | Módulo `COMPLIANCE` |
| Módulos novos | Enum `AppModules` +3 | Filtros de módulo nos grupos novos | — | Checkboxes no usuário | Sim |

**Migrations:** todas aditivas e com preservação (padrão já praticado — ex.: C.A. copiado antes de remover coluna). **ERP SENIOR:** nenhuma integração nova é exigida por P1/P2 (a O.C. continua registrada manualmente); integração automática fica como evolução futura. **PostgreSQL:** volumetria baixa; índices em `supplier_document(valid_until)`, `quotation_item(source_pr_item_id)`.

---

## 8. Dados necessários por indicador (Etapa 7)

| Indicador | Dados necessários | Já existe | Precisa criar |
|---|---|---|---|
| OTIF | data O.C., prazo prometido, data/qtde das entregas | ✔ tudo | congelar `promised_date` |
| Saving de negociação | versões da proposta | ✔ | — |
| Saving de referência | último preço pago por item de catálogo | ✔ (O.C.s anteriores) | congelar no registro |
| Cost Avoidance | pleito de reajuste × fechado | ✖ | registro do pleito |
| Compliance Score | nº propostas, urgência justificada, homologação, contrato×categoria, neededBy×createdAt, limite de alçada | parcial | urgência (P1), homologação (P2), categoria tabelada (P3), limite (P3) |
| Supplier Scorecard | OTIF, lead time real, competitividade (preço × melhor do mapa), qualidade | OTIF/lead/competitividade derivam de dados existentes | devolução/rejeição |
| Risk Score | concentração, single source, docs, atrasos, dependência financeira | concentração/single/atrasos ✔ | docs (P2), dependência (externa) |
| SLA/Aging | timestamps de cada etapa | ✔ (timeline) | — |
| Spend sob contrato | vínculo O.C.×contrato vigente | derivável (fornecedor+vigência) | vínculo explícito na O.C. (recomendado) |
| Produtividade composta | itens conduzidos, SLA, saving, backlog por comprador | ✔ | — (qualidade depois) |

---

## 9. Dashboards propostos (Etapa 8)

Formato: Objetivo · Público · KPIs · Filtros · Visuais · Alertas · Ações/Drill-down · Origem.

1. **Executivo** — Responder às 4 perguntas em uma tela · Diretoria/SupplyManager · Spend total, saving total (negociação+referência), % SLA, compliance médio, nº fornecedores críticos, spend sob contrato · período/categoria/regional · 4 blocos-pergunta com tendência mensal · fornecedor crítico, contrato vencendo · drill para os dashboards temáticos · dados agregados dos módulos.
2. **Operacional** — O dia a dia da fila · SupplyManager/compradores · backlog por etapa, aging médio, % SLA no mês, urgentes abertas · comprador/CC/família · funil por etapa + aging empilhado · SLA estourando · clique abre a Gestão de Solicitações filtrada · triagem+timeline.
3. **Compradores** — Desempenho individual balanceado · SupplyManager · por comprador: itens conduzidos, tempo médio por etapa, % SLA, saving, backlog atual · período/comprador · tabela comparativa + radar (futuro score) · comprador fora de SLA · drill para os processos dele · cotações+timeline.
4. **Fornecedores** — Base saudável? · Compras/SupplyManager · nº por estado de homologação, OTIF médio, classes A-D (futuro), certidões a vencer · categoria/estado · ranking OTIF, tabela de certidões · certidão 30/15/0, classe D · drill para o cadastro · fornecedores+O.C.s+entregas.
5. **Financeiro** — Quanto e como se gasta · Diretoria/Controladoria · spend por mês, por categoria, sob contrato ×fora, faturado ×entregue, saving acumulado · período/categoria/CC/empresa · série mensal + pizza sob contrato · faturamento > O.C. · drill O.C./NF · pedidos+notas+contratos.
6. **Compliance** — Compramos conforme a política? · Auditor/Diretoria · score médio, distribuição, processos <70, penalidade mais frequente · período/CC/comprador · histograma + tabela de ocorrências · processo <50 · drill para o processo com o detalhamento da pena · derivação sobre processos.
7. **Contratos** — Cobertura e vigência · Compras/SupplyManager · nº vigentes, saldo total, consumo %, vencendo 90/60/30, spend sem contrato em categoria tabelada · categoria/fornecedor · linha do tempo de vigências + barras de consumo · saldo <20%, vencido · drill para o contrato · contratos+O.C.s.
8. **Backlog** — O que está parado e há quanto tempo · SupplyManager · itens por faixa de aging × etapa, mais antigo, entradas ×saídas na semana · comprador/família/CC · heatmap etapa×faixa · item >10 dias · designar direto do dashboard · triagem.
9. **Procurement Insights** — Anomalias e oportunidades · SupplyManager/Auditor · nº alertas por tipo (sobrepreço, fracionamento, emergencial recorrente, concentração) · período/categoria · lista de alertas com evidência (números, datas, valores) · novos alertas da semana · marcar como tratado/justificado (evento) · regras determinísticas sobre a base.

---

## 10. Inteligência Artificial / Procurement Insights (Etapa 9)

| Capacidade | Classificação | Observação |
|---|---|---|
| Detecção de sobrepreço (>15% do histórico do item) | **Já é possível** (determinístico) | histórico de propostas/O.C.s existe; melhora com mais meses |
| Detecção de compras repetidas | **Já é possível** | mesmo item de catálogo em SCs próximas |
| Fracionamento de compras | **Precisa de novos dados** | a evidência (SCs próximas) existe; o gatilho "abaixo do limite de alçada" exige o limite por valor (P3-20) |
| Emergenciais recorrentes por CC | **Já é possível**; fica confiável após P1-1 | prioridade existe; justificativa qualifica |
| Concentração/depend. de fornecedor (spend) | **Já é possível** | ≥40% da categoria após P3-13; hoje por família |
| Dependência financeira do fornecedor | **Precisa de integração externa** | faturamento do fornecedor não é dado interno |
| Sugestão de fornecedores | **Precisa de histórico** | scorecard + histórico por categoria primeiro |
| Sugestão de agrupamento | **Precisa de novos dados** (C4 implementado) | determinístico depois do agrupamento |
| Previsão de demanda / sazonalidade | **Precisa de histórico** (12m+) | futura |
| Assistente / perguntas em linguagem natural | **Futura** (integração LLM) | só depois de os indicadores existirem — IA não compensa dado inexistente |

Princípio adotado: **primeiro regras determinísticas com evidência exibida** (auditáveis), IA generativa por último.

---

## 11. Nova arquitetura funcional proposta (Etapa 10)

24 módulos, mapeados ao que existe:

| # | Módulo | Base atual | Evolução |
|---|---|---|---|
| 1 | Dashboard Executivo | — | novo (P3) |
| 2 | Dashboard Operacional | Dashboard de Suprimentos | evolui |
| 3 | Solicitações | Inclusão SC/Lote/Meus Pedidos | +urgência justificada |
| 4 | Gestão de Demandas | Gestão de Solicitações | +aging, lote, prioridade, agrupamento |
| 5 | Compras | condução do comprador | — |
| 6 | Cotações | Abrir Cotação/Processos | +impostos, multi-SC, score informativo |
| 7 | Aprovações | Central de Aprovação + alçadas N1/N2 | — |
| 8 | Pedidos | Pedidos de Compra | +promised_date, vínculo contrato |
| 9 | Faturamento | no processo | — |
| 10 | Entregas | no processo | +OTIF, devolução (P3) |
| 11 | Fornecedores | cadastro+portal | +estados |
| 12 | Homologação | — | novo (P2) |
| 13 | Avaliação de Fornecedores | — | novo (P3) |
| 14 | Contratos | contrato de parceria | +teto/saldo/reajuste/gestor |
| 15 | Produtos e Serviços | catálogo | — |
| 16 | Categorias e Famílias | famílias | +categoria |
| 17 | Centros de Custo | CC + alçadas | +limite de valor opcional |
| 18 | Compliance | — | novo (P2) |
| 19 | Backlog | filas | dashboard (P3) |
| 20 | SLA | meta×realizado | +% e mediana |
| 21 | Notificações | Central de Avisos | +tipos; e-mail futuro |
| 22 | Auditoria | timeline por processo | trilha consolidada (P3) |
| 23 | Procurement Insights | — | novo (P3) |
| 24 | Administração | usuários/módulos/empresas | +3 módulos, parâmetros (faixas de aging, % sobrepreço, penalidades) |

---

## 12. Funcionalidades rejeitadas ou fora de escopo (Etapa 11)

| Proposta | Por que não agora | Risco se entrasse | Destino |
|---|---|---|---|
| Emissão própria de O.C. | Contradiz o processo com o SENIOR (regra crítica 6) | O.C. divergente entre sistemas | Fora de escopo enquanto o SENIOR for o emissor |
| Múltiplas bases oficiais de saving | Destrói a rastreabilidade da métrica | Saving "escolhido a dedo" | Rejeitado; referência entra como métrica separada |
| Máquina de estados persistida por item | Dupla fonte de verdade vs. derivação atual | Status dessincronizado | Rejeitado; derivação enriquecida |
| Score multicritério obrigatório já | 3 dos 5 componentes não existem | Escolhas travadas por número sem lastro | Futura (informativo primeiro) |
| Ressignificar códigos de erro | Quebra contrato de API/testes/suporte | Regressões silenciosas | Rejeitado; códigos imutáveis |
| Liquidação financeira / pagamento | Escopo do financeiro no SENIOR | Duplicação contábil | Fora de escopo |
| FISPQ / estoque físico | Decisão já tomada (SESMT/WMS) | Reabrir escopo encerrado | Fora de escopo |
| Dependência financeira do fornecedor | Dado externo não disponível | Score de risco fictício | Futura (se houver fonte) |
| Assistente IA em linguagem natural | Indicadores ainda não existem | Custo sem valor | Futura |
| WhatsApp/Teams | Depende do Notification Center | Integração antes do básico (e-mail) | Futura |

---

## 13. Checklist de preservação (Etapa 12 — auditoria do V2)

Verificação do `prompt-sistema-trino-supply-v2.md` contra as 12 regras críticas:

| # | Regra crítica | Situação no V2 |
|---|---|---|
| 1 | Cotação nasce de SC aprovada | ✅ Preservada e **generalizada** (itens aprovados, de uma ou várias SCs) — **ALTERAÇÃO DE REGRA EXISTENTE — NECESSITA APROVAÇÃO** (só o "uma SC"→"itens de SCs"; o "aprovada" é intocado) |
| 2 | Propostas versionadas e imutáveis | ✅ Intocada |
| 3 | Segregação de funções | ✅ Intocada (comprador ∉ N1/N2; N1 ≠ N2) |
| 4 | Alçadas por centro de custo | ✅ Intocada (limite de valor é **adicional e opcional**, não substitui as listas) |
| 5 | Saving rastreável | ✅ Intocada; métricas novas têm nomes próprios e são congeladas |
| 6 | O.C. registrada, não emitida | ✅ Intocada |
| 7 | Faltante do almoxarifado vira SC | ✅ Intocada (no nome do solicitante original) |
| 8 | EPI/EPC com C.A. por fornecedor | ✅ Intocada |
| 9 | Solicitante restrito aos CCs vinculados | ✅ Intocada |
| 10 | Toda decisão gera timeline | ✅ Intocada e estendida aos módulos novos |
| 11 | Importação recusa inválido/duplicado | ✅ Intocada |
| 12 | Acesso = papel E módulo | ✅ Intocada; +3 módulos |

Regras adicionais preservadas: janela de alteração da SC, quantidades reduzíveis (nunca o item trocado) na aprovação de material, contrato preenchendo a proposta, C.A. na importação, menus, cache do SPA, ritual de entrega, **códigos de erro imutáveis** (novos conceitos em faixas novas).

**Única alteração de regra existente no V2:** a generalização da regra 1 (multi-SC), destacada acima e no próprio V2 — não implementar sem a sua aprovação.

O rascunho v2 do Gemini **reprova** neste checklist (perde funcionalidades e ressignifica códigos) — motivo pelo qual não foi adotado como base.

---

*Documentos-resultado: este arquivo (etapas 1–11 e checklist) e `prompt-sistema-trino-supply-v2.md` (etapa 12).*
