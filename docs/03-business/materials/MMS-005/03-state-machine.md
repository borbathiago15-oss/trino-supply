**Documento:** MMS-005-03 — State Machine
**Módulo:** MMS-005 — Receiving (Recebimento)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02 (Business Rules), MMS-001 (§15.3), MMS-004, FD-001-04/06/07
**Referências:** MMS-004-03 / MMS-003-03 (padrão), GOV-001

# 1. Objetivo
Definir os estados do documento de **Recebimento**, transições, guards e eventos. O saldo não tem estado (é projeção no MMS-004); a entrada é side effect da conclusão.

# 2. Estados Oficiais
| Código | Estado | Final |
|--------|--------|-------|
| ST-RC-001 | Aguardando | Não |
| ST-RC-002 | Em Conferência | Não |
| ST-RC-003 | Concluído | Sim |
| ST-RC-004 | Concluído com Divergência | Sim |
| ST-RC-005 | Cancelado | Sim |

# 3. Especificação
- **ST-RC-001 Aguardando:** criado a partir do documento de origem (RC-BR-001); aguarda chegada física. Aceita: iniciar conferência, cancelar.
- **ST-RC-002 Em Conferência:** registra quantidades recebidas (conversão de UoM — RC-BR-010) e divergências (RC-BR-011). Aceita: concluir, cancelar.
- **ST-RC-003 Concluído:** conferência sem divergência (ou dentro da tolerância); gera **entrada no MMS-004** (RC-BR-020); compra dedicada entra reservada (RC-BR-021).
- **ST-RC-004 Concluído com Divergência:** divergências acima da tolerância registradas com destino (e aprovação quando parametrizada — RC-BR-040); gera entrada da quantidade aceita.
- **ST-RC-005 Cancelado:** cancelamento de recebimento Aguardando/Em Conferência sem entrada gerada.

# 4. Matriz de Transições
| Origem | Destino | Permitido | Guard | Evento |
|--------|---------|-----------|-------|--------|
| Aguardando | Em Conferência | Sim | material chegou; responsável | Em conferência |
| Aguardando | Cancelado | Sim | zero conferência | Recebimento cancelado |
| Em Conferência | Concluído | Sim | sem divergência ou dentro da tolerância (RC-BR-012) | Recebimento concluído + Entrada gerada |
| Em Conferência | Concluído com Divergência | Sim | divergência tratada/destino (e aprovada se exigido) | Concluído c/ divergência + Entrada gerada |
| Em Conferência | Cancelado | Sim | antes de gerar entrada | Recebimento cancelado |
| Concluído* / Cancelado | qualquer | Não | terminal | — |

# 5. Eventos de Domínio
Recebimento registrado (Aguardando) · Em conferência · Recebimento concluído · Concluído com divergência · Divergência registrada · Entrada no estoque gerada · Atendimento retomado · Recebimento cancelado. (Especificação técnica em MMS-005-05.)

# 6. Restrições
Nenhuma transição sem auditoria (RC-BR-050); documento concluído é imutável (correção via estorno da entrada no MMS-004); nenhuma edição direta de estado; nenhuma entrada sem conferência concluída.

# 7. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | State Machine do Receiving: 5 estados ST-RC-001..005 (Aguardando, Em Conferência, Concluído, Concluído com Divergência, Cancelado) com guards, side effects (entrada no MMS-004, compra dedicada reservada), matriz de transições, eventos e restrições — padrão MMS-004-03/MMS-003-03. |
