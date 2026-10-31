using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CqrsLens;

const string GeneratedContractsSource = """
using System.Threading;
using System.Threading.Tasks;

namespace MediatR
{
    public interface IRequest<out TResponse> { }
    public interface IRequest { }
    public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }

    public interface INotification { }
    public interface INotificationHandler<in TNotification> where TNotification : INotification
    {
        Task Handle(TNotification notification, CancellationToken cancellationToken);
    }

    public interface IPublisher
    {
        Task Publish(object notification, CancellationToken cancellationToken = default);
    }
}

namespace FluentValidation
{
    public abstract class AbstractValidator<T> { }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext { }
    public class DbSet<TEntity> where TEntity : class { }
}
""";

var path = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "samples/SampleShop";
var modesArg = GetValue(args, "--mode") ?? "both";
var warmup = int.TryParse(GetValue(args, "--warmup"), out var w) ? Math.Max(0, w) : 3;
var iterations = int.TryParse(GetValue(args, "--iterations"), out var i) ? Math.Max(1, i) : 10;
var json = args.Contains("--json", StringComparer.Ordinal);
var generate = GetValue(args, "--generate");
if (generate is not null)
{
    if (!int.TryParse(generate, out var sliceCount) || sliceCount < 1)
        throw new ArgumentException("--generate expects a positive slice count, e.g. --generate 500");

    path = GenerateSolution(
        Path.Combine(FindRepositoryRoot(), "artifacts", "bench", "synth-" + sliceCount),
        sliceCount,
        args.Contains("--force", StringComparer.Ordinal));
    Console.Error.WriteLine($"Generated {sliceCount} synthetic slices under {path}");

    if (args.Contains("--generate-only", StringComparer.Ordinal))
        return;
}

var modes = modesArg switch
{
    "text" => new[] { AnalysisMode.Text },
    "roslyn" => new[] { AnalysisMode.Roslyn },
    "both" => new[] { AnalysisMode.Text, AnalysisMode.Roslyn },
    _ => throw new ArgumentException($"unknown --mode '{modesArg}' (text, roslyn, both)"),
};

var runs = new List<RunResult>();
foreach (var mode in modes)
{
    // Warm up JIT and the static regex/parser state before measuring.
    for (var run = 0; run < warmup; run++)
        Analyzer.Analyze(path, mode);

    var samples = new List<Sample>(iterations);
    for (var run = 0; run < iterations; run++)
    {
        var processAllocated = GC.GetTotalAllocatedBytes(precise: false);
        var collections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        var watch = Stopwatch.StartNew();
        var result = Analyzer.Analyze(path, mode);
        watch.Stop();

        samples.Add(new Sample(
            watch.Elapsed.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: false) - processAllocated,
            GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2) - collections,
            result));
    }

    var counts = samples
        .Select(s => ShapeCounts(s.Result))
        .Distinct()
        .ToList();
    if (counts.Count != 1)
        throw new InvalidOperationException($"analysis produced nondeterministic counts: {string.Join(" | ", counts)}");

    runs.Add(new RunResult(mode.ToString().ToLowerInvariant(), samples));
}

