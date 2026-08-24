# SUP-001 — Gestão de Fornecedores (MVP)

| Campo | Valor |
|---|---|
| Código | SUP-001 |
| Módulo | Procurement — Supplier Management |
| Versão | 0.1.0 (MVP consolidado) |
| Status | 🟡 MVP — pacote completo (01–17) pendente de refinamento |
| Depende de | Foundation (IAM/RBAC) |

> Spec MVP consolidada em documento único, seguindo a regra do projeto de
> documentar antes de implementar. O desdobramento no pacote padrão de 17
> documentos fica registrado como pendência no GOV-002 §10.

## 1. Escopo do MVP

Cadastro de fornecedores para emissão de pedidos de compra (PO-001):
identidade fiscal, contato e situação (ativo/inativo). Sem homologação,
avaliação de desempenho ou anexos nesta fase.

## 2. Modelo

`procurement.supplier`

| Campo | Regra |
|---|---|
| `legal_name` | obrigatório, mín. 3 caracteres |
| `trade_name` | opcional |
| `tax_id` | obrigatório; somente dígitos após normalização; 11 (CPF) ou 14 (CNPJ) dígitos; **único** |
| `email`, `phone` | opcionais |
| `active` | default `true`; inativo não recebe novos pedidos |

## 3. Regras de negócio

- **SUP-BR-001** — `tax_id` é único no cadastro; duplicidade é rejeitada.
- **SUP-BR-002** — Fornecedor inativo não pode receber novos pedidos de compra (validação no PO-001).
- **SUP-BR-003** — Inativação é reversível e não afeta pedidos já emitidos.
- **SUP-BR-004** — Manutenção do cadastro: Comprador, Gestor de Suprimentos e Administrador. Auditor consulta.

## 4. Catálogo de erros

| Código | HTTP | Situação |
|---|---|---|
| SUP-ERR-010 | 400 | `tax_id` duplicado |
| SUP-ERR-011 | 400 | `tax_id` inválido (tamanho/dígitos) |
| SUP-ERR-012 | 400 | Razão social ausente/curta |
| SUP-ERR-404 | 404 | Fornecedor inexistente |
| SUP-ERR-900 | 403 | Papel sem acesso |

## 5. API (resumo)

| Método | Rota | Papéis |
|---|---|---|
| GET `/api/v1/suppliers` | lista (ativos por padrão; `all=true` para mantenedores) | mantenedores + Auditor |
| POST `/api/v1/suppliers` | cria | mantenedores |
| PATCH `/api/v1/suppliers/{id}` | atualiza contato / ativa-inativa | mantenedores |

Envelope padrão `{data, correlationId}` / `{error:{code,message,correlationId}}`.
