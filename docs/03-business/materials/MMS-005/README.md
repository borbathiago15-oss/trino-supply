**Documento:** MMS-005 — Receiving (Visão do Módulo)
**Módulo:** MMS-005 — Receiving (Recebimento)
**Versão:** 1.1.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-001 (Documento Mestre Funcional — seções 6, 8.4, 9, 14, 15.3, 16, 20, 22, 23, 24, 25), ADR-012, ADR-013 (Conversão de UoM), FD-001-01, FD-001-02, FD-001-04, FD-001-05, FD-001-06, FD-001-07, FD-001-10
**Referências:** MMS-002 (Item Catalog), MMS-003 (Material Requisition), MMS-004 (Inventory Management), PR-001 (Purchase Requisition — origem de compras), GOV-001

---

# Objetivo

O **Receiving** é a porta de entrada física de material na Materials Management Suite: o módulo que recebe, confere e dá entrada no estoque de todo material que chega ao almoxarifado — comprado, transferido ou devolvido — sempre contra um documento de origem.

O módulo existe para garantir que **nada entra no estoque sem conferência e sem documento** (MMS-RG-06) e que **nenhuma diferença "some"**: falta, excesso e avaria são registrados com destino documentado (MMS-RG-07). É o último elo do fluxo corporativo: sem ele, a rota de compra não se fecha e a solicitação de material não conclui.

---

# Escopo

## O que faz

- **Recebimento contra documento de origem:** Pedido de Compra (futuro, domínio Procurement), transferência entre depósitos ou devolução de solicitante — sempre com referência obrigatória (MMS-RG-06).
- **Conferência quantitativa:** quantidade recebida × quantidade esperada, item a item. A quantidade pode ser conferida na **unidade de compra** do item (ex.: caixas); ao gerar a entrada, o sistema **converte para a unidade base** pelo fator do item (ADR-013, MMS-002 IC-BR-091) e o documento de entrada registra o fator aplicado (MMS-004 IV-BR-009).
- **Registro de divergências:** falta, excesso e avaria, cada uma com destino documentado (MMS-RG-07) — nenhuma diferença desaparece sem rastro.
- **Entrada no estoque:** conferência concluída gera o documento de entrada no MMS-004 (integração interna Receiving ↔ Inventory, MMS-001 §22).
- **Retomada de atendimento pendente:** quando a origem é compra destinada a uma solicitação de material, a entrada dispara o atendimento da MMS-003 (MMS-001 §8.4); em **compra dedicada** (parametrizada), o material entra no estoque já reservado para a solicitação de origem.
- **Tolerâncias parametrizáveis:** limites de aceite de divergência por empresa/item (MMS-RG-11).
- **Cancelamento de recebimento aguardando**, com as restrições de estado da máquina de estados.

## O que NÃO faz

- **Não inspeciona qualidade com certificação:** quarentena/inspeção de qualidade é evolução da v2.0 (MMS-001 §8, sugestão de evolução) — o MVP registra avaria como divergência, sem fluxo de inspeção formal.
- **Não compra nem aprova compra:** o Pedido de Compra nasce no domínio PR-001/Procurement; este módulo apenas o consome como documento de origem.
- **Não altera saldo diretamente:** quem registra a entrada (e o saldo) é o MMS-004, por documento de movimentação (MMS-P-07/P-08).
- **Não cuida de pagamento/financeiro:** fora da suíte (MMS-001 §8.4).
- **Não cadastra itens nem gerencia reservas:** catálogo é MMS-002; reservas são MMS-004.
- **Não recebe sem documento:** recebimento informal (material que "apareceu") não existe — toda entrada tem origem referenciada.

## Integrações esperadas

| Integração | Direção | Comportamento |
|------------|---------|---------------|
| MMS-004 Inventory | Dispara | Conferência concluída gera documento de entrada; origem dedicada entra já reservada |
| MMS-003 Material Requisition | Alimenta | Entrada de material comprado retoma o atendimento pendente da solicitação |
| PR-001 / Procurement (futuro PO) | Consome | Pedido de compra como documento de origem, com quantidades esperadas e previsão |
| MMS-002 Item Catalog | Consome | Itens válidos para conferência (somente ativos; unidade base e unidades de compra com fator de conversão — ADR-013) |
| Foundation (FD-001-01..10) | Consome | Identidade, organização, workflow (aprovação de divergência quando parametrizada), notificações, auditoria, timeline, configuração |

