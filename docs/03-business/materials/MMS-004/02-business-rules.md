**Documento:** MMS-004-02 — Business Rules
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-004 (Visão do Módulo), MMS-004-01 (Business Context), MMS-001 (Documento Mestre Funcional — seções 6, 8.3, 9, 14, 15.2, 18, 20, 23), MMS-002 (Item Catalog), FD-001-10 (Configuration)
**Referências:** MMS-003, MMS-005, PR-001, MMS-002-02 (padrão de formato), FD-001-01, FD-001-02, FD-001-04, FD-001-05, FD-001-06, FD-001-07, GOV-001

---

# 1. Objetivo

Este documento formaliza as **regras de negócio** do módulo Inventory Management. Cada regra possui código, nome, descrição, tipo, validação, mensagem e código de erro, evento, caso de uso, API, caso de teste, configuração e observações — no padrão estabelecido pelo MMS-002-02.

Regras de vínculo:

- Nenhuma regra inventa comportamento: toda regra deriva da visão aprovada do módulo (MMS-004 README), do Business Context (MMS-004-01) ou das regras gerais da suíte (MMS-001, seção 14 — códigos MMS-RG referenciados explicitamente).
- Casos de uso (`UC-IV-xxx`), endpoints e casos de teste (`TC-IV-xxx`) citados aqui são **referências conceituais** que serão especificados nos documentos MMS-004-07 (Use Cases), MMS-004-13 (API) e MMS-004-17 (Test Scenarios) — sem divergir deste catálogo.
- Conflito entre implementação e este documento resolve-se pela documentação.

---

# 2. Classificação das Regras

| Tipo | Significado | Tratamento |
|------|-------------|------------|
| **Obrigatória** | Inegociável em qualquer implantação | Exige teste automatizado (DoD) |
| **Parametrizável** | Comportamento definido por configuração (FD-001-10) | Valor padrão documentado; teste nos dois modos |
| **Informativa** | Orientação de qualidade que gera indicador, sem bloqueio | Verificada por KPI/relatório |

---

# 3. Convenções deste Documento

- **Regras:** `IV-BR-xxx`, sequenciais por família, imutáveis após publicação;
- **Erros:** `IV-ERR-xxx` (catálogo de erros do módulo, espelhado nas mensagens i18n `iv.*`);
- **Eventos:** nomes funcionais da visão do módulo (MMS-004 README — Eventos Publicados); a especificação técnica seguirá ADR-010 no documento de eventos do módulo;
- **Casos de uso:** `UC-IV-xxx` (conceituais até o MMS-004-07);
- **Casos de teste:** `TC-IV-xxx` (conceituais até o MMS-004-17);
- **API:** caminhos conceituais sob `/api/v1/inventory` (especificação no MMS-004-13).

---

# 4. Regras de Integridade de Saldo

## IV-BR-001 — Saldo Derivado de Movimentação

| Campo | Valor |
|-------|-------|
| Código | IV-BR-001 |
| Nome | Saldo Derivado de Movimentação |
| Descrição | Nenhum saldo é criado ou alterado diretamente — nem por usuário, administrador, API ou job. Saldo é sempre a projeção derivada de documentos de movimentação confirmados (MMS-P-08). |
| Tipo | Obrigatória |
| Validação | Toda escrita de saldo ocorre exclusivamente pela confirmação de documento de movimentação; inexistência de qualquer outro caminho de escrita (prova de integridade do DoD). |
| Mensagem de erro | "Saldo não pode ser alterado diretamente. Registre um documento de movimentação." |
| Código do erro | IV-ERR-001 |
| Evento | — (regra estrutural) |
| Caso de uso | Todos |
| API | Todos os endpoints de escrita |
| Caso de teste | TC-IV-001 |
| Configuração | Não configurável (restrição arquitetural 1 da visão). |
| Observações | Inclui posição inicial: saldo inicial entra por documento de entrada de carga inicial. |

---

## IV-BR-002 — Nenhuma Movimentação sem Documento

| Campo | Valor |
|-------|-------|
| Código | IV-BR-002 |
| Nome | Nenhuma Movimentação sem Documento |
| Descrição | Toda alteração de saldo (total ou reservado) é registrada por um documento de movimentação com tipo, quantidade, item, local, responsável, data/hora e documento de origem referenciável (MMS-P-07). |
| Tipo | Obrigatória |
| Validação | Documento completo e válido antes da confirmação; referência de origem obrigatória conforme o tipo. |
| Mensagem de erro | "Informe o documento de origem da movimentação." |
| Código do erro | IV-ERR-002 |
| Evento | — (regra estrutural) |
| Caso de uso | Todos |
| API | Todos os endpoints de escrita |
| Caso de teste | TC-IV-002 |
| Configuração | Não configurável (restrição arquitetural 2 da visão). |
| Observações | Origem por tipo: entrada ← MMS-005/devolução/ajuste; saída ← MMS-003/consumo/ajuste; reserva/liberação ← MMS-003; transferência ← operação; ajuste ← inventário/divergência. |

---

## IV-BR-003 — Documento Confirmado Imutável

| Campo | Valor |
|-------|-------|
| Código | IV-BR-003 |
| Nome | Documento Confirmado Imutável |
| Descrição | Documento de movimentação confirmado não pode ser editado nem excluído. Correções ocorrem exclusivamente por **estorno**: novo documento vinculado ao original, com efeito inverso (MMS-001, seção 15.2). |
| Tipo | Obrigatória |
| Validação | Operações de escrita sobre documento confirmado recusadas; estorno exige referência ao documento original e motivo. |
| Mensagem de erro | "Documento confirmado é imutável. Utilize o estorno para correção." |
| Código do erro | IV-ERR-003 |
| Evento | Estorno registrado |
| Caso de uso | UC-IV-008 |
| API | POST /api/v1/inventory/movements/{id}/reverse |
| Caso de teste | TC-IV-003 |
| Configuração | Não configurável (restrição arquitetural 3 da visão). |
| Observações | Estorno também é imutável após confirmação. |

---

## IV-BR-004 — Saída Nunca Gera Saldo Negativo

| Campo | Valor |
|-------|-------|
| Código | IV-BR-004 |
| Nome | Saída Nunca Gera Saldo Negativo |
| Descrição | Qualquer documento que reduza saldo (saída, transferência na origem, ajuste negativo, reserva sobre o disponível) é bloqueado quando a quantidade excede o saldo correspondente (MMS-RG-04). |
| Tipo | Obrigatória |
| Validação | Verificação atômica na confirmação: quantidade ≤ saldo disponível (reserva) ou ≤ saldo total (saída/ajuste negativo). |
| Mensagem de erro | "Saldo insuficiente para a movimentação." |
| Código do erro | IV-ERR-004 |
| Evento | — (bloqueio auditado) |
| Caso de uso | UC-IV-002, UC-IV-003, UC-IV-005, UC-IV-006 |
| API | POST /api/v1/inventory/movements |
| Caso de teste | TC-IV-004 |
| Configuração | Não configurável. |
| Observações | O bloqueio acontece na validação, não em conferência posterior. |

