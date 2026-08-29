# PROMPT OFICIAL V2 — TRINO SUPPLY

> **Status: OFICIAL — aprovado pelo responsável do produto em 2026-08-29.**
> Substitui o `prompt-sistema-trino-supply.md` como especificação de referência; a aprovação
> cobre também a generalização da regra 1 (cotação a partir de itens de várias SCs — §8/§20-1).
> Construído como **superconjunto** do documento oficial atual: tudo o que existe está preservado;
> as evoluções aprovadas na `analise-evolucao-trino-supply.md` entram marcadas com a prioridade.
>
> Legenda de marcação:
> - *(sem marca)* — funcionalidade **existente em produção**; não pode ser removida nem alterada sem justificativa formal.
> - **[V2-P1] … [V2-P4]** — evolução aprovada, com a prioridade do backlog (P1 crítica → P4 futura).
> - **⚠ ALTERAÇÃO DE REGRA EXISTENTE — NECESSITA APROVAÇÃO** — único ponto do V2 que muda uma regra atual.

---

## 1. O que é o sistema

Plataforma web de **Procurement e Suprimentos do GRUPO TRINO**: ciclo completo de compras e de
material de almoxarifado — **solicitação → aprovação → cotação/BID → negociação → duas alçadas →
registro da O.C. fechada no ERP SENIOR → faturamento → entrega** — com cadastros, dashboards e
timeline auditável; e, na evolução V2, a camada de gestão: **homologação e avaliação de
fornecedores, contratos com teto e saldo, compliance, SLA/aging, risco e insights**.

Objetivos estratégicos (as quatro perguntas): **comprar bem** (saving de negociação e de
referência), **com eficiência** (lead time, SLA, aging, produtividade), **com controle**
(alçadas, segregação, compliance score, auditoria) e **reduzindo riscos** (homologação,
certidões, scorecard, concentração).

Premissa de UX: **quanto mais simples e óbvio, melhor — o leigo também usa.** Nenhuma
funcionalidade de tela existe sem backend funcional correspondente (nada fictício).

### Fronteiras (o que o sistema NÃO faz)

- **Não emite Ordem de Compra nem liquida financeiro.** A O.C. é fechada no **ERP SENIOR**; aqui
  ela é **registrada** (número, data, anexo) após as duas aprovações.
- **Não é o sistema de saldo físico do almoxarifado** (escopo do sistema legado/WMS). Aqui vive o
  **atendimento** das solicitações de material.
- **Não trata FISPQ** nem estoque físico de químicos (gestão do SESMT).

---

## 2. Arquitetura

| Camada | Tecnologia |
|---|---|
| API | .NET 9, minimal APIs, projeto único `TrinoSupply.Foundation.Api` |
| Dados | PostgreSQL + EF Core 9 (Npgsql); migrations versionadas, **sempre aditivas e preservando dados** |
| Front | SPA de arquivo único (`wwwroot/index.html`); Portal do Fornecedor em `portal.html` |
| Auth | JWT 15 min + refresh token com renovação automática no cliente |
| Documentos | `foundation.stored_document` (anexos, fotos, certidões, notas — até 10 MB) |
| PDF | QuestPDF (OC legada) |
| Deploy | Docker → Railway; deploy automático no merge para `main` |

Envelope de resposta: `{ data, correlationId }` / `{ error: { code, message }, correlationId }`.
HTML servido com `Cache-Control: no-cache, must-revalidate`; assets cacheáveis.

**Regra de engenharia [V2-P1]: códigos de erro são imutáveis.** Um código publicado nunca muda de
significado; conceito novo recebe código novo. Faixas reservadas para o V2: `PR-ERR-050+`,
`SUP-ERR-030+`, `CT-ERR-0xx` (contratos), `CP-ERR-0xx` (compliance), `INS-ERR-0xx` (insights).

---

## 3. Identidade, papéis e autorização

