#!/usr/bin/env bash
set -euo pipefail

wait_tcp() {
  local host=$1 port=$2 name=$3 attempts=${4:-90}
  echo "Waiting for ${name} at ${host}:${port}..."
  for _ in $(seq 1 "$attempts"); do
    if (exec 3<>"/dev/tcp/${host}/${port}") 2>/dev/null; then
      exec 3>&- 3<&- || true
      echo "${name} is reachable."
      return 0
    fi
    sleep 2
  done
  echo "${name} not reachable after ${attempts} attempts; starting anyway (app readiness will fail)." >&2
}

if [[ -v ERP_POSTGRES_PASSWORD ]]; then
  # Npgsql escapes a double quote inside a quoted connection string value by doubling it.
  password=${ERP_POSTGRES_PASSWORD//\"/\"\"}
  export ConnectionStrings__Default="Host=db;Port=5432;Database=erpmanagement;Username=crest;Password=\"${password}\""
  unset ERP_POSTGRES_PASSWORD password
fi

# Compose depends_on conditions provide ordered startup on docker; the TCP waits also
# cover orchestrators that ignore healthcheck conditions (e.g. podman-compose variants).
wait_tcp "${DB_HOST:-db}" "${DB_PORT:-5432}" "PostgreSQL"
wait_tcp "${RabbitMQ__HostName:-rabbitmq}" "${RabbitMQ__Port:-5672}" "RabbitMQ" 30

exec dotnet ErpManagement.Web.dll