---

## IV-BR-005 — Validação Usa Somente o Disponível

| Campo | Valor |
|-------|-------|
| Código | IV-BR-005 |
| Nome | Validação Usa Somente o Disponível |
| Descrição | Toda validação de atendimento (para MMS-003) e toda nova reserva consideram exclusivamente o **saldo disponível** (total − reservado), nunca o saldo total (MMS-RG-09). |
| Tipo | Obrigatória |
| Validação | Cálculo do disponível no momento da validação/reserva, na mesma transação da decisão. |
| Mensagem de erro | "Saldo disponível insuficiente: há reservas ativas sobre este item." |
| Código do erro | IV-ERR-005 |
| Evento | — (regra de consumo) |
| Caso de uso | UC-IV-001, UC-IV-003 |
| API | GET /api/v1/inventory/balances, POST /api/v1/inventory/reservations |
| Caso de teste | TC-IV-005 |
| Configuração | Não configurável. |
| Observações | Garante que nenhum saldo seja prometido duas vezes (MMS-004-01, seção 3). |

---

## IV-BR-006 — Empresa Obrigatória e Isolada

| Campo | Valor |
|-------|-------|
| Código | IV-BR-006 |
| Nome | Empresa Obrigatória e Isolada |
| Descrição | Todo saldo, documento e local pertence a exatamente uma empresa; nenhuma consulta, regra, evento ou relatório cruza empresas. |
| Tipo | Obrigatória |
| Validação | Filtro obrigatório por `company_id` em toda operação; empresa existente e ativa (FD-001-02). |
| Mensagem de erro | — (registro fora do escopo responde 404) |
| Código do erro | IV-ERR-404 |
| Evento | — |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-IV-006 |
| Configuração | Não configurável. |
| Observações | Acesso direto a recurso de outra empresa responde 404 (anti-enumeração, padrão da suíte). |

---

## IV-BR-007 — Item Ativo para Novas Operações

| Campo | Valor |
|-------|-------|
| Código | IV-BR-007 |
| Nome | Item Ativo para Novas Operações |
| Descrição | Somente itens **Ativos** no Item Catalog (MMS-002) aceitam novas entradas (exceto devolução), reservas e transferências. Item inativado bloqueia novas reservas, mas o saldo remanescente segue movimentável até zerar (MMS-RG-08). |
| Tipo | Obrigatória |
| Validação | Estado do item consultado no MMS-002 no momento da operação; evento "Item inativado" bloqueia novas reservas do item. |
| Mensagem de erro | "Item inativo no catálogo: novas reservas bloqueadas. Saldo remanescente pode ser movimentado." |
| Código do erro | IV-ERR-007 |
| Evento | — (regra de consumo; consumo do evento Item inativado) |
| Caso de uso | UC-IV-003, UC-IV-005, UC-IV-006 |
| API | POST /api/v1/inventory/movements, /reservations |
| Caso de teste | TC-IV-007 |
| Configuração | Não configurável. |
| Observações | Devolução de item inativo é aceita (retorno ao estoque para baixa posterior). |

---

## IV-BR-008 — Local Válido e Ativo

| Campo | Valor |
|-------|-------|
| Código | IV-BR-008 |
| Nome | Local Válido e Ativo |
| Descrição | Toda movimentação referencia um local válido (depósito ou endereço, conforme granularidade configurada) existente e ativo na estrutura almoxarifado → depósito → endereço. |
| Tipo | Obrigatória |
| Validação | Existência e estado do local; coerência com a granularidade configurada (endereço obrigatório apenas quando a empresa opera com endereçamento). |
| Mensagem de erro | "Local inválido, inativo ou incompatível com a granularidade configurada." |
| Código do erro | IV-ERR-008 |
| Evento | — |
| Caso de uso | Todos os de movimentação |
| API | Todos os endpoints de escrita |
| Caso de teste | TC-IV-008 |
| Configuração | Granularidade (`materials.inventory.addressing.level`; padrão: `deposit` — sem endereço). |
| Observações | Estrutura de locais na família IV-BR-050. |

---

## IV-BR-009 — Quantidade Positiva na Unidade do Item

| Campo | Valor |
|-------|-------|
| Código | IV-BR-009 |
| Nome | Quantidade Positiva na Unidade do Item |
| Descrição | Toda quantidade movimentada é positiva e expressa na unidade de medida do item no catálogo (MMS-002); não há conversão de unidades no MVP. |
| Tipo | Obrigatória |
| Validação | Quantidade > 0; unidade herdada do item, sem conversão. |
| Mensagem de erro | "Quantidade inválida para a unidade do item." |
| Código do erro | IV-ERR-009 |
| Evento | — |
| Caso de uso | Todos os de movimentação |
| API | Todos os endpoints de escrita |
| Caso de teste | TC-IV-009 |
| Configuração | Não configurável. |
| Observações | Conversão de unidades é funcionalidade futura (roadmap). |

---

# 5. Regras de Entrada e Saída

## IV-BR-010 — Entrada com Documento de Origem

| Campo | Valor |
|-------|-------|
| Código | IV-BR-010 |
| Nome | Entrada com Documento de Origem |
| Descrição | Toda entrada referencia sua origem: recebimento conferido (MMS-005), devolução de solicitante (MMS-003), ajuste aprovado ou carga inicial. Compra dedicada gera entrada já reservada ao documento de origem. |
| Tipo | Obrigatória |
| Validação | Tipo de origem dentro do conjunto fechado {recebimento, devolução, ajuste, carga inicial}; referência obrigatória à origem. |
| Mensagem de erro | "Informe a origem da entrada." |
| Código do erro | IV-ERR-010 |
| Evento | Entrada registrada |
| Caso de uso | UC-IV-001 |
| API | POST /api/v1/inventory/movements (tipo entrada) |
| Caso de teste | TC-IV-010 |
| Configuração | Não configurável. |
| Observações | Compra dedicada com reserva automática na entrada: comportamento pleno na v1.1; no MVP, reserva vinculada manual ou via integração. |

---

## IV-BR-011 — Saída com Documento de Origem