### Papéis
`SystemAdministrator`, `Requester`, `Approver`, `PurchasingOfficer`, `SupplyManager`, `Director`,
`WarehouseOperator`, `WarehouseSupervisor`, `Auditor`.

### Módulos autorizáveis
Atuais: `SOLICITACOES`, `APROVACAO`, `MATERIAL`, `ESTOQUE`, `COMPRAS`, `PRODUTOS`,
`FORNECEDORES`, `CENTROS_CUSTO`, `USUARIOS`.
**[V2-P2]** Novos: `CONTRATOS` (gestão de contratos), `COMPLIANCE` (score e controles),
`INSIGHTS` (painel de anomalias).

**Acesso = papel E módulo autorizado.** Admin tem todos; o menu mostra só o que o usuário pode
usar. Cadastro de usuário: nome, e-mail, papel, senha inicial (mín. 12), módulos, centros de
custo vinculados (vazio = sem restrição), diretor responsável; **[V2-P4]** flag opcional
"pode registrar entrega" para Requester.

---

## 4. Menu

Estrutura atual preservada (Dashboard de Suprimentos; Solicitações de Compra com Nova
Solicitação/Meus Pedidos/Central de Aprovação; Material; Estoque com Fila e Painel de
Atendimentos; Compras com Gestão de Solicitações, Cotações [Abrir/Processos] e Pedidos de
Compra; Cadastros com Produtos/Famílias, Centros de Custo/Empresas, Usuários, Fornecedores).

**[V2]** Novos itens, cada um sob o seu módulo: **Contratos** (em Compras ou Cadastros →
Fornecedores), **Compliance** e **Insights** (em um grupo "Gestão" junto do Dashboard
Executivo), **Homologação** (aba dentro do fornecedor, não item de menu).

---

## 5. Alçadas por centro de custo

Cada centro tem duas listas de aprovadores — **Nível 1** (libera a solicitação de material e a 1ª
aprovação da cotação) e **Nível 2** (autoriza a compra). **Qualquer pessoa do nível resolve a
etapa.** Centro sem lista usa o vínculo antigo (gerente do centro / diretor do gerente). As
mensagens de recusa dizem de quem é a alçada (`MR-ERR-002`, `RFQ-ERR-031`, `RFQ-ERR-032`).

**Segregação de funções:** quem selecionou o vencedor não aprova a própria escolha; o N2 não pode
ser quem selecionou nem quem deu o N1 (`RFQ-ERR-030`). O comprador que conduz não delibera.

**[V2-P3] Limite de valor por nível (opcional).**
- **O quê:** campo `limite de valor` por nível no cadastro do CC. **Quem:** quem mantém CC.
- **Efeito:** **medição e alerta**, não bloqueio — processo acima do limite aparece no
  Compliance como "acima da alçada configurada". Sem limite cadastrado, o controle não penaliza.
- **Não substitui** as listas de pessoas (regra crítica 4 intocada).

---

## 6. Solicitação de Compra (SC)

### Existente
- Cabeçalho: justificativa, centro de custo (restrito aos vinculados — `PR-ERR-021`), prioridade
  NORMAL/URGENTE, data necessária, local de entrega, observações, anexos.
- Itens: descrição livre **ou** item do catálogo, quantidade, unidade, preço estimado.
  Item livre não bloqueia a SC (exceto EPI/EPC sem C.A. — §12).
- **Solicitação em Lote**: grade por família com preço, saldo, previsão de entrada, consumo médio
  (90 dias) e cobertura; o solicitante digita só quantidades.
- Estados persistidos: `Draft → Submitted → Approved | Returned | Rejected | Cancelled`.
- **Janela de alteração:** o solicitante edita/exclui rascunho; após o envio ainda altera
  **enquanto não houver comprador designado nem cotação** (`PR-ERR-040`, `PR-ERR-041`).
- **Meus Pedidos** com o status do processo (§8) e ações por estado.
- **Central de Aprovação** com o que está na alçada do usuário, já com preços.