---

# Objetivos do Negócio

1. **Proteger a acuracidade do estoque na entrada** — a conferência é a primeira linha de defesa do KPI de acuracidade ≥ 98% (MMS-001 §20).
2. **Fechar a rota de compra** — material comprado volta ao fluxo e atende a solicitação de origem sem intervenção manual.
3. **Evidenciar a qualidade de fornecedores e transporte** — divergências registradas viram o KPI de divergência de recebimento.
4. **Eliminar entrada informal** — toda entrada com documento, responsável e data/hora (MMS-P-01, MMS-RG-06).

## Quem utiliza

| Papel | Uso principal |
|-------|---------------|
| Almoxarife | Registra recebimentos e executa conferências (Warehouse Workspace, MMS-001 §12.2) |
| Supervisor de Almoxarifado | Trata divergências e aprova destinos quando parametrizado |
| Comprador | Acompanha recebimento dos pedidos originados no PR-001 |
| Solicitante | É beneficiado indiretamente (material comprado retoma sua solicitação) |
| Gerente de Suprimentos | KPIs de divergência e lead time de fornecedor |
| Administrador | Configura tolerâncias e parâmetros |
| Auditor | Consulta trilhas e documentos (somente leitura) |

---

# Objetivos do MVP

Conforme o roadmap oficial (MMS-001 §24 — "Receiving (núcleo)"):

1. Recebimento contra documento de origem (pedido de compra, transferência ou devolução);
2. Conferência quantidade recebida × esperada, item a item;
3. Registro de divergências (falta, excesso, avaria) com destino documentado;
4. Geração da entrada no estoque (MMS-004) ao concluir a conferência;
5. Retomada do atendimento pendente da MMS-003 para material de rota de compra;
6. Tolerâncias de divergência parametrizáveis (MMS-RG-11).

---

# Problemas que Resolve

| Problema atual | Como o módulo resolve |
|----------------|----------------------|
| Material entra sem conferência | Recebimento sempre contra documento, com conferência item a item (MMS-RG-06) |
| Diferenças "somem" na operação | Falta/excesso/avaria com destino documentado e auditável (MMS-RG-07) |
| Compra não fecha o ciclo da solicitação | Entrada dispara atendimento pendente da MMS-003; compra dedicada entra reservada |
| Fornecedor sem medição de qualidade | KPI de divergência de recebimento por origem |
| Estoque inflado por recebimento errado | Saldo só muda por documento de entrada gerado após conferência (MMS-P-08) |

---

# Atores

| Ator | Tipo | Interação |
|------|------|-----------|
| Almoxarife | Humano | Registra recebimento e confere |
| Supervisor de Almoxarifado | Humano | Trata divergências, aprova destinos restritos |
| MMS-004 Inventory | Sistema | Recebe o documento de entrada e atualiza saldos |
| MMS-003 Material Requisition | Sistema | Retoma atendimento de itens comprados |
| PR-001 / Procurement | Sistema | Fornece documento de origem (pedido) e expectativa de quantidades |
| FD-001-04 Workflow | Sistema | Aprovação de tratativa de divergência quando parametrizada |
| FD-001-05 Notifications | Sistema | Avisa interessados sobre recebimento, divergência e atendimento retomado |

---

# Fluxo Macro

Recorte do fluxo corporativo oficial (MMS-001 §9) sob a ótica deste módulo:

```
Documento de origem (Pedido / Transferência / Devolução)
        ↓
Recebimento registrado (Aguardando)
        ↓
Conferência item a item (Em Conferência)
        ↓
   ┌────┴──────────────┐
Sem divergência      Com divergência (falta/excesso/avaria)
   ↓                       ↓
Concluído            Concluído com Divergência
   ↓                  (destino documentado; aprovação se parametrizada)
   └────────┬─────────┘
            ↓
Entrada no estoque (MMS-004 — documento de entrada)
            ↓
   ┌────────┴─────────┐
Origem = compra     Origem = transferência/
de solicitação      devolução
   ↓                       ↓
Retoma atendimento   Estoque disponível
da MMS-003           (compra dedicada:
(compra dedicada:    entra reservada)
entra reservada)
```

