# RFQ-001 — Cotação / BID e Processo Fechado de Compras (MVP)

| Campo | Valor |
|---|---|
| Código | RFQ-001 |
| Módulo | Procurement — Quotation / BID (processo fechado de compras) |
| Versão | 0.1.0 (MVP consolidado) |
| Status | 🟡 MVP — pacote completo (01–17) pendente de refinamento |
| Depende de | PR-001, SUP-001, PO-001, Foundation (IAM/RBAC) |

> Spec MVP consolidada. O processo de compras é **um encadeamento único**:
> cotação, aprovação e emissão da OC não existem como funções independentes.
> Sem IA e sem integração com ERP nesta fase (decisão registrada).

## 1. Fluxo oficial

```
Solicitação de Compra (PR-001) → Aprovação da Solicitação → Fila de Suprimentos
→ Cotação/BID (RFQ-yyyy-nnnnnn | BID-yyyy-nnnnnn) → Convite a fornecedores ativos
→ Propostas (Portal do Fornecedor, versionadas, imutáveis) → Encerramento p/ análise
→ Escolha do fornecedor (critérios + justificativa obrigatória)
→ Aprovação do Gerente → Aprovação do Diretor → Aprovado para emissão
→ Emissão da OC (OC via procurement.po_number_seq) → PDF no modelo oficial
→ OC disponível para acompanhamento
```

## 2. Máquina de estados da cotação

| Estado | Rótulo |
|---|---|
| Open | COTAÇÃO ABERTA / AGUARDANDO PROPOSTAS |
| Analysis | PROPOSTAS RECEBIDAS / EM ANÁLISE |
| AwaitingManager | FORNECEDOR SELECIONADO → AGUARDANDO APROVAÇÃO GERENCIAL |
| AwaitingDirector | AGUARDANDO APROVAÇÃO DIRETORIA |
| ApprovedForIssue | APROVADO PARA EMISSÃO |
| PoIssued | OC EMITIDA |
| Rejected | REJEITADO (motivo obrigatório) |
| Cancelled | CANCELADA (motivo obrigatório) |

"AJUSTES SOLICITADOS" é uma transição (AwaitingManager/AwaitingDirector →
Analysis) registrada na timeline com motivo — o histórico nunca é perdido.
"AGUARDANDO COTAÇÃO" é o estado derivado da PR aprovada sem cotação ativa
(fila de Suprimentos).

## 3. Regras fundamentais (RFQ-BR)

- **RFQ-BR-001** — Cotação nasce somente de PR `APPROVED`; uma cotação ativa por PR; itens copiados por snapshot (sem redigitação).
- **RFQ-BR-002** — Numeração automática, única e nunca reutilizada: sequências `procurement.rfq_number_seq` e `procurement.bid_number_seq`. O usuário não informa número.
- **RFQ-BR-003** — Convite apenas a fornecedores **ativos** do cadastro único (SUP-001); nada de cadastro paralelo. Convite registrado na timeline.
- **RFQ-BR-004** — Propostas são **imutáveis e versionadas** (nova proposta supersede, nunca apaga); registram fornecedor, data/hora, versão, condições e anexos.
- **RFQ-BR-005** — Escolha do vencedor exige proposta existente, critérios e **justificativa obrigatória**; registra usuário, data/hora e proposta vencedora.
- **RFQ-BR-006** — Segregação de funções: quem selecionou o vencedor não aprova como Gerente nem como Diretor; o Diretor não pode ser o mesmo usuário da aprovação gerencial.
- **RFQ-BR-007** — Diretor só recebe o processo após aprovação do Gerente. OC só é emitida em `ApprovedForIssue` — nunca sem as duas aprovações, sem fornecedor, com fornecedor inativo, sem itens ou sem valor.
- **RFQ-BR-008** — Após aprovação do Diretor os dados ficam **bloqueados**; qualquer alteração exige "Solicitar ajustes" (retorno formal com novo ciclo de aprovação). Sem alteração silenciosa.
- **RFQ-BR-009** — Toda transição gera evento de timeline: data/hora, usuário, ação, status anterior/novo, observação e documento relacionado. Eventos e propostas não podem ser excluídos.
- **RFQ-BR-010** — Conversão direta PR→OC fica **desativada**: PR aprovada segue exclusivamente pelo processo de cotação. (Itens de solicitação de material em rota de compra seguem com emissão direta até serem incorporados ao processo.)

