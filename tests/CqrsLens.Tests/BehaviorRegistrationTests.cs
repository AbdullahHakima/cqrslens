using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 6 Stage 2 aspect D, decision #7 branch (b): behaviors carry a resolved
/// registration site and a per-site ordinal, and the reports present them in
/// registered order with an explicit "not guaranteed" label. The fields are
/// presentation-only and never move detection counts or the JSON contract.
/// </summary>
public sealed class BehaviorRegistrationTests : IDisposable
{
    private readonly string _root;

    public BehaviorRegistrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-d2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        WriteProject(_root, "Demo");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DirectDiRegistrations_AssignPerSiteOrdinalInSourceOrder(AnalysisMode mode)
    {
        WriteContracts();
        WriteFile("DependencyInjection.cs", """
            namespace Demo;

            public static class DependencyInjection
            {
                public static void AddApp()
                {
                    Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
                    Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidatorBehavior<,>));
                }
            }
            """);
        WriteBehaviors("LoggingBehavior", "ValidatorBehavior");

        var result = Analyzer.Analyze(_root, mode);
        var log = result.Behaviors.Single(b => b.Name == "LoggingBehavior");
        var val = result.Behaviors.Single(b => b.Name == "ValidatorBehavior");

        Assert.Equal(2, result.Behaviors.Count);
        Assert.Equal(1, log.RegistrationOrder);
        Assert.Equal(2, val.RegistrationOrder);
        Assert.NotNull(log.RegistrationSite);
        Assert.Equal(log.RegistrationSite, val.RegistrationSite);
        Assert.StartsWith("DependencyInjection.cs:", log.RegistrationSite);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void AddOpenBehaviorRegistrations_AssignPerSiteOrdinalInSourceOrder(AnalysisMode mode)
    {
        WriteContracts();
        WriteFile("Extensions.cs", """
            namespace Demo;

            public static class Extensions
            {
                public static void AddApp()
                {
                    Services.AddMediatR(cfg =>
                    {
                        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
                        cfg.AddOpenBehavior(typeof(ValidatorBehavior<,>));
                    });
                }
            }
            """);
        WriteBehaviors("LoggingBehavior", "ValidatorBehavior");

        var result = Analyzer.Analyze(_root, mode);

        Assert.Equal(1, result.Behaviors.Single(b => b.Name == "LoggingBehavior").RegistrationOrder);
        Assert.Equal(2, result.Behaviors.Single(b => b.Name == "ValidatorBehavior").RegistrationOrder);
        Assert.All(result.Behaviors, b => Assert.StartsWith("Extensions.cs:", b.RegistrationSite));
    }

    [Fact]
    public void UnregisteredBehavior_KeepsNullFields()
    {
        WriteContracts();
        WriteFromAssemblyScanOnly();
        WriteBehaviors("LoggingBehavior");

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);
        var log = result.Behaviors.Single(b => b.Name == "LoggingBehavior");

        Assert.Null(log.RegistrationOrder);
        Assert.Null(log.RegistrationSite);
    }

    [Fact]
    public void AssemblyScanningAndFrameworkBuiltIns_AreNotRegistrations()
    {
        WriteContracts();
        // Autofac assembly scanning and MediatR's built-in pre/post adapters must not
        // be read as user behavior registrations.
        WriteFile("MediatorModule.cs", """
            namespace Demo;

            public static class MediatorModule
            {
                public static void Load()
                {
                    Builder.RegisterAssemblyTypes(typeof(MediatorModule)).AsClosedTypesOf(typeof(IPipelineBehavior<,>));
                    Builder.RegisterGeneric(typeof(RequestPostProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
                    Builder.RegisterGeneric(typeof(RequestPreProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
                }
            }
            """);
        WriteBehaviors("LoggingBehavior");

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);

