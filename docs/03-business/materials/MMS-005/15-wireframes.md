# MMS-005-15 — Wireframes

**Documento:** MMS-005-15 — Wireframes
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-14, MMS-005-07
**Referências:** MMS-004-15 / MMS-003-15 (padrão), GOV-001

## WF-RC-01 — Fila de Recebimentos
```
[Filtros: status ▾ | origem ▾ | busca nº]
[Tabela: nº | origem | itens | status(chip) | pendente há | abrir]
```
UC-RC-004.

## WF-RC-02 — Conferência
```
[Cabeçalho: nº, origem, depósito | Iniciar conferência]
[Linhas: item | esperado | recebido [valor|unidade ▾(CX/UN)] → convertido: N base (×F) | 📷 scan]
[+ registrar divergência]
```
Conversão de UoM visível (RC-BR-010). UC-RC-002.

## WF-RC-03 — Divergências
```
[Registrar: linha | tipo ▾(falta/excesso/avaria) | qtd | destino* | motivo]
[Fila de aprovação: destino | aprovar/rejeitar]  (SoD)
```
UC-RC-002.

## WF-RC-04 — Conclusão
```
[Resumo: itens conferidos | divergências tratadas | destino documentado]
[Concluir → gera entrada no estoque]  (idempotente)
```
RC-BR-020. UC-RC-003.

## WF-RC-05 — Timeline/Detalhe
```
[Detalhe: linhas + divergências + entrada gerada (link MMS-004)]
[Timeline: marcos + vínculo pedido (PR-001) / solicitação (MMS-003)]
```
UC-RC-004.

## Matriz
| Wireframe | Tela | UC |
|-----------|------|-----|
| WF-RC-01 | RC-SCR-01 | UC-RC-004 |
| WF-RC-02 | RC-SCR-02 | UC-RC-002 |
| WF-RC-03 | RC-SCR-03 | UC-RC-002 |
| WF-RC-04 | RC-SCR-04 | UC-RC-003 |
| WF-RC-05 | RC-SCR-05 | UC-RC-004 |

# Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 5 wireframes WF-RC-01..05 (fila, conferência com conversão de UoM, divergências, conclusão com entrada, timeline) com zonas, estados e matriz de rastreabilidade — padrão MMS-004-15. |
