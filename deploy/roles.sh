#!/bin/bash
set -euo pipefail
export DB_OWNER_PASSWORD="$(cat /run/secrets/owner_password)"
export DB_APP_PASSWORD="$(cat /run/secrets/app_password)"
psql -v ON_ERROR_STOP=1 --username postgres --dbname postgres <<'SQL'
\getenv owner_password DB_OWNER_PASSWORD
\getenv app_password DB_APP_PASSWORD
SELECT 'CREATE ROLE messanger_owner LOGIN' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname='messanger_owner') \gexec
SELECT 'CREATE ROLE messanger_app LOGIN' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname='messanger_app') \gexec
ALTER ROLE messanger_owner PASSWORD :'owner_password';
ALTER ROLE messanger_app PASSWORD :'app_password';
SELECT 'CREATE DATABASE messanger OWNER messanger_owner' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname='messanger') \gexec
REVOKE ALL ON DATABASE messanger FROM PUBLIC;
GRANT CONNECT ON DATABASE messanger TO messanger_app;
\connect messanger
ALTER SCHEMA public OWNER TO messanger_owner;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO messanger_app;
ALTER DEFAULT PRIVILEGES FOR ROLE messanger_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO messanger_app;
ALTER DEFAULT PRIVILEGES FOR ROLE messanger_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO messanger_app;
SQL
