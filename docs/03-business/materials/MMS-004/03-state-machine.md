**Documento:** MMS-004-03 — State Machine
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.1.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004 (Visão do Módulo v1.1.0), MMS-004-02 (Business Rules — IV-BR-001..042, IV-BR-070..073, IV-BR-130/131), MMS-001 (Documento Mestre Funcional — §15.2, MMS-RG-03/04/05), ADR-014 (Motor de Regras de Reposição), FD-001-04 (Workflow), FD-001-06 (Audit), FD-001-07 (Timeline), FD-001-10 (Configuration)
**Referências:** MMS-002-03 (padrão de formato), MMS-003, MMS-005, PR-001, GOV-001

---

# 1. Objetivo

Definir todos os estados possíveis das **entidades com ciclo de vida** do módulo Inventory Management — Documento de Movimentação, Reserva, Ajuste, Inventário e Sugestão de Reposição (ADR-014) — suas transições, restrições e eventos associados, garantindo evolução controlada, previsível, auditável e compatível com as regras do módulo (MMS-004-02).

Nenhuma transição poderá ocorrer fora das regras definidas neste documento. Saldos **não possuem estado** — são projeções derivadas (IV-BR-001) e não aparecem nesta máquina.

---

# 2. Princípios

- Toda entidade possui exatamente um estado atual.
- Toda mudança de estado gera auditoria com saldo anterior/posterior quando afeta saldo (IV-BR-090), evento de domínio e marco na Timeline (IV-BR-091).
- Nenhum estado pode ser alterado diretamente no banco de dados — toda transição é **ação de negócio** executada por serviço de domínio (MMS-001 §15).
- Documento de movimentação **confirmado é imutável**; a correção ocorre por estorno, que é outro documento (IV-BR-003).
- Nenhuma entidade é excluída fisicamente; cancelamento de rascunhos é lógico (soft delete).
- O estado da Reserva é independente do estado da solicitação de origem (MMS-003): a reserva reage a eventos da solicitação, não os replica.

---

# 3. Entidades e Estados Oficiais

## 3.1 Documento de Movimentação (entrada, saída, transferência)

| Código | Estado | Final |
|--------|--------|-------|
| ST-IV-001 | Rascunho | Não |
| ST-IV-002 | Confirmado | Não (estornável — IV-BR-003) |
| ST-IV-003 | Estornado | Sim |
| ST-IV-004 | Cancelado | Sim (somente de Rascunho) |

## 3.2 Reserva

| Código | Estado | Final |
|--------|--------|-------|
| ST-IV-010 | Ativa | Não |
| ST-IV-011 | Atendida | Sim |
| ST-IV-012 | Liberada | Sim |
| ST-IV-013 | Vencida | Sim |

## 3.3 Ajuste

| Código | Estado | Final |
|--------|--------|-------|
| ST-IV-020 | Pendente de Aprovação | Não |
| ST-IV-021 | Aprovado (confirmado) | Não (estornável) |
| ST-IV-022 | Rejeitado | Sim |
| ST-IV-023 | Estornado | Sim |

Quando `materials.inventory.adjustment.approval-required=false`, o ajuste nasce diretamente em **Aprovado (confirmado)** — os estados Pendente e Rejeitado não são percorridos (IV-BR-041).

## 3.4 Inventário (contagem física)

| Código | Estado | Final |
|--------|--------|-------|
| ST-IV-030 | Aberto | Não |
| ST-IV-031 | Em Contagem | Não |
| ST-IV-032 | Fechado | Sim |
| ST-IV-033 | Cancelado | Sim |

## 3.5 Sugestão de Reposição (ADR-014)

| Código | Estado | Final |
|--------|--------|-------|
| ST-IV-040 | Sugerida | Não |
| ST-IV-041 | Confirmada | Sim |
| ST-IV-042 | Descartada | Sim |