Regras herdadas que este módulo impõe:

1. **Nenhuma entrada sem documento de origem** (MMS-RG-06);
2. **Nenhuma divergência sem destino documentado** (MMS-RG-07);
3. **Saldo só muda no MMS-004**, por documento de movimentação gerado após a conferência (MMS-P-07/P-08);
4. **Compra dedicada parametrizável**: entrada já reservada para a solicitação de origem (regra 4 do fluxo corporativo).

---

# Entradas

| Entrada | Origem | Observação |
|---------|--------|------------|
| Documento de origem | PR-001/Procurement (pedido), MMS-004 (transferência), MMS-003 (devolução) | Obrigatório (MMS-RG-06) |
| Quantidades esperadas | Documento de origem | Base da conferência |
| Quantidades recebidas | Almoxarife | Conferência física; pode ser informada na unidade de compra, convertida para a base na entrada (ADR-013) |
| Divergências e destinos | Almoxarife/Supervisor | Falta, excesso, avaria com destino documentado |
| Tolerâncias | FD-001-10 (`materials.receiving.*`) | Limites de aceite parametrizáveis (MMS-RG-11) |
| Vínculo com solicitação | MMS-003 / PR-001 | Identifica compra dedicada e atendimento pendente |

---

# Saídas

| Saída | Destino | Observação |
|-------|---------|------------|
| Documento de entrada | MMS-004 | Gera movimentação de entrada e atualiza saldo |
| Sinal de atendimento pendente | MMS-003 | Material comprado retoma a solicitação de origem |
| Registros de divergência | Auditoria/gestão | Falta, excesso, avaria com destino |
| Eventos de negócio | Barramento (FD-001) | Ver "Eventos Publicados" |
| Notificações | FD-001-05 | Recebimento registrado, divergência, atendimento retomado |
| Registros de timeline e auditoria | FD-001-07 / FD-001-06 | Todas as etapas e decisões |

---

# Dependências do Foundation

| Domínio | Uso no módulo |
|---------|---------------|
| FD-001-01 Identity & Access | Autenticação, papéis, permissões, escopo organizacional (MMS-P-05) |
| FD-001-02 Organization | Empresa, unidade e almoxarifado do recebimento |
| FD-001-04 Workflow | Aprovação de tratativa de divergência quando parametrizada (MMS-P-04) |
| FD-001-05 Notifications | Todos os avisos — o módulo nunca notifica por conta própria |
| FD-001-06 Audit | Auditoria de recebimento, conferência, divergência e destino (MMS-P-02) |
| FD-001-07 Timeline | Linha do tempo do recebimento e vínculo com a solicitação (MMS-P-03) |
| FD-001-10 Configuration | Parâmetros `materials.receiving.*` (MMS-P-06, MMS-RG-11) |

Parâmetros previstos (nomes conceituais, definição fina no documento de configuração do módulo):

| Parâmetro | Efeito |
|-----------|--------|
| `materials.receiving.tolerance.quantity` | Tolerância de divergência de quantidade por empresa/item |
| `materials.receiving.divergence.approval-required` | Exigência de aprovação para tratativa de divergência |
| `materials.receiving.dedicated-purchase.auto-reserve` | Compra dedicada entra automaticamente reservada (espelha `materials.requisition.purchase.dedicated`) |
| `materials.receiving.partial.allowed` | Permite recebimento parcial contra o mesmo documento de origem |

---

# Eventos Publicados (funcionais)

Conforme o catálogo corporativo (MMS-001 §16); a especificação técnica (envelope, payloads) seguirá o padrão ADR-010 quando o pacote funcional do módulo for detalhado:

1. Recebimento registrado / em conferência / concluído / concluído com divergência / cancelado;
2. Divergência registrada (falta, excesso, avaria) com destino;
3. Entrada no estoque gerada (referência ao documento de movimentação do MMS-004);
4. Atendimento de solicitação retomado (material de rota de compra disponível).

