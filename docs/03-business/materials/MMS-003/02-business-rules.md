**Documento:** MMS-003-02 — Business Rules
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 (Visão do Módulo v1.1.0), MMS-001 (Documento Mestre Funcional — seções 8.2, 9, 14, 15.1, 23), MMS-002 (Item Catalog), MMS-004 (Inventory Management), FD-001-03 (Document Management), FD-001-04 (Workflow), FD-001-09 (Master Data), FD-001-10 (Configuration)
**Referências:** MMS-005, PR-001, MMS-002-02 / MMS-004-02 (padrão de formato), FD-001-01, FD-001-02, FD-001-06, FD-001-07, GOV-001

---

# 1. Objetivo

Formalizar as **regras de negócio** do módulo Material Requisition. Cada regra possui código, nome, descrição, tipo, validação, mensagem e código de erro, evento, caso de uso, API, caso de teste, configuração e observações — no padrão MMS-002-02.

Regras de vínculo:

- Toda regra deriva da visão aprovada do módulo (MMS-003 README), do fluxo corporativo (MMS-001 §9) ou das regras gerais da suíte (MMS-001 §14 — códigos MMS-RG referenciados explicitamente).
- Casos de uso (`UC-MR-xxx`), endpoints e testes (`TC-MR-xxx`) são referências conceituais que serão especificados em MMS-003-07 (Use Cases), MMS-003-13 (API) e MMS-003-17 (Test Scenarios).
- Conflito entre implementação e este documento resolve-se pela documentação.

# 2. Classificação das Regras

| Tipo | Significado | Tratamento |
|------|-------------|------------|
| **Obrigatória** | Inegociável em qualquer implantação | Exige teste automatizado (DoD) |
| **Parametrizável** | Comportamento definido por configuração (FD-001-10) | Valor padrão documentado; teste nos dois modos |
| **Informativa** | Orientação de qualidade que gera indicador, sem bloqueio | Verificada por KPI/relatório |

# 3. Convenções

- **Regras:** `MR-BR-xxx`, sequenciais por família, imutáveis após publicação;
- **Erros:** `MR-ERR-xxx` (espelhados nas mensagens i18n `mr.*`);
- **Eventos:** nomes funcionais da visão (MMS-003 README — Eventos Publicados); especificação técnica ADR-010 no MMS-003-05;
- **APIs:** caminhos conceituais sob `/api/v1/material-requisitions` (especificação no MMS-003-13);
- **Configuração:** namespace `materials.requisition.*` (FD-001-10).

---

# 4. Regras Gerais (Identidade e Cadastro)

## MR-BR-001 — Empresa Obrigatória e Isolada

| Campo | Valor |
|-------|-------|
| Código | MR-BR-001 |
| Nome | Empresa Obrigatória e Isolada |
| Descrição | Toda solicitação pertence a exatamente uma empresa; nenhuma consulta, regra, evento ou relatório cruza empresas. |
| Tipo | Obrigatória |
| Validação | Filtro obrigatório por `company_id`; empresa existente e ativa (FD-001-02); recurso fora do escopo responde 404. |
| Mensagem de erro | — (fora do escopo responde 404) |
| Código do erro | MR-ERR-404 |
| Evento | — |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-MR-001 |
| Configuração | Não configurável. |
| Observações | Anti-enumeração, padrão da suíte (MMS-001 §6.2). |

## MR-BR-002 — Solicitante e Escopo Organizacional

| Campo | Valor |
|-------|-------|
| Código | MR-BR-002 |
| Nome | Solicitante e Escopo Organizacional |
| Descrição | Toda solicitação tem um solicitante identificado; o solicitante opera dentro de seu escopo organizacional (empresa/unidade/centro de custo autorizados — FD-001-01/02). |
| Tipo | Obrigatória |
| Validação | Usuário autenticado com permissão MR-PERM-001; escopo avaliado na criação. |
| Mensagem de erro | "Operação fora do seu escopo organizacional." |
| Código do erro | MR-ERR-002 |
| Evento | Solicitação criada |
| Caso de uso | UC-MR-001 |
| API | POST /api/v1/material-requisitions |
| Caso de teste | TC-MR-002 |
| Configuração | Não configurável. |
| Observações | MMS-P-05; escopo obrigatório (MMS-001 §23). |

## MR-BR-003 — Justificativa Obrigatória

