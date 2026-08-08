**Documento:** MMS-004 — Inventory Management (Visão do Módulo)
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.1.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-001 (Documento Mestre Funcional — seções 6, 8.3, 9, 14, 15.2, 20), MMS-002 (Item Catalog v1.2.0), ADR-012, ADR-013 (Conversão de UoM), ADR-014 (Motor de Regras de Reposição), FD-001-01, FD-001-02, FD-001-04, FD-001-05, FD-001-06, FD-001-07, FD-001-10
**Referências:** MMS-003 (Material Requisition — consumidor de reservas), MMS-005 (Receiving — origem de entradas), PR-001 (rota de compra), GOV-001

---

# Objetivo

O **Inventory Management** é o módulo Estoque do Trino Supply: guarda a verdade sobre **quanto existe, de quê, onde e para quem**. Todo saldo do sistema vive aqui — e, pelo princípio MMS-P-08, **nenhum saldo é editado diretamente**: saldo é sempre o resultado de movimentações registradas por documento.

O módulo existe para que a plataforma possa confiar no próprio estoque: reservar sem prometer o que não tem, alertar antes da ruptura e dar ao gestor visão real do capital parado.

---

# Escopo

## O que faz

1. **Saldos** por item × depósito × (opcionalmente) cliente/contrato, em duas visões: saldo total e saldo disponível (total − reservado). A validação de estoque considera **somente o disponível** (MMS-RG-09).
2. **Documentos de movimentação** (MMS-P-07 — nenhuma movimentação sem documento):
   - **Entrada** — origem: Receiving (MMS-005), devolução de solicitante ou ajuste;
   - **Saída** — destino: atendimento de solicitação (MMS-003), consumo ou ajuste;
   - **Reserva** — bloqueio de saldo disponível para uma solicitação, com validade parametrizável;
   - **Liberação de reserva** — manual ou automática por vencimento (MMS-RG-03);
   - **Transferência** — entre depósitos/endereços (saída + entrada vinculadas);
   - **Ajuste** — correção de divergência, com justificativa obrigatória e aprovação quando parametrizado (MMS-RG-05).
3. **Estrutura de locais**: almoxarifado → depósito → endereço, com granularidade de endereçamento parametrizável (empresa pode operar só até depósito).
4. **Bloqueios de integridade**: saída maior que o disponível é bloqueada (MMS-RG-04); estoque dedicado a cliente/contrato não atende outro cliente/contrato (MMS-RG-10).
5. **Alertas**: estoque abaixo do mínimo, ruptura (saldo zero com demanda aberta) e reserva a vencer (MMS-RG-12, MMS-RG-03) — despachados pelo Notification Center.
6. **Inventário (contagem física)**: cíclico por curva ABC ou geral, com registro de contagem, divergência e tratamento por ajuste aprovado.
7. **Consulta de posição**: saldo por item/depósito/endereço, extrato de movimentações por item e por documento.

## O que NÃO faz

- **Não solicita material** — é o MMS-003; o estoque é consultado e reservado por ele.
- **Não recebe fisicamente** — conferência é do MMS-005; o estoque registra a entrada decorrente.
- **Não cadastra itens** — identidade e parâmetros vêm do MMS-002 (o estoque consome mín/máx/ponto de pedido, unidade base, unidades de conversão e a regra de reposição; não os edita).
- **Não compra** — alertas de reposição viram demanda no domínio PR-001.
- **Não faz valorização fiscal/contábil** — o MVP registra custo médio apenas como referência gerencial (MMS-001, seção 8.3).

## Integrações esperadas

| Integração | Direção | Descrição |
|------------|---------|-----------|
| MMS-002 Item Catalog | Consome | Identidade do item, classificação e parâmetros de reposição |
| MMS-003 Material Requisition | Produz para | Validação de estoque, reserva, separação, baixa na entrega |
| MMS-005 Receiving | Consome | Entrada de material conferido (já reservado, se compra dedicada) |
| PR-001 (Compras) | Produz para | Demanda de reposição/compra quando o estoque não atende |
| Analytics (futuro) | Produz para | Saldos, movimentações e KPIs |

---

# Objetivos do Negócio

1. **Acuracidade ≥ 98%** — saldo do sistema confere com o físico (KPI central do MMS-001).
2. **Atendimento pelo estoque primeiro** — a rota de compra é exceção; a taxa de atendimento pelo estoque é medida e incentivada.
3. **Zero movimentação sem documento** — toda alteração de saldo rastreável a um documento, um responsável e uma data/hora.
4. **Prevenção à ruptura** — alertas de mínimo e ruptura antes da falta impactar a operação.
5. **Capital visível** — valor parado, cobertura em dias e giro consultáveis por depósito e categoria.

## Quem utiliza

- Almoxarife — opera reservas, separações, entregas, entradas e contagens;
- Supervisor de Almoxarifado — aprova ajustes, gerencia fila e endereçamento;
- Gerente de Suprimentos — acompanha KPIs, alertas e posição;
- Solicitante — consulta indireta (status do atendimento via MMS-003);
- Auditor — extrato e trilha de movimentações.

