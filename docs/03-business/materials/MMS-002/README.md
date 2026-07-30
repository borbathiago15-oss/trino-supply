**Documento:** MMS-002 — Item Catalog (Visão do Módulo)
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-001 (Documento Mestre Funcional — seções 7, 8.1, 14, 24), ADR-012, FD-001-09 (Master Data), FD-001-01, FD-001-02, FD-001-04, FD-001-06, FD-001-07, FD-001-10
**Referências:** PR-001 (consumidor de itens na rota de compra), GOV-001

---

# Objetivo

O **Item Catalog** é o cadastro mestre de itens do Trino Supply: a fonte única e oficial de tudo aquilo que pode ser solicitado, estocado, movimentado ou comprado. Nenhum módulo (Material Requisition, Inventory, Receiving, Purchase Requisition) referencia um item que não exista e esteja vigente neste catálogo.

O módulo existe para eliminar o cadastro duplicado e informal de materiais — o mesmo parafuso cadastrado três vezes com nomes diferentes — que destrói a acuracidade do estoque e infla compras desnecessárias.

---

# Escopo

## O que faz

1. Cadastro de itens com identidade única por empresa: código, descrição, descrição detalhada, unidade de medida, categoria e características.
2. Classificação do item: **estocável**, **não estocável** (compra/consumo direto) ou **sob encomenda**.
3. Parâmetros de reposição por item (e, opcionalmente, por depósito): estoque mínimo, estoque máximo, ponto de pedido e lead time de referência.
4. Criticidade do item (parametrizável: baixa/média/alta) para workflow de aprovação e priorização.
5. Ciclo de vida: Rascunho → Ativo → Inativo, com regras de uso por estado.
6. Consulta pública do catálogo para todos os módulos e usuários autorizados, com busca por código, descrição e categoria.
7. Equivalências e sinônimos de busca (nomes alternativos usados pela operação) para reduzir duplicidade.

## O que NÃO faz

- **Não cadastra fornecedores nem preços** — pertencem ao domínio Procurement (Supplier Management / Purchase Order).
- **Não controla saldo** — saldo é do MMS-004 Inventory Management; o catálogo guarda apenas parâmetros (mín/máx/ponto de pedido).
- **Não recria vocabulários** — unidade de medida e categoria vêm do Master Data (FD-001-09); o catálogo referencia por `typeCode+code`, nunca por rótulo.
- **Não gerencia estrutura organizacional** — empresa/unidade/centro de custo vêm de FD-001-02.
- **Não trata valorização fiscal/contábil** — fora de escopo (MMS-001, seção 8.3).

## Integrações esperadas

| Integração | Direção | Descrição |
|------------|---------|-----------|
| MMS-003 Material Requisition | Consumido por | Itens solicitados devem existir e estar Ativos |
| MMS-004 Inventory Management | Consumido por | Saldos e movimentações referenciam itens do catálogo; parâmetros de reposição alimentam alertas |
| MMS-005 Receiving | Consumido por | Conferência contra itens do documento de origem |
| PR-001 (Compras) | Consumido por | Rota de compra referencia o mesmo item do catálogo — rastreabilidade ponta a ponta |
| FD-001-09 Master Data | Consome | Unidades de medida e categorias (vigência avaliada na data de referência) |

---

# Objetivos do Negócio

1. **Eliminar duplicidade cadastral** — um item, um código, uma descrição oficial (sinônimos apenas como auxílio de busca).
2. **Dar identidade única ao material** em toda a cadeia: solicitação → estoque → compra → recebimento.
3. **Viabilizar reposição e alertas** — sem parâmetros de mín/máx/ponto de pedido por item, o estoque não é gerenciável.
4. **Melhorar indicadores:** acuracidade de estoque, taxa de atendimento pelo estoque e ruptura (KPIs do MMS-001, seção 20) dependem de cadastro confiável.

## Quem utiliza

- Operação (solicitantes) — consulta e busca ao pedir material;
- Almoxarifado — consulta na movimentação e no recebimento;
- Gerente de Suprimentos — manutenção do catálogo e dos parâmetros;
- Compras — referência na rota de compra.

---

# Objetivos do MVP

