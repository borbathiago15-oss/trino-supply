**Documento:** MMS-002-01 — Business Context
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 (Visão do Módulo), MMS-001 (Documento Mestre Funcional — seções 6, 8.1, 10, 11, 14, 20, 23, 25), ADR-012, FD-001-09 (Master Data)
**Referências:** MMS-003, MMS-004, MMS-005, PR-001, FD-001-01, FD-001-02, FD-001-10, GOV-001

---

# 1. Objetivo

Este documento descreve o **contexto de negócio** do módulo Item Catalog: por que ele existe, qual problema resolve, quem participa, o que o dispara e como o sucesso é medido. É a porta de entrada do pacote funcional do MMS-002 e a referência de propósito para todos os demais documentos do módulo (regras, estados, casos de uso, permissões, eventos, API, UX e testes).

Nada neste documento cria funcionalidade: todo o conteúdo deriva da visão aprovada do módulo (MMS-002 README) e do Documento Mestre Funcional da suíte (MMS-001).

---

# 2. Contexto de Negócio

Toda operação de suprimentos começa por uma pergunta simples: **"que material é esse?"** Quando a resposta depende de planilhas, memória de operador ou cadastros paralelos, toda a cadeia seguinte herda o erro — solicitações com descrições livres, estoque fragmentado por nomes diferentes do mesmo item, compras duplicadas e indicadores que não fecham.

A Materials Management Suite (MMS-001) foi desenhada para operar sobre uma **fonte única e oficial de itens**: o Item Catalog. Ele é o primeiro módulo do roadmap da suíte (MMS-001, seção 24) porque nenhum outro módulo funciona sem ele — Material Requisition solicita itens do catálogo, Inventory movimenta saldos de itens do catálogo, Receiving confere itens do catálogo e a rota de compra (PR-001) referencia os mesmos itens.

O Item Catalog é, portanto, a **fundação cadastral** da suíte de materiais do Trino Supply.

---

# 3. Problema de Negócio

| Problema observado | Consequência |
|--------------------|--------------|
| Mesmo material cadastrado várias vezes com nomes diferentes ("parafuso sextavado 1/4", "PARAF. SEXT. 1/4") | Estoque fragmentado, compra do que já existe, inventário impossível de reconciliar |
| Unidade de medida livre ("CX", "cx", "caixa", "CXA") | Quantidades incomparáveis, erros de separação e de compra |
| Pedidos de material inexistente ou descontinuado | Retrabalho, atraso no atendimento, negociação caso a caso |
| Ausência de parâmetros de reposição (mín/máx/ponto de pedido) | Estoque reativo: ruptura e excesso convivendo |
| Alterações cadastrais sem rastro | Impossível auditar quem mudou o quê e quando |

O custo do problema aparece nos KPIs da suíte (MMS-001, seção 20): sem cadastro confiável, **acuracidade de estoque**, **taxa de atendimento pelo estoque** e **ruptura** não têm como atingir as metas.

---

# 4. Objetivos do Processo

1. Garantir **um item, um código, uma descrição oficial** por empresa (sinônimos apenas como auxílio de busca).
2. Manter o **ciclo de vida** do item sob controle: Rascunho → Ativo → Inativo, com regras de uso por estado.
3. Registrar **classificação** (estocável / não estocável / sob encomenda) e **criticidade** para orientar workflow e roteamento.
4. Manter **parâmetros de reposição** (mínimo, máximo, ponto de pedido, lead time) que viabilizam alertas e gestão do estoque.
5. Oferecer **consulta pública e performática** do catálogo a todos os módulos e usuários autorizados.

---

# 5. Objetivos Estratégicos

| Objetivo estratégico | Contribuição do módulo |
|----------------------|------------------------|
| Estoque primeiro, compra só do que falta | Catálogo único permite validar disponibilidade real antes de comprar |
| Acuracidade de estoque ≥ 98% | Elimina fragmentação por duplicidade — pré-condição da contagem confiável |
| Rastreabilidade ponta a ponta | A mesma identidade de item atravessa solicitação, estoque, compra e recebimento |
| Parametrização acima de customização | Vocabulários e comportamentos variáveis via Master Data (FD-001-09) e Configuration (FD-001-10) |
| Segurança e auditoria por padrão | 100% das alterações cadastrais auditadas (MMS-P-02) |

---

# 6. Quando um Item Deve Ser Cadastrado

