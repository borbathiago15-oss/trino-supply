**Documento:** MMS-002-12 — Business Journey do Item Catalog
**Módulo:** MMS-002 — Item Catalog (Materials Domain)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002-01 (Business Context), MMS-002-02 (Business Rules), MMS-002-03 (State Machine), MMS-002-04 (Domain Model), MMS-002-06 (BPMN), MMS-002-07 (Use Cases)
**Referências:** PR-001-12 (Business Journey da Solicitação de Compra — padrão de formato), ADR-009 (Domínio Materials), ADR-010 (Aggregate enxuto)

# MMS-002-12 — Business Journey do Item Catalog

## 1. Objetivo

Descrever, em detalhes, a jornada completa de um **Item** no catálogo, desde o surgimento da necessidade de cadastro até o item se tornar referência confiável e operável para todo o Trino Supply — e, eventualmente, seu descarte.

Esta jornada será a referência oficial para:

- UX
- BPMN
- APIs
- Banco de Dados
- Regras de Negócio
- Testes
- Auditoria
- Integrações (ERP, MMS-003, MMS-004)

## 2. Princípios da Jornada

A jornada do Item será guiada pelos seguintes princípios:

1. **O catálogo é a fonte única de identidade do material** — nenhum módulo cadastra "seu" item; todos referenciam o Item Catalog.
2. **Cadastrar deve ser simples; ativar deve exigir completude** — o sistema conduz o mantenedor, reduzindo erros e retrabalho.
3. **Item incompleto nunca entra na operação** — apenas `ACTIVE` pode ser consumido por MMS-003 (requisição) e MMS-004 (estoque).
4. **Item nunca é apagado** — o ciclo de vida é Draft → Active → Inactive → Discarded (ADR-010), com histórico preservado.
5. **Toda decisão crítica ocorre com rastreabilidade** — ativação, inativação, reativação e descarte exigem motivo e geram auditoria e evento.
6. **Quem busca não precisa saber o código** — o catálogo se adapta ao vocabulário do usuário (sinônimos), e não o contrário.

## 3. Fluxo Macro da Jornada

```
Identificação da Necessidade de Cadastro
        │
        ▼
Criação do Item (DRAFT)
        │
        ▼
Enriquecimento dos Dados
(sinônimos, CA, grade de tamanhos, imagem, parâmetros de reposição)
        │
        ▼
Validação Automática de Completude
        │
        ├──────────────► Incompleto (permanece DRAFT, gaps listados)
        │
        ▼
Ativação (ACTIVE)
        │
        ▼
Consumo pela Operação
(MMS-003 requisições, MMS-004 estoque, busca por sinônimo)
        │
        ▼
Manutenção Contínua
(edição de dados permitidos, atualização de CA, imagem, parâmetros)
        │
        ├──────────────► Inativação (INACTIVE) ◄──────► Reativação (ACTIVE)
        │
        ▼
Descarte (DISCARDED — terminal)
```

## 4. Jornada A — O Mantenedor do Catálogo (jornada principal)

### 4.1 Etapa 1 — Identificação da Necessidade de Cadastro

**Objetivo:** reconhecer que um material precisa existir (ou ser corrigido) no catálogo.

**Origens possíveis:**

- Solicitante não encontra o item na busca da requisição (MMS-003) e reporta ao mantenedor.
- Novo EPI ou fardamento aprovado pela área de Segurança do Trabalho / RH.
- Importação ou sincronização inicial com o ERP (código ERP conhecido).
- Revisão periódica do catálogo (higiene de dados).
- Migração de planilha legada.

**Ponto de dor típico:** o usuário operacional desiste da requisição quando não acha o item — e a demanda vira processo paralelo (papel, e-mail). A jornada do catálogo começa, na prática, com a **frustração de quem buscou e não achou**.

**Momento de verdade:** o mantenedor verificar se o item já existe (busca por descrição, sinônimo e código ERP) **antes** de criar — evitando duplicidade, o maior inimigo do catálogo.

**Resultado esperado:** necessidade confirmada como legítima e ainda não atendida pelo catálogo.

**KPI da etapa:** taxa de itens criados que já existiam (duplicidade detectada pós-criação) — meta: 0%.

### 4.2 Etapa 2 — Criação do Item

O mantenedor inicia o cadastro (UC-IC-001).

Neste momento o sistema gera:

- Identificador único do item
- Data/Hora de criação
- Empresa (multi-tenant, company_id)
- Autor (mantenedor autenticado)
- Versão inicial (controle de concorrência otimista)

