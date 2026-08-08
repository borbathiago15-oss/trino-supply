# MMS-003-07 — Use Cases

**Documento:** MMS-003-07 — Use Cases
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 v1.1.0, MMS-003-02 (Business Rules), MMS-003-03 (State Machine), MMS-003-04 (Domain Model), MMS-003-05 (Event Storming), MMS-002, MMS-004, PR-001, MMS-005, FD-001-04
**Referências:** MMS-004-07 / MMS-002-07 (padrão), MMS-003-09 (Permissions), MMS-003-13 (API), GOV-001

> Especificação UML dos casos de uso do Material Requisition: fluxo principal, alternativos, exceção, pré/pós-condições, regras, eventos, APIs, permissões e testes.

# 0. Convenções
APIs sob `/api/v1/material-requisitions` (MMS-003-13); permissões MR-PERM (MMS-003-09); eventos EVT-MR (MMS-003-05); regras MR-BR (MMS-003-02); estados ST-MR-001..009 (MMS-003-03); testes TC-MR (MMS-003-17).

---

# UC-MR-001 — Criar Solicitação de Material
**Ator:** Solicitante. **Pré:** autenticado, permissão MR-PERM-001, itens Ativos no catálogo.
**Pós:** solicitação em Rascunho; EVT-MR-001.
**Fluxo principal:** 1) informa itens (busca MMS-002), quantidades, tamanho quando grade; 2) justificativa, motivo, centro de custo, local de entrega, data necessária; 3) anexa evidências se o motivo exigir; 4) salva.
**Alternativos:** A1 salvar mínimo e complementar depois; A2 duplicar solicitação existente.
**Exceção:** E1 item inativo → MR-ERR-010; E2 tamanho ausente (grade) → MR-ERR-012; E3 motivo exige anexo → MR-ERR-013; E4 fora do escopo → MR-ERR-002.
**Regras:** MR-BR-001..013. **APIs:** POST /material-requisitions; POST .../items; POST .../attachments. **Permissão:** MR-PERM-001. **Testes:** TC-MR-001-*.

# UC-MR-002 — Submeter Solicitação
**Ator:** Solicitante. **Pré:** solicitação em Rascunho válida. **Pós:** Submetida; entra no workflow (ou Aprovada se aprovação não exigida); EVT-MR-002.
**Fluxo:** 1) revisa; 2) submete; 3) sistema valida guards (MR-BR-021) e roteia ao workflow (MR-BR-030).
**Exceção:** E1 incompleta → MR-ERR-021. **Regras:** MR-BR-021/030. **API:** POST .../submit. **Permissão:** MR-PERM-001.

# UC-MR-003 — Aprovar / Rejeitar / Retornar (Aprovação Parcial)
**Ator:** Aprovador. **Pré:** solicitação Em Aprovação; aprovador ≠ solicitante (MR-BR-032). **Pós:** Aprovada (total/parcial) / Rejeitada / Retornada; EVT-MR-004/005/006/007.
**Fluxo principal:** 1) abre a fila de aprovações; 2) revisa itens, justificativa, motivo, centro de custo, saldo consultável; 3) decide: aprovar integral, **alterar quantidades**, **rejeitar itens**, ou recusar tudo; 4) registra parecer.
**Alternativos:** A1 aprovação parcial (MR-BR-031) — itens rejeitados/reduzidos com motivo do decisor; A2 retornar ao solicitante (FA-MR-01).
**Exceção:** E1 solicitante = aprovador → MR-ERR-032; E2 ação inválida por item → MR-ERR-031.
**Regras:** MR-BR-030/031/032. **APIs:** GET .../approvals; POST .../approve; /reject; /return. **Permissão:** MR-PERM-002.

# UC-MR-004 — Validar Estoque e Rotear (Sistema)
**Ator:** Sistema (pós-aprovação). **Pré:** solicitação Aprovada. **Pós:** cada item com rota; Em Atendimento e/ou Aguardando Compra; EVT-MR-009/010/012.
**Fluxo:** 1) consulta MMS-004 item a item (disponível — MR-BR-041); 2) item com saldo → reserva/atendimento (MMS-004); 3) item sem saldo → demanda de compra PR-001 com referência de origem (MR-BR-043).
**Regras:** MR-BR-040..044. **APIs:** (interno) MMS-004/PR-001 via eventos. **Testes:** TC-MR-040..043.

