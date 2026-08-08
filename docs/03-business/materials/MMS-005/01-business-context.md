# MMS-005-01 — Business Context

**Documento:** MMS-005-01 — Business Context
**Módulo:** MMS-005 — Receiving (Recebimento)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005 (Visão v1.1.0), MMS-001 (§8.4, §9), MMS-002, MMS-004, PR-001
**Referências:** MMS-002-01 / MMS-003-01 (padrão), GOV-001

# 1. Contexto
O **Receiving** é a porta de entrada física de material na suíte: recebe, confere e dá entrada no estoque de tudo que chega ao almoxarifado — comprado, transferido ou devolvido — sempre contra um documento de origem. É o último elo do fluxo corporativo: sem ele, a rota de compra não fecha e a solicitação não conclui.

# 2. Problema
| Dor | Impacto |
|-----|---------|
| Material entra sem conferência | Estoque inflado, acuracidade destruída |
| Diferenças "somem" | Perda sem responsável; fornecedor sem medição |
| Compra não fecha o ciclo | Solicitação pendente indefinidamente |

# 3. Objetivos
1. Proteger a acuracidade do estoque na entrada (1ª linha do KPI ≥ 98%).
2. Fechar a rota de compra (material recebido retoma a solicitação — MMS-003).
3. Evidenciar qualidade de fornecedores/transporte (KPI de divergência).
4. Eliminar entrada informal (nada entra sem documento — MMS-RG-06).

# 4. Stakeholders / Personas
Almoxarife (confere e dá entrada), Supervisor (trata divergências), Comprador (acompanha recebimento dos pedidos), Gerente (KPIs), Auditor (trilha).

# 5. Premissas
- Recebimento sempre contra documento de origem (pedido/transferência/devolução) — MMS-RG-06.
- Divergência sempre com destino documentado — MMS-RG-07.
- Saldo só muda no MMS-004 (MMS-P-08); a conferência pode capturar unidade de compra, convertida para a base na entrada (ADR-013).
- Sem inspeção de qualidade certificada no MVP (v2.0).

# 6. Restrições
Multiempresa; escopo organizacional; parametrização (tolerâncias, aprovação de divergência) via FD-001-10; aprovação/notificação só via Foundation.

# 7. Gatilhos
Pedido de compra confirmado (PR-001), transferência expedida (MMS-004), devolução de solicitante (MMS-003).

# 8. Entradas e Saídas
**Entradas:** documento de origem, quantidades esperadas/recebidas (em unidade de compra convertível), divergências e destinos, tolerâncias. **Saídas:** documento de entrada (MMS-004), sinal de atendimento retomado (MMS-003), registros de divergência, eventos, timeline/auditoria.

# 9. KPIs
Divergência de recebimento, lead time de entrega, tempo de conferência, acuracidade de entrada, recebimentos pendentes.

# 10. Riscos
| Risco | Mitigação |
|-------|-----------|
| Entrada de material não conforme | Registro de avaria como divergência; quarentena v2.0 |
| Erro de unidade | Conversão de UoM na entrada (ADR-013), fator registrado |
| Divergência sem tratamento | Aprovação parametrizável + destino documentado |

# 11. Glossário
Recebimento; Conferência; Divergência (falta/excesso/avaria); Compra dedicada; Documento de origem — conforme MMS-001 §25.

# 12. Dependências
MMS-004 (entrada/saldo), MMS-003 (atendimento), MMS-002 (itens/unidades), PR-001 (pedido), FD-001-01/02/04/05/06/07/10.

# 13. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Business Context do Receiving: contexto, problema, objetivos, personas, premissas (conversão de UoM na entrada, saldo só no MMS-004), restrições, gatilhos, entradas/saídas, KPIs, riscos, glossário e dependências — padrão MMS-002-01/MMS-003-01. |
