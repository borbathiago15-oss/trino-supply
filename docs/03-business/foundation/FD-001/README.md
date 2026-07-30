**Documento:** FD-001 — Foundation Domain
**Versão:** 1.0.0
**Status:** Approved

> Decisão arquitetural associada: ADR-011 — Foundation antes das APIs (ver `docs/17-adr/ADR-011-foundation-before-apis.md`).

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