### [V2-P1] Urgência justificada
- **O quê:** prioridade URGENTE passa a exigir `Justificativa de urgência` e
  `Impacto da não compra`. **Quem:** o solicitante, na criação/edição. **Quando:** sempre que
  prioridade = URGENTE (criação, edição ou upgrade de prioridade).
- **Dados:** 2 campos texto novos (anuláveis no banco; obrigatórios pela API quando URGENTE).
- **Erro:** `PR-ERR-050` — "Compra urgente exige a justificativa da urgência e o impacto de não
  comprar." **Histórico:** os textos entram na timeline e aparecem na triagem e na cotação.
- **Impacto:** insumo do Compliance Score (§14) e do insight de emergenciais recorrentes.

### [V2-P3] Alteração de prioridade pela gestão
- **Quem:** `SupplyManager`/comprador designado, na Gestão de Solicitações. **Regra:**
  justificativa obrigatória; elevação a URGENTE exige os campos do item acima. **Histórico:**
  evento `PRIORIDADE_ALTERADA` com antes/depois e motivo.

---

## 7. Material / Almoxarifado

Fluxo existente, intocado:
1. Solicitante pede material do catálogo (CC + itens + quantidades).
2. **Nível 1 do centro** aprova — **pode reduzir quantidades, nunca trocar o item**
   (`MR-ERR-031`) — ou recusa com justificativa (`MR-ERR-030`).
3. Só o aprovado entra na **Fila de Atendimento**.
4. O estoque informa o entregue por item ("Atender tudo" = aprovado).
5. **O faltante vira SC automaticamente, no nome e no CC do solicitante original**, já na fila de
   triagem.

Estados: `Submitted → Approved → Fulfilled | PartiallyFulfilled | PurchaseRoute | Rejected | Cancelled`.

**Painel de Atendimentos** com 4 cartões clicáveis (aguardando aprovação, em andamento,
concluídos, parciais aguardando compra), cada um abrindo a lista por trás do número, com atalhos
para Central de Aprovação, Fila de Atendimento e Processos de Cotação, e análises por centro e
por solicitante.

---

## 8. Gestão de Demandas (triagem)

### Existente
Painel com **uma linha por item** das SCs e solicitações de material em aberto; designação de
comprador (transfere a responsabilidade); filtros por solicitante, comprador e status; os **8
status do processo** derivados de (SC, cotação, pedido): `PENDENTE`, `EM_COTACAO`,
`AGUARDANDO_APROVACAO`, `PEDIDO_APROVADO`, `PEDIDO_REJEITADO`, `OC_FATURAMENTO`,
`PEDIDO_ENTREGUE`, `CANCELADO_PARCIAL`.

**Regra de arquitetura (mantida):** o status do processo é **derivado, nunca persistido em
paralelo** — não criar máquina de estados redundante por item.

### [V2-P1] Aging
- **O quê:** cada item exibe a faixa de idade — padrão 0-2 / 3-5 / 6-10 / >10 dias — contada da
  entrada na fila (envio da SC ou criação do faltante). **Configurável** pelo admin
  (parâmetros do sistema). **Filtro por faixa** e contadores no topo.
- **Dados:** nenhum novo (usa timestamps existentes). **Resultado:** fila ordenável por idade;
  base do Dashboard de Backlog.

### [V2-P3] Redistribuição em lote
- **O quê:** seleção múltipla de itens e designação/redesignação de comprador em uma ação.
- **Histórico:** um evento por item, com quem designou.

### [V2-P2] Agrupamento de demandas (multi-SC) — ⚠ ALTERAÇÃO DE REGRA EXISTENTE — NECESSITA APROVAÇÃO
- **Regra atual:** a cotação nasce de **uma** SC aprovada. **Regra V2 (generalização):** a
  cotação nasce de **itens aprovados de uma ou mais SCs**. O "aprovada" é intocado
  (regra crítica 1); muda apenas a cardinalidade.