| Campo | Valor |
|-------|-------|
| Código | MR-BR-003 |
| Nome | Justificativa Obrigatória |
| Descrição | Toda solicitação exige justificativa livre, registrada e auditada. |
| Tipo | Obrigatória |
| Validação | Campo não vazio; tamanho mínimo/máximo parametrizável. |
| Mensagem de erro | "Informe a justificativa da solicitação." |
| Código do erro | MR-ERR-003 |
| Evento | Solicitação criada / submetida |
| Caso de uso | UC-MR-001 |
| API | POST, PATCH /api/v1/material-requisitions |
| Caso de teste | TC-MR-003 |
| Configuração | Tamanhos (`materials.requisition.justification.*`). |
| Observações | Base do contexto decisório do aprovador. |

## MR-BR-004 — Motivo Estruturado Parametrizável

| Campo | Valor |
|-------|-------|
| Código | MR-BR-004 |
| Nome | Motivo Estruturado |
| Descrição | Além da justificativa livre, a solicitação registra um **motivo** em vocabulário do Master Data (roteiro mensal, troca programada, troca de tamanho, danificado, perda, roubo…). Obrigatoriedade e lista são parametrizáveis por empresa. |
| Tipo | Parametrizável |
| Validação | Quando `materials.requisition.reason.required=true`, o motivo é obrigatório e vigente no FD-001-09 (`typeCode+code`). |
| Mensagem de erro | "Selecione um motivo válido para a solicitação." |
| Código do erro | MR-ERR-004 |
| Evento | Solicitação criada / submetida |
| Caso de uso | UC-MR-001 |
| API | POST, PATCH /api/v1/material-requisitions |
| Caso de teste | TC-MR-004 |
| Configuração | `materials.requisition.reason.required` (padrão: `false`); `typeCode` do vocabulário. |
| Observações | Origem: MMS-003 README v1.1.0 (EPI/Fardamento). |

## MR-BR-005 — Centro de Custo Obrigatório

| Campo | Valor |
|-------|-------|
| Código | MR-BR-005 |
| Nome | Centro de Custo Obrigatório |
| Descrição | Toda solicitação imputa um centro de custo do escopo do solicitante (FD-001-02), base de indicadores de consumo e controle gerencial. |
| Tipo | Obrigatória |
| Validação | Centro de custo existente, ativo e dentro do escopo; vínculo com a unidade. |
| Mensagem de erro | "Centro de custo inválido ou fora do seu escopo." |
| Código do erro | MR-ERR-005 |
| Evento | Solicitação criada |
| Caso de uso | UC-MR-001 |
| API | POST /api/v1/material-requisitions |
| Caso de teste | TC-MR-005 |
| Configuração | Não configurável (fronteira com FD-001-02). |
| Observações | KPI "consumo por centro de custo". |

## MR-BR-006 — Local de Entrega Ativo

| Campo | Valor |
|-------|-------|
| Código | MR-BR-006 |
| Nome | Local de Entrega Ativo |
| Descrição | A solicitação seleciona um local de entrega **ativo** do cadastro da empresa (MR-BR-060). |
| Tipo | Obrigatória |
| Validação | Local existente, ativo e da mesma empresa. |
| Mensagem de erro | "Local de entrega inválido ou inativo." |
| Código do erro | MR-ERR-006 |
| Evento | Solicitação criada |
| Caso de uso | UC-MR-001 |
| API | POST /api/v1/material-requisitions |
| Caso de teste | TC-MR-006 |
| Configuração | Não configurável. |
| Observações | Cadastro de locais de entrega na família MR-BR-060. |

---

# 5. Regras dos Itens da Solicitação

## MR-BR-010 — Somente Itens Ativos

| Campo | Valor |
|-------|-------|
| Código | MR-BR-010 |
| Nome | Somente Itens Ativos |
| Descrição | Apenas itens **Ativos** no Item Catalog (MMS-002) entram em novas solicitações; item inativado bloqueia inclusão (MMS-RG-08). |
| Tipo | Obrigatória |
| Validação | Estado do item consultado no MMS-002 no momento da inclusão (IC-BR-021). |
| Mensagem de erro | "Item inativo ou inexistente no catálogo." |
| Código do erro | MR-ERR-010 |
| Evento | — (validação de consumo) |
| Caso de uso | UC-MR-001 |
| API | POST, PATCH /api/v1/material-requisitions |
| Caso de teste | TC-MR-010 |
| Configuração | Não configurável. |
| Observações | Item inativado após a criação e antes da submissão é sinalizado ao solicitante. |

## MR-BR-011 — Quantidade Positiva

| Campo | Valor |
|-------|-------|
| Código | MR-BR-011 |
| Nome | Quantidade Positiva |
| Descrição | Toda linha de item tem quantidade > 0, na unidade base do item (MMS-002, ADR-013). |
| Tipo | Obrigatória |
| Validação | Quantidade numérica > 0; unidade herdada do item. |
| Mensagem de erro | "Quantidade inválida para o item." |
| Código do erro | MR-ERR-011 |
| Evento | Solicitação criada / submetida |
| Caso de uso | UC-MR-001 |
| API | POST, PATCH /api/v1/material-requisitions |
| Caso de teste | TC-MR-011 |
| Configuração | Não configurável. |
| Observações | A conversão de UoM ocorre no estoque/compra (MMS-004/PR-001), não na solicitação. |