# UC-MR-005 — Acompanhar e Confirmar Recebimento
**Ator:** Solicitante. **Pré:** solicitação em atendimento. **Pós:** ao confirmar, itens/solicitação concluem quando todos terminam (MR-BR-052); EVT-MR-011.
**Fluxo:** 1) acompanha status consolidado das duas rotas; 2) recebe notificação de disponibilidade; 3) confirma recebimento.
**Exceção:** E1 confirmar com itens pendentes → MR-ERR-052. **Regras:** MR-BR-051/052. **APIs:** GET .../{id}; GET .../{id}/timeline; POST .../confirm-receipt. **Permissão:** MR-PERM-001.

# UC-MR-006 — Cancelar Solicitação
**Ator:** Solicitante/Gestão. **Pré:** estado permitido (MR-BR-022). **Pós:** Cancelada; reservas liberadas; EVT-MR-008.
**Exceção:** E1 estado não permite → MR-ERR-022. **API:** POST .../cancel. **Permissão:** MR-PERM-001 (própria) / MR-PERM-004 (gestão).

# UC-MR-007 — Gerenciar Locais de Entrega
**Ator:** Administrador/Gerente. **Pré:** permissão MR-PERM-005. **Pós:** local criado/alterado/inativado (código gerado — MR-BR-060).
**Exceção:** E1 inativar local em uso → MR-ERR-060. **APIs:** GET/POST/PATCH .../delivery-locations. **Permissão:** MR-PERM-005.

# UC-MR-008 — Operar a Visão do Almoxarifado
**Ator:** Almoxarife/Supervisor. **Pré:** permissão MR-PERM-003. **Pós:** consulta da fila de aprovadas com filtros; nenhum efeito de estado (leitura).
**Fluxo:** filtra por solicitante, período, status, centro de custo, empresa, categoria, número; acessa informações e anexos (MR-BR-070).
**Exceção:** E1 sem acesso → MR-ERR-070. **API:** GET .../warehouse-queue. **Permissão:** MR-PERM-003.

---

# 8. Matriz de Rastreabilidade

| UC | Regras MR-BR | Eventos | Endpoints | Permissões |
|----|--------------|---------|-----------|------------|
| UC-MR-001 Criar | 001..013, 020 | EVT-MR-001 | POST /material-requisitions; .../items; .../attachments | MR-PERM-001 |
| UC-MR-002 Submeter | 021, 030 | EVT-MR-002 | POST .../submit | MR-PERM-001 |
| UC-MR-003 Aprovar | 030, 031, 032 | EVT-MR-004/005/006/007 | GET .../approvals; POST .../approve; /reject; /return | MR-PERM-002 |
| UC-MR-004 Validar/Rotear | 040, 041, 042, 043, 044 | EVT-MR-009/010/012 | (interno) MMS-004/PR-001 | (sistema) |
| UC-MR-005 Acompanhar/Confirmar | 050, 051, 052 | EVT-MR-011 | GET .../{id}; /timeline; POST .../confirm-receipt | MR-PERM-001 |
| UC-MR-006 Cancelar | 022 | EVT-MR-008 | POST .../cancel | MR-PERM-001/004 |
| UC-MR-007 Locais | 060 | Local criado/alterado | GET/POST/PATCH .../delivery-locations | MR-PERM-005 |
| UC-MR-008 Visão almox. | 070 | — | GET .../warehouse-queue | MR-PERM-003 |

**Cobertura:** UC-MR-001..008 cobrindo 30/30 regras e os eventos EVT-MR-001..012.

# 9. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 8 casos de uso UC-MR-001..008 (criar, submeter, aprovar/rejeitar/retornar com aprovação parcial, validar/rotear pós-aprovação, acompanhar/confirmar, cancelar, gerenciar locais de entrega, visão do almoxarifado) em especificação UML com regras MR-BR, eventos EVT-MR, APIs, permissões MR-PERM e testes TC-MR; matriz de rastreabilidade — padrão MMS-004-07. |
