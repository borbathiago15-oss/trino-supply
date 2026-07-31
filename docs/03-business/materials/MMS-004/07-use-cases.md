# MMS-004-07 — Use Cases

**Documento:** MMS-004-07 — Use Cases
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-004 v1.0.0, MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-04 (Domain Model), MMS-004-05 (Event Storming), MMS-004-06 (BPMN), MMS-002, MMS-003, MMS-005, FD-001-04, FD-001-05, FD-001-09
**Referências:** MMS-002-07 (padrão de formato Enterprise), PR-001-07, MMS-004-09 Permissions (a produzir), MMS-004-13 API (a produzir), GOV-001

> Especificação dos Casos de Uso do módulo Inventory Management.
> Cada caso de uso segue especificação UML completa: fluxo principal, fluxos alternativos, fluxos de exceção, pré/pós-condições, regras, eventos, mensagens, validações, APIs, permissões e testes relacionados.

---

# 0. Convenções da Especificação

| Campo | Significado |
| ----- | ----------- |
| **APIs** | Endpoints REST sob `/api/v1/inventory` (contrato detalhado em MMS-004-13). Todas exigem autenticação JWT e propagam `X-Correlation-Id`; escritas exigem `Idempotency-Key` |
| **Permissões** | Códigos IV-PERM conforme MMS-004-09; toda decisão registra auditoria de autorização |
| **Eventos** | Códigos EVT-IV conforme MMS-004-05 §14 (payload e garantias) |
| **Mensagens** | Mensagens de negócio exibidas ao usuário (chaves i18n `iv.uc.*`) |
| **Testes** | Códigos TC-IV-xxx-y: cenários de teste de aceite vinculados ao caso de uso (detalhados em MMS-004-17) |
| **Erros** | Códigos IV-ERR conforme MMS-004-02 (Business Rules); IV-ERR-090 transição inválida, IV-ERR-409 conflito de versão, IV-ERR-404 anti-enumeração, IV-ERR-900 permissão insuficiente |
| **Estados** | Documento: ST-IV-001 Rascunho, ST-IV-002 Confirmado, ST-IV-003 Estornado, ST-IV-004 Cancelado. Reserva: ST-IV-010 Ativa, ST-IV-011 Atendida, ST-IV-012 Liberada, ST-IV-013 Vencida. Ajuste: ST-IV-020 Pendente, ST-IV-021 Aprovado, ST-IV-022 Rejeitado, ST-IV-023 Estornado. Inventário: ST-IV-030 Aberto, ST-IV-031 Em Contagem, ST-IV-032 Fechado, ST-IV-033 Cancelado — MMS-004-03 |
| **Saldo** | Projeção derivada exclusivamente de documentos Confirmados (MMS-P-08; INV-IV-01). Nenhum UC escreve saldo diretamente; o efeito é aplicado pelo StockBalanceService na confirmação |

---

# UC-IV-001 — Registrar Entrada de Estoque

## Objetivo

Registrar a entrada de itens no estoque, efetivando o saldo, com origem em recebimento conferido (MMS-005) ou entrada avulsa.

## Atores

- Almoxarife / Supervisor de Almoxarifado (primário, entrada avulsa)
- Sistema (primário, origem MMS-005; secundário — validações, efeito de saldo, eventos)

## Pré-condições

- Itens ativos no catálogo (MMS-002).
- Locais de destino ativos e pertencentes à empresa.
- Origem MMS-005: conferência de recebimento concluída com evento `ReceivingConferenceCompleted` publicado.
- Origem manual: usuário autenticado com permissão IV-PERM-001.

## Pós-condições

- Documento de entrada em estado **Confirmado** (ST-IV-002).
- Saldo efetivado por linha (saldo anterior/posterior registrado — IV-BR-095).
- Evento EVT-IV-001 `StockEntryRegistered` publicado.
- Alertas de mínimo/ruptura reavaliados (POL-IV-09).

## Gatilho

- Externo: MSG-IV-C01 `ReceivingConferenceCompleted` (MMS-004-06 §3, Fluxo A).
- Manual: Almoxarife seleciona "Nova entrada".

## Fluxo Principal

1. Sistema (origem MMS-005) gera o documento de entrada em Rascunho com itens, quantidades, local e referência ao recebimento; ou Almoxarife (origem manual) informa os mesmos dados.
2. Sistema executa a bateria de validação (Sub-Processo SP-IV-01): item ativo, quantidade > 0, local ativo, tamanho quando o item possui grade, idempotência da origem.
3. Sistema confirma o documento e aplica o efeito de saldo linha a linha (transacional).
4. Sistema publica EVT-IV-001 e reavalia alertas do item.

**Detalhamento do passo 2:** a chave de saldo é (empresa, item, local, tamanho quando aplicável) — IV-BR-001/120. Entrada com item de grade exige tamanho por linha.

**Detalhamento do passo 3:** atomicidade total (INV-IV-04): ou todas as linhas confirmam, ou nenhuma.

## Fluxos Alternativos

A1. Entrada parcial de recebimento com divergência.
- A1.1. O recebimento conferido com divergência (MMS-005) gera entrada apenas das quantidades aceitas; divergências seguem o fluxo de destino documentado do MMS-005 (MMS-RG-07). O documento de entrada referencia a conferência.

A2. Entrada avulsa com documento de referência externo.
- A2.1. Almoxarife informa número de nota/documento externo no campo de referência (obrigatório para rastreabilidade — IV-BR-013).

A3. Entrada em local com endereçamento detalhado.
- A3.1. Quando `materials.inventory.addressing.level = address`, o endereço específico é obrigatório por linha (IV-BR-061).

## Fluxos de Exceção

E1. Item inativo no catálogo.
- E1.1. Recusa com `IV-ERR-010`; documento permanece Rascunho com pendência por linha.

E2. Local inativo ou de outra empresa.
- E2.1. Recusa com `IV-ERR-060`.

E3. Tamanho ausente para item com grade.
- E3.1. Recusa com `IV-ERR-120`.

E4. Entrada duplicada (mesma chave de idempotência da origem).
- E4.1. Sistema retorna o documento já registrado, sem efeito duplicado (IV-BR-090; FA-IV-005); ocorrência auditada.

E5. Falha técnica na validação/efeito (timeout TIME-IV-001).
- E5.1. EXC-IV-007 → COMP-IV-002 (até 3 reexecuções idempotentes; depois, documento permanece Rascunho com motivo técnico).

E6. Usuário sem permissão (origem manual).
- E6.1. Recusa com `IV-ERR-900`; auditoria de negação.

## Regras