- **O quê:** o comprador seleciona na triagem itens idênticos ou da mesma família/categoria,
  provenientes de SCs diferentes, e abre **uma** cotação consolidada.
- **Rastreabilidade obrigatória:** cada item da cotação referencia o **item de SC de origem**
  (`quotation_item.source_pr_item_id`); status derivado, avisos e "Meus Pedidos" continuam por
  solicitante; a O.C. registra o rateio por origem; a timeline da cotação lista as SCs de origem.
- **Dados:** FK nova em `quotation_item`; `quotation.source_pr_id` passa a anulável
  (registros legados preservados, caminho 1:1 continua funcionando idêntico).
- **Erros:** item não aprovado na seleção → recusa (mesmo conceito da regra 1); item já em outra
  cotação aberta → recusa com o número do processo.
- **Impacto:** `MarkSourcePrApproved`, triagem, `ProcessStatus` e avisos passam a operar por item
  de SC de origem. **Entrega isolada, com testes de regressão do fluxo de SC única.**

---

## 9. Cotação / BID — processo de ponta a ponta

### Existente (intocado)
- Duas telas: **Abrir Cotação** (fila de aprovadas) e **Processos de Cotação** (condução).
- Tipos: compra, serviço, BID; numeração única; prazo de resposta.
- **Fornecedores concorrentes** conviváveis durante cotação/análise (`RFQ-ERR-020`); convite
  registrado; texto pronto com o link do portal.
- **Propostas versionadas e imutáveis** (regra crítica 2), por duas vias: comprador lança e
  **anexa a proposta oficial**; ou o fornecedor envia pelo **Portal** (CNPJ + chave).
- Campos: preço por item, frete, desconto, moeda, validade, prazo de entrega, condição de
  pagamento, **prazo para pagamento (dias)**. Desconto > total é recusado (`RFQ-ERR-021`).
- **Mapa de cotação** com melhor preço e menor total destacados.
- **Negociação e ganho:** valor fechado ou desconto % → nova versão da proposta + **saving
  contra a 1ª proposta do fornecedor** (valor maior não é ganho — `RFQ-ERR-022`); preservado na
  escolha; timeline `NEGOCIACAO_REGISTRADA`.
- **Escolha do vencedor:** versão mais recente, critérios marcados, justificativa obrigatória.
- **Alçadas:** Aprovador 01 (N1) → Aprovador 02 (N2), com segregação; cada um aprova, rejeita ou
  pede ajustes (volta ao comprador **zerando a aprovação N1** — qualquer alteração material
  reinicia as alçadas).
- **Registro da O.C. do SENIOR:** número/data/anexo; só após as duas alçadas (`RFQ-ERR-040`);
  número único (`RFQ-ERR-041`); o número da O.C. é o número do pedido.
- **Faturamento** (notas com data de faturamento, valor, anexo; total faturado × total) e
  **entrega** (quantidade por item, encerrar saldo com motivo, data de entrega final) **dentro
  do processo**. **Fornecedor com contrato de parceria vigente:** sinalizado, com o botão
  **"Preencher pelo contrato de parceria"**.
- **Timeline** integral por evento.

### [V2-P2] Impostos e outros custos
- **O quê:** proposta ganha `impostos` e `outros custos`; total = itens + frete + impostos +
  outros − desconto; mapa ganha as duas linhas. **Quem/quando:** quem registra a proposta
  (comprador ou portal). **Impacto:** base futura do TCO.

### [V2-P1] Data prometida e OTIF
- **O quê:** no registro da O.C., o sistema **congela** `data prometida` = data da O.C. + prazo
  de entrega da proposta vencedora (editável pelo comprador com justificativa). A cada entrega:
  **On-Time** = entrega concluída até a data prometida; **In-Full** = todas as quantidades
  recebidas. OTIF do pedido = On-Time ∧ In-Full; consolidação mensal por fornecedor.
- **Dados:** campo novo `promised_date` no pedido; o resto já existe. **Histórico:** o OTIF do
  pedido aparece no detalhe e na avaliação do fornecedor.

