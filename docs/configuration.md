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
  "render": "mermaid",
  "watermark": "CONTOSO ARCHITECTURE",
  "markers": ["ICommand", "IQuery"],
  "handlerMarkers": ["ICommandHandler", "IQueryHandler"],
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
| `render` | `mermaid` (default, browser-rendered via Mermaid.js) or `svg` (pre-rendered inline SVG, zero JavaScript). `--render` overrides it. |
| `watermark` | Optional custom watermark text rendered as block ASCII art in the console and embedded as a watermark stamp in Markdown, HTML, and JSON reports. `--watermark` overrides it. |
| `markers` | Custom marker interfaces that count as request roots. `--markers` overrides the list. |
| `handlerMarkers` | Custom handler-root interfaces for wrappers supplied by an external package. Source-declared wrappers are followed automatically. `--handler-markers` overrides the list. |
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
- `MultipleHandlers`
- `LayerViolation`
- `ApplicationLayerViolation`
- `CircularProjectDependency`
- `CommandWithoutValidator`
- `OrphanValidator`
- `OrphanNotification`
- `AmbiguousProjectName`

Turning a rule `off` removes its findings from the report and from `--strict`
evaluation. Promoting a rule to `error` makes `--strict` fail when that finding
exists.

## Projects and source filtering

`include` and `exclude` filter **source files**; project discovery still sees
all `.csproj` files so the project graph remains useful. Use `layers` to correct
a project's classification, or `exclude` generated source that should not affect
the CQRS map or checks.

## External handler wrappers

Source-declared handler abstractions are traversed automatically:

```csharp
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand> { }
public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand> { }
```

Some applications instead use wrappers from a package, for example
`Ardalis.SharedKernel.ICommandHandler<TCommand, TResult>`. Because that interface
is not in the scanned source, name it explicitly as a handler root:

```json
{
  "markers": ["ICommand", "IQuery"],
  "handlerMarkers": ["ICommandHandler", "IQueryHandler"]
}
```

or on the command line:

```bash
cqrslens . --markers ICommand,IQuery --handler-markers ICommandHandler,IQueryHandler
```

CqrsLens then reads the first generic argument as the request type, matching the
MediatR convention. Explicit configuration avoids guessing from a type name; the
default scan is unchanged.

FastEndpoints validators do not need configuration: `Validator<T>` is recognized
as a validator root and its first generic argument is linked using the same rules
as FluentValidation validators.