- IV-BR-001 (chave de saldo), IV-BR-002 (efeito por linha)
- IV-BR-010 (item ativo), IV-BR-011 (quantidade > 0), IV-BR-013 (referência)
- IV-BR-060..062 (locais), IV-BR-090 (idempotência), IV-BR-095 (auditoria com saldos)
- IV-BR-120 (tamanho para itens com grade)

## Eventos

- StockEntryRegistered (EVT-IV-001)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-001-A | "Entrada {number} confirmada. Saldos atualizados." |
| MSG-IV-UC-001-B | "Entrada bloqueada. Pendências: {failures}." |
| MSG-IV-UC-001-C | "Esta entrada já foi registrada (documento {number})." |

## Validações

- Item: ativo; linha: quantidade > 0; local: ativo e da empresa.
- Tamanho: obrigatório por linha quando o item possui grade de tamanhos.
- Referência de origem: obrigatória (recebimento ou documento externo).
- Idempotência: `Idempotency-Key` obrigatória na API; chave funcional da origem verificada.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/movements` | Cria documento de entrada (type=ENTRY); `201` com `id` e `number` |
| POST | `/api/v1/inventory/movements/{id}/confirm` | Confirma e efetiva saldo; `200` ou `422` com pendências |

## Permissões

- IV-PERM-001 (Registrar movimentação) e IV-PERM-002 (Confirmar movimentação).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-001-1 | Entrada por recebimento conferido → Confirmado + EVT-IV-001 + saldo efetivado |
| TC-IV-001-2 | Entrada avulsa válida → idem |
| TC-IV-001-3 | Item inativo → IV-ERR-010, permanece Rascunho |
| TC-IV-001-4 | Item com grade sem tamanho → IV-ERR-120 |
| TC-IV-001-5 | Reenvio idempotente → mesmo documento, sem efeito duplicado |
| TC-IV-001-6 | Entrada normaliza alerta de ruptura vigente |

---

# UC-IV-002 — Registrar Saída de Estoque (Atendimento)

## Objetivo

Registrar a saída de itens do estoque, atendendo uma reserva (entrega a solicitante) ou saída avulsa autorizada, com baixa de saldo e da reserva vinculada.

## Atores

- Almoxarife (primário)
- Sistema (validações, efeito, baixa de reserva, alertas)

## Pré-condições

- Documento em Rascunho ou reserva Ativa a atender.
- Usuário autenticado com permissão IV-PERM-001/002.
- Para saída por reserva: reserva em estado **Ativa** (ST-IV-010), não vencida.

## Pós-condições

- Documento de saída **Confirmado**; saldo reduzido por linha.
- Reserva vinculada baixada (total → ST-IV-011 Atendida; parcial → permanece Ativa com saldo restante).
- Eventos EVT-IV-002 `StockIssueRegistered` e, quando aplicável, EVT-IV-006 `ReservationFulfilled`.
- Alertas de mínimo/ruptura avaliados (EVT-IV-015/016 quando atingidos).

## Gatilho

- Almoxarife seleciona "Atender" na fila de reservas (visão do almoxarifado) ou "Nova saída".

## Fluxo Principal

1. Almoxarife seleciona a reserva a atender (ou inicia saída avulsa informando itens/quantidades/local).
2. Sistema pré-carrega o documento de saída com os dados da reserva (item, quantidade, local, tamanho, solicitante/centro de custo quando originada de MMS-003).
3. Almoxarife confirma as quantidades efetivamente entregues (total ou parcial).
4. Sistema executa SP-IV-01 com guards de saída: saldo disponível, segregação, reserva ativa, tamanho.
5. Sistema confirma o documento, efetiva a baixa, baixa a reserva e publica os eventos.
6. Sistema avalia mínimo/ruptura e dispara alertas quando atingidos.

**Detalhamento do passo 4:** saldo avaliado é o **disponível** (físico − reservado), por chave de saldo (IV-BR-020; MMS-RG-09).

## Fluxos Alternativos

A1. Atendimento parcial.
- A1.1. Quantidade entregue < reservada: reserva permanece Ativa com saldo restante e mesmo `expiresAt`; MMS-003 é informado da entrega parcial via EVT-IV-002 (payload com `reservationId` e quantidades).

A2. Saída avulsa sem reserva.
- A2.1. Permitida somente quando `materials.inventory.issue.without-reservation = true` (padrão false); exige motivo estruturado e centro de custo; auditoria reforçada.

A3. Troca de EPI/Fardamento (mesmo item, outro tamanho).
- A3.1. Não é saída direta: exige liberação da reserva original e nova reserva com o novo tamanho (IV-BR-121) — o sistema orienta o Almoxarife (MSG-IV-UC-002-C).

## Fluxos de Exceção

E1. Saldo disponível insuficiente.
- E1.1. Recusa com `IV-ERR-020` (EXC-IV-001); indica disponível × solicitado por linha; documento permanece Rascunho.

E2. Violação de segregação cliente/contrato.
- E2.1. Recusa com `IV-ERR-070` (EXC-IV-002); auditoria obrigatória.

E3. Reserva vencida ou inexistente no momento da confirmação.
- E3.1. Recusa com `IV-ERR-030` (EXC-IV-005); orienta nova reserva (FA-IV-006).

E4. Quantidade entregue maior que a reservada.
- E4.1. Recusa com `IV-ERR-034` — atendimento nunca excede o saldo da reserva.

E5. Falha técnica (timeout).
- E5.1. EXC-IV-007 → COMP-IV-002.

E6. Usuário sem permissão.
- E6.1. Recusa com `IV-ERR-900`; auditoria de negação.

## Regras

- IV-BR-020 (saldo disponível; MMS-RG-04 — nunca negativo), IV-BR-021 (efeito por linha)
- IV-BR-030..034 (reserva ativa, baixa, limites), IV-BR-070 (segregação; MMS-RG-10)
- IV-BR-090 (idempotência), IV-BR-095 (auditoria), IV-BR-120/121 (grade/tamanho)

## Eventos

- StockIssueRegistered (EVT-IV-002)
- ReservationFulfilled (EVT-IV-006, baixa total)
- StockMinimumAlerted / StockoutAlerted (EVT-IV-015/016, condicionais)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-002-A | "Saída {number} confirmada. Reserva {reservationNumber} atendida." |
| MSG-IV-UC-002-B | "Saldo insuficiente: disponível {available}, solicitado {requested}." |
| MSG-IV-UC-002-C | "Para troca de tamanho, libere a reserva atual e crie uma nova com o tamanho desejado." |
| MSG-IV-UC-002-D | "Reserva {reservationNumber} vencida. Crie uma nova reserva." |

## Validações

- Guards SP-IV-01 + saldo disponível + segregação + reserva ativa.
- Quantidade entregue ≤ saldo da reserva; > 0 por linha.
- Tamanho obrigatório quando grade; deve ser o tamanho da reserva (IV-BR-121).

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/movements` | Cria documento de saída (type=ISSUE), opcionalmente com `reservationId` |
| POST | `/api/v1/inventory/movements/{id}/confirm` | Confirma e efetiva baixa; `200` ou `422` com pendências |
| POST | `/api/v1/inventory/reservations/{id}/fulfill` | Atalho: gera e confirma a saída vinculada à reserva |