## MR-BR-012 — Tamanho Obrigatório para Item com Grade

| Campo | Valor |
|-------|-------|
| Código | MR-BR-012 |
| Nome | Tamanho Obrigatório para Item com Grade |
| Descrição | Quando o item possui grade de tamanhos (MMS-002, IC-BR-081), a seleção do tamanho é obrigatória por linha e acompanha reserva, separação e entrega (MMS-004, IV-BR-121). |
| Tipo | Obrigatória |
| Validação | Tamanho ∈ grade vigente do item; obrigatório apenas quando o item tem grade. |
| Mensagem de erro | "Selecione um tamanho válido da grade do item." |
| Código do erro | MR-ERR-012 |
| Evento | Solicitação criada / submetida |
| Caso de uso | UC-MR-001 |
| API | POST, PATCH /api/v1/material-requisitions |
| Caso de teste | TC-MR-012 |
| Configuração | Não configurável (decorre do cadastro do item). |
| Observações | Origem: MMS-003 README v1.1.0. |

## MR-BR-013 — Anexos por Motivo

| Campo | Valor |
|-------|-------|
| Código | MR-BR-013 |
| Nome | Anexos por Motivo |
| Descrição | O solicitante pode anexar evidências via Document Management (FD-001-03); motivos configurados (ex.: danificado, perda, roubo) **exigem** anexo. |
| Tipo | Parametrizável |
| Validação | Quando o motivo consta em `materials.requisition.attachments.required-reasons`, ao menos um anexo válido é obrigatório (tipo/tamanho por FD-001-03). |
| Mensagem de erro | "Este motivo exige anexo (evidência)." |
| Código do erro | MR-ERR-013 |
| Evento | Solicitação submetida |
| Caso de uso | UC-MR-001 |
| API | POST /api/v1/material-requisitions/{id}/attachments |
| Caso de teste | TC-MR-013 |
| Configuração | `materials.requisition.attachments.required-reasons` (padrão: vazia). |
| Observações | Binário no MinIO via FD-001-03; a solicitação guarda referência. |

---

# 6. Regras de Ciclo de Vida

## MR-BR-020 — Estado Inicial Rascunho

| Campo | Valor |
|-------|-------|
| Código | MR-BR-020 |
| Nome | Estado Inicial Rascunho |
| Descrição | Toda solicitação nasce em **Rascunho** e só entra no fluxo após a submissão. Estados conforme MMS-001 §15.1 e a State Machine do módulo (MMS-003-03). |
| Tipo | Obrigatória |
| Validação | Estado inicial fixo na factory do Aggregate. |
| Mensagem de erro | — (automático) |
| Código do erro | — |
| Evento | Solicitação criada |
| Caso de uso | UC-MR-001 |
| API | POST /api/v1/material-requisitions |
| Caso de teste | TC-MR-020 |
| Configuração | Não configurável. |
| Observações | Ciclo: Rascunho → Submetida → Em Aprovação → Aprovada → Em Atendimento → Concluída (finais: Rejeitada, Cancelada; exceção: Aguardando Compra). |

## MR-BR-021 — Submissão Completa

| Campo | Valor |
|-------|-------|
| Código | MR-BR-021 |
| Nome | Submissão Completa |
| Descrição | A submissão exige a solicitação válida em todas as regras de cadastro e de itens (MR-BR-001..013); submeter dispara o workflow (MR-BR-030). |
| Tipo | Obrigatória |
| Validação | Bateria de guards de cadastro/itens satisfeita; ao menos uma linha de item. |
| Mensagem de erro | "A solicitação não está apta à submissão." |
| Código do erro | MR-ERR-021 |
| Evento | Solicitação submetida |
| Caso de uso | UC-MR-002 |
| API | POST /api/v1/material-requisitions/{id}/submit |
| Caso de teste | TC-MR-021 |
| Configuração | Não configurável. |
| Observações | Após submetida, edição segue a matriz de editabilidade por estado (MMS-003-03). |

## MR-BR-022 — Cancelamento por Estado