A sugestão nasce em **Sugerida** por avaliação automática da regra do item (IV-BR-130, POL-IV-10). De **Sugerida** vai para **Confirmada** (CMD-IV-021 — dispara a rota: demanda PR-001 ou documento de transferência, IV-BR-131) ou **Descartada** (CMD-IV-022). Uma sugestão **não altera saldo** em nenhum estado. Quando `materials.replenishment.auto-execute=true` (v2.0), a transição Sugerida→Confirmada ocorre automaticamente, sem ação humana.

---

# 4. Especificação dos Estados — Documento de Movimentação

## ST-IV-001 — Rascunho (documento)

Documento em preparação; ainda **não afeta saldo** nem reservado.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Criar Aggregate; validar tipo de documento, origem (IV-BR-010/011), item Ativo quando exigido (IV-BR-007), local válido (IV-BR-008), quantidade positiva (IV-BR-009), tamanho quando o item tem grade (IV-BR-120); registrar `created_at`/`created_by`; inicializar `version = 1` |
| Exit Actions | Validar invariantes do destino: confirmação exige o conjunto completo de regras (ver Guard Conditions) |
| Eventos aceitos | UpdateMovement, ConfirmMovement, CancelMovement, AddComment |
| Eventos rejeitados | ReverseMovement, qualquer efeito sobre saldo |
| Guard Conditions | **ConfirmMovement:** todas as regras do tipo (IV-BR-010/011/030); saldo suficiente (IV-BR-004); disponível suficiente para reserva (IV-BR-005); segregação cliente/contrato (IV-BR-060); item Ativo (IV-BR-007); local válido e granularidade (IV-BR-008/051); tamanho válido (IV-BR-120/121); freeze de inventário (IV-BR-073) · **CancelMovement:** zero efeitos registrados |
| Side Effects | Nenhum sobre saldo; auditoria e timeline de criação/edição |
| SLA | Livre (sem SLA); rascunhos antigos alimentam indicador operacional |
| Responsável | Almoxarife (operacional) ou Sistema (integrações MMS-003/MMS-005) |
| Auditoria | Criação e alterações com valores anterior/posterior dos campos do documento |

---

## ST-IV-002 — Confirmado (documento)

O documento produziu seu efeito sobre o saldo (total e/ou reservado) de forma atômica. **Imutável** (IV-BR-003).

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Aplicar efeito no saldo na mesma transação; gravar saldo anterior/posterior na auditoria (IV-BR-090); publicar evento do tipo (Entrada registrada / Saída registrada / Transferência registrada); avaliar alertas (IV-BR-080/081); invalidar cache de leitura (IV-BR-110); para transferência: aplicar saída + entrada vinculadas (IV-BR-030); para saída por atendimento: baixar reserva vinculada (IV-BR-023) |
| Exit Actions | Validar estorno: referência ao documento original e motivo obrigatório (IV-BR-003) |
| Eventos aceitos | ReverseMovement, AddComment, consultas de leitura |
| Eventos rejeitados | UpdateMovement, ConfirmMovement (já confirmado), CancelMovement, qualquer edição de campo |
| Guard Conditions | **ReverseMovement:** motivo obrigatório; documento ainda não estornado; usuário com permissão de estorno (MMS-004-09); estorno respeita as mesmas regras de saldo no sentido inverso |
| Side Effects | Saldo alterado; alertas de mínimo/ruptura avaliados; KPIs de movimentação atualizados; timeline do documento e do item |
| SLA | Confirmação com efeito imediato; propagação ao cache de leitura < 5s |
| Responsável | Sistema (efeito); Almoxarife/Sistema (autoria) |
| Auditoria | Confirmação com saldo anterior/posterior, responsável, data/hora, correlationId |

---

## ST-IV-003 — Estornado (documento)

Estado terminal. O documento teve seus efeitos integralmente revertidos por um documento de estorno vinculado.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Vincular o documento de estorno; registrar `reversed_at`/`reversed_by` e motivo; publicar Estorno registrado |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta) |
| Eventos rejeitados | Todos |
| Guard Conditions | — (a guarda ocorre na origem: somente Confirmado não estornado pode ser estornado) |
| Side Effects | Timeline final; o documento e seu estorno permanecem consultáveis no extrato |
| SLA | — |
| Responsável | Sistema (a partir da ação autorizada) |
| Auditoria | Estorno com motivo, usuário, data/hora, saldo anterior/posterior do efeito reverso |