## Permissões

- IV-PERM-001 (Registrar movimentação), IV-PERM-002 (Confirmar movimentação), IV-PERM-003 (Gerenciar reservas — atendimento).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-002-1 | Atendimento total → Confirmado + EVT-IV-002 + EVT-IV-006, reserva Atendida |
| TC-IV-002-2 | Atendimento parcial → reserva permanece Ativa com saldo restante |
| TC-IV-002-3 | Saldo insuficiente → IV-ERR-020 com disponível × solicitado |
| TC-IV-002-4 | Reserva vencida → IV-ERR-030 |
| TC-IV-002-5 | Segregação violada → IV-ERR-070 + auditoria |
| TC-IV-002-6 | Saída zera saldo → EVT-IV-016 disparado |
| TC-IV-002-7 | Saída avulsa bloqueada por configuração → IV-ERR-090 com orientação |

---

# UC-IV-003 — Criar Reserva

## Objetivo

Reservar saldo disponível para atendimento futuro, com validade, originada de solicitação aprovada (MMS-003) ou criada manualmente pelo almoxarifado.

## Atores

- Sistema (primário, origem MMS-003)
- Almoxarife (primário, reserva manual)

## Pré-condições

- Item ativo; local ativo; saldo disponível ≥ quantidade.
- Origem MMS-003: evento `RequisitionApproved` recebido.
- Origem manual: usuário com permissão IV-PERM-003.

## Pós-condições

- Reserva em estado **Ativa** (ST-IV-010) com `expiresAt = now + reservation.ttl` (padrão 72h).
- Saldo disponível reduzido (físico inalterado).
- Evento EVT-IV-005 `ReservationCreated` publicado (consumido pelo MMS-003).

## Gatilho

- Externo: MSG-IV-C02 `RequisitionApproved`.
- Manual: Almoxarife seleciona "Nova reserva".

## Fluxo Principal

1. Sistema (ou Almoxarife) informa item, quantidade, local, tamanho (quando grade) e referência (solicitação ou motivo).
2. Sistema valida: item ativo, quantidade > 0, saldo disponível suficiente, local ativo.
3. Sistema cria a reserva Ativa com validade configurada e publica EVT-IV-005.

**Detalhamento do passo 3:** múltiplas reservas podem coexistir para a mesma chave de saldo, desde que a soma não exceda o disponível (IV-BR-031).

## Fluxos Alternativos

A1. Reserva parcial de solicitação.
- A1.1. Disponível < solicitado: o sistema cria a reserva pelo disponível e sinaliza o saldo não atendido ao MMS-003 (que segue rota de compra para a diferença — MMS-003 README, rota mista).

A2. Reserva manual para separação programada.
- A2.1. Almoxarife cria reserva com motivo estruturado e centro de custo, sem vínculo com MMS-003.

## Fluxos de Exceção

E1. Saldo disponível insuficiente (reserva manual).
- E1.1. Recusa com `IV-ERR-020`.

E2. Item inativo.
- E2.1. Recusa com `IV-ERR-010`.

E3. Tamanho ausente para item com grade.
- E3.1. Recusa com `IV-ERR-120`.

E4. Falha no consumo da mensagem externa.
- E4.1. TIME-IV-003 → retry; persistindo, DLQ + reconciliação (MMS-004-06 §15.2).

E5. Usuário sem permissão (manual).
- E5.1. Recusa com `IV-ERR-900`.

## Regras

- IV-BR-030..032 (criação, disponibilidade, coexistência), IV-BR-036 (validade; MMS-RG-03)
- IV-BR-010/011/060/120 (referências), IV-BR-090 (idempotência)

## Eventos

- ReservationCreated (EVT-IV-005)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-003-A | "Reserva {reservationNumber} criada, válida até {expiresAt}." |
| MSG-IV-UC-003-B | "Saldo disponível insuficiente para reservar {requested} unidade(s)." |
| MSG-IV-UC-003-C | "Reserva parcial criada: {reserved} de {requested} unidade(s)." |

## Validações

- Saldo disponível (físico − reservado) ≥ quantidade; referências válidas; tamanho quando grade.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/reservations` | Cria reserva; `201` com `id`, `number` e `expiresAt` |

## Permissões

- IV-PERM-003 (Gerenciar reservas).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-003-1 | Reserva automática por solicitação aprovada → Ativa + EVT-IV-005 |
| TC-IV-003-2 | Reserva parcial por disponibilidade → MSG-IV-UC-003-C |
| TC-IV-003-3 | Sem saldo → IV-ERR-020 |
| TC-IV-003-4 | Reserva com tamanho (EPI) → chave de saldo com tamanho |

---

# UC-IV-004 — Liberar e Vencer Reserva

## Objetivo

Encerrar uma reserva sem atendimento: liberação manual (ou por cancelamento da solicitação) ou vencimento automático por tempo.

## Atores

- Almoxarife / Supervisor (liberação manual)
- Sistema (vencimento por timer; liberação reativa ao MMS-003)

## Pré-condições

- Reserva em estado **Ativa**.
- Liberação manual: usuário com permissão IV-PERM-003.

## Pós-condições

- Reserva em **Liberada** (ST-IV-012) ou **Vencida** (ST-IV-013).
- Saldo reservado retorna ao disponível.
- EVT-IV-007 `ReservationReleased` ou EVT-IV-008 `ReservationExpired` publicado.

## Gatilho

- Manual: Almoxarife seleciona "Liberar".
- Externo: MSG-IV-C03 `RequisitionCancelled` (POL-IV-08).
- Temporal: TMR-IV-001 (`expiresAt` atingido).

## Fluxo Principal (Liberação Manual)

1. Almoxarife seleciona a reserva e confirma a liberação.
2. Sistema transiciona para Liberada, devolve o saldo ao disponível e publica EVT-IV-007.

## Fluxo Principal (Vencimento)

1. TMR-IV-001 dispara no `expiresAt`.
2. Sistema transiciona para Vencida, devolve o saldo e publica EVT-IV-008.
3. MMS-003 é informado (necessidade reaberta); FD-001-05 notifica os envolvidos.

**Detalhamento:** TMR-IV-002 (24h antes, configurável por `expiring-window`) envia alerta "reserva vencendo" sem interromper o fluxo.

## Fluxos Alternativos

A1. Liberação reativa a cancelamento de solicitação.
- A1.1. POL-IV-08 consome `RequisitionCancelled` e libera todas as reservas Ativas da solicitação (sem intervenção do Almoxarife).

A2. Vencimento com separação em curso.
- A2.1. O vencimento prevalece (RG-IV-GW-005-C); a saída vinculada falhará com `IV-ERR-030` e o Almoxarife cria nova reserva (FA-IV-006).

## Fluxos de Exceção

E1. Reserva não está Ativa (já atendida/liberada/vencida).
- E1.1. Recusa com `IV-ERR-090` (transição inválida).

E2. Falha na publicação do evento.
- E2.1. TIME-IV-002 → COMP-IV-003 (outbox/DLQ).

E3. Usuário sem permissão.
- E3.1. Recusa com `IV-ERR-900`.

## Regras

- IV-BR-035 (liberação), IV-BR-036 (vencimento; MMS-RG-03), IV-BR-037 (devolução de saldo)
- MMS-004-03 (Reserva — transições terminais)

## Eventos

- ReservationReleased (EVT-IV-007)
- ReservationExpired (EVT-IV-008)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-004-A | "Reserva {reservationNumber} liberada. Saldo disponível novamente." |
| MSG-IV-UC-004-B | "Reserva {reservationNumber} vence em {hours}h. Providencie o atendimento." |
| MSG-IV-UC-004-C | "Reserva {reservationNumber} vencida. O saldo foi liberado." |

## Validações

- Estado Ativa obrigatório; transições terminais irreversíveis.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/reservations/{id}/release` | Libera a reserva; `200` |
| GET | `/api/v1/inventory/reservations` | Fila de reservas (filtros: status, item, local, vencimento) |

