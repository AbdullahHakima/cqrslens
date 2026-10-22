#!/usr/bin/env bash
#
# Scans the pinned public corpus in validation/corpus.tsv and writes a
# reproducible results table plus full reports/SARIF/logs.
#
# Usage (after building the tool):
#   dotnet build CqrsLens.sln -c Release
#   bash scripts/validate-real-world.sh
#
# Environment overrides:
#   CQRSLENS_VALIDATION_OUTPUT  output directory (default: artifacts/validation)
#   CQRSLENS_VALIDATION_TOOL    path to cqrslens.dll (default: Release net8.0 build)
#   CQRSLENS_VALIDATION_KEEP=1  keep cloned repositories for inspection

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${CQRSLENS_VALIDATION_OUTPUT:-$repo_root/artifacts/validation}"
corpus="${CQRSLENS_VALIDATION_CORPUS:-$repo_root/validation/corpus.tsv}"
tool="${CQRSLENS_VALIDATION_TOOL:-$repo_root/src/CqrsLens/bin/Release/net8.0/cqrslens.dll}"
keep="${CQRSLENS_VALIDATION_KEEP:-0}"

if ! command -v git >/dev/null 2>&1; then
    echo "error: git is required" >&2
    exit 2
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: dotnet is required" >&2
    exit 2
fi
if [[ ! -f "$tool" ]]; then
    echo "error: cqrslens build not found at $tool" >&2
    echo "build it first: dotnet build CqrsLens.sln -c Release" >&2
    exit 2
fi
if [[ ! -f "$corpus" ]]; then
    echo "error: corpus not found at $corpus" >&2
    exit 2
fi

mkdir -p "$output/reports" "$output/logs" "$output/clones"
rm -rf "$output/reports"/* "$output/logs"/* "$output/clones"/*
results="$output/results.tsv"
markdown="$output/validation.md"
printf 'repository\turl\trequested_ref\tresolved_commit\tscan_path\tmarkers\tprojects\trequests\thandlers\tvalidators\tbehaviors\tdbcontexts\tcheck_errors\tcheck_warnings\tdiag_errors\tdiag_warnings\tdiag_info\tduration_ms\tstatus\treport\n' > "$results"

tool_version="$(dotnet "$tool" --version 2>/dev/null | sed 's/^CqrsLens //' || true)"
tool_version="${tool_version:-unknown}"
generated_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
failures=0

while IFS=$'\t' read -r name url ref subdir markers; do
    [[ -z "${name:-}" || "$name" == \#* ]] && continue
    [[ -z "${url:-}" || -z "${ref:-}" || -z "${subdir:-}" || -z "${markers:-}" ]] && {
        echo "error: malformed corpus row: $name" >&2
        exit 2
    }

    echo "==> $name @ $ref"
    clone_dir="$output/clones/$name"
    log="$output/logs/$name.log"
    report="$output/reports/$name.md"
    summary="$output/reports/$name-summary.md"
    sarif="$output/reports/$name.sarif"
    commit=""
    status="clone-failed"
    projects=0 requests=0 handlers=0 validators=0 behaviors=0 dbcontexts=0
    check_errors=0 check_warnings=0
    diag_errors=0 diag_warnings=0 diag_info=0
    duration_ms=0

    rm -rf "$clone_dir"
    mkdir -p "$clone_dir"
    if git -C "$clone_dir" init -q \
        && git -C "$clone_dir" remote add origin "$url" \
        && git -C "$clone_dir" fetch -q --depth 1 origin "$ref" \
        && git -C "$clone_dir" checkout -q --detach FETCH_HEAD; then
        commit="$(git -C "$clone_dir" rev-parse HEAD 2>/dev/null || true)"
    fi

    if [[ -n "$commit" ]]; then
        scan_path="$clone_dir/$subdir"
        if [[ ! -d "$scan_path" ]]; then
            status="missing-scan-path"
            failures=1
        else
            marker_args=()
            if [[ "$markers" != "-" ]]; then
                marker_args+=(--markers "$markers")
            fi

            start_ns="$(date +%s%N)"
            set +e
            dotnet "$tool" "$scan_path" \
                --analysis text \
                --ci-root "$clone_dir" \
                --sarif "$sarif" \
                --summary "$summary" \
                "${marker_args[@]}" \
                -o "$report" > "$log" 2>&1
            exit_code=$?
            set -e
            end_ns="$(date +%s%N)"
            duration_ms=$(( (end_ns - start_ns) / 1000000 ))
            if [[ "$exit_code" -eq 0 ]]; then
                status="ok"
            else
                status="scan-exit-$exit_code"
                failures=1
            fi
        fi
    else
        printf 'clone failed for %s @ %s\n' "$url" "$ref" > "$log"
        failures=1
    fi

    if [[ -f "$log" ]]; then
        scanned_line="$(grep 'Scanned .* project(s):' "$log" | tail -n1 | tr -d '\r' || true)"
        if [[ -n "$scanned_line" ]]; then
            parsed_counts="$(printf '%s\n' "$scanned_line" | sed -nE \
                's/.*Scanned ([0-9]+) project\(s\): ([0-9]+) request\(s\), ([0-9]+) handler\(s\), ([0-9]+) validator\(s\), ([0-9]+) behavior\(s\), ([0-9]+) DbContext\(s\).*/\1 \2 \3 \4 \5 \6/p')"
            if [[ -n "$parsed_counts" ]]; then
                read -r projects requests handlers validators behaviors dbcontexts <<< "$parsed_counts"
            fi
        fi

        checks_line="$(grep 'Architecture checks:' "$log" | tail -n1 | tr -d '\r' || true)"
        if [[ "$checks_line" =~ Architecture\ checks:\ ([0-9]+)\ error\(s\),\ ([0-9]+)\ warning\(s\) ]]; then
            check_errors="${BASH_REMATCH[1]}"
            check_warnings="${BASH_REMATCH[2]}"
        fi
    fi

    if [[ -f "$summary" ]]; then
        diag_line="$(awk '
            { sub(/\r$/, "") }
            found && length($0) > 0 { print; exit }
            /^### Scan diagnostics$/ { found = 1 }
        ' "$summary")"
        if [[ "$diag_line" =~ ^([0-9]+)\ error\(s\),\ ([0-9]+)\ warning\(s\),\ ([0-9]+)\ info\ item\(s\)\.$ ]]; then
            diag_errors="${BASH_REMATCH[1]}"
            diag_warnings="${BASH_REMATCH[2]}"
            diag_info="${BASH_REMATCH[3]}"
        fi
    fi

    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "$name" "$url" "$ref" "$commit" "$subdir" "$markers" \
        "$projects" "$requests" "$handlers" "$validators" "$behaviors" "$dbcontexts" \
        "$check_errors" "$check_warnings" \
        "$diag_errors" "$diag_warnings" "$diag_info" \
        "$duration_ms" "$status" "$report" >> "$results"

    printf '    %s projects, %s requests, %s handlers, %s validators, checks %s/%s, diagnostics %s/%s/%s, %ss, %s\n' \
        "$projects" "$requests" "$handlers" "$validators" \
        "$check_errors" "$check_warnings" \
        "$diag_errors" "$diag_warnings" "$diag_info" \
        "$((duration_ms / 1000))" "$status"