| Campo | Valor |
|-------|-------|
| Código | MR-BR-022 |
| Nome | Cancelamento por Estado |
| Descrição | A solicitação pode ser cancelada pelo solicitante ou pela gestão apenas nos estados permitidos; o cancelamento libera reservas ativas vinculadas (MMS-004, IV-BR-022). |
| Tipo | Parametrizável |
| Validação | Estado atual ∈ `materials.requisition.cancel.allowed-states`; motivo quando parametrizado; dispara liberação de reservas. |
| Mensagem de erro | "A solicitação não pode ser cancelada no estado atual." |
| Código do erro | MR-ERR-022 |
| Evento | Solicitação cancelada |
| Caso de uso | UC-MR-006 |
| API | POST /api/v1/material-requisitions/{id}/cancel |
| Caso de teste | TC-MR-022 |
| Configuração | `materials.requisition.cancel.allowed-states` (padrão: Rascunho, Submetida, Em Aprovação, Aguardando Compra). |
| Observações | Cancelar solicitação com itens já entregues não é permitido (conclusão parcial preservada). |

---

# 7. Regras de Workflow e Aprovação

## MR-BR-030 — Workflow Parametrizável

| Campo | Valor |
|-------|-------|
| Código | MR-BR-030 |
| Nome | Workflow Parametrizável |
| Descrição | A aprovação ocorre via FD-001-04, parametrizável por empresa, unidade, valor estimado e criticidade dos itens (MMS-P-04). Quando não exigida, a solicitação segue direto à validação de estoque. |
| Tipo | Parametrizável |
| Validação | Roteamento do workflow conforme `materials.requisition.approval.required` e critérios; nenhum fluxo paralelo (MMS-P-04). |
| Mensagem de erro | "Solicitação aguardando aprovação." |
| Código do erro | MR-ERR-030 |
| Evento | Solicitação em aprovação / aprovada / rejeitada / retornada |
| Caso de uso | UC-MR-003 |
| API | POST /api/v1/material-requisitions/{id}/submit |
| Caso de teste | TC-MR-030 |
| Configuração | `materials.requisition.approval.required` e critérios (valor, criticidade). |
| Observações | Mesma experiência de fila de aprovações do PR-001. |

## MR-BR-031 — Aprovação Parcial por Item

| Campo | Valor |
|-------|-------|
| Código | MR-BR-031 |
| Nome | Aprovação Parcial por Item |
| Descrição | O aprovador pode aprovar integralmente, **alterar quantidades**, **rejeitar itens específicos**, cancelar toda a solicitação e registrar observações. Itens rejeitados/reduzidos são registrados com motivo do decisor; apenas os aprovados seguem para validação/roteamento. |
| Tipo | Parametrizável |
| Validação | Quando `materials.requisition.approval.partial-allowed=true`, ajustes por item são permitidos; toda alteração registra decisor e motivo. |
| Mensagem de erro | "Ação de aprovação inválida para o item." |
| Código do erro | MR-ERR-031 |
| Evento | Aprovação parcial registrada / Solicitação aprovada |
| Caso de uso | UC-MR-003 |
| API | POST /api/v1/material-requisitions/{id}/approve |
| Caso de teste | TC-MR-031 |
| Configuração | `materials.requisition.approval.partial-allowed` (padrão: `true`). |
| Observações | Origem: MMS-003 README v1.1.0. |

## MR-BR-032 — Segregação de Funções (SoD)

| Campo | Valor |
|-------|-------|
| Código | MR-BR-032 |
| Nome | Segregação de Funções |
| Descrição | O solicitante **nunca aprova a própria solicitação** (mesma SoD do PR-001); aprovador ≠ solicitante em qualquer modo. |
| Tipo | Obrigatória |
| Validação | Aprovador diferente do solicitante; decisão auditada. |
| Mensagem de erro | "O solicitante não pode aprovar a própria solicitação." |
| Código do erro | MR-ERR-032 |
| Evento | — (negação auditada) |
| Caso de uso | UC-MR-003 |
| API | POST /api/v1/material-requisitions/{id}/approve |
| Caso de teste | TC-MR-032 |
| Configuração | Não desligável. |
| Observações | MMS-001 §23; padrão PR-001-09. |

---

# 8. Regras de Validação de Estoque e Roteamento

## MR-BR-040 — Validação Após a Aprovação

| Campo | Valor |
|-------|-------|
| Código | MR-BR-040 |
| Nome | Validação Após a Aprovação |
| Descrição | A validação de estoque ocorre **após** a aprovação, nunca antes; aprovar não compromete saldo, reservar sim (regra 2 do fluxo corporativo — MMS-001 §9). |
| Tipo | Obrigatória |
| Validação | Validação disparada apenas em solicitações Aprovadas; consulta o MMS-004 item a item. |
| Mensagem de erro | — (regra de fluxo) |
| Código do erro | — |
| Evento | Estoque validado para a solicitação |
| Caso de uso | UC-MR-004 |
| API | (interno) integração MMS-004 |
| Caso de teste | TC-MR-040 |
| Configuração | Não configurável. |
| Observações | Reserva antecipada é funcionalidade futura sujeita a ADR. |