---

## ST-IV-004 — Cancelado (documento)

Estado terminal. Rascunho descartado antes de qualquer efeito sobre saldo.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Registrar motivo, usuário, data/hora; soft delete |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta de auditoria) |
| Eventos rejeitados | Todos |
| Guard Conditions | — (a guarda ocorre na origem: somente Rascunho pode ser cancelado) |
| Side Effects | Timeline final; indicador de rascunhos cancelados |
| SLA | — |
| Responsável | Sistema (a partir da ação do operador) |
| Auditoria | Cancelamento com motivo, usuário, IP e dispositivo |

---

# 5. Especificação dos Estados — Reserva

## ST-IV-010 — Ativa (reserva)

A quantidade está bloqueada: saiu do disponível e compõe o reservado (IV-BR-020). Contagem de validade em curso (IV-BR-021).

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Transferir quantidade do disponível para o reservado na mesma transação; calcular `expires_at` (criação + `materials.inventory.reservation.ttl`); publicar Reserva criada; vincular solicitação de origem (MMS-003); gravar tamanho quando aplicável (IV-BR-120) |
| Exit Actions | Validar destino: atendimento exige reserva vinculada e quantidade ≤ remanescente (IV-BR-023); liberação manual com motivo quando parametrizado (IV-BR-022) |
| Eventos aceitos | FulfillReservation (total/parcial), ReleaseReservation, ExpireReservation (job), AddComment |
| Eventos rejeitados | CreateReservation (já existe), edição de quantidade/item/tamanho (exige liberação + nova reserva — IV-BR-121) |
| Guard Conditions | **FulfillReservation:** reserva ativa; quantidade entregue ≤ remanescente; mesmo tamanho (IV-BR-121); segregação preservada (IV-BR-060) · **ReleaseReservation:** motivo quando `release-reason-required=true` · **ExpireReservation:** `now ≥ expires_at` |
| Side Effects | Alerta de reserva a vencer na janela configurada (IV-BR-082); alertas de mínimo/ruptura reavaliados na criação |
| SLA | Validade parametrizável (`materials.inventory.reservation.ttl`; padrão 72h); atendimento ideal dentro da validade |
| Responsável | Almoxarife (separação/atendimento); Sistema (vencimento) |
| Auditoria | Criação e todas as transições com saldo disponível/reservado anterior/posterior |

---

## ST-IV-011 — Atendida (reserva)

Estado terminal. A reserva foi baixada por saída por atendimento (total ou pela última parcela parcial).

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Baixar reservado e total no mesmo ato atômico da saída (IV-BR-023); vincular o documento de saída; publicar Reserva atendida |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta) |
| Eventos rejeitados | Todos |
| Guard Conditions | — |
| Side Effects | Timeline final; KPI de atendimento pelo estoque (com MMS-003) |
| SLA | — |
| Responsável | Sistema (efeito da saída) |
| Auditoria | Vínculo reserva × documento de saída com saldos anterior/posterior |

---

## ST-IV-012 — Liberada (reserva)

Estado terminal. A reserva foi desfeita manualmente (cancelamento da solicitação, desistência) e a quantidade retornou ao disponível.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Devolver quantidade remanescente ao disponível; registrar motivo quando exigido (IV-BR-022); publicar Reserva liberada |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta) |
| Eventos rejeitados | Todos |
| Guard Conditions | — |
| Side Effects | Alertas de mínimo reavaliados; timeline |
| SLA | — |
| Responsável | Almoxarife/Sistema (origem MMS-003) |
| Auditoria | Liberação com motivo, usuário, data/hora, saldos anterior/posterior |

---

## ST-IV-013 — Vencida (reserva)

