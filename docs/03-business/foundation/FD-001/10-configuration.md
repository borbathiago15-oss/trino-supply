# FD-001-10 — Configuration

| Campo | Valor |
|---|---|
| **Documento** | FD-001-10 |
| **Módulo** | Foundation — Configuration (FD-BC-010) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-07-30 |
| **Dependências** | FD-001 (Foundation Overview), FD-001-01 (IAM), FD-001-02 (Organization), FD-001-06 (Audit Service), FD-001-05 (Notification Center), ADR-009/010/011, SEC-001/SEC-003 (Segredos) |
| **Referências de negócio** | PR-001-05 §14.1 (Mensageria Transversal), PR-001-02 Business Rules (configs `procurement.pr.*`), FD-001-09 Master Data (fronteira vocabulário × parâmetro) |

---

## 1. Objetivo

O Configuration é o domínio do Foundation responsável pelos **parâmetros de comportamento do sistema**: valores tipados, versionados e auditados que controlam como os módulos operam — limites, janelas, flags e integrações — sem exigir deploy para mudar.

Ele entrega:

1. **Definições tipadas de configuração** — toda chave tem esquema (tipo, faixa, formato, obrigatoriedade) registrado antes do primeiro uso;
2. **Escopos com herança** — plataforma → organização → módulo (+ usuário apenas onde documentado), com resolução por precedência;
3. **Feature flags governadas** — ligamento gradual de funcionalidades por organização, com auditoria;
4. **Segredos por referência** — chaves sensíveis nunca armazenadas em claro: apenas referência ao cofre de segredos (SEC-001);
5. **Mudança sem deploy** — valores atualizados em runtime com propagação por evento e invalidação de cache;
6. **Auditoria total** — quem mudou, o quê, de qual valor para qual valor, quando e por quê.

### 1.1 Fronteira com os vizinhos

| Pertence ao Configuration (FD-001-10) | Pertence a outro domínio |
|---|---|
| Parâmetros de comportamento: limites, janelas, timeouts, flags, endpoints de integração, retenções operacionais | Vocabulário de negócio (moedas, UoM, categorias) → **Master Data (FD-001-09)** |
| Preferências técnicas da organização (limites institucionais de canal, idioma padrão, fuso) | Estrutura e políticas organizacionais → **Organization (FD-001-02)** |
| Preferências individuais de notificação | **Notification Center (FD-001-05)** |

**Regra de bolso:** se muda o **significado** de um dado de negócio, é Master Data; se muda o **comportamento** do sistema, é Configuration.

### 1.2 O que o Configuration NÃO é

- **Não é cofre de segredos.** Senhas, tokens e chaves vivem no secret manager; aqui ficam apenas `SecretRef` (identificador + versão), nunca o valor (CF-BR-006).
- **Não é variável de ambiente.** Configurações de infraestrutura (connection strings, portas) pertencem ao deploy; o Configuration cobre parâmetros de negócio/comportamento ajustáveis em runtime.
- **Não é documento de negócio.** Nenhuma regra de negócio "mora" aqui: a chave parametriza, a regra permanece documentada no módulo.

---

## 2. Conceitos do Domínio

| Conceito | Descrição |
|---|---|
| **ConfigDefinition** | Registro de uma chave: `key` (dotted, ex.: `workflow.approval.reminder-hours`), esquema (tipo, faixa, regex, enum), escopo permitido, sensibilidade, valor padrão, módulo proprietário, versão do esquema. |
| **ConfigValue** | Valor atribuído a uma chave em um escopo (plataforma/organização/módulo/usuário), com `version` para controle otimista e `reason` obrigatório na mudança. |
| **Scope** | `PLATFORM` → `ORGANIZATION` → `MODULE` → `USER` (USER apenas para chaves explicitamente marcadas `userOverridable`). |
| **Resolution** | Resolução por precedência: valor mais específico vence; ausência cai para o escopo acima até o `default` da definição. |
| **FeatureFlag** | Definição booleana com estratégia de rollout: `ALL`, `ORG_LIST`, `PERCENTAGE` (futuro), `OFF`. |
| **SecretRef** | Referência a segredo externo: `{ vault, path, version }`; resolução em runtime por identidade de serviço, nunca persistida em claro nem exposta por API. |
| **ConfigSnapshot** | Fotografia dos valores efetivos consumida por um processo (ex.: pin de configurações em workflow instanciado — alinhado ao FD-001-04). |

