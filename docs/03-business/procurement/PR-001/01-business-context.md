# PR-001-01 — Business Context

> Este documento descreve o contexto de negócio que justifica a existência do módulo de Solicitação de Compra (Purchase Requisition).

**Versão:** 1.1.0
**Status:** 🟢 Approved

---

# 1. Objetivo

Definir o contexto operacional da Solicitação de Compra dentro do processo de suprimentos da organização, identificando os problemas de negócio que o módulo resolve, os atores envolvidos, os gatilhos que iniciam o processo e os resultados esperados.

Este documento não descreve funcionalidades do sistema. Seu objetivo é explicar o processo de negócio que será suportado pelo Trino Supply.

---

# 2. Contexto de Negócio

Toda organização necessita adquirir materiais, equipamentos ou serviços para manter suas operações.

Essas necessidades surgem diariamente em diferentes áreas, como:

- Manutenção
- Produção
- Logística
- Recursos Humanos
- Tecnologia da Informação
- Facilities
- Engenharia
- Administrativo
- Comercial

Independentemente da área, todas as demandas possuem uma característica comum:

> Existe uma necessidade operacional que precisa ser formalizada antes de qualquer aquisição.

Essa formalização ocorre por meio da Solicitação de Compra.

---

# 3. Problema de Negócio

Em muitas empresas, as necessidades de compra ainda são registradas por:

- E-mails
- Mensagens em aplicativos
- Telefonemas
- Planilhas
- Conversas informais
- Solicitações verbais

Esse modelo gera diversos problemas:

- Falta de rastreabilidade
- Compras sem autorização
- Informações incompletas
- Retrabalho
- Compras duplicadas
- Dificuldade de auditoria
- Falta de priorização
- Perda de histórico
- Baixa previsibilidade

Como consequência, aumentam os custos operacionais e diminui a capacidade de controle da organização.

---

# 4. Objetivos do Processo

O processo de Solicitação de Compra tem como objetivos:

- Formalizar toda necessidade de aquisição.
- Garantir informações mínimas obrigatórias.
- Direcionar automaticamente o fluxo de aprovação.
- Registrar evidências para auditoria.
- Disponibilizar uma demanda estruturada para a área de Compras.
- Criar histórico completo do processo.

---

# 5. Objetivos Estratégicos

Alinhamento do módulo com a estratégia da organização:

| Objetivo Estratégico | Como o módulo contribui |
|----------------------|------------------------|
| Governança de gastos | Nenhuma compra inicia sem solicitação formal e aprovação registrada |
| Redução de custos operacionais | Elimina retrabalho, compras duplicadas e urgências evitáveis |
| Conformidade e auditabilidade | Trilha completa: quem pediu, quem aprovou, quando e por quê |
| Previsibilidade de demanda | Base de dados estruturada por centro de custo, projeto e categoria |
| Escalabilidade da operação | Processo padronizado independente de pessoas ou áreas |
| Transformação digital | Substitui e-mails e planilhas por plataforma única (Single Source of Truth) |

---

# 6. Quando uma Solicitação Deve Ser Criada

Uma Solicitação de Compra deverá ser criada sempre que houver necessidade de:

- Comprar materiais.
- Contratar serviços.
- Repor estoque (quando aplicável).
- Adquirir ativos.
- Realizar compras emergenciais.
- Atender projetos específicos.
- Atender demandas administrativas.

Não deverá ser utilizada para:

- Movimentações internas de estoque.
- Transferências entre unidades.
- Baixas de estoque.
- Processos financeiros.
- Processos fiscais.

---

# 7. Fora do Escopo

Explicitamente fora do escopo deste processo de negócio:

- Cotação e negociação com fornecedores (módulo RFQ).
- Equalização de propostas (módulo de Equalização).
- Emissão de Pedido de Compra (módulo Purchase Order).
- Recebimento físico e aceite (módulo Receiving).
- Gestão de estoque (módulo futuro de Inventory).
- Pagamentos e processos financeiros/fiscais.
- Cadastro e homologação de fornecedores (Supplier Management).

---

# 8. Contexto Organizacional

O processo opera sobre a estrutura organizacional oficial (FD-001-02):