Estado terminal. A validade expirou sem atendimento integral; o job de vencimento emitiu o documento de liberação correspondente (IV-BR-021).

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Emitir documento de liberação por vencimento (nunca escrita direta — IV-BR-001); devolver quantidade remanescente ao disponível; publicar Reserva vencida; notificar almoxarifado e solicitante (via MMS-003) |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta) |
| Eventos rejeitados | Todos |
| Guard Conditions | — (transição exclusiva do job de vencimento) |
| Side Effects | KPI "reservas vencidas sem atendimento"; alerta de liberação por vencimento |
| SLA | Processamento do job em até 15 minutos após o vencimento |
| Responsável | Sistema (job de vencimento) |
| Auditoria | Vencimento com timestamp do job, saldos anterior/posterior e correlationId do ciclo do job |

---

# 6. Especificação dos Estados — Ajuste

## ST-IV-020 — Pendente de Aprovação (ajuste)

Ajuste registrado aguardando decisão (modo `approval-required=true` — IV-BR-041). **Não afeta saldo** neste estado.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Validar justificativa obrigatória e tipo de motivo (IV-BR-040); validar quantidade e viabilidade de saldo (IV-BR-004 para ajuste negativo); registrar registrante; abrir tarefa de aprovação (FD-001-04); publicar Ajuste registrado |
| Exit Actions | Validar decisão: aprovador ≠ registrante (IV-BR-041); rejeição exige motivo |
| Eventos aceitos | ApproveAdjustment, RejectAdjustment, AddComment |
| Eventos rejeitados | UpdateAdjustment (edição exige rejeição e novo registro), ReverseAdjustment |
| Guard Conditions | **ApproveAdjustment:** aprovador autorizado e ≠ registrante; saldo ainda viável no momento da aprovação (revalidação IV-BR-004) · **RejectAdjustment:** motivo obrigatório |
| Side Effects | Notificação ao aprovador; SLA de aprovação monitorado |
| SLA | Aprovação em até 24h úteis (indicador operacional) |
| Responsável | Supervisor de Almoxarifado / Gerente de Suprimentos (aprovador) |
| Auditoria | Registro com justificativa e registrante; decisão com aprovador, data/hora e motivo |

---

## ST-IV-021 — Aprovado/Confirmado (ajuste)

O ajuste produziu seu efeito no saldo. Imutável; estornável (IV-BR-003 aplicado a ajustes).

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Aplicar efeito no saldo na mesma transação da aprovação (ou do registro, quando approval não exigida); gravar saldo anterior/posterior (IV-BR-042); publicar Ajuste aprovado; vincular inventário de origem quando houver (IV-BR-072); avaliar alertas |
| Exit Actions | Validar estorno: motivo obrigatório; estorno de ajuste também passa por aprovação quando parametrizado |
| Eventos aceitos | ReverseAdjustment, AddComment, consultas |
| Eventos rejeitados | Qualquer edição; ApproveAdjustment/RejectAdjustment (já decidido) |
| Guard Conditions | **ReverseAdjustment:** motivo; documento ainda não estornado; aprovação quando parametrizada |
| Side Effects | Saldo corrigido; KPI de divergências; timeline do ajuste, do item e do inventário |
| SLA | Efeito imediato na confirmação |
| Responsável | Sistema (efeito); Supervisor/Gerente (decisão) |
| Auditoria | Registro completo: justificativa, registrante, aprovador, saldos anterior/posterior, vínculos (IV-BR-042) |

---

## ST-IV-022 — Rejeitado (ajuste)

Estado terminal. O ajuste foi negado; **nenhum efeito sobre saldo** foi produzido.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Registrar motivo da rejeição e aprovador; publicar Ajuste rejeitado; notificar o registrante |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta) |
| Eventos rejeitados | Todos |
| Guard Conditions | — |
| Side Effects | Timeline final; divergência de inventário associada retorna para tratativa (nova proposta) |
| SLA | — |
| Responsável | Supervisor/Gerente (decisão) |
| Auditoria | Rejeição com motivo, aprovador, data/hora |

---

## ST-IV-023 — Estornado (ajuste)

