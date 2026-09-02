#!/usr/bin/env bash
# Regenerates the OpenAPI document and the TypeScript client from it.
#
# Two steps, both from the API's own route table:
#
#   apps/api  ->  packages/api-client/openapi/v1.json
#             ->  packages/api-client/src/generated/v1.ts
#
# ⚠️ Both outputs are committed, and CI fails when either is stale. That is
# deliberate: it puts a change to an endpoint and the client's view of that
# endpoint in the same diff, where a reviewer sees them together. Generating at
# build time instead lets the two drift between releases and disagree first in
# production.
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"

# The exporter starts the host to materialise the route table, so it needs
# configuration to be *present* — it never opens a connection or signs a token.
export Auth__SigningKey="${Auth__SigningKey:-$(head -c 32 /dev/urandom | base64)}"
export Database__ConnectionString="${Database__ConnectionString:-Host=localhost;Database=unused}"
export AssetStorage__Directory="${AssetStorage__Directory:-/tmp/shellwright-openapi}"
export Logging__LogLevel__Default=Warning

dotnet run --project apps/api/Shellwright.Api -- \
	--export-openapi "$root/packages/api-client/openapi/v1.json" >/dev/null

# ⚠️ Formatted here rather than exempted from the formatter.
#
# Prettier checks every JSON file in the repository, and the serialiser's
# indentation is not quite its own. Adding the file to .prettierignore would
# work and would also mean the one artefact a reviewer reads most closely is
# the one nothing keeps tidy. Formatting during generation keeps both checks
# agreeing about the same committed bytes.
pnpm exec prettier --write packages/api-client/openapi/v1.json >/dev/null

pnpm --filter @shellwright/api-client generate
