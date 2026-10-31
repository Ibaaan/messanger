#!/bin/bash
set -euo pipefail
install -d -m 700 -o postgres -g postgres /run/postgres-tls
install -m 600 -o postgres -g postgres /run/secrets/db_key /run/postgres-tls/server.key
install -m 644 -o postgres -g postgres /run/secrets/db_cert /run/postgres-tls/server.crt
exec /usr/local/bin/docker-entrypoint.sh postgres \
  -c ssl=on -c ssl_cert_file=/run/postgres-tls/server.crt -c ssl_key_file=/run/postgres-tls/server.key \
  -c hba_file=/etc/postgresql/messanger_hba.conf -c password_encryption=scram-sha-256