Estado terminal. Ajuste confirmado revertido por estorno vinculado (também aprovado quando parametrizado).

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Vincular ajuste de estorno; registrar motivo, `reversed_at`/`reversed_by`; publicar Estorno registrado |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta) |
| Eventos rejeitados | Todos |
| Guard Conditions | — |
| Side Effects | Timeline final; extrato mantém ajuste e estorno consultáveis |
| SLA | — |
| Responsável | Sistema (a partir da ação autorizada e aprovada) |
| Auditoria | Estorno com motivo, aprovador (quando houver), saldos anterior/posterior |

---

# 7. Especificação dos Estados — Inventário

## ST-IV-030 — Aberto (inventário)

Inventário criado com escopo (curva ABC ou geral), responsável e prazo (IV-BR-070). Nenhuma contagem registrada ainda.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Validar escopo não vazio e sem sobreposição com outro inventário aberto (IV-BR-070); materializar a lista de itens × locais do escopo; publicar Inventário iniciado; quando `freeze=true`, sinalizar bloqueio de movimentação do escopo (IV-BR-073) |
| Exit Actions | Validar início da contagem: responsável designado |
| Eventos aceitos | StartCounting, CancelInventory, AddComment |
| Eventos rejeitados | CloseInventory (sem contagens), RegisterCount (antes do início) |
| Guard Conditions | **CancelInventory:** motivo obrigatório; zero contagens registradas |
| Side Effects | Notificação ao almoxarifado do escopo; KPI de cobertura de inventário |
| SLA | Prazo definido na abertura (parâmetro operacional) |
| Responsável | Supervisor de Almoxarifado |
| Auditoria | Abertura com escopo, responsável, prazo e autor |

---

## ST-IV-031 — Em Contagem (inventário)

Contagens sendo registradas por item × local (IV-BR-071). Saldo **não é alterado** neste estado.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Registrar início (`started_at`); habilitar registro de contagens do escopo |
| Exit Actions | Validar encerramento: todos os itens do escopo contados ou dispensados com motivo; divergências acima da tolerância com proposta de ajuste gerada (IV-BR-072) |
| Eventos aceitos | RegisterCount, RecountItem, CloseInventory, AddComment |
| Eventos rejeitados | StartCounting (já em contagem), qualquer alteração de saldo pela contagem |
| Guard Conditions | **RegisterCount:** item × local dentro do escopo; quantidade ≥ 0; snapshot do saldo do sistema no instante da contagem (IV-BR-073) · **CloseInventory:** 100% do escopo tratado |
| Side Effects | Divergências calculadas contra o snapshot; propostas de ajuste geradas no fechamento; alerta de movimentação durante inventário ao responsável (IV-BR-073) |
| SLA | Conclusão dentro do prazo do inventário |
| Responsável | Almoxarife (contador); Supervisor (encerramento) |
| Auditoria | Cada contagem com contador, data/hora, quantidade e snapshot do saldo de referência |

---

## ST-IV-032 — Fechado (inventário)

Estado terminal. Escopo totalmente tratado; divergências viraram ajustes (aprovados conforme IV-BR-041) ou foram dispensadas dentro da tolerância.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Gerar propostas de ajuste vinculadas para divergências acima da tolerância (IV-BR-072); publicar Inventário fechado com sumário (contados, divergentes, ajustados, dentro da tolerância); liberar freeze quando ativo; calcular acuracidade do ciclo |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta) |
| Eventos rejeitados | Todos |
| Guard Conditions | — |
| Side Effects | KPI de acuracidade (meta ≥ 98%) atualizado; KPI de divergências por ciclo |
| SLA | — |
| Responsável | Sistema (a partir do encerramento do Supervisor) |
| Auditoria | Fechamento com sumário, responsável, data/hora e vínculos dos ajustes gerados |

---

## ST-IV-033 — Cancelado (inventário)

Estado terminal. Inventário descartado antes de qualquer contagem.