1. CRUD completo de itens com ciclo de vida (Rascunho/Ativo/Inativo).
2. Classificação estocável/não estocável/sob encomenda e criticidade.
3. Parâmetros de reposição por item (mínimo, máximo, ponto de pedido, lead time).
4. Busca por código, descrição, sinônimo e categoria.
5. Validação de vigência de unidade de medida e categoria via Master Data.
6. Auditoria e timeline em 100% das alterações (MMS-P-02/03).

---

# Problemas que Resolve

| Problema | Solução do módulo |
|----------|-------------------|
| Item duplicado com nomes diferentes | Código único por empresa + sinônimos de busca + alerta de descrição semelhante no cadastro |
| Pedido de item inexistente/descontinuado | Apenas itens Ativos entram em novas solicitações (MMS-RG-08) |
| Estoque sem parâmetros de reposição | Mín/máx/ponto de pedido/lead time por item |
| Unidade de medida livre ("CX", "cx", "caixa") | Referência fechada ao Master Data (FD-001-09) |
| Falta de rastreabilidade cadastral | Auditoria + timeline em toda alteração |

---

# Atores

| Ator | Papel no módulo |
|------|-----------------|
| **Solicitante** | Consulta o catálogo ao criar solicitações de material |
| **Almoxarife / Supervisor** | Consulta na operação; sugere novos itens e correções |
| **Gerente de Suprimentos** | Mantém o catálogo: cria, edita, ativa, inativa, define parâmetros |
| **Comprador** | Consulta na rota de compra |
| **Administrador** | Configurações e exceções |
| **Auditor** | Consulta e trilha |

---

# Fluxo Macro

```
Cadastro do item (Gerente de Suprimentos)
        │
        ▼
Validações (unicidade de código, unidade e categoria vigentes, campos obrigatórios)
        │
        ▼
Item em Rascunho ──► Ativação ──► Item Ativo
        │                            │
        │                            ├─► Usado por MMS-003, MMS-004, MMS-005, PR-001
        │                            │
        │                            ▼
        │                      Inativação ──► Item Inativo
        │                            (não entra em novas solicitações;
        │                             saldo remanescente segue movimentável)
        ▼
   Descarte do rascunho (opcional, auditado)
```

A ativação de item **não exige workflow** no MVP (responsabilidade do papel Gerente de Suprimentos, auditada); a exigência de aprovação cadastral é parâmetro (FD-001-10) para clientes que a demandarem.

---

# Entradas

- Dados do item: código, descrição, descrição detalhada, unidade de medida (FD-001-09), categoria (FD-001-09), características, classificação, criticidade;
- Parâmetros de reposição: mínimo, máximo, ponto de pedido, lead time;
- Sinônimos de busca;
- Motivo de alterações sensíveis (inativação, mudança de unidade) — trilha de auditoria.

# Saídas

- Catálogo consultável (código + descrição + classificação + parâmetros);
- Eventos de negócio do ciclo de vida do item;
- Alerta de descrição semelhante (prevenção de duplicidade);
- Base de referência para saldos (MMS-004), solicitações (MMS-003) e compras (PR-001).

---

# Dependências do Foundation

| Serviço | Uso |
|---------|-----|
| FD-001-01 IAM | Autenticação e permissões do módulo |
| FD-001-02 Organization | Escopo empresa/unidade do item |
| FD-001-04 Workflow Engine | Aprovação cadastral quando parametrizada |
| FD-001-06 Audit Service | Trilha de todas as alterações |
| FD-001-07 Timeline Service | Marcos do ciclo de vida do item |
| FD-001-09 Master Data | Unidades de medida e categorias (fronteira formal: vocabulário ≠ cadastro) |
| FD-001-10 Configuration | Parâmetros do módulo (`materials.item.*`) |

---

# Eventos Publicados (funcionais)

1. Item cadastrado (rascunho); 2. Item ativado; 3. Item alterado; 4. Item inativado; 5. Parâmetros de reposição alterados; 6. Sinônimo incluído/removido. (Especificação técnica seguirá ADR-010 na documentação de eventos do módulo.)

# Eventos Consumidos (funcionais)

- Alteração/inativação de unidade de medida ou categoria no Master Data (FD-001-09) — para avaliação de impacto em itens que as referenciam (vigência por data de referência, sem alteração retroativa).

