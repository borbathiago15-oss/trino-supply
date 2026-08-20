#!/usr/bin/env bash
# Popula uma instância NOVA do Trino Supply com dados de demonstração para o piloto:
# CNPJs, centros de custo, itens de EPI/fardamento com saldo, colaboradores, usuários com os
# três perfis e pedidos em situações diferentes (rascunho, aguardando aprovação, aprovado).
#
# Uso:
#   API_URL=https://sua-api.up.railway.app PROVISIONING_KEY=sua-chave ./deploy/seed-demo.sh
#
# Se a empresa JÁ existir, informe COMPANY_ID (e a senha do admin) para popular só os dados:
#   API_URL=... COMPANY_ID=<uuid> ADMIN_PASSWORD=<senha> ./deploy/seed-demo.sh
#
# Rode UMA vez, numa instância recém-criada. Requer apenas bash + curl.
set -euo pipefail

API="${API_URL:-http://localhost:5098}"
KEY="${PROVISIONING_KEY:-}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@trino.com}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Trino@2026}"
SENHA_PADRAO="${SENHA_PADRAO:-Trino@2026}"
B="$API/api/v1"

# A chave só é necessária para criar a empresa; com COMPANY_ID definido, populamos uma existente.
[ -n "$KEY" ] || [ -n "${COMPANY_ID:-}" ] || {
  echo "ERRO: defina PROVISIONING_KEY (para criar a empresa) ou COMPANY_ID (para popular uma existente)."; exit 1; }

campo() { grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d'"' -f4; }
req()   { curl -sS -X "$1" "$B$2" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' ${3:+-d "$3"}; }
post()  { req POST "$1" "$2"; }
put()   { req PUT  "$1" "$2"; }
etapa() { printf '\n\033[1m%s\033[0m\n' "$*"; }

etapa "1/8 Empresa e administrador…"
if [ -n "${COMPANY_ID:-}" ]; then
  # Empresa já existe (ex.: criada antes pelo painel/curl): apenas populamos os dados nela.
  COMPANY="$COMPANY_ID"
  echo "   usando empresa existente"
else
  COMPANY=$(curl -sS -X POST "$B/companies" -H 'Content-Type: application/json' -H "X-Provisioning-Key: $KEY" \
    -d "{\"legalName\":\"Grupo Trino\",\"taxId\":\"11.111.111/0001-11\",\"adminSubject\":\"admin\",
         \"adminEmail\":\"$ADMIN_EMAIL\",\"adminName\":\"Administrador\",\"adminPassword\":\"$ADMIN_PASSWORD\"}" \
    | campo companyId)
  [ -n "$COMPANY" ] || { echo "ERRO: não foi possível criar a empresa. Confira API_URL e PROVISIONING_KEY."; exit 1; }
fi
TOKEN=$(curl -sS -X POST "$B/auth/login" -H 'Content-Type: application/json' \
  -d "{\"companyId\":\"$COMPANY\",\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | campo accessToken)
[ -n "$TOKEN" ] || { echo "ERRO: login do admin falhou."; exit 1; }
echo "   empresa: $COMPANY"

etapa "2/8 Unidades de medida…"
for u in '{"code":"un","name":"Unidade","dimension":"contagem","factorToBase":1}' \
         '{"code":"par","name":"Par","dimension":"contagem","factorToBase":1}' \
         '{"code":"cx","name":"Caixa","dimension":"contagem","factorToBase":1}'; do
  post /materials/units "$u" >/dev/null; done
echo "   un, par, cx"

etapa "3/8 CNPJs do grupo e centros de custo vinculados…"
post /purchases/paying-companies '{"code":"TRINO-SP","legalName":"Trino Serviços SP Ltda","taxId":"11.111.111/0001-11"}' >/dev/null
post /purchases/paying-companies '{"code":"TRINO-MG","legalName":"Trino Serviços MG Ltda","taxId":"22.222.222/0001-22"}' >/dev/null
post /purchases/cost-centers '{"code":"CC-101","name":"Obra Norte","payingCompanyCode":"TRINO-SP"}' >/dev/null
post /purchases/cost-centers '{"code":"CC-102","name":"Obra Sul","payingCompanyCode":"TRINO-SP"}' >/dev/null
post /purchases/cost-centers '{"code":"CC-201","name":"Manutenção MG","payingCompanyCode":"TRINO-MG"}' >/dev/null
post /purchases/cost-centers '{"code":"CC-900","name":"Administrativo","payingCompanyCode":"TRINO-SP"}' >/dev/null
echo "   2 CNPJs, 4 centros"

etapa "4/8 Catálogo de itens (EPI e fardamento)…"
post /materials/items '{"code":"BOTINA-40","name":"Botina de segurança nº 40","baseUnitCode":"par","group":"EPI","ca":"38123"}' >/dev/null
post /materials/items '{"code":"BOTINA-42","name":"Botina de segurança nº 42","baseUnitCode":"par","group":"EPI","ca":"38123"}' >/dev/null
post /materials/items '{"code":"LUVA-M","name":"Luva de raspa M","baseUnitCode":"par","group":"EPI","ca":"41577"}' >/dev/null
post /materials/items '{"code":"CAPACETE","name":"Capacete classe B","baseUnitCode":"un","group":"EPI","ca":"31469"}' >/dev/null
post /materials/items '{"code":"OCULOS","name":"Óculos de proteção","baseUnitCode":"un","group":"EPI","ca":"12563"}' >/dev/null
post /materials/items '{"code":"CAMISA-M","name":"Camisa de uniforme M","baseUnitCode":"un","group":"Fardamento"}' >/dev/null
post /materials/items '{"code":"CALCA-42","name":"Calça de uniforme 42","baseUnitCode":"un","group":"Fardamento"}' >/dev/null
echo "   7 itens"

etapa "5/8 Entrada de estoque em lote + pontos de reposição…"
post /materials/movements/batch '{"direction":1,"reason":"Carga inicial do piloto","lines":[
  {"itemCode":"BOTINA-40","quantity":25},{"itemCode":"BOTINA-42","quantity":18},
  {"itemCode":"LUVA-M","quantity":60},{"itemCode":"CAPACETE","quantity":30},
  {"itemCode":"OCULOS","quantity":40},{"itemCode":"CAMISA-M","quantity":50},
  {"itemCode":"CALCA-42","quantity":12}]}' >/dev/null
