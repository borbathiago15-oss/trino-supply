# MMS-003-12 — Business Journey

**Documento:** MMS-003-12 — Business Journey
**Módulo:** MMS-003 — Material Requisition
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 v1.1.0, MMS-003-07 (Use Cases), MMS-001 (§10)
**Referências:** MMS-002-12 / PR-001-12 (padrão), GOV-001

> Jornadas por papel, com etapas, pontos de dor, momentos de verdade e KPIs.

# 1. Jornada Macro
`Necessidade → Solicitar → Aprovar → Validar estoque → Atender (estoque) / Comprar → Entregar → Confirmar → Concluir`

# 2. Jornada do Solicitante
| Etapa | Ação | Ponto de dor evitado | Momento de verdade | KPI |
|-------|------|----------------------|--------------------|-----|
| Solicitar | Busca item, informa qtd/tamanho/motivo/anexo | "não sei o código certo" | busca por sinônimo (MMS-002) | tempo de criação |
| Acompanhar | Vê status consolidado das 2 rotas | "não sei se vem do estoque ou compra" | status unificado (MR-BR-051) | previsibilidade |
| Receber | Notificado da disponibilidade | "não sei quando chega" | MR-NOT-004 | tempo de atendimento |
| Confirmar | Confirma recebimento | pendência esquecida | conclusão (MR-BR-052) | taxa de conclusão |

# 3. Jornada do Aprovador
| Etapa | Ação | Necessidade | KPI |
|-------|------|-------------|-----|
| Decidir | Revisa contexto (justificativa, CC, saldo) | decisão informada | ciclo de aprovação |
| Ajustar | Aprova parcial / rejeita item | controle fino | taxa de ajuste |

# 4. Jornada do Almoxarife
| Etapa | Ação | Necessidade | KPI |
|-------|------|-------------|-----|
| Priorizar | Fila filtrável (visão do almoxarifado) | ver o que atender | fila/backlog |
| Atender | Reserva/separação/entrega (via MMS-004) | registro rápido | tempo de atendimento |

# 5. Jornada do Auditor
Consulta timeline e trilha; verifica rastreabilidade bidirecional solicitação↔compra e decisões de aprovação.

# 6. Pontos de Dor Resolvidos (síntese)
Pedido informal, compra do que já existe, falta de visibilidade, gasto sem controle, demanda órfã — todos endereçados por regras MR-BR e pelo fluxo (MMS-001 §9).

# 7. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Business Journey do Material Requisition: jornada macro e jornadas do solicitante, aprovador, almoxarife e auditor, com etapas, pontos de dor, momentos de verdade e KPIs por etapa — padrão MMS-002-12. |