| Situação | Ação esperada |
|----------|---------------|
| Material recorrente consumido pela operação e ainda não catalogado | Gerente de Suprimentos cadastra, valida e ativa |
| Item identificado como duplicado em potencial (alerta de descrição semelhante) | Revisão antes de confirmar o cadastro |
| Material que passa a exigir controle de reposição | Cadastro/atualização dos parâmetros mín/máx/ponto de pedido/lead time |
| Item descontinuado | Inativação — nunca exclusão (saldo remanescente segue movimentável, MMS-RG-08) |
| Item comprado sob encomenda ou de consumo direto (não estocável) | Cadastro com a classificação correspondente, sem parâmetros de estoque obrigatórios |

---

# 7. Fora do Escopo

Conforme a fronteira formal da visão do módulo (MMS-002 README):

- **Fornecedores e preços** — domínio Procurement (Supplier Management / Purchase Order);
- **Saldo de estoque** — domínio do MMS-004 Inventory Management (o catálogo guarda parâmetros, nunca saldos);
- **Vocabulários de unidade de medida e categoria** — pertencem ao Master Data (FD-001-09); o catálogo apenas referencia por `typeCode+code`;
- **Estrutura organizacional** (empresa/unidade/centro de custo) — FD-001-02;
- **Valorização fiscal/contábil de estoque** — fora da suíte no MVP (MMS-001, seção 8.3);
- **Lote, validade e série** — versão 2.0 (MMS-002 README, roadmap).

---

# 8. Contexto Organizacional

- O catálogo é **multiempresa**: cada empresa possui seu próprio catálogo, com isolamento total (código único por empresa).
- A **manutenção** do catálogo é responsabilidade do papel **Gerente de Suprimentos** (criação, edição, ativação, inativação, parâmetros); a ativação não exige workflow no MVP, mas a exigência de aprovação cadastral é parâmetro (FD-001-10) para clientes que a demandarem.
- O **consumo** é amplo e transversal: solicitantes (MMS-003), almoxarifado (MMS-004/MMS-005) e Compras (PR-001) consultam o mesmo catálogo, sempre somente itens **Ativos** para novas operações.
- Unidades de medida e categorias seguem a governança do Master Data (FD-001-09): vigência avaliada na data de referência, sem efeito retroativo.

---

# 9. Stakeholders

| Stakeholder | Interesse | Influência |
|-------------|-----------|------------|
| Gerente de Suprimentos | Catálogo saneado, sem duplicidade, parâmetros completos | Mantenedor do módulo |
| Almoxarifado (almoxarife e supervisor) | Identificação inequívoca do item na operação | Consumidor intenso |
| Solicitantes | Encontrar o item certo com facilidade | Consumidor via MMS-003 |
| Compras | Referência única na rota de compra | Consumidor via PR-001 |
| Gestão (Management Workspace) | Indicadores confiáveis de estoque e atendimento | Beneficiária dos KPIs |
| Auditoria | Trilha completa de alterações | Verificadora |
| Administração da plataforma | Parâmetros e exceções | Configuradora |

---

# 10. Personas

Conforme as personas da suíte (MMS-001, seção 11), recortadas para este módulo:

## Persona 1 — Solicitante Operacional

Busca o item pelo nome que usa no dia a dia (sinônimo), encontra o registro oficial e solicita com a unidade de medida correta — sem precisar conhecer o código.

## Persona 2 — Almoxarife

Consulta o catálogo na movimentação e no recebimento; sugere novos itens e correções ao Gerente de Suprimentos quando identifica material não catalogado.

## Persona 3 — Gerente de Suprimentos

Mantém o catálogo: cadastra, classifica, parametriza reposição, trata alertas de duplicidade, ativa e inativa itens — tudo com motivo registrado.

## Persona 4 — Comprador

Referencia o item do catálogo na rota de compra, garantindo que o material comprado é exatamente o solicitado.

## Persona 5 — Auditor

Consulta o catálogo e a trilha de alterações para verificar conformidade cadastral.

---

# 11. Premissas

1. O Foundation (FD-001) está operacional: identidade, organização, master data, configuração, auditoria e timeline disponíveis como serviço.
2. Unidades de medida e categorias existem e são governadas no Master Data (FD-001-09) antes do cadastro de itens que as referenciam.
3. A operação aceita que somente itens **Ativos** entram em novas solicitações (MMS-RG-08).
4. A empresa define seus próprios códigos de item dentro da máscara configurada; o sistema garante unicidade por empresa.
5. Não há migração de legado no MVP; carga inicial ocorre por cadastro manual (importação em lote é v1.1).

---

# 12. Restrições

