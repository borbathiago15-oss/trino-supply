**Documento:** PR-001-07 — Use Cases
**Versão:** 1.1.0
**Status:** Approved

# PR-001-07 — Use Cases

> Especificação dos Casos de Uso do módulo Purchase Requisition.
> Cada caso de uso segue especificação UML completa: fluxo principal, fluxos alternativos, fluxos de exceção, pré/pós-condições, regras, eventos, mensagens, validações, APIs, permissões e testes relacionados.

---

# 0. Convenções da Especificação

| Campo | Significado |
| ----- | ----------- |
| **APIs** | Endpoints REST conforme contrato do módulo (PR-001 API). Todas exigem autenticação JWT e propagam `X-Correlation-Id` |
| **Permissões** | Códigos PR-PERM conforme PR-001-09; toda decisão registra auditoria de autorização |
| **Eventos** | Códigos EVT conforme PR-001-05 §14 (payload e garantias) |
| **Mensagens** | Mensagens de negócio exibidas ao usuário (chaves i18n `pr.uc.*`) |
| **Testes** | Códigos TC-UC-xxx-y: cenários de teste de aceite vinculados ao caso de uso |
| **Erros** | Códigos PR-ERR conforme PR-001-02 (Business Rules) |

---

# UC-001 — Criar Solicitação de Compra

## Objetivo

Permitir que um solicitante registre uma nova necessidade de aquisição.

## Atores

- Solicitante (primário)
- Sistema (secundário — numeração, auditoria)

## Pré-condições

- Usuário autenticado.
- Usuário com permissão para criar requisições.
- Empresa e unidade válidas.

## Pós-condições

- Requisição criada em estado **Draft**.
- Número único gerado no escopo da empresa (sequência por empresa).
- Registro de auditoria e entrada de Timeline criados.

## Gatilho

O usuário identifica uma necessidade de compra.

## Fluxo Principal

1. Selecionar "Nova Solicitação".
2. Informar os dados gerais.
3. Salvar a requisição.

**Detalhamento do passo 2:** o usuário informa justificativa (obrigatória), data necessária (obrigatória), centro de custo (obrigatório), prioridade (padrão: Normal), e opcionalmente projeto e unidade de negócio (default: unidade do usuário).

**Detalhamento do passo 3:** o sistema valida os campos (ver Validações), gera o número sequencial da requisição, persiste o aggregate em estado Draft, publica EVT-001 e registra Timeline/Auditoria.

## Fluxos Alternativos

A1. Cancelar criação.
- A1.1. O usuário abandona o formulário sem salvar. Nenhum dado é persistido; nenhum evento é publicado.

A2. Salvar rascunho parcial.
- A2.1. O usuário salva apenas com campos mínimos de rascunho. Campos obrigatórios de submissão (PR-BR-020) serão exigidos somente no UC-003.

A3. Unidade de negócio diferente da padrão.
- A3.1. O usuário seleciona outra unidade dentro de seu escopo organizacional. O sistema valida o escopo antes de aceitar.

## Fluxos de Exceção

E1. Usuário sem permissão.
- E1.1. Sistema recusa a operação com `PR-ERR-001` (permissão insuficiente); decisão de autorização auditada (PR-001-09 §10).

E2. Centro de custo inválido ou inativo.
- E2.1. Sistema recusa com `PR-ERR-021` e mensagem MSG-UC-001-B.

E3. Data necessária inválida (passada ou abaixo da antecedência mínima configurada).
- E3.1. Em Draft, o sistema aceita o salvamento com aviso (warning); o bloqueio ocorre no UC-003 (PR-BR-050).

E4. Falha na geração do número sequencial.
- E4.1. Operação abortada com rollback completo; erro técnico registrado; nenhum evento publicado.

## Regras

- PR-BR-001
- PR-BR-004
- PR-BR-006
- PR-BR-021 (centro de custo — validação de existência)
- PR-BR-022 (projeto — validação quando informado)

## Eventos

- PurchaseRequisitionCreated (EVT-001)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-001-A | "Solicitação {number} criada com sucesso." |
| MSG-UC-001-B | "Centro de custo inválido ou inativo para a unidade selecionada." |
| MSG-UC-001-C | "A data necessária está no passado. Revise antes do envio para aprovação." |

## Validações