if (json)
{
    var payload = runs.Select(r => new
    {
        mode = r.Mode,
        iterations = r.Samples.Count,
        milliseconds = Stats(r.Samples.Select(s => s.Milliseconds)),
        allocatedBytes = Stats(r.Samples.Select(s => (double)s.AllocatedBytes)),
        gen0Collections = Stats(r.Samples.Select(s => (double)s.Gen0Collections)),
        phases = AveragePhases(r.Samples).Select(p => new
        {
            name = p.Name,
            milliseconds = p.Milliseconds,
            allocatedBytes = p.AllocatedBytes,
        }),
        projects = r.Samples[0].Result.Projects.Count,
        requests = r.Samples[0].Result.Requests.Count,
        handlers = r.Samples[0].Result.Handlers.Count,
        validators = r.Samples[0].Result.Validators.Count,
        notifications = r.Samples[0].Result.Notifications.Count,
        dbContexts = r.Samples[0].Result.DbContexts.Count,
        endpoints = r.Samples[0].Result.Endpoints.Count,
        files = r.Samples[0].Result.FileCount,
        discoveredFiles = r.Samples[0].Result.DiscoveredFileCount,
        diagnostics = r.Samples[0].Result.Diagnostics.Count,
        checks = r.Samples[0].Result.Checks.Count,
    });
    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

Console.WriteLine($"CqrsLens benchmark · {Path.GetFullPath(path)}");
Console.WriteLine($"{warmup} warmup + {iterations} measured iteration(s)");
foreach (var run in runs)
{
    var ms = Stats(run.Samples.Select(s => s.Milliseconds));
    var alloc = Stats(run.Samples.Select(s => (double)s.AllocatedBytes));
    var gen0 = Stats(run.Samples.Select(s => (double)s.Gen0Collections));
    Console.WriteLine();
    Console.WriteLine($"[{run.Mode}]");
    Console.WriteLine($"  elapsed   min {ms.Min,8:0.00} ms  median {ms.Median,8:0.00} ms  mean {ms.Mean,8:0.00} ms  p95 {ms.P95,8:0.00} ms  max {ms.Max,8:0.00} ms");
    Console.WriteLine($"  alloc     mean {alloc.Mean / 1048576.0,7:0.00} MB  median {alloc.Median / 1048576.0,7:0.00} MB");
    Console.WriteLine($"  GC        mean {gen0.Mean,7:0.00} collections");
    Console.WriteLine($"  shapes    {run.Samples[0].Result.Projects.Count} project(s), {run.Samples[0].Result.FileCount} file(s), " +
                      $"{run.Samples[0].Result.Requests.Count} request(s), {run.Samples[0].Result.Handlers.Count} handler(s), " +
                      $"{run.Samples[0].Result.Validators.Count} validator(s), {run.Samples[0].Result.Notifications.Count} notification(s), " +
                      $"{run.Samples[0].Result.DbContexts.Count} db context(s), {run.Samples[0].Result.Endpoints.Count} endpoint(s)");
    Console.WriteLine($"  findings  {run.Samples[0].Result.Checks.Count} check(s), {run.Samples[0].Result.Diagnostics.Count} diagnostic(s)");
    Console.WriteLine("  phases (mean)");
    foreach (var (name, milliseconds, allocated) in AveragePhases(run.Samples))
        Console.WriteLine($"    {name,-24} {milliseconds,8:0.00} ms {allocated / 1048576.0,8:0.00} MB");
}

static string? GetValue(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static Stats Stats(IEnumerable<double> values)
{
    var sorted = values.OrderBy(v => v).ToArray();
    var mean = sorted.Average();
    var p95Index = Math.Clamp((int)Math.Ceiling(sorted.Length * 0.95) - 1, 0, sorted.Length - 1);
    return new Stats(
        sorted[0],
        sorted[sorted.Length / 2],
        mean,
        sorted[p95Index],
        sorted[^1]);
}

static List<(string Name, double Milliseconds, double AllocatedBytes)> AveragePhases(IReadOnlyList<Sample> samples)
{
    return samples
        .SelectMany(s => s.Result.PhaseTimings)
        .GroupBy(p => p.Name)
        .Select(g => (
            g.Key,
            g.Average(p => (double)p.ElapsedMilliseconds),
            g.Average(p => (double)p.AllocatedBytes)))
        .ToList();
}

static string GenerateSolution(string root, int slices, bool force)
{
    var manifest = Path.Combine(root, ".benchmark-slices");
    if (!force && File.Exists(manifest) &&
        File.ReadAllText(manifest).Trim() == slices.ToString(CultureInfo.InvariantCulture))
        return root;

    if (Directory.Exists(root))
        Directory.Delete(root, true);

    var projectDirs = new[] { "Domain", "Application", "Infrastructure", "Api" };
    foreach (var dir in projectDirs)
        Directory.CreateDirectory(Path.Combine(root, dir));

    WriteGeneratedProject(root, "Domain", "Bench.Domain", Array.Empty<string>(), Array.Empty<string>());
    WriteGeneratedProject(root, "Application", "Bench.Application",
        new[] { "Domain" }, Array.Empty<string>());
    WriteGeneratedProject(root, "Infrastructure", "Bench.Infrastructure",
        new[] { "Domain", "Application" }, Array.Empty<string>());
    WriteGeneratedProject(root, "Api", "Bench.Api",
        new[] { "Domain", "Application", "Infrastructure" },
        new[] { "Microsoft.AspNetCore.App" });

    WriteGeneratedFile(Path.Combine(root, "Domain", "Contracts.cs"), GeneratedContractsSource);

    // Regression shape for the "Parse sources" pathology: a data file whose
    // payload is one oversized single-line string literal (NorthwindTraders'
    // SampleDataSeeder shape). Keeping it in the synthetic solution means the
    // benchmark harness exercises the long-word-run parse path on every run.
    WriteGeneratedFile(
        Path.Combine(root, "Application", "SeedData.cs"),
        GenerateLongLineDataFile(512 * 1024));

    var moduleCount = Math.Max(1, Math.Min(10, slices));
    var dbSets = new StringBuilder();
    for (var i = 0; i < slices; i++)
    {
        var module = i * moduleCount / slices;

        var handlerBody = i % 4 == 0
            ? $$"""
        private readonly IPublisher _publisher;

        public CreateThing{{i}}CommandHandler(IPublisher publisher) => _publisher = publisher;

        public async Task<Guid> Handle(CreateThing{{i}}Command request, CancellationToken cancellationToken)
        {
            await _publisher.Publish(new Thing{{i}}CreatedNotification(request.Id), cancellationToken);
            return request.Id;
        }
"""
            : $$"""
        public Task<Guid> Handle(CreateThing{{i}}Command request, CancellationToken cancellationToken)
            => Task.FromResult(request.Id);
""";

        var validator = i % 2 == 0
            ? $$"""
    public sealed class CreateThing{{i}}CommandValidator : AbstractValidator<CreateThing{{i}}Command>
    {
    }
"""
            : "";

        WriteGeneratedFile(
            Path.Combine(root, "Application", "Features", "Module" + module, "Thing" + i + ".cs"),
            $$"""
using System;
using System.Threading;
using System.Threading.Tasks;
using Bench.Domain.Events;
using FluentValidation;
using MediatR;

namespace Bench.Application.Features.Module{{module}}
{
    public sealed record CreateThing{{i}}Command(Guid Id, string Name) : IRequest<Guid>;

    public sealed class CreateThing{{i}}CommandHandler : IRequestHandler<CreateThing{{i}}Command, Guid>
    {
{{handlerBody}}
    }

    public sealed record GetThing{{i}}Query(Guid Id) : IRequest<string>;

    public sealed class GetThing{{i}}QueryHandler : IRequestHandler<GetThing{{i}}Query, string>
    {
        public Task<string> Handle(GetThing{{i}}Query request, CancellationToken cancellationToken)
            => Task.FromResult(string.Empty);
    }
{{validator}}
}
""");

        WriteGeneratedFile(
            Path.Combine(root, "Domain", "Entities", "Thing" + i + ".cs"),
            $$"""
using System;
using MediatR;

namespace Bench.Domain.Entities
{
    public sealed class Thing{{i}}
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}

namespace Bench.Domain.Events
{
    public sealed record Thing{{i}}CreatedNotification(Guid Id) : INotification;
}
""");

        WriteGeneratedFile(
            Path.Combine(root, "Api", "Endpoints", "Thing" + i + "Endpoints.cs"),
            $$"""
using System;
using Bench.Application.Features.Module{{module}};
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bench.Api.Endpoints
{
    public static class Thing{{i}}Endpoints
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/things/{{i}}", (CreateThing{{i}}Command command) => Results.Ok(command.Id));
            app.MapGet("/api/things/{{i}}", (GetThing{{i}}Query query) => Results.Ok(query));
        }
    }
}
""");

        dbSets.AppendLine(
            $"        public DbSet<Thing{i}> Things{i} {{ get; }} = null!;");
    }

    WriteGeneratedFile(
        Path.Combine(root, "Infrastructure", "Persistence", "BenchDbContext.cs"),
        $$"""
using Bench.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bench.Infrastructure.Persistence
{
    public sealed class BenchDbContext : DbContext
    {
{{dbSets}}
    }
}
""");

    File.WriteAllText(manifest, slices.ToString(CultureInfo.InvariantCulture));
    return root;
}

static void WriteGeneratedProject(
    string root,
    string directory,
    string assemblyName,
    IReadOnlyList<string> projectReferences,
    IReadOnlyList<string> frameworkReferences)
{
    var builder = new StringBuilder();
    builder.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
    builder.AppendLine("  <PropertyGroup>");
    builder.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
    builder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
    builder.AppendLine("    <Nullable>enable</Nullable>");
    builder.AppendLine($"    <AssemblyName>{assemblyName}</AssemblyName>");
    builder.AppendLine("    <RootNamespace>" + assemblyName.Replace('.', '_') + "</RootNamespace>");
    builder.AppendLine("  </PropertyGroup>");
    if (projectReferences.Count > 0)
    {
        builder.AppendLine("  <ItemGroup>");
        foreach (var reference in projectReferences)
            builder.AppendLine($"    <ProjectReference Include=\"../{reference}/Bench.{reference}.csproj\" />");
        builder.AppendLine("  </ItemGroup>");
    }
    if (frameworkReferences.Count > 0)
    {
        builder.AppendLine("  <ItemGroup>");
        foreach (var reference in frameworkReferences)
            builder.AppendLine($"    <FrameworkReference Include=\"{reference}\" />");
        builder.AppendLine("  </ItemGroup>");
    }
    builder.AppendLine("</Project>");
    WriteGeneratedFile(Path.Combine(root, directory, "Bench." + directory + ".csproj"), builder.ToString());
}

static string GenerateLongLineDataFile(int literalLength)
{
    const string digits = "FABCDEF0123456789";
    var literal = new StringBuilder(literalLength);
    for (var i = 0; i < literalLength; i++)
        literal.Append(digits[i % digits.Length]);

    var builder = new StringBuilder(literalLength + 512);
    builder.AppendLine("namespace Bench.Application;");
    builder.AppendLine("public static class SeedData");
    builder.AppendLine("{");
    builder.AppendLine("    public static byte[] Load()");
    builder.AppendLine("    {");
    builder.Append("        return StringToByteArray(\"");
    builder.Append(literal);
    builder.AppendLine("\");");
    builder.AppendLine("    }");
    builder.AppendLine();
    builder.AppendLine("    private static byte[] StringToByteArray(string hex) => new byte[hex.Length / 2];");
    builder.AppendLine("}");
    return builder.ToString();
}

static void WriteGeneratedFile(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
}

static ShapeCounts ShapeCounts(AnalysisResult result) => new(
    result.FileCount,
    result.DiscoveredFileCount,
    result.Projects.Count,
    result.Requests.Count,
    result.Handlers.Count,
    result.Validators.Count,
    result.Notifications.Count,
    result.NotificationHandlers.Count,
    result.DbContexts.Count,
    result.Endpoints.Count,
    result.Checks.Count,
    result.Diagnostics.Count);

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CqrsLens.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

internal sealed record Sample(double Milliseconds, long AllocatedBytes, int Gen0Collections, AnalysisResult Result);

internal sealed record RunResult(string Mode, IReadOnlyList<Sample> Samples);

internal sealed record Stats(double Min, double Median, double Mean, double P95, double Max);

internal sealed record ShapeCounts(
    int Files,
    int DiscoveredFiles,
    int Projects,
    int Requests,
    int Handlers,
    int Validators,
    int Notifications,
    int NotificationHandlers,
    int DbContexts,
    int Endpoints,
    int Checks,
    int Diagnostics);