---

# Objetivos do MVP

1. Saldos total/disponível por item × depósito com endereçamento opcional.
2. Os seis documentos de movimentação (entrada, saída, reserva, liberação, transferência, ajuste).
3. Bloqueios MMS-RG-04 (saldo negativo) e MMS-RG-10 (segregação cliente/contrato).
4. Alertas de mínimo, ruptura e reserva vencendo.
5. Inventário com contagem, divergência e ajuste aprovado.
6. Extrato de movimentações e posição de estoque.
7. Auditoria e timeline em 100% das movimentações, com saldo anterior/posterior (MMS-001, seção 18).

---

# Problemas que Resolve

| Problema | Solução do módulo |
|----------|-------------------|
| "O sistema diz que tem, mas não tem" | Saldo só por movimentação documentada + inventário cíclico |
| Venda/promessa do mesmo saldo duas vezes | Reserva com validade; validação usa apenas o disponível |
| Saldo negativo por lançamentos desordenados | Bloqueio MMS-RG-04 + confirmação sequencial por documento |
| Material de um cliente usado em outro contrato | Segregação por cliente/contrato (MMS-RG-10) |
| Ajustes "sumindo" com divergências | Ajuste com justificativa e aprovação, auditado |
| Descobrir a falta quando ela já aconteceu | Alertas de mínimo e ruptura |

---

# Atores

| Ator | Papel no módulo |
|------|-----------------|
| **Almoxarife** | Executa todas as movimentações operacionais e contagens |
| **Supervisor de Almoxarifado** | Aprova ajustes; gerencia endereçamento e fila de atendimento |
| **Gerente de Suprimentos** | KPIs, alertas, posição, parâmetros do módulo |
| **Sistema (MMS-003/MMS-005)** | Validação, reserva e entrada por documento de origem |
| **Auditor** | Consulta e trilha |

---

# Fluxo Macro

```
Documento de origem (solicitação, recebimento, devolução, contagem, ajuste)
        │
        ▼
Movimentação registrada (sempre vinculada ao documento)
        │
        ▼
Validações (saldo disponível, segregação, item ativo, local válido)
        │
        ▼
Confirmação ──► saldo atualizado (total e/ou reservado)
        │
        ├─► timeline + auditoria (saldo anterior/posterior)
        ├─► eventos de negócio
        └─► alertas (mínimo / ruptura / reserva vencendo), quando aplicável
```

---

# Entradas

- Documentos de origem: solicitação de material (MMS-003), recebimento conferido (MMS-005), devolução, transferência, ajuste, contagem de inventário;
- Parâmetros do item (MMS-002): mínimo, máximo, ponto de pedido, lead time, classificação;
- Estrutura de locais: almoxarifados, depósitos, endereços;
- Parâmetros do módulo (`materials.inventory.*`): validade de reserva, exigência de aprovação de ajuste, granularidade de endereçamento, tolerâncias de contagem.

# Saídas

- Saldos total/disponível por item × depósito (× cliente/contrato);
- Extrato de movimentações e posição de estoque;
- Eventos de negócio de toda movimentação;
- Alertas de mínimo, ruptura e reserva vencendo;
- Resultado de validação de estoque para o MMS-003 (atende / não atende, por item);
- Demanda de reposição para o domínio de Compras (quando parametrizado).

---

# Dependências do Foundation

| Serviço | Uso |
|---------|-----|
| FD-001-01 IAM | Permissões e segregação de funções (quem ajusta não aprova o próprio ajuste) |
| FD-001-02 Organization | Empresa/unidade dos almoxarifados e depósitos |
| FD-001-04 Workflow Engine | Aprovação de ajustes quando parametrizada |
| FD-001-05 Notification Center | Todos os alertas do módulo (despacho exclusivo) |
| FD-001-06 Audit Service | Trilha com saldo anterior/posterior |
| FD-001-07 Timeline Service | Marcos por documento e por item |
| FD-001-10 Configuration | Parâmetros `materials.inventory.*` |

---

# Eventos Publicados (funcionais)

1. Entrada registrada; 2. Saída registrada; 3. Reserva criada; 4. Reserva liberada; 5. Reserva vencida; 6. Transferência registrada; 7. Ajuste registrado / aprovado; 8. Alerta de estoque mínimo; 9. Alerta de ruptura; 10. Inventário iniciado / contagem registrada / divergência aprovada. (Especificação técnica seguirá ADR-010.)

# Eventos Consumidos (funcionais)

- Recebimento conferido (MMS-005) → gera entrada; compra dedicada → entrada já reservada;
- Solicitação aprovada (MMS-003) → validação de estoque e reserva;
- Item inativado (MMS-002) → bloqueia novas reservas do item; saldo segue movimentável;
- Pedido confirmado / previsão de entrega (Procurement) → visibilidade de entrada futura.

---

# Requisitos Não Funcionais

