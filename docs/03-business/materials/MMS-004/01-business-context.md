**Documento:** MMS-004-01 — Business Context
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-004 (Visão do Módulo), MMS-001 (Documento Mestre Funcional — seções 6, 8.3, 9, 14, 15.2, 20), ADR-012, MMS-002 (Item Catalog)
**Referências:** MMS-003 (Material Requisition), MMS-005 (Receiving), PR-001 (Compras), FD-001-01, FD-001-02, FD-001-04, FD-001-05, FD-001-06, FD-001-07, FD-001-10, GOV-001

---

# 1. Objetivo

Este documento descreve o **contexto de negócio** do módulo Inventory Management: por que ele existe, qual problema resolve, quem participa, o que o dispara e como o sucesso é medido. É a porta de entrada do pacote funcional do MMS-004 e a referência de propósito para todos os demais documentos do módulo (regras, estados, casos de uso, permissões, eventos, API, UX e testes).

Nada neste documento cria funcionalidade: todo o conteúdo deriva da visão aprovada do módulo (MMS-004 README) e do Documento Mestre Funcional da suíte (MMS-001).

---

# 2. Contexto de Negócio

Depois que o item existe no catálogo (MMS-002), a pergunta seguinte da operação é: **"quanto existe, de quê, onde e para quem?"** Quando a resposta depende de planilhas do almoxarifado, memória do almoxarife ou contagens anuais, a cadeia inteira opera no escuro — solicitações prometem o que não existe, compras duplicam o que já está parado, e o gestor descobre a ruptura quando ela já aconteceu.

O Inventory Management é o módulo Estoque do Trino Supply: **a fonte única de verdade sobre saldos**. Pelo princípio MMS-P-08, **nenhum saldo é editado diretamente** — saldo é sempre o resultado derivado de movimentações registradas por documento (MMS-P-07: nenhuma movimentação sem documento). Essa disciplina é o que permite à plataforma **confiar no próprio estoque**: reservar sem prometer o que não tem, alertar antes da ruptura e dar ao gestor visão real do capital parado.

O MMS-004 é o terceiro módulo do roadmap da suíte (MMS-001, seção 24) e o **centro gravitacional** da operação de materiais: o MMS-003 valida e reserva aqui, o MMS-005 dá entrada aqui, e a rota de compra (PR-001) é alimentada pelo que aqui falta.

---

# 3. Problema de Negócio

| Problema observado | Consequência |
|--------------------|--------------|
| "O sistema diz que tem, mas não tem" (saldo desconfiado) | Separação frustrada, compra emergencial, descredito do sistema |
| Saldo editável "na mão" para corrigir divergência | Divergências escondidas, trilha destruída, auditoria impossível |
| Mesma unidade de saldo prometida a duas solicitações | Atendimento parcial, conflito entre solicitantes, retrabalho |
| Saldo negativo por lançamentos fora de ordem | Números sem sentido físico, reconciliação manual |
| Material de um cliente/contrato usado em outro | Quebra de contrato, glosa, passivo comercial |
| Ajustes sem justificativa nem aprovação | "Sumiço" institucionalizado de divergências |
| Falta descoberta só quando o item é solicitado | Parada de operação, compra emergencial mais cara |

O custo do problema aparece nos KPIs centrais da suíte (MMS-001, seção 20): **acuracidade ≥ 98%**, **ruptura** e **taxa de atendimento pelo estoque** só existem se o saldo for confiável por construção.

---

# 4. Objetivos do Processo