- Justificativa: obrigatória, 10–2.000 caracteres.
- Data necessária: obrigatória, formato ISO; warning se no passado.
- Centro de custo: obrigatório, existente e ativo no escopo da empresa/unidade.
- Projeto: opcional; se informado, deve existir e estar ativo.
- Prioridade: enum válido (Low, Normal, High, Critical).

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions` | Cria a requisição em Draft; retorna `201` com `id` e `number` |

## Permissões

- PR-PERM-001 (Criar requisição), avaliada no escopo empresa/unidade do usuário.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-001-1 | Criação com todos os campos válidos → Draft + EVT-001 |
| TC-UC-001-2 | Criação sem permissão → PR-ERR-001 + auditoria de negação |
| TC-UC-001-3 | Centro de custo inativo → PR-ERR-021 |
| TC-UC-001-4 | Cancelamento de criação → nenhum dado persistido |
| TC-UC-001-5 | Numeração: duas criações concorrentes na mesma empresa → números únicos e sequenciais |

---

# UC-002 — Adicionar Item

## Objetivo

Adicionar um item à requisição.

## Atores

- Solicitante (primário)

## Pré-condições

- Requisição existente em estado **Draft** (editável).
- Usuário é o solicitante da requisição ou possui permissão de edição no escopo.
- Usuário autenticado com permissão PR-PERM-002.

## Pós-condições

- Item persistido com `sequence` incremental dentro da requisição.
- `totalEstimatedValue` da requisição recalculado (quando preço estimado informado).
- Timeline e Auditoria registradas.

## Gatilho

O usuário seleciona "Adicionar Item" em uma requisição editável.

## Fluxo Principal

1. Selecionar "Adicionar Item".
2. Informar descrição.
3. Informar quantidade.
4. Informar unidade.
5. Confirmar.

**Detalhamento:** o usuário pode informar adicionalmente tipo do item (Material/Serviço), preço unitário estimado, categoria, centro de custo e projeto do item (herdados da requisição quando omitidos).

## Fluxos Alternativos

A1. Herança de classificação da requisição.
- A1.1. Centro de custo e projeto do item são herdados do cabeçalho quando não informados explicitamente.

A2. Item de serviço.
- A2.1. Para `itemType = Service`, o sistema aceita unidades de medida de serviço (ex.: hora, mês) e não exige categoria de material.

A3. Edição de item recém-adicionado.
- A3.1. Imediatamente após adicionar, o usuário altera o item na mesma sessão. Aplica-se UC de edição de item com as mesmas validações.

## Fluxos de Exceção

E1. Quantidade inválida (≤ 0 ou acima do teto configurado).
- E1.1. Recusa com `PR-ERR-010` e MSG-UC-002-A.

E2. Descrição ausente ou fora do tamanho permitido.
- E2.1. Recusa com `PR-ERR-011`.

E3. Unidade de medida ausente ou incompatível com o tipo do item.
- E3.1. Recusa com `PR-ERR-012`.

E4. Requisição não editável (estado diferente de Draft).
- E4.1. Recusa com `PR-ERR-040` (transição de estado inválida); mensagem orienta a consultar a timeline.

E5. Categoria inválida.
- E5.1. Recusa com `PR-ERR-040-C` quando categoria informada não existe ou está inativa.

## Regras

- PR-BR-010
- PR-BR-011
- PR-BR-012
- PR-BR-040 (categoria)

## Eventos

- ItemAdded (EVT-003)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-002-A | "A quantidade deve ser maior que zero." |
| MSG-UC-002-B | "Item adicionado à solicitação {number}." |
| MSG-UC-002-C | "A requisição não está em edição. Consulte o status atual." |

## Validações

- Quantidade > 0.
- Descrição obrigatória.
- Unidade obrigatória.
- Preço unitário estimado ≥ 0 quando informado; `estimatedTotalPrice = quantity × estimatedUnitPrice` (arredondamento: half-up, 2 casas).
- Categoria (quando informada): existente e ativa.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions/{id}/items` | Adiciona item; retorna `201` com `itemId` e `sequence` |
| PATCH | `/api/v1/purchase-requisitions/{id}/items/{itemId}` | Edita item existente em Draft |
| DELETE | `/api/v1/purchase-requisitions/{id}/items/{itemId}` | Remove item (publica EVT-004) |

## Permissões

- PR-PERM-002 (Editar requisição), restrita ao estado Draft e ao escopo organizacional.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-002-1 | Adição válida → item persistido + EVT-003 + total recalculado |
| TC-UC-002-2 | Quantidade zero → PR-ERR-010 |
| TC-UC-002-3 | Adição em estado Submitted → PR-ERR-040 |
| TC-UC-002-4 | Herança de centro de custo do cabeçalho |
| TC-UC-002-5 | Sequência incremental: 3 itens → sequences 1, 2, 3 |

---

# UC-003 — Enviar para Aprovação

## Objetivo

Submeter a requisição para validação e workflow.

## Atores

- Solicitante (primário)
- Sistema (validação automática, workflow)

## Pré-condições

- Requisição em estado **Draft** ou **Returned**.
- Ao menos 1 item ativo na requisição.
- Usuário autenticado com permissão PR-PERM-004 no escopo da requisição.

## Pós-condições

Status:

Submitted

- Validação automática executada (EVT-006/EVT-007).
- Em caso de sucesso: workflow instanciado (EVT-008) e aprovadores notificados.
- Em caso de falha de validação: requisição em Returned com pendências listadas.

## Gatilho

O usuário seleciona "Enviar".

## Fluxo Principal

1. Usuário seleciona "Enviar".
2. Sistema valida os dados.
3. Sistema identifica o workflow.
4. Sistema altera o status.
5. Sistema inicia aprovações.

**Detalhamento:** o passo 2 executa o conjunto completo de validações de submissão (ver Validações); o passo 3 resolve a cadeia de aprovadores conforme RG-GW-003 (PR-001-06 §9.3); o passo 4 transiciona o estado conforme State Machine (PR-001-03); o passo 5 publica EVT-008 e dispara notificações (POL-004).