## 4. Papéis

| Papel | Pode |
|---|---|
| Solicitante | criar/acompanhar a própria PR; nunca escolhe fornecedor, não aprova a própria compra, não emite OC |
| Suprimentos (Comprador/Gestor) | fila de aprovadas, abrir cotação, convidar fornecedores, registrar/acompanhar propostas, analisar, indicar vencedor, emitir OC após aprovação final |
| Gerente (Gestor de Suprimentos) | analisar processo completo e Aprovar / Rejeitar / Solicitar ajustes (motivo obrigatório p/ rejeitar/ajustar) |
| Diretor (novo papel `Director`) | idem, somente após o Gerente |
| Fornecedor (Portal) | login por CNPJ + chave de acesso; vê e responde **somente as próprias cotações**; anexa proposta; consulta histórico próprio; jamais vê dados de outros fornecedores |

## 5. Portal do Fornecedor (v1 simples)

Login (CNPJ + chave gerada pelo cadastro de fornecedores, armazenada como hash),
dashboard com "Minhas Cotações", busca por número (ex.: `RFQ-2026-000001`),
visualização da cotação, resposta com valor por item, prazo, condição de
pagamento, frete, validade, observações e anexo (PDF/imagem/planilha até 10 MB),
histórico de versões enviadas. Token de sessão próprio (papel `Supplier`) sem
nenhum acesso aos módulos internos.

## 6. Ordem de Compra e PDF

- OC emitida a partir do processo recupera automaticamente: empresa compradora
  (cadastro Empresa), fornecedor (cadastro), itens/quantidades/valores da
  proposta vencedora, condições comerciais, nº da PR e da cotação e os
  aprovadores. Nada é redigitado.
- PDF gerado no servidor (QuestPDF) seguindo o **modelo oficial da OC do Grupo
  Trino** (cabeçalho da empresa, bloco do fornecedor, dados de entrega,
  cláusulas padrão, itens com valores, totais, valor líquido + por extenso,
  comprador; 2ª página com a política de pagamento a fornecedores do cadastro
  Empresa). Armazenado na infraestrutura documental interna.

## 7. Infraestrutura documental (mínimo)

`foundation.stored_document`: id, nome, tipo, tamanho, conteúdo (bytea ≤10 MB),
vínculo (entidade + id), autor e data. Download autorizado por papel/vínculo
(fornecedor só acessa os próprios anexos). Anexos e PDFs de OC vivem aqui.

## 8. Erros principais

| Código | HTTP | Situação |
|---|---|---|
| RFQ-ERR-001 | 422 | PR não aprovada ou já possui cotação ativa |
| RFQ-ERR-010 | 400 | Fornecedor inativo/bloqueado/não cadastrado no convite |
| RFQ-ERR-020 | 409 | Transição fora do estado permitido |
| RFQ-ERR-021 | 400 | Justificativa/critérios ausentes na escolha ou decisão |
| RFQ-ERR-030 | 422 | SoD violada (mesmo usuário em etapas conflitantes) |
| RFQ-ERR-040 | 409 | Emissão de OC sem `ApprovedForIssue` ou com fornecedor inativo |
| RFQ-ERR-050 | 403 | Fornecedor tentando acessar cotação de outro fornecedor |
| PO-ERR-023 | 422 | Conversão direta PR→OC desativada — use o processo de cotação |

## 9. Fora desta etapa

IA, integração ERP, integrações externas de compras, automação financeira,
avaliação avançada de fornecedores, chat com fornecedores, SRM avançado,
envio real de e-mail (o convite é gerado e registrado; envio fica manual até o
Notification Center existir — decisão aberta em GOV-002 §10).