1. Nenhum vocabulário recriado: unidade de medida e categoria são referências lógicas (`typeCode+code`), nunca cópias de rótulo.
2. Nenhum saldo, fornecedor ou preço armazenado no módulo — referências cruzadas por identidade.
3. Inativação sempre lógica; nenhum item é excluído fisicamente após a primeira referência de outro módulo.
4. Nenhum comportamento variável em código: aprovação cadastral, campos obrigatórios adicionais e tolerâncias são parâmetros (FD-001-10).
5. Nada neste módulo pode contradizer o MMS-001 nem os princípios MMS-P-01..08.

---

# 13. Gatilhos de Negócio

## Operacionais

- Solicitante não encontra item na busca do catálogo → demanda de novo cadastro ao Gerente de Suprimentos;
- Almoxarife identifica material físico sem registro → sugestão de cadastro;
- Alerta de descrição semelhante durante cadastro → revisão de duplicidade.

## Administrativos

- Saneamento periódico: itens sem parâmetros de reposição, itens sem movimento, duplicidades confirmadas;
- Mudança de vocabulário no Master Data (unidade/categoria inativada) → avaliação de impacto nos itens que a referenciam.

## Estratégicos

- Meta de acuracidade ≥ 98% exige saneamento cadastral contínuo;
- Expansão para nova unidade/almoxarifado → revisão de parâmetros por item.

## Emergenciais

- Item crítico inativado por engano → reativação auditada com motivo;
- Duplicidade confirmada afetando saldo → tratativa com o MMS-004 (fusão assistida é funcionalidade futura; no MVP, correção via movimentação documentada).

---

# 14. Resultado Esperado

- Catálogo único, íntegro e consultável por todos os módulos;
- Itens classificados (estocável/não estocável/sob encomenda) com criticidade definida;
- Parâmetros de reposição preenchidos para itens estocáveis;
- Duplicidade cadastral tendendo a zero, com alerta preventivo no cadastro;
- Trilha de auditoria completa de todas as alterações.

---

# 15. Participantes do Processo

| Papel | Participação |
|-------|--------------|
| Gerente de Suprimentos | Mantenedor: cria, edita, ativa, inativa, parametriza |
| Almoxarife / Supervisor | Consumidor e propositor de novos itens/correções |
| Solicitante | Consumidor via busca do catálogo |
| Comprador | Consumidor na rota de compra |
| Administrador | Configura parâmetros do módulo (`materials.item.*`) |
| Auditor | Verificador somente leitura |

---

# 16. Entradas do Processo

| Entrada | Origem | Observação |
|---------|--------|------------|
| Dados do item (código, descrição, descrição detalhada, características) | Gerente de Suprimentos | Código único por empresa |
| Unidade de medida e categoria | FD-001-09 Master Data | Referência `typeCode+code`, vigência na data de referência |
| Classificação e criticidade | Gerente de Suprimentos | Estocável / não estocável / sob encomenda; baixa/média/alta (parametrizável) |
| Parâmetros de reposição | Gerente de Suprimentos | Mínimo, máximo, ponto de pedido, lead time |
| Sinônimos de busca | Operação / Gerente | Auxílio de busca, nunca descrição oficial alternativa |
| Motivo de alterações sensíveis | Responsável pela alteração | Obrigatório em inativação e mudança de unidade |

---

# 17. Saídas do Processo

| Saída | Destino |
|-------|---------|
| Catálogo consultável (código, descrição, classificação, parâmetros) | MMS-003, MMS-004, MMS-005, PR-001 e usuários autorizados |
| Eventos do ciclo de vida do item | Barramento (consumidores da suíte) |
| Alerta de descrição semelhante | Gerente de Suprimentos (prevenção de duplicidade) |
| Registros de auditoria e timeline | FD-001-06 / FD-001-07 |
| Parâmetros de reposição | MMS-004 (alertas de mínimo/ruptura) |

---

# 18. Indicadores de Negócio

- Cobertura cadastral: percentual de materiais recorrentes catalogados;
- Saneamento: itens sem parâmetro de reposição; itens sem movimento há N dias;
- Qualidade: taxa de duplicidade detectada (alertas confirmados ÷ cadastros);
- Eficiência: tempo médio de cadastro e de ativação;
- Uso: buscas sem resultado (demanda reprimida de cadastro).

---

# 19. KPIs