## Fluxos Alternativos

A1. Campos obrigatórios ausentes.
- A1.1. Sistema retorna a requisição com a lista de pendências (EVT-011, origem Validation); usuário corrige e reenvia (retorna ao passo 1).

A2. Workflow inexistente.
- A2.1. Sistema suspende o envio com EXC-001 (PR-001-06 §11.1); notifica Administrador; requisição retorna a Draft para novo envio após configuração.

A3. Reenvio após retorno para ajuste.
- A3.1. Requisição em Returned é reenviada; o sistema incrementa o contador de ciclo, instancia nova cadeia de aprovação completa (COMP-003) e registra o ciclo na Timeline.

## Fluxos de Exceção

E1. Usuário sem permissão de submissão no escopo.
- E1.1. Recusa com `PR-ERR-001`; auditoria de negação.

E2. Requisição sem itens.
- E2.1. Recusa com `PR-ERR-020` (ao menos um item obrigatório).

E3. Data necessária inválida no momento do envio.
- E3.1. Recusa com `PR-ERR-050`.

E4. Violação de concorrência otimista (aggregate alterado por outra sessão).
- E4.1. Recusa com `PR-ERR-409` (conflito de versão); usuário recarrega os dados.

E5. Falha técnica na validação (timeout).
- E5.1. EXC-007 → COMP-001 (até 3 reexecuções; depois, Returned com motivo técnico).

## Regras

- PR-BR-020
- PR-BR-030
- PR-BR-021, PR-BR-022, PR-BR-040, PR-BR-050 (validações de submissão)

## Eventos

- PurchaseRequisitionSubmitted (EVT-005)
- ValidationStarted (EVT-006) / ValidationCompleted (EVT-007)
- ApprovalStarted (EVT-008)
- PurchaseRequisitionReturned (EVT-011, em falha)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-003-A | "Solicitação {number} enviada para aprovação." |
| MSG-UC-003-B | "Existem pendências que impedem o envio: {failures}." |
| MSG-UC-003-C | "Não existe workflow configurado para esta solicitação. O administrador foi notificado." |

## Validações

- Campos obrigatórios completos (justificativa, data, centro de custo).
- Ao menos 1 item ativo com quantidade e unidade válidas.
- Centro de custo e projeto ativos.
- Categorias dos itens válidas.
- Data necessária válida (não passada; antecedência mínima).
- Permissão de submissão no escopo.
- Workflow disponível para a combinação empresa/unidade/valor/categoria.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions/{id}/submit` | Submete a requisição; retorna `202` (processamento assíncrono da validação) ou `200` com resultado |

## Permissões

- PR-PERM-004 (Enviar para aprovação).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-003-1 | Envio válido → Submitted + EVT-005/006/007/008 + notificação |
| TC-UC-003-2 | Envio sem itens → PR-ERR-020 |
| TC-UC-003-3 | Workflow inexistente → EXC-001 + notificação ao Administrador |
| TC-UC-003-4 | Reenvio após Returned → ciclo incrementado, nova cadeia instanciada |
| TC-UC-003-5 | Conflito de versão → PR-ERR-409 |
| TC-UC-003-6 | Falha de validação → EVT-011 com lista de pendências |

---

# UC-004 — Aprovar Solicitação

## Objetivo

Registrar a aprovação de uma requisição.

## Atores

- Gestor (primário — aprovador do nível atual)

## Pré-condições

- Requisição em estado **Approval** (Waiting Approval) com nível pendente atribuído ao aprovador (direto, delegado ou escalonado).
- Usuário autenticado com permissão PR-PERM-005 no escopo.
- Segregation of Duties satisfeita (aprovador ≠ solicitante; alçada suficiente — RG-GW-002-E).

## Pós-condições

Status:

Approved

ou

Waiting Approval (caso existam outros níveis).

- Decisão registrada em Approval (BO-004) com parecer.
- Se último nível: EVT-009 publicado, requisição liberada para Compras e comprador notificado.
- Se níveis restantes: próximo nível ativado e seu aprovador notificado.

## Gatilho

O aprovador abre uma aprovação pendente na fila de trabalho.

## Fluxo Principal

1. Abrir aprovação pendente.
2. Revisar informações.
3. Aprovar.
4. Registrar parecer.

**Detalhamento do passo 2:** o aprovador visualiza dados gerais, itens, anexos, comentários e timeline da requisição antes de decidir.

**Detalhamento do passo 3:** o sistema valida SoD e alçada, registra a decisão e verifica se o nível está completo (em paralelismo, aplica `approval.parallel.policy`); concluído o nível, ativa o próximo ou finaliza o fluxo.

## Fluxos Alternativos

A1. Aprovação de nível intermediário.
- A1.1. Existem níveis restantes → status Waiting Approval; EVT de progresso de nível registrado na Timeline; próximo aprovador notificado.

A2. Aprovação em nível paralelo (política ANY).
- A2.1. Primeira aprovação conclui o nível; demais pendências do nível são canceladas com registro.

A3. Aprovação por delegado.
- A3.1. Delegado vigente aprova em nome do delegante; auditoria registra `delegatedBy` e a delegação utilizada (PR-001-09 §9).

## Fluxos de Exceção

E1. Aprovador é o próprio solicitante.
- E1.1. Bloqueio por SoD (EXC-006); auditoria obrigatória; nível redirecionado ao próximo aprovador elegível.

E2. Valor acima da alçada do aprovador.
- E2.1. Bloqueio com EXC-006; escalonamento para aprovador com alçada adequada.

E3. Requisição não está em estado de aprovação (já decidida/cancelada).
- E3.1. Recusa com `PR-ERR-040`; interface atualizada com o estado atual.

E4. Delegação expirada ou inexistente (aprovação em nome de terceiro).
- E4.1. Recusa com `PR-ERR-001`; auditoria de negação.

## Regras

- PR-BR-030 (workflow)
- PR-BR-031 (decisões de aprovação)
- SoD e alçada (PR-001-09 §8)

## Eventos

- PurchaseRequisitionApproved (EVT-009)
- Progresso de nível registrado na Timeline (sem evento de domínio próprio no catálogo atual)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-004-A | "Solicitação {number} aprovada e liberada para Compras." |
| MSG-UC-004-B | "Aprovação registrada. Aguardando os demais níveis." |
| MSG-UC-004-C | "Você não pode aprovar esta solicitação (conflito de interesse ou alçada insuficiente)." |

## Validações

- Estado = Approval com nível pendente do aprovador.
- SoD: aprovador ≠ solicitante; alçada ≥ valor total estimado.
- Delegação vigente (quando aplicável): dentro do período, escopo compatível.
- Versão do aggregate (concorrência otimista).

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions/{id}/approve` | Registra aprovação do nível atual; corpo: `{ comments? }` |
| GET | `/api/v1/approvals/pending?cursor=` | Fila de aprovações pendentes do usuário (keyset pagination) |

