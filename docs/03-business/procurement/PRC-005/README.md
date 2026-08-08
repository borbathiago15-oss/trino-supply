**Documento:** PRC-005 — Supplier Management (Visão do Módulo)
**Módulo:** PRC-005 — Supplier Management (Gestão de Fornecedores)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** PRC-001 (Documento Mestre da Suíte), FD-001-01/02/03/04/05/06/07/09/10, PR-001 (consumidor), TPES-002, ADR-012
**Referências:** RFQ (PRC-002), Purchase Order (PRC-004), Contract Management (PRC-006), MMS-005 (divergências de recebimento), GOV-001

> Base cadastral da suíte de Compras: quem pode fornecer, com qual homologação, sob qual avaliação e com quais documentos. RFQ, Pedido e Contrato referenciam fornecedores homologados aqui. Não contradiz o PRC-001.

---

# Objetivo
O **Supplier Management** é o cadastro mestre de fornecedores do Trino Supply: identidade única, homologação, avaliação de desempenho e documentação. Nenhum RFQ, pedido ou contrato referencia fornecedor que não exista e esteja **homologado e ativo** aqui.

# Escopo

## O que faz
1. **Cadastro de fornecedor** com identidade única por empresa (razão social, documento fiscal, contatos, categorias de fornecimento).
2. **Homologação**: fluxo de aprovação (FD-001-04) que qualifica o fornecedor para participar de cotações — com documentos obrigatórios e validade.
3. **Avaliação de desempenho**: score alimentado por eventos objetivos (divergências de recebimento — MMS-005; pontualidade de entrega; qualidade), com histórico.
4. **Documentação do fornecedor**: certidões, contratos sociais, certificações (FD-001-03), com vigência e alertas de vencimento.
5. **Categorias de fornecimento**: o que o fornecedor pode fornecer (vocabulário Master Data — FD-001-09), usado para direcionar RFQs.
6. **Ciclo de vida**: Rascunho → Em Homologação → Homologado → Suspenso → Inativo.
7. **Consulta** para RFQ/Pedido/Contrato: apenas fornecedores homologados e ativos, filtráveis por categoria e score.

## O que NÃO faz
- **Não cota nem compra** — isso é RFQ (PRC-002)/Pedido (PRC-004).
- **Não gerencia contratos** — é o Contract Management (PRC-006); aqui só a documentação cadastral.
- **Não recebe material** — recebimento é MMS-005; o desempenho de recebimento **alimenta** a avaliação.
- **Não trata pagamento/financeiro** — fora do MVP.

## Integrações esperadas
| Integração | Direção | Descrição |
|------------|---------|-----------|
| RFQ (PRC-002) | Produz para | Fornecedores homologados por categoria para convite à cotação |
| Purchase Order (PRC-004) | Produz para | Fornecedor do pedido deve estar homologado/ativo |
| Contract Management (PRC-006) | Produz para | Contratos vinculados a fornecedor homologado |
| MMS-005 Receiving | Consome | Divergências de recebimento alimentam o score de desempenho |
| FD-001-03/04/09 | Consome | Documentos, workflow de homologação, categorias (Master Data) |

---

# Objetivos do Negócio
1. Comprar apenas de fornecedores **qualificados** (homologação).
2. Direcionar cotações aos fornecedores certos (categoria + score).
3. Profissionalizar a relação: histórico de desempenho objetivo.
4. Evitar risco (documentação vigente, compliance).

# Quem utiliza
Comprador (consulta/convida), Analista de Suprimentos (homologa/avalia), Gerente (política/score), Administrador (config), Auditor (trilha), Fornecedor (portal — roadmap).

# Objetivos do MVP
1. CRUD de fornecedor com identidade única e categorias.
2. Homologação com documentos obrigatórios e workflow (FD-001-04).
3. Avaliação básica alimentada por divergências de recebimento (MMS-005).
4. Ciclo de vida e consulta de homologados por categoria para RFQ.

---

# Fluxo Macro
```
Cadastro do fornecedor (Rascunho)
      ↓ documentos + categorias
Em Homologação → workflow (FD-001-04)
      ↓
Homologado ──► disponível para RFQ/Pedido/Contrato
      │
      ├─ avaliação contínua (score) ← eventos de recebimento (MMS-005)
      │
      ├─ Suspenso (documento vencido / desempenho / decisão) → bloqueia novos convites
      └─ Inativo (encerramento) — histórico preservado
```

# Regras Gerais (herdadas do PRC-001 + específicas)
- PRC-RG-02 (RFQ só com homologado/ativo) — este módulo é a fonte da homologação.
- Identidade única por empresa (documento fiscal); soft delete; nunca exclusão física após referência.
- Homologação exige documentos obrigatórios vigentes; vencimento suspende automaticamente (alerta prévio via FD-001-05).
- SoD: quem cadastra não homologa (parametrizável); toda decisão auditada.
- Score é derivado de eventos objetivos, não editável diretamente (analogia ao saldo do MMS-004: projeção).

# Eventos Publicados (funcionais)
Fornecedor cadastrado / enviado à homologação / homologado / suspenso / reativado / inativado; documento anexado/vencido; avaliação atualizada.

# Eventos Consumidos
Recebimento com divergência (MMS-005) → atualiza score; documento vencido (FD-001-03) → suspensão.

# Requisitos Não Funcionais
Multiempresa; escopo; homologação auditada; consulta performática (keyset); score reconstruível a partir dos eventos; LGPD para dados de contato.

# Restrições Arquiteturais
Não cota/compra/contrata; consome Foundation; parametrização acima de customização; máquina de estados única; score como projeção (não editável).

# KPIs
% de compras com fornecedor homologado; nº de fornecedores ativos por categoria; score médio; documentos a vencer; tempo de homologação.

# Roadmap
- **v1 (MVP):** cadastro, homologação, categorias, avaliação básica (via recebimento), ciclo de vida, consulta para RFQ.
- **v1.1:** portal do fornecedor (autoatualização de documentos), scorecard detalhado, blacklist/compliance.
- **v2.0:** integração fiscal (validação de documento), due diligence automatizada.

# Critérios de Conclusão (DoD)
Visão aprovada; pacote de 17 artefatos documentado no padrão da suíte; homologação operável com workflow; score alimentado por MMS-005; consulta de homologados disponível para RFQ.

# Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Visão do módulo Supplier Management: cadastro mestre de fornecedores, homologação com documentos/workflow, avaliação de desempenho (score como projeção alimentada por MMS-005), categorias (Master Data), ciclo de vida, consulta para RFQ, fronteiras com RFQ/Pedido/Contrato/Receiving, NFRs, KPIs, roadmap e DoD — conforme PRC-001 e padrão MMS-002. |
