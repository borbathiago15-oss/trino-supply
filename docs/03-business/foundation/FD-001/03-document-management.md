**Documento:** FD-001-03 — Document Management
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Dependências:** FD-001 (Foundation Domain), FD-001-02 (Organization), ADR-011

# FD-001-03 — Document Management

> O domínio **Document Management** é responsável por todos os arquivos e anexos da plataforma Trino Supply: upload, download, versionamento, metadados e classificação.

---

# 1. Objetivo

Centralizar a gestão de documentos em um único serviço do Foundation, garantindo que **nenhum módulo de negócio implemente armazenamento de arquivos por conta própria**.

Os módulos (Purchase Requisition, RFQ, Supplier, Contracts, Receiving etc.) referenciam documentos exclusivamente pelos identificadores fornecidos por este domínio.

Os objetivos são:

* Single Source of Truth para arquivos e anexos.
* Padronização de upload, download e versionamento.
* Rastreabilidade completa (quem enviou, quando, em qual contexto).
* Multiempresa com isolamento garantido.
* Evolução independente do mecanismo de storage.

---

# 2. Responsabilidades

O Document Management é responsável por:

* Upload de arquivos
* Download de arquivos
* Versionamento de documentos
* Metadados (nome, tipo, tamanho, autor, datas)
* Classificação do documento
* Coleções de documentos vinculadas a entidades de negócio
* Exclusão lógica (Soft Delete)
* Eventos de domínio de documentos

O Document Management **não** é responsável por:

* Regras de negócio dos módulos consumidores (ex.: obrigatoriedade de anexo por categoria — PR-BR-023)
* Autorização de acesso (IAM — FD-001-01)
* Notificações (Notification Center — FD-001-05)

---

# 3. Conceitos do Domínio

## Document

Representa um arquivo gerenciado pela plataforma.

Atributos principais:

* Id
* Empresa
* Nome do arquivo
* Tipo de conteúdo (MIME type)
* Tamanho
* Classificação
* Autor do upload
* Versão atual
* Status
* Datas de criação, alteração e exclusão lógica

---

## Document Version

Cada alteração de conteúdo gera uma nova versão. Versões são imutáveis.

Atributos principais:

* Id
* Documento
* Número da versão
* Chave de storage (`storage_key`)
* Tamanho
* Autor
* Data de criação

---

## Document Collection

Agrupamento de documentos vinculado a uma entidade de negócio (ex.: os anexos de uma Purchase Requisition).

Atributos principais:

* Id
* Empresa
* Tipo da entidade de origem (ex.: `purchase-requisition`)
* Id da entidade de origem
* Documentos associados

Este é o mecanismo pelo qual o módulo PR-001 referencia seus anexos sem possuir tabelas próprias de arquivos (ver nota arquitetural em PR-001-11, seção 14).

---

## Classification

Classificação funcional do documento, configurável pela organização.

Exemplos: especificação técnica, orçamento, contrato, foto, catálogo, nota fiscal.

---

# 4. Regras de Negócio

## DM-BR-001 — Identificador único

Todo documento e toda versão possuem identificador único gerado pelo sistema (UUID).

**Tipo:** Obrigatória

---

## DM-BR-002 — Empresa obrigatória

Todo documento pertence a exatamente uma empresa. Nenhum documento existe fora do contexto multiempresa.

**Tipo:** Obrigatória

---

## DM-BR-003 — Tipos de arquivo permitidos

Os tipos de arquivo aceitos são configuráveis pela organização. Referência inicial (UC-010): PDF, DOCX, XLSX e imagens.

**Tipo:** Configurável

---

## DM-BR-004 — Tamanho máximo

O tamanho máximo por arquivo é parametrizável pela organização.

**Tipo:** Configurável

---

## DM-BR-005 — Versões imutáveis

Uma versão criada nunca é alterada. Correções geram nova versão.

**Tipo:** Obrigatória

---

## DM-BR-006 — Exclusão somente lógica

Documentos nunca são excluídos fisicamente; utiliza-se Soft Delete. O conteúdo em storage é preservado para auditoria.

**Tipo:** Obrigatória

---

## DM-BR-007 — Vínculo por coleção

Documentos de negócio são associados a entidades exclusivamente via Document Collection, nunca por tabelas de anexo próprias dos módulos.

**Tipo:** Obrigatória

---

## DM-BR-008 — Isolamento multiempresa

Nenhuma consulta, download ou listagem pode alcançar documentos de empresas fora do escopo autorizado do usuário (IAM-BR-008).

**Tipo:** Obrigatória

---

## DM-BR-009 — Auditoria obrigatória

Upload, nova versão, classificação, remoção e download geram registro de auditoria e evento de domínio.

**Tipo:** Obrigatória

---

# 5. Entidades do Domínio

* Document
* DocumentVersion
* DocumentCollection
* Classification

---

# 6. Eventos de Domínio

Conforme ADR-010, somente eventos de negócio:

* DocumentUploaded
* DocumentVersionCreated
* DocumentClassified
* DocumentAttachedToCollection
* DocumentDetachedFromCollection
* DocumentRemoved
* DocumentDownloaded

---

# 7. Integrações

## Consumidores

* Purchase Requisition — anexos da solicitação (PR-001, UC-010, PR-BR-023)
* Supplier Management — documentação e homologação (roadmap MVP)
* Contract Management — contratos e aditivos (roadmap MVP)
* Receiving — comprovantes e divergências (roadmap MVP)
* Todos os módulos futuros

## Provedores

* Organization (FD-001-02) — contexto de empresa
* IAM (FD-001-01) — autorização e escopo
* Audit Service (FD-001-06) — trilha de auditoria

---

# 8. Storage

O storage oficial da plataforma é **MinIO** (stack aprovada).

Convenções:

* O banco de dados guarda apenas metadados e a chave de storage (`storage_key`), nunca o conteúdo binário.
* A organização física em buckets/pastas é detalhe de implementação deste domínio e não vaza para os consumidores.
* O acesso ao conteúdo ocorre somente através do Document Management — nunca por URL pública direta (Segurança por padrão).

---

# 9. Requisitos Não Funcionais

* Metadados seguem as convenções de persistência: UUID, `created_at`/`updated_at`/`deleted_at`, `created_by`/`updated_by`/`deleted_by`, `version` (PR-001-11, seção 3).
* Toda tabela principal possui `company_id`; nenhuma consulta ignora esse filtro (PR-001-11, seção 9).
* Listagens paginadas; preferir Keyset Pagination (PR-001-11, seção 12).
* Metadados elegíveis a cache (Redis); conteúdo binário nunca em cache de aplicação.

---

# 10. Roadmap

Versão 1

* Upload e download
* Versionamento
* Coleções por entidade
* Classificação
* Auditoria e eventos

Versão 2

* Pré-visualização de documentos
* OCR e indexação de conteúdo
* Expiração e retenção por política

Versão 3

* Assinatura digital
* Antivírus integrado
* Compartilhamento externo controlado (Portal do Fornecedor)