## Permissões

- PR-PERM-005 (Aprovar), no escopo organizacional e com SoD/alçada.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-004-1 | Aprovação de nível único → Approved + EVT-009 + handoff |
| TC-UC-004-2 | Aprovação de nível intermediário → Waiting Approval + próximo nível notificado |
| TC-UC-004-3 | SoD: solicitante tenta aprovar → EXC-006 + auditoria |
| TC-UC-004-4 | Alçada insuficiente → escalonamento |
| TC-UC-004-5 | Aprovação por delegado vigente → auditoria com `delegatedBy` |
| TC-UC-004-6 | Paralelismo ANY: primeira aprovação conclui nível, demais canceladas |

---

# UC-005 — Rejeitar Solicitação

## Objetivo

Rejeitar a requisição.

## Atores

- Gestor (primário — aprovador do nível atual)

## Pré-condições

- Requisição em estado **Approval** com nível pendente atribuído ao aprovador.
- Usuário autenticado com permissão PR-PERM-006 no escopo.

## Pós-condições

- Estado terminal **Rejected**; workflow encerrado; demais níveis pendentes cancelados.
- Motivo registrado e visível ao solicitante na Timeline.
- Solicitante notificado.

## Gatilho

O aprovador decide rejeitar a requisição na fila de trabalho.

## Fluxo Principal

1. Abrir requisição.
2. Informar motivo.
3. Confirmar rejeição.

**Detalhamento:** após a confirmação, o sistema registra a decisão, transiciona para Rejected, cancela as aprovações pendentes dos demais níveis e publica EVT-010.

## Fluxos Alternativos

A1. Rejeição em nível intermediário.
- A1.1. A rejeição em qualquer nível encerra o fluxo completo (RG-GW-002-B); níveis subsequentes não são ativados.

A2. Rejeição com sugestão de correção.
- A2.1. O aprovador inclui orientações no motivo; o solicitante pode criar nova requisição referenciando a rejeitada (cópia manual; UC-012 no roadmap automatizará).

## Fluxos de Exceção

E1. Motivo ausente.
- E1.1. Recusa com `PR-ERR-031` (motivo obrigatório); nenhuma alteração de estado.

E2. Requisição já decidida/cancelada por outro ator.
- E2.1. Recusa com `PR-ERR-040`; interface atualizada com o estado atual.

## Validação

Motivo obrigatório.

## Regras

- PR-BR-031 (motivo obrigatório em rejeição)

## Eventos

- PurchaseRequisitionRejected (EVT-010)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-005-A | "Solicitação {number} rejeitada. O solicitante será notificado." |
| MSG-UC-005-B | "Informe o motivo da rejeição." |

## Validações

