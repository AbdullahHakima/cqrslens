# CqrsLens configuration

CqrsLens looks for `.cqrslens.json` at the scan root:

```bash
cqrslens .                 # reads ./.cqrslens.json when present
cqrslens src --config ci/cqrslens.json
```

`--config` points at an explicit file (relative to the current directory). CLI
flags override the matching config values; config is validated strictly, so an
unknown key or invalid value fails with exit code `1` instead of being ignored.

## Example

```json
{
  "$schema": "https://raw.githubusercontent.com/AbdullahHakima/cqrslens/main/cqrslens.schema.json",
  "analysis": "text",
  "markers": ["ICommand", "IQuery"],
  "include": ["src/**"],
  "exclude": [
    "**/Migrations/**",
    "**/Generated/**",
    "**/obj/**"
  ],
  "layers": {
    "Contoso.Domain": "Domain",
    "Contoso.Web": "Api"
  },
  "rules": {
    "MissingHandler": "error",
    "LayerViolation": "error",
    "CommandWithoutValidator": "warning",
    "AmbiguousProjectName": "off"
  }
}
```

## Settings

| Setting | Meaning |
| --- | --- |
| `analysis` | `text` (default) or `roslyn`. `--analysis` overrides it. |
| `markers` | Custom marker interfaces that count as request roots. `--markers` overrides the list. |
| `include` | Optional root-relative globs. When present, only matching source files are scanned. |
| `exclude` | Root-relative globs removed after `include`. |
| `layers` | Project-name to layer overrides; they replace the naming/folder classifier and feed the layer checks. |
| `rules` | Per-rule severity: `off`, `warning`, or `error`. |

Glob patterns use `/` separators and are matched case-insensitively:

- `**` crosses directory boundaries;
- `*` matches within one path segment;
- `?` matches one character.

For example, `**/Generated/**` excludes generated source in every folder, while
`src/**/*.cs` includes only C# files below `src/`.

## Rule codes

- `MissingHandler`
- `LayerViolation`
- `CommandWithoutValidator`
- `AmbiguousProjectName`

Turning a rule `off` removes its findings from the report and from `--strict`
evaluation. Promoting a rule to `error` makes `--strict` fail when that finding
exists.

## Projects and source filtering

`include` and `exclude` filter **source files**; project discovery still sees
all `.csproj` files so the project graph remains useful. Use `layers` to correct
a project's classification, or `exclude` generated source that should not affect
the CQRS map or checks.
