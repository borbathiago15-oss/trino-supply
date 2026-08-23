**Documento:** MMS-004-12 — Business Journey do Inventory Management
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-23
**Dependências:** MMS-004-01 (Business Context), MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-04 (Domain Model), MMS-004-06 (BPMN), MMS-004-07 (Use Cases)
**Referências:** MMS-002-12 (Business Journey do Item Catalog — padrão de formato), PR-001-12, ADR-009, ADR-010

# MMS-004-12 — Business Journey do Inventory Management

## 1. Objetivo

Descrever, em detalhes, a jornada completa de um **saldo de estoque** no Trino Supply — da entrada do material conferido até a saída pelo atendimento, passando por reservas, transferências, ajustes, inventários e alertas — e as jornadas das pessoas que operam, governam e auditam esse saldo.

Esta jornada será a referência oficial para:

- UX
- BPMN
- APIs
- Banco de Dados
- Regras de Negócio
- Testes
- Auditoria
- Integrações (MMS-002, MMS-003, MMS-005, PR-001)

## 2. Princípios da Jornada

A jornada do estoque será guiada pelos seguintes princípios:

1. **Saldo é derivado, nunca editado** — todo saldo se explica pela soma de documentos confirmados (MMS-P-08); não existe "acertar na mão".
2. **Nenhuma movimentação sem documento** — documento, responsável, data/hora e origem referenciável, sempre (MMS-P-07).
3. **Documento confirmado é imutável** — errou, estorna; o original permanece consultável para sempre.
4. **Promessa só com lastro** — validação e reserva usam apenas o **disponível** (total − reservado, MMS-RG-09); nenhum saldo é prometido duas vezes.
5. **A integridade bloqueia na origem** — saldo negativo (MMS-RG-04) e uso de estoque dedicado fora do contrato (MMS-RG-10) são impedidos na validação, não descobertos na conferência.
6. **Alertas antes da falta** — mínimo, ruptura e reserva vencendo notificam quem pode agir, sem nunca travar a operação.
7. **O físico confere com o sistema** — inventário cíclico com divergência tratada por ajuste aprovado sustenta a meta de acuracidade ≥ 98%.

## 3. Fluxo Macro da Jornada

```
Material conferido no Recebimento (MMS-005)
        │
        ▼
Entrada confirmada ──► saldo total e disponível aumentam
        │
        ▼
Solicitação aprovada (MMS-003) ──► validação pelo disponível
        │
        ▼
Reserva criada (validade 72h) ──► disponível bloqueado
        │
        ├──────────────► Vencimento/Liberação → saldo devolvido ao disponível
        │
        ▼
Separação e entrega (saída confirmada) ──► reserva baixada, total reduzido
        │
        ├──────────────► Saldo ≤ mínimo → alerta de reposição → demanda PR-001
        ├──────────────► Saldo = 0 com demanda → alerta de RUPTURA (prioridade alta)
        │
        ▼
Manutenção contínua
(transferências entre locais, devoluções, estornos de erro operacional)
        │
        ▼
Inventário cíclico (curva ABC) ──► contagem → divergência → ajuste aprovado
        │
        ▼
Acuracidade registrada ──► saldo segue confiável por construção
```

## 4. Jornada A — O Almoxarife (jornada principal)

### 4.1 Etapa 1 — Entrada do Material

**Objetivo:** transformar material fisicamente conferido em saldo confiável.

**Origens possíveis:**

- Recebimento conferido no MMS-005 (automática — MSG-IV-C01);
- Devolução de solicitante (MMS-003);
- Entrada avulsa com documento externo de referência;
- Carga inicial documentada (go-live).

**Ponto de dor típico:** no processo manual, o material chega, é guardado, e a entrada "fica para depois" — o sistema diz menos do que existe e a validação de solicitações falha sem motivo real.

**Momento de verdade:** a entrada por recebimento chega **pré-preenchida** (itens, quantidades, referência) — o almoxarife só confere o destino físico e confirma. Entrada em segundos, saldo imediato.

**Resultado esperado:** documento Confirmado, saldo atualizado, alerta de ruptura/mínimo eventualmente **normalizado** pela entrada.

**KPI da etapa:** tempo médio recebimento conferido → saldo efetivado — meta: < 5 minutos (automático).

### 4.2 Etapa 2 — Fila de Reservas e Separação

Aprovada a solicitação no MMS-003, o sistema valida o disponível e cria a reserva automaticamente (UC-IV-003). O almoxarife encontra na **visão do almoxarifado** (US-IV-014) a fila do dia:

