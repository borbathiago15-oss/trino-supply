**Documento:** ARC-007 — Domain Benchmark: Odoo & ERPNext (Anexo Informativo)
**Versão:** 1.0.0
**Status:** Approved (informativo / referência)
**Criticidade:** 🟡 Referência
**Escopo:** Domínios de Materiais e Suprimentos (MMS-* e PR-001)

> **Natureza deste documento.** Anexo **informativo** de benchmark. Não é decisão de arquitetura e **não altera** nenhum documento normativo. Qualquer conceito aqui recomendado só entra no produto pela via normal: revisão do módulo correspondente (MMS-*/PR-001) e, quando houver mudança estrutural, **ADR**. O documento normativo que fecha a seção de arquitetura continua sendo ARC-006.
>
> **Aviso de licença e propriedade intelectual.** Odoo Community é **LGPLv3**; ERPNext é **GPL-3.0**. Ambos são estudados aqui **apenas como referência conceitual de domínio**. É proibido copiar código, esquema de banco ou texto desses projetos para o Trino Supply (stack .NET/Next.js, proprietário). Todo conteúdo abaixo é descrição própria de conceitos de ERP amplamente conhecidos.
>
> Documentos relacionados: ARC-001, ARC-002, MMS-001, MMS-002, MMS-004, PR-001, ADR-009/010/012.

# ARC-007 — Domain Benchmark: Odoo & ERPNext

## 1. Objetivo e método

O owner pediu para avaliar se os repositórios **Odoo** (`odoo/odoo`) e **ERPNext** (`frappe/erpnext`) trazem algo que **complete o sistema e a arquitetura** do Trino Supply.

Método: comparar o **modelo de domínio de suprimentos** desses dois ERPs maduros com o que o Trino Supply já tem documentado (MMS-001/002/004 e PR-001), classificar cada conceito por **status de cobertura** e destacar um pequeno conjunto de **candidatos a refinamento**.

### 1.1 Conclusão executiva

1. **Nenhum dos dois "completa" a arquitetura.** São stacks incompatíveis (Odoo: Python + ORM próprio; ERPNext: Frappe/Python + Vue) e licenças copyleft (LGPL/GPL). Não há reaproveitamento de código ou de camadas para um SaaS proprietário .NET/Next.js. A arquitetura do Trino (ARC-001..006) permanece válida.
2. **Como benchmark de domínio, o resultado é majoritariamente de validação.** O modelo central do Trino — **saldo derivado do razão de movimentações** (ADR-009 + MMS-004, princípio MMS-P-08) — é exatamente o modelo dos dois ERPs (Odoo `stock.move`→`stock.quant`; ERPNext *Stock Ledger Entry*→*Bin*). O desenho do Trino está alinhado à prática de mercado.
3. **Há um conjunto pequeno de refinamentos candidatos** (seção 4), a maioria já implícita no roadmap dos módulos MMS.

---

## 2. Os dois sistemas de referência

| Aspecto | Odoo (Community) | ERPNext |
|---------|------------------|---------|
| Stack | Python, ORM próprio, QWeb; PostgreSQL | Frappe Framework (Python/JS), Vue (Frappe UI); MariaDB/PostgreSQL |
| Licença | LGPLv3 | GPL-3.0 |
| Módulos de suprimentos | `stock` (Inventory), `purchase`, `product`, `uom`, `purchase_requisition` | Stock, Buying, (Manufacturing), Order Management |
| Modelo de estoque | `stock.move` / move lines → `stock.quant` (on-hand); `stock.location`; `stock.picking` | *Stock Ledger Entry* → *Bin*; *Warehouse*; *Stock Entry*, *Purchase Receipt* |
| Reposição | Reordering rules (`orderpoint`), Routes & Rules (push/pull) | *Reorder level* em *Item*, *Material Request* |
| Valoração | FIFO / AVCO / Standard + `stock.valuation.layer`; landed costs | FIFO / Moving Average; *Landed Cost Voucher* |
| Rastreabilidade | Lotes/Séries, upstream/downstream | *Batch* / *Serial No* |
| Fornecedor | `res.partner` + `supplierinfo` (pricelist) | *Supplier*, *Supplier Quotation*, *Supplier Scorecard* |

Ambos confirmam o mesmo padrão arquitetural que o Trino adotou; divergem em profundidade de recursos fiscais/contábeis (fora do MVP do Trino por decisão — MMS-001 §8.3).

