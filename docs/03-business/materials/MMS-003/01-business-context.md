# MMS-003-01 — Business Context

**Documento:** MMS-003-01 — Business Context
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 (Visão v1.1.0), MMS-001 (§8.2, §9), MMS-002, MMS-004, PR-001
**Referências:** MMS-002-01 / PR-001-01 (padrão), GOV-001

---

# 1. Contexto

Toda operação precisa de material — e, sem um canal formal, o pedido acontece por bilhete, e-mail ou conversa, sem rastro, sem controle de gasto e sem saber se o item já existe no estoque. O **Material Requisition** transforma essa necessidade informal em uma **solicitação rastreável**, aprovada por workflow e atendida pelo caminho mais barato (**estoque primeiro, compra só do que falta**).

# 2. Problema

| Dor | Impacto |
|-----|---------|
| Pedido informal sem rastro | Não se sabe quem pediu, quando, por quê; sem auditoria |
| Compra do que já existe em estoque | Capital parado, compras desnecessárias |
| Gasto sem controle prévio | Demanda não passa por aprovação com contexto |
| Solicitante sem visibilidade | Não sabe se o material vem do estoque ou da compra, nem quando |

# 3. Objetivos

1. Formalizar e rastrear 100% da demanda de material (MMS-P-01).
2. Atender pelo estoque sempre que possível (KPI da suíte — MMS-001 §20).
3. Controlar a demanda antes do gasto (workflow com contexto decisório).
4. Dar previsibilidade ao solicitante (status consolidado das duas rotas).

# 4. Stakeholders e Personas

| Persona | Objetivo | Necessidade-chave |
|---------|----------|-------------------|
| Solicitante (operação/manutenção) | Obter o material no prazo | Fluxo simples, status visível |
| Aprovador (gestor de área) | Controlar demanda e custo | Contexto decisório (justificativa, centro de custo, saldo) |
| Almoxarife | Atender com precisão | Fila priorizada, informações e anexos |
| Gerente de Suprimentos | Otimizar atendimento | KPIs, parametrização |
| Auditor | Verificar conformidade | Trilhas e timeline |

# 5. Premissas
- O usuário solicita uma **necessidade**, nunca um fornecedor (princípio PR-001-12).
- Validação de estoque é **após** a aprovação (MMS-001 §9, regra 2).
- O módulo **não movimenta saldo** nem compra — dispara MMS-004 e PR-001.

# 6. Restrições
- Multiempresa, escopo organizacional obrigatório.
- Comportamentos variáveis são parâmetros (FD-001-10), nunca código.
- Aprovação só via FD-001-04; notificação só via FD-001-05.

# 7. Gatilhos
Necessidade operacional (consumo, troca programada, item danificado/perdido/roubado — motivo estruturado FD-001-09), reposição de EPI/fardamento por roteiro mensal, demanda de projeto/manutenção (futuro).

# 8. Entradas e Saídas
**Entradas:** itens (MMS-002), quantidades, tamanho (grade), justificativa, motivo, anexos, centro de custo, local de entrega, data necessária. **Saídas:** solicitação roteada (estoque/compra), demanda de compra (PR-001), confirmação de entrega, eventos, timeline/auditoria.

# 9. Indicadores / KPIs
Taxa de atendimento pelo estoque, tempo de atendimento, ciclo de aprovação, taxa de cancelamento/rejeição, consumo por colaborador/centro de custo, produtos mais solicitados (MMS-003 README §KPIs).

# 10. Riscos
| Risco | Mitigação |
|-------|-----------|
| Solicitação sem contexto (aprovação "no escuro") | Justificativa + motivo estruturado + saldo consultável |
| Item duplicado/inexistente | Só itens Ativos do catálogo (MR-BR-010) |
| Demanda de compra "órfã" | Rastreabilidade bidirecional obrigatória (MR-BR-043) |
| Reserva cativa | Validade de reserva (MMS-004, IV-BR-021) |

# 11. Glossário
Solicitação de Material; Rota mista; Aprovação parcial; Compra dedicada; Local de entrega; Visão do almoxarifado; Motivo estruturado — conforme MMS-001 §25 e MMS-003 README.

# 12. Dependências
MMS-002 (itens), MMS-004 (estoque), PR-001 (compra), MMS-005 (recebimento), FD-001-01/02/03/04/05/06/07/09/10.

# 13. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Business Context do Material Requisition: contexto, problema, objetivos, stakeholders/personas, premissas, restrições, gatilhos, entradas/saídas, KPIs, riscos, glossário e dependências — padrão MMS-002-01, derivado da visão (MMS-003 v1.1.0) e do fluxo corporativo (MMS-001 §9). |
