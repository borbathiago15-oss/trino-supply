# PR-001-01 — Business Context

> Este documento descreve o contexto de negócio que justifica a existência do módulo de Solicitação de Compra (Purchase Requisition).

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

# 5. Quando uma Solicitação Deve Ser Criada

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

# 6. Gatilhos de Negócio

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

# 7. Resultado Esperado

Ao final deste processo deve existir uma Solicitação de Compra contendo todas as informações necessárias para que o processo de aquisição possa continuar sem necessidade de complementações.

Uma solicitação de qualidade reduz retrabalho e acelera o ciclo completo de compras.

---

# 8. Participantes do Processo

| Papel | Responsabilidade |
|--------|------------------|
| Solicitante | Identificar a necessidade e registrar a solicitação. |
| Gestor | Avaliar e aprovar a demanda conforme políticas da empresa. |
| Comprador | Executar o processo de aquisição após aprovação. |
| Administrador | Configurar regras, parâmetros e permissões. |

---

# 9. Entradas do Processo

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

# 10. Saídas do Processo

Ao término deste processo serão produzidos:

- Solicitação de Compra registrada.
- Histórico de alterações.
- Timeline do processo.
- Registro de auditoria.
- Solicitação pronta para aprovação.

---

# 11. Indicadores de Negócio

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

# 12. Riscos de Negócio

Os principais riscos associados ao processo são:

| Risco | Impacto |
|--------|---------|
| Solicitação incompleta | Atraso no processo de compras |
| Falta de aprovação | Compras fora da política da empresa |
| Centro de custo incorreto | Erros orçamentários |
| Data de necessidade inadequada | Atrasos operacionais |
| Classificação incorreta | Indicadores inconsistentes |
| Compras emergenciais recorrentes | Aumento de custos e perda de planejamento |

---

# 13. Princípios do Processo

O processo de Solicitação de Compra deve seguir os seguintes princípios:

1. Toda necessidade deve ser formalizada.
2. Nenhuma solicitação deve seguir para Compras sem atender aos requisitos mínimos definidos pela organização.
3. Toda decisão deve ser registrada.
4. Toda alteração relevante deve ser auditável.
5. O processo deve ser simples para o solicitante e rigoroso para a governança.

---

# 14. Dependências

Este documento serve como base para:

- PR-001-02 — Business Rules
- PR-001-03 — Event Storming
- PR-001-04 — State Machine
- PR-001-05 — BPMN
- PR-001-11 — Domain Model
- PR-001-12 — Database
- PR-001-13 — API