### [V2-P2] Saving de referência
- **O quê:** no registro da O.C., para cada item **de catálogo**, o sistema busca o **último
  preço pago** (última O.C. não cancelada com aquele item) e congela
  `saving de referência = (último preço − preço fechado) × quantidade`.
- **Regra:** métrica **separada e com nome próprio** — nunca se mistura ao saving de negociação
  (regra crítica 5). Sem histórico do item, fica vazio (não zero).
- **Resultado:** somado no dashboard ao lado do saving de negociação.

### [V2-P4] Score multicritério da cotação (informativo)
- Só depois de OTIF/qualidade/risco existirem: score 0–100 por proposta
  (peso sugerido: preço 40, prazos 20, OTIF 20, qualidade 10, risco 10), **exibido como apoio**.
  A escolha continua livre com justificativa; tornar o score obrigatório é decisão de negócio
  futura, fora deste prompt.

---

## 10. Pedidos de Compra

Histórico dos pedidos (O.C. do ERP, fornecedor, itens, total, situação `EMITIDO/FATURADO/
PARCIAL/RECEBIDO/CANCELADO`); lança faturamento/entrega e completa pedidos antigos; PDF da OC
legada disponível. **[V2-P2]** Pedido ganha `data prometida`, OTIF e o **vínculo explícito com o
contrato** do fornecedor quando aplicável (para saldo e spend sob contrato).
**[V2-P3]** Registro de **devolução/rejeição** por item no recebimento (habilita "qualidade" no
scorecard), com motivo e evento na timeline.

---

## 11. Fornecedores, homologação e contratos

### Existente (intocado)
Cadastro (razão social, fantasia, **CNPJ/CPF único**, contato, situação), chave do Portal
(exibida uma vez, só hash persistido), inativo não recebe pedidos. **Contrato de parceria**:
número, vigência, produtos com **preço, condição, prazo de pagamento e prazo de entrega fixos**;
válido só na vigência; lista vazia encerra (`SUP-ERR-020/021/022/023`); preenche a proposta em um
clique.

### [V2-P2] Homologação
- **Estados:** `PROSPECT → EM_HOMOLOGACAO → HOMOLOGADO → RESTRITO → BLOQUEADO → INATIVO`.
- **Migração:** todo fornecedor ativo atual nasce **HOMOLOGADO** (grandfathering — a operação não
  para). **Quem:** `SupplyManager` muda estados; timeline do fornecedor registra.
- **Certidões:** CND Federal, FGTS, CNDT, contrato social (extensível), cada uma com anexo
  (`stored_document`) e **validade**. Avisos a 30/15/0 dias na Central de Avisos.
- **Regra de restrição:** certidão vencida → `RESTRITO` **automático somente para fornecedor que
  já tem certidões cadastradas** (quem nunca cadastrou não é punido retroativamente).
- **Regra de bloqueio:** PROSPECT/EM_HOMOLOGACAO **podem ser convidados e propor**; a **seleção
  do vencedor** exige HOMOLOGADO — erro `SUP-ERR-030`: "Conclua a homologação do fornecedor
  antes de selecioná-lo." BLOQUEADO não é convidável.

### [V2-P3] Avaliação periódica (Supplier Scorecard)
- Apuração mensal por fornecedor: **OTIF %**, lead time médio real, **competitividade**
  (preço dele × melhor do mapa nas cotações que participou), qualidade (% devolução — depende de
  §10). Classes: A ≥ 90 (estratégico), B 75–89, C 60–74 (plano de ação), D < 60 (crítico —
  alerta; bloqueio de convite é decisão do gestor, não automática).

### [V2-P2] Contratos — teto e saldo
- **O quê:** contrato ganha `valor contratado (teto)`, **[V2-P3]** `índice de reajuste`,
  `gestor responsável`, `anexo do contrato`. Cada O.C. registrada de fornecedor com contrato
  vigente **abate o saldo** (consumo = Σ O.C.s na vigência).