| Campo | Valor |
|-------|-------|
| Código | IV-BR-011 |
| Nome | Saída com Documento de Origem |
| Descrição | Toda saída referencia sua origem: atendimento de solicitação (MMS-003), consumo ou ajuste aprovado. Saída por atendimento baixa a reserva vinculada no mesmo ato. |
| Tipo | Obrigatória |
| Validação | Tipo de origem dentro do conjunto fechado {atendimento, consumo, ajuste}; reserva vinculada obrigatória quando origem = atendimento. |
| Mensagem de erro | "Informe a origem da saída." |
| Código do erro | IV-ERR-011 |
| Evento | Saída registrada |
| Caso de uso | UC-IV-002 |
| API | POST /api/v1/inventory/movements (tipo saída) |
| Caso de teste | TC-IV-011 |
| Configuração | Não configurável. |
| Observações | A baixa da reserva na entrega é atômica com a saída (IV-BR-023). |

---

## IV-BR-012 — Confirmação Sequencial por Saldo

| Campo | Valor |
|-------|-------|
| Código | IV-BR-012 |
| Nome | Confirmação Sequencial por Saldo |
| Descrição | Movimentações que afetam o mesmo saldo (item × local × cliente/contrato) são confirmadas sequencialmente; a segunda operação concorrente aguarda ou falha com orientação de nova tentativa. |
| Tipo | Obrigatória |
| Validação | Serialização por chave de saldo na confirmação; optimistic concurrency nos documentos. |
| Mensagem de erro | "Este saldo está sendo movimentado. Tente novamente." |
| Código do erro | IV-ERR-012 |
| Evento | — |
| Caso de uso | Todos os de movimentação |
| API | Todos os endpoints de escrita |
| Caso de teste | TC-IV-012 |
| Configuração | Não configurável. |
| Observações | NFR de concorrência da visão do módulo. |

---

# 6. Regras de Reserva

## IV-BR-020 — Reserva Bloqueia o Disponível

| Campo | Valor |
|-------|-------|
| Código | IV-BR-020 |
| Nome | Reserva Bloqueia o Disponível |
| Descrição | A criação de reserva transfere quantidade do disponível para o reservado, no saldo do item × local (× cliente/contrato), em favor de uma solicitação (MMS-003) identificada. |
| Tipo | Obrigatória |
| Validação | Quantidade ≤ disponível (IV-BR-004/005); solicitação de origem identificada; item Ativo (IV-BR-007). |
| Mensagem de erro | "Saldo disponível insuficiente para a reserva." |
| Código do erro | IV-ERR-020 |
| Evento | Reserva criada |
| Caso de uso | UC-IV-003 |
| API | POST /api/v1/inventory/reservations |
| Caso de teste | TC-IV-020 |
| Configuração | Não configurável. |
| Observações | Reserva é documento de movimentação (IV-BR-002), com estado próprio (MMS-004-03). |

---

## IV-BR-021 — Validade de Reserva Parametrizável

| Campo | Valor |
|-------|-------|
| Código | IV-BR-021 |
| Nome | Validade de Reserva Parametrizável |
| Descrição | Toda reserva possui data/hora de validade calculada a partir de parâmetro da empresa; reserva vencida sem atendimento é liberada automaticamente (MMS-RG-03). |
| Tipo | Parametrizável |
| Validação | Validade = criação + `materials.inventory.reservation.ttl`; job de vencimento processa reservas expiradas. |
| Mensagem de erro | — (comportamento automático) |
| Código do erro | — |
| Evento | Reserva vencida / Reserva liberada |
| Caso de uso | UC-IV-004 |
| API | POST /api/v1/inventory/reservations (validade calculada) |
| Caso de teste | TC-IV-021 |
| Configuração | `materials.inventory.reservation.ttl` (padrão: 72h); janela do alerta pré-vencimento (`materials.inventory.reservation.expiring-window`; padrão: 24h). |
| Observações | O job nunca edita saldo diretamente: emite documento de liberação (IV-BR-001). |

---

## IV-BR-022 — Liberação Manual de Reserva

| Campo | Valor |
|-------|-------|
| Código | IV-BR-022 |
| Nome | Liberação Manual de Reserva |
| Descrição | Reserva ativa pode ser liberada manualmente (cancelamento da solicitação, desistência do atendimento), devolvendo a quantidade ao disponível. |
| Tipo | Obrigatória |
| Validação | Reserva em estado ativo; referência à solicitação de origem; motivo quando parametrizado. |
| Mensagem de erro | "A reserva não está ativa para liberação." |
| Código do erro | IV-ERR-022 |
| Evento | Reserva liberada |
| Caso de uso | UC-IV-004 |
| API | POST /api/v1/inventory/reservations/{id}/release |
| Caso de teste | TC-IV-022 |
| Configuração | Exigência de motivo (`materials.inventory.reservation.release-reason-required`; padrão: `false`). |
| Observações | Cancelamento da solicitação no MMS-003 dispara liberação automática vinculada. |

---

## IV-BR-023 — Baixa da Reserva na Entrega

| Campo | Valor |
|-------|-------|
| Código | IV-BR-023 |
| Nome | Baixa da Reserva na Entrega |
| Descrição | A entrega (saída por atendimento) baixa a reserva vinculada no mesmo ato atômico: o reservado diminui e o total diminui, sem passar pelo disponível. Entrega parcial baixa parcialmente; o excedente não atendido é liberado ou mantido conforme a solicitação. |
| Tipo | Obrigatória |
| Validação | Reserva ativa vinculada; quantidade entregue ≤ quantidade reservada remanescente; atomicidade saída + baixa. |
| Mensagem de erro | "Quantidade entregue excede a reserva vinculada." |
| Código do erro | IV-ERR-023 |
| Evento | Saída registrada / Reserva atendida |
| Caso de uso | UC-IV-002 |
| API | POST /api/v1/inventory/movements (saída com reserva) |
| Caso de teste | TC-IV-023 |
| Configuração | Não configurável. |
| Observações | Separação é etapa operacional do MMS-003; a baixa financeira do saldo ocorre aqui. |

---

# 7. Regras de Transferência

## IV-BR-030 — Transferência Atômica

| Campo | Valor |
|-------|-------|
| Código | IV-BR-030 |
| Nome | Transferência Atômica |
| Descrição | Toda transferência é um par indivisível: saída no local de origem + entrada no local de destino, no mesmo documento. Falha em qualquer lado estorna a operação inteira — nunca existe transferência "pela metade". |
| Tipo | Obrigatória |
| Validação | Origem e destino válidos e distintos; mesma empresa; quantidade ≤ disponível na origem; confirmação em transação única. |
| Mensagem de erro | "Transferência não confirmada: verifique origem, destino e saldo." |
| Código do erro | IV-ERR-030 |
| Evento | Transferência registrada |
| Caso de uso | UC-IV-005 |
| API | POST /api/v1/inventory/transfers |
| Caso de teste | TC-IV-030 |
| Configuração | Não configurável. |
| Observações | Transferência com recebimento em trânsito (estado "em trânsito") é v1.1 (roadmap da visão). |