| KPI | Cálculo | Meta/observação |
|-----|---------|-----------------|
| Taxa de duplicidade detectada | alertas de descrição semelhante confirmados ÷ cadastros no período | Tendência a zero |
| Itens sem parâmetro de reposição | itens estocáveis ativos sem mín/máx/ponto de pedido | Saneamento contínuo |
| Itens ativos por categoria | contagem por categoria (FD-001-09) | Visão de cobertura |
| Itens sem movimento há N dias | itens ativos sem movimentação no período (com MMS-004) | Insumo para obsolescência (MMS-001 §20) |
| Tempo médio de cadastro/ativação | criação → ativação | SLA interno do mantenedor |
| Buscas sem resultado | buscas do catálogo sem correspondência | Mede demanda de novos cadastros |

---

# 20. Riscos de Negócio

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Duplicidade apesar do alerta (cadastro forçado) | Média | Alto | Alerta de descrição semelhante + saneamento periódico + KPI dedicado |
| Inativação indevida de item em uso | Baixa | Alto | MMS-RG-08: saldo remanescente segue movimentável; reativação auditada |
| Descuido dos parâmetros de reposição | Alta | Médio | KPI de saneamento + relatório de itens sem parâmetro |
| Vocabulário de Master Data inativado com itens referenciando | Baixa | Médio | Vigência por data de referência (sem retroatividade) + avaliação de impacto |
| Catálogo tratado como "mais um cadastro" e abandonado | Média | Alto | Único caminho de solicitação (MMS-003 só aceita itens Ativos) — o uso força a manutenção |
| Carga inicial pobre (cadastro apressado) | Média | Médio | Campos obrigatórios + validações + importação em lote com validação na v1.1 |

---

# 21. Glossário

| Termo | Definição |
|-------|-----------|
| **Item** | Unidade de material cadastrada no Item Catalog; estocável, não estocável ou sob encomenda (MMS-001 §25) |
| **Código do item** | Identificador único do item por empresa, na máscara configurada |
| **Descrição oficial** | Nome único e formal do item; sinônimos não a substituem |
| **Sinônimo** | Nome alternativo de busca usado pela operação; auxilia a encontrar o item oficial |
| **Classificação** | Estocável (vai para o estoque), não estocável (compra/consumo direto) ou sob encomenda |
| **Criticidade** | Grau de importância operacional do item (baixa/média/alta, parametrizável); orienta workflow e priorização |
| **Parâmetros de reposição** | Estoque mínimo, estoque máximo, ponto de pedido e lead time de referência do item |
| **Alerta de descrição semelhante** | Aviso no cadastro quando já existe item com descrição parecida — prevenção de duplicidade |
| **Rascunho / Ativo / Inativo** | Estados do ciclo de vida do item; somente Ativos entram em novas solicitações |
| **typeCode+code** | Forma de referência lógica a vocabulários do Master Data (FD-001-09), sem cópia de rótulo |

---

# 22. Princípios do Processo

1. **Um item, um código, uma descrição oficial** — a unicidade cadastral é inegociável.
2. **Catálogo é referência, não dono de tudo** — saldo, fornecedor, preço e vocabulários têm donos próprios; o catálogo referencia por identidade.
3. **Somente Ativo é solicitável** (MMS-RG-08) — descontinuação controlada, nunca abrupta.
4. **Toda alteração é auditada** (MMS-P-02) e aparece na timeline (MMS-P-03).
5. **Parametrização acima de customização** (MMS-P-06) — comportamentos variáveis são configuração.
6. **Documentação como fonte da verdade** — conflito entre código e documentação resolve-se pela documentação.

---

# 23. Dependências

| Dependência | Tipo | Uso |
|-------------|------|-----|
| MMS-001 (Documento Mestre) | Obrigatória | Regras, princípios, estados e KPIs herdados |
| FD-001-01 Identity & Access | Obrigatória | Autenticação e permissões por papel |
| FD-001-02 Organization | Obrigatória | Escopo empresa/unidade |
| FD-001-04 Workflow | Condicional | Aprovação cadastral quando parametrizada |
| FD-001-06 Audit | Obrigatória | Trilha de todas as alterações |
| FD-001-07 Timeline | Obrigatória | Marcos do ciclo de vida |
| FD-001-09 Master Data | Obrigatória | Unidades de medida e categorias (fronteira formal) |
| FD-001-10 Configuration | Obrigatória | Parâmetros `materials.item.*` |
| MMS-003 / MMS-004 / MMS-005 / PR-001 | Consumidores | Usam o catálogo como referência única de itens |

---

# 24. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação do Business Context do Item Catalog: contexto, problema, objetivos, stakeholders, personas, premissas, restrições, gatilhos, entradas/saídas, indicadores, KPIs, riscos, glossário, princípios e dependências — derivado da visão aprovada do módulo (MMS-002 README) e do Documento Mestre (MMS-001), no padrão do pacote PR-001-01 |
