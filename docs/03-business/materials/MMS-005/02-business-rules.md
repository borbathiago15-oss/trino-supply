**Documento:** MMS-005-02 — Business Rules
**Módulo:** MMS-005 — Receiving (Recebimento)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005 (Visão v1.1.0), MMS-001 (§8.4, §9, §14, §15.3), MMS-002, MMS-004, PR-001, FD-001-03/04/10
**Referências:** MMS-004-02 / MMS-003-02 (padrão), GOV-001

# 1. Objetivo
Formalizar as regras de negócio do Receiving. Cada regra: código, nome, descrição, tipo, validação, erro (RC-ERR), evento, UC, API, teste, configuração, observações — padrão MMS-004-02. Prefixo `RC-BR`; API sob `/api/v1/receivings`.

# 2. Classificação
Obrigatória · Parametrizável · Informativa (idem MMS-004-02 §2).

# 3. Regras Gerais

## RC-BR-001 — Recebimento com Documento de Origem
| Campo | Valor |
|-------|-------|
| Descrição | Todo recebimento referencia um documento de origem: Pedido de Compra (PR-001), transferência (MMS-004) ou devolução (MMS-003) — nenhuma entrada informal (MMS-RG-06). |
| Tipo | Obrigatória |
| Validação | Tipo de origem ∈ {pedido, transferência, devolução}; referência existente e verificável. |
| Erro | RC-ERR-001 |
| Evento | Recebimento registrado |
| UC/API/Teste | UC-RC-001 / POST /receivings / TC-RC-001 |
| Config | Não configurável. |

## RC-BR-002 — Empresa Obrigatória e Isolada
| Campo | Valor |
|-------|-------|
| Descrição | Recebimento pertence a uma empresa; isolamento por `company_id`; fora do escopo → 404. |
| Tipo | Obrigatória |
| Erro | RC-ERR-404 |
| UC/API/Teste | Todos / Todos / TC-RC-002 |

## RC-BR-010 — Conferência Item a Item
| Campo | Valor |
|-------|-------|
| Descrição | A conferência compara quantidade recebida × esperada, item a item. A recebida pode ser informada na unidade de compra e é convertida para a base na entrada (ADR-013, fator registrado — MMS-004 IV-BR-009). |
| Tipo | Obrigatória |
| Validação | Item do documento de origem; quantidade recebida ≥ 0; conversão para a base. |
| Erro | RC-ERR-010 |
| Evento | Em conferência |
| UC/API/Teste | UC-RC-002 / POST /receivings/{id}/lines / TC-RC-010 |

## RC-BR-011 — Divergência com Destino Documentado
| Campo | Valor |
|-------|-------|
| Descrição | Falta, excesso e avaria são registradas com quantidade e **destino documentado** (MMS-RG-07) — nenhuma diferença desaparece. |
| Tipo | Obrigatória |
| Validação | Tipo ∈ {falta, excesso, avaria}; destino informado; dentro/fora da tolerância. |
| Erro | RC-ERR-011 |
| Evento | Divergência registrada |
| UC/API/Teste | UC-RC-002 / POST /receivings/{id}/divergences / TC-RC-011 |

## RC-BR-012 — Tolerância Parametrizável
| Campo | Valor |
|-------|-------|
| Descrição | Divergência dentro da tolerância é aceita sem tratamento; acima exige tratativa (e aprovação quando parametrizada — MMS-RG-11). |
| Tipo | Parametrizável |
| Config | `materials.receiving.tolerance.quantity`; `materials.receiving.divergence.approval-required`. |
| Erro | RC-ERR-012 |
| UC/API/Teste | UC-RC-002 / (interno) / TC-RC-012 |

## RC-BR-020 — Entrada no Estoque via MMS-004
| Campo | Valor |
|-------|-------|
| Descrição | Conferência concluída gera **exatamente um** documento de entrada no MMS-004 (o Receiving nunca altera saldo — MMS-P-08); a entrada é na unidade base. |
| Tipo | Obrigatória |
| Validação | Um documento de entrada por recebimento concluído; quantidade convertida (RC-BR-010). |
| Erro | RC-ERR-020 |
| Evento | Entrada no estoque gerada |
| UC/API/Teste | UC-RC-003 / POST /receivings/{id}/complete / TC-RC-020 |

## RC-BR-021 — Compra Dedicada Entra Reservada
| Campo | Valor |
|-------|-------|
| Descrição | Quando a origem é compra destinada a uma solicitação e a compra dedicada está habilitada, o material entra no estoque **já reservado** para a solicitação de origem e retoma o atendimento (MMS-004/MMS-003). |
| Tipo | Parametrizável |
| Config | `materials.receiving.dedicated-purchase.auto-reserve`. |
| Evento | Atendimento retomado |
| UC/API/Teste | UC-RC-003 / (evento) / TC-RC-021 |