## MR-BR-041 — Validação Usa Somente o Disponível

| Campo | Valor |
|-------|-------|
| Código | MR-BR-041 |
| Nome | Validação Usa Somente o Disponível |
| Descrição | A validação considera exclusivamente o **saldo disponível** do MMS-004 (total − reservado — MMS-RG-09 / IV-BR-005), por item × tamanho × local. |
| Tipo | Obrigatória |
| Validação | Resultado por item (atende / não atende) com base no disponível no momento da validação. |
| Mensagem de erro | — (resultado de validação) |
| Código do erro | — |
| Evento | Estoque validado para a solicitação |
| Caso de uso | UC-MR-004 |
| API | (interno) MMS-004 GET balances |
| Caso de teste | TC-MR-041 |
| Configuração | Não configurável. |
| Observações | Segregação cliente/contrato respeitada (IV-BR-060). |

## MR-BR-042 — Roteamento por Item (Rota Mista)

| Campo | Valor |
|-------|-------|
| Código | MR-BR-042 |
| Nome | Roteamento por Item |
| Descrição | Itens com saldo seguem para reserva/atendimento (MMS-004); itens sem saldo geram demanda de compra (PR-001). Uma mesma solicitação pode ter as duas rotas; nenhum item fica sem rota. |
| Tipo | Obrigatória |
| Validação | Cada item aprovado recebe rota explícita (estoque/compra) após a validação. |
| Mensagem de erro | — (fato de roteamento) |
| Código do erro | — |
| Evento | Item roteado (estoque × compra) |
| Caso de uso | UC-MR-004 |
| API | (interno) roteamento |
| Caso de teste | TC-MR-042 |
| Configuração | Não configurável. |
| Observações | A solicitação só conclui quando todos os itens concluem (MR-BR-052). |

## MR-BR-043 — Demanda de Compra com Rastreabilidade Bidirecional

| Campo | Valor |
|-------|-------|
| Código | MR-BR-043 |
| Nome | Demanda de Compra com Rastreabilidade Bidirecional |
| Descrição | Item sem saldo gera demanda de compra no PR-001 com **referência à solicitação de origem**; o PR-001 mantém o vínculo de volta (rastreabilidade bidirecional — regra 3 do fluxo). |
| Tipo | Obrigatória |
| Validação | Demanda gerada via outbox na mesma transação do roteamento (ADR-010); referência de origem obrigatória. |
| Mensagem de erro | — |
| Código do erro | — |
| Evento | Demanda de compra gerada |
| Caso de uso | UC-MR-004 |
| API | (evento) → PR-001 |
| Caso de teste | TC-MR-043 |
| Configuração | Não configurável. |
| Observações | Status da compra alimenta a visão consolidada (MR-BR-051). |

## MR-BR-044 — Compra Dedicada Parametrizável

| Campo | Valor |
|-------|-------|
| Código | MR-BR-044 |
| Nome | Compra Dedicada Parametrizável |
| Descrição | Quando parametrizado, o material comprado para a solicitação entra no estoque **já reservado** para ela (compra dedicada — MMS-005/MMS-004). |
| Tipo | Parametrizável |
| Validação | `materials.requisition.purchase.dedicated=true` → entrada gerada pelo MMS-005 já reservada à solicitação de origem. |
| Mensagem de erro | — |
| Código do erro | — |
| Evento | (consumido) Material recebido → atendimento retomado |
| Caso de uso | UC-MR-005 |
| API | (evento) ← MMS-005 |
| Caso de teste | TC-MR-044 |
| Configuração | `materials.requisition.purchase.dedicated` (padrão: `false`). |
| Observações | Integração plena na v1.1 (MMS-005). |

---

# 9. Regras de Atendimento, Entrega e Conclusão

## MR-BR-050 — Módulo Não Movimenta Saldo

| Campo | Valor |
|-------|-------|
| Código | MR-BR-050 |
| Nome | Módulo Não Movimenta Saldo |
| Descrição | Reserva, separação e entrega são executadas pelo MMS-004; a solicitação é o **documento de origem** (MMS-P-07), mas nunca altera saldo diretamente. |
| Tipo | Obrigatória |
| Validação | Nenhum caminho de escrita de saldo neste módulo; ações de estoque via contratos/eventos do MMS-004. |
| Mensagem de erro | — (restrição arquitetural) |
| Código do erro | — |
| Evento | (consumidos) Reserva criada / Separação / Entrega |
| Caso de uso | UC-MR-005 |
| API | (eventos) ↔ MMS-004 |
| Caso de teste | TC-MR-050 |
| Configuração | Não configurável. |
| Observações | Fronteira de domínio inviolável (ADR-012). |