- **Empresa:** toda solicitação pertence a exatamente uma empresa (PR-BR-002).
- **Unidade:** origem operacional da demanda; obrigatoriedade configurável (PR-BR-003).
- **Centro de custo:** imputação orçamentária, por solicitação ou por item (PR-BR-014).
- **Projeto:** vínculo com investimentos; obrigatoriedade configurável (PR-BR-015).
- **Multiempresa:** usuários enxergam somente empresas autorizadas (PR-BR-071).

O módulo atende desde organizações de estrutura simples (uma empresa, uma unidade) até grupos multiempresa com múltiplas unidades e centros de custo — sem alteração de código, apenas configuração.

---

# 9. Stakeholders

| Stakeholder | Interesse | Influência |
|-------------|-----------|------------|
| Diretoria / CFO | Controle de gastos e conformidade | Alta |
| Gerência de Suprimentos | Processo padronizado e indicadores | Alta |
| Gestores aprovadores | Decisões rápidas com informação completa | Alta |
| Solicitantes (todas as áreas) | Processo simples e previsível | Média |
| Equipe de Compras | Demanda estruturada e completa | Alta |
| Auditoria interna | Rastreabilidade total | Alta |
| TI / Segurança | Conformidade LGPD e OWASP | Média |
| Controladoria | Imputação correta de custos | Média |

---

# 10. Personas

## Persona 1 — Solicitante Operacional

Técnico de manutenção ou analista administrativo. Conhece a necessidade, não conhece o processo de compras. Precisa de um fluxo guiado, com poucos campos e validações que evitem retrabalho.

## Persona 2 — Gestor Aprovador

Gestor de área com alçada definida. Recebe dezenas de aprovações por semana. Precisa de visão resumida, contexto (centro de custo, justificativa, anexos) e decisão em um clique com parecer.

## Persona 3 — Comprador

Responsável por transformar demandas aprovadas em cotações e pedidos. Precisa de fila priorizada, informações completas e histórico confiável.

## Persona 4 — Auditor

Consulta processos encerrados. Precisa de timeline completa, trilha imutável e evidências exportáveis.

---

# 11. Premissas

- Usuários possuem acesso autenticado à plataforma (FD-001-01).
- A estrutura organizacional (empresas, unidades, centros de custo, projetos) está cadastrada no Foundation (FD-001-02).
- Existe ao menos um workflow de aprovação configurado (PR-BR-030).
- O solicitante conhece a necessidade, mas não necessariamente o fornecedor.
- Toda organização opera em ambiente multiempresa, mesmo que com uma única empresa.

---

# 12. Restrições

- O processo não inicia cotação nem pedido; termina na disponibilização para Compras.
- Nenhuma dependência de IA, ERP ou mobile no MVP (decisão definitiva do produto).
- Edição livre somente em Draft (PR-BR-040).
- Cancelamento somente antes de gerar Pedido de Compra (PR-BR-050).
- O solicitante não aprova a própria requisição (SoD, PR-001-09).

---

# 13. Gatilhos de Negócio

Os principais eventos que originam uma Solicitação de Compra são:

## Operacionais

- Material indisponível.
- Equipamento danificado.
- Falta de insumos.
- Manutenção corretiva.
- Manutenção preventiva.

## Administrativos

- Contratação de serviços.
- Aquisição de mobiliário.
- Compra de materiais de escritório.

## Estratégicos

- Novos projetos.
- Expansão operacional.
- Investimentos (CAPEX).

## Emergenciais

- Paradas operacionais.
- Quebras de equipamentos.
- Ocorrências de segurança.
- Situações críticas.

---

# 14. Resultado Esperado

Ao final deste processo deve existir uma Solicitação de Compra contendo todas as informações necessárias para que o processo de aquisição possa continuar sem necessidade de complementações.

Uma solicitação de qualidade reduz retrabalho e acelera o ciclo completo de compras.

---

# 15. Participantes do Processo

| Papel | Responsabilidade |
|--------|------------------|
| Solicitante | Identificar a necessidade e registrar a solicitação. |
| Gestor | Avaliar e aprovar a demanda conforme políticas da empresa. |
| Comprador | Executar o processo de aquisição após aprovação. |
| Administrador | Configurar regras, parâmetros e permissões. |