## RC-BR-022 — Recebimento Parcial
| Campo | Valor |
|-------|-------|
| Descrição | Recebimento parcial contra o mesmo documento de origem é permitido quando parametrizado; o saldo pendente do documento permanece aberto. |
| Tipo | Parametrizável |
| Config | `materials.receiving.partial.allowed` (padrão: true). |
| Erro | RC-ERR-022 |
| UC/API/Teste | UC-RC-002 / POST /receivings/{id}/lines / TC-RC-022 |

## RC-BR-030 — Máquina de Estados Única
| Campo | Valor |
|-------|-------|
| Descrição | Estados: Aguardando → Em Conferência → Concluído / Concluído com Divergência / Cancelado (MMS-001 §15.3); nenhuma transição fora dela; sem edição direta de estado. |
| Tipo | Obrigatória |
| Erro | RC-ERR-030 |
| UC/API/Teste | Todos / — / TC-RC-030 |

## RC-BR-040 — Aprovação de Divergência
| Campo | Valor |
|-------|-------|
| Descrição | Quando parametrizado, o destino da divergência exige aprovação (FD-001-04); quem confere não aprova o próprio destino restrito (SoD). |
| Tipo | Parametrizável |
| Config | `materials.receiving.divergence.approval-required`. |
| Erro | RC-ERR-040 |
| UC/API/Teste | UC-RC-002 / POST .../divergences/{id}/approve / TC-RC-040 |

## RC-BR-050 — Auditoria e Timeline
| Campo | Valor |
|-------|-------|
| Descrição | 100% das conferências, divergências, destinos e entradas geram auditoria (FD-001-06) e timeline (FD-001-07); falha de timeline nunca bloqueia. |
| Tipo | Obrigatória |
| UC/API/Teste | Todos / Todos / TC-RC-050 |

## RC-BR-060 — Permissões e Escopo
| Campo | Valor |
|-------|-------|
| Descrição | Receber/conferir é do Almoxarife; aprovar destino de divergência é do Supervisor; deny by default; escopo organizacional obrigatório; menus sem permissão ocultados. |
| Tipo | Obrigatória |
| Erro | RC-ERR-100 |
| UC/API/Teste | Todos / Todos / TC-RC-060 |

# 4. Matriz de Rastreabilidade

| Regra | Origem | UC | API | Evento | Teste |
|-------|--------|-----|-----|--------|-------|
| RC-BR-001 | MMS-RG-06 | UC-RC-001 | POST /receivings | Recebimento registrado | TC-RC-001 |
| RC-BR-002 | MMS-001 §6.2 | Todos | Todos | — | TC-RC-002 |
| RC-BR-010 | MMS-005 README / ADR-013 | UC-RC-002 | POST .../lines | Em conferência | TC-RC-010 |
| RC-BR-011 | MMS-RG-07 | UC-RC-002 | POST .../divergences | Divergência registrada | TC-RC-011 |
| RC-BR-012 | MMS-RG-11 | UC-RC-002 | (interno) | — | TC-RC-012 |
| RC-BR-020 | MMS-P-07/08 | UC-RC-003 | POST .../complete | Entrada gerada | TC-RC-020 |
| RC-BR-021 | MMS-001 §9 (regra 4) | UC-RC-003 | (evento) | Atendimento retomado | TC-RC-021 |
| RC-BR-022 | MMS-005 README | UC-RC-002 | POST .../lines | — | TC-RC-022 |
| RC-BR-030 | MMS-001 §15.3 | Todos | — | — | TC-RC-030 |
| RC-BR-040 | MMS-RG-11 / FD-001-04 | UC-RC-002 | POST .../divergences/{id}/approve | — | TC-RC-040 |
| RC-BR-050 | MMS-P-02/03 | Todos | Todos | Todos | TC-RC-050 |
| RC-BR-060 | MMS-001 §23 | Todos | Todos | — | TC-RC-060 |

**Cobertura:** 12/12 regras com origem e teste (100%).

# 5. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Business Rules do Receiving: 12 regras RC-BR (documento de origem, conferência com conversão de UoM, divergência com destino, tolerância, entrada via MMS-004, compra dedicada, recebimento parcial, state machine, aprovação de divergência, auditoria, permissões) com erros RC-ERR, eventos, config `materials.receiving.*` e rastreabilidade 100% — padrão MMS-004-02/MMS-003-02. |