## MR-BR-051 — Acompanhamento Consolidado

| Campo | Valor |
|-------|-------|
| Código | MR-BR-051 |
| Nome | Acompanhamento Consolidado |
| Descrição | O solicitante acompanha todos os itens na mesma solicitação, nas rotas de estoque e de compra, com status atualizado por eventos consumidos (MMS-004, PR-001, MMS-005). |
| Tipo | Obrigatória |
| Validação | Projeção de status por item atualizada a cada evento consumido. |
| Mensagem de erro | — |
| Código do erro | — |
| Evento | (consumidos) status de reserva/separação/entrega/compra/recebimento |
| Caso de uso | UC-MR-005 |
| API | GET /api/v1/material-requisitions/{id} |
| Caso de teste | TC-MR-051 |
| Configuração | Não configurável. |
| Observações | Jornada 10.1 (MMS-001). |

## MR-BR-052 — Conclusão por Confirmação

| Campo | Valor |
|-------|-------|
| Código | MR-BR-052 |
| Nome | Conclusão por Confirmação |
| Descrição | O solicitante confirma o recebimento; a solicitação conclui quando **todos** os itens concluem (entregues pelo estoque ou recebidos da compra). |
| Tipo | Obrigatória |
| Validação | Estado Concluída apenas com todos os itens em estado terminal de atendimento. |
| Mensagem de erro | "Há itens pendentes de atendimento nesta solicitação." |
| Código do erro | MR-ERR-052 |
| Evento | Solicitação concluída |
| Caso de uso | UC-MR-005 |
| API | POST /api/v1/material-requisitions/{id}/confirm-receipt |
| Caso de teste | TC-MR-052 |
| Configuração | Não configurável. |
| Observações | Conclusão parcial não existe: a solicitação só fecha completa. |

---

# 10. Regras de Locais de Entrega

## MR-BR-060 — Cadastro de Locais de Entrega

| Campo | Valor |
|-------|-------|
| Código | MR-BR-060 |
| Nome | Cadastro de Locais de Entrega |
| Descrição | O módulo mantém, por empresa, o cadastro de locais de entrega (código **gerado pelo sistema**, descrição/cliente, vínculo com a unidade), administrado por Administrador ou Gerente de Suprimentos. |
| Tipo | Obrigatória |
| Validação | Código único por empresa gerado pelo sistema; local ativo para seleção; inativação exige que nenhuma solicitação em aberto o referencie. |
| Mensagem de erro | "Local de entrega inválido ou em uso." |
| Código do erro | MR-ERR-060 |
| Evento | Local de entrega criado / alterado / inativado |
| Caso de uso | UC-MR-007 |
| API | POST/PATCH /api/v1/material-requisitions/delivery-locations |
| Caso de teste | TC-MR-060 |
| Configuração | Não configurável. |
| Observações | Origem: MMS-003 README v1.1.0. |

---

# 11. Regras da Visão do Almoxarifado

## MR-BR-070 — Visão Exclusiva do Almoxarifado

| Campo | Valor |
|-------|-------|
| Código | MR-BR-070 |
| Nome | Visão Exclusiva do Almoxarifado |
| Descrição | A fila do Warehouse Workspace mostra todas as solicitações **aprovadas** de todos os solicitantes, com acesso a informações e anexos, filtrável por solicitante, período, status, centro de custo, empresa, tipo de produto (categoria) e número. É **exclusiva** dos papéis de almoxarifado — solicitantes não a acessam. |
| Tipo | Obrigatória |
| Validação | Acesso restrito a papéis de almoxarifado (deny explícito ao Requester — alinhado a MMS-004-09); filtros aplicados sobre o escopo do operador. |
| Mensagem de erro | "Você não tem acesso à visão do almoxarifado." |
| Código do erro | MR-ERR-070 |
| Evento | — |
| Caso de uso | UC-MR-008 |
| API | GET /api/v1/material-requisitions/warehouse-queue |
| Caso de teste | TC-MR-070 |
| Configuração | Não configurável. |
| Observações | Origem: MMS-003 README v1.1.0. |

---

# 12. Regras de Auditoria, Timeline e Segurança

## MR-BR-080 — Auditoria de 100% das Transições

| Campo | Valor |
|-------|-------|
| Código | MR-BR-080 |
| Nome | Auditoria de 100% das Transições |
| Descrição | Toda criação, alteração, decisão de aprovação, roteamento, cancelamento e conclusão gera auditoria imutável com autor, data/hora, correlationId e valores anterior/posterior (MMS-P-02). |
| Tipo | Obrigatória |
| Validação | Registro no FD-001-06 na mesma transação da operação. |
| Mensagem de erro | — (falha aborta a operação) |
| Código do erro | — |
| Evento | Todos |
| Caso de uso | Todos |
| API | Todos |
| Caso de teste | TC-MR-080 |
| Configuração | Não configurável. |
| Observações | MMS-001 §18. |

