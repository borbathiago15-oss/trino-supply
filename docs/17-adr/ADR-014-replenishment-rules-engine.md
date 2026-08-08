# ADR-014 — Motor de Regras de Reposição (configurável)

**Status:** 🟡 Proposed
**Data:** 2026-08-08
**Criticidade:** 🟠 Alta
**Origem:** ARC-007 (Domain Benchmark Odoo & ERPNext), candidato de refinamento P1 (#20)

> **Status Proposed:** esta ADR registra a direção de projeto recomendada pelo benchmark, mas **depende de aceite do owner** por expandir o comportamento de reposição do MVP em direção à automação prevista para a v2.0. Enquanto não aceita, o comportamento vigente é o dos documentos MMS aprovados (alerta de reposição → demanda manual para PR-001).

---

## Contexto

Hoje a reposição no Trino é **implícita e distribuída**: o Item Catalog (MMS-002) guarda parâmetros por item (mínimo, máximo, ponto de pedido, lead time) e o Inventory (MMS-004) **emite alertas** quando o saldo cruza o mínimo/ruptura. A transformação do alerta em **ação de suprimento** é manual — alguém decide se repõe por compra (PR-001), por transferência entre depósitos ou não repõe. A reposição automática por ponto de pedido está no **roadmap MMS-004 v2.0**.

O benchmark ARC-007 (§3, #20; §4 P1) observou que Odoo e ERPNext tratam isso como um **motor configurável de regras** — Odoo com *Reordering Rules* + *Routes & Rules* (push/pull), ERPNext com *Reorder Level* + *Material Request* automático. O ganho não é a automação em si, mas **tornar explícita e parametrizável a política**: *quando* repor, *quanto* repor e *por qual rota* (estoque próprio via transferência, ou compra via PR-001).

Sem uma decisão registrada, o Trino tende a codificar essa política de forma dispersa (parte no alerta, parte na operação), contrariando o princípio de **parametrização acima de customização** (MMS-P-06 / FD-001-10) e dificultando a automação da v2.0.

## Decisão (proposta)

1. **Introduzir o conceito de Regra de Reposição** como entidade configurável (FD-001-10), avaliada pelo Inventory (MMS-004) sobre a posição de saldo disponível:
   - **Gatilho:** saldo disponível ≤ ponto de pedido (ou mínimo), por item × depósito.
   - **Quantidade sugerida:** política parametrizável (ex.: repor até o máximo; múltiplo de embalagem — usando o fator de UoM do ADR-013; lote econômico fixo).
   - **Rota de suprimento:** parametrizável por item/depósito — **transferência** de outro depósito com excedente, **compra** (demanda PR-001), ou **nenhuma** (item controlado manualmente).

2. **A regra produz uma sugestão de reposição, não uma ação automática, no primeiro estágio.** A sugestão é um artefato revisável (fila de reposição) que um responsável confirma. A execução automática (gerar PR-001 sem intervenção) permanece **opt-in por parâmetro** e é o alvo da v2.0.

3. **A regra nunca cria saldo nem compra por conta própria fora das fronteiras existentes:** reposição por compra sempre passa pelo domínio PR-001 (a MMS produz demanda, não compra — MMS-001 §3); reposição por transferência sempre gera os documentos de movimentação do MMS-004 (nenhuma movimentação sem documento — MMS-P-07).

4. **Toda avaliação e sugestão são auditadas** (FD-001-06) e aparecem na timeline (FD-001-07); a decisão de rota é rastreável (por que compra e não transferência).

5. **Rastreabilidade bidirecional preservada:** demanda de compra gerada por regra referencia a origem (item/depósito/regra), como já ocorre na rota MMS-003 → PR-001.

## Consequências (se aceita)

* **Documentos impactados:** MMS-004 (avaliação da regra, fila de sugestões, eventos de reposição sugerida/confirmada); MMS-002 (parâmetros da regra por item, além dos já existentes); PR-001 (origem "reposição automática" além de "solicitação de material"); FD-001-10 (namespace `materials.replenishment.*`).
* Prepara formalmente a automação da v2.0 do MMS-004 sem reescrever o modelo — a diferença entre v1 e v2 vira **parâmetro** (sugestão revisável × execução automática).
* Interage com o ADR-013: quantidade sugerida pode respeitar múltiplos de embalagem (fator de UoM).
* **Não** introduz previsão de demanda / IA — isso permanece "funcionalidade futura" do MMS-004; a regra é determinística.

## Alternativas consideradas

* **Manter apenas o alerta atual (sem motor de regras):** mais simples, mas deixa a política de reposição implícita e não parametrizável, contrariando MMS-P-06 e dificultando a v2.0.
* **Automação total imediata (gerar PR-001 automaticamente no MVP):** rejeitada para o primeiro estágio — risco de compras indevidas sem revisão; melhor evoluir de sugestão revisável para automática por parâmetro.

## Pendências para aceite

* Confirmar com o owner se o **motor de sugestão revisável** entra já no MVP do MMS-004 ou permanece integralmente na v1.1/v2.0.
* Definir o catálogo inicial de políticas de quantidade (até o máximo / múltiplo de embalagem / lote fixo).

## Referências

* ARC-007 — Domain Benchmark: Odoo & ERPNext (candidato P1 #20)
* MMS-001 §3 (fronteira: a MMS não compra) · MMS-002 · MMS-004 · PR-001
* ADR-013 — Conversão de UoM (múltiplos de embalagem)
* FD-001-10 — Configuration (`materials.replenishment.*`)