| Aspecto | Definição |
|---------|-----------|
| Entry Actions | Registrar motivo, usuário, data/hora; liberar freeze quando ativo; soft delete |
| Exit Actions | Nenhuma (estado terminal) |
| Eventos aceitos | Nenhum (somente consulta de auditoria) |
| Eventos rejeitados | Todos |
| Guard Conditions | — (a guarda ocorre na origem: somente Aberto sem contagens pode ser cancelado) |
| Side Effects | Timeline final; indicador de inventários cancelados |
| SLA | — |
| Responsável | Supervisor de Almoxarifado |
| Auditoria | Cancelamento com motivo, usuário, IP e dispositivo |

---

# 8. Fluxos Principais

**Documento de Movimentação:**
Rascunho → (ConfirmMovement) → Confirmado

**Reserva:**
Ativa → (FulfillReservation) → Atendida

**Ajuste (approval-required=true):**
Pendente de Aprovação → (ApproveAdjustment) → Aprovado/Confirmado

**Inventário:**
Aberto → (StartCounting) → Em Contagem → (CloseInventory) → Fechado

---

# 9. Fluxos Alternativos

**Documento:**
Rascunho → (CancelMovement) → Cancelado
Confirmado → (ReverseMovement) → Estornado

**Reserva:**
Ativa → (ReleaseReservation) → Liberada
Ativa → (ExpireReservation — job) → Vencida
Ativa → (FulfillReservation parcial) → permanece Ativa com remanescente reduzido

**Ajuste:**
Pendente de Aprovação → (RejectAdjustment) → Rejeitado
Aprovado/Confirmado → (ReverseAdjustment) → Estornado
(approval-required=false): registro → Aprovado/Confirmado diretamente

**Inventário:**
Aberto → (CancelInventory) → Cancelado

---

# 10. Matriz de Transições

## 10.1 Documento de Movimentação

| Origem | Destino | Permitido | Guard Condition | Evento |
|--------|---------|-----------|-----------------|--------|
| Rascunho | Confirmado | Sim | Regras do tipo + saldo/disponível (IV-BR-004/005) + segregação (IV-BR-060) + item/local/tamanho + freeze (IV-BR-073) | Entrada/Saída/Transferência registrada |
| Rascunho | Cancelado | Sim | Zero efeitos | — |
| Confirmado | Estornado | Sim | Motivo + permissão de estorno (IV-BR-003) | Estorno registrado |
| Confirmado | Cancelado | Não | — | — |
| Estornado | qualquer | Não | — | — |
| Cancelado | qualquer | Não | — | — |

## 10.2 Reserva

| Origem | Destino | Permitido | Guard Condition | Evento |
|--------|---------|-----------|-----------------|--------|
| Ativa | Atendida | Sim | Saída vinculada, quantidade ≤ remanescente, tamanho igual (IV-BR-023/121) | Reserva atendida |
| Ativa | Liberada | Sim | Motivo quando parametrizado (IV-BR-022) | Reserva liberada |
| Ativa | Vencida | Sim | `now ≥ expires_at` (job — IV-BR-021) | Reserva vencida |
| Atendida | qualquer | Não | — | — |
| Liberada | qualquer | Não | — | — |
| Vencida | qualquer | Não | — | — |

## 10.3 Ajuste

| Origem | Destino | Permitido | Guard Condition | Evento |
|--------|---------|-----------|-----------------|--------|
| Pendente | Aprovado | Sim | Aprovador ≠ registrante; revalidação de saldo (IV-BR-041/004) | Ajuste aprovado |
| Pendente | Rejeitado | Sim | Motivo obrigatório | Ajuste rejeitado |
| Aprovado | Estornado | Sim | Motivo + aprovação quando parametrizada | Estorno registrado |
| Rejeitado | qualquer | Não | — | — |
| Estornado | qualquer | Não | — | — |

## 10.4 Inventário

| Origem | Destino | Permitido | Guard Condition | Evento |
|--------|---------|-----------|-----------------|--------|
| Aberto | Em Contagem | Sim | Responsável designado | — |
| Aberto | Cancelado | Sim | Motivo + zero contagens | — |
| Em Contagem | Fechado | Sim | 100% do escopo tratado; divergências com ajuste proposto (IV-BR-072) | Inventário fechado |
| Em Contagem | Cancelado | Não | — | — |
| Fechado | qualquer | Não | — | — |
| Cancelado | qualquer | Não | — | — |