- **Alertas:** saldo < 20% (`CT-ERR-010` ao tentar exceder o teto: aviso, com prosseguir
  justificado — não bloqueio cego); vigência a 90/60/30 dias e vencida — na Central de Avisos.
- **Indicadores:** spend sob contrato × fora de contrato; consumo por contrato.

### [V2-P3] Risco (parcial)
Concentração de spend (fornecedor ≥ 40% da categoria) e single-source calculados dos dados;
classificação Baixo/Médio/Alto/Crítico exibida no fornecedor. Dependência financeira: **futura**
(dado externo).

---

## 12. Catálogo de produtos

Existente (intocado): tela **por busca**; código gerado pela família; **foto** (miniatura +
ampliação); **fornecedores do produto com C.A. por fornecedor** — EPI/EPC só circulam com C.A.
em pelo menos um fornecedor (`IC-ERR-023`); tipos de produto; **importação por planilha** com
prévia, grade de tamanhos e recusa de linha inválida/duplicada (`IMP-ERR-012`); **famílias** com
os **4 prazos-meta** (0–365 dias — `IC-ERR-026`), renomeação propagada e unificação.

**[V2-P3] Categoria** — cadastro simples que **agrupa famílias** (FK opcional na família).
Nada muda na família ou no produto; spend, contratos ("categoria tabelada") e insights ganham a
dimensão. **[V2-P4]** Marcação visual de "pendência cadastral" no item livre da SC + ação
"criar produto a partir do item".

---

## 13. SLA e prazos

Existente: prazos-meta por família × realizado no dashboard (4 etapas: solicitação→cotação,
cotação→aprovação, aprovação→O.C., O.C.→entrega), com etapa estourada destacada.

**[V2-P2] SLA por processo** — cada processo é medido contra a meta da(s) família(s) dos seus
itens; o painel passa a mostrar, por etapa e no total: **% dentro do prazo, % fora, média e
mediana**; processos fora do SLA listados com drill-down. Dados: timestamps já existentes.

---

## 14. Compliance **[V2-P2, após P1]**

- **O quê:** todo processo concluído recebe **Compliance Score = 100 − penalizações**:
  sem cotação competitiva (1 proposta) −25 · emergencial −20 · fornecedor não homologado na
  seleção −30 · sem contrato em categoria tabelada −15 · SC criada após a data da necessidade
  −20 · acima do limite de alçada configurado (§5) −10. **Pesos parametrizáveis** pelo admin.
- **Quem vê:** módulo `COMPLIANCE` (Auditor, SupplyManager, Director).
- **Resultado:** selo no processo com o **detalhamento de cada penalidade** (evidência: nº de
  propostas, datas, estado do fornecedor); médias por comprador/CC/período no dashboard.
- **Regra:** o score **não bloqueia** nenhum processo — mede e expõe. Histórico: penalidades
  gravadas com o processo (imutáveis após o encerramento).

---

## 15. Notificações

Existente: **Central de Avisos** no dashboard, com escopo por alçada e centros do usuário.

**[V2-P2/P3]** Novos tipos internos: aprovação pendente na minha alçada, SLA estourando/estourado,
certidão vencendo (30/15/0), contrato vencendo (90/60/30) e vencido, saldo contratual < 20%,
entrega atrasada (data prometida vencida), O.C. registrada, pedido entregue, faltante virou SC.
**[V2-P4]** E-mail, Teams/WhatsApp: somente via Notification Center (FD-001-05) — fora do escopo
até lá.

---

## 16. Procurement Insights **[V2-P3]**

Painel de **regras determinísticas com evidência exibida** (módulo `INSIGHTS`):