**Status inicial:** `DRAFT`.

Dados mínimos da criação:

- Código (único por empresa, soft-delete-ciente)
- Descrição
- Grupo (EPI ou Fardamento)
- Unidade
- Código ERP (quando aplicável, único por empresa)

Enquanto estiver em `DRAFT`:

- Pode editar todos os campos
- Pode excluir (soft delete)
- Pode adicionar sinônimos, CA, grade de tamanhos, imagem, parâmetros de reposição
- **Não pode** ser consumido por MMS-003/MMS-004 (IC-BR-001)

### 4.3 Etapa 3 — Enriquecimento dos Dados

Cada informação adicional aumenta a utilidade operacional do item:

| Enriquecimento | Aplicação | Regra |
|---|---|---|
| Sinônimos | Todo item | Melhoram a busca do solicitante (IC-BR-040..043) |
| CA (Certificado de Aprovação) | Obrigatório p/ grupo EPI | Válido e não vencido (IC-BR-080/081) |
| Grade de tamanhos | Quando o item tem variação | Valores do domínio FD-001-09 SIZE_GRID (IC-BR-082) |
| Imagem | Recomendado | Via FD-001-03 Storage (MinIO) (IC-BR-083) |
| Parâmetros de reposição | Quando estocado | Ponto de reposição, estoque mínimo/máximo (IC-BR-060..063) |

**Momento de verdade:** o sistema mostra, em tempo real, **o que falta para o item poder ser ativado** (checklist de completude) — o mantenedor nunca descobre a exigência só no erro da ativação.

### 4.4 Etapa 4 — Validação Automática de Completude

Antes da ativação o sistema executa validações automáticas (IC-BR-020..023, IC-BR-080..083).

**Obrigatórias (bloqueantes):**

- Descrição preenchida e normalizada?
- Grupo válido (EPI/Fardamento)?
- Unidade válida (domínio FD-001-09 UNIT_OF_MEASURE)?
- Grupo EPI → CA informado, válido e não vencido?
- Tamanhos da grade pertencem à grade padrão?
- Usuário possui permissão `items.write` + `items.lifecycle`?

**Inteligentes (não bloqueantes):**

- Alertar descrição muito semelhante a item existente (possível duplicata).
- Alertar CA com vencimento próximo (< 90 dias).
- Alertar item EPI sem imagem (dificulta identificação no almoxarifado).
- Alertar item estocado sem parâmetros de reposição.

**Importante:** alertas não bloqueiam. Apenas as obrigatórias bloqueiam a ativação.

### 4.5 Etapa 5 — Ativação

Ao confirmar a ativação (UC-IC-002), o sistema:

1. Valida completude (Etapa 4);
2. Registra motivo da ativação (obrigatório — IC-BR-030);
3. Muda o status para `ACTIVE`;
4. Trava edição de campos estruturais (código, grupo, unidade — IC-BR-031);
5. Publica o evento `EVT-IC-003 item.activated` (crítico — consumers: MMS-003, MMS-004, search);
6. Gera registro de auditoria (quem, quando, motivo, versão).

**Momento de verdade:** a partir deste instante o item passa a existir para a operação inteira. A ativação é o "go live" do material.

**KPI da etapa:** tempo mediano criação → ativação — meta: < 1 dia útil para itens completos.

### 4.6 Etapa 6 — Consumo pela Operação

Com o item `ACTIVE`:

- **MMS-003 (Requisição de Material):** o solicitante encontra o item na busca — inclusive pelos **sinônimos** cadastrados — e o inclui na solicitação ao almoxarifado.
- **MMS-004 (Gestão de Estoque):** saldos, reservas e movimentações passam a referenciar o item; parâmetros de reposição alimentam alertas de estoque mínimo e ruptura.
- **Search:** o item é indexado e ranqueado por relevância (descrição > sinônimo > código ERP).

**Momento de verdade:** o solicitante digita o nome que ele conhece ("luva vaqueta", "farda verão") e **encontra** o item oficial. Cada busca sem resultado é um sinal de gap no catálogo ou nos sinônimos.

**KPI da etapa:** taxa de buscas com resultado encontrado (search success rate) — meta: ≥ 95%.

### 4.7 Etapa 7 — Manutenção Contínua

Durante a vida ativa, o mantenedor pode (UC-IC-003):

- Editar descrição, sinônimos, CA (renovação), imagem, parâmetros de reposição;
- **Não pode** alterar código, grupo e unidade (campos estruturais congelados — IC-BR-031);
- Toda alteração incrementa versão e gera auditoria + evento (`EVT-IC-002 item.updated`, `EVT-IC-006 item.ca-updated` etc.).

