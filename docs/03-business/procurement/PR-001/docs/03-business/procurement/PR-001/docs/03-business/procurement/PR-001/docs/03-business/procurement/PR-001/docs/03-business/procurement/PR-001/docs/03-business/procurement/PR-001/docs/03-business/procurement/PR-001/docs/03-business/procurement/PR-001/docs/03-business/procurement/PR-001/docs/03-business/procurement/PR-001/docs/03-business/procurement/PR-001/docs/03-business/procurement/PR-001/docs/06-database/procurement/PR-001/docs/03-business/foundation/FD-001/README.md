Perfeito. Aqui vou fazer uma mudança importante no rumo do projeto.

## 📌 Decisão de Arquitetura

Eu **não seguiria para a API agora**.

Mesmo sendo tecnicamente possível, isso criaria um problema: estaríamos definindo endpoints antes de termos uma **linguagem comum da plataforma**.

Um produto enterprise não nasce pela API; ele nasce pelo **modelo de dados compartilhado** e pelo **modelo de domínio**.

### Minha proposta

Antes de documentarmos o módulo de Requisição, vamos construir o **Foundation**, que será reutilizado por todos os módulos.

Essa decisão evita retrabalho quando chegarmos em:

* RFQ
* Purchase Order
* Supplier
* Contracts
* Receiving
* Inventory
* Analytics

---

# 🏛 Arquitetura proposta da plataforma

```text
Foundation
│
├── Identity & Access Management
├── Organization
├── Document Management
├── Workflow Engine
├── Notification Center
├── Audit
├── Timeline
├── Collaboration
├── Master Data
├── Configuration
└── Platform Settings

Procurement
│
├── Purchase Requisition
├── RFQ
├── Quotation
├── Purchase Order
└── Purchase Catalog

Supplier Management
Contracts
Receiving
Analytics
Platform
```

Essa arquitetura permitirá reutilização máxima e baixo acoplamento.

---

# A próxima entrega não será PR-001-12 (API)

Ela será o primeiro documento do **Foundation**, porque tudo o que construirmos daqui para frente dependerá dele.

## 📄 Arquivo

```text
docs/03-business/foundation/FD-001/README.md
```

**Documento:** FD-001 — Foundation Domain
**Versão:** 1.0.0
**Status:** Approved

---

# FD-001 — Foundation Domain

## Objetivo

O domínio **Foundation** concentra todos os serviços e componentes compartilhados da plataforma Trino Supply.

Nenhum módulo de negócio deverá implementar funcionalidades que já existam no Foundation.

O objetivo é garantir:

* Reutilização
* Padronização
* Baixo acoplamento
* Escalabilidade
* Governança

---

## Princípios

1. Todo módulo consome serviços do Foundation.
2. O Foundation não depende de módulos de negócio.
3. O Foundation define padrões corporativos.
4. Todo componente compartilhado deve ser implementado apenas uma vez.

---

## Capacidades do Foundation

| Código    | Capacidade                   | Responsabilidade                            |
| --------- | ---------------------------- | ------------------------------------------- |
| FD-BC-001 | Identity & Access Management | Usuários, papéis, permissões e autenticação |
| FD-BC-002 | Organization                 | Empresas, filiais, unidades e departamentos |
| FD-BC-003 | Document Management          | Anexos e arquivos                           |
| FD-BC-004 | Workflow Engine              | Aprovações e fluxos                         |
| FD-BC-005 | Notification Center          | Notificações e mensagens                    |
| FD-BC-006 | Audit Service                | Trilha de auditoria                         |
| FD-BC-007 | Timeline Service             | Histórico cronológico                       |
| FD-BC-008 | Collaboration                | Comentários e menções                       |
| FD-BC-009 | Master Data                  | Cadastros compartilhados                    |
| FD-BC-010 | Configuration                | Parâmetros da plataforma                    |

---

## Responsabilidades

### Identity & Access Management

* Usuários
* Grupos
* Roles
* Permissões
* MFA (futuro)
* SSO (futuro)

---

### Organization

* Empresas
* Filiais
* Unidades
* Centros de custo
* Projetos
* Estrutura organizacional

---

### Workflow

* Aprovações
* Alçadas
* SLA
* Delegações
* Escalonamentos

---

### Notification

* E-mail
* Sistema
* Push (futuro)
* Teams (futuro)
* Slack (futuro)

---

### Audit

* Logs de negócio
* Logs de acesso
* Histórico de alterações
* Conformidade

---

### Timeline

* Eventos do processo
* Mudanças de estado
* Comentários
* Aprovações

---

### Document Management

* Versionamento
* Upload
* Download
* Metadados
* Classificação

---

### Collaboration

* Comentários
* Menções
* Discussões
* Histórico de comunicação

---

## Módulos consumidores

Todos os módulos da plataforma utilizarão o Foundation:

* Purchase Requisition
* RFQ
* Purchase Order
* Supplier Management
* Contracts
* Receiving
* Inventory (futuro)
* Analytics



