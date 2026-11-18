#!/usr/bin/env bash
set -euo pipefail

DB_HOST="${DB_HOST:-db}"
DB_PORT="${DB_PORT:-5432}"

echo "Waiting for PostgreSQL at ${DB_HOST}:${DB_PORT}..."
for i in $(seq 1 60); do
  if (exec 3<>"/dev/tcp/${DB_HOST}/${DB_PORT}") 2>/dev/null; then
    exec 3>&- 3<&- || true
    echo "PostgreSQL is reachable."
    break
  fi
  if [ "$i" -eq 60 ]; then
    echo "PostgreSQL not reachable after 60 attempts; starting anyway (app will retry)." >&2
  fi
  sleep 2
done

exec dotnet ErpManagement.Web.dll