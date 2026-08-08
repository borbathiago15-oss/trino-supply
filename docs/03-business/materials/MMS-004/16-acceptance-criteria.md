# MMS-004-16 — Acceptance Criteria

**Documento:** MMS-004-16 — Acceptance Criteria
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004-02 (Business Rules v1.1.0), MMS-004-07 (Use Cases v1.1.0), MMS-004-11 (Database), MMS-004-13 (API)
**Referências:** MMS-002-16 / MMS-003-16 (padrão), GOV-001

> Critérios Given/When/Then, prioridade P0/P1/P2, rastreados a IV-BR/UC.

# 1. Integridade de Saldo
| ID | Prio | Critério |
|----|------|----------|
| AC-IV-001 | P0 | **Dado** qualquer caminho (API/job/banco) **Quando** tento alterar saldo diretamente **Então** é bloqueado (IV-BR-001; prova de INV-IV-01) |
| AC-IV-002 | P0 | **Dado** saída > disponível **Quando** confirmo **Então** IV-ERR-004 (saldo nunca negativo — IV-BR-004) |
| AC-IV-003 | P0 | **Dado** documento confirmado **Quando** tento editar **Então** IV-ERR-003; correção só por estorno (IV-BR-003) |
| AC-IV-004 | P0 | **Dado** validação/reserva **Então** usa apenas o disponível (IV-BR-005) |

# 2. Conversão de UoM (ADR-013)
| ID | Prio | Critério |
|----|------|----------|
| AC-IV-010 | P0 | **Dado** entrada capturada em caixa **Quando** confirmo **Então** o saldo é afetado na base e o documento registra o fator aplicado (IV-BR-009) |
| AC-IV-011 | P1 | **Dado** fator alterado no item **Então** movimentos históricos não são reprocessados (IC-BR-093) |

# 3. Reservas
| ID | Prio | Critério |
|----|------|----------|
| AC-IV-020 | P0 | **Dado** reserva **Quando** criada **Então** transfere disponível→reservado (IV-BR-020) |
| AC-IV-021 | P1 | **Dado** reserva vencida **Quando** o job roda **Então** emite documento de liberação (IV-BR-021), nunca escrita direta |
| AC-IV-022 | P0 | **Dado** entrega **Quando** baixa a reserva **Então** reservado e total diminuem atomicamente (IV-BR-023) |

# 4. Ajustes e Inventário
| ID | Prio | Critério |
|----|------|----------|
| AC-IV-030 | P0 | **Dado** ajuste **Quando** o registrante tenta aprovar **Então** bloqueado (SoD — IV-BR-041) |
| AC-IV-031 | P1 | **Dado** contagem **Então** não altera saldo; divergência vs snapshot (IV-BR-071/073) |
| AC-IV-032 | P1 | **Dado** divergência acima da tolerância **Então** gera ajuste vinculado ao inventário (IV-BR-072) |

# 5. Segregação e Locais
| ID | Prio | Critério |
|----|------|----------|
| AC-IV-040 | P0 | **Dado** estoque dedicado **Quando** outro cliente/contrato demanda **Então** IV-ERR-060 (IV-BR-060) |
| AC-IV-041 | P1 | **Dado** local com saldo **Quando** inativo **Então** IV-ERR-052 |
| AC-IV-042 | P1 | **Dado** transferência **Então** preserva segregação (IV-BR-031) |

# 6. Reposição (ADR-014)
| ID | Prio | Critério |
|----|------|----------|
| AC-IV-050 | P1 | **Dado** disponível ≤ ponto de pedido **Então** gera sugestão de reposição (IV-BR-130) |
| AC-IV-051 | P1 | **Dado** sugestão confirmada rota compra **Então** gera demanda PR-001 com origem (IV-BR-131) |
| AC-IV-052 | P0 | **Dado** sugestão **Então** nunca altera saldo nem compra por conta própria |

# 7. Transversais
| ID | Prio | Critério |
|----|------|----------|
| AC-IV-060 | P0 | **Dado** recurso de outra empresa **Então** 404 (IV-BR-006) |
| AC-IV-061 | P0 | **Dado** confirmação **Então** auditoria com saldo anterior/posterior (IV-BR-090) |
| AC-IV-062 | P1 | **Dado** consulta/validação de saldo **Então** p95 < 2 s (IV-BR-110) |
| AC-IV-063 | P1 | **Dado** confirmação concorrente na mesma chave **Então** serializada (IV-BR-012) |

# 8. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | ~25 critérios AC-IV (integridade de saldo, conversão de UoM, reservas, ajustes/inventário, segregação/locais, reposição, transversais NFR/segurança) em Given/When/Then com prioridades e rastreabilidade a IV-BR/UC — padrão MMS-002-16/MMS-003-16. |
