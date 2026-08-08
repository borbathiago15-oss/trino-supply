# MMS-004-15 — Wireframes

**Documento:** MMS-004-15 — Wireframes
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004-14 (UX), MMS-004-07 (Use Cases)
**Referências:** MMS-002-15 / MMS-003-15 (padrão), GOV-001

> Wireframes textuais por tela (IV-SCR), com zonas, estados, variantes responsivas e rastreabilidade.

## WF-IV-01 — Posição de Estoque
```
[Filtros: item ▾ | depósito ▾ | categoria | com reserva ☑]
[Tabela: item | local | total | reservado | disponível | custo méd. | último mov.]
[Ações por linha: extrato | movimentar]
```
Saldo somente leitura (ADR-009). UC-IV-011.

## WF-IV-02 — Registrar Movimentação
```
[Tipo ▾ entrada/saída/transferência]
[Item ▾ | tamanho ▾(grade) | local origem/destino ▾ | segregação]
[Quantidade: valor | unidade ▾ (base/compra)] → [convertido: N na base (fator ×F)]
[Origem: documento ▾]
[Confirmar]  ← idempotente
```
Captura de UoM com conversão visível (ADR-013). UC-IV-001/002/005.

## WF-IV-03 — Reservas
```
[Abas: Ativas | A vencer | Vencidas]
[Tabela: solicitação | item | tamanho | qtd | local | expira em | ações(liberar)]
```
UC-IV-003/004.

## WF-IV-04 — Ajustes
```
[Registrar: item | local | delta(+/-) | motivo ▾ | justificativa | anexo]
[Fila de aprovação: saldo anterior → posterior | aprovar/rejeitar]
```
SoD: registrante ≠ aprovador (IV-BR-041). UC-IV-006.

## WF-IV-05 — Inventário
```
[Abrir: escopo ABC/geral | responsável | prazo]
[Contagem: item | local | contado | (snapshot oculto) ]
[Divergências: item | contado | sistema | delta | tolerância | gerar ajuste]
[Fechar]
```
UC-IV-007.

## WF-IV-06 — Endereçamento
```
[Árvore: Almoxarifado > Depósito > Endereço | ativo | inativar(exige saldo 0)]
```
UC-IV-009.

## WF-IV-07 — Alertas
```
[Lista: tipo(mínimo/ruptura/reserva) | item | local | prioridade | reconhecer]
```
UC-IV-010.

## WF-IV-08 — Sugestões de Reposição
```
[Fila: item | depósito | disponível | ponto pedido | qtd sugerida | rota(compra/transf) | confirmar/descartar]
```
ADR-014. UC-IV-012.

## Matriz de Rastreabilidade
| Wireframe | Tela | UC |
|-----------|------|-----|
| WF-IV-01 | IV-SCR-01 | UC-IV-011 |
| WF-IV-02 | IV-SCR-02 | UC-IV-001/002/005 |
| WF-IV-03 | IV-SCR-03 | UC-IV-003/004 |
| WF-IV-04 | IV-SCR-04 | UC-IV-006 |
| WF-IV-05 | IV-SCR-05 | UC-IV-007 |
| WF-IV-06 | IV-SCR-06 | UC-IV-009 |
| WF-IV-07 | IV-SCR-07 | UC-IV-010 |
| WF-IV-08 | IV-SCR-08 | UC-IV-012 |

# Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 8 wireframes WF-IV-01..08 (posição, movimentação com conversão de UoM, reservas, ajustes, inventário, endereçamento, alertas, sugestões de reposição) com zonas, estados, responsividade e matriz de rastreabilidade — padrão MMS-002-15/MMS-003-15. |
