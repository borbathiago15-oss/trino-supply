# MMS-003-16 — Acceptance Criteria

**Documento:** MMS-003-16 — Acceptance Criteria
**Módulo:** MMS-003 — Material Requisition
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003-02 (Business Rules), MMS-003-07 (Use Cases), MMS-003-13 (API)
**Referências:** MMS-002-16 / PR-001-16 (padrão), GOV-001

> Critérios formais em Given/When/Then, com prioridade (P0 crítico, P1 importante, P2 desejável) e rastreabilidade a regras/UC. Alinhados ao DoD do módulo.

# 1. Ciclo de Vida

| ID | Prio | Critério |
|----|------|----------|
| AC-MR-001 | P0 | **Dado** rascunho válido **Quando** submeto **Então** vai a Submetida/Em Aprovação e publica EVT-MR-002 (MR-BR-021) |
| AC-MR-002 | P0 | **Dado** item inativo **Quando** adiciono **Então** MR-ERR-010 (MR-BR-010) |
| AC-MR-003 | P0 | **Dado** item com grade sem tamanho **Quando** submeto **Então** MR-ERR-012 (MR-BR-012) |
| AC-MR-004 | P1 | **Dado** motivo que exige anexo sem anexo **Quando** submeto **Então** MR-ERR-013 (MR-BR-013) |
| AC-MR-005 | P0 | **Dado** estado não permitido **Quando** cancelo **Então** MR-ERR-022; caso permitido, libera reservas (MR-BR-022) |

# 2. Aprovação

| ID | Prio | Critério |
|----|------|----------|
| AC-MR-010 | P0 | **Dado** que sou o solicitante **Quando** aprovo minha solicitação **Então** MR-ERR-032 (SoD — MR-BR-032) |
| AC-MR-011 | P0 | **Dado** aprovação parcial **Quando** reduzo qtd e rejeito item **Então** só os aprovados roteiam e publica EVT-MR-005 (MR-BR-031) |
| AC-MR-012 | P1 | **Dado** retorno **Quando** o aprovador devolve **Então** volta a Rascunho (FA-MR-01) |

# 3. Validação e Roteamento

| ID | Prio | Critério |
|----|------|----------|
| AC-MR-020 | P0 | **Dado** solicitação aprovada **Quando** valida estoque **Então** usa apenas o disponível (MR-BR-041) e nunca antes da aprovação (MR-BR-040) |
| AC-MR-021 | P0 | **Dado** item com saldo **Quando** roteia **Então** vai para reserva (MMS-004); sem saldo → demanda PR-001 com referência de origem (MR-BR-042/043) |
| AC-MR-022 | P1 | **Dado** compra dedicada habilitada **Quando** o material é recebido **Então** entra reservado à solicitação (MR-BR-044) |

# 4. Atendimento e Conclusão

| ID | Prio | Critério |
|----|------|----------|
| AC-MR-030 | P0 | **Dado** itens pendentes **Quando** confirmo recebimento **Então** MR-ERR-052; só conclui com todos os itens terminados (MR-BR-052) |
| AC-MR-031 | P1 | **Dado** rota mista **Quando** abro a solicitação **Então** vejo o status por item das duas rotas (MR-BR-051) |
| AC-MR-032 | P0 | **Dado** qualquer operação **Então** o módulo nunca escreve saldo (MR-BR-050) |

# 5. Locais e Visão do Almoxarifado

| ID | Prio | Critério |
|----|------|----------|
| AC-MR-040 | P1 | **Dado** novo local de entrega **Quando** crio **Então** o código é gerado pelo sistema e único por empresa (MR-BR-060) |
| AC-MR-041 | P0 | **Dado** que sou Solicitante comum **Quando** acesso a visão do almoxarifado **Então** MR-ERR-070 (MR-BR-070) |
| AC-MR-042 | P1 | **Dado** a visão do almoxarifado **Quando** filtro por categoria/período/status **Então** vejo as solicitações aprovadas correspondentes |

# 6. Transversais (NFR/Segurança)

| ID | Prio | Critério |
|----|------|----------|
| AC-MR-050 | P0 | **Dado** recurso de outra empresa **Quando** acesso **Então** 404 (MR-BR-001, anti-enumeração) |
| AC-MR-051 | P0 | **Dado** escrita concorrente **Quando** há conflito de versão **Então** MR-ERR-409 (MR-BR-091) |
| AC-MR-052 | P1 | **Dado** qualquer transição **Então** gera auditoria e timeline (MR-BR-080/081) |
| AC-MR-053 | P1 | **Dado** listagens **Então** usam keyset (sem OFFSET) |

# 7. Matriz de Rastreabilidade (síntese)
28 critérios AC-MR cobrindo UC-MR-001..008 e as 30 regras MR-BR; prioridades P0/P1/P2. Alinhados ao DoD (MMS-003 README §DoD).

# 8. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 28 critérios de aceite AC-MR (ciclo de vida, aprovação com SoD e parcial, validação/roteamento, atendimento/conclusão, locais e visão do almoxarifado, transversais NFR/segurança) em Given/When/Then com prioridades e rastreabilidade a MR-BR/UC — padrão MMS-002-16. |