---

## IV-BR-031 — Transferência Preserva Segregação

| Campo | Valor |
|-------|-------|
| Código | IV-BR-031 |
| Nome | Transferência Preserva Segregação |
| Descrição | A transferência não altera a segregação cliente/contrato do saldo: estoque dedicado transferido permanece dedicado ao mesmo cliente/contrato no destino. |
| Tipo | Obrigatória |
| Validação | Cliente/contrato de origem = cliente/contrato de destino no documento de transferência. |
| Mensagem de erro | "Transferência não pode alterar o cliente/contrato do estoque dedicado." |
| Código do erro | IV-ERR-031 |
| Evento | Transferência registrada |
| Caso de uso | UC-IV-005 |
| API | POST /api/v1/inventory/transfers |
| Caso de teste | TC-IV-031 |
| Configuração | Não configurável. |
| Observações | Mudança de dedicação, quando existir, será documento próprio (funcionalidade futura). |

---

# 8. Regras de Ajuste

## IV-BR-040 — Ajuste com Justificativa Obrigatória

| Campo | Valor |
|-------|-------|
| Código | IV-BR-040 |
| Nome | Ajuste com Justificativa Obrigatória |
| Descrição | Todo ajuste de saldo (positivo ou negativo) exige justificativa registrada, tipo de motivo (divergência de inventário, perda, avaria, achado, erro de lançamento) e documento de referência quando houver. |
| Tipo | Obrigatória |
| Validação | Justificativa não vazia; tipo de motivo dentro do conjunto configurado; ajuste negativo respeita IV-BR-004. |
| Mensagem de erro | "Informe a justificativa do ajuste." |
| Código do erro | IV-ERR-040 |
| Evento | Ajuste registrado |
| Caso de uso | UC-IV-006 |
| API | POST /api/v1/inventory/adjustments |
| Caso de teste | TC-IV-040 |
| Configuração | Conjunto de motivos (`materials.inventory.adjustment.reasons`; padrão: divergência de inventário, perda, avaria, achado, erro de lançamento). |
| Observações | Ajuste é a única forma de corrigir divergência físico × sistema (MMS-004-01, seção 3). |

---

## IV-BR-041 — Aprovação de Ajuste Parametrizada

| Campo | Valor |
|-------|-------|
| Código | IV-BR-041 |
| Nome | Aprovação de Ajuste Parametrizada |
| Descrição | Quando parametrizado, o ajuste só é confirmado após aprovação (workflow FD-001-04). Quem registra o ajuste **nunca aprova o próprio ajuste** — segregação de funções garantida em qualquer modo (MMS-RG-05). |
| Tipo | Parametrizável |
| Validação | `materials.inventory.adjustment.approval-required=true` → ajuste nasce pendente de aprovação; aprovador ≠ registrante sempre. |
| Mensagem de erro | "Ajuste aguardando aprovação." / "O registrante não pode aprovar o próprio ajuste." |
| Código do erro | IV-ERR-041 |
| Evento | Ajuste registrado / Ajuste aprovado / Ajuste rejeitado |
| Caso de uso | UC-IV-006 |
| API | POST /api/v1/inventory/adjustments/{id}/approve, /reject |
| Caso de teste | TC-IV-041 |
| Configuração | `materials.inventory.adjustment.approval-required` (padrão: `true`). |
| Observações | Ajuste rejeitado não movimenta saldo; rejeição exige motivo. |

---

## IV-BR-042 — Ajuste Auditado com Saldos

| Campo | Valor |
|-------|-------|
| Código | IV-BR-042 |
| Nome | Ajuste Auditado com Saldos |
| Descrição | O registro de auditoria do ajuste contém obrigatoriamente saldo anterior e posterior, justificativa, registrante, aprovador (quando houver) e vínculo com o inventário que o originou, quando aplicável. |
| Tipo | Obrigatória |
| Validação | Presença dos campos no registro de auditoria (FD-001-06). |
| Mensagem de erro | — (regra de evidência) |
| Código do erro | — |
| Evento | Ajuste registrado / Ajuste aprovado |
| Caso de uso | UC-IV-006 |
| API | GET /api/v1/inventory/movements/{id} |
| Caso de teste | TC-IV-042 |
| Configuração | Não configurável. |
| Observações | Instância específica da regra geral IV-BR-090. |

---

# 9. Regras de Estrutura de Locais

## IV-BR-050 — Estrutura Almoxarifado → Depósito → Endereço

| Campo | Valor |
|-------|-------|
| Código | IV-BR-050 |
| Nome | Estrutura de Locais em Três Níveis |
| Descrição | A estrutura de locais segue a hierarquia almoxarifado → depósito → endereço. Saldo existe por item × depósito (mínimo) e, quando configurado, por item × endereço. |
| Tipo | Obrigatória |
| Validação | Hierarquia íntegra: endereço pertence a depósito, depósito a almoxarifado, almoxarifado a empresa/unidade (FD-001-02). |
| Mensagem de erro | "Estrutura de local inválida." |
| Código do erro | IV-ERR-050 |
| Evento | — (estrutura administrativa) |
| Caso de uso | UC-IV-009 |
| API | POST /api/v1/inventory/locations |
| Caso de teste | TC-IV-050 |
| Configuração | Não configurável (modelo da suíte). |
| Observações | Depósito é o nível mínimo obrigatório de saldo (visão do módulo, escopo 3). |

---

## IV-BR-051 — Granularidade de Endereçamento Parametrizável

| Campo | Valor |
|-------|-------|
| Código | IV-BR-051 |
| Nome | Granularidade de Endereçamento Parametrizável |
| Descrição | A empresa escolhe operar até depósito ou até endereço. Mudança de granularidade para endereço exige endereçamento dos saldos existentes; para depósito, exige consolidação documentada. |
| Tipo | Parametrizável |
| Validação | Transição de granularidade apenas com saldos adequados ao novo nível; operação administrativa auditada. |
| Mensagem de erro | "Existem saldos incompatíveis com a granularidade desejada." |
| Código do erro | IV-ERR-051 |
| Evento | — (configuração) |
| Caso de uso | UC-IV-009 |
| API | Configuração do módulo |
| Caso de teste | TC-IV-051 |
| Configuração | `materials.inventory.addressing.level` (padrão: `deposit`; opção: `address`). |
| Observações | Evita endereçamento sem disciplina de uso (risco do MMS-004-01, seção 20). |