## Permissões

- IV-PERM-003 (Gerenciar reservas).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-004-1 | Liberação manual → Liberada + EVT-IV-007 + saldo devolvido |
| TC-IV-004-2 | Vencimento por timer → Vencida + EVT-IV-008 |
| TC-IV-004-3 | Cancelamento da solicitação → liberação automática (POL-IV-08) |
| TC-IV-004-4 | Alerta de vencimento 24h antes (TMR-IV-002) |
| TC-IV-004-5 | Liberação de reserva já atendida → IV-ERR-090 |

---

# UC-IV-005 — Registrar Transferência entre Locais

## Objetivo

Mover saldo entre locais de armazenagem da mesma empresa, com efeito atômico de saída na origem e entrada no destino.

## Atores

- Almoxarife (primário)
- Sistema (validações, efeito atômico)

## Pré-condições

- Origem e destino ativos, distintos, da mesma empresa.
- Saldo disponível na origem.
- Usuário com permissão IV-PERM-001/002.

## Pós-condições

- Documento de transferência **Confirmado**; saldo movido atomicamente (INV-IV-09).
- EVT-IV-003 `StockTransferRegistered` publicado.
- Saldo global do item inalterado.

## Gatilho

Almoxarife seleciona "Nova transferência".

## Fluxo Principal

1. Almoxarife informa item(s), quantidade(s), origem e destino (e tamanho, quando grade).
2. Sistema valida referências e saldo disponível na origem (SP-IV-01 + RG-IV-GW-001).
3. Sistema confirma: baixa na origem + entrada no destino na mesma transação.
4. Sistema publica EVT-IV-003 e avalia alertas em ambos os locais.

## Fluxos Alternativos

A1. Transferência para reabastecer ponto de separação.
- A1.1. Origem = depósito principal; destino = área de separação; fluxo idêntico, com referência de motivo "reabastecimento".

A2. Transferência entre endereços do mesmo depósito.
- A2.1. Quando `addressing.level = address`, origem/destino são endereços; validações idênticas.

## Fluxos de Exceção

E1. Origem = destino.
- E1.1. Recusa com `IV-ERR-050`.

E2. Saldo insuficiente na origem.
- E2.1. Recusa com `IV-ERR-020`; documento permanece Rascunho.

E3. Local inativo ou de outra empresa.
- E3.1. Recusa com `IV-ERR-060`.

E4. Falha técnica.
- E4.1. EXC-IV-007 → COMP-IV-002 (sem efeito parcial — atomicidade garantida).

E5. Usuário sem permissão.
- E5.1. Recusa com `IV-ERR-900`.

## Regras

- IV-BR-050..053 (transferência), IV-BR-020 (saldo origem), IV-BR-060..062 (locais), IV-BR-120 (tamanho)

## Eventos

- StockTransferRegistered (EVT-IV-003)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-005-A | "Transferência {number} confirmada: {quantity} × {item} de {origin} para {destination}." |
| MSG-IV-UC-005-B | "Origem e destino devem ser diferentes." |

## Validações

