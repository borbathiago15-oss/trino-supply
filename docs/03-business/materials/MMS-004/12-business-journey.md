# MMS-004-12 — Business Journey

**Documento:** MMS-004-12 — Business Journey
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004 v1.1.0, MMS-004-07 (Use Cases), MMS-001 (§10)
**Referências:** MMS-002-12 / MMS-003-12 (padrão), GOV-001

# 1. Jornada Macro
`Documento de origem → Movimentação → Confirmação → Saldo atualizado → Alertas/KPIs`

# 2. Jornada do Almoxarife
| Etapa | Ação | Dor evitada | Momento de verdade | KPI |
|-------|------|-------------|--------------------|-----|
| Receber/dar entrada | Registra entrada (unidade de compra convertida) | "entrei errado" | conversão automática (ADR-013) | acuracidade de entrada |
| Reservar/separar/entregar | Baixa por documento | "o saldo não bate" | saldo por movimento (MMS-P-08) | tempo de atendimento |
| Contar (inventário) | Registra contagem com snapshot | "divergência sumiu" | ajuste aprovado | divergência por ciclo |

# 3. Jornada do Supervisor
| Etapa | Ação | Necessidade | KPI |
|-------|------|-------------|-----|
| Aprovar ajuste | Decide com saldo anterior/posterior | controle (SoD) | ajustes/mês |
| Gerir endereçamento | Estrutura de locais | organização | ocupação |

# 4. Jornada do Gerente de Suprimentos
| Etapa | Ação | Necessidade | KPI |
|-------|------|-------------|-----|
| Acompanhar posição | Vê valor parado, cobertura, giro | capital visível | giro/cobertura |
| Revisar sugestões de reposição | Confirma/descarta (ADR-014) | reposição no tempo certo | ruptura evitada |

# 5. Jornada do Auditor
Extrato de movimentações e trilha com saldo anterior/posterior; reconstrução da posição a partir do razão de movimentos.

# 6. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Business Journey do Inventory: jornada macro e jornadas do almoxarife, supervisor, gerente e auditor, com etapas, pontos de dor, momentos de verdade (conversão de UoM, saldo por movimento, sugestão de reposição) e KPIs — padrão MMS-002-12. |