- Filtros operacionais: solicitante, período, status, centro de custo, empresa, tipo de produto (EPI/Fardamento), número;
- Reservas ordenadas por vencimento, com destaque para as que vencem em 24h (TMR-IV-002);
- Tamanho da grade visível por linha (EPI/Fardamento — IV-BR-120/121).

**Ponto de dor típico:** reserva esquecida vence, o saldo volta ao disponível e outro atendimento o consome — o solicitante original volta à fila. A validade com alerta pré-vencimento existe para expor esse risco **antes** do vencimento.

**Momento de verdade:** a fila diz exatamente **o que separar, para quem, de onde e até quando** — o almoxarife não decide de memória.

**KPI da etapa:** taxa de reservas vencidas sem atendimento — meta: < 5%.

### 4.3 Etapa 3 — Entrega (Saída por Atendimento)

Na entrega (UC-IV-002), o almoxarife confirma as quantidades efetivamente entregues:

1. Sistema pré-carrega o documento de saída com os dados da reserva;
2. Entrega total baixa a reserva (Atendida); parcial mantém a reserva Ativa com o restante;
3. Troca de tamanho **não é edição**: libera a reserva e cria outra com o tamanho novo (IV-BR-121);
4. Saída confirmada → MMS-003 informa o solicitante; alertas de mínimo/ruptura reavaliados.

**Momento de verdade:** o bloqueio claro quando algo não pode ser feito — "disponível 4, solicitado 6", "reserva vencida, crie uma nova" — com a orientação do que fazer, não só o erro.

**KPI da etapa:** tempo médio criação da reserva → atendimento — meta: dentro da validade (72h).

### 4.4 Etapa 4 — Transferências e Organização Física

Reorganização entre depósitos/endereços (UC-IV-005): saída na origem + entrada no destino, atômicas, saldo global inalterado. Reabastecimento do ponto de separação é transferência com motivo — nunca "ajuste".

**Momento de verdade:** a transferência que falha em qualquer lado **não existe pela metade** — o almoxarife nunca precisa "consertar" uma transferência incompleta.

### 4.5 Etapa 5 — Contagem de Inventário

Quando o Supervisor abre um inventário (UC-IV-007), o almoxarife recebe a lista de contagem do escopo — **cega** por padrão (sem o saldo do sistema, `count.blind`):

- Conta item × local (× tamanho) e registra a quantidade;
- A contagem **nunca altera saldo** — alimenta a apuração de divergência;
- A operação continua durante a contagem (padrão `count.freeze = false`).

**KPI da etapa:** cobertura do ciclo de contagem no prazo — meta: 100% do escopo dentro do deadline.

## 5. Jornada B — O Supervisor de Almoxarifado (governança operacional)

```
Divergência identificada (contagem, avaria, perda, achado, erro)
        │
        ▼
Registra ajuste com motivo estruturado + justificativa (UC-IV-006)
        │
        ▼
Ajuste Pendente ──► notificação ao Gestor (nunca aprova o próprio — SoD)
        │
        ├──────────────► Aprovado → efeito no saldo + auditoria com saldos
        └──────────────► Rejeitado → sem efeito; nova proposta ou justificativa
        
Erro em documento confirmado
        │
        ▼
Estorno com motivo (UC-IV-008) ──► novo documento inverso; original preservado

Estrutura física
        │
        ▼
Gerencia locais (UC-IV-009): almoxarifado → depósito → endereço;
inativação só com saldo zero e sem reservas
```

**Pontos de dor típicos:** (1) ajuste usado como atalho para "sumir" com divergência — bloqueado pela justificativa obrigatória + aprovação de terceiro; (2) endereçamento adotado sem disciplina — mitigado pela granularidade parametrizável (a empresa só endereça quando estiver pronta).

**Momentos de verdade:** (1) o sistema recusa a aprovação do próprio ajuste — a segregação não depende de boa vontade; (2) o inventário **não fecha** com divergência pendente sem tratamento (IV-BR-102).

**KPIs da jornada:** tempo médio de aprovação de ajuste (meta: < 24h — SLA); estornos por período e por motivo (tendência de queda); acuracidade por ciclo (meta ≥ 98%).

## 6. Jornada C — O Gestor de Suprimentos (capital e continuidade)

O gestor olha o estoque como capital e como risco de continuidade:

- **Fila de decisão:** ajustes pendentes de aprovação (com SLA e escalonamento ESC-IV-001);
- **Alertas:** mínimo e ruptura (UC-IV-010) com posição e demanda aberta — ruptura sem normalização em 48h escala para ele (ESC-IV-004);
- **Posição:** físico/reservado/disponível por depósito e categoria; cobertura em dias, giro, valor parado (custo médio de referência);
- **Rota de compra:** o que o estoque não atende vira demanda estruturada no PR-001 — a compra é exceção medida, não hábito.

