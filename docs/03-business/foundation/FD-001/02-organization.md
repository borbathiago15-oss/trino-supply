**Documento:** FD-001-02 — Organization
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Dependências:** FD-001 (Foundation Domain), FD-001-01 (IAM), ADR-011

# FD-001-02 — Organization

> O domínio **Organization** é responsável pela estrutura organizacional oficial da plataforma Trino Supply: empresas, filiais, unidades, departamentos, centros de custo e projetos.

---

# 1. Objetivo

Centralizar a estrutura organizacional em um único domínio do Foundation, garantindo que **nenhum módulo de negócio modele empresa, unidade, centro de custo ou projeto por conta própria**.

Todos os módulos referenciam a estrutura organizacional exclusivamente por identificadores fornecidos por este domínio.

Os objetivos são:

* Single Source of Truth da estrutura organizacional.
* Suporte a multiempresa (regra PR-BR-071).
* Base para o Escopo Organizacional da autorização (PR-001-09 e FD-001-01).
* Base para imputação de custos (centro de custo) e investimentos (projeto).
* Rastreabilidade e auditoria de toda alteração estrutural.

---

# 2. Responsabilidades

O Organization é responsável por:

* Empresas
* Filiais
* Unidades operacionais
* Departamentos
* Centros de custo
* Projetos
* Hierarquia organizacional
* Vigência e status das entidades
* Eventos de domínio da estrutura

O Organization **não** é responsável por:

* Autenticação e autorização (IAM — FD-001-01)
* Regras de aprovação e alçadas (Workflow Engine — FD-001-04)
* Cadastros de negócio como materiais, serviços e categorias (Master Data — FD-001-09)

---

# 3. Conceitos do Domínio

## Company (Empresa)

Entidade raiz da estrutura. Toda informação da plataforma pertence a exatamente uma empresa.

Atributos principais:

* Id
* Razão social
* Nome fantasia
* Documento fiscal
* Status
* Data de criação

Regra associada: PR-BR-002 (toda solicitação pertence a uma empresa cadastrada).

---

## Branch (Filial)

Estabelecimento de uma empresa.

Atributos principais:

* Id
* Empresa
* Nome
* Documento fiscal
* Endereço
* Status

---

## Business Unit (Unidade)

Unidade operacional de uma empresa. Contexto de trabalho dos usuários e origem das solicitações.

Atributos principais:

* Id
* Empresa
* Filial (opcional)
* Nome
* Código
* Status

Regra associada: PR-BR-003 (vínculo com unidade é configurável pela organização).

---

## Department (Departamento)

Subdivisão funcional de uma unidade ou empresa. Utilizado no Escopo Organizacional (PR-001-09).

Atributos principais:

* Id
* Empresa
* Unidade
* Nome
* Status

---

## Cost Center (Centro de Custo)

Estrutura de imputação de custos. Pode ser informado por solicitação ou por item (PR-BR-014).

Atributos principais:

* Id
* Empresa
* Unidade (opcional, conforme parametrização)
* Código
* Descrição
* Status

---

## Project (Projeto)

Estrutura de imputação de investimentos e demandas específicas. Obrigatoriedade configurável (PR-BR-015).

Atributos principais:

* Id
* Empresa
* Código
* Nome
* Descrição
* Data de início
* Data de término
* Status

---

## Organizational Hierarchy (Hierarquia)

Representa os vínculos entre as entidades:

```text
Company
  └── Branch
        └── Business Unit
              └── Department
                    └── Cost Center
Project (vinculado à Company, transversal à hierarquia)
```

---

# 4. Status e Vigência

Todas as entidades do domínio possuem ciclo de vida simplificado:

| Status | Descrição |
|--------|-----------|
| Active | Disponível para uso em novos documentos |
| Inactive | Indisponível para novos vínculos; vínculos históricos preservados |

* Projetos adicionalmente respeitam vigência por data de início e término.
* Não existe exclusão física: somente Soft Delete (convenção PR-001-11).

---