- Motivo: obrigatório, 10–1.000 caracteres.
- Estado = Approval com nível pendente do aprovador.
- Versão do aggregate (concorrência otimista).

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions/{id}/reject` | Registra rejeição; corpo: `{ reason, comments? }` |

## Permissões

- PR-PERM-006 (Rejeitar), no escopo organizacional.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-005-1 | Rejeição com motivo → Rejected + EVT-010 + notificação |
| TC-UC-005-2 | Rejeição sem motivo → PR-ERR-031, sem mudança de estado |
| TC-UC-005-3 | Rejeição encerra níveis pendentes (cancelamento de BO-004) |
| TC-UC-005-4 | Rejeição após estado final → PR-ERR-040 |

---

# UC-006 — Retornar para Ajustes

## Objetivo

Solicitar correções ao solicitante.

## Atores

- Gestor (primário) ou Sistema (validação automática)

## Pré-condições

- (Gestor) Requisição em estado **Approval** com nível pendente do aprovador.
- (Sistema) Requisição em estado **Validation** com falhas identificadas.
- (Gestor) Permissão PR-PERM-007 no escopo.

## Pós-condições

Resultado

Status:

Returned for Adjustment

Evento:

PurchaseRequisitionReturned

- Cadeia de aprovação suspensa; níveis pendentes cancelados (COMP-003); novo envio instancia nova cadeia completa.
- Solicitante notificado com o detalhamento das correções solicitadas.

## Gatilho

O aprovador seleciona "Solicitar ajuste" ou a validação automática falha.

## Fluxo Principal

1. Abrir requisição (ou validação automática falha).
2. Informar as correções necessárias (motivo obrigatório).
3. Confirmar retorno.
4. Sistema suspende a cadeia e notifica o solicitante.

## Fluxos Alternativos

A1. Retorno pela validação automática.
- A1.1. O sistema preenche automaticamente a lista de pendências (campos/regras que falharam) no motivo do retorno.

A2. Correção e reenvio pelo solicitante.
- A2.1. Solicitante edita a requisição em Returned (campos liberados para edição), reenvia via UC-003; contador de ciclo incrementado.

## Fluxos de Exceção

E1. Motivo/pendências ausentes.
- E1.1. Recusa com `PR-ERR-031` (gestor) ou bloqueio técnico (sistema sempre fornece a lista de falhas).

E2. Requisição não está em estado retornável.
- E2.1. Recusa com `PR-ERR-040`.

## Regras

- PR-BR-031 (motivo obrigatório)
- COMP-003 (suspensão da cadeia — PR-001-06 §15.4)

## Eventos

- PurchaseRequisitionReturned (EVT-011)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-006-A | "Solicitação {number} retornada para ajustes. O solicitante foi notificado." |
| MSG-UC-006-B | "Descreva os ajustes necessários." |

## Validações

- Motivo: obrigatório, 10–1.000 caracteres.
- Estado ∈ {Approval, Validation}.
- Versão do aggregate.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions/{id}/return` | Retorna para ajuste; corpo: `{ reason }` |

## Permissões

- PR-PERM-007 (Retornar para ajuste), no escopo organizacional.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-006-1 | Retorno pelo aprovador → Returned + EVT-011 + notificação |
| TC-UC-006-2 | Retorno pela validação automática com lista de falhas |
| TC-UC-006-3 | Edição em Returned e reenvio → nova cadeia (ciclo 2) |
| TC-UC-006-4 | Retorno sem motivo → PR-ERR-031 |

---

# UC-007 — Cancelar Solicitação

## Objetivo

Cancelar uma requisição.

## Atores

- Solicitante (primário)
- Gestor (secundário — conforme política da empresa)

## Pré-condições

- Requisição em estado cancelável conforme State Machine (PR-001-03): Draft, Returned, Submitted ou Approval (conforme política).
- Usuário autenticado com permissão PR-PERM-008 no escopo.

## Pós-condições

- Estado terminal **Cancelled**.
- Workflow encerrado e aprovações pendentes canceladas (COMP-002).
- Aprovadores pendentes notificados (quando houver).

## Restrições

Não possuir Pedido de Compra.

## Gatilho

O solicitante (ou gestor autorizado) seleciona "Cancelar solicitação".

## Fluxo Principal

1. Abrir requisição.
2. Selecionar "Cancelar".
3. Informar motivo do cancelamento (obrigatório).
4. Confirmar.
5. Sistema verifica restrições (sem Pedido de Compra vinculado), transiciona para Cancelled e publica EVT-012.

## Fluxos Alternativos

A1. Cancelamento de rascunho.
- A1.1. Requisição em Draft é cancelada diretamente, sem workflow ativo; apenas Timeline/Auditoria registradas.

A2. Cancelamento com aprovação em andamento.
- A2.1. EVT-012 consumido pelo Workflow Engine encerra a instância e cancela BO-004 pendentes; aprovadores em fila são notificados do cancelamento.

## Fluxos de Exceção

E1. Requisição possui Pedido de Compra vinculado.
- E1.1. Recusa com `PR-ERR-060`; mensagem orienta contatar o comprador responsável.

E2. Estado não cancelável (Approved em política restritiva, Closed, Rejected, já Cancelled).
- E2.1. Recusa com `PR-ERR-040`.

E3. Usuário sem permissão de cancelamento (ex.: solicitante cancelando após política de bloqueio).
- E3.1. Recusa com `PR-ERR-001`; auditoria de negação.

## Regras

- PR-BR-060 (restrição de Pedido de Compra)
- Restrições por estado (PR-001-09 §6)
- COMP-002 (PR-001-06 §15.4)