- Origem ≠ destino; ambos ativos e da empresa; saldo disponível na origem; tamanho quando grade.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/transfers` | Cria e confirma transferência; `201` (ou `422` com pendências) |

## Permissões

- IV-PERM-001, IV-PERM-002.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-005-1 | Transferência válida → efeito atômico + EVT-IV-003, saldo global inalterado |
| TC-IV-005-2 | Origem sem saldo → IV-ERR-020 |
| TC-IV-005-3 | Origem = destino → IV-ERR-050 |
| TC-IV-005-4 | Falha técnica → nenhum efeito parcial (rollback completo) |

---

# UC-IV-006 — Registrar e Aprovar Ajuste de Estoque

## Objetivo

Corrigir o saldo por ajuste (positivo ou negativo) com motivo estruturado e aprovação, garantindo Segregation of Duties.

## Atores

- Supervisor de Almoxarifado (registra)
- Gestor de Suprimentos (aprova/rejeita)
- Sistema (validações, efeito após aprovação, notificações)

## Pré-condições

- Item ativo; local ativo.
- Registrador com IV-PERM-004; aprovador com IV-PERM-005 e ≠ registrador (IV-BR-085).
- `materials.inventory.adjustment.approval-required = true` (padrão).

## Pós-condições

- Ajuste em **Aprovado** (ST-IV-021) com efeito de saldo aplicado, ou **Rejeitado** (ST-IV-022) sem efeito.
- EVT-IV-009/010/011 publicados conforme o desfecho.

## Gatilho

Supervisor seleciona "Novo ajuste" (ou geração automática por inventário — UC-IV-007).

## Fluxo Principal

1. Supervisor informa tipo (positivo/negativo), motivo estruturado (FD-001-09, `adjustment.reasons`), itens, quantidades e locais (e tamanho, quando grade).
2. Sistema valida e registra o ajuste em **Pendente**; publica EVT-IV-009; notifica aprovadores (POL-IV-05).
3. Gestor analisa e **aprova** (com comentário opcional) ou **rejeita** (com motivo obrigatório).
4. Aprovado: sistema revalida saldo (ajuste negativo), aplica o efeito e publica EVT-IV-010.
5. Rejeitado: sistema publica EVT-IV-011 e notifica o registrador.

**Detalhamento do passo 4:** a revalidação na aprovação cobre o intervalo entre registro e decisão (RG-IV-GW-003-C).

## Fluxos Alternativos

A1. Ajuste originado de inventário.
- A1.1. O sistema gera o ajuste com tipo/quantidade derivados da divergência, vinculado ao inventário; registrador = Supervisor que abriu o inventário; fluxo de aprovação idêntico.

A2. Aprovação com comentário.
- A2.1. Comentário do aprovador registrado em auditoria e timeline do ajuste.

A3. Ajuste positivo de regularização de legado.
- A3.1. Motivo "regularização de implantação"; usado na carga inicial de saldos (migração), sempre com aprovação.

## Fluxos de Exceção

E1. Aprovador = registrador (SoD).
- E1.1. Recusa com `IV-ERR-085` (EXC-IV-006); auditoria obrigatória.

E2. Saldo insuficiente para ajuste negativo (na aprovação).
- E2.1. Aprovação bloqueada com `IV-ERR-084`; ajuste retorna a Pendente com motivo.

E3. Rejeição sem motivo.
- E3.1. Decisão não registrada; `IV-ERR-086`.

E4. Ajuste já decidido (dupla decisão).
- E4.1. Recusa com `IV-ERR-090`.

E5. SLA de aprovação estourado (24h).
- E5.1. TMR-IV-003 → ESC-IV-001 (escalonamento ao Gestor de Suprimentos); ajuste permanece Pendente.

E6. Usuário sem permissão.
- E6.1. Recusa com `IV-ERR-900`.

## Regras

- IV-BR-080..086 (ajuste, motivo, SoD, saldo, rejeição)
- IV-BR-095 (auditoria com saldos), IV-BR-120 (tamanho)

## Eventos

- AdjustmentRegistered (EVT-IV-009)
- AdjustmentApproved (EVT-IV-010)
- AdjustmentRejected (EVT-IV-011)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-006-A | "Ajuste {number} registrado e enviado para aprovação." |
| MSG-IV-UC-006-B | "Ajuste {number} aprovado. Saldo atualizado." |
| MSG-IV-UC-006-C | "Ajuste {number} rejeitado. Motivo: {reason}." |
| MSG-IV-UC-006-D | "Você não pode aprovar um ajuste registrado por você." |

## Validações

- Motivo estruturado obrigatório; quantidade > 0; referências válidas; SoD; saldo para negativo.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/adjustments` | Registra ajuste em Pendente; `201` |
| POST | `/api/v1/inventory/adjustments/{id}/approve` | Aprova e aplica efeito; `200` ou `422` |
| POST | `/api/v1/inventory/adjustments/{id}/reject` | Rejeita (motivo obrigatório); `200` |
| GET | `/api/v1/inventory/adjustments` | Fila de aprovação (filtros: status, período, registrador) |

## Permissões

- IV-PERM-004 (Registrar ajuste), IV-PERM-005 (Aprovar/Rejeitar ajuste).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-006-1 | Registro → Pendente + EVT-IV-009 + notificação ao aprovador |
| TC-IV-006-2 | Aprovação válida → efeito aplicado + EVT-IV-010 |
| TC-IV-006-3 | SoD violado (aprovador = registrador) → IV-ERR-085 + auditoria |
| TC-IV-006-4 | Ajuste negativo sem saldo na aprovação → IV-ERR-084 |
| TC-IV-006-5 | Rejeição com motivo → EVT-IV-011, sem efeito |
| TC-IV-006-6 | SLA estourado → ESC-IV-001 |

---

# UC-IV-007 — Executar Inventário (Contagem Física)

## Objetivo

Apurar a acuracidade do estoque por contagem física, gerando ajustes aprovados para as divergências e fechando o inventário com acuracidade registrada.

## Atores

- Supervisor (abre e fecha)
- Almoxarife (conta)
- Gestor (aprova ajustes de divergência — via UC-IV-006)
- Sistema (apuração, tolerância, geração de ajustes)

## Pré-condições

- Escopo definido: locais e/ou itens (ou geral); tipo geral ou cíclico (classe A/B/C).
- Usuário com IV-PERM-006.

## Pós-condições

- Inventário em **Fechado** (ST-IV-032) com acuracidade registrada (KPI ≥ 98%).
- Ajustes de divergência concluídos (aprovados com efeito, ou rejeitados com justificativa auditada).
- EVT-IV-012/013/014 publicados.

## Gatilho

Supervisor seleciona "Novo inventário"; ou agenda de inventário cíclico (TMR-IV-004).

## Fluxo Principal

1. Supervisor define escopo e tipo, e abre o inventário (Aberto → EVT-IV-012).
2. Sistema gera a lista de contagem (chaves de saldo do escopo com saldo sistêmico — cego ou ciente conforme `count.blind`, padrão cego).
3. Almoxarife registra as quantidades contadas (Em Contagem no primeiro lançamento; EVT-IV-013 por lançamento).
4. Supervisor encerra os lançamentos; sistema apura divergências (contado × sistêmico).
5. GW-IV-004: sem divergência (ou dentro da tolerância) → fechamento direto; com divergência → sistema gera ajustes vinculados (Fluxo E / UC-IV-006).
6. Após ajustes concluídos, Supervisor fecha o inventário (EVT-IV-014).

**Detalhamento do passo 2:** com `count.freeze = false` (padrão), movimentações continuam durante a contagem e a apuração considera o saldo sistêmico no momento do encerramento dos lançamentos (IV-BR-100).

## Fluxos Alternativos

A1. Inventário cíclico por classe.
- A1.1. Escopo sugerido automaticamente: classe A a cada 30 dias, B a 90, C a 180 (`cycle-count.*`); Supervisor confirma ou ajusta o escopo.

A2. Recontagem.
- A2.1. Divergência acima do limiar de recontagem (`count.recount-threshold`, padrão desabilitado) exige segunda contagem antes da geração do ajuste; segunda contagem prevalece.

A3. Cancelamento de inventário.
- A3.1. Supervisor cancela com motivo (Aberto/Em Contagem → Cancelado, ST-IV-033); lançamentos preservados para auditoria; nenhum ajuste é gerado.

## Fluxos de Exceção

E1. Lançamento fora do escopo.
- E1.1. Recusa com `IV-ERR-100` (chave de saldo não pertence ao escopo do inventário).

