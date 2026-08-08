# MMS-005-12 — Business Journey

**Documento:** MMS-005-12 — Business Journey
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-07, MMS-001 (§10)
**Referências:** MMS-004-12 / MMS-003-12 (padrão), GOV-001

# 1. Jornada Macro
`Chegada do material → Conferência → Tratamento de divergência → Entrada no estoque → (atendimento retomado)`

# 2. Jornada do Almoxarife
| Etapa | Ação | Dor evitada | Momento de verdade | KPI |
|-------|------|-------------|--------------------|-----|
| Registrar | Abre recebimento contra documento | "material apareceu" | nada sem documento (RC-BR-001) | recebimentos pendentes |
| Conferir | Confere em unidade de compra | "entrei errado" | conversão automática (ADR-013) | acuracidade de entrada |
| Divergir | Registra falta/excesso/avaria + destino | "diferença sumiu" | destino documentado (RC-BR-011) | divergência de recebimento |
| Concluir | Gera entrada no estoque | "estoque não bate" | entrada única no MMS-004 | tempo de conferência |

# 3. Jornada do Supervisor
Aprova destino de divergência com contexto; monitora fila de pendentes.

# 4. Jornada do Comprador
Acompanha recebimento dos pedidos; divergências alimentam a avaliação do fornecedor (KPI de divergência, futuro scorecard).

# 5. Jornada do Auditor
Trilha de conferência, divergências e entrada; rastreabilidade pedido→recebimento→entrada.

# 6. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Business Journey do Receiving: jornada macro e jornadas do almoxarife, supervisor, comprador e auditor, com etapas, dores, momentos de verdade (conversão de UoM, destino documentado, entrada única) e KPIs — padrão MMS-004-12. |