## MR-BR-081 — Timeline da Solicitação

| Campo | Valor |
|-------|-------|
| Código | MR-BR-081 |
| Nome | Timeline da Solicitação |
| Descrição | Marcos (submissão, decisões, roteamento, reserva/entrega, vínculos com PR-001, recebimento) aparecem na timeline (FD-001-07), com visibilidade por regra; falha de timeline nunca bloqueia o fluxo (MMS-P-03). |
| Tipo | Obrigatória |
| Validação | Entrada de timeline por marco, com paginação keyset. |
| Mensagem de erro | — (não bloqueia) |
| Código do erro | — |
| Evento | Todos |
| Caso de uso | UC-MR-005 |
| API | GET /api/v1/material-requisitions/{id}/timeline |
| Caso de teste | TC-MR-081 |
| Configuração | Não configurável. |
| Observações | Inclui vínculos bidirecionais com o PR-001. |

## MR-BR-090 — Permissões por Papel

| Campo | Valor |
|-------|-------|
| Código | MR-BR-090 |
| Nome | Permissões por Papel |
| Descrição | Criar/consultar próprias solicitações é do Solicitante; aprovar é do Aprovador; a visão do almoxarifado é dos papéis de almoxarifado; configuração é do Administrador/Gerente. Deny by default; menus sem permissão são ocultados. |
| Tipo | Obrigatória |
| Validação | Fluxo de autorização do Foundation (escopo → RBAC → ABAC), com negação auditada. |
| Mensagem de erro | "Você não tem permissão para esta operação." |
| Código do erro | MR-ERR-100 |
| Evento | — (negação auditada) |
| Caso de uso | Todos |
| API | Todos |
| Caso de teste | TC-MR-090 |
| Configuração | Não configurável (matriz detalhada no MMS-003-09). |
| Observações | SoD MR-BR-032; padrão PR-001-09. |

## MR-BR-091 — Concorrência e Keyset

| Campo | Valor |
|-------|-------|
| Código | MR-BR-091 |
| Nome | Concorrência e Keyset |
| Descrição | Toda escrita usa concorrência otimista (`version`/`If-Match`); listagens usam paginação keyset; validação de estoque pode ser assíncrona quando a solicitação tem muitos itens. |
| Tipo | Obrigatória |
| Validação | Conflito de versão recusado sem efeito; cursor opaco assinado nas listas. |
| Mensagem de erro | "A solicitação foi alterada por outro usuário. Recarregue e tente novamente." |
| Código do erro | MR-ERR-409 |
| Evento | — |
| Caso de uso | Todos os de escrita |
| API | Todos os endpoints de escrita/lista |
| Caso de teste | TC-MR-091 |
| Configuração | `materials.requisition.search.page-size` (padrão: 20). |
| Observações | NFR da visão do módulo. |

---

# 13. Matriz de Rastreabilidade

