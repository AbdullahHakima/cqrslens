using System.Text.Json;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class ArchitectureDiffTests : IDisposable
{
    private readonly string _root;

    public ArchitectureDiffTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static (int Code, string Stdout, string Stderr) Run(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private string CreateCleanFixture(string folderName)
    {
        var dir = Path.Combine(_root, folderName);
        Directory.CreateDirectory(dir);
        WriteFile(dir, "App/App.Application.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFile(dir, "App/Ping.cs", """
            using MediatR;

            namespace App.Application;

            public sealed record PingQuery : IRequest<int>;
            public sealed class PingQueryHandler : IRequestHandler<PingQuery, int>
            {
                public Task<int> Handle(PingQuery request, CancellationToken ct) => Task.FromResult(1);
            }
            """);
        return dir;
    }

    [Fact]
    public void IdenticalScans_ReportNoChanges_InMarkdownAndJson()
    {
        var scanDir = CreateCleanFixture("app");
        var baseJson = Path.Combine(_root, "base.json");
        var reportOut = Path.Combine(_root, "ARCHITECTURE.md");
        var diffMd = Path.Combine(_root, "diff.md");
        var diffJson = Path.Combine(_root, "diff.json");

        Assert.Equal(
            ExitCodes.Success,
            Run(new[] { scanDir, "-o", baseJson, "--generated-at", "2026-10-25T12:00:00Z" }).Code);

        var mdResult = Run(new[]
        {
            scanDir,
            "-o", reportOut,
            "--diff", baseJson,
            "--diff-output", diffMd,
            "--generated-at", "2026-10-25T12:05:00Z",
        });

        Assert.Equal(ExitCodes.Success, mdResult.Code);
        Assert.Contains("Architecture diff: no changes detected.", mdResult.Stdout);
        Assert.Contains($"Diff written to {diffMd}", mdResult.Stdout);

        var markdown = File.ReadAllText(diffMd);
        Assert.StartsWith(ArchitectureDiffer.CommentMarker, markdown);
        Assert.Contains("## CqrsLens Architecture Diff", markdown);
        Assert.Contains("No architecture changes detected", markdown);
        Assert.Contains("| Requests (commands + queries) | 1 | 1 | 0 |", markdown);

        var jsonResult = Run(new[]
        {
            scanDir,
            "-o", reportOut,
            "--diff", baseJson,
            "--diff-output", diffJson,
            "--generated-at", "2026-10-25T12:05:00Z",
        });

        Assert.Equal(ExitCodes.Success, jsonResult.Code);
        using var doc = JsonDocument.Parse(File.ReadAllText(diffJson));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(doc.RootElement.GetProperty("hasChanges").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("requests").GetProperty("delta").GetInt32());
    }

    [Fact]
    public void FolderDiffAndJsonDiff_ProduceByteIdenticalFullSurfaceDiff()
    {
        var baseDir = Path.Combine(_root, "base-repo", "Shop");
        var headDir = Path.Combine(_root, "head-repo", "Shop");

        // --- Base repository state ---
        WriteFile(baseDir, "Shop.Application/Shop.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="MediatR" Version="12.0.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile(baseDir, "Shop.Application/Features.cs", """
            using MediatR;
            using Microsoft.EntityFrameworkCore;

            namespace Shop.Application;

            public sealed record GetOrderQuery(int Id) : IRequest<int>;
            public sealed class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, int>
            {
            }

            public sealed record LegacySyncCommand : IRequest;

            public sealed class Order
            {
                public int Id { get; set; }
            }

            public sealed class ShopDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
            }
            """);
        WriteFile(baseDir, "Shop.Application/Dispatcher.cs", """
            using MediatR;

            namespace Shop.Application;

            public sealed class Dispatcher
            {
                private readonly IMediator _mediator;
                public Dispatcher(IMediator mediator) => _mediator = mediator;
                public Task Handle() => _mediator.Send(new GetOrderQuery(1));
            }
            """);

        // --- Head repository state ---
        WriteFile(headDir, "Shop.Domain/Shop.Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"></Project>
            """);
        WriteFile(headDir, "Shop.Application/Shop.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Shop.Domain\Shop.Domain.csproj" />
                <PackageReference Include="MediatR" Version="12.0.0" />
                <PackageReference Include="FluentValidation" Version="11.9.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile(headDir, "Shop.Application/Features.cs", """
            using FluentValidation;
            using MediatR;
            using Microsoft.EntityFrameworkCore;

            namespace Shop.Application;

            // Modified request: response changed int -> OrderDto, validator added
            public sealed record OrderDto(int Id);
            public sealed record GetOrderQuery(int Id) : IRequest<OrderDto>;
            public sealed class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
            {
            }
            public sealed class GetOrderQueryValidator : AbstractValidator<GetOrderQuery>
            {
            }

            // Added request + handler + validator
            public sealed record CreateOrderCommand(string Sku) : IRequest<int>;
            public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
            {
            }
            public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
            {
            }

            // Added command without validator (triggers new CommandWithoutValidator warning)
            public sealed record CancelOrderCommand(int Id) : IRequest;
            public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
            {
            }

            // Added pipeline behavior
            public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : notnull
            {
            }

            public sealed class Order
            {
                public int Id { get; set; }
            }

            public sealed class Invoice
            {
                public int Id { get; set; }
            }

            public sealed class ShopDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
                public DbSet<Invoice> Invoices => Set<Invoice>();
            }
            """);
        WriteFile(headDir, "Shop.Application/Dispatcher.cs", """
            using MediatR;

            namespace Shop.Application;

            public sealed class Dispatcher
            {
                private readonly IMediator _mediator;
                public Dispatcher(IMediator mediator) => _mediator = mediator;
                public async Task Handle()
                {
                    await _mediator.Send(new GetOrderQuery(1));
                    await _mediator.Send(new CreateOrderCommand("sku"));
                    await _mediator.Send(new CancelOrderCommand(1));
                }
            }
            """);

        var baseJson = Path.Combine(_root, "base-architecture.json");
        Assert.Equal(
            ExitCodes.Success,
            Run(new[] { baseDir, "-o", baseJson, "--generated-at", "2026-10-25T10:00:00Z" }).Code);

        var reportOut = Path.Combine(_root, "head-ARCHITECTURE.md");
        var diffFromJsonMd = Path.Combine(_root, "diff-from-json.md");
        var diffFromDirMd = Path.Combine(_root, "diff-from-dir.md");
        var diffJsonOut = Path.Combine(_root, "diff.json");
        var summaryOut = Path.Combine(_root, "summary.md");

        var fromJsonRun = Run(new[]
        {
            headDir,
            "-o", reportOut,
            "--diff", baseJson,
            "--diff-output", diffFromJsonMd,
            "--summary", summaryOut,
            "--generated-at", "2026-10-25T11:00:00Z",
        });

        var fromDirRun = Run(new[]
        {
            headDir,
            "-o", reportOut,
            "--diff", baseDir,
            "--diff-output", diffFromDirMd,
            "--generated-at", "2026-10-25T11:00:00Z",
        });

        var jsonDiffRun = Run(new[]
        {
            headDir,
            "-o", reportOut,
            "--diff", baseJson,
            "--diff-output", diffJsonOut,
            "--generated-at", "2026-10-25T11:00:00Z",
        });

        Assert.Equal(ExitCodes.Success, fromJsonRun.Code);
        Assert.Equal(ExitCodes.Success, fromDirRun.Code);
        Assert.Equal(ExitCodes.Success, jsonDiffRun.Code);

        var mdFromJson = File.ReadAllText(diffFromJsonMd);
        var mdFromDir = File.ReadAllText(diffFromDirMd);
        Assert.Equal(mdFromJson, mdFromDir);

        // Verify PR comment marker and key sections
        Assert.StartsWith("<!-- cqrslens-architecture-diff -->", mdFromJson);
        Assert.Contains("## CqrsLens Architecture Diff", mdFromJson);
        Assert.Contains("| Projects | 1 | 2 | +1 |", mdFromJson);
        Assert.Contains("| Requests (commands + queries) | 2 | 3 | +1 |", mdFromJson);
        Assert.Contains("| Handlers | 1 | 3 | +2 |", mdFromJson);
        Assert.Contains("| Validators | 0 | 2 | +2 |", mdFromJson);
        Assert.Contains("| Pipeline behaviors | 0 | 1 | +1 |", mdFromJson);
        Assert.Contains("| Check errors | 1 | 0 | -1 |", mdFromJson);

        // Verify Architecture Checks delta (LegacySyncCommand MissingHandler resolved, CancelOrderCommand warning new)
        Assert.Contains("#### New findings (1)", mdFromJson);
        Assert.Contains("CancelOrderCommand", mdFromJson);
        Assert.Contains("#### Resolved findings (2)", mdFromJson);
        Assert.Contains("LegacySyncCommand", mdFromJson);

        // Verify CQRS Delta Mermaid diagram and tables
        Assert.Contains("```mermaid", mdFromJson);
        Assert.Contains("accTitle: CQRS diff map", mdFromJson);
        Assert.Contains(":::clsAdded", mdFromJson);
        Assert.Contains(":::clsModified", mdFromJson);
        Assert.Contains(":::clsRemoved", mdFromJson);
        Assert.Contains("#### Added requests (2)", mdFromJson);
        Assert.Contains("+ CreateOrderCommand", mdFromJson);
        Assert.Contains("+ CancelOrderCommand", mdFromJson);
        Assert.Contains("#### Modified requests (1)", mdFromJson);
        Assert.Contains("~ GetOrderQuery", mdFromJson);
        Assert.Contains("Response: int → OrderDto", mdFromJson);
        Assert.Contains("+Validator: GetOrderQueryValidator", mdFromJson);
        Assert.Contains("#### Removed requests (1)", mdFromJson);
        Assert.Contains("- LegacySyncCommand", mdFromJson);
        Assert.Contains("#### Pipeline behaviors", mdFromJson);
        Assert.Contains("LoggingBehavior", mdFromJson);

        // Verify Project & Layer and DbContext changes
        Assert.Contains("### Project & Layer Changes", mdFromJson);
        Assert.Contains("Added project **Shop.Domain**", mdFromJson);
        Assert.Contains("Modified project **Shop.Application**", mdFromJson);
        Assert.Contains("+refs: Shop.Domain", mdFromJson);
        Assert.Contains("+packages: FluentValidation", mdFromJson);
        Assert.Contains("### Data Access Changes (DbContexts)", mdFromJson);
        Assert.Contains("+DbSets: Invoices -> Invoice", mdFromJson);

        // Verify summary includes diff section
        var summary = File.ReadAllText(summaryOut);
        Assert.Contains("### Architecture diff", summary);
        Assert.Contains("+2/-1/~1 request(s)", summary);

        // Verify JSON diff structure
        using var doc = JsonDocument.Parse(File.ReadAllText(diffJsonOut));
        var root = doc.RootElement;
        Assert.True(root.GetProperty("hasChanges").GetBoolean());
        Assert.Equal(2, root.GetProperty("requests").GetProperty("added").GetArrayLength());
        Assert.Equal(1, root.GetProperty("requests").GetProperty("modified").GetArrayLength());
        Assert.Equal(1, root.GetProperty("requests").GetProperty("removed").GetArrayLength());
        Assert.Equal(1, root.GetProperty("checks").GetProperty("newFindings").GetArrayLength());
        Assert.Equal(2, root.GetProperty("checks").GetProperty("resolvedFindings").GetArrayLength());
    }

    [Fact]
    public void LineNumberShifts_DoNotCreateFalseCheckFindingDiffs()
    {
        var baseDir = Path.Combine(_root, "base");
        var headDir = Path.Combine(_root, "head");

        WriteFile(baseDir, "App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFile(baseDir, "PingCommand.cs", """
            namespace App;
            public sealed record PingCommand : IRequest<int>;
            """);

        WriteFile(headDir, "App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFile(headDir, "PingCommand.cs", """
            // Added header comments
            // Line 2
            // Line 3
            // Line 4
            namespace App;

            public sealed record PingCommand : IRequest<int>;
            """);

        var diffMd = Path.Combine(_root, "diff.md");
        var reportOut = Path.Combine(_root, "ARCHITECTURE.md");

        var result = Run(new[]
        {
            headDir,
            "-o", reportOut,
            "--diff", baseDir,
            "--diff-output", diffMd,
        });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("Architecture diff: no changes detected.", result.Stdout);
        var md = File.ReadAllText(diffMd);
        Assert.Contains("No change in architecture check findings", md);
    }

    [Fact]
    public void InvalidDiffInputs_AreReportedAsUsageErrors()
    {
        var scanDir = CreateCleanFixture("valid");
        var reportOut = Path.Combine(_root, "ARCHITECTURE.md");
        var diffOut = Path.Combine(_root, "diff.md");

        // 1. --diff-output without --diff
        var noDiff = Run(new[] { scanDir, "-o", reportOut, "--diff-output", diffOut });
        Assert.Equal(ExitCodes.Usage, noDiff.Code);
        Assert.Contains("--diff-output requires --diff", noDiff.Stderr);

        // 2. Missing diff file/folder
        var missingDiff = Run(new[] { scanDir, "-o", reportOut, "--diff", Path.Combine(_root, "missing.json") });
        Assert.Equal(ExitCodes.Usage, missingDiff.Code);
        Assert.Contains("diff path not found", missingDiff.Stderr);

        // 3. Empty diff file
        var emptyJson = Path.Combine(_root, "empty.json");
        File.WriteAllText(emptyJson, "   ");
        var emptyRun = Run(new[] { scanDir, "-o", reportOut, "--diff", emptyJson });
        Assert.Equal(ExitCodes.Usage, emptyRun.Code);
        Assert.Contains("diff file is empty", emptyRun.Stderr);

        // 4. Missing schemaVersion
        var noSchemaJson = Path.Combine(_root, "no-schema.json");
        File.WriteAllText(noSchemaJson, "{}");
        var noSchemaRun = Run(new[] { scanDir, "-o", reportOut, "--diff", noSchemaJson });
        Assert.Equal(ExitCodes.Usage, noSchemaRun.Code);
        Assert.Contains("missing schemaVersion", noSchemaRun.Stderr);

        // 5. Unsupported schemaVersion
        var badSchemaJson = Path.Combine(_root, "bad-schema.json");
        File.WriteAllText(badSchemaJson, "{\"schemaVersion\": 99}");
        var badSchemaRun = Run(new[] { scanDir, "-o", reportOut, "--diff", badSchemaJson });
        Assert.Equal(ExitCodes.Usage, badSchemaRun.Code);
        Assert.Contains("unsupported diff JSON schema version 99", badSchemaRun.Stderr);

        // 6. Diff directory without .csproj
        var emptyDir = Path.Combine(_root, "empty-dir");
        Directory.CreateDirectory(emptyDir);
        var emptyDirRun = Run(new[] { scanDir, "-o", reportOut, "--diff", emptyDir });
        Assert.Equal(ExitCodes.Usage, emptyDirRun.Code);
        Assert.Contains("no .csproj files found under diff directory", emptyDirRun.Stderr);

        // 7. Colliding --diff-output and -o
        var baseJson = Path.Combine(_root, "base.json");
        Run(new[] { scanDir, "-o", baseJson });
        var colliding = Run(new[] { scanDir, "-o", reportOut, "--diff", baseJson, "--diff-output", reportOut });
        Assert.Equal(ExitCodes.Usage, colliding.Code);
        Assert.Contains("--diff-output and the other output paths must be different files", colliding.Stderr);
    }
}