E2. Fechamento com divergência sem ajuste concluído nem justificativa.
- E2.1. Recusa com `IV-ERR-102` (FA-IV-004).

E3. Lançamento após encerramento dos lançamentos.
- E3.1. Recusa com `IV-ERR-090`.

E4. Usuário sem permissão.
- E4.1. Recusa com `IV-ERR-900`.

## Regras

- IV-BR-100..103 (inventário, apuração, tolerância, fechamento)
- IV-BR-080..086 (ajustes gerados), IV-BR-095 (auditoria)

## Eventos

- InventoryCountStarted (EVT-IV-012)
- CountEntryRegistered (EVT-IV-013)
- InventoryCountClosed (EVT-IV-014)
- AdjustmentRegistered (EVT-IV-009, por divergência)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-007-A | "Inventário {number} aberto. {count} chave(s) de saldo no escopo." |
| MSG-IV-UC-007-B | "Contagem registrada para {item} em {location}." |
| MSG-IV-UC-007-C | "{divergent} divergência(s) encontrada(s). Ajustes gerados para aprovação." |
| MSG-IV-UC-007-D | "Inventário {number} fechado. Acuracidade: {accuracy}%." |

## Validações

- Escopo obrigatório; lançamentos apenas dentro do escopo; tolerância `count.tolerance` (padrão 0); fechamento exige divergências resolvidas ou justificadas.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/counts` | Abre inventário; `201` |
| POST | `/api/v1/inventory/counts/{id}/entries` | Registra contagem; `201` |
| GET | `/api/v1/inventory/counts/{id}/divergences` | Apuração de divergências |
| POST | `/api/v1/inventory/counts/{id}/close` | Fecha inventário; `200` ou `422` |
| POST | `/api/v1/inventory/counts/{id}/cancel` | Cancela (motivo obrigatório); `200` |

## Permissões

- IV-PERM-006 (Gerenciar inventário); lançamentos por IV-PERM-001.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-007-1 | Abertura → EVT-IV-012 + lista de contagem cega |
| TC-IV-007-2 | Contagem sem divergência → fechamento direto + EVT-IV-014 + acuracidade 100% |
| TC-IV-007-3 | Divergência → ajuste vinculado gerado; fechamento após aprovação |
| TC-IV-007-4 | Fechamento com divergência pendente → IV-ERR-102 |
| TC-IV-007-5 | Lançamento fora do escopo → IV-ERR-100 |
| TC-IV-007-6 | Cancelamento com motivo → ST-IV-033, sem ajustes |

---

# UC-IV-008 — Estornar Movimentação

## Objetivo

Reverter formalmente o efeito de um documento Confirmado, por meio de novo documento com efeito inverso, preservando o original (MMS-P-08).

## Atores

- Supervisor de Almoxarifado (primário)
- Sistema (validações, efeito inverso)

## Pré-condições

- Documento em estado **Confirmado** (ST-IV-002), ainda não estornado.
- Usuário com IV-PERM-007.
- Para ajuste aprovado: estornante ≠ aprovador (SoD — IV-BR-114).

## Pós-condições

- Documento original em **Estornado** (ST-IV-003).
- Documento de estorno criado já Confirmado, com efeito inverso aplicado.
- EVT-IV-004 `MovementReversed` publicado.

## Gatilho

Supervisor seleciona "Estornar" em um documento confirmado (erro operacional detectado — COMP-IV-001).

## Fluxo Principal

1. Supervisor seleciona o documento e informa o motivo do estorno (obrigatório).
2. Sistema valida: estado Confirmado, não estornado antes, efeito inverso viável (não gera saldo negativo), SoD quando aplicável.
3. Sistema cria o documento de estorno (tipo espelhado, quantidades idênticas, referência ao original), confirma com efeito inverso e marca o original como Estornado.
4. Sistema publica EVT-IV-004 e avalia alertas.

**Detalhamento do passo 3:** estorno de saída devolve saldo; estorno de entrada retira saldo (exige disponível — RG-IV-GW-002-B). Reserva vinculada já atendida não é recriada: o estorno devolve o saldo ao disponível (IV-BR-115).

## Fluxos Alternativos

A1. Estorno parcial por linha.
- A1.1. Permitido por linha do documento (quantidade total da linha); estornos parciais de quantidade dentro da linha não são suportados no MVP (v1.1).

A2. Estorno de transferência.
- A2.1. Efeito inverso atômico: retira do destino e devolve à origem; exige saldo disponível no destino.

## Fluxos de Exceção

E1. Documento não Confirmado ou já estornado.
- E1.1. Recusa com `IV-ERR-110`.

E2. Efeito inverso geraria saldo negativo.
- E2.1. Recusa com `IV-ERR-113`, indicando saldo atual.

E3. Motivo ausente.
- E3.1. Recusa com `IV-ERR-112`.

E4. SoD violado (estorno de ajuste pelo aprovador).
- E4.1. Recusa com `IV-ERR-085`; auditoria.

E5. Usuário sem permissão.
- E5.1. Recusa com `IV-ERR-900`.

## Regras

- IV-BR-110..115 (estorno), MMS-P-08 (imutabilidade do original), IV-BR-095 (auditoria)

## Eventos

- MovementReversed (EVT-IV-004)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-008-A | "Documento {number} estornado pelo documento {reversalNumber}." |
| MSG-IV-UC-008-B | "Estorno não permitido: o saldo atual não comporta o efeito inverso." |

## Validações

- RG-IV-GW-002-A..D; motivo mínimo 10 caracteres.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/movements/{id}/reverse` | Estorna o documento; `201` com o documento de estorno |

## Permissões

- IV-PERM-007 (Estornar documento).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-008-1 | Estorno de saída → saldo devolvido + EVT-IV-004 + original Estornado |
| TC-IV-008-2 | Estorno de entrada sem saldo → IV-ERR-113 |
| TC-IV-008-3 | Duplo estorno → IV-ERR-110 |
| TC-IV-008-4 | Estorno sem motivo → IV-ERR-112 |
| TC-IV-008-5 | Estorno de ajuste pelo aprovador (SoD) → IV-ERR-085 |

---

# UC-IV-009 — Gerenciar Locais de Armazenagem

## Objetivo

Cadastrar e manter a estrutura de endereçamento do estoque (almoxarifado → depósito → endereço, conforme `addressing.level`).

## Atores

- Supervisor de Almoxarifado / Administrador (primário)

## Pré-condições

- Usuário com IV-PERM-008.

## Pós-condições

- Local cadastrado/atualizado/inativado; hierarquia sem ciclos; auditoria registrada.

## Gatilho