# Eventos Consumidos (funcionais)

| Evento | Origem | Reação do módulo |
|--------|--------|------------------|
| Pedido de compra confirmado / previsão de entrega | PR-001 / Procurement (futuro) | Cria expectativa de recebimento (Aguardando) |
| Transferência expedida | MMS-004 | Cria expectativa de recebimento no depósito de destino (v1.1) |
| Devolução registrada pelo solicitante | MMS-003 (futuro) | Cria expectativa de recebimento de devolução |
| Item inativado | MMS-002 | Alerta em recebimentos pendentes do item (recebimento em trânsito segue conferível) |

---

# Requisitos Não Funcionais

| Categoria | Requisito |
|-----------|-----------|
| Multiempresa | Isolamento por empresa em toda consulta, regra e evento (MMS-001 §6.2) |
| Escopo organizacional | Recebimento ocorre em almoxarifado/depósito do escopo do operador (FD-001-01/02) |
| Performance | Conferência otimizada para operação rápida (scanner-friendly, preparação para código de barras — MMS-001 §12.2) |
| Confiabilidade | Entrada no estoque nunca é perdida: geração do documento via outbox na mesma transação da conclusão (padrão ADR-010) |
| Auditabilidade | 100% das conferências, divergências e destinos auditados (MMS-P-02) |
| Segurança | Deny by default; menu sem permissão é ocultado; destino de divergência restrito a papel autorizado |
| Internacionalização | Mensagens via catálogo i18n do Foundation (padrão PR-001-14) |

---

# Restrições Arquiteturais

1. **Documentação é a fonte da verdade:** conflitos entre implementação e este documento (ou o MMS-001) resolvem-se pela documentação.
2. **Fronteira de domínio inviolável:** este módulo não mantém saldos, catálogo, compras ou solicitações — consome documentos de origem e dispara entradas via contratos/eventos (baixo acoplamento, ADR-012).
3. **Nenhuma regra operacional em código:** tolerâncias, aprovações e comportamentos variáveis são parâmetros FD-001-10 (MMS-P-06, MMS-RG-11).
4. **Saldo somente no MMS-004:** este módulo jamais altera saldo diretamente (MMS-P-08).
5. **Aprovação somente via FD-001-04** e **notificação somente via FD-001-05** (padrões da suíte).
6. **Máquina de estados única:** `Aguardando → Em Conferência → Concluído / Concluído com Divergência / Cancelado` (MMS-001 §15.3); nenhuma transição fora dela nem edição direta de estado.
7. **Sem inspeção de qualidade no MVP:** quarentena é v2.0 e reutilizará a state machine de documentos, sem fluxo paralelo (MMS-001 §8, sugestão de evolução).

---

# Matriz de Dependência com Outros Módulos

| Módulo | Tipo de dependência | Natureza |
|--------|--------------------|----------|
| MMS-001 | Documento mestre | Regras, fluxo e estados herdados (obrigatória) |
| MMS-004 Inventory | Forte | Sem o documento de entrada não há efeito no estoque |
| MMS-003 Material Requisition | Forte | Fecha a rota de compra; sem ela o atendimento pendente não retoma |
| MMS-002 Item Catalog | Forte | Itens e unidades válidas para conferência |
| PR-001 / Procurement | Forte (inter-domínio) | Documento de origem da rota de compra (pedido futuro) |
| FD-001-01/02/04/05/06/07/10 | Forte | Infraestrutura funcional do Foundation |

---

# KPIs

| KPI | Cálculo | Meta/observação |
|-----|---------|-----------------|
| Divergência de recebimento | recebimentos com divergência ÷ total de recebimentos | Qualidade de fornecedores/transporte (MMS-001 §20) |
| Lead time de entrega | pedido confirmado → recebimento concluído | Base para negociação com fornecedores |
| Tempo de conferência | início da conferência → entrada no estoque | Eficiência operacional do almoxarifado |
| Acuracidade de entrada | entradas sem ajuste posterior ÷ entradas | Contribui para a acuracidade ≥ 98% da suíte |
| Recebimentos pendentes | documentos aguardando há mais de N dias (parametrizável) | Saúde da fila de recebimento |