**Ponto de dor típico:** CA de EPI vence e ninguém percebe → item continua ativo irregularmente. Mitigação: alerta proativo de vencimento (IC-NOT-005) e view operacional `vw_epi_sem_ca`/CA vencido.

### 4.8 Etapa 8 — Inativação e Reativação

**Inativação (UC-IC-004):** item sai da operação (`INACTIVE`) — novas requisições e movimentações são bloqueadas (IC-BR-050), mas históricos, saldos e referências existentes permanecem íntegros. Exige motivo + publica `EVT-IC-004 item.inactivated` (crítico).

**Reativação (UC-IC-005):** item `INACTIVE` pode voltar a `ACTIVE` (ex.: CA renovado, retorno do fornecimento). Mesmas validações de completude da ativação + motivo + `EVT-IC-005 item.reactivated` (crítico).

**Momento de verdade:** a inativação **não quebra** o passado (requisições antigas continuam consultáveis) e **protege** o futuro (nada novo é criado sobre o item).

### 4.9 Etapa 9 — Descarte

**Descarte (UC-IC-006):** estado terminal `DISCARDED` para itens cadastrados por engano ou sem uso confirmado (IC-BR-070: apenas se nunca foi consumido). Libera o código para reuso (unicidade soft-delete-ciente). Exige motivo + auditoria + `EVT-IC-007 item.discarded`.

**Resultado:** o item sai definitivamente do catálogo operacional, mas permanece auditável para sempre.

## 5. Jornada B — O Solicitante (consumidor indireto)

O solicitante **não opera o catálogo**, mas é quem mais depende dele:

```
Necessidade de material (EPI/fardamento)
        │
        ▼
Busca na requisição (MMS-003) por nome livre
        │
        ├──────────────► Encontrou (descrição/sinônimo/código ERP) → segue a requisição ✅
        │
        ├──────────────► Não encontrou → reporta ao mantenedor (alimenta Jornada A, Etapa 1)
        │
        ▼
Item encontrado com imagem e tamanhos claros
        │
        ▼
Seleciona produto + tamanho + quantidade (grade do item)
```

**Momentos de verdade:** (1) encontrar pelo próprio vocabulário; (2) ver a imagem e escolher o tamanho certo na grade — reduz troca por tamanho errado, o motivo mais comum de solicitação.

**KPI da jornada:** % de requisições sem retrabalho por item/tamanho errado — meta: ≥ 98%.

## 6. Jornada C — O Auditor (observador)

O auditor não executa ações; consome rastros:

- Timeline completo do item (criação, edições, ativações, inativações, motivos, autores, versões);
- Eventos publicados no outbox (`EVT-IC-001..008`) com correlationId;
- Trilha de permissões avaliadas (POL-IC-AUTH-001..008);
- Views de higiene do catálogo (EPI sem CA, CA vencido, itens nunca consumidos).

**Momento de verdade:** qualquer pergunta "quem ativou este EPI com CA vencido?" tem resposta em segundos, com evidência.

## 7. Encerramento da Jornada

A jornada do Item termina de duas formas:

1. **Vida longa e útil:** o item permanece `ACTIVE`, sendo referência confiável para requisições e estoque — encerramento não ocorre, e isso é o cenário ideal.
2. **Descarte justificado:** o item atinge `DISCARDED` (terminal), com motivo, auditoria e histórico preservados.

Em ambos os casos, **nenhum dado é perdido** e **nenhuma referência histórica é quebrada**.

## 8. Resumo de KPIs da Jornada

| KPI | Jornada | Meta |
|---|---|---|
| Taxa de duplicidade pós-criação | Mantenedor (Etapa 1) | 0% |
| Tempo mediano criação → ativação | Mantenedor (Etapa 5) | < 1 dia útil |
| Search success rate (buscas com resultado) | Solicitante | ≥ 95% |
| % itens EPI ativos com CA válido | Mantenedor (Etapa 7) | 100% |
| % requisições sem retrabalho por item/tamanho | Solicitante | ≥ 98% |
| Cobertura de auditoria de transições | Auditor | 100% |

## 9. Histórico de Versão

| Versão | Data | Autor | Descrição |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada — jornadas do mantenedor (cadastro→ativação→manutenção→descarte), do solicitante (busca por sinônimo) e do auditor; pontos de dor, momentos de verdade e KPIs por etapa; alinhada a IC-BR, UC-IC-001..007 e EVT-IC-001..008. |
