# MMS-003-09 — Permissions & Authorization

**Documento:** MMS-003-09 — Permissions
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 v1.1.0, MMS-003-02 (Business Rules), MMS-003-03 (State Machine), MMS-001 (§23), FD-001-01 (IAM), SEC-001
**Referências:** MMS-004-09 / PR-001-09 (padrão), MMS-003-13 (API), GOV-001

> Modelo híbrido **RBAC + ABAC + Escopo**, deny by default, avaliação **server-side** em 100% das requisições (SEC-001). Alinhado à matriz geral da suíte (MMS-001 §23).

# 1. Papéis

| Papel | Descrição |
|-------|-----------|
| Requester (Solicitante) | Cria e acompanha as **próprias** solicitações; confirma recebimento |
| Approver (Aprovador) | Decide solicitações do seu escopo (aprova/rejeita/retorna, aprovação parcial) |
| Warehouse (Almoxarife/Supervisor) | Opera a visão do almoxarifado (fila de aprovadas); atende via MMS-004 |
| Supply Manager (Gerente) | KPIs, parametrização, cancelamento gerencial |
| Administrator | Locais de entrega, configuração, exceções |
| Auditor | Somente leitura + trilha |

# 2. Permissões (MR-PERM)

| Código | Permissão | Scope |
|--------|-----------|-------|
| MR-PERM-001 | Criar/editar/submeter/cancelar/confirmar as próprias solicitações | `requisitions.write` |
| MR-PERM-002 | Aprovar/rejeitar/retornar (aprovação parcial) | `requisitions.approve` |
| MR-PERM-003 | Acessar a visão do almoxarifado (fila de aprovadas + anexos) | `requisitions.warehouse` |
| MR-PERM-004 | Cancelamento gerencial de solicitações de terceiros | `requisitions.write` (ABAC gestão) |
| MR-PERM-005 | Gerenciar locais de entrega | `requisitions.admin` |
| MR-PERM-006 | Consultar solicitações (leitura ampla no escopo) | `requisitions.read` |
| MR-PERM-007 | Auditoria/trilha | `requisitions.audit` |

# 3. Matriz Papel × Permissão

| Permissão | Requester | Approver | Warehouse | Supply Mgr | Admin | Auditor |
|-----------|:--------:|:--------:|:---------:|:----------:|:-----:|:-------:|
| MR-PERM-001 (próprias) | ✔ | | | ✔ | ✔ | |
| MR-PERM-002 (aprovar) | | ✔ | | ✔ | | |
| MR-PERM-003 (visão almox.) | ✖ (negação explícita) | | ✔ | ✔ | ✔ | |
| MR-PERM-004 (cancelar terceiros) | | | | ✔ | ✔ | |
| MR-PERM-005 (locais) | | | | ✔ | ✔ | |
| MR-PERM-006 (consulta) | ✔ (próprias) | ✔ | ✔ | ✔ | ✔ | ✔ |
| MR-PERM-007 (auditoria) | | | | ✔ | ✔ | ✔ |

> **Negação explícita:** o Requester **não** acessa a visão do almoxarifado (MR-BR-070), mesmo que também tenha outro papel — alinhado ao padrão do MMS-004-09 (Requester com deny da visão do almoxarifado).

# 4. Restrições ABAC (por atributo/estado)

| Código | Regra |
|--------|-------|
| ABAC-MR-01 | Requester só lê/edita solicitações onde `requesterId = usuário` (titularidade) |
| ABAC-MR-02 | Approver só decide dentro do seu escopo organizacional e apenas em estado Em Aprovação |
| ABAC-MR-03 | Edição de itens só em Rascunho (ou via retorno/ajuste do aprovador) — matriz de editabilidade (MMS-003-03 §8) |
| ABAC-MR-04 | **SoD:** aprovador ≠ solicitante (MR-BR-032) — não desligável |
| ABAC-MR-05 | Cancelamento só nos estados permitidos (MR-BR-022) e por titular ou gestão (MR-PERM-004) |
| ABAC-MR-06 | Escopo organizacional obrigatório em toda operação (MR-BR-091 / MMS-001 §23) |

# 5. Policies de Autorização

`POL-MR-AUTH-001` deny by default · `-002` escopo → RBAC → ABAC → delegação · `-003` recurso fora do escopo responde **404** (anti-enumeração) · `-004` toda negação auditada (FD-001-06) · `-005` SoD inviolável (ABAC-MR-04) · `-006` frontend apenas reflete; autoridade é server-side.

# 6. JWT Claims (referência)
`sub`, `company_id`, `org_scope[]`, `roles[]`, `scopes[]` (`requisitions.*`); avaliação por requisição; cache de decisão ≤ 60 s com invalidação em mudança de papel/escopo (SEC-001 §4).

# 7. Segregação de Funções (SoD)

| Código | Descrição | Desligável? |
|--------|-----------|-------------|
| SOD-MR-001 | Solicitante não aprova a própria solicitação | Não |
| SOD-MR-002 | Quem opera o almoxarifado não aprova a demanda que atenderá (separação de aprovação × atendimento) | Configurável por empresa |

# 8. Delegação
Aprovação delegável via Workflow (FD-001-04), preservando SoD (o delegado não pode ser o solicitante). Delegações são auditadas e temporais.

# 9. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Modelo RBAC+ABAC+Escopo do Material Requisition: 6 papéis, 7 permissões MR-PERM, matriz papel×permissão (com negação explícita da visão do almoxarifado ao Requester), 6 restrições ABAC, policies POL-MR-AUTH, JWT claims, SoD (SOD-MR-001 não desligável) e delegação — padrão MMS-004-09/PR-001-09. |