| Alerta | Regra padrão (parametrizável) |
|---|---|
| Sobrepreço | preço cotado > 15% acima da média histórica do item |
| Compras repetidas / fracionamento | mesmo solicitante + mesmo item/família em janela de 30 dias; com limite de alçada configurado, destaca quando as partes somadas o excedem |
| Emergenciais recorrentes | CC com % de SCs urgentes acima do dobro da média |
| Concentração | fornecedor ≥ 40% do spend da categoria no período |

Cada alerta mostra os números que o geraram e aceita **"tratar/justificar"** (evento com autor).
**[V2-P4 — IA]** sugestão de fornecedor, sugestão de agrupamento, previsão de demanda,
sazonalidade e assistente em linguagem natural: só após histórico e indicadores maduros.

---

## 17. Dashboards

Existente (intocado): **Dashboard de Suprimentos** com filtros (período, fornecedor, comprador,
solicitante, família, CC, regional, gerente, cliente), KPIs, séries mensais, rankings, **ganho de
negociação** e **prazos meta × realizado**, tabela de fornecedores e Central de Avisos.
**Painel de Atendimentos** com cartões clicáveis.

**[V2-P3]** Novos (especificação completa no §9 da análise): **Executivo** (as 4 perguntas),
**Compliance**, **Contratos**, **Backlog**, **Fornecedores**, **Compradores**, **Financeiro**,
**Insights**. Todos com drill-down para a tela operacional correspondente.

---

## 18. Referência de API (v1)

Rotas existentes preservadas integralmente (auth, users, purchase-requisitions + attachments,
approvals, material-requisitions + panel, triage, quotations + suppliers/proposals/negotiation/
close/select-winner/manager-decision/director-decision/register-po/cancel/timeline, portal,
purchase-orders + erp-order/invoices/deliveries/pdf, items + import/image/summary/batch-view,
product-families, product-types, suppliers + contract/portal-key, cost-centers, company/companies,
inventory, analytics, documents).

**[V2] Novas rotas (por prioridade):**

| Prioridade | Rota | Função |
|---|---|---|
| P1 | validação em `POST/PATCH /purchase-requisitions` | urgência justificada (`PR-ERR-050`) |
| P1 | `GET /triage` (+ `agingDays`/faixa) | aging |
| P1 | `GET /analytics/suppliers/{id}` | OTIF consolidado |
| P2 | `POST /quotations` (aceitando itens de SCs) | agrupamento multi-SC |
| P2 | `POST /suppliers/{id}/documents`, `PATCH /suppliers/{id}/homologation` | homologação/certidões |
| P2 | `PUT /suppliers/{id}/contract` (+teto), `GET` com saldo | contratos |
| P2 | `GET /analytics/sla`, `GET /analytics/compliance` | SLA % e compliance |
| P3 | `GET /insights`, `POST /insights/{id}/resolve` | insights |
| P3 | `POST /triage/assign-batch`, `POST /triage/{id}/priority` | lote e prioridade |
| P3 | `POST /purchase-orders/{id}/returns` | devolução no recebimento |
| P3 | `GET/POST /categories` | categorias |

---

## 19. Banco de dados — alterações V2 (todas aditivas)

| Prioridade | Alteração |
|---|---|
| P1 | `purchase_requisition`: `urgency_reason`, `urgency_impact` |
| P1 | `purchase_order`: `promised_date` |
| P2 | `proposal`: `tax_value`, `other_costs` |
| P2 | `purchase_order_item`: `last_paid_unit_price`, `reference_saving` |
| P2 | `supplier`: `homologation_status`, `contract_value_limit` (+P3: `contract_adjustment_index`, `contract_manager_id`, `contract_document_id`) |
| P2 | nova `procurement.supplier_document` (tipo, validade, documento, status) |
| P2 | `quotation_item.source_pr_item_id` (FK); `quotation.source_pr_id` → anulável |
| P3 | nova `materials.product_category` + FK anulável em `product_family` |
| P3 | nova `procurement.purchase_order_return` (item, qtde, motivo) |
| P3 | `cost_center_approver` ou CC: limites de valor opcionais |
| P3 | nova `foundation.system_parameter` (faixas de aging, % sobrepreço, pesos do compliance) |