---

## 3. Matriz conceito → cobertura no Trino

Legenda de status: **✅ MVP** (já no escopo v1) · **🗓️ Roadmap** (previsto v1.1/v2.0) · **🟠 Parcial/verificar** (conceito existe, mas um aspecto específico não é explícito) · **🔴 Lacuna** (não previsto) · **⚪ Fora de escopo por decisão** (deliberadamente excluído com justificativa registrada).

| # | Conceito de ERP (Odoo/ERPNext) | Onde no Trino | Status |
|---|--------------------------------|---------------|--------|
| 1 | Saldo derivado de razão de movimentos (ledger → on-hand) | MMS-004 (MMS-P-08), ADR-009 | ✅ MVP |
| 2 | Documentos de movimentação (entrada/saída/transferência/ajuste) | MMS-004 (6 documentos) | ✅ MVP |
| 3 | Reserva de saldo com validade | MMS-004 (reserva + expiração) | ✅ MVP |
| 4 | Hierarquia de locais / armazém → depósito → endereço | MMS-004 (endereçamento parametrizável) | ✅ MVP |
| 5 | Inventário / contagem cíclica | MMS-004 (contagem + divergência + ajuste) | ✅ MVP |
| 6 | Curva ABC | MMS-002/004 | 🗓️ Roadmap 1.1 |
| 7 | Ponto de pedido / mín-máx (reordering rules) | MMS-002 (parâmetros de reposição) | ✅ MVP (cálculo de alerta); reposição automática 🗓️ v2.0 |
| 8 | Unidade de medida (catálogo de UoM) | MMS-002 via Master Data FD-001-09 | ✅ MVP |
| 9 | **Conversão de UoM** (comprar em CX, estocar em UN) | MMS-002 (UoM única por item) → **ADR-013 (Accepted)** | ✅ Decidido (revisão de módulo pendente) |
| 10 | Lote / Validade / Número de série | MMS-002/004 | 🗓️ Roadmap 2.0 |
| 11 | Estratégia de retirada (FEFO/FIFO) e putaway | MMS-004 (endereçamento) | 🟠 Verificar (pareado com #10) |
| 12 | Inspeção de qualidade / quarentena no recebimento | MMS-005 | 🗓️ Roadmap 2.0 |
| 13 | Recebimento contra documento + divergências (falta/excesso/avaria) | MMS-005 (MMS-RG-06/07) | ✅ MVP |
| 14 | Transferência entre depósitos (em trânsito) | MMS-004 | ✅ MVP (em trânsito 🗓️ v1.1) |
| 15 | Segregação de estoque por cliente/contrato (3PL) | MMS-004 (MMS-RG-10) | ✅ MVP — **Trino à frente** (ver §5) |
| 16 | Valoração de estoque (FIFO/AVCO/standard) | MMS-001 §8.3 (custo médio gerencial) | ⚪ Fora de escopo MVP (retorna com integração fiscal) |
| 17 | **Landed costs** (frete/seguro no custo) | — | 🔴 Lacuna (relevante quando #16 entrar) |
| 18 | Requisição de compra / RFQ / Pedido | PR-001 + roadmap RFQ/PO (MMS-001 §8.5) | ✅ MVP (PR-001); RFQ/PO 🗓️ Roadmap |
| 19 | Rastreabilidade demanda↔suprimento (procurement group) | MMS-003 ↔ PR-001 (rastreab. bidirecional) | ✅ MVP |
| 20 | **Motor de rotas/regras de suprimento (push/pull configurável)** | Ponto de pedido → PR-001 → **ADR-014 (Accepted)** | ✅ Decidido (sugestão v1.1, automático v2.0) |
| 21 | Cadastro de fornecedor + pricelist + scorecard | Procurement / Supplier Mgmt | 🗓️ Roadmap (domínio BC-SUP) |
| 22 | Acordos-quadro / blanket orders | Contract Management | 🗓️ Roadmap (domínio BC-CTR) |
| 23 | Operação por código de barras / RFID | MMS-004 (scanner-friendly previsto) | 🗓️ Roadmap / futuro |
| 24 | Multimoeda / multi-idioma | MMS-001 §2 | 🗓️ Futuro (estrutural, preparado) |
| 25 | Imutabilidade de movimento + estorno (nunca editar) | MMS-004 (restrição arquitetural 3) | ✅ MVP — **Trino explícito** (ver §5) |

**Balanço:** dos 25 conceitos, a maioria já está **coberta (MVP ou roadmap)**. Apenas **#17 (landed costs)** é lacuna limpa; **#9, #11 e #20** são "parcial/verificar"; **#16** é exclusão deliberada e coerente.

---

## 4. Candidatos a refinamento (priorizados)

Recomendações para avaliar — **nenhuma exige mudar a arquitetura**; são refinamentos de módulo (e uma ADR onde estrutural).

| Prio | Candidato | Por quê | Onde entraria |
|------|-----------|---------|----------------|
| P1 | **Conversão de UoM** (compra × estoque) (#9) — **→ ADR-013 (Accepted)** | Comprar em caixa e controlar em unidade é regra comum; sem conversão, gera erro de saldo e de compra. Baixo custo se tratado no Item Catalog. | **Decidido em ADR-013**; revisão **MMS-002** (alvo 1.2.0) + MMS-004/005/PR-001 pendente |
| P1 | **Motor de regras de reposição** (#20) — **→ ADR-014 (Accepted)** | Trino já tem ponto de pedido → PR-001; formalizar como *regra configurável* (quando repor, de onde: estoque/transferência/compra) dá clareza e prepara automação da v2.0. | **ADR-014 aceita**; sugestão revisável na v1.1 e execução automática na v2.0 do MMS-004 |
| P2 | **Estratégia de retirada FEFO/FIFO + putaway** (#11) | Necessário quando lote/validade (#10) entrar na v2.0; decidir cedo evita retrabalho de endereçamento. | Roadmap **MMS-004 v2.0** (pareado com lote/validade) |
| P2 | **Landed costs** (#17) | Única lacuna limpa; só faz sentido junto da valoração fiscal (#16), hoje fora do MVP. Registrar para não ser esquecido. | Roadmap fiscal (**decisão aberta** + ADR quando valoração retornar) |
| P3 | **Supplier scorecard / homologação estruturada** (#21) | Odoo/ERPNext mostram valor de avaliação de fornecedor; alinhar o roadmap do BC-SUP a esse padrão. | Roadmap **Supplier Management** |

---

## 5. Onde o Trino já está à frente (não regredir)

O benchmark também mostra decisões do Trino **mais fortes** que o comportamento padrão dos dois ERPs — devem ser preservadas:

1. **Segregação de estoque por cliente/contrato (3PL)** como regra de primeira classe (MMS-RG-10). Em Odoo/ERPNext isso exige configuração adicional (localizações/owner). No Trino é nativo.
2. **Imutabilidade do documento de movimentação + correção só por estorno** (MMS-004, restrição 3). Disciplina de auditoria explícita desde o MVP.
3. **Parametrização acima de customização** (MMS-P-06 / FD-001-10): comportamento de cliente é configuração, nunca fork — coerente com o modelo de apps dos ERPs, porém sem a proliferação de módulos custom.
4. **Deny by default + SoD** em toda a suíte (MMS §23), alinhado a SEC-001, mais rígido que o default de um ERP genérico.
5. **Fronteiras de domínio explícitas** (MMS não compra, não gerencia fornecedor, não reimplementa Foundation) — os ERPs monolíticos misturam esses domínios; o Trino os isola por Bounded Context (ARC-002).

---

## 6. Recomendação final

- **Não** adotar Odoo/ERPNext como base de código ou de arquitetura (stack e licença incompatíveis).
- **Usar** este benchmark como validação: o modelo de estoque por razão de movimentos está correto e alinhado ao mercado.
- **Encaminhar** os candidatos P1 (conversão de UoM; motor de regras de reposição) para revisão dos módulos **MMS-002/MMS-004**, com ADR onde houver mudança estrutural.
- **Registrar** landed costs/valoração como dependência do roadmap fiscal (decisão aberta em GOV-002).

---

## 7. Rastreabilidade

| Este documento | Relaciona-se a |
|----------------|----------------|
| Modelo de saldo por movimento (validação) | ADR-009, MMS-004 (MMS-P-08) |
| Eventos de negócio | ADR-010 |
| Fronteiras de contexto | ARC-002, MMS-001 §3 |
| UoM / Master Data | MMS-002, FD-001-09 |
| Reposição / rota de compra | MMS-002/003/004, PR-001 |
| Valoração fiscal (fora de escopo MVP) | MMS-001 §8.3, GOV-002 §10 |
