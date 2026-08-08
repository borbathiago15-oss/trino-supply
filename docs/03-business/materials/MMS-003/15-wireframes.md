# MMS-003-15 — Wireframes

**Documento:** MMS-003-15 — Wireframes
**Módulo:** MMS-003 — Material Requisition
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003-14 (UX), MMS-003-07 (Use Cases)
**Referências:** MMS-002-15 / PR-001-15 (padrão), GOV-001

> Wireframes textuais (baixa fidelidade) por tela, com zonas, variantes responsivas, estados e rastreabilidade. Alinhados a MR-SCR (MMS-003-14).

## WF-MR-01 — Minhas Solicitações (lista)
```
[Cabeçalho: título + botão "Nova Solicitação"]
[Filtros: status ▾ | período | busca nº]
[Tabela: Nº | Data | Itens | Status(chip) | Progresso ▓▓░ | Ações]
[Rodapé: paginação keyset "Carregar mais"]
```
Estados: vazio ("Nenhuma solicitação"), carregando, erro. UC-MR-005.

## WF-MR-02 — Formulário de Solicitação
```
[Seção Itens: busca item ▾ (sinônimos) | qtd | tamanho ▾(se grade) | + adicionar]
[Lista de itens adicionados: item | qtd | tamanho | remover]
[Seção Dados: justificativa* | motivo ▾ | centro de custo ▾ | local de entrega ▾ | data necessária]
[Seção Anexos: arrastar/soltar (obrigatório se motivo exige)]
[Ações: Salvar rascunho | Submeter]
```
Validações em linha (MR-ERR-010/011/012/013). UC-MR-001/002.

## WF-MR-03 — Detalhe Consolidado
```
[Cabeçalho: nº, status, solicitante, data]
[Itens: item | qtd | rota(chip: Estoque/Compra) | status(chip) | ref compra]
[Timeline lateral: marcos + vínculos PR-001]
[Ações: Confirmar recebimento (quando aplicável) | Cancelar]
```
UC-MR-005/006.

## WF-MR-04 — Fila de Aprovações + Decisão
```
[Fila: nº | solicitante | valor est. | criticidade | data]
[Detalhe: itens com [aprovar] [reduzir qtd] [rejeitar+motivo]]
[Parecer: campo texto | Aprovar | Rejeitar | Retornar]
```
SoD: aprovar desabilitado se solicitante = usuário (MR-ERR-032). UC-MR-003.

## WF-MR-05 — Visão do Almoxarifado
```
[Filtros: solicitante | período de/até | status | centro de custo | empresa | categoria | nº]
[Tabela: nº | solicitante | itens | categoria | status | anexos 📎 | abrir]
```
Acesso restrito (MR-BR-070). UC-MR-008.

## WF-MR-06 — Locais de Entrega
```
[Lista: código(gerado) | descrição | unidade | ativo | editar]
[Form: descrição | unidade ▾ | ativo ☑]
```
UC-MR-007.

## Variantes Responsivas
Desktop (tabela completa) → Tablet (cards, scanner-friendly no almoxarifado) → Mobile (leitura/acompanhamento; criação simplificada — roadmap v2.0).

## Matriz de Rastreabilidade
| Wireframe | Tela (UX) | UC |
|-----------|-----------|-----|
| WF-MR-01 | MR-SCR-01 | UC-MR-005 |
| WF-MR-02 | MR-SCR-02 | UC-MR-001/002 |
| WF-MR-03 | MR-SCR-03 | UC-MR-005/006 |
| WF-MR-04 | MR-SCR-04 | UC-MR-003 |
| WF-MR-05 | MR-SCR-05 | UC-MR-008 |
| WF-MR-06 | MR-SCR-06 | UC-MR-007 |

# Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 6 wireframes WF-MR-01..06 (lista, formulário, detalhe consolidado, fila/decisão de aprovação, visão do almoxarifado, locais de entrega) com zonas, estados, variantes responsivas e matriz de rastreabilidade — padrão MMS-002-15. |