1. Manter **saldos total e disponível** (total − reservado) por item × depósito × (opcionalmente) cliente/contrato, sempre derivados de movimentações.
2. Garantir que toda alteração de saldo ocorra por um dos **seis documentos de movimentação**: entrada, saída, reserva, liberação de reserva, transferência e ajuste.
3. Impedir violações de integridade: saída acima do disponível (MMS-RG-04) e uso de estoque dedicado fora do cliente/contrato dono (MMS-RG-10).
4. Controlar **reservas com validade**, com liberação manual ou automática por vencimento (MMS-RG-03).
5. Operar a **estrutura de locais** almoxarifado → depósito → endereço, com granularidade parametrizável.
6. **Alertar antes da falta**: estoque abaixo do mínimo, ruptura e reserva a vencer (MMS-RG-12).
7. Executar **inventário (contagem física)** cíclico por curva ABC ou geral, com divergência tratada por ajuste aprovado.

---

# 5. Objetivos Estratégicos

| Objetivo estratégico | Contribuição do módulo |
|----------------------|------------------------|
| Estoque primeiro, compra só do que falta | Validação pelo saldo disponível (MMS-RG-09) torna a rota de compra a exceção |
| Acuracidade de estoque ≥ 98% | Saldo derivado de documentos + inventário cíclico + ajuste aprovado |
| Rastreabilidade ponta a ponta | Toda movimentação com documento, responsável, data/hora e saldo anterior/posterior |
| Zero promessa sem lastro | Reserva bloqueia o disponível; nenhuma solicitação aprovada promete saldo já comprometido |
| Capital visível | Valor parado, cobertura em dias e giro consultáveis por depósito e categoria |
| Segurança e auditoria por padrão | Documentos imutáveis após confirmação; correção apenas por estorno |

---

# 6. Quando o Estoque é Movimentado

| Situação | Documento | Origem típica |
|----------|-----------|---------------|
| Material conferido no recebimento | Entrada | MMS-005 (Receiving) |
| Atendimento de solicitação aprovada | Saída | MMS-003 (Material Requisition) |
| Solicitação aprovada aguardando separação | Reserva | MMS-003 |
| Solicitação cancelada / validade expirada | Liberação de reserva | MMS-003 / job automático (MMS-RG-03) |
| Reorganização entre depósitos ou endereços | Transferência | Almoxarife / Supervisor |
| Devolução de solicitante | Entrada | MMS-003 |
| Divergência de contagem física | Ajuste (com justificativa e aprovação quando parametrizado, MMS-RG-05) | Inventário |
| Correção de documento confirmado com erro | Estorno (novo documento vinculado) | Supervisor |

---

# 7. Fora do Escopo

Conforme a fronteira formal da visão do módulo (MMS-004 README):

- **Solicitar material** — domínio do MMS-003; o estoque é consultado e reservado por ele;
- **Receber fisicamente** — conferência é do MMS-005; o estoque registra a entrada decorrente;
- **Cadastrar itens** — identidade e parâmetros vêm do MMS-002 (mín/máx/ponto de pedido são somente leitura aqui);
- **Comprar** — alertas de reposição viram demanda no domínio PR-001;
- **Valorização fiscal/contábil** — o MVP registra custo médio apenas como referência gerencial (MMS-001, seção 8.3);
- **Lote, validade e número de série** — versão 2.0 (MMS-004 README, roadmap);
- **Quarentena de qualidade** — versão 2.0, com MMS-005 v2.0.

---

# 8. Contexto Organizacional

- O estoque é **multiempresa**: isolamento total por empresa; além disso, suporta **segregação por cliente/contrato** (MMS-RG-10) para operações dedicadas — estoque dedicado não atende outro cliente/contrato.
- A **estrutura de locais** segue almoxarifado → depósito → endereço; a empresa escolhe a granularidade (parâmetro `materials.inventory.*`) — pode operar apenas até depósito.
- A **operação** é do Almoxarife (movimentações e contagens); o Supervisor de Almoxarifado aprova ajustes e gerencia endereçamento e fila; o Gerente de Suprimentos acompanha KPIs, alertas e posição.
- **Quem ajusta não aprova o próprio ajuste** — segregação de funções garantida pelo IAM (FD-001-01) e, quando parametrizada, pelo Workflow Engine (FD-001-04).
- O **consumo externo** é transversal: MMS-003 (validação/reserva/separação/baixa), MMS-005 (entradas conferidas) e PR-001 (demanda de reposição) interagem com o estoque sempre por documento.

