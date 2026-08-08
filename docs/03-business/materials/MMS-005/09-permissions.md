# MMS-005-09 — Permissions & Authorization

**Documento:** MMS-005-09 — Permissions
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02/03, MMS-001 (§23), FD-001-01, SEC-001
**Referências:** MMS-004-09 / MMS-003-09 (padrão), GOV-001

> RBAC + ABAC + Escopo, deny by default, avaliação server-side (SEC-001).

# 1. Papéis
| Papel | Descrição |
|-------|-----------|
| Warehouse (Almoxarife) | Registra, confere, trata divergências, conclui |
| Supervisor | Aprova destino de divergência; gerencia fila |
| Buyer (Comprador) | Acompanha recebimento dos pedidos (leitura) |
| Supply Manager | KPIs, parametrização |
| Administrator | Configuração |
| Auditor | Leitura + trilha |

# 2. Permissões (RC-PERM)
| Código | Permissão | Scope |
|--------|-----------|-------|
| RC-PERM-001 | Registrar/conferir/concluir/cancelar recebimento | `receiving.write` |
| RC-PERM-002 | Aprovar destino de divergência | `receiving.approve` |
| RC-PERM-003 | Consultar recebimentos e timeline | `receiving.read` |
| RC-PERM-004 | Configurar tolerâncias/parâmetros | `receiving.admin` |
| RC-PERM-005 | Auditoria/trilha | `receiving.audit` |

# 3. Matriz Papel × Permissão
| Permissão | Warehouse | Supervisor | Buyer | Supply Mgr | Admin | Auditor |
|-----------|:--------:|:----------:|:-----:|:----------:|:-----:|:-------:|
| RC-PERM-001 | ✔ | ✔ | | | ✔ | |
| RC-PERM-002 | | ✔ | | ✔ | | |
| RC-PERM-003 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| RC-PERM-004 | | | | ✔ | ✔ | |
| RC-PERM-005 | | | | ✔ | ✔ | ✔ |

# 4. Restrições ABAC
- ABAC-RC-01 Operações dentro do escopo do almoxarifado do usuário (FD-001-02).
- ABAC-RC-02 Divergência só tratável em Em Conferência (matriz de estados).
- ABAC-RC-03 **SoD:** quem confere não aprova o próprio destino restrito de divergência (RC-BR-040) — configurável por empresa.
- ABAC-RC-04 Conclusão só com conferência tratada; entrada única (INV-RC-04).

# 5. Policies
`POL-RC-AUTH-001` deny by default · `-002` escopo→RBAC→ABAC→delegação · `-003` fora do escopo → 404 · `-004` negação auditada · `-005` frontend só reflete.

# 6. SoD
| Código | Descrição | Desligável? |
|--------|-----------|-------------|
| SOD-RC-001 | Conferente ≠ aprovador de destino restrito de divergência | Configurável |

# 7. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | RBAC+ABAC+Escopo do Receiving: 6 papéis, 5 permissões RC-PERM, matriz papel×permissão, 4 restrições ABAC, policies POL-RC-AUTH, SoD (SOD-RC-001) — padrão MMS-004-09/MMS-003-09. |