## Eventos

PurchaseRequisitionCancelled (EVT-012)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-007-A | "Solicitação {number} cancelada." |
| MSG-UC-007-B | "Esta solicitação não pode ser cancelada pois já possui Pedido de Compra vinculado." |

## Validações

- Motivo: obrigatório, 10–1.000 caracteres.
- Estado cancelável conforme State Machine e política da empresa.
- Ausência de Pedido de Compra vinculado.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions/{id}/cancel` | Cancela a requisição; corpo: `{ reason }` |

## Permissões

- PR-PERM-008 (Cancelar), conforme política da empresa (`pr.cancel.policy`) e restrições por estado.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-007-1 | Cancelamento em Draft → Cancelled + EVT-012 |
| TC-UC-007-2 | Cancelamento com workflow ativo → COMP-002 + aprovadores notificados |
| TC-UC-007-3 | Cancelamento com Pedido de Compra → PR-ERR-060 |
| TC-UC-007-4 | Cancelamento sem motivo → validação bloqueia |

---

# UC-008 — Consultar Solicitações

## Objetivo

Pesquisar requisições.

## Atores

- Solicitante, Gestor, Comprador, Administrador, Auditor (todos com PR-PERM-009)

## Pré-condições

- Usuário autenticado com permissão PR-PERM-009.

## Pós-condições

- Lista paginada de requisições dentro do escopo organizacional do usuário (nenhum dado fora do escopo é retornado ou contado).
- Consulta registrada em log de acesso quando em perfil Auditor.

## Gatilho

O usuário acessa a listagem de solicitações.

## Fluxo Principal

1. Abrir a listagem.
2. Aplicar filtros desejados.
3. Navegar pelos resultados (paginação).
4. Selecionar uma requisição para detalhe.

## Filtros

- Número
- Status
- Empresa
- Unidade
- Solicitante
- Centro de custo
- Projeto
- Prioridade
- Data

## Fluxos Alternativos

A1. Busca por número exato.
- A1.1. Filtro por número retorna no máximo 1 registro por empresa.

A2. Combinação de filtros com ordenação.
- A2.1. Resultado ordenado por data de criação (descendente, padrão), data necessária ou prioridade.

A3. Nenhum resultado.
- A3.1. Lista vazia com mensagem orientando a revisão dos filtros (nunca expõe existência de dados fora do escopo).

## Fluxos de Exceção

E1. Cursor de paginação inválido/expirado.
- E1.1. Recusa com `PR-ERR-400`; cliente reinicia a paginação do início.

E2. Filtro com formato inválido (data malformada, status inexistente).
- E2.1. Recusa com `PR-ERR-400` e detalhamento do campo inválido.

## Regras

- Isolamento por escopo organizacional (PR-001-09 §7) — "Recurso não encontrado" para dados fora do escopo (anti-IDOR).

## Eventos

- Nenhum evento de domínio (operação de leitura). Acesso de Auditor registrado em log de auditoria de consulta.

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-008-A | "Nenhuma solicitação encontrada para os filtros informados." |

## Validações

- Formato de datas (ISO), enums de status/prioridade válidos.
- Tamanho de página: 1–100 (padrão 20).
- Cursor: opaco, assinado, com expiração.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| GET | `/api/v1/purchase-requisitions?cursor=&pageSize=&status=&number=&costCenterId=&projectId=&priority=&dateFrom=&dateTo=` | Listagem com keyset pagination |
| GET | `/api/v1/purchase-requisitions/{id}` | Detalhe completo da requisição (cabeçalho + itens) |

## Permissões

- PR-PERM-009 (Consultar), sempre filtrada pelo escopo organizacional do usuário.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-008-1 | Listagem com keyset pagination: 3 páginas consistentes, sem duplicatas |
| TC-UC-008-2 | Filtro combinado status + centro de custo + período |
| TC-UC-008-3 | Isolamento de escopo: usuário não vê requisições de outra empresa/unidade |
| TC-UC-008-4 | Cursor inválido → PR-ERR-400 |
| TC-UC-008-5 | Detalhe de requisição fora do escopo → 404 (anti-IDOR) |

---

# UC-009 — Visualizar Timeline

## Objetivo

Consultar todo histórico da requisição.

## Atores

- Solicitante, Gestor, Comprador, Administrador, Auditor (PR-PERM-014)

## Pré-condições

- Requisição existente e dentro do escopo do usuário.
- Usuário autenticado com permissão PR-PERM-014.

## Pós-condições

- Histórico cronológico completo exibido, derivado de registros imutáveis (BO-006/BO-007).

Inclui:

- Aprovações
- Comentários
- Alterações
- Eventos
- Auditoria

## Gatilho

O usuário seleciona a aba "Timeline" no detalhe da requisição.

## Fluxo Principal

1. Abrir detalhe da requisição.
2. Selecionar Timeline.
3. Sistema consolida eventos, aprovações, comentários e alterações em ordem cronológica.
4. Usuário navega pelo histórico (paginação por cursor).

## Fluxos Alternativos

A1. Filtro por tipo de entrada.
- A1.1. Usuário filtra a timeline por tipo: eventos, aprovações, comentários ou alterações.

A2. Visualização de auditoria (perfil restrito).
- A2.1. Usuários com PR-PERM-013 acessam visão de auditoria detalhada (antes/depois de cada alteração, decisões de autorização).

## Fluxos de Exceção

E1. Requisição fora do escopo.
- E1.1. "Recurso não encontrado" (anti-IDOR); tentativa registrada em log de segurança.

## Regras

- Registros de Timeline e Auditoria são imutáveis (append-only) — nenhuma operação de edição ou exclusão é permitida.

## Eventos

- Nenhum evento de domínio (leitura).

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-009-A | "Histórico indisponível no momento. Tente novamente." |

## Validações

- Requisição no escopo do usuário.
- Cursor de paginação válido.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| GET | `/api/v1/purchase-requisitions/{id}/timeline?cursor=` | Timeline consolidada (keyset pagination) |
| GET | `/api/v1/purchase-requisitions/{id}/history?cursor=` | Histórico de auditoria (PR-PERM-013) |

## Permissões

- PR-PERM-014 (Visualizar timeline); PR-PERM-013 (Visualizar auditoria) para a visão de histórico.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-009-1 | Timeline completa após ciclo criar→enviar→aprovar: entradas em ordem cronológica |
| TC-UC-009-2 | Filtro por tipo de entrada |
| TC-UC-009-3 | Acesso à auditoria sem PR-PERM-013 → negado + auditoria |
| TC-UC-009-4 | Imutabilidade: tentativa de alteração de registro → rejeitada no banco (trigger) |

---

# UC-010 — Anexar Documento

## Objetivo

Adicionar documentos de apoio.

## Atores

- Solicitante, Gestor (PR-PERM-012)

## Pré-condições

- Requisição existente em estado que permita anexos (Draft, Returned, Approval — conforme política).
- Usuário autenticado com permissão PR-PERM-012 no escopo.

## Pós-condições

- Arquivo persistido no MinIO (DS-002) via FD-001-03; metadados em DS-001.
- Anexo vinculado à requisição e visível na Timeline.
- Nenhuma URL pública gerada em momento algum (acesso via URL assinada de curta duração).

## Gatilho

O usuário seleciona "Anexar documento".

## Fluxo Principal

1. Selecionar arquivo local.
2. Sistema valida tipo, tamanho e conteúdo (ver Validações).
3. Upload para storage via FD-001-03.
4. Confirmação vincula o anexo à requisição e publica EVT-014.

Tipos:

- PDF
- DOCX
- XLSX
- Imagens

## Fluxos Alternativos

A1. Múltiplos anexos.
- A1.1. Usuário anexa vários arquivos em sequência; cada um gera seu próprio registro e evento.

A2. Download de anexo.
- A2.1. Usuário autorizado solicita download; sistema emite URL assinada de curta duração (≤ 5 min) e registra o acesso em auditoria.

## Fluxos de Exceção

E1. Tipo de arquivo não permitido ou assinatura (magic bytes) divergente da extensão.
- E1.1. Recusa com `PR-ERR-070`; tentativa registrada (arquivos executáveis são sempre bloqueados).

E2. Tamanho acima do limite configurado (`attachment.max-size.mb`, padrão 25 MB).
- E2.1. Recusa com `PR-ERR-071`.

E3. Falha no upload (indisponibilidade de storage).
- E3.1. Retry automático (3x); persistindo, mensagem de indisponibilidade temporária; nenhum metadado órfão persistido.

E4. Anexo em estado não permitido.
- E4.1. Recusa com `PR-ERR-040`.

## Regras

- FD-001-03 Document Management (upload, quarentena/antivírus quando habilitado, sem URL pública)
- Limite de anexos por requisição (`attachment.max-count`, padrão 10)

## Eventos

- AttachmentAdded (EVT-014)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-010-A | "Documento anexado com sucesso." |
| MSG-UC-010-B | "Tipo de arquivo não permitido." |
| MSG-UC-010-C | "O arquivo excede o tamanho máximo de {max} MB." |

## Validações

- Extensão e MIME na lista permitida; validação por magic bytes (conteúdo real).
- Tamanho ≤ limite configurado.
- Nome de arquivo sanitizado (sem path traversal).
- Escopo e estado da requisição.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions/{id}/attachments` | Upload (multipart); retorna `201` com `attachmentId` |
| GET | `/api/v1/purchase-requisitions/{id}/attachments/{attachmentId}/download` | Emite URL assinada de curta duração |
| DELETE | `/api/v1/purchase-requisitions/{id}/attachments/{attachmentId}` | Remove anexo (apenas estados editáveis; auditado) |