---

# Requisitos Não Funcionais

| Categoria | Requisito |
|-----------|-----------|
| Multiempresa | Código do item único por empresa; isolamento total entre empresas |
| Performance | Busca de catálogo com paginação keyset; resultado < 2s |
| Auditoria | 100% das alterações auditadas com valores anterior/posterior |
| Concorrência | Optimistic concurrency via version |
| Disponibilidade | Consulta ao catálogo nunca bloqueia operação de estoque (cache de leitura) |
| Integridade referencial | Unidade de medida e categoria sempre vigentes na data de referência |
| Internacionalização | Descrição oficial em pt-BR; preparado para descrições localizadas (futuro) |

---

# Restrições Arquiteturais

1. O módulo não armazena saldo, fornecedor ou preço — referências cruzadas são por identidade, nunca por cópia de dados.
2. Unidade de medida e categoria são referências lógicas ao Master Data (`typeCode+code`), seguindo MD-BR do FD-001-09.
3. Nenhum comportamento variável em código: exigência de aprovação cadastral, campos obrigatórios adicionais e tolerâncias são parâmetros (FD-001-10).
4. Inativação é sempre lógica; nenhum item é excluído fisicamente após a primeira referência por outro módulo.
5. O módulo segue MMS-001 (nada aqui pode contradizê-lo) e os princípios MMS-P-01..08.

---

# Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Direção | Descrição |
|--------|------|---------|-----------|
| Foundation | Obrigatória | Consome | Serviços da tabela acima |
| MMS-003 Material Requisition | Obrigatória | Produz para | Itens solicitáveis (Ativos) |
| MMS-004 Inventory | Obrigatória | Produz para | Identidade e parâmetros de reposição |
| MMS-005 Receiving | Obrigatória | Produz para | Itens conferíveis |
| PR-001 (Compras) | Obrigatória | Produz para | Referência de item na rota de compra |
| Analytics | Opcional | Produz para | Dimensão de item/categoria |

---

# KPIs

- Itens ativos por categoria e por depósito atendido;
- Taxa de duplicidade detectada (alertas de descrição semelhante confirmados);
- Itens sem parâmetro de reposição (saneamento cadastral);
- Itens sem movimento há N dias (insumo para obsolescência — KPI do MMS-001);
- Tempo médio de cadastro/ativação de item.

---

# Critérios de Qualidade

- Todas as regras de negócio do módulo documentadas e codificadas;
- Ciclo de vida (state machine) definido;
- Permissões por papel definidas (padrão PR-001-09);
- Eventos funcionais mapeados;
- Rastreabilidade com MMS-001 verificada (nenhuma contradição).

---

# Critérios de Conclusão do Módulo (DoD)

1. Este documento aprovado no registry.
2. Documentos funcionais do módulo (regras, estados, permissões, eventos) aprovados.
3. Matriz de rastreabilidade regra × funcionalidade × evento preenchida.
4. Fronteira com FD-001-09 validada (nenhum vocabulário recriado).

---

# Roadmap

## Versão 1 (MVP)

- CRUD de itens, ciclo de vida, classificação, criticidade, parâmetros de reposição, sinônimos, busca, alerta de duplicidade.

## Versão 1.1

- Parâmetros de reposição por depósito; importação em lote com validação; curva ABC calculada sobre consumo (com MMS-004).

## Versão 2.0

- Lote/validade/série por item (com MMS-004 v2.0); imagens e fichas técnicas (FD-001-03); equivalentes/substitutos formais; descrições localizadas.

---

# Funcionalidades Futuras

- Sugestão de cadastro a partir de solicitações recorrentes de itens não catalogados;
- Portal de solicitação de novo item pelo solicitante, com triagem do Gerente de Suprimentos;
- Deduplicação assistida (fusão de itens com remapeamento de referências).

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
| 1.0.0 | 2026-07-30 | Criação da visão do módulo Item Catalog: objetivo, escopo, classificação, ciclo de vida, parâmetros de reposição, integrações, eventos, NFRs, KPIs, DoD e roadmap — conforme MMS-001 (seção 8.1) e ADR-012 |