---

## IV-BR-052 — Local Inativado sem Saldo

| Campo | Valor |
|-------|-------|
| Código | IV-BR-052 |
| Nome | Local Inativado sem Saldo |
| Descrição | Depósito ou endereço com saldo (total ou reservado) não pode ser inativado; a inativação exige saldo zerado por movimentação documentada. |
| Tipo | Obrigatória |
| Validação | Saldo zero no local antes da inativação. |
| Mensagem de erro | "O local possui saldo e não pode ser inativado." |
| Código do erro | IV-ERR-052 |
| Evento | — (administrativa) |
| Caso de uso | UC-IV-009 |
| API | POST /api/v1/inventory/locations/{id}/inactivate |
| Caso de teste | TC-IV-052 |
| Configuração | Não configurável. |
| Observações | Local inativo não aceita novas movimentações (IV-BR-008). |

---

# 10. Regras de Segregação

## IV-BR-060 — Estoque Dedicado por Cliente/Contrato

| Campo | Valor |
|-------|-------|
| Código | IV-BR-060 |
| Nome | Estoque Dedicado por Cliente/Contrato |
| Descrição | Saldo pode ser segregado por cliente/contrato. Estoque dedicado só atende reservas e saídas do mesmo cliente/contrato; o bloqueio ocorre na validação, não na conferência (MMS-RG-10). |
| Tipo | Obrigatória |
| Validação | Verificação de compatibilidade cliente/contrato em toda reserva e saída; saldo dedicado invisível para demandas de outros clientes/contratos. |
| Mensagem de erro | "Saldo disponível apenas para outro cliente/contrato." |
| Código do erro | IV-ERR-060 |
| Evento | — (bloqueio auditado) |
| Caso de uso | UC-IV-001, UC-IV-002, UC-IV-003 |
| API | POST /api/v1/inventory/reservations, /movements |
| Caso de teste | TC-IV-060 |
| Configuração | Uso de segregação (`materials.inventory.segregation.enabled`; padrão: `false` — empresa sem estoque dedicado). |
| Observações | Saldo comum (sem dedicação) atende qualquer demanda da empresa. |

---

# 11. Regras de Inventário (Contagem Física)

## IV-BR-070 — Inventário Cíclico ou Geral

| Campo | Valor |
|-------|-------|
| Código | IV-BR-070 |
| Nome | Inventário Cíclico ou Geral |
| Descrição | O inventário é aberto por ciclo (curva ABC — classe e período parametrizáveis) ou geral (todos os itens/locais do escopo). Todo inventário identifica escopo, responsável e prazo. |
| Tipo | Parametrizável |
| Validação | Escopo não vazio; classe ABC conforme classificação do item (MMS-002); somente um inventário aberto por escopo sobreposto. |
| Mensagem de erro | "Já existe inventário aberto para este escopo." |
| Código do erro | IV-ERR-070 |
| Evento | Inventário iniciado |
| Caso de uso | UC-IV-007 |
| API | POST /api/v1/inventory/counts |
| Caso de teste | TC-IV-070 |
| Configuração | Frequência por classe (`materials.inventory.cycle-count.frequency.*`; padrão: A=30d, B=90d, C=180d). |
| Observações | Inventário cíclico automático (abertura por agenda) é v1.1; no MVP a abertura é manual. |

---

## IV-BR-071 — Contagem Registrada por Item × Local

| Campo | Valor |
|-------|-------|
| Código | IV-BR-071 |
| Nome | Contagem Registrada por Item × Local |
| Descrição | A contagem física é registrada por item × local do escopo, com quantidade contada, contador e data/hora. Contagem registrada não altera saldo — apenas alimenta a divergência. |
| Tipo | Obrigatória |
| Validação | Quantidade ≥ 0; item e local dentro do escopo do inventário aberto. |
| Mensagem de erro | "Contagem fora do escopo do inventário." |
| Código do erro | IV-ERR-071 |
| Evento | Contagem registrada |
| Caso de uso | UC-IV-007 |
| API | POST /api/v1/inventory/counts/{id}/entries |
| Caso de teste | TC-IV-071 |
| Configuração | Não configurável. |
| Observações | Contagem cega (sem exibição do saldo do sistema ao contador) é parâmetro futuro. |

---

## IV-BR-072 — Divergência Tratada por Ajuste Aprovado

| Campo | Valor |
|-------|-------|
| Código | IV-BR-072 |
| Nome | Divergência Tratada por Ajuste Aprovado |
| Descrição | Divergência (contagem ≠ saldo) acima da tolerância parametrizada gera proposta de ajuste vinculada ao inventário, que segue o fluxo de ajuste (IV-BR-040/041). Divergência dentro da tolerância é registrada sem ajuste. |
| Tipo | Parametrizável |
| Validação | Tolerância por quantidade e/ou percentual; ajuste gerado sempre vinculado ao inventário e à contagem. |
| Mensagem de erro | — (comportamento de fluxo) |
| Código do erro | — |
| Evento | Divergência aprovada (via Ajuste aprovado) |
| Caso de uso | UC-IV-007 |
| API | POST /api/v1/inventory/counts/{id}/close |
| Caso de teste | TC-IV-072 |
| Configuração | Tolerâncias (`materials.inventory.count.tolerance.qty` e `.percent`; padrão: 0 e 0%). |
| Observações | O inventário só é encerrado com todas as divergências tratadas ou dispensadas com motivo. |

---

## IV-BR-073 — Movimentação Durante Inventário

| Campo | Valor |
|-------|-------|
| Código | IV-BR-073 |
| Nome | Movimentação Durante Inventário |
| Descrição | Item × local em contagem registrada aguardando fechamento tem novas movimentações sinalizadas ao responsável; o bloqueio total da movimentação durante o inventário é parametrizável. |
| Tipo | Parametrizável |
| Validação | Quando `freeze=true`, movimentações no escopo são recusadas até o encerramento; quando `false`, a divergência é calculada contra o saldo no momento da contagem. |
| Mensagem de erro | "Local em inventário: movimentação bloqueada até o encerramento." |
| Código do erro | IV-ERR-073 |
| Evento | — |
| Caso de uso | UC-IV-007 |
| API | Todos os endpoints de escrita |
| Caso de teste | TC-IV-073 |
| Configuração | `materials.inventory.count.freeze` (padrão: `false`). |
| Observações | O saldo de referência da divergência é sempre o do instante da contagem registrada. |

---

# 12. Regras de Alertas

## IV-BR-080 — Alerta de Estoque Mínimo