**Momento de verdade:** a pergunta "posso confiar nesse número?" desaparece — todo saldo é explicável por documentos, e a acuracidade por ciclo comprova.

**KPIs da jornada:** ruptura (tendência a zero); taxa de atendimento pelo estoque; valor parado e cobertura por depósito/categoria.

## 7. Jornada D — O Solicitante (consumidor indireto)

O solicitante **não acessa o estoque** (IV-BR-097 — negação explícita da visão do almoxarifado). Sua jornada acontece no MMS-003, mas é o estoque que a honra:

```
Solicitação aprovada (MMS-003)
        │
        ▼
Validação silenciosa pelo disponível (MMS-RG-09)
        │
        ├──────────────► Atende → reserva criada → "em separação"
        ├──────────────► Atende parcial → reserva parcial + rota de compra da diferença
        └──────────────► Não atende → rota de compra (PR-001), com rastreabilidade
        │
        ▼
Acompanha o status na própria solicitação (reservado → separado → entregue)
        │
        ▼
Recebe o material — tamanho certo (grade), quantidade certa, no prazo
```

**Momentos de verdade:** (1) a promessa do sistema se cumpre — o que foi reservado está lá na entrega; (2) o vencimento da reserva reabre a necessidade **visivelmente**, em vez de morrer em silêncio.

**KPI da jornada:** % de solicitações atendidas dentro da validade da reserva — meta: ≥ 95%.

## 8. Jornada E — O Auditor (observador)

O auditor não executa ações; consome rastros:

- Extrato de movimentações com trilha ponta a ponta: documento → origem (recebimento MMS-005, solicitação MMS-003, ajuste, inventário, estorno);
- **Saldo anterior/posterior por linha** em 100% das confirmações (IV-BR-095) — a assinatura de auditoria do módulo;
- Ajustes com justificativa, motivo estruturado, registrador e aprovador distintos (SoD);
- Estornos sempre vinculados ao documento original, com motivo;
- Eventos publicados no outbox (EVT-IV-001..016) com correlationId;
- Decisões de autorização (POL-IV-AUTH-001..011), incluindo negações e alertas de segurança.

**Momento de verdade:** qualquer pergunta — "por que este saldo é 37?", "quem aprovou este ajuste negativo?" — tem resposta em segundos, com evidência imutável.

## 9. Encerramento da Jornada

A jornada de um saldo termina de duas formas:

1. **Consumo íntegro:** o saldo zera por saídas documentadas (atendimentos, consumos, transferências) — cada unidade rastreável do recebimento à entrega. Cenário ideal.
2. **Item inativado com saldo remanescente:** novas reservas bloqueadas (MMS-RG-08); o saldo segue movimentável até zerar, com tratamento das reservas ativas em até 5 dias úteis (ESC-IV-003).

Em ambos os casos, **nenhum documento é perdido** e **nenhum saldo fica sem explicação**.

## 10. Resumo de KPIs da Jornada

| KPI | Jornada | Meta |
|---|---|---|
| Tempo recebimento conferido → saldo efetivado | Almoxarife (Etapa 1) | < 5 min |
| Taxa de reservas vencidas sem atendimento | Almoxarife (Etapa 2) | < 5% |
| Atendimento dentro da validade da reserva | Solicitante | ≥ 95% |
| Tempo médio de aprovação de ajuste | Supervisor | < 24h (SLA) |
| Acuracidade de estoque por ciclo | Supervisor / Gestor | ≥ 98% |
| Ruptura (itens sem saldo com demanda aberta) | Gestor | Tendência a zero |
| Taxa de atendimento pelo estoque | Gestor | Crescente (estoque primeiro) |
| Cobertura de auditoria com saldo anterior/posterior | Auditor | 100% |

## 11. Histórico de Versão

| Versão | Data | Autor | Descrição |
|---|---|---|---|
| 1.0.0 | 2026-08-23 | Arquiteto Principal | Versão inicial aprovada — jornada macro do saldo (entrada→reserva→entrega→inventário), jornadas do Almoxarife (entrada, fila, entrega, transferência, contagem), do Supervisor (ajuste com SoD, estorno, locais), do Gestor (alertas, capital, rota de compra), do Solicitante (consumidor indireto via MMS-003) e do Auditor; pontos de dor, momentos de verdade e KPIs por etapa; alinhada a IV-BR, UC-IV-001..011 e EVT-IV-001..016. |
