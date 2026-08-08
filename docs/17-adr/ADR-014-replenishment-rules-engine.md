# ADR-014 — Motor de Regras de Reposição (configurável)

**Status:** 🟢 Accepted
**Data:** 2026-08-08 (proposta) · 2026-08-08 (aceita pelo owner)
**Criticidade:** 🟠 Alta
**Origem:** ARC-007 (Domain Benchmark Odoo & ERPNext), candidato de refinamento P1 (#20)

> **Aceita pelo owner em 2026-08-08.** As duas pendências de aceite foram resolvidas (ver "Resolução do aceite"): o motor de **sugestão revisável** entra na **v1.1 do MMS-004** e a **execução automática** permanece **v2.0** (opt-in); o catálogo inicial de políticas de quantidade tem três opções. O MVP não é alterado — até a v1.1, o comportamento vigente continua sendo o dos documentos MMS aprovados (alerta de reposição → demanda manual para PR-001).

---

## Contexto

Hoje a reposição no Trino é **implícita e distribuída**: o Item Catalog (MMS-002) guarda parâmetros por item (mínimo, máximo, ponto de pedido, lead time) e o Inventory (MMS-004) **emite alertas** quando o saldo cruza o mínimo/ruptura. A transformação do alerta em **ação de suprimento** é manual — alguém decide se repõe por compra (PR-001), por transferência entre depósitos ou não repõe. A reposição automática por ponto de pedido está no **roadmap MMS-004 v2.0**.

O benchmark ARC-007 (§3, #20; §4 P1) observou que Odoo e ERPNext tratam isso como um **motor configurável de regras** — Odoo com *Reordering Rules* + *Routes & Rules* (push/pull), ERPNext com *Reorder Level* + *Material Request* automático. O ganho não é a automação em si, mas **tornar explícita e parametrizável a política**: *quando* repor, *quanto* repor e *por qual rota* (estoque próprio via transferência, ou compra via PR-001).

Sem uma decisão registrada, o Trino tende a codificar essa política de forma dispersa (parte no alerta, parte na operação), contrariando o princípio de **parametrização acima de customização** (MMS-P-06 / FD-001-10) e dificultando a automação da v2.0.

## Decisão

1. **Introduzir o conceito de Regra de Reposição** como entidade configurável (FD-001-10), avaliada pelo Inventory (MMS-004) sobre a posição de saldo disponível:
   - **Gatilho:** saldo disponível ≤ ponto de pedido (ou mínimo), por item × depósito.
   - **Quantidade sugerida:** política parametrizável (ex.: repor até o máximo; múltiplo de embalagem — usando o fator de UoM do ADR-013; lote econômico fixo).
   - **Rota de suprimento:** parametrizável por item/depósito — **transferência** de outro depósito com excedente, **compra** (demanda PR-001), ou **nenhuma** (item controlado manualmente).

2. **A regra produz uma sugestão de reposição, não uma ação automática, no primeiro estágio.** A sugestão é um artefato revisável (fila de reposição) que um responsável confirma. A execução automática (gerar PR-001 sem intervenção) permanece **opt-in por parâmetro** e é o alvo da v2.0.

3. **A regra nunca cria saldo nem compra por conta própria fora das fronteiras existentes:** reposição por compra sempre passa pelo domínio PR-001 (a MMS produz demanda, não compra — MMS-001 §3); reposição por transferência sempre gera os documentos de movimentação do MMS-004 (nenhuma movimentação sem documento — MMS-P-07).

4. **Toda avaliação e sugestão são auditadas** (FD-001-06) e aparecem na timeline (FD-001-07); a decisão de rota é rastreável (por que compra e não transferência).

5. **Rastreabilidade bidirecional preservada:** demanda de compra gerada por regra referencia a origem (item/depósito/regra), como já ocorre na rota MMS-003 → PR-001.

## Consequências

* **Documentos impactados:** MMS-004 (avaliação da regra, fila de sugestões, eventos de reposição sugerida/confirmada); MMS-002 (parâmetros da regra por item, além dos já existentes); PR-001 (origem "reposição automática" além de "solicitação de material"); FD-001-10 (namespace `materials.replenishment.*`).
* Prepara formalmente a automação da v2.0 do MMS-004 sem reescrever o modelo — a diferença entre v1 e v2 vira **parâmetro** (sugestão revisável × execução automática).
* Interage com o ADR-013: quantidade sugerida pode respeitar múltiplos de embalagem (fator de UoM).
* **Não** introduz previsão de demanda / IA — isso permanece "funcionalidade futura" do MMS-004; a regra é determinística.

## Alternativas consideradas

* **Manter apenas o alerta atual (sem motor de regras):** mais simples, mas deixa a política de reposição implícita e não parametrizável, contrariando MMS-P-06 e dificultando a v2.0.
* **Automação total imediata (gerar PR-001 automaticamente no MVP):** rejeitada para o primeiro estágio — risco de compras indevidas sem revisão; melhor evoluir de sugestão revisável para automática por parâmetro.

## Resolução do aceite

As pendências que condicionavam o aceite foram decididas pelo owner em 2026-08-08:

1. **Estágio de entrega.** O **motor de sugestão revisável** (fila de sugestões de reposição, com confirmação humana) entra na **v1.1 do MMS-004** — evolução natural dos alertas do MVP, sem alterar o escopo do MVP. A **execução automática** (gerar PR-001 / transferência sem intervenção) permanece na **v2.0**, ativável por parâmetro (opt-in). Assim, a diferença entre v1.1 e v2.0 é apenas configuração (sugestão revisável × execução automática), como previsto na Decisão §2.
2. **Catálogo inicial de políticas de quantidade sugerida:**
   - **(a) Repor até o máximo** — quantidade = máximo − saldo disponível;
   - **(b) Múltiplo de embalagem** — arredonda a sugestão para cima até o múltiplo do fator de UoM (ADR-013), respeitando (a) como teto/piso conforme parâmetro;
   - **(c) Lote econômico fixo** — quantidade fixa por item/depósito (parâmetro).

   Políticas adicionais (ex.: lote econômico calculado, cobertura em dias) ficam como evolução futura e não exigem nova ADR se permanecerem dentro deste modelo de regra.

Estas resoluções são refletidas no roadmap do MMS-004 (v1.1/v2.0) na revisão de módulo correspondente.

## Referências

* ARC-007 — Domain Benchmark: Odoo & ERPNext (candidato P1 #20)
* MMS-001 §3 (fronteira: a MMS não compra) · MMS-002 · MMS-004 · PR-001
* ADR-013 — Conversão de UoM (múltiplos de embalagem)
* FD-001-10 — Configuration (`materials.replenishment.*`)