Nenhuma coluna existente é removida ou ressignificada. Migrations com preservação de dados
(padrão da casa).

---

## 20. Regras de negócio que não podem se perder

1. A cotação nasce **somente de itens de SC aprovados** — ⚠ generalizada de "uma SC aprovada"
   para "itens aprovados de uma ou mais SCs" pelo agrupamento (§8); **essa generalização precisa
   de aprovação antes de ser implementada**; até lá vale a forma atual.
2. Propostas **versionadas e imutáveis**; a escolha exige a versão mais recente.
3. **Segregação de funções** nas duas alçadas.
4. **Qualquer pessoa do nível** do centro resolve a etapa; sem lista, vale o vínculo antigo.
   Limite de valor é adicional e opcional — nunca substitui as listas.
5. O **saving de negociação** é medido contra a 1ª proposta do fornecedor vencedor e é imutável.
   Saving de referência e cost avoidance são métricas **separadas, com nomes próprios,
   congeladas** no registro — nunca se somam ou se confundem com o saving de negociação.
6. A **O.C. é registrada, nunca emitida**; número único no sistema; registro só após as duas
   alçadas.
7. O **faltante do almoxarifado** vira SC no nome do **solicitante original**.
8. **EPI/EPC** só circulam com **C.A. de algum fornecedor** do produto.
9. Solicitante só usa os **centros de custo vinculados** a ele.
10. Toda decisão relevante gera **evento na timeline** com autor, data e justificativa — inclusive
    nos módulos novos (homologação, contrato, prioridade, insight tratado).
11. Importação **não cadastra** linha inválida ou duplicada.
12. Acesso = **papel E módulo**; menu mostra só o permitido.
13. **[V2]** Códigos de erro são **imutáveis**; conceito novo ⇒ código novo.
14. **[V2]** Status de processo é **derivado**, nunca persistido em paralelo.
15. **[V2]** Compliance **mede, não bloqueia**; homologação bloqueia **na seleção do vencedor**,
    nunca no convite; fornecedores atuais nascem homologados.

---

## 21. Critérios de aceite e ritual de entrega

1. Migration EF Core aditiva com preservação estrita de dados para toda mudança de schema.
2. Nenhuma tela sem endpoint funcional; nenhuma funcionalidade fictícia.
3. Erro sempre com código estável e mensagem em português dizendo **o que fazer**.
4. Testes unitários obrigatórios para: cálculo dos savings, OTIF, segregação de alçadas,
   homologação na seleção, saldo contratual, compliance score, validação de C.A. e as regras de
   agrupamento (rastreabilidade por origem).
5. Ritual: build → `dotnet test` → e2e contra PostgreSQL em Docker → conferência visual do SPA →
   commit → PR → merge squash em `main` (deploy automático) → verificação em produção.
6. O agrupamento multi-SC (§8) só entra com **testes de regressão do fluxo de SC única** verdes.
7. Idioma: todo texto de usuário, erro e documentação em **português**.

---

## 22. Ordem de implementação sugerida

**Fase 1 (P1):** urgência justificada → aging na triagem → `promised_date` + OTIF.
**Fase 2 (P2):** saving de referência → impostos/custos na proposta → teto/saldo contratual →
homologação + certidões → SLA por processo → **agrupamento multi-SC** (entrega isolada) →
Compliance Score → módulos novos.
**Fase 3 (P3):** categoria → scorecard A-D → devolução → lote/prioridade na triagem → alertas de
contrato/certidão → dashboards novos → insights → limite de alçada.
**Fase 4 (P4):** cost avoidance, TCO, risco completo, score multicritério, e-mail/Teams, IA.

---

*V2 aprovado em 2026-08-29. Fase 1 (P1) implementada na mesma data: urgência justificada
(PR-ERR-050), aging na Gestão de Solicitações e OTIF (data prometida + consolidação por
fornecedor no dashboard).*