| Regra | Origem (documento) | UC | API (conceitual) | Evento | Teste |
|-------|--------------------|-----|------------------|--------|-------|
| MR-BR-001 | MMS-001 §6.2 | Todos | Todos | — | TC-MR-001 |
| MR-BR-002 | MMS-003 README (atores) | UC-MR-001 | POST /material-requisitions | Solicitação criada | TC-MR-002 |
| MR-BR-003 | MMS-003 README (escopo) | UC-MR-001 | POST, PATCH | Criada/submetida | TC-MR-003 |
| MR-BR-004 | MMS-003 README v1.1.0 | UC-MR-001 | POST, PATCH | Criada/submetida | TC-MR-004 |
| MR-BR-005 | MMS-003 README (entradas) | UC-MR-001 | POST | Solicitação criada | TC-MR-005 |
| MR-BR-006 | MMS-003 README (locais) | UC-MR-001 | POST | Solicitação criada | TC-MR-006 |
| MR-BR-010 | MMS-RG-08 / IC-BR-021 | UC-MR-001 | POST, PATCH | — | TC-MR-010 |
| MR-BR-011 | MMS-003 README (escopo) | UC-MR-001 | POST, PATCH | Criada/submetida | TC-MR-011 |
| MR-BR-012 | MMS-003 README v1.1.0 / IC-BR-081 | UC-MR-001 | POST, PATCH | Criada/submetida | TC-MR-012 |
| MR-BR-013 | MMS-003 README v1.1.0 | UC-MR-001 | POST .../attachments | Solicitação submetida | TC-MR-013 |
| MR-BR-020 | MMS-001 §15.1 | UC-MR-001 | POST | Solicitação criada | TC-MR-020 |
| MR-BR-021 | MMS-003 README (fluxo) | UC-MR-002 | POST .../submit | Solicitação submetida | TC-MR-021 |
| MR-BR-022 | MMS-003 README (cancelamento) | UC-MR-006 | POST .../cancel | Solicitação cancelada | TC-MR-022 |
| MR-BR-030 | MMS-P-04 / FD-001-04 | UC-MR-003 | POST .../submit | Em aprovação/aprovada/rejeitada | TC-MR-030 |
| MR-BR-031 | MMS-003 README v1.1.0 | UC-MR-003 | POST .../approve | Aprovação parcial registrada | TC-MR-031 |
| MR-BR-032 | MMS-001 §23 / PR-001-09 | UC-MR-003 | POST .../approve | — | TC-MR-032 |
| MR-BR-040 | MMS-001 §9 (regra 2) | UC-MR-004 | (interno) | Estoque validado | TC-MR-040 |
| MR-BR-041 | MMS-RG-09 / IV-BR-005 | UC-MR-004 | (interno) MMS-004 | Estoque validado | TC-MR-041 |
| MR-BR-042 | MMS-001 §9 (rota mista) | UC-MR-004 | (interno) | Item roteado | TC-MR-042 |
| MR-BR-043 | MMS-001 §9 (regra 3) | UC-MR-004 | (evento) PR-001 | Demanda de compra gerada | TC-MR-043 |
| MR-BR-044 | MMS-001 §9 (regra 4) | UC-MR-005 | (evento) MMS-005 | (consumido) recebido | TC-MR-044 |
| MR-BR-050 | ADR-012 / MMS-P-07 | UC-MR-005 | (eventos) MMS-004 | (consumidos) | TC-MR-050 |
| MR-BR-051 | MMS-003 README (acompanhamento) | UC-MR-005 | GET /{id} | (consumidos) | TC-MR-051 |
| MR-BR-052 | MMS-003 README (conclusão) | UC-MR-005 | POST .../confirm-receipt | Solicitação concluída | TC-MR-052 |
| MR-BR-060 | MMS-003 README v1.1.0 | UC-MR-007 | POST/PATCH .../delivery-locations | Local criado/alterado | TC-MR-060 |
| MR-BR-070 | MMS-003 README v1.1.0 | UC-MR-008 | GET .../warehouse-queue | — | TC-MR-070 |
| MR-BR-080 | MMS-P-02 / MMS-001 §18 | Todos | Todos | Todos | TC-MR-080 |
| MR-BR-081 | MMS-P-03 | UC-MR-005 | GET .../timeline | Todos | TC-MR-081 |
| MR-BR-090 | MMS-001 §23 | Todos | Todos | — | TC-MR-090 |
| MR-BR-091 | MMS-003 README (NFR) | Todos | Escrita/lista | — | TC-MR-091 |

**Cobertura:** 30/30 regras com origem documentada e teste associado (100%).

---

# 14. Dependências

| Dependência | Uso |
|-------------|-----|
| MMS-003 (Visão) | Origem de todas as regras |
| MMS-001 (Documento Mestre) | Regras MMS-RG-08/09/11/12 e princípios MMS-P-01..08 |
| MMS-002 (Item Catalog) | Itens ativos (MR-BR-010), unidade base, grade (MR-BR-012) |
| MMS-004 (Inventory) | Validação de estoque, reserva, entrega (MR-BR-040..051), IV-BR-005/060/121 |
| PR-001 | Demanda de compra com rastreabilidade (MR-BR-043) |
| MMS-005 (Receiving) | Retomada do atendimento de itens comprados (MR-BR-044) |
| FD-001-03/04/09/10 | Anexos, workflow, motivos/grades e parâmetros |
| FD-001-01/02/06/07 | Autorização, escopo, auditoria e timeline |

---

# 15. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Criação das Business Rules do Material Requisition: 30 regras codificadas (MR-BR-001..091) em 9 famílias — identidade/cadastro, itens (com tamanho/anexos), ciclo de vida, workflow/aprovação (parcial por item + SoD), validação de estoque e roteamento (rota mista, rastreabilidade bidirecional, compra dedicada), atendimento/entrega/conclusão, locais de entrega, visão do almoxarifado, auditoria/timeline/segurança — com validação, erros (MR-ERR), eventos, UCs/API/testes conceituais, configurações `materials.requisition.*` e matriz de rastreabilidade 100% — derivadas da visão do módulo (MMS-003 v1.1.0) e do fluxo corporativo (MMS-001 §9), no padrão MMS-002-02/MMS-004-02 |