---

# Critérios de Qualidade

- Todo recebimento referencia um documento de origem válido (MMS-RG-06);
- Toda divergência tem tipo (falta/excesso/avaria), quantidade e destino documentado (MMS-RG-07);
- Toda conferência concluída gera exatamente um documento de entrada no MMS-004;
- Toda entrada de rota de compra retoma o atendimento da solicitação de origem;
- Toda transição de estado é ação de negócio documentada — nunca edição direta (MMS-001 §15);
- Toda notificação passa pelo FD-001-05; toda auditoria pelo FD-001-06.

---

# Critérios de Conclusão do Módulo (DoD)

O módulo MMS-005 estará concluído (documentação + implementação MVP) quando:

1. Esta visão estiver aprovada e registrada no GOV-002;
2. O pacote funcional detalhado existir: regras de negócio, máquina de estados fina, casos de uso, permissões (RBAC/ABAC, padrão PR-001-09), eventos (padrão ADR-010), API, UX/wireframes e critérios de aceite — seguindo o padrão do pacote PR-001;
3. O fluxo receber → conferir → divergir/concluir → dar entrada estiver operacional com as integrações MMS-004 e MMS-003;
4. Compra dedicada entrando reservada quando parametrizada;
5. Auditoria, timeline e notificações ativas em 100% das etapas;
6. KPIs de recebimento calculáveis a partir dos dados do módulo.

---

# Roadmap

## Versão 1 (MVP)

Escopo dos "Objetivos do MVP": recebimento contra documento, conferência, divergências com destino, entrada no estoque, retomada de atendimento pendente, tolerâncias parametrizáveis.

## Versão 1.1

- Recebimento de transferência entre depósitos com conferência no destino (MMS-001 §24);
- Recebimento de devolução de solicitante com reentrada no estoque;
- Compra dedicada com reserva automática plenamente integrada;
- Recebimento parcial múltiplo contra o mesmo documento;
- Exportações e relatório de divergências por fornecedor/origem.

## Versão 2.0

- **Quarentena/inspeção de qualidade:** recebido → em inspeção → liberado/recusado, reutilizando a state machine de documentos (MMS-001 §8, sugestão de evolução);
- Controle por lote/validade e série na conferência;
- Integração com agendamento de docas e janelas de recebimento.

---

# Funcionalidades Futuras

Registradas para visibilidade — **fora do compromisso de qualquer versão**: coletor de dados/RFID na conferência, mobile offline para doca, OCR de notas fiscais e documentos de transporte, integração fiscal de entrada (escrituração), scorecard automático de fornecedor alimentado pelas divergências.

---

# Versionamento do Módulo

- **Versão do documento:** 1.0.0 (visão do módulo — primeira emissão).
- **Esquema:** SemVer; mudanças de escopo/regra exigem nova versão e entrada no histórico.
- **Pacote funcional detalhado:** será versionado documento a documento, seguindo o padrão do PR-001 (01-business-context … 17-test-scenarios) quando iniciado.

---

# Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação da visão do módulo Receiving: recebimento físico contra documento de origem (MMS-RG-06), conferência quantitativa, divergências com destino documentado (MMS-RG-07), entrada no estoque via MMS-004, retomada de atendimento pendente da MMS-003 e compra dedicada parametrizável; sem inspeção de qualidade no MVP (v2.0); fronteiras com MMS-002/003/004 e PR-001; NFRs, KPIs, DoD e roadmap. Fontes: MMS-001 (§8.4, §9, §14, §15.3, §16, §22, §23, §24) |
| 1.1.0 | 2026-08-08 | Propagação de ADR-013 (Conversão de UoM): a conferência pode ser feita na unidade de compra do item, com conversão para a unidade base ao gerar a entrada no estoque (fator aplicado registrado no documento — MMS-004 IV-BR-009); integração com MMS-002 e entradas atualizadas. Fronteira preservada: o saldo continua exclusivamente no MMS-004, na unidade base |