## 10.5 Sugestão de Reposição (ADR-014)

| Origem | Destino | Permitido | Guard Condition | Evento |
|--------|---------|-----------|-----------------|--------|
| Sugerida | Confirmada | Sim | Rota válida para a classificação (IV-BR-101/131); auto-execução só com `auto-execute=true` (v2.0) | Sugestão de reposição confirmada |
| Sugerida | Descartada | Sim | Decisão do responsável (motivo opcional) | Sugestão de reposição descartada |
| Confirmada | qualquer | Não | — | — |
| Descartada | qualquer | Não | — | — |

---

# 11. Eventos de Domínio

Conforme o catálogo funcional da visão do módulo (MMS-004 README — Eventos Publicados); a especificação técnica (envelope, payload) seguirá ADR-010 no documento de eventos do módulo (MMS-004-05):

| Evento | Transição/ação de origem |
|--------|--------------------------|
| Entrada registrada | Rascunho→Confirmado (documento tipo entrada) |
| Saída registrada | Rascunho→Confirmado (documento tipo saída) |
| Transferência registrada | Rascunho→Confirmado (documento tipo transferência) |
| Estorno registrado | Confirmado→Estornado (documento ou ajuste) |
| Reserva criada | criação → Ativa |
| Reserva atendida | Ativa→Atendida |
| Reserva liberada | Ativa→Liberada |
| Reserva vencida | Ativa→Vencida |
| Ajuste registrado | criação → Pendente |
| Ajuste aprovado | Pendente→Aprovado (ou registro direto) |
| Ajuste rejeitado | Pendente→Rejeitado |
| Inventário iniciado | criação → Aberto |
| Contagem registrada | RegisterCount (Em Contagem) |
| Divergência aprovada | via Ajuste aprovado vinculado ao inventário |
| Alerta de estoque mínimo | avaliação pós-confirmação (IV-BR-080) |
| Alerta de ruptura | condição disponível zero com demanda aberta (IV-BR-081) |
| Sugestão de reposição gerada | criação → Sugerida (avaliação IV-BR-130) |
| Sugestão de reposição confirmada | Sugerida→Confirmada (IV-BR-131) |
| Sugestão de reposição descartada | Sugerida→Descartada |

---

# 12. Restrições

Não permitido:

- Editar qualquer campo de documento Confirmado, Estornado ou Cancelado (IV-BR-003);
- Cancelar documento Confirmado (correção apenas por estorno);
- Estornar documento já estornado ou rascunho;
- Editar quantidade, item ou tamanho de reserva Ativa (liberar + reservar novamente — IV-BR-121);
- Atender reserva Liberada ou Vencida;
- Reativar reserva em qualquer estado final (nova reserva é novo documento);
- Aprovador aprovar o próprio ajuste (IV-BR-041);
- Editar ajuste Pendente (rejeitar e registrar novo);
- Registrar contagem fora do escopo ou fora do estado Em Contagem (IV-BR-071);
- Fechar inventário com escopo não tratado;
- Cancelar inventário com contagens registradas;
- Alterar sugestão de reposição já Confirmada ou Descartada; efeito de saldo por sugestão (a sugestão nunca movimenta saldo — IV-BR-130/131);
- Alterar qualquer estado diretamente no banco (MMS-001 §15);
- Efeito de saldo por contagem (contagem alimenta divergência, nunca saldo — IV-BR-071);
- Qualquer transição sem auditoria (IV-BR-090).

---

# 13. Auditoria

Cada transição de estado registra (FD-001-06, IV-BR-090):

- usuário (ou job) e papel;
- data e hora;
- estado anterior e novo estado;
- motivo (obrigatório em estorno, cancelamento, liberação quando parametrizada, rejeição);
- saldo anterior/posterior quando a transição afeta saldo;
- vínculos (documento de origem, reserva, inventário, documento estornado);
- correlationId;
- IP e dispositivo (ações humanas).