---

# 9. Stakeholders

| Stakeholder | Interesse | Influência |
|-------------|-----------|------------|
| Almoxarife | Fila clara, endereçamento correto, operação sem retrabalho | Operador principal |
| Supervisor de Almoxarifado | Ajustes sob controle, acuracidade, fila saudável | Gestor operacional |
| Gerente de Suprimentos | KPIs, alertas, posição, capital parado | Gestor tático |
| Solicitantes | Atendimento do que foi aprovado | Beneficiário indireto (via MMS-003) |
| Compras | Demanda de reposição confiável | Consumidor via PR-001 |
| Gestão (Management Workspace) | Indicadores de estoque e atendimento | Beneficiária dos KPIs |
| Auditoria | Extrato e trilha com saldo anterior/posterior | Verificadora |
| Administração da plataforma | Parâmetros do módulo | Configuradora |

---

# 10. Personas

Conforme as personas da suíte (MMS-001, seção 11), recortadas para este módulo:

## Persona 1 — Almoxarife

Executa o dia a dia: recebe a fila de reservas aprovadas, separa, entrega, dá entrada em recebimentos e devoluções, transfere entre endereços e registra contagens. Precisa de confirmação imediata de saldo e de bloqueio claro quando algo não pode ser feito.

## Persona 2 — Supervisor de Almoxarifado

Gerencia a operação do almoxarifado: aprova ajustes com justificativa, revisa divergências de inventário, define endereçamento e acompanha reservas vencendo. Não aprova o próprio ajuste.

## Persona 3 — Gerente de Suprimentos

Olha o estoque como capital: acuracidade, ruptura, cobertura, giro, valor parado. Acompanha alertas de mínimo e aciona a rota de compra quando o estoque não atende.

## Persona 4 — Sistema (MMS-003 / MMS-005)

Consome o estoque por integração: valida disponibilidade, cria e baixa reservas, registra entradas de recebimentos conferidos — sempre por documento de origem.

## Persona 5 — Auditor

Percorre extrato e timeline verificando que todo saldo se explica por documentos, que ajustes têm justificativa e aprovação, e que estornos referenciam o documento original.

---

# 11. Premissas

1. O Foundation (FD-001) está operacional: identidade, organização, workflow, notificações, auditoria, timeline e configuração disponíveis como serviço.
2. O Item Catalog (MMS-002) está operacional: itens Ativos com classificação e parâmetros de reposição cadastrados.
3. A operação aceita que **nenhum saldo é editado diretamente** — inclusive por administradores e jobs (MMS-P-08).
4. A operação aceita que **documentos confirmados são imutáveis** — correções ocorrem por estorno vinculado (MMS-001, seção 15.2).
5. Unidades de medida e quantidades seguem o cadastro do item (MMS-002); conversões de unidade não existem no MVP.
6. Não há migração de saldo legado no MVP; a posição inicial é construída por documentos de entrada (carga inicial documentada).

---

# 12. Restrições

1. Nenhum saldo editável diretamente — saldo é projeção derivada de movimentações (MMS-P-08).
2. Nenhuma movimentação sem documento de origem referenciável (MMS-P-07).
3. Documentos de movimentação imutáveis após confirmação; correção apenas por **estorno** (novo documento vinculado), nunca por edição.
4. Validação de estoque considera **somente o disponível** (total − reservado) — MMS-RG-09.
5. Saída que geraria saldo negativo é bloqueada — MMS-RG-04.
6. Estoque dedicado a cliente/contrato não atende outro cliente/contrato — MMS-RG-10.
7. Parâmetros de reposição (mín/máx/ponto de pedido/lead time) são somente leitura neste módulo — pertencem ao MMS-002.
8. Alertas nunca bloqueiam a movimentação principal (notificação é assíncrona).
9. Toda transferência é atômica: saída + entrada vinculadas; falha em qualquer lado = estorno.
10. Nada neste módulo pode contradizer o MMS-001 nem os princípios MMS-P-01..08.