put /materials/items/BOTINA-40/replenishment '{"minLevel":10,"maxLevel":40}' >/dev/null
put /materials/items/CALCA-42/replenishment  '{"minLevel":20,"maxLevel":60}' >/dev/null
echo "   saldo carregado (CALCA-42 já nasce abaixo do mínimo → aparece em Reposição)"

etapa "6/8 Colaboradores…"
post /materials/collaborators '{"name":"José da Silva","registration":"M-1001","costCenterCode":"CC-101","companyCode":"TRINO-SP","admissionDate":"2026-02-10"}' >/dev/null
post /materials/collaborators '{"name":"Maria Souza","registration":"M-1002","costCenterCode":"CC-102","companyCode":"TRINO-SP","admissionDate":"2026-03-01"}' >/dev/null
post /materials/collaborators '{"name":"Carlos Pereira","registration":"M-2001","costCenterCode":"CC-201","companyCode":"TRINO-MG","admissionDate":"2025-11-20"}' >/dev/null
echo "   3 colaboradores"

etapa "7/8 Usuários e perfis…"
ROLES=$(req GET /roles "")
role_id() { echo "$ROLES" | tr '}' '\n' | grep "\"name\":\"$1\"" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4; }
R_JUNIOR=$(role_id "Master Junior"); R_PLENO=$(role_id "Pleno")
criar_usuario() { # nome_login  email  nome_exibicao  role_id  centros_json
  local uid; uid=$(post /users "{\"subject\":\"$1\",\"email\":\"$2\",\"displayName\":\"$3\",\"password\":\"$SENHA_PADRAO\"}" | campo userId)
  [ -n "$4" ] && post "/users/$uid/roles" "{\"roleId\":\"$4\"}" >/dev/null
  [ -n "${5:-}" ] && put "/users/$uid/cost-centers" "{\"codes\":$5}" >/dev/null
  echo "$uid"
}
criar_usuario aprovador1 aprovador1@trino.com "Ana (aprovadora nível 1)" "$R_JUNIOR" >/dev/null
criar_usuario aprovador2 aprovador2@trino.com "Bruno (aprovador nível 2)" "$R_JUNIOR" >/dev/null
criar_usuario junior     junior@trino.com     "Júnior (só CC-101)"        "$R_JUNIOR" '["CC-101"]' >/dev/null
criar_usuario pleno      pleno@trino.com      "Paula (perfil Pleno)"      "$R_PLENO"  >/dev/null
echo "   aprovador1, aprovador2, junior (escopado em CC-101), pleno"