---

# 14. Timeline

Cada transição gera automaticamente (FD-001-07, IV-BR-091):

- marco na Timeline do documento/reserva/ajuste/inventário;
- marco na Timeline do item afetado;
- registro de auditoria;
- evento de domínio;
- notificação quando aplicável (aprovador de ajuste, reserva vencendo/vencida, alertas — especificação no MMS-004-10).

---

# 15. APIs Relacionadas (conceituais — especificação no MMS-004-13)

POST /api/v1/inventory/movements

PATCH /api/v1/inventory/movements/{id}

POST /api/v1/inventory/movements/{id}/confirm

POST /api/v1/inventory/movements/{id}/cancel

POST /api/v1/inventory/movements/{id}/reverse

POST /api/v1/inventory/reservations

POST /api/v1/inventory/reservations/{id}/release

POST /api/v1/inventory/adjustments

POST /api/v1/inventory/adjustments/{id}/approve

POST /api/v1/inventory/adjustments/{id}/reject

POST /api/v1/inventory/counts

POST /api/v1/inventory/counts/{id}/start

POST /api/v1/inventory/counts/{id}/entries

POST /api/v1/inventory/counts/{id}/close

POST /api/v1/inventory/counts/{id}/cancel

---

# 16. Regras Relacionadas

IV-BR-001/002 (saldo derivado, documento obrigatório)

IV-BR-003 (imutabilidade e estorno)

IV-BR-004/005 (saldo negativo bloqueado; validação pelo disponível)

IV-BR-007/008/009 (item ativo, local válido, quantidade)

IV-BR-020..023 (reserva: criação, validade, liberação, baixa)

IV-BR-030/031 (transferência atômica e segregada)

IV-BR-040..042 (ajuste: justificativa, aprovação, auditoria)

IV-BR-060 (segregação cliente/contrato)

IV-BR-070..073 (inventário: abertura, contagem, divergência, freeze)

IV-BR-090/091 (auditoria e timeline)

IV-BR-120/121 (tamanho em reserva, separação e entrega)

---

# 17. Casos de Uso Relacionados (conceituais — especificação no MMS-004-07)

UC-IV-001 (Registrar entrada)

UC-IV-002 (Registrar saída / atender reserva)

UC-IV-003 (Criar reserva)

UC-IV-004 (Liberar reserva / processar vencimento)

UC-IV-005 (Transferir entre locais)

UC-IV-006 (Registrar e aprovar ajuste)

UC-IV-007 (Executar inventário)

UC-IV-008 (Estornar documento)

---

# 18. Casos de Teste (conceituais — especificação no MMS-004-17)

TC-IV-003 (imutabilidade e estorno)

TC-IV-020..023 (transições de reserva)

TC-IV-021 (vencimento automático pelo job)

TC-IV-040/041 (aprovação e segregação de ajuste)

TC-IV-070..073 (ciclo de inventário)

TC-IV-121 (bloqueio de troca de tamanho no atendimento)

---

# 19. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação da State Machine do Inventory Management: 4 entidades com ciclo de vida (Documento de Movimentação 4 estados, Reserva 4 estados, Ajuste 4 estados, Inventário 4 estados) com entry/exit actions, eventos aceitos/rejeitados, guard conditions, side effects, SLA, responsável e auditoria por estado; fluxos principais e alternativos; 4 matrizes de transição; eventos de domínio; restrições; rastreabilidade com regras IV-BR, UCs, APIs e testes conceituais — no padrão MMS-002-03, derivada da visão do módulo e das Business Rules (MMS-004-02) |
| 1.1.0 | 2026-08-08 | Incorporação de ADR-014: nova entidade **Sugestão de Reposição** (5ª entidade) com 3 estados (ST-IV-040 Sugerida, ST-IV-041 Confirmada, ST-IV-042 Descartada), matriz de transição §10.5, eventos (gerada/confirmada/descartada) e restrições (a sugestão nunca movimenta saldo; auto-execução opt-in na v2.0) — IV-BR-130/131 |
