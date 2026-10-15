#!/usr/bin/env bash
# Validates everything that is not C#: workflows, YAML/JSON config against SchemaStore schemas,
# JSON syntax and Markdown. C# is covered by `dotnet format`, build and test.
# Needs network (schemas). The tools (actionlint, ajv, markdownlint-cli2, node) come from the devcontainer.
set -uo pipefail

cd "$(dirname "$0")/.."

schemas="$(mktemp -d)"
trap 'rm -rf "$schemas"' EXIT
failed=0

step() { printf '\n== %s ==\n' "$1"; }
run() { "$@" || failed=1; }

for tool in actionlint ajv markdownlint-cli2 node curl; do
  command -v "$tool" >/dev/null || { echo "Missing tool: $tool. Rebuild the devcontainer (tools are declared in .devcontainer/devcontainer.json)." >&2; exit 2; }
done

fetch() { curl -fsSL "$1" -o "$schemas/$2" || { echo "Could not download schema $1" >&2; failed=1; return 1; }; }
validate() { # <draft> <schema> <file>...
  local spec="$1" schema="$2"; shift 2
  for f in "$@"; do run ajv validate --spec="$spec" -c ajv-formats --strict=false -s "$schemas/$schema" -d "$f"; done
}

step "GitHub Actions workflows (actionlint)"
run actionlint

step "YAML and JSON config against schemas"
fetch https://json.schemastore.org/dependabot-2.0.json dependabot.json \
  && validate draft7 dependabot.json .github/dependabot.yml
fetch https://json.schemastore.org/github-issue-forms.json issue-forms.json \
  && validate draft7 issue-forms.json .github/ISSUE_TEMPLATE/bug_report.yml .github/ISSUE_TEMPLATE/feature_request.yml
fetch https://json.schemastore.org/github-issue-config.json issue-config.json \
  && validate draft7 issue-config.json .github/ISSUE_TEMPLATE/config.yml
fetch https://json.schemastore.org/claude-code-settings.json claude-settings.json \
  && validate draft7 claude-settings.json .claude/settings.json
fetch https://raw.githubusercontent.com/devcontainers/spec/main/schemas/devContainer.base.schema.json devcontainer.json \
  && validate draft2019 devcontainer.json .devcontainer/devcontainer.json

step "JSON syntax (VS Code, SDK pin, fixtures)"
for f in .vscode/*.json global.json tests/*/Fixtures/*.json; do
  node -e "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8'))" "$f" && echo "ok $f" || failed=1
done

step "Markdown (markdownlint)"
run markdownlint-cli2

printf '\n'
if [ "$failed" -ne 0 ]; then echo "VALIDATION FAILED"; exit 1; fi
echo "All config validation passed."