etapa "8/8 Pedidos de exemplo…"
# $1 CNPJ  $2 centro  $3 justificativa  $4 linhas  $5 aprovador nível 1 (padrão: aprovador1)
novo_pedido() { post /purchases/requisitions "{\"payingCompanyCode\":\"$1\",\"costCenterCode\":\"$2\",\"priority\":\"Normal\",
  \"justification\":\"$3\",\"approverLevel1Subject\":\"${5:-aprovador1}\",\"approverLevel2Subject\":\"aprovador2\",
  \"lines\":$4}" | campo requisitionId; }
novo_pedido TRINO-SP CC-101 "Reposição de botinas da Obra Norte" '[{"itemCode":"BOTINA-40","quantity":5,"unit":"par"}]' >/dev/null
P2=$(novo_pedido TRINO-SP CC-102 "Uniformes para novos contratados"   '[{"itemCode":"CAMISA-M","quantity":10,"unit":"un"}]')
P3=$(novo_pedido TRINO-MG CC-201 "Capacetes para equipe de manutenção" '[{"itemCode":"CAPACETE","quantity":40,"unit":"un"}]')
# Dois pedidos com o JÚNIOR como aprovador de nível 1, em centros diferentes: ele só enxerga o do
# CC-101 (o de CC-201 fica invisível para ele) — é a demonstração do escopo por centro.
P4=$(novo_pedido TRINO-SP CC-101 "EPI para a Obra Norte (aprovação do Júnior)"    '[{"itemCode":"LUVA-M","quantity":6,"unit":"par"}]' junior)
P5=$(novo_pedido TRINO-MG CC-201 "EPI da Manutenção MG (fora do escopo do Júnior)" '[{"itemCode":"OCULOS","quantity":5,"unit":"un"}]' junior)
for id in "$P2" "$P3" "$P4" "$P5"; do post "/purchases/requisitions/$id/submit" "" >/dev/null; done
echo "   1 rascunho + 4 aguardando aprovação (2 deles com o Júnior como aprovador)"

cat <<FIM

===========================================================
  Dados de demonstração criados.
===========================================================
  Company ID : $COMPANY
  (copie esse código: ele é pedido na tela de login)

  LOGINS (senha: $SENHA_PADRAO)
  --------------------------------------------------------
  $ADMIN_EMAIL       Master — acesso total
  aprovador1@trino.com  aprova nível 1 (veja a Central de Aprovação)
  aprovador2@trino.com  aprova nível 2
  junior@trino.com      Master Junior — SÓ enxerga o centro CC-101
  pleno@trino.com       Pleno — módulos liberados

  ROTEIRO SUGERIDO
  --------------------------------------------------------
  1. Entre como aprovador1 → Central de Aprovação → aprove
     "Uniformes para novos contratados"; entre como aprovador2
     e aprove de novo: como HÁ saldo, o pedido é atendido pelo
     estoque e dá baixa automática ("Atendido pelo estoque"),
     e o saldo de CAMISA-M cai de 50 para 40.
  2. Faça o mesmo com "Capacetes" (40 un, mais do que os 30 em
     estoque): ele fica Aprovado e segue para compra — emita a OC
     em Pedido e baixe o PDF.
  3. Entre como junior@trino.com: ele é aprovador de DOIS pedidos,
     mas a Central mostra só o do CC-101. O de CC-201 não aparece
     para ele — é o escopo por centro de custo em ação.
  4. Em Entregas (EPI) → baixa para José da Silva → baixe a
     Ficha de Entrega em PDF para assinatura.
  5. Dashboard de Estoque → CALCA-42 aparece em "Reposição sugerida";
     gere o pedido a partir dela.
===========================================================
FIM