Supervisor acessa "Locais de armazenagem".

## Fluxo Principal

1. Supervisor informa código, descrição, tipo (almoxarifado/depósito/endereço) e local pai (quando aplicável).
2. Sistema valida unicidade do código por empresa, integridade da hierarquia (sem ciclos, tipo compatível com o pai) e nível permitido pela configuração.
3. Sistema persiste e registra auditoria.

## Fluxos Alternativos

A1. Inativação de local.
- A1.1. Permitida apenas sem saldo físico e sem reservas ativas no local (IV-BR-063); histórico preservado; novas movimentações bloqueadas.

A2. Alteração de endereçamento da empresa (nível).
- A2.1. Mudança de `addressing.level` é configuração de módulo (Administrador) e exige migração assistida dos saldos — fora do fluxo operacional (MMS-004-11, estratégia de migração).

## Fluxos de Exceção

E1. Código duplicado.
- E1.1. Recusa com `IV-ERR-061`.

E2. Hierarquia inválida (ciclo ou tipo incompatível).
- E2.1. Recusa com `IV-ERR-062`.

E3. Inativação com saldo ou reserva ativa.
- E3.1. Recusa com `IV-ERR-063`, indicando posição atual.

E4. Usuário sem permissão.
- E4.1. Recusa com `IV-ERR-900`.

## Regras

- IV-BR-060..063 (locais e hierarquia), IV-BR-095 (auditoria)

## Eventos

- Nenhum evento de domínio no MVP (locais são dados estruturantes; alterações trilhadas via Timeline/Audit). Evento `LocationChanged` previsto na v1.1.

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-009-A | "Local {code} cadastrado." |
| MSG-IV-UC-009-B | "Não é possível inativar: o local possui saldo ou reservas ativas." |

## Validações