        // No user behavior is named by the framework adapters, so no count or site moves.
        Assert.Single(result.Behaviors);
        Assert.Null(result.Behaviors[0].RegistrationSite);
    }

    [Fact]
    public void AmbiguousNameAcrossProjects_PrefersTheBehaviorsOwnProject()
    {
        WriteContracts();
        WriteProject(Path.Combine(_root, "Owner"), "Owner");
        WriteProject(Path.Combine(_root, "Other"), "Other");
        WriteFile("Owner/Reg.cs", Registration("LoggingBehavior", "OwnerReg"));
        WriteFile("Other/Reg.cs", Registration("LoggingBehavior", "OtherReg"));
        WriteFile("Owner/LoggingBehavior.cs", """
            using MediatR;

            namespace Owner;
            public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
            """);

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);
        var log = result.Behaviors.Single(b => b.Name == "LoggingBehavior");

        Assert.Equal("Owner", log.ProjectName);
        Assert.StartsWith("Owner/Reg.cs:", log.RegistrationSite);
    }

    [Fact]
    public void SameNameRegisteredAtTwoSitesInOneProject_IsAmbiguousAndLeftNull()
    {
        WriteContracts();
        WriteFile("First.cs", Registration("LoggingBehavior", "FirstReg"));
        WriteFile("Second.cs", Registration("LoggingBehavior", "SecondReg"));
        WriteBehaviors("LoggingBehavior");

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);
        var log = result.Behaviors.Single(b => b.Name == "LoggingBehavior");

        Assert.Null(log.RegistrationOrder);
        Assert.Null(log.RegistrationSite);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Reports_PresentRegisteredOrderWithProvenanceAndDisclaimer(AnalysisMode mode)
    {
        WriteContracts();
        WriteFile("Extensions.cs", """
            namespace Demo;

            public static class Extensions
            {
                public static void AddApp()
                {
                    Services.AddMediatR(cfg =>
                    {
                        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
                        cfg.AddOpenBehavior(typeof(ValidatorBehavior<,>));
                    });
                }
            }
            """);
        WriteBehaviors("LoggingBehavior", "ValidatorBehavior");

        var result = Analyzer.Analyze(_root, mode);
        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        Assert.Contains("### Registered order", md);
        Assert.Contains("#### `Extensions.cs:", md);
        Assert.Contains("| 1 | LoggingBehavior |", md);
        Assert.Contains("| 2 | ValidatorBehavior |", md);
        Assert.Contains("Execution order is not guaranteed and is not proven by the scan", md);
        Assert.DoesNotContain("Registration site not found", md);

        Assert.Contains("<h3>Registered order</h3>", html);
        Assert.Contains("Execution order is not guaranteed and is not proven by the scan", html);
        Assert.Contains("<title>Pipeline behavior chain</title>", html);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Reports_PutUnregisteredBehaviorsUnderTheNotDeterminableHeading(AnalysisMode mode)
    {
        WriteContracts();
        WriteFromAssemblyScanOnly();
        WriteBehaviors("LoggingBehavior");

        var result = Analyzer.Analyze(_root, mode);
        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        Assert.DoesNotContain("### Registered order", md);
        Assert.Contains("### Registration site not found — order not determinable", md);
        Assert.Contains("Registration site not found — order not determinable", html);
    }

    [Fact]
    public void PresentationOrder_IsDeterministic()
    {
        WriteContracts();
        WriteFile("Extensions.cs", """
            namespace Demo;

            public static class Extensions
            {
                public static void AddApp()
                {
                    Services.AddMediatR(cfg =>
                    {
                        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
                        cfg.AddOpenBehavior(typeof(ValidatorBehavior<,>));
                    });
                }
            }
            """);
        WriteBehaviors("LoggingBehavior", "ValidatorBehavior");

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);
        var first = BehaviorOrdering.Flattened(result.Behaviors).Select(b => b.Name).ToList();
        var second = BehaviorOrdering.Flattened(result.Behaviors).Select(b => b.Name).ToList();

        Assert.Equal(new[] { "LoggingBehavior", "ValidatorBehavior" }, first);
        Assert.Equal(first, second);
    }

    private static string Registration(string behaviorName, string className)
        => $$"""
            namespace Demo;

            public static class {{className}}
            {
                public static void AddApp()
                {
                    Services.AddTransient(typeof(IPipelineBehavior<,>), typeof({{behaviorName}}<,>));
                }
            }
            """;

    private void WriteFromAssemblyScanOnly()
        => WriteFile("Module.cs", """
            namespace Demo;

            public static class Module
            {
                public static void Load()
                {
                    Builder.RegisterGeneric(typeof(RequestPostProcessorBehavior<,>)).As(typeof(IPipelineBehavior<,>));
                }
            }
            """);

    private void WriteBehaviors(params string[] names)
    {
        var body = string.Join("\n", names.Select(n =>
            $"public sealed class {n}<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> {{ }}"));
        WriteFile("Behaviors.cs", $"using MediatR;\n\nnamespace Demo;\n{body}\n");
    }

    private void WriteContracts()
        => WriteFile("Contracts.cs", """
            using System;

            namespace MediatR
            {
                public interface IPipelineBehavior<TRequest, TResponse> { }
            }

            namespace Demo
            {
                public static class Services
                {
                    public static void AddTransient(Type service, Type implementation) { }
                    public static void AddMediatR(Action<MediatRConfig> configure) { }
                }

                public sealed class MediatRConfig
                {
                    public void AddOpenBehavior(Type openBehaviorType) { }
                }

                public static class Builder
                {
                    public static Registration RegisterGeneric(Type type) => new();
                    public static Registration RegisterAssemblyTypes(params Type[] types) => new();
                }

                public sealed class Registration
                {
                    public Registration As(Type service) => this;
                    public Registration AsClosedTypesOf(Type service) => this;
                }
            }
            """);

    private static void WriteProject(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, name + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    private void WriteFile(string relative, string code)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }
}