| Campo | Valor |
|-------|-------|
| Código | IV-BR-080 |
| Nome | Alerta de Estoque Mínimo |
| Descrição | Quando o disponível de um item estocável fica abaixo do mínimo parametrizado (MMS-002), o sistema emite alerta ao responsável pelo ressuprimento, via Notification Center (MMS-RG-12). |
| Tipo | Obrigatória |
| Validação | Avaliação após toda confirmação de movimentação que reduz disponível; alerta por item × depósito; sem duplicidade de alerta aberto para a mesma condição. |
| Mensagem de erro | — (alerta, não erro) |
| Código do erro | — |
| Evento | Alerta de estoque mínimo |
| Caso de uso | UC-IV-010 |
| API | GET /api/v1/inventory/alerts |
| Caso de teste | TC-IV-080 |
| Configuração | Destinatários por tipo de alerta (`materials.inventory.alerts.recipients.*`). |
| Observações | Item sem mínimo parametrizado não gera alerta (saneamento é KPI do MMS-002). |

---

## IV-BR-081 — Alerta de Ruptura

| Campo | Valor |
|-------|-------|
| Código | IV-BR-081 |
| Nome | Alerta de Ruptura |
| Descrição | Quando um item com demanda aberta (solicitação aprovada não atendida) fica com disponível zero, o sistema emite alerta de ruptura com prioridade elevada (MMS-RG-12). |
| Tipo | Obrigatória |
| Validação | Condição: disponível = 0 ∧ demanda aberta (informada pelo MMS-003); emissão imediata na ocorrência da condição. |
| Mensagem de erro | — (alerta, não erro) |
| Código do erro | — |
| Evento | Alerta de ruptura |
| Caso de uso | UC-IV-010 |
| API | GET /api/v1/inventory/alerts |
| Caso de teste | TC-IV-081 |
| Configuração | Destinatários (`materials.inventory.alerts.recipients.*`). |
| Observações | Ruptura é KPI da suíte (MMS-001, seção 20) e gatilho emergencial (MMS-004-01, seção 13). |

---

## IV-BR-082 — Alerta de Reserva a Vencer

| Campo | Valor |
|-------|-------|
| Código | IV-BR-082 |
| Nome | Alerta de Reserva a Vencer |
| Descrição | Reserva ativa que entra na janela pré-vencimento gera alerta ao almoxarifado e ao solicitante (via MMS-003), antes da liberação automática (IV-BR-021). |
| Tipo | Obrigatória |
| Validação | Avaliação periódica das reservas ativas dentro da janela configurada; um alerta por reserva. |
| Mensagem de erro | — (alerta, não erro) |
| Código do erro | — |
| Evento | — (notificação; evento de domínio ocorre no vencimento) |
| Caso de uso | UC-IV-010 |
| API | GET /api/v1/inventory/reservations?expiring=true |
| Caso de teste | TC-IV-082 |
| Configuração | Janela (`materials.inventory.reservation.expiring-window`; padrão: 24h). |
| Observações | KPI "reservas vencidas sem atendimento" mede a falha que este alerta previne. |

---

## IV-BR-083 — Alertas Nunca Bloqueiam a Movimentação

| Campo | Valor |
|-------|-------|
| Código | IV-BR-083 |
| Nome | Alertas Nunca Bloqueiam a Movimentação |
| Descrição | A emissão de qualquer alerta é assíncrona e nunca condiciona a confirmação da movimentação principal; falha de notificação não afeta o saldo. |
| Tipo | Obrigatória |
| Validação | Despacho exclusivo via FD-001-05 após a confirmação; isolamento de falha. |
| Mensagem de erro | — (NFR) |
| Código do erro | — |
| Evento | Todos os alertas |
| Caso de uso | UC-IV-010 |
| API | — |
| Caso de teste | TC-IV-083 |
| Configuração | Não configurável (NFR da visão). |
| Observações | Princípio 8 do MMS-004-01. |

---

# 13. Regras de Auditoria e Timeline

## IV-BR-090 — Auditoria com Saldo Anterior/Posterior

| Campo | Valor |
|-------|-------|
| Código | IV-BR-090 |
| Nome | Auditoria com Saldo Anterior/Posterior |
| Descrição | 100% das movimentações geram registro de auditoria imutável com documento, responsável, data/hora, correlationId e **saldo anterior/posterior** do saldo afetado (MMS-P-02; MMS-001, seção 18). Falha de auditoria aborta a operação. |
| Tipo | Obrigatória |
| Validação | Registro no FD-001-06 na mesma transação da confirmação. |
| Mensagem de erro | — (falha aborta a operação) |
| Código do erro | — |
| Evento | Todos os eventos do módulo |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-IV-090 |
| Configuração | Não configurável. |
| Observações | Saldo anterior/posterior é a assinatura de auditoria específica deste módulo (visão, Objetivos do MVP 7). |

---

## IV-BR-091 — Timeline por Documento e por Item

| Campo | Valor |
|-------|-------|
| Código | IV-BR-091 |
| Nome | Timeline por Documento e por Item |
| Descrição | Marcos (confirmação, estorno, reserva, vencimento, ajuste, inventário) aparecem na timeline do documento e do item (FD-001-07), com paginação keyset; falha de timeline nunca bloqueia a operação (MMS-P-03). |
| Tipo | Obrigatória |
| Validação | Entrada de timeline por marco. |
| Mensagem de erro | — (não bloqueia o fluxo) |
| Código do erro | — |
| Evento | Todos os eventos do módulo |
| Caso de uso | UC-IV-011 |
| API | GET /api/v1/inventory/movements/{id}/timeline, GET /api/v1/inventory/balances/{itemId}/timeline |
| Caso de teste | TC-IV-091 |
| Configuração | Não configurável. |
| Observações | Extrato de movimentações (visão, escopo 7) é a visão operacional desta trilha. |

---

# 14. Regras de Segurança

## IV-BR-100 — Permissões por Papel e Segregação

| Campo | Valor |
|-------|-------|
| Código | IV-BR-100 |
| Nome | Permissões por Papel e Segregação |
| Descrição | Consulta de saldo/extrato é ampla aos papéis autorizados; movimentação é restrita ao Almoxarife e integrações; aprovação de ajuste ao Supervisor/Gerente; configuração ao Administrador; deny by default; quem ajusta não aprova (IV-BR-041); menus sem permissão são ocultados (MMS-001, seção 23). |
| Tipo | Obrigatória |
| Validação | Fluxo de autorização do Foundation (escopo → RBAC → ABAC), com negação auditada. |
| Mensagem de erro | "Você não tem permissão para esta operação." |
| Código do erro | IV-ERR-100 |
| Evento | — (negação auditada) |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-IV-100 |
| Configuração | Não configurável (matriz detalhada no MMS-004-09). |
| Observações | A visão do almoxarifado (fila de solicitações) é exclusiva dos papéis de almoxarifado — solicitantes não a acessam (MMS-003). |