- Código único por empresa; hierarquia acíclica; nível ≤ `addressing.level`; inativação condicionada.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/inventory/locations` | Cadastra local; `201` |
| PATCH | `/api/v1/inventory/locations/{id}` | Edita descrição/hierarquia; `If-Match` |
| POST | `/api/v1/inventory/locations/{id}/inactivate` | Inativa (condições IV-BR-063); `200` ou `422` |
| GET | `/api/v1/inventory/locations` | Árvore de locais |

## Permissões

- IV-PERM-008 (Gerenciar locais).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-009-1 | Cadastro válido com hierarquia → persistido |
| TC-IV-009-2 | Ciclo na hierarquia → IV-ERR-062 |
| TC-IV-009-3 | Inativação com saldo → IV-ERR-063 |
| TC-IV-009-4 | Código duplicado → IV-ERR-061 |

---

# UC-IV-010 — Tratar Alertas de Estoque (Mínimo e Ruptura)

## Objetivo

Apresentar e tratar os alertas de estoque mínimo e ruptura, direcionando a reposição e o acompanhamento até a normalização.

## Atores

- Almoxarife / Supervisor / Gestor (tratamento)
- Sistema (disparo, normalização, escalonamento)

## Pré-condições

- Item com parâmetros de reposição definidos (MMS-002, UC-IC-007).
- Evento de efeito de saldo ocorrido (saída, transferência, ajuste, estorno).

## Pós-condições

- Alerta em estado **Aberto** ou **Normalizado** (BO-IV-008).
- Notificações entregues conforme `alerts.recipients.*`.
- Ruptura sem normalização em 48h escalonada (ESC-IV-004).

## Gatilho

- Automático: POL-IV-09/10 após efeito de saldo (saldo ≤ mínimo → EVT-IV-015; saldo = 0 → EVT-IV-016).
- Manual: usuário acessa a tela de alertas.

## Fluxo Principal

1. Sistema detecta cruzamento de limiar após efeito de saldo e abre o alerta (sem duplicar alerta aberto para a mesma chave — IV-BR-091).
2. Sistema publica EVT-IV-015/016 e notifica os destinatários configurados.
3. Usuário visualiza a fila de alertas, analisa posição e consumo, e registra a ação (gerar necessidade de reposição — que segue os fluxos MMS-003/PR-001).
4. Entrada de saldo posterior normaliza o alerta automaticamente (POL-IV-09).

## Fluxos Alternativos

A1. Ciência manual sem reposição imediata.
- A1.1. Usuário registra ciência com comentário (alerta permanece Aberto até normalização de saldo); ciência auditada.

A2. Alerta de mínimo recorrente.
- A2.1. Reabertura após normalização é permitida; histórico de ocorrências por chave mantido para análise de cobertura.

## Fluxos de Exceção

E1. Falha na notificação.
- E1.1. Retry conforme FD-001-05; alerta permanece registrado independentemente da entrega (notificação nunca bloqueia operação — IV-BR-092).

E2. Usuário sem permissão de consulta.
- E2.1. Recusa com `IV-ERR-900`.

## Regras

- IV-BR-090..092 (alertas, deduplicação, não bloqueio; MMS-RG-12)
- IV-BR-095 (auditoria de ciência)

## Eventos

- StockMinimumAlerted (EVT-IV-015)
- StockoutAlerted (EVT-IV-016)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-010-A | "Item {item} atingiu o estoque mínimo em {location}." |
| MSG-IV-UC-010-B | "RUPTURA: item {item} sem saldo em {location}." |
| MSG-IV-UC-010-C | "Alerta normalizado: saldo de {item} restabelecido." |

## Validações

- Deduplicação por chave de saldo enquanto alerta Aberto; destinatários válidos na configuração.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| GET | `/api/v1/inventory/alerts` | Fila de alertas (filtros: tipo, status, item, local) |
| POST | `/api/v1/inventory/alerts/{id}/acknowledge` | Registra ciência (comentário opcional); `200` |

## Permissões

- IV-PERM-009 (Consultar posição/alertas) e IV-PERM-010 (Tratar alertas).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-010-1 | Saída atinge mínimo → EVT-IV-015 + notificação |
| TC-IV-010-2 | Saída zera saldo → EVT-IV-016 (prioridade alta) |
| TC-IV-010-3 | Deduplicação: segundo cruzamento com alerta Aberto → sem novo alerta |
| TC-IV-010-4 | Entrada normaliza alerta automaticamente |
| TC-IV-010-5 | Ruptura 48h sem normalização → ESC-IV-004 |

---

# UC-IV-011 — Consultar Posição e Extrato de Estoque

## Objetivo

Fornecer a posição consolidada do estoque (físico, reservado, disponível) e o extrato completo de movimentações por item/local, com trilha até os documentos de origem.

## Atores

- Almoxarife, Supervisor, Gestor, Auditor (consulta)
- Módulos integrados (consulta operacional via projeção — MMS-003, MMS-005)

## Pré-condições

- Usuário autenticado com IV-PERM-009.

## Pós-condições

- Nenhuma alteração de estado (operação somente-leitura).

## Gatilho

Usuário acessa "Posição de estoque" ou "Extrato"; módulo consumidor consulta disponibilidade.

## Fluxo Principal

1. Usuário informa filtros (item, local, tamanho, período, tipo de documento).
2. Sistema consulta a projeção de leitura (cache Redis, TTL `cache.ttl`) com fallback ao banco transacional.
3. Sistema retorna posição (físico/reservado/disponível por chave) ou extrato (documentos com efeito e saldos anterior/posterior por linha).

**Detalhamento do passo 3:** o extrato reconstrói a trilha completa: documento → origem (recebimento MMS-005, solicitação MMS-003, ajuste, inventário, estorno) — rastreabilidade ponta a ponta (MMS-P-08).

## Fluxos Alternativos

A1. Visão do almoxarifado (operacional).
- A1.1. Fila operacional com filtros: solicitante, período, status, centro de custo, empresa, tipo de produto (EPI/Fardamento), número — conforme requisito da visão do almoxarifado (MMS-003 §Visão do Almoxarifado; IV-BR-097).

A2. Exportação do extrato.
- A2.1. Exportação CSV/Excel do extrato filtrado, com registro de auditoria da exportação (LGPD — dados de consumo por colaborador restritos a perfis autorizados).

A3. Consulta de disponibilidade por módulo consumidor.
- A3.1. MMS-003 consulta disponibilidade para validação pós-aprovação (MMS-RG-09) via projeção somente-leitura.

## Fluxos de Exceção

E1. Falha da projeção (cache indisponível).
- E1.1. Fallback ao banco transacional; degradação registrada.

E2. Perfil sem acesso a dados sensíveis (consumo por colaborador).
- E2.1. Campos restritos omitidos; acesso negado registrado quando aplicável (IV-ERR-900).

## Regras

- IV-BR-096 (projeção nunca fonte de verdade), IV-BR-097 (visão do almoxarifado), IV-BR-098 (LGPD/retenção)

## Eventos

- Nenhum (operação somente-leitura).

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-IV-UC-011-A | "{count} registro(s) encontrado(s)." |
| MSG-IV-UC-011-B | "Nenhuma movimentação no período/filtros informados." |

## Validações

- Paginação keyset obrigatória; filtros restritos a valores válidos; campos sensíveis por perfil.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| GET | `/api/v1/inventory/balances` | Posição por chave (filtros: item, local, tamanho) |
| GET | `/api/v1/inventory/balances/{itemId}/statement` | Extrato do item (período, local, cursor keyset) |
| GET | `/api/v1/inventory/movements` | Lista de documentos (filtros da visão do almoxarifado) |
| GET | `/api/v1/inventory/movements/{id}` | Detalhe do documento com linhas e saldos |

## Permissões

- IV-PERM-009 (Consultar posição/extrato); campos sensíveis conforme MMS-004-09.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-IV-011-1 | Posição por item/local → físico/reservado/disponível corretos |
| TC-IV-011-2 | Extrato com trilha até documento de origem (recebimento/solicitação) |
| TC-IV-011-3 | Visão do almoxarifado com todos os filtros |
| TC-IV-011-4 | Fallback de cache → consulta ao banco sem erro |
| TC-IV-011-5 | Paginação keyset: 2 páginas sem duplicidade nem omissão |
| TC-IV-011-6 | Perfil restrito não vê consumo por colaborador |

---

# 8. Matriz de Rastreabilidade UC × Regras × Eventos × APIs × Permissões

| UC | Regras IV-BR | Eventos | Endpoints | Permissões |
| -- | ------------ | ------- | --------- | ---------- |
| UC-IV-001 Entrada | 001, 002, 010, 011, 013, 060..062, 090, 095, 120 | EVT-IV-001 | POST /movements; POST /movements/{id}/confirm | IV-PERM-001/002 |
| UC-IV-002 Saída/Atendimento | 020, 021, 030..034, 070, 090, 095, 120, 121 | EVT-IV-002, 006, 015, 016 | POST /movements; POST /movements/{id}/confirm; POST /reservations/{id}/fulfill | IV-PERM-001/002/003 |
| UC-IV-003 Criar Reserva | 010, 011, 030..032, 036, 060, 090, 120 | EVT-IV-005 | POST /reservations | IV-PERM-003 |
| UC-IV-004 Liberar/Vencer | 035, 036, 037, State Machine | EVT-IV-007, 008 | POST /reservations/{id}/release; GET /reservations | IV-PERM-003 |
| UC-IV-005 Transferência | 020, 050..053, 060..062, 120 | EVT-IV-003 | POST /transfers | IV-PERM-001/002 |
| UC-IV-006 Ajuste | 080..086, 095, 120 | EVT-IV-009, 010, 011 | POST /adjustments; POST /adjustments/{id}/approve; /reject; GET /adjustments | IV-PERM-004/005 |
| UC-IV-007 Inventário | 080..086, 095, 100..103 | EVT-IV-009, 012, 013, 014 | POST /counts; POST /counts/{id}/entries; GET /counts/{id}/divergences; POST /counts/{id}/close; /cancel | IV-PERM-006 (lançamentos IV-PERM-001) |
| UC-IV-008 Estorno | 085, 110..115, 095 | EVT-IV-004 | POST /movements/{id}/reverse | IV-PERM-007 |
| UC-IV-009 Locais | 060..063, 095 | — (v1.1: LocationChanged) | POST/PATCH /locations; POST /locations/{id}/inactivate; GET /locations | IV-PERM-008 |
| UC-IV-010 Alertas | 090..092, 095 | EVT-IV-015, 016 | GET /alerts; POST /alerts/{id}/acknowledge | IV-PERM-009/010 |
| UC-IV-011 Posição/Extrato | 096, 097, 098 | — | GET /balances; GET /balances/{itemId}/statement; GET /movements; GET /movements/{id} | IV-PERM-009 |

**Cobertura:** 40/40 regras IV-BR mapeadas a pelo menos um UC; 16/16 eventos EVT-IV cobertos (EVT-IV-012..014 no UC-IV-007; EVT-IV-015/016 nos UC-IV-002/010).

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: 11 casos de uso (UC-IV-001..011) em especificação UML completa — fluxo principal, fluxos alternativos, fluxos de exceção, pré/pós-condições, regras IV-BR, eventos EVT-IV, mensagens i18n, validações, APIs `/api/v1/inventory`, permissões IV-PERM e testes TC-IV; matriz de rastreabilidade consolidada com 100% de cobertura de regras e eventos. |
