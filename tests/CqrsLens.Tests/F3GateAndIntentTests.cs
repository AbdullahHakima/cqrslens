using System.Security.Cryptography;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// CP2 coverage for every rule in R-FIXTURES:
/// freshness gate pass/drift/fail-closed/read-only, text/roslyn parity,
/// C-BOUNDARY true positives and true negatives, C-SURFACE both directions,
/// C-INTENT-STALE, unparsable intent fail-closed, ownership declared-unverified,
/// no-intent parity, and --strict severity interaction.
/// </summary>
public sealed class F3GateAndIntentTests : IDisposable
{
    private readonly string _root;

    public F3GateAndIntentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-f3-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CreateRepoDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string repoDir, string relativePath, string content)
    {
        var full = Path.Combine(repoDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static void WriteMinimalModularApp(string repoDir, bool includeExtraCommand = false)
    {
        WriteFile(repoDir, "App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
        WriteFile(repoDir, "Stubs.cs", """
            using System;
            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                [AttributeUsage(AttributeTargets.Class)] public sealed class ApiControllerAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Method)] public sealed class HttpGetAttribute : Attribute
                {
                    public HttpGetAttribute(string template) { }
                }
            }
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddScoped<TService, TImpl>(this IServiceCollection services) => services;
                }
            }
            """);

        WriteFile(repoDir, "Core/CoreHelper.cs", """
            namespace App.Core;
            public interface ICoreHelper { void Normalize(); }
            public sealed class CoreHelper : ICoreHelper
            {
                public void Normalize() { }
            }
            """);

        WriteFile(repoDir, "Data/DataStore.cs", """
            using App.Core;
            namespace App.Data;
            public interface IDataStore { void Save(); }
            public sealed class DataStore : IDataStore
            {
                private readonly ICoreHelper _coreHelper;
                public DataStore(ICoreHelper coreHelper) => _coreHelper = coreHelper;
                public void Save()
                {
                    _coreHelper.Normalize();
                }
            }
            """);

        WriteFile(repoDir, "Presentation/PresentationServices.cs", """
            using Microsoft.AspNetCore.Mvc;
            using App.Data;
            namespace App.Presentation;
            public interface IPresentationHelper { void Format(); }
            public sealed class PresentationHelper : IPresentationHelper
            {
                public void Format() { }
            }

            public interface IPresentationFacade { void Run(); }
            public sealed class PresentationFacade : IPresentationFacade
            {
                private readonly IPresentationHelper _helper;
                private readonly IDataStore _dataStore;
                public PresentationFacade(IPresentationHelper helper, IDataStore dataStore)
                {
                    _helper = helper;
                    _dataStore = dataStore;
                }

                public void Run()
                {
                    _helper.Format();
                    _dataStore.Save();
                }
            }

            [ApiController]
            public sealed class OrdersController : ControllerBase
            {
                [HttpGet("/api/orders")]
                public string GetOrders() => "ok";
            }
            """);

        WriteFile(repoDir, "Startup.cs", """
            using Microsoft.Extensions.DependencyInjection;
            using App.Core;
            using App.Data;
            using App.Presentation;
            namespace App;
            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<ICoreHelper, CoreHelper>();
                    services.AddScoped<IDataStore, DataStore>();
                    services.AddScoped<IPresentationHelper, PresentationHelper>();
                    services.AddScoped<IPresentationFacade, PresentationFacade>();
                }
            }
            """);

        if (includeExtraCommand)
        {
            WriteFile(repoDir, "Extra/ExtraQuery.cs", """
                namespace App.Extra;
                public interface IRequest<T> { }
                public sealed record ExtraCommand : IRequest<int>;
                """);
        }
    }

    private static (int Code, string Stdout, string Stderr) RunCli(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static Dictionary<string, (DateTime UtcMtime, string Sha256)> SnapshotMetadata(string dir)
    {
        var result = new Dictionary<string, (DateTime, string)>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
            var bytes = File.ReadAllBytes(path);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            result[rel] = (File.GetLastWriteTimeUtc(path), hash);
        }
        return result;
    }

    [Fact]
    public void R_GATE_CleanTree_PassesExitZero_AndLeavesCommittedFilesReadOnly()
    {
        var repo = CreateRepoDir("clean-gate");
        WriteMinimalModularApp(repo);
        var docs = Path.Combine(_root, "clean-docs");

        var export = RunCli(repo, "--export-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, export.Code);

        var before = SnapshotMetadata(docs);
        var checkText = RunCli(repo, "--analysis", "text", "--check-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        var checkRoslyn = RunCli(repo, "--analysis", "roslyn", "--check-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        var after = SnapshotMetadata(docs);

        Assert.Equal(ExitCodes.Success, checkText.Code);
        Assert.Equal(ExitCodes.Success, checkRoslyn.Code);
        Assert.Contains("Docs freshness gate passed", checkText.Stdout);
        Assert.Contains("Docs freshness gate passed", checkRoslyn.Stdout);
        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var key in before.Keys)
        {
            Assert.Equal(before[key].UtcMtime, after[key].UtcMtime);
            Assert.Equal(before[key].Sha256, after[key].Sha256);
        }
    }

    [Fact]
    public void R_GATE_OneFileMutation_FailsNamingFileAndChangedSection()
    {
        var repo = CreateRepoDir("mutated-file");
        WriteMinimalModularApp(repo);
        var docs = Path.Combine(_root, "mutated-docs");

        Assert.Equal(
            ExitCodes.Success,
            RunCli(repo, "--export-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color").Code);

        var targetPage = Path.Combine(docs, "02-building-blocks.md");
        var original = File.ReadAllText(targetPage);
        File.WriteAllText(targetPage, original.Replace("| `repository` | 0 |", "| `repository` | 999 |", StringComparison.Ordinal));

        var check = RunCli(repo, "--check-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");

        Assert.Equal(ExitCodes.StrictFailure, check.Code);
        Assert.Contains("02-building-blocks.md", check.Stdout);
        Assert.Contains("component added/removed", check.Stdout);
        Assert.Contains("docs freshness gate failed", check.Stderr);
    }

    [Fact]
    public void R_GATE_ModelChangingCodeEdit_FailsNamingChangedSections()
    {
        var repo = CreateRepoDir("code-edit");
        WriteMinimalModularApp(repo, includeExtraCommand: false);
        var docs = Path.Combine(_root, "code-edit-docs");

        Assert.Equal(
            ExitCodes.Success,
            RunCli(repo, "--export-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color").Code);

        // Mutate code to add a command with missing handler (changes components, relations/summaries, and findings)
        WriteMinimalModularApp(repo, includeExtraCommand: true);

        var check = RunCli(repo, "--check-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");

        Assert.Equal(ExitCodes.StrictFailure, check.Code);
        Assert.Contains("02-building-blocks.md", check.Stdout);
        Assert.Contains("06-findings.md", check.Stdout);
        Assert.Contains("component added/removed", check.Stdout);
        Assert.Contains("finding added/removed", check.Stdout);
    }

    [Fact]
    public void R_GATE_FailClosed_ScanErrorsAlwaysExitNonZero()
    {
        var docs = Path.Combine(_root, "any-docs");
        Directory.CreateDirectory(docs);

        // 1. Missing folder
        var missing = RunCli(Path.Combine(_root, "does-not-exist"), "--check-docs", docs, "--no-banner", "--no-color");
        Assert.NotEqual(ExitCodes.Success, missing.Code);
        Assert.DoesNotContain("Docs freshness gate passed", missing.Stdout);

        // 2. Folder without .csproj
        var emptyRepo = CreateRepoDir("empty-repo");
        var noProj = RunCli(emptyRepo, "--check-docs", docs, "--no-banner", "--no-color");
        Assert.NotEqual(ExitCodes.Success, noProj.Code);
        Assert.DoesNotContain("Docs freshness gate passed", noProj.Stdout);

        // 3. Ambiguous / incomplete scan (AmbiguousLink -> CanRunStrictChecks == false)
        var ambigRepo = CreateRepoDir("ambig-repo");
        WriteFile(ambigRepo, "App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
        WriteFile(ambigRepo, "Items.cs", """
            namespace Alpha { public sealed record Ping : IRequest<int>; }
            namespace Beta { public sealed record Ping : IRequest<int>; }
            namespace Handlers { public sealed class PingHandler : IRequestHandler<Ping, int> { } }
            """);
        var ambigDocs = Path.Combine(_root, "ambig-docs");
        Assert.Equal(
            ExitCodes.Success,
            RunCli(ambigRepo, "--export-docs", ambigDocs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color").Code);

        var ambigCheck = RunCli(ambigRepo, "--check-docs", ambigDocs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.StrictFailure, ambigCheck.Code);
        Assert.Contains("scan failed", ambigCheck.Stderr);
        Assert.DoesNotContain("Docs freshness gate passed", ambigCheck.Stdout);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void R_INTENT_C_BOUNDARY_TruePositiveWithFileLine_AndTrueNegatives(AnalysisMode mode)
    {
        var repo = CreateRepoDir($"boundary-{mode}");
        WriteMinimalModularApp(repo);
        WriteFile(repo, ".cqrslens.json", """
            {
              "intent": {
                "modules": {
                  "Presentation": { "components": ["App.Presentation"] },
                  "Data": { "components": ["App.Data"] },
                  "Core": { "components": ["App.Core"] }
                },
                "boundaries": [
                  { "from": "Presentation", "to": "Data", "severity": "error" }
                ]
              }
            }
            """);

        var settings = ConfigurationLoader.Load(repo);
        var result = Analyzer.Analyze(repo, mode, new AnalysisOptions { Intent = settings.Intent });

        var boundaryFindings = result.Checks
            .Where(c => c.Code == ArchitectureChecker.IntentBoundaryCode)
            .ToList();

        var finding = Assert.Single(boundaryFindings);
        Assert.Equal(CheckSeverity.Error, finding.Severity);
        Assert.Contains("Presentation", finding.Message);
        Assert.Contains("Data", finding.Message);
        Assert.Contains("PresentationServices.cs:", finding.Message);
        Assert.NotNull(finding.Location);
        Assert.EndsWith("Presentation/PresentationServices.cs", finding.Location!.File);
        Assert.True(finding.Location.Line > 0);
        // Allowed Data -> Core call and intra-module PresentationFacade -> PresentationHelper call produced no boundary finding.
        Assert.DoesNotContain(boundaryFindings, f => f.Message.Contains("CoreHelper", StringComparison.Ordinal));
        Assert.DoesNotContain(boundaryFindings, f => f.Message.Contains("PresentationHelper", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void R_INTENT_C_SURFACE_DetectsUndeclaredAndAbsentEndpointsAndComponents(AnalysisMode mode)
    {
        var repo = CreateRepoDir($"surface-{mode}");
        WriteMinimalModularApp(repo);
        WriteFile(repo, ".cqrslens.json", """
            {
              "intent": {
                "publicSurface": {
                  "endpoints": ["GET /api/missing-route"],
                  "components": ["App.Presentation.OrdersController", "App.Missing.GhostComponent"],
                  "severity": "warning"
                }
              }
            }
            """);

        var settings = ConfigurationLoader.Load(repo);
        var result = Analyzer.Analyze(repo, mode, new AnalysisOptions { Intent = settings.Intent });

        var surfaceFindings = result.Checks
            .Where(c => c.Code == ArchitectureChecker.IntentSurfaceCode)
            .ToList();

        Assert.All(surfaceFindings, f => Assert.Equal(CheckSeverity.Warning, f.Severity));
        // Undeclared model endpoint GET /api/orders
        Assert.Contains(surfaceFindings, f => f.Message.Contains("/api/orders", StringComparison.Ordinal) &&
                                              f.Message.Contains("not in the declared public surface", StringComparison.Ordinal));
        // Absent declared endpoint GET /api/missing-route
        Assert.Contains(surfaceFindings, f => f.Message.Contains("/api/missing-route", StringComparison.Ordinal) &&
                                              f.Message.Contains("absent from the model", StringComparison.Ordinal));
        // Undeclared model component (e.g., DataStore)
        Assert.Contains(surfaceFindings, f => f.Message.Contains("App.Data.DataStore", StringComparison.Ordinal) &&
                                              f.Message.Contains("not in the declared public surface", StringComparison.Ordinal));
        // Declared OrdersController is NOT reported as undeclared or absent
        Assert.DoesNotContain(surfaceFindings, f => f.Target is not null &&
                                                    f.Target.EndsWith("OrdersController", StringComparison.Ordinal));
        // Absent declared component App.Missing.GhostComponent
        Assert.Contains(surfaceFindings, f => f.Message.Contains("App.Missing.GhostComponent", StringComparison.Ordinal) &&
                                              f.Message.Contains("absent from the model", StringComparison.Ordinal));
    }

    [Fact]
    public void R_INTENT_StaleIntent_ModuleNamingMissingProjectOrComponent_ProducesFinding()
    {
        var repo = CreateRepoDir("stale-intent");
        WriteMinimalModularApp(repo);
        WriteFile(repo, ".cqrslens.json", """
            {
              "intent": {
                "modules": {
                  "GhostProjectModule": { "projects": ["NonExistentProject"] },
                  "GhostComponentModule": { "components": ["App.NonExistent.MissingService"] }
                }
              }
            }
            """);

        var settings = ConfigurationLoader.Load(repo);
        var result = Analyzer.Analyze(repo, AnalysisMode.Text, new AnalysisOptions { Intent = settings.Intent });

        var stale = result.Checks
            .Where(c => c.Code == ArchitectureChecker.IntentStaleCode)
            .ToList();

        Assert.Equal(2, stale.Count);
        Assert.Contains(stale, f => f.Message.Contains("NonExistentProject", StringComparison.Ordinal));
        Assert.Contains(stale, f => f.Message.Contains("App.NonExistent.MissingService", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("""{ "intent": { "unknownSection": {} } }""")]
    [InlineData("""{ "intent": { "modules": { "ModA": { "components": ["App.*"] } } } }""")]
    [InlineData("""{ "intent": { "modules": { "ModA": { "projects": ["App?"] } } } }""")]
    [InlineData("""{ "intent": { "modules": { "ModA": {} } } }""")]
    [InlineData("""{ "intent": { "modules": { "ModA": { "projects": ["App"] } }, "boundaries": [ { "from": "ModA", "to": "MissingMod" } ] } }""")]
    [InlineData("""{ "intent": { "modules": { "ModA": { "projects": ["App"] } }, "boundaries": [ { "from": "ModA", "to": "ModA" } ] } }""")]
    [InlineData("""{ "intent": { "modules": { "ModA": { "projects": ["App"] }, "ModB": { "projects": ["App"] } }, "boundaries": [ { "from": "ModA", "to": "ModB", "severity": "fatal" } ] } }""")]
    [InlineData("""{ "intent": { "publicSurface": {} } }""")]
    [InlineData("""{ "intent": { "publicSurface": { "endpoints": ["GET /api/*"] } } }""")]
    [InlineData("""{ "intent": { "ownership": { "UndeclaredModule": "TeamA" } } }""")]
    public void R_INTENT_UnparsableOrInvalidIntent_FailsClosedAsScanError(string configJson)
    {
        var repo = CreateRepoDir("invalid-intent-" + Guid.NewGuid().ToString("N")[..8]);
        WriteMinimalModularApp(repo);
        WriteFile(repo, ".cqrslens.json", configJson);
        var docs = Path.Combine(_root, "docs-" + Guid.NewGuid().ToString("N")[..8]);

        var check = RunCli(repo, "--check-docs", docs, "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Usage, check.Code);
        Assert.Contains("scan failed", check.Stderr);
        Assert.DoesNotContain("Docs freshness gate passed", check.Stdout);
    }

    [Fact]
    public void R_INTENT_Ownership_RenderedDeclaredUnverified_AndNeverProducesFinding()
    {
        var repo = CreateRepoDir("ownership");
        WriteMinimalModularApp(repo);
        WriteFile(repo, ".cqrslens.json", """
            {
              "intent": {
                "modules": {
                  "Core": { "components": ["App.Core"] },
                  "Data": { "components": ["App.Data"] }
                },
                "ownership": {
                  "Core": "Platform Team",
                  "Data": "Storage Team"
                }
              }
            }
            """);

        var docs = Path.Combine(_root, "ownership-docs");
        var export = RunCli(repo, "--export-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, export.Code);

        var buildingBlocks = File.ReadAllText(Path.Combine(docs, "02-building-blocks.md"));
        var findings = File.ReadAllText(Path.Combine(docs, "06-findings.md"));
        Assert.Contains("Platform Team (declared, unverified)", buildingBlocks);
        Assert.Contains("Storage Team (declared, unverified)", buildingBlocks);
        Assert.Contains("declared, unverified", buildingBlocks);
        Assert.Contains("No intent violations or stale intent selectors found.", findings);

        var check = RunCli(repo, "--check-docs", docs, "--strict", "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, check.Code);
    }

    [Fact]
    public void R_INTENT_NoIntentBehaviour_IsIdenticalToPreF3()
    {
        var repo = CreateRepoDir("no-intent");
        WriteMinimalModularApp(repo);
        var docs = Path.Combine(_root, "no-intent-docs");

        var export = RunCli(repo, "--export-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, export.Code);

        Assert.DoesNotContain("Declared Intent", File.ReadAllText(Path.Combine(docs, "02-building-blocks.md")));
        Assert.DoesNotContain("Declared Public Surface", File.ReadAllText(Path.Combine(docs, "01-context.md")));
        Assert.DoesNotContain("Intent Verification Findings", File.ReadAllText(Path.Combine(docs, "06-findings.md")));

        var result = Analyzer.Analyze(repo);
        Assert.DoesNotContain(result.Checks, c => c.Code.StartsWith("C-", StringComparison.Ordinal));
    }

    [Fact]
    public void R_GATE_StrictInteraction_FailsOnlyWhenStrictAndErrorSeverityIntentFindingExists()
    {
        var repo = CreateRepoDir("strict-intent");
        WriteMinimalModularApp(repo);
        WriteFile(repo, ".cqrslens.json", """
            {
              "intent": {
                "modules": {
                  "Presentation": { "components": ["App.Presentation"] },
                  "Data": { "components": ["App.Data"] }
                },
                "boundaries": [
                  { "from": "Presentation", "to": "Data", "severity": "error" }
                ]
              }
            }
            """);

        var docs = Path.Combine(_root, "strict-docs");
        Assert.Equal(
            ExitCodes.Success,
            RunCli(repo, "--export-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color").Code);

        var nonStrict = RunCli(repo, "--check-docs", docs, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, nonStrict.Code);
        Assert.Contains("C-BOUNDARY", nonStrict.Stdout);
        Assert.Contains("Docs freshness gate passed", nonStrict.Stdout);

        var strict = RunCli(repo, "--check-docs", docs, "--strict", "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.StrictFailure, strict.Code);
        Assert.Contains("C-BOUNDARY", strict.Stdout);
        Assert.Contains("--strict", strict.Stderr);
    }
}