# 5. Regras de Negócio

## ORG-BR-001 — Identificador único

Toda entidade do domínio possui identificador único gerado pelo sistema (UUID).

**Tipo:** Obrigatória

---

## ORG-BR-002 — Empresa obrigatória

Nenhuma entidade do domínio existe fora de uma empresa.

**Tipo:** Obrigatória

---

## ORG-BR-003 — Unidade pertence a uma empresa

Toda unidade está vinculada a exatamente uma empresa.

**Tipo:** Obrigatória

---

## ORG-BR-004 — Centro de custo por empresa

O centro de custo pertence a uma empresa; o vínculo adicional com unidade é configurável.

**Tipo:** Configurável

---

## ORG-BR-005 — Projeto com vigência

Todo projeto possui data de início; data de término é opcional enquanto o projeto estiver ativo.

**Tipo:** Obrigatória

---

## ORG-BR-006 — Inativação em vez de exclusão

Entidades com vínculos históricos não podem ser excluídas; devem ser inativadas.

**Tipo:** Obrigatória

---

## ORG-BR-007 — Entidade inativa não recebe novos vínculos

Documentos novos não podem referenciar empresas, unidades, centros de custo ou projetos inativos.

**Tipo:** Obrigatória

---

## ORG-BR-008 — Código único por empresa

Códigos de unidade, centro de custo e projeto são únicos dentro da empresa.

**Tipo:** Obrigatória

---

## ORG-BR-009 — Auditoria obrigatória

Toda criação, alteração, inativação ou reativação gera registro de auditoria e evento de domínio.

**Tipo:** Obrigatória

---

## ORG-BR-010 — Isolamento multiempresa

Nenhuma consulta pode retornar entidades de empresas fora do escopo autorizado do usuário (PR-BR-071, IAM-BR-008).

**Tipo:** Obrigatória

---

# 6. Entidades do Domínio

* Company
* Branch
* BusinessUnit
* Department
* CostCenter
* Project

---

# 7. Eventos de Domínio

Conforme ADR-010, somente eventos de negócio:

* CompanyCreated
* CompanyUpdated
* CompanyDeactivated
* BranchCreated
* BranchUpdated
* BranchDeactivated
* BusinessUnitCreated
* BusinessUnitUpdated
* BusinessUnitDeactivated
* DepartmentCreated
* DepartmentUpdated
* DepartmentDeactivated
* CostCenterCreated
* CostCenterUpdated
* CostCenterDeactivated
* ProjectCreated
* ProjectUpdated
* ProjectClosed

---

# 8. Integrações

## Consumidores

* Identity & Access Management — Escopo Organizacional (FD-001-01)
* Purchase Requisition — empresa, unidade, centro de custo, projeto (PR-001)
* Workflow Engine — contexto de aprovação (FD-001-04)
* Analytics — indicadores por empresa, unidade e centro de custo
* Todos os módulos de negócio futuros (RFQ, Purchase Order, Supplier, Contracts, Receiving)

## Provedores

* Nenhum. O Organization não depende de outros domínios.

---

# 9. Requisitos Não Funcionais

* Consultas sempre filtradas por `company_id` (convenção PR-001-11, seção 9).
* Listagens paginadas e indexadas; preferir Keyset Pagination (PR-001-11, seção 12).
* Estrutura organizacional elegível para cache (Redis) por ser de baixa volatilidade e alta leitura.
* Toda entidade segue as convenções: UUID, `created_at`/`updated_at`/`deleted_at`, `created_by`/`updated_by`/`deleted_by`, `version` (PR-001-11, seção 3).

---

# 10. Roadmap

Versão 1

* Empresas
* Filiais
* Unidades
* Departamentos
* Centros de custo
* Projetos
* Auditoria e eventos

Versão 2

* Importação de estrutura (planilha)
* Hierarquia visual (organograma)
* Fusão e reorganização de unidades

Versão 3

* Sincronização com ERP (quando o módulo de integrações existir — fora do MVP)