---

## 3. Escopos e Precedência

```
PLATFORM (padrão corporativo)
   └─ ORGANIZATION (ajuste da organização)
        └─ MODULE (ajuste específico do módulo na organização)
             └─ USER (somente chaves userOverridable)
```

- Resolução sempre do mais específico para o `default` da ConfigDefinition (CF-BR-003).
- Chaves declaradas como `lockedAt: PLATFORM` não admitem override em escopos inferiores (uso: limites de segurança e compliance).
- Mudança em escopo superior **não** afeta quem já tem override em escopo inferior.
- Exemplos: `workflow.approval.sla-hours` (PLATFORM=48, ORG "A"=24), `notification.low.window-start` (ORG), `collaboration.edit-window-hours` (PLATFORM, locked).

---

## 4. Modelo de Dados

### 4.1 ConfigDefinition

| Campo | Descrição |
|---|---|
| `key` | Dotted key imutável, namespaced por módulo (`{dominio}.{contexto}.{nome}`). |
| `schema` | `{ type: string\|number\|boolean\|enum\|duration\|cron\|json, min, max, regex, options[] }`. |
| `allowedScopes` | Subconjunto de PLATFORM/ORGANIZATION/MODULE/USER. |
| `lockedAt` | (Opcional) escopo que trava overrides. |
| `sensitivity` | `PUBLIC` \| `INTERNAL` \| `SECRET_REF` (este último só aceita SecretRef). |
| `default` | Valor padrão validado pelo esquema. |
| `ownerModule` | Módulo proprietário da chave (responsável pelo consumo). |
| `schemaVersion` | Versão do esquema; mudanças incompatíveis exigem migração documentada. |

### 4.2 ConfigValue

| Campo | Descrição |
|---|---|
| `valueId` | UUIDv7. |
| `key` | Referência à definição. |
| `scope` / `scopeRef` | Escopo + alvo (`organizationId`, `moduleCode`, `userId`). |
| `value` | Valor validado (ou SecretRef). |
| `version` | Controle otimista (409 em conflito). |
| `reason` | Motivo obrigatório da atribuição/alteração. |
| `updatedBy` / `updatedAt` | Governança. |

- Cache de leitura (Redis) com chave `cfg:{key}:{scope}:{scopeRef}` e invalidação por evento (CF-EVT-002).

---

## 5. Feature Flags

- Flag = ConfigDefinition booleana com `rollout: ALL | ORG_LIST | OFF` (`PERCENTAGE` na v2).
- Avaliação em runtime: `feature.enabled(flagKey, organizationId)` — server-side, nunca apenas no frontend (CF-BR-008).
- Flags de funcionalidades **não documentadas** são proibidas: toda flag referencia o documento que a justifica (CF-BR-009) — coerente com a regra-mestre do projeto.
- Rollout e rollback são mudanças de ConfigValue: auditadas, com `reason`, e propagadas por evento.

---

## 6. Regras de Negócio

| Código | Regra |
|---|---|
| **CF-BR-001** | Nenhuma chave é lida sem ConfigDefinition registrada; chave desconhecida falha na validação e nunca assume valor implícito em código. |
| **CF-BR-002** | Todo valor é validado contra o esquema antes de persistir; valor inválido é rejeitado com código de erro. |
| **CF-BR-003** | A resolução segue precedência de escopo até o `default` da definição; nenhum módulo implementa fallback próprio. |
| **CF-BR-004** | Toda mudança exige `reason` e é auditada com antes/depois (FD-001-06); leituras administrativas também são auditadas. |
| **CF-BR-005** | Mudança publica evento e invalida cache; propagação P95 ≤ 5 s. |
| **CF-BR-006** | Chaves `SECRET_REF` armazenam apenas referência ao cofre; valor de segredo nunca persiste, nunca aparece em API, log ou auditoria. |
| **CF-BR-007** | Chaves `lockedAt` não admitem override em escopo inferior; tentativa retorna erro explícito. |
| **CF-BR-008** | Feature flags são avaliadas server-side; o frontend recebe apenas o resultado da avaliação. |
| **CF-BR-009** | Toda flag/chave referencia o documento que a justifica; parâmetro sem lastro documental é removido. |
| **CF-BR-010** | Manutenção exige permissão por escopo: `config.platform.manage`, `config.organization.manage`, `config.module.manage`; SoD entre quem propõe e quem aplica em chaves marcadas `requiresDualControl`. |
| **CF-BR-011** | Isolamento multiempresa: valores de organização são invisíveis a outras; escopo USER só é resolvido para o próprio usuário. |
| **CF-BR-012** | Processos que fixam comportamento no início (ex.: workflow instanciado) consomem ConfigSnapshot pinado; mudanças posteriores não os alteram retroativamente. |