done < "$corpus"

{
    printf '# Real-world validation\n\n'
    printf -- '- Generated: `%s`\n' "$generated_at"
    printf -- '- CqrsLens: `%s`\n' "$tool_version"
    printf -- '- Corpus: `validation/corpus.tsv` (full commit SHAs)\n'
    printf -- '- Analysis: text scanner; no build or MSBuild evaluation\n'
    printf -- '- Strict gate: disabled (this is an informational corpus run)\n\n'
    printf '| Repository | Commit | Path | Markers | Projects | Requests | Handlers | Validators | Checks (E/W) | Diagnostics (E/W/I) | Time | Status |\n'
    printf '| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n'
    awk -F '\t' '
        NR == 1 { next }
        {
            url = $2
            sub(/\.git$/, "", url)
            printf "| [%s](%s) | `%s` | `%s` | `%s` | %s | %s | %s | %s | %s/%s | %s/%s/%s | %sms | %s |\n", \
                $1, url, substr($4, 1, 12), $5, $6, $7, $8, $9, \
                $10, $13, $14, $15, $16, $17, $18, $19
        }
    ' "$results"
    printf '\n'
    printf 'Full reports, SARIF, logs and the machine-readable `results.tsv` are in the workflow artifact.\n'
    printf 'See `docs/validation.md` for corpus rationale, limitations and how to add a repository.\n'
} > "$markdown"

if [[ "$keep" != "1" ]]; then
    rm -rf "$output/clones"
fi

echo
echo "Results: $markdown"
echo "Raw data: $results"

if [[ "$failures" -ne 0 ]]; then
    echo "error: one or more corpus scans failed" >&2
    exit 1
fi