---

# 16. Entradas do Processo

O processo inicia com uma necessidade de negócio e recebe informações como:

- Material ou serviço.
- Quantidade.
- Unidade de medida.
- Data de necessidade.
- Centro de custo.
- Projeto.
- Prioridade.
- Justificativa.
- Anexos.

---

# 17. Saídas do Processo

Ao término deste processo serão produzidos:

- Solicitação de Compra registrada.
- Histórico de alterações.
- Timeline do processo.
- Registro de auditoria.
- Solicitação pronta para aprovação.

---

# 18. Indicadores de Negócio

Este processo deverá permitir o acompanhamento dos seguintes indicadores:

- Número de solicitações por área.
- Número de solicitações por centro de custo.
- Solicitações emergenciais.
- Tempo médio para criação.
- Tempo médio até aprovação.
- Solicitações canceladas.
- Solicitações rejeitadas.
- Lead Time da requisição.

---

# 19. KPIs

| KPI | Definição | Meta de referência |
|-----|-----------|--------------------|
| Lead Time da requisição | Criação → disponibilização para Compras | Redução contínua |
| Tempo médio de aprovação | Submissão → decisão final | Conforme SLA configurado |
| Taxa de rejeição | Rejeitadas ÷ submetidas | Tendência de queda |
| Taxa de retrabalho | Retornos para ajuste ÷ submetidas | Tendência de queda |
| % solicitações emergenciais | Emergenciais ÷ total | Tendência de queda |
| Cobertura de formalização | Compras com PR ÷ compras totais | 100% |
| Tempo até Compras | Aprovação → Ready for Procurement | Imediato |

---

# 20. Riscos de Negócio

Os principais riscos associados ao processo são:

| Risco | Impacto | Mitigação |
|--------|---------|-----------|
| Solicitação incompleta | Atraso no processo de compras | Validações obrigatórias na submissão (PR-BR-020) |
| Falta de aprovação | Compras fora da política da empresa | Workflow obrigatório (PR-BR-030) e escalonamento por SLA |
| Centro de custo incorreto | Erros orçamentários | Validação contra Organization (FD-001-02) |
| Data de necessidade inadequada | Atrasos operacionais | Validação de data e prioridade |
| Classificação incorreta | Indicadores inconsistentes | Categorias do Master Data |
| Compras emergenciais recorrentes | Aumento de custos e perda de planejamento | KPI dedicado e revisão gerencial |
| Contorno do processo (compra informal) | Perda de governança | KPI de cobertura de formalização + auditoria |
| Aprovador indisponível | Gargalo de aprovação | Delegação (PR-BR-035) e escalonamento |

---

# 21. Glossário

| Termo | Definição |
|-------|-----------|
| Solicitação de Compra (PR) | Documento formal que registra uma necessidade de aquisição |
| Draft | Estado inicial, editável, da solicitação |
| Workflow | Fluxo de aprovação parametrizável |
| Alçada | Limite de autoridade de aprovação de um gestor |
| Centro de custo | Estrutura de imputação orçamentária |
| Lead Time | Tempo total do processo, da criação à disponibilização |
| SoD | Segregação de Funções — impede conflitos de interesse |
| Equalização | Comparação técnica de propostas (módulo futuro) |
| RFQ | Request for Quotation — cotação com fornecedores |
| Pedido de Compra (OC) | Documento que formaliza a compra junto ao fornecedor |

---

# 22. Princípios do Processo

O processo de Solicitação de Compra deve seguir os seguintes princípios:

1. Toda necessidade deve ser formalizada.
2. Nenhuma solicitação deve seguir para Compras sem atender aos requisitos mínimos definidos pela organização.
3. Toda decisão deve ser registrada.
4. Toda alteração relevante deve ser auditável.
5. O processo deve ser simples para o solicitante e rigoroso para a governança.

---

# 23. Dependências

Este documento serve como base para:

- PR-001-02 — Business Rules
- PR-001-03 — State Machine
- PR-001-04 — Domain Model
- PR-001-05 — Event Storming
- PR-001-06 — BPMN
- PR-001-11 — Database
- PR-001-12 — Business Journey
- PR-001-13 — API