---

## 7. Eventos

### 7.1 Publicados pelo Configuration

| Código | Evento | Payload (resumo) | Consumidores |
|---|---|---|---|
| **CF-EVT-001** | ConfigDefinitionRegistered | key, schemaVersion, allowedScopes, ownerModule | Plataforma, Audit |
| **CF-EVT-002** | ConfigValueChanged | key, scope, scopeRef, oldValueHash, newValue (não-sensível), version, reason, changedBy | Cache, módulos, Audit |
| **CF-EVT-003** | ConfigValueRemoved | key, scope, scopeRef, removedBy, reason | Cache, Audit |
| **CF-EVT-004** | FeatureFlagToggled | flagKey, rollout, organizationId?, enabled, changedBy, reason | Módulos, Audit |
| **CF-EVT-005** | ConfigSecretRotated | key, scope, newSecretVersion (sem valor) | Serviços consumidores, Audit |

- Valores sensíveis trafegam **hashed ou omitidos** nos payloads (CF-BR-006); envelope e garantias conforme PR-001-05 §14.1 / ADR-010.

### 7.2 Consumidos

| Origem | Uso |
|---|---|
| (MVP: nenhum) | Manutenção via API administrativa; na v2, sincronização de definições por pacote de release (config-as-code) poderá registrar definições por job auditado. |

---

## 8. APIs

| Método | Endpoint | Descrição | Permissão |
|---|---|---|---|
| `GET` | `/api/v1/configuration/resolve?keys[]=&organizationId=&module=` | Resolve valores efetivos (interno/server-to-server) | serviço autenticado |
| `GET` | `/api/v1/configuration/features?organizationId=` | Resultado das flags para a organização | autenticado |
| `GET` | `/api/v1/admin/configuration/definitions?cursor=` | Lista definições | `config.read` |
| `POST` | `/api/v1/admin/configuration/definitions` | Registra definição | `config.definition.manage` |
| `GET` | `/api/v1/admin/configuration/values?key=&scope=&cursor=` | Lista valores por chave/escopo | `config.read` |
| `PUT` | `/api/v1/admin/configuration/values/{key}/{scope}` | Define/altera valor (version + reason) | permissão do escopo (CF-BR-010) |
| `DELETE` | `/api/v1/admin/configuration/values/{key}/{scope}` | Remove override (volta ao escopo superior) | permissão do escopo |
| `POST` | `/api/v1/admin/configuration/features/{flagKey}/rollout` | Ajusta rollout da flag | `config.platform.manage` |
| `POST` | `/api/v1/admin/configuration/secrets/{key}/rotate` | Aponta nova versão de SecretRef | `config.secret.manage` |

- Erros padronizados: `CF-ERR-001` (chave não definida), `CF-ERR-002` (valor fora do esquema), `CF-ERR-003` (escopo não permitido), `CF-ERR-004` (lockedAt), `CF-ERR-005` (conflito de versão), `CF-ERR-006` (valor secreto em claro rejeitado), `CF-ERR-007` (dual control pendente).

---

## 9. Integrações com o Foundation

| Domínio | Integração |
|---|---|
| **FD-001-01 IAM** | Permissões `config.*`, autenticação, identidade de serviço para SecretRef. |
| **FD-001-02 Organization** | Escopo ORGANIZATION, limites institucionais (referenciados, não duplicados). |
| **FD-001-04 Workflow Engine** | Parâmetros de SLA/lembretes; ConfigSnapshot pinado na instanciação (CF-BR-012). |
| **FD-001-05 Notification Center** | Parâmetros de retry, janelas, filas; alerta de mudanças críticas. |
| **FD-001-06 Audit Service** | Trilha de toda mudança e leitura administrativa (CF-BR-004). |
| **FD-001-09 Master Data** | Tipos de catálogo habilitados por organização (feature de governança). |