---

## IV-BR-101 — Escopo Organizacional

| Campo | Valor |
|-------|-------|
| Código | IV-BR-101 |
| Nome | Escopo Organizacional |
| Descrição | Operações respeitam o escopo organizacional do usuário (empresa/unidade/almoxarifado autorizados, FD-001-01/02): o almoxarife opera os almoxarifados do seu escopo; a gerência consulta o seu. |
| Tipo | Obrigatória |
| Validação | Escopo avaliado em toda operação. |
| Mensagem de erro | "Operação fora do seu escopo organizacional." |
| Código do erro | IV-ERR-101 |
| Evento | — (negação auditada) |
| Caso de uso | Todos |
| API | Todos os endpoints |
| Caso de teste | TC-IV-101 |
| Configuração | Não configurável. |
| Observações | MMS-001, seção 23. |

---

# 15. Regras de Performance e Disponibilidade

## IV-BR-110 — Validação e Consulta de Saldo < 2s

| Campo | Valor |
|-------|-------|
| Código | IV-BR-110 |
| Nome | Validação e Consulta de Saldo < 2s |
| Descrição | Validação de estoque (para MMS-003) e consulta de saldo/posição respondem em menos de 2 segundos na carga de referência; leitura com cache e invalidação por evento. |
| Tipo | Obrigatória |
| Validação | Índices por (empresa, item, local); paginação keyset no extrato; cache de leitura; percentil 95 medido. |
| Mensagem de erro | — (NFR) |
| Código do erro | — |
| Evento | Invalidação por eventos de movimentação |
| Caso de uso | UC-IV-001, UC-IV-011 |
| API | GET /api/v1/inventory/balances |
| Caso de teste | TC-IV-110 |
| Configuração | TTL de cache (`materials.inventory.cache.ttl`); tamanho de página (`materials.inventory.search.page-size`). |
| Observações | NFR da visão do módulo. |

---

## IV-BR-111 — Extrato com Paginação Keyset

| Campo | Valor |
|-------|-------|
| Código | IV-BR-111 |
| Nome | Extrato com Paginação Keyset |
| Descrição | O extrato de movimentações (por item, por documento, por local) é paginado por keyset, ordenado por data/hora decrescente, sem offset. |
| Tipo | Obrigatória |
| Validação | Cursor opaco na resposta; ordenação estável. |
| Mensagem de erro | "Cursor de paginação inválido ou expirado." |
| Código do erro | IV-ERR-111 |
| Evento | — |
| Caso de uso | UC-IV-011 |
| API | GET /api/v1/inventory/movements |
| Caso de teste | TC-IV-111 |
| Configuração | `materials.inventory.search.page-size` (padrão: 50). |
| Observações | Padrão da suíte para listagens grandes. |

---

# 16. Regras de EPI e Fardamento

Família introduzida na concepção do módulo (MMS-002-02, IC-BR-081: "o tamanho acompanha reserva, separação e entrega (MMS-004)"; "o saldo por tamanho é responsabilidade do MMS-004"). Aplica-se a itens cuja categoria pertença aos grupos EPI ou Fardamento e que referenciem grade de tamanhos.

## IV-BR-120 — Saldo por Tamanho quando Item tem Grade

| Campo | Valor |
|-------|-------|
| Código | IV-BR-120 |
| Nome | Saldo por Tamanho quando Item tem Grade |
| Descrição | Item com grade de tamanhos (MMS-002, IC-BR-081) tem saldo controlado por item × tamanho × local. Toda movimentação desse item exige o tamanho, que deve pertencer à grade vigente do item. |
| Tipo | Obrigatória |
| Validação | Tamanho obrigatório para item com grade; tamanho ∈ grade do item (Master Data, vigência na data de referência); todas as regras de saldo aplicam-se ao saldo do tamanho. |
| Mensagem de erro | "Informe um tamanho válido da grade do item." |
| Código do erro | IV-ERR-120 |
| Evento | — (regra estrutural aplicada a todos os documentos) |
| Caso de uso | Todos os de movimentação |
| API | Todos os endpoints de escrita (campo `sizeCode`) |
| Caso de teste | TC-IV-120 |
| Configuração | Não configurável (decorrente do cadastro do item). |
| Observações | Item sem grade movimenta sem tamanho; as duas formas convivem no mesmo módulo. |

---

## IV-BR-121 — Tamanho Acompanha Reserva, Separação e Entrega

| Campo | Valor |
|-------|-------|
| Código | IV-BR-121 |
| Nome | Tamanho Acompanha Reserva, Separação e Entrega |
| Descrição | O tamanho informado na solicitação (MMS-003) propaga-se obrigatoriamente pela reserva, pela separação e pela entrega; troca de tamanho no atendimento exige liberação da reserva original e nova reserva. |
| Tipo | Obrigatória |
| Validação | Tamanho da reserva = tamanho da saída por atendimento; divergência bloqueia a baixa (IV-BR-023). |
| Mensagem de erro | "O tamanho da entrega difere do tamanho reservado." |
| Código do erro | IV-ERR-121 |
| Evento | — (bloqueio auditado) |
| Caso de uso | UC-IV-002, UC-IV-003 |
| API | POST /api/v1/inventory/reservations, /movements |
| Caso de teste | TC-IV-121 |
| Configuração | Não configurável. |
| Observações | Origem: MMS-002-02, IC-BR-081 (observação de fronteira com este módulo). |

---

# 17. Matriz de Rastreabilidade