---

# 13. Gatilhos de Negócio

## Operacionais

- Recebimento conferido (MMS-005) → entrada no depósito/endereço de destino;
- Solicitação aprovada (MMS-003) → validação de disponibilidade e criação de reserva;
- Separação concluída e entrega confirmada (MMS-003) → baixa da reserva e saída;
- Devolução de solicitante → entrada com documento de origem;
- Necessidade de reorganização física → transferência entre depósitos/endereços.

## Administrativos

- Reserva com validade expirada (MMS-RG-03) → liberação automática pelo job de vencimento;
- Divergência identificada (conferência, achado, perda) → ajuste com justificativa e aprovação;
- Erro em documento confirmado → estorno vinculado;
- Revisão de endereçamento → cadastro/ajuste de depósitos e endereços pelo Supervisor.

## Estratégicos

- Meta de acuracidade ≥ 98% → programação de inventários cíclicos por curva ABC;
- Meta de atendimento pelo estoque → revisão de parâmetros de reposição (com MMS-002);
- Capital parado elevado → análise de cobertura, giro e itens sem movimento.

## Emergenciais

- Ruptura de item crítico com demanda aberta → alerta de ruptura e demanda urgente para PR-001;
- Item inativado (MMS-002) com saldo remanescente → bloqueio de novas reservas; saldo segue movimentável até zerar (MMS-RG-08);
- Suspeita de extravio → inventário parcial do item/depósito com ajuste aprovado.

---

# 14. Resultado Esperado

- Saldo confiável por construção: todo saldo explicável pela soma de documentos;
- Reservas honradas: nenhum saldo prometido duas vezes;
- Integridade preservada: zero saldo negativo, zero uso indevido de estoque dedicado;
- Divergências visíveis e tratadas: inventário cíclico com ajuste justificado e aprovado;
- Alertas antes da falta: mínimo, ruptura e reserva vencendo notificados aos responsáveis;
- Trilha completa: auditoria e timeline de 100% das movimentações, com saldo anterior/posterior.

---

# 15. Participantes do Processo

| Papel | Participação |
|-------|--------------|
| Almoxarife | Executa movimentações operacionais e contagens |
| Supervisor de Almoxarifado | Aprova ajustes; gerencia endereçamento e fila |
| Gerente de Suprimentos | KPIs, alertas, posição, parâmetros do módulo |
| Sistema (MMS-003/MMS-005) | Validação, reserva e entrada por documento de origem |
| Administrador | Configura parâmetros do módulo (`materials.inventory.*`) |
| Auditor | Consulta e trilha (somente leitura) |

---

# 16. Entradas do Processo

| Entrada | Origem | Observação |
|---------|--------|------------|
| Documentos de origem (solicitação, recebimento, devolução, transferência, ajuste, contagem) | MMS-003, MMS-005, operação | Referência obrigatória em toda movimentação |
| Parâmetros do item (mínimo, máximo, ponto de pedido, lead time, classificação) | MMS-002 | Somente leitura neste módulo |
| Estrutura de locais (almoxarifados, depósitos, endereços) | Supervisor de Almoxarifado | Granularidade parametrizável |
| Parâmetros do módulo (`materials.inventory.*`) | Administrador | Validade de reserva, aprovação de ajuste, granularidade, tolerâncias de contagem |
| Justificativa de ajuste | Responsável pelo ajuste | Obrigatória; aprovação quando parametrizada (MMS-RG-05) |
| Contagem física | Almoxarife | Quantidade contada por item × local |

---

# 17. Saídas do Processo

