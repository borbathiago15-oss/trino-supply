# ADR-013 — Conversão de Unidade de Medida (compra × estoque) no Item Catalog

**Status:** 🟢 Accepted
**Data:** 2026-08-08
**Criticidade:** 🔴 Core
**Origem:** ARC-007 (Domain Benchmark Odoo & ERPNext), candidato de refinamento P1 (#9)

---

## Contexto

O **Item Catalog (MMS-002)** hoje trata a unidade de medida (UoM) como **um único valor por item**, referenciado do Master Data (FD-001-09). Na operação real de suprimentos, porém, um mesmo material é **comprado** em uma unidade e **estocado/consumido** em outra: compra-se uma *caixa com 12 unidades* e o estoque controla *unidades*; compra-se um *rolo de 100 m* e consome-se em *metros*.

O benchmark ARC-007 (§3, #9) mostrou que os dois ERPs de referência (Odoo, ERPNext) tratam isso como capacidade de primeira classe — Odoo com `uom.uom` + categorias de conversão, ERPNext com *UOM Conversion Factor*. Sem conversão explícita, o Trino corre três riscos concretos:

1. **Erro de saldo** — receber "10 caixas" e lançar "10 unidades" (ou o inverso) corrompe a acuracidade, que é o KPI central do MMS-004 (≥ 98%).
2. **Erro de compra** — parâmetros de reposição (mín/máx/ponto de pedido) em unidade de estoque, mas compra em unidade de embalagem, sem fator de conversão, geram quantidades erradas na rota de compra (PR-001).
3. **Retrabalho estrutural** — introduzir conversão depois que saldos e movimentos já existem em produção é caro; a dimensão de UoM precisa ser decidida antes da implementação do estoque.

O princípio de que a UoM é **vocabulário do Master Data** (FD-001-09), e não cadastro do catálogo, permanece — esta decisão adiciona a **relação de conversão**, sem recriar vocabulário.

## Decisão

1. **Toda quantidade no domínio de materiais é sempre expressa e persistida na Unidade de Estoque (base UoM) do item.** O saldo (MMS-004) existe exclusivamente na unidade base. Não há saldo em unidades alternativas.

2. **O Item Catalog (MMS-002) passa a definir, por item:**
   - a **Unidade de Estoque (base)** — obrigatória; é a UoM em que o saldo é mantido;
   - zero ou mais **Unidades Alternativas** com **fator de conversão** para a base (ex.: `1 CX = 12 UN`), cada uma com um **papel** opcional (ex.: unidade de compra padrão, unidade de consumo).

3. **A conversão é uma relação dentro da mesma categoria de UoM do Master Data** (FD-001-09). Não se converte entre categorias incompatíveis (ex.: massa × comprimento). A vigência da UoM é avaliada na data de referência, como já ocorre hoje.

4. **Conversão ocorre nas fronteiras de entrada/saída de dados; o núcleo opera só na base:**
   - Receiving (MMS-005) e movimentações (MMS-004) podem **capturar** a quantidade na unidade alternativa, mas **convertem para a base** antes de afetar o saldo; a quantidade original e a unidade informada ficam registradas no documento para auditoria.
   - A rota de compra (PR-001) pode **exibir/solicitar** na unidade de compra, convertendo de/para a base.

5. **Fator de conversão é imutável para movimentos já realizados.** Alterar o fator de um item **não reprocessa** saldos nem documentos históricos (mesma disciplina de vigência do FD-001-09 e de imutabilidade de movimento do MMS-004). Movimentos guardam o fator aplicado no momento.

6. **Precisão e arredondamento** da conversão são **parametrizáveis** (`materials.item.uom.*`, FD-001-10), com regra de arredondamento explícita e determinística; nenhum comportamento fica implícito em código.

## Consequências

* **Documentos impactados (revisão em passe dedicado, não retroativa):**
  - **MMS-002** — escopo passa a incluir unidade base + unidades alternativas com fator; novas regras `IC-BR` de conversão (categoria compatível, fator > 0, unicidade de papel); domain model ganha o Value Object de conversão; versão do módulo evolui (alvo **1.2.0**).
  - **MMS-004** — movimentações registram quantidade base + (quantidade informada, UoM informada, fator aplicado); saldo permanece só na base.
  - **MMS-005** — conferência aceita unidade de compra e converte na entrada.
  - **PR-001** — rota de compra pode operar na unidade de compra.
  - **MMS-002-11 (Database)** — colunas de fator/UoM base e alternativa; sem alteração de saldo.
* A acuracidade de estoque deixa de depender de disciplina manual de conversão.
* Itens de EPI/Fardamento com grade de tamanhos (MMS-002 v1.1) não são afetados: tamanho é dimensão do item, não UoM.
* Enquanto os documentos de módulo não forem revisados, vale o comportamento atual (UoM única); esta ADR **autoriza e obriga** a revisão, não a substitui.

## Alternativas consideradas

* **Manter UoM única e converter fora do sistema (planilha/manual):** rejeitada — reintroduz exatamente o erro operacional que a suíte existe para eliminar.
* **Saldo multi-unidade (manter saldo em várias UoM):** rejeitada — multiplica a fonte de verdade e quebra o princípio de saldo único derivado de movimento (MMS-P-08).

## Referências

* ARC-007 — Domain Benchmark: Odoo & ERPNext (candidato P1 #9)
* MMS-002 — Item Catalog · MMS-004 — Inventory Management · MMS-005 — Receiving
* FD-001-09 — Master Data (vocabulário de UoM e categorias)
* FD-001-10 — Configuration (`materials.item.uom.*`)
* ADR-009 — Banco como projeção do domínio