## Permissões

- PR-PERM-012 (Anexar documentos); download exige PR-PERM-009 no escopo.

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-010-1 | Upload de PDF válido → EVT-014 + metadados persistidos |
| TC-UC-010-2 | Arquivo executável disfarçado (.exe → .pdf) → bloqueio por magic bytes |
| TC-UC-010-3 | Arquivo acima do limite → PR-ERR-071 |
| TC-UC-010-4 | Download gera URL assinada ≤ 5 min + auditoria de acesso |
| TC-UC-010-5 | Nenhuma URL pública em nenhuma resposta da API |

---

# UC-011 — Inserir Comentário

Permitir comunicação durante o processo.

## Atores

- Solicitante, Gestor, Comprador (PR-PERM-011)

## Pré-condições

- Requisição existente, em estado não terminal (Closed e Cancelled não aceitam comentários — configurável).
- Usuário autenticado com permissão PR-PERM-011 no escopo.

## Pós-condições

- Comentário persistido e exibido na Timeline.
- Participantes relevantes notificados (exceto o autor).

## Gatilho

O usuário seleciona "Adicionar comentário" no detalhe da requisição.

## Fluxo Principal

1. Escrever mensagem.
2. Definir visibilidade (pública ou interna).
3. Confirmar.
4. Sistema persiste, publica EVT-015 e notifica participantes.

