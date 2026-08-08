# MMS-005-07 — Use Cases

**Documento:** MMS-005-07 — Use Cases
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02/03/04/05, MMS-004, MMS-003, PR-001, FD-001-04
**Referências:** MMS-004-07 / MMS-003-07 (padrão), MMS-005-09 (Permissions), MMS-005-13 (API), GOV-001

> Convenções: APIs `/api/v1/receivings` (MMS-005-13); permissões RC-PERM (MMS-005-09); eventos EVT-RC (MMS-005-05); regras RC-BR; estados ST-RC-001..005.

# UC-RC-001 — Registrar Recebimento
**Ator:** Almoxarife / Sistema (PR-001). **Pré:** documento de origem válido (RC-BR-001). **Pós:** recebimento em Aguardando; EVT-RC-001.
**Fluxo:** 1) seleciona documento de origem; 2) sistema cria o recebimento com as quantidades esperadas.
**Exceção:** E1 origem inválida → RC-ERR-001. **API:** POST /receivings; POST .../cancel. **Permissão:** RC-PERM-001.

# UC-RC-002 — Conferir e Tratar Divergências
**Ator:** Almoxarife (+ Supervisor p/ aprovação). **Pré:** recebimento Aguardando. **Pós:** Em Conferência; divergências registradas; EVT-RC-002/003/004.
**Fluxo:** 1) inicia conferência; 2) registra quantidade recebida por item (unidade de compra convertida — RC-BR-010); 3) registra falta/excesso/avaria com destino (RC-BR-011); 4) aprova destino quando exigido (RC-BR-040).
**Alternativos:** A1 dentro da tolerância → sem tratativa (RC-BR-012); A2 recebimento parcial (RC-BR-022).
**Exceção:** E1 divergência sem destino → RC-ERR-011; E2 acima da tolerância sem tratativa → RC-ERR-012. **APIs:** POST .../start; .../lines; .../divergences; .../divergences/{id}/approve. **Permissão:** RC-PERM-001/002.

# UC-RC-003 — Concluir e Dar Entrada
**Ator:** Almoxarife. **Pré:** conferência tratada. **Pós:** Concluído / Concluído com Divergência; **entrada gerada no MMS-004** (RC-BR-020); compra dedicada reservada (RC-BR-021); EVT-RC-005/006/007.
**Fluxo:** 1) conclui; 2) sistema gera exatamente um documento de entrada no MMS-004 (idempotente); 3) se compra dedicada, entra reservada e retoma a solicitação (MMS-003).
**Exceção:** E1 estado inválido/entrada já gerada → RC-ERR-020. **API:** POST .../complete (`Idempotency-Key`). **Permissão:** RC-PERM-001.

# UC-RC-004 — Consultar Recebimentos
**Ator:** Almoxarife/Comprador/Gerente/Auditor. **Pré:** permissão RC-PERM-003. **Pós:** leitura (fila, detalhe, timeline).
**Fluxo:** filtra por status/origem; abre detalhe (linhas + divergências) e timeline.
**API:** GET /receivings; /{id}; /{id}/timeline. **Permissão:** RC-PERM-003.

# 8. Matriz de Rastreabilidade
| UC | Regras RC-BR | Eventos | Endpoints | Permissões |
|----|--------------|---------|-----------|------------|
| UC-RC-001 Registrar | 001, 002 | EVT-RC-001 | POST /receivings; /cancel | RC-PERM-001 |
| UC-RC-002 Conferir/Divergência | 010, 011, 012, 022, 040 | EVT-RC-002/003/004 | POST /start; /lines; /divergences; /approve | RC-PERM-001/002 |
| UC-RC-003 Concluir/Entrada | 020, 021, 030 | EVT-RC-005/006/007 | POST /complete | RC-PERM-001 |
| UC-RC-004 Consultar | 050, 060 | — | GET /receivings; /{id}; /timeline | RC-PERM-003 |

**Cobertura:** UC-RC-001..004 cobrindo 12/12 regras e EVT-RC-001..008.

# 9. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | 4 casos de uso UC-RC-001..004 (registrar, conferir/tratar divergências, concluir com entrada no MMS-004, consultar) em especificação UML com regras RC-BR, eventos EVT-RC, APIs, permissões RC-PERM e matriz de rastreabilidade — padrão MMS-004-07. |