| Saída | Destino |
|-------|---------|
| Saldos total/disponível por item × depósito (× cliente/contrato) | MMS-003, gerência, dashboards |
| Extrato de movimentações e posição de estoque | Almoxarifado, gerência, auditoria |
| Resultado de validação de estoque (atende / não atende, por item) | MMS-003 |
| Eventos de negócio de toda movimentação | Barramento (consumidores da suíte) |
| Alertas de mínimo, ruptura e reserva vencendo | FD-001-05 Notification Center |
| Demanda de reposição | PR-001 (quando parametrizado) |
| Resultado de inventário (contagem, divergência, ajuste) | Supervisor, gerência, auditoria |
| Registros de auditoria e timeline | FD-001-06 / FD-001-07 |

---

# 18. Indicadores de Negócio

- Acuracidade de estoque por ciclo de inventário (meta ≥ 98%);
- Ruptura: itens com saldo zero e demanda aberta;
- Cobertura em dias e giro por depósito/categoria;
- Valor parado (custo médio de referência gerencial);
- Reservas vencidas sem atendimento;
- Divergências de inventário (quantidade e valor) por ciclo;
- Taxa de atendimento pelo estoque (com MMS-003).

---

# 19. KPIs

| KPI | Cálculo | Meta/observação |
|-----|---------|-----------------|
| Acuracidade de estoque | itens com contagem = saldo ÷ itens contados | ≥ 98% (KPI central, MMS-001 §20) |
| Ruptura | itens com saldo disponível zero e demanda aberta | Tendência a zero; alerta ativo |
| Reservas vencidas sem atendimento | reservas liberadas por vencimento ÷ reservas criadas | Mede disciplina de separação |
| Divergência de inventário | Σ |contagem − saldo| por ciclo (qtd e valor) | Redução ciclo a ciclo |
| Cobertura em dias | saldo ÷ consumo médio diário | Por depósito/categoria |
| Giro de estoque | consumo no período ÷ saldo médio | Por depósito/categoria |
| Valor parado | saldo × custo médio de referência | Visão de capital (gerencial) |
| Taxa de atendimento pelo estoque | solicitações atendidas sem compra ÷ total (com MMS-003) | Estoque primeiro |

---

# 20. Riscos de Negócio

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Divergência físico × sistema apesar da disciplina | Média | Alto | Inventário cíclico ABC + ajuste aprovado + KPI de acuracidade |
| Reservas abandonadas travando disponível | Alta | Médio | Validade parametrizável + liberação automática (MMS-RG-03) + alerta de reserva vencendo |
| Lançamentos fora de ordem gerando bloqueio de operação | Média | Médio | Confirmação sequencial por saldo; erro claro (saldo insuficiente) orientando a correção |
| Ajuste usado para maquiar perda | Baixa | Alto | Justificativa obrigatória + aprovação + quem ajusta não aprova + auditoria |
| Estoque dedicado consumido por outro contrato | Baixa | Alto | Bloqueio MMS-RG-10 na validação, não só na conferência |
| Alertas ignorados até a ruptura | Média | Alto | Escalonamento por criticidade; KPI de ruptura no workspace de gestão |
| Endereçamento adotado sem disciplina de uso | Média | Médio | Granularidade parametrizável (empresa pode operar só até depósito) |
| Carga inicial de saldo malfeita | Média | Alto | Posição inicial apenas por documento de entrada auditado; inventário de conferência no go-live |

---

# 21. Glossário