## Fluxos Alternativos

A1. Comentário interno.
- A1.1. `internal = true`: visível apenas para Gestor, Comprador e Administrador (nunca para o Solicitante); marcado visualmente como interno.

## Fluxos de Exceção

E1. Mensagem vazia ou acima do limite (2.000 caracteres).
- E1.1. Recusa com `PR-ERR-080`.

E2. Estado terminal.
- E2.1. Recusa com `PR-ERR-040` (ou aceite conforme configuração `comments.allow-on-terminal`).

## Regras

- Conteúdo sanitizado contra XSS (renderização segura; armazenamento como texto puro).

## Eventos

- CommentAdded (EVT-015)

## Mensagens

| Código | Mensagem |
| ------ | -------- |
| MSG-UC-011-A | "Comentário publicado." |

## Validações

- Mensagem: 1–2.000 caracteres, sanitizada.
- `internal`: booleano; solicitante não pode ler nem criar comentários internos.

## APIs

| Método | Endpoint | Descrição |
| ------ | -------- | --------- |
| POST | `/api/v1/purchase-requisitions/{id}/comments` | Publica comentário; corpo: `{ message, internal }` |

## Permissões

- PR-PERM-011 (Inserir comentário); leitura de comentários internos restrita por papel (ABAC).

## Testes Relacionados

| Código | Cenário |
| ------ | ------- |
| TC-UC-011-1 | Comentário público → EVT-015 + notificação aos participantes |
| TC-UC-011-2 | Comentário interno invisível ao solicitante |
| TC-UC-011-3 | Payload com script (XSS) → sanitizado na renderização |
| TC-UC-011-4 | Autor não recebe notificação do próprio comentário |

---

# UC-012 — Copiar Requisição (Roadmap)

Criar nova requisição baseada em uma existente.

**Status:** Roadmap — especificação preliminar; detalhamento completo será formalizado em versão futura deste documento antes da implementação. Não implementar sem especificação aprovada.

**Escopo preliminar:** copiar dados gerais e itens (sem anexos, comentários e aprovações); nova numeração; estado Draft; referência à requisição de origem.

---

# UC-013 — Criar a partir de Template (Roadmap)

Criar utilizando modelos pré-configurados.

**Status:** Roadmap — especificação preliminar. Não implementar sem especificação aprovada.

**Escopo preliminar:** templates por empresa com itens, centro de custo e categoria pré-definidos; governança de criação/versionamento de templates pelo Administrador.

---

# UC-014 — Exportar PDF (Roadmap)

Gerar relatório da requisição.

**Status:** Roadmap — especificação preliminar. Não implementar sem especificação aprovada.

**Escopo preliminar:** PDF da requisição com dados gerais, itens, aprovações e timeline; permissão PR-PERM-010; geração assíncrona com download via URL assinada.

---

# Matriz de Casos de Uso

| Caso de Uso | Solicitante | Gestor | Comprador | Administrador |
|--------------|-------------|---------|------------|---------------|
| UC-001 | ✔ | | | |
| UC-002 | ✔ | | | |
| UC-003 | ✔ | | | |
| UC-004 | | ✔ | | |
| UC-005 | | ✔ | | |
| UC-006 | | ✔ | | |
| UC-007 | ✔ | ✔ | | |
| UC-008 | ✔ | ✔ | ✔ | ✔ |
| UC-009 | ✔ | ✔ | ✔ | ✔ |
| UC-010 | ✔ | ✔ | | |
| UC-011 | ✔ | ✔ | ✔ | |

**Nota:** a matriz reflete o uso típico por papel. O Auditor possui acesso de leitura a UC-008/UC-009 (PR-PERM-009/013/014). Casos UC-012 a UC-014 são roadmap e não entram na matriz operacional.

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | — | Arquitetura Trino | Versão inicial aprovada |
| 1.1.0 | 2026-07-30 | Arquitetura Trino | Transformados UC-001 a UC-011 em especificações UML completas (fluxo principal, alternativos, exceção, pré/pós-condições, regras, eventos, mensagens, validações, APIs, permissões, testes relacionados); UC-012 a UC-014 mantidos como roadmap com escopo preliminar; adicionadas convenções da especificação e histórico de versão. Matriz de casos de uso preservada |