| Regra | Origem (documento) | UC | API (conceitual) | Evento | Teste |
|-------|--------------------|-----|------------------|--------|-------|
| IV-BR-001 | MMS-P-08 / MMS-004 README (restrição 1) | Todos | Escrita | — | TC-IV-001 |
| IV-BR-002 | MMS-P-07 / MMS-004 README (restrição 2) | Todos | Escrita | — | TC-IV-002 |
| IV-BR-003 | MMS-001 §15.2 / MMS-004 README (restrição 3) | UC-IV-008 | POST .../reverse | Estorno registrado | TC-IV-003 |
| IV-BR-004 | MMS-RG-04 | UC-IV-002/003/005/006 | POST /movements | — | TC-IV-004 |
| IV-BR-005 | MMS-RG-09 | UC-IV-001/003 | GET /balances, POST /reservations | — | TC-IV-005 |
| IV-BR-006 | MMS-001 §6.2 | Todos | Todos | — | TC-IV-006 |
| IV-BR-007 | MMS-RG-08 / MMS-004 README (eventos consumidos) | UC-IV-003/005/006 | POST /movements, /reservations | — | TC-IV-007 |
| IV-BR-008 | MMS-004 README (escopo 3) | Todos movimentação | Escrita | — | TC-IV-008 |
| IV-BR-009 | MMS-002 (unidade do item) | Todos movimentação | Escrita | — | TC-IV-009 |
| IV-BR-010 | MMS-004 README (escopo 2) | UC-IV-001 | POST /movements | Entrada registrada | TC-IV-010 |
| IV-BR-011 | MMS-004 README (escopo 2) | UC-IV-002 | POST /movements | Saída registrada | TC-IV-011 |
| IV-BR-012 | MMS-004 README (NFR) | Todos movimentação | Escrita | — | TC-IV-012 |
| IV-BR-020 | MMS-004 README (escopo 2) | UC-IV-003 | POST /reservations | Reserva criada | TC-IV-020 |
| IV-BR-021 | MMS-RG-03 | UC-IV-004 | POST /reservations | Reserva vencida/liberada | TC-IV-021 |
| IV-BR-022 | MMS-004 README (escopo 2) | UC-IV-004 | POST .../release | Reserva liberada | TC-IV-022 |
| IV-BR-023 | MMS-004 README (escopo 2 / integração MMS-003) | UC-IV-002 | POST /movements | Saída registrada/Reserva atendida | TC-IV-023 |
| IV-BR-030 | MMS-004 README (NFR — consistência) | UC-IV-005 | POST /transfers | Transferência registrada | TC-IV-030 |
| IV-BR-031 | MMS-RG-10 | UC-IV-005 | POST /transfers | Transferência registrada | TC-IV-031 |
| IV-BR-040 | MMS-RG-05 / MMS-004 README (escopo 2) | UC-IV-006 | POST /adjustments | Ajuste registrado | TC-IV-040 |
| IV-BR-041 | MMS-RG-05 / FD-001-04 | UC-IV-006 | POST .../approve, /reject | Ajuste aprovado/rejeitado | TC-IV-041 |
| IV-BR-042 | MMS-004 README (MVP 7) | UC-IV-006 | GET /movements/{id} | Ajuste registrado | TC-IV-042 |
| IV-BR-050 | MMS-004 README (escopo 3) | UC-IV-009 | POST /locations | — | TC-IV-050 |
| IV-BR-051 | MMS-004 README (escopo 3) | UC-IV-009 | Configuração | — | TC-IV-051 |
| IV-BR-052 | MMS-004 README (escopo 3) | UC-IV-009 | POST .../inactivate | — | TC-IV-052 |
| IV-BR-060 | MMS-RG-10 | UC-IV-001/002/003 | POST /reservations, /movements | — | TC-IV-060 |
| IV-BR-070 | MMS-004 README (escopo 6) | UC-IV-007 | POST /counts | Inventário iniciado | TC-IV-070 |
| IV-BR-071 | MMS-004 README (escopo 6) | UC-IV-007 | POST .../entries | Contagem registrada | TC-IV-071 |
| IV-BR-072 | MMS-004 README (escopo 6) | UC-IV-007 | POST .../close | Divergência aprovada | TC-IV-072 |
| IV-BR-073 | MMS-004 README (escopo 6) | UC-IV-007 | Escrita | — | TC-IV-073 |
| IV-BR-080 | MMS-RG-12 | UC-IV-010 | GET /alerts | Alerta de estoque mínimo | TC-IV-080 |
| IV-BR-081 | MMS-RG-12 | UC-IV-010 | GET /alerts | Alerta de ruptura | TC-IV-081 |
| IV-BR-082 | MMS-RG-03 / MMS-RG-12 | UC-IV-010 | GET /reservations?expiring | — | TC-IV-082 |
| IV-BR-083 | MMS-004 README (NFR — disponibilidade) | UC-IV-010 | — | Todos alertas | TC-IV-083 |
| IV-BR-090 | MMS-P-02 / MMS-001 §18 | Todos | Todos | Todos | TC-IV-090 |
| IV-BR-091 | MMS-P-03 | UC-IV-011 | GET .../timeline | Todos | TC-IV-091 |
| IV-BR-100 | MMS-001 §23 / FD-001-01 | Todos | Todos | — | TC-IV-100 |
| IV-BR-101 | MMS-001 §23 | Todos | Todos | — | TC-IV-101 |
| IV-BR-110 | MMS-004 README (NFR — performance) | UC-IV-001/011 | GET /balances | — | TC-IV-110 |
| IV-BR-111 | Padrão suíte (keyset) | UC-IV-011 | GET /movements | — | TC-IV-111 |
| IV-BR-120 | MMS-002-02 IC-BR-081 (fronteira) | Todos movimentação | Escrita (sizeCode) | — | TC-IV-120 |
| IV-BR-121 | MMS-002-02 IC-BR-081 (fronteira) | UC-IV-002/003 | POST /reservations, /movements | — | TC-IV-121 |

**Cobertura:** 40/40 regras com origem documentada e teste associado (100%).

---

# 18. Dependências

| Dependência | Uso |
|-------------|-----|
| MMS-004 (Visão) / MMS-004-01 (Business Context) | Origem de todas as regras |
| MMS-001 (Documento Mestre) | Regras MMS-RG-03/04/05/08/09/10/12 e princípios MMS-P-01..08 herdados |
| MMS-002 (Item Catalog) | Estado do item (IV-BR-007), unidade (IV-BR-009), parâmetros de reposição (IV-BR-080), grade de tamanhos (IV-BR-120/121) |
| FD-001-01 / FD-001-02 | Autorização, segregação de funções, escopo e estrutura organizacional |
| FD-001-04 | Workflow de aprovação de ajustes (IV-BR-041) |
| FD-001-05 | Despacho exclusivo de alertas (IV-BR-080..083) |
| FD-001-06 / FD-001-07 | Auditoria e timeline (IV-BR-090/091) |
| FD-001-10 | Todos os parâmetros `materials.inventory.*` |
| MMS-004-07 / MMS-004-13 / MMS-004-17 | Especificação de UCs, API e testes (documentos seguintes do pacote) |

---

# 19. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação das Business Rules do Inventory Management: 40 regras codificadas (IV-BR-001..121) nas famílias integridade de saldo, entrada/saída, reserva, transferência, ajuste, estrutura de locais, segregação, inventário, alertas, auditoria/timeline, segurança, performance e EPI/Fardamento, com validação, erros (IV-ERR), eventos, UCs/API/testes conceituais, configurações `materials.inventory.*` e matriz de rastreabilidade 100% — derivadas da visão do módulo e das regras da suíte (MMS-RG), no padrão MMS-002-02 |
