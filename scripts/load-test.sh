#!/usr/bin/env bash
# Runs the k6 load scripts against a freshly published API and a clean database.
#
# ⚠️ Rate limits are raised for the duration, and that is stated in the baseline
# rather than hidden. Left at production values the limiter is the first thing
# the load hits, and the run reports the limiter's latency instead of the
# endpoint's — the first attempt failed 99.95% of its requests and produced a
# p95 for the 0.05% that got through. Whether the *limits* are right is a
# separate question, answered by the integration tests that exercise them.
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"

port="${SHELLWRIGHT_LOAD_PORT:-5199}"
database="${SHELLWRIGHT_LOAD_DATABASE:-shellwright_load}"
publish="${SHELLWRIGHT_LOAD_PUBLISH:-/tmp/shellwright-load-api}"

command -v k6 >/dev/null || { echo "k6 is not installed. See https://k6.io/docs/get-started/installation/"; exit 1; }

eval "$(bash scripts/dev-postgres.sh)"

admin_host=$(sed -n 's/.*Host=\([^;]*\).*/\1/Ip' <<<"$SHELLWRIGHT_TEST_PG_ADMIN")
admin_port=$(sed -n 's/.*Port=\([^;]*\).*/\1/Ip' <<<"$SHELLWRIGHT_TEST_PG_ADMIN")
admin_user=$(sed -n 's/.*Username=\([^;]*\).*/\1/Ip' <<<"$SHELLWRIGHT_TEST_PG_ADMIN")

psql -h "$admin_host" -p "$admin_port" -U "$admin_user" -qtA \
	-c "DROP DATABASE IF EXISTS $database WITH (FORCE)" >/dev/null
psql -h "$admin_host" -p "$admin_port" -U "$admin_user" -qtA \
	-c "CREATE DATABASE $database OWNER shellwright_migrator" >/dev/null
psql -h "$admin_host" -p "$admin_port" -U "$admin_user" -d "$database" -qtA >/dev/null <<SQL
ALTER SCHEMA public OWNER TO shellwright_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO shellwright_app;
SQL

base="Host=$admin_host;Port=$admin_port;Database=$database"
export SHELLWRIGHT_MIGRATION_CONNECTION="$base;Username=shellwright_migrator;Password=shellwright_migrator"
dotnet ef database update --project apps/api/Shellwright.Api >/dev/null

# Release, published, and started as a real process. `dotnet run` leaves the
# build server and the launcher in the measurement, and Debug turns off the
# optimisations the numbers are supposed to describe.
dotnet publish apps/api/Shellwright.Api -c Release -o "$publish" >/dev/null

export ASPNETCORE_URLS="http://127.0.0.1:$port"
export ASPNETCORE_ENVIRONMENT=Production
export Auth__SigningKey="$(head -c 32 /dev/urandom | base64)"
export Database__ConnectionString="$base;Username=shellwright_app;Password=shellwright_app;Maximum Pool Size=40"
export AssetStorage__Directory="${publish}-assets"
export Logging__LogLevel__Default=Warning
export RateLimits__ReadPerMinute=1000000
export RateLimits__WritePerMinute=1000000
export RateLimits__WriteBurst=1000000
export RateLimits__AuthPerMinute=1000000

dotnet "$publish/Shellwright.Api.dll" >/tmp/shellwright-load-api.log 2>&1 &
api=$!
trap 'kill "$api" 2>/dev/null || true' EXIT

for _ in $(seq 1 40); do
	curl -sf "http://127.0.0.1:$port/health/ready" >/dev/null 2>&1 && break
	sleep 1
done

curl -sf "http://127.0.0.1:$port/health/ready" >/dev/null || {
	echo "The API did not become ready. See /tmp/shellwright-load-api.log"
	exit 1
}

export SHELLWRIGHT_BASE_URL="http://127.0.0.1:$port"

status=0
for script in "$@"; do
	printf '\n\033[1m▸ %s\033[0m\n' "$script"
	k6 run "$script" || status=1
done

[ "$#" -eq 0 ] && {
	for script in tests/load/config-read.js tests/load/config-validate.js tests/load/config-save.js; do
		printf '\n\033[1m▸ %s\033[0m\n' "$script"
		k6 run "$script" || status=1
	done
}

exit "$status"