| Categoria | Requisito |
|-----------|-----------|
| Integridade | Saldo derivado de movimentações; nenhuma escrita direta de saldo (MMS-P-08) |
| Concorrência | Movimentações sobre o mesmo saldo confirmadas sequencialmente; optimistic concurrency nos documentos |
| Multiempresa | Isolamento por empresa; segregação por cliente/contrato (MMS-RG-10) |
| Performance | Validação de estoque e consulta de saldo < 2s; leitura com cache |
| Auditoria | 100% das movimentações com saldo anterior/posterior e correlationId |
| Disponibilidade | Alertas nunca bloqueiam a movimentação principal |
| Consistência | Toda transferência é atômica (saída + entrada vinculadas; falha = estorno) |

---

# Restrições Arquiteturais

1. Nenhum saldo editável diretamente — inclusive por administradores e jobs.
2. Nenhuma movimentação sem documento de origem referenciável (MMS-P-07).
3. Documentos de movimentação são imutáveis após confirmação; correções ocorrem por **estorno** (novo documento vinculado), nunca por edição (estados do MMS-001, seção 15.2).
4. Parâmetros de reposição são somente leitura neste módulo (pertencem ao MMS-002).
5. O módulo segue MMS-001 e os princípios MMS-P-01..08; nada aqui pode contradizê-los.

---

# Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Direção | Descrição |
|--------|------|---------|-----------|
| Foundation | Obrigatória | Consome | Serviços da tabela acima |
| MMS-002 Item Catalog | Obrigatória | Consome | Identidade e parâmetros do item |
| MMS-003 Material Requisition | Obrigatória | Produz para | Validação, reserva, separação, baixa |
| MMS-005 Receiving | Obrigatória | Consome | Entradas conferidas |
| PR-001 (Compras) | Obrigatória | Produz para | Demanda de reposição/compra |
| Analytics | Opcional | Produz para | KPIs e posição |

---

# KPIs

- Acuracidade de estoque (meta ≥ 98%);
- Ruptura (itens com saldo zero e demanda aberta);
- Cobertura em dias e giro por depósito/categoria;
- Valor parado (custo médio de referência);
- Reservas vencidas sem atendimento;
- Divergências de inventário (quantidade e valor) por ciclo;
- Taxa de atendimento pelo estoque (com MMS-003).

---

# Critérios de Qualidade

- Todos os seis documentos de movimentação com regras, estados e eventos documentados;
- Bloqueios MMS-RG-04/09/10 com casos de teste previstos;
- Fluxo de inventário completo (abertura → contagem → divergência → ajuste aprovado);
- Rastreabilidade com MMS-001 verificada.

---

# Critérios de Conclusão do Módulo (DoD)

1. Este documento aprovado no registry.
2. Documentos funcionais do módulo (regras, state machine, permissões, eventos, database) aprovados.
3. Matriz de rastreabilidade regra × movimentação × evento preenchida.
4. Prova de integridade: nenhum caminho (API, job ou banco) altera saldo sem documento.

---

# Roadmap

## Versão 1 (MVP)

- Saldos, seis documentos de movimentação, endereçamento opcional, alertas, inventário básico, extrato e posição.

## Versão 1.1

- Transferência com recebimento em trânsito; inventário cíclico automático por curva ABC; compra dedicada com reserva automática na entrada; relatórios completos (posição, movimentações, curva ABC, acuracidade); **fila de sugestões de reposição** (avaliação da regra do item — ADR-014, IV-BR-130/131), com execução automática opt-in reservada à v2.0.

## Versão 2.0

- Controle por lote/validade e número de série; quarentena (com MMS-005 v2.0); sugestão de rebalanceamento entre depósitos; **reposição automática** por ponto de pedido (execução da regra ADR-014 sem intervenção — `materials.replenishment.auto-execute=true`, gera PR-001/transferência).

---

# Funcionalidades Futuras

- Operação por código de barras/QR/RFID; mobile offline para almoxarifado;
- Previsão de demanda e sugestão de parâmetros (IA);
- Valorização de estoque com integração fiscal/contábil.

---

# Versionamento do Módulo

| Versão | Situação |
|--------|----------|
| 1.0.0 | Visão do módulo aprovada (este documento) |
| 1.1.0 | Requisitos da versão 1.1 do roadmap |
| 2.0.0 | Requisitos da versão 2.0 do roadmap |

---

# Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação da visão do módulo Inventory Management: saldos derivados de movimentação, seis documentos de movimentação, reservas com validade, endereçamento, bloqueios de integridade, alertas, inventário, eventos, NFRs, KPIs, DoD e roadmap — conforme MMS-001 (seção 8.3) e ADR-012 |
| 1.1.0 | 2026-08-08 | Propagação de ADR-013 e ADR-014: saldo mantido na unidade base com conversão de UoM na captura de movimentação (IV-BR-009 revisada); consumo da unidade base, das unidades de conversão e da regra de reposição do MMS-002 v1.2.0; nova fila de sugestões de reposição na v1.1 (IV-BR-130/131) e reposição automática opt-in reafirmada na v2.0. Regras em MMS-004-02 v1.1.0 |
