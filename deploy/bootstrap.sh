#!/bin/sh
# Bootstrap do banco do piloto: aplica as migrations (scripts idempotentes do EF) como superuser
# e provisiona a role da aplicação. Executado por um container postgres:16 (tem psql).
set -e

export PGPASSWORD="${DB_PASSWORD:-trino}"
DB_HOST="${DB_HOST:-db}"
DB_USER="${DB_USER:-trino}"
DB_NAME="${DB_NAME:-trino}"

echo "bootstrap: aguardando o banco em ${DB_HOST}…"
until pg_isready -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; do
    sleep 1
done

for f in foundation materials procurement; do
    echo "bootstrap: aplicando ${f}.sql"
    psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 -f "/sql/${f}.sql"
done

# grants.sql lê as senhas das roles (app e worker) de settings de sessão (APP/WORKER_DB_PASSWORD).
echo "bootstrap: aplicando grants.sql"
psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -v ON_ERROR_STOP=1 \
    -c "SET trino.app_password = '$(printf "%s" "${APP_DB_PASSWORD:-apppw}" | sed "s/'/''/g")'" \
    -c "SET trino.worker_password = '$(printf "%s" "${WORKER_DB_PASSWORD:-workerpw}" | sed "s/'/''/g")'" \
    -f "/sql/grants.sql"

echo "bootstrap: concluído."