| Termo | Definição |
|-------|-----------|
| **Saldo total** | Quantidade física registrada do item no local (derivada de movimentações) |
| **Saldo disponível** | Saldo total − saldo reservado; única visão válida para validação (MMS-RG-09) |
| **Saldo reservado** | Quantidade bloqueada por reservas ativas em favor de solicitações |
| **Documento de movimentação** | Registro formal de entrada, saída, reserva, liberação, transferência ou ajuste (MMS-P-07) |
| **Entrada** | Documento que aumenta saldo (recebimento, devolução, ajuste positivo) |
| **Saída** | Documento que diminui saldo (atendimento, consumo, ajuste negativo) |
| **Reserva** | Bloqueio de saldo disponível para uma solicitação, com validade parametrizável |
| **Liberação de reserva** | Desbloqueio manual ou automático por vencimento (MMS-RG-03) |
| **Transferência** | Par atômico saída + entrada entre locais; falha em qualquer lado = estorno |
| **Ajuste** | Correção de divergência com justificativa obrigatória e aprovação quando parametrizada (MMS-RG-05) |
| **Estorno** | Documento de correção vinculado ao documento original confirmado; única forma de correção |
| **Endereçamento** | Estrutura almoxarifado → depósito → endereço, com granularidade parametrizável |
| **Estoque dedicado** | Saldo segregado por cliente/contrato; não atende outros (MMS-RG-10) |
| **Ruptura** | Saldo disponível zero com demanda aberta (MMS-RG-12) |
| **Inventário** | Contagem física cíclica (curva ABC) ou geral, com divergência tratada por ajuste aprovado |
| **Curva ABC** | Classificação de itens por importância de consumo/valor, orientando a frequência de contagem |
| **Custo médio de referência** | Valor gerencial de referência do saldo; não é valorização fiscal/contábil |

---

# 22. Princípios do Processo

1. **Saldo é derivado, nunca editado** (MMS-P-08) — inclusive administradores e jobs obedecem.
2. **Nenhuma movimentação sem documento** (MMS-P-07) — documento, responsável e data/hora sempre.
3. **Documento confirmado é imutável** — correção apenas por estorno vinculado.
4. **Validação usa somente o disponível** (MMS-RG-09) — reserva honrada é reserva que bloqueia.
5. **Integridade bloqueia na origem** — saldo negativo (MMS-RG-04) e segregação (MMS-RG-10) impedidos na validação.
6. **Toda alteração é auditada** (MMS-P-02) com saldo anterior/posterior, e aparece na timeline (MMS-P-03).
7. **Parametrização acima de customização** (MMS-P-06) — validade de reserva, aprovação de ajuste, granularidade e tolerâncias são configuração (`materials.inventory.*`).
8. **Alertas informam, não bloqueiam** — a movimentação principal nunca espera notificação.
9. **Documentação como fonte da verdade** — conflito entre código e documentação resolve-se pela documentação.

---

# 23. Dependências

| Dependência | Tipo | Uso |
|-------------|------|-----|
| MMS-001 (Documento Mestre) | Obrigatória | Regras, princípios, estados e KPIs herdados |
| MMS-002 Item Catalog | Obrigatória | Identidade e parâmetros do item (somente leitura) |
| MMS-003 Material Requisition | Obrigatória (consumidor) | Validação, reserva, separação, baixa |
| MMS-005 Receiving | Obrigatória (produtor) | Entradas conferidas |
| PR-001 (Compras) | Obrigatória (consumidor) | Demanda de reposição/compra |
| FD-001-01 Identity & Access | Obrigatória | Permissões e segregação de funções |
| FD-001-02 Organization | Obrigatória | Empresa/unidade dos almoxarifados e depósitos |
| FD-001-04 Workflow | Condicional | Aprovação de ajustes quando parametrizada |
| FD-001-05 Notification Center | Obrigatória | Despacho exclusivo de todos os alertas do módulo |
| FD-001-06 Audit | Obrigatória | Trilha com saldo anterior/posterior |
| FD-001-07 Timeline | Obrigatória | Marcos por documento e por item |
| FD-001-10 Configuration | Obrigatória | Parâmetros `materials.inventory.*` |
| Analytics (futuro) | Opcional (consumidor) | Saldos, movimentações e KPIs |

---

# 24. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação do Business Context do Inventory Management: contexto, problema, objetivos, stakeholders, personas, premissas, restrições, gatilhos, entradas/saídas, indicadores, KPIs, riscos, glossário, princípios e dependências — derivado da visão aprovada do módulo (MMS-004 README) e do Documento Mestre (MMS-001), no padrão do pacote MMS-002-01 |