---

## 10. Requisitos Não Funcionais

| Categoria | Requisito |
|---|---|
| **Desempenho** | Resolução via cache com hit ratio ≥ 99%; P95 ≤ 20 ms por chave. |
| **Consistência** | Invalidação por evento P95 ≤ 5 s; fallback com repovoamento; sem fallback próprio nos módulos (CF-BR-003). |
| **Segurança** | Segredos só por referência (CF-BR-006); permissões por escopo; dual control em chaves críticas; valores sensíveis hasheados em eventos e auditoria. |
| **Confiabilidade** | Outbox na publicação; consumidores idempotentes; leitura degradada usa último valor conhecido + alerta. |
| **Observabilidade** | Métricas de hit ratio, mudanças por chave/ator, flags ativas por organização, falhas de validação. |
| **Governança** | Definições versionadas; remoção de chave exige depreciação com janela (v2). |

---

## 11. Restrições Arquiteturais

1. **Definição antes de uso:** nenhum código lê chave não registrada (CF-BR-001) — fim de "magic strings" espalhadas.
2. **Resolução centralizada:** módulos consomem valores resolvidos; nenhum módulo guarda cópia própria de configuração de comportamento.
3. **Sem segredo em claro:** tentativa de gravar valor em chave `SECRET_REF` é rejeitada e auditada (CF-ERR-006).
4. **Configuração acima de customização:** comportamento novo = nova chave documentada, não código condicional escondido.
5. **Multiempresa por construção:** escopo e isolamento explícitos em toda leitura/escrita.

---

## 12. Critérios de Conclusão do Módulo (MVP)

- [ ] Registro de ConfigDefinition com esquema, escopos, lockedAt e sensibilidade.
- [ ] Resolução por precedência PLATFORM→ORGANIZATION→MODULE→USER com default da definição.
- [ ] Cache Redis com invalidação por CF-EVT-002 e fallback.
- [ ] Feature flags com rollout `ALL/ORG_LIST/OFF` e avaliação server-side.
- [ ] SecretRef com rotação por API (sem valor em claro em lugar nenhum).
- [ ] Auditoria completa de mudanças e leituras administrativas.
- [ ] Permissões por escopo + dual control em chaves marcadas.
- [ ] Testes: validação de esquema, precedência, lockedAt, isolamento multiempresa, dual control, propagação de invalidação, rejeição de segredo em claro, snapshot pinado.

---

## 13. Matriz de Dependência com Outros Módulos

| Módulo | Tipo | Descrição |
|---|---|---|
| FD-001-01 IAM | Obrigatória | Permissões `config.*`, identidade de serviço. |
| FD-001-02 Organization | Obrigatória | Escopo de organização. |
| FD-001-04 Workflow Engine | Consumo | Parâmetros de processo + ConfigSnapshot. |
| FD-001-05 Notification Center | Consumo + alertas | Parâmetros de entrega; notificação de mudanças críticas. |
| FD-001-06 Audit Service | Produção de eventos | Trilha de mudanças/leituras. |
| FD-001-09 Master Data | Consumo | Tipos habilitados por organização. |
| Todos os módulos de negócio | Consumo | Parâmetros namespaced (ex.: `procurement.pr.*`). |

---

## 14. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Definições tipadas, escopos com precedência, lockedAt, feature flags ALL/ORG_LIST/OFF, SecretRef com rotação, cache com invalidação, auditoria, dual control, ConfigSnapshot. |
| **v2.0** | Rollout por percentual, depreciação de chaves com janela, config-as-code (definições via pacote de release com reconciliação), histórico de valores com diff visual. |
| **v3.0** | Ambientes por organização (sandbox de configuração), simulação de impacto ("o que muda se…"), políticas de aprovação via workflow para mudanças críticas. |

---

## 15. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-07-30 | Versão inicial aprovada: objetivo, fronteiras com Master Data/Organization/Notification, conceitos, escopos e precedência, modelos ConfigDefinition/ConfigValue, feature flags, regras CF-BR-001..012, eventos CF-EVT-001..005, APIs, integrações, NFRs, restrições, critérios de conclusão, matriz de dependência e roadmap. **Fecha a documentação dos 10 domínios do Foundation (ADR-011).** | Arquitetura Trino |
