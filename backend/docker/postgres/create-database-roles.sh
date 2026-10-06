#!/bin/sh
set -eu

read_secret() {
    tr -d '\r\n' < "$1"
}

database_host="${POSTGRES_HOST:-postgres}"
database_port="${POSTGRES_PORT:-5432}"
database_name="${POSTGRES_DATABASE:-inventory}"
admin_user="${POSTGRES_ADMIN_USER:-postgres}"
admin_password_file="${POSTGRES_ADMIN_PASSWORD_FILE:-/run/secrets/postgres_admin_password}"
owner_password_file="${POSTGRES_OWNER_PASSWORD_FILE:-/run/secrets/postgres_owner_password}"
app_password_file="${POSTGRES_APP_PASSWORD_FILE:-/run/secrets/postgres_app_password}"

export PGPASSWORD="$(read_secret "$admin_password_file")"
owner_password="$(read_secret "$owner_password_file")"
app_password="$(read_secret "$app_password_file")"

psql \
    --host="$database_host" \
    --port="$database_port" \
    --username="$admin_user" \
    --dbname="$database_name" \
    --set=ON_ERROR_STOP=1 \
    --set=owner_password="$owner_password" \
    --set=app_password="$app_password" <<'SQL'
SELECT format(
    'CREATE ROLE inventory_owner LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L',
    :'owner_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'inventory_owner')
\gexec

SELECT format(
    'CREATE ROLE inventory_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L',
    :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'inventory_app')
\gexec

ALTER ROLE inventory_owner
    WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
    PASSWORD :'owner_password';
ALTER ROLE inventory_app
    WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
    PASSWORD :'app_password';

ALTER DATABASE inventory OWNER TO inventory_owner;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO inventory_owner;
GRANT USAGE ON SCHEMA public TO inventory_app;
GRANT CONNECT ON DATABASE inventory TO inventory_app;

ALTER DEFAULT PRIVILEGES FOR ROLE inventory_owner IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO inventory_app;
ALTER DEFAULT PRIVILEGES FOR ROLE inventory_owner IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO inventory_app;

GRANT SELECT, INSERT, UPDATE, DELETE
    ON ALL TABLES IN SCHEMA public TO inventory_app;
GRANT USAGE, SELECT, UPDATE
    ON ALL SEQUENCES IN SCHEMA public TO inventory_app;
SQL
