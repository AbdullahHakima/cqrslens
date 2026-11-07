using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Unit coverage for the D1 service-resolution index: the declaration half
/// (concrete class implements interface) and the registration half (MS-DI and
/// Autofac syntax), plus the fail-closed ambiguity rules and Roslyn parity.
/// </summary>
public class ServiceResolutionIndexTests
{
    [Fact]
    public void SingleImplementation_ResolvesByDeclaration()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IGreeter { }

            public class Greeter : IGreeter { }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("App.IGreeter", resolution.Interface);
        Assert.Equal("App.IGreeter", resolution.InterfaceName);
        Assert.Equal("App.Greeter", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Declaration, resolution.Source);
        Assert.False(resolution.IsAmbiguous);
        Assert.False(resolution.IsOpenGeneric);
        var candidate = Assert.Single(resolution.Candidates);
        Assert.True(candidate.DeclaredInScan);
        Assert.False(candidate.FromRegistration);
    }

    [Fact]
    public void MultipleImplementations_AreAmbiguousAndChooseNothing()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class FirstRepo : IRepo { }
            public class SecondRepo : IRepo { }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.True(resolution.IsAmbiguous);
        Assert.Null(resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.None, resolution.Source);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    [Fact]
    public void Registration_DisambiguatesMultipleCandidates()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class FirstRepo : IRepo { }
            public class SecondRepo : IRepo { }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IRepo, SecondRepo>();
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("App.IRepo", resolution.Interface);
        Assert.Equal("App.SecondRepo", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Registration, resolution.Source);
        Assert.False(resolution.IsAmbiguous);
    }

    [Fact]
    public void Registration_ConfirmsSingleDeclaration()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class Repo : IRepo { }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IRepo, Repo>();
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("App.Repo", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Both, resolution.Source);
        var candidate = Assert.Single(resolution.Candidates);
        Assert.True(candidate.DeclaredInScan);
        Assert.True(candidate.FromRegistration);
    }

    [Fact]
    public void FactoryForm_ResolvesNamedFromRegistration()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class Repo : IRepo { }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IRepo>(sp => sp.GetRequiredService<Repo>());
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("App.Repo", resolution.Implementation);
        Assert.True(resolution.Candidates[0].FromRegistration);
    }

    [Fact]
    public void TypeofForm_ResolvesImplementation()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class Repo : IRepo { }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient(typeof(IRepo), typeof(Repo));
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("App.Repo", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Both, resolution.Source);
    }

    [Fact]
    public void Autofac_RegisterTypeAs_ResolvesImplementation()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class Repo : IRepo { }

            public static class Setup
            {
                public static void Configure(ContainerBuilder builder)
                {
                    builder.RegisterType<Repo>().As<IRepo>().InstancePerLifetimeScope();
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("App.IRepo", resolution.Interface);
        Assert.Equal("App.Repo", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Both, resolution.Source);
    }

    [Fact]
    public void Autofac_Typeof_ResolvesImplementation()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class Repo : IRepo { }

            public static class Setup
            {
                public static void Configure(ContainerBuilder builder)
                {
                    builder.RegisterType(typeof(Repo)).As(typeof(IRepo));
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("App.IRepo", resolution.Interface);
        Assert.Equal("App.Repo", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Both, resolution.Source);
    }

    [Fact]
    public void Autofac_AsSelf_IsANoOpForTheIndex()
    {
        var result = Build(("File.cs", """
            namespace App;

            public class Repo { }

            public static class Setup
            {
                public static void Configure(ContainerBuilder builder)
                {
                    builder.RegisterType<Repo>().AsSelf();
                }
            }
            """));

        Assert.Empty(result.ServiceResolutions);
    }

    [Fact]
    public void OpenGenericRegistration_DoesNotResolveClosedInterface()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IPipelineBehavior<TRequest, TResponse> { }

            public class ClosedRequest { }

            public class BehaviorA : IPipelineBehavior<ClosedRequest, int> { }
            public class BehaviorB : IPipelineBehavior<ClosedRequest, int> { }

            public class GlobalBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped(typeof(IPipelineBehavior<,>), typeof(GlobalBehavior<,>));
                }
            }
            """));

        var open = Assert.Single(result.ServiceResolutions, r => r.IsOpenGeneric);
        Assert.Equal("App.IPipelineBehavior", open.InterfaceName);
        Assert.Equal("App.GlobalBehavior", open.Implementation);
        Assert.Equal(ServiceResolutionSource.Both, open.Source);

        // The open-generic registration must not touch the closed interface, which
        // still has two declaration candidates and therefore stays ambiguous.
        var closed = Assert.Single(result.ServiceResolutions, r => !r.IsOpenGeneric);
        Assert.Equal("App.IPipelineBehavior", closed.InterfaceName);
        Assert.True(closed.IsAmbiguous);
        Assert.Null(closed.Implementation);
    }

    [Fact]
    public void ClosedGenericImplementation_ResolvesWhenUnambiguous()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IHandler<T> { }

            public class Order { }

            public class OrderHandler : IHandler<Order> { }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.False(resolution.IsOpenGeneric);
        Assert.Equal(1, resolution.Arity);
        Assert.Equal(new[] { "Order" }, resolution.Arguments);
        Assert.Equal("App.OrderHandler", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Declaration, resolution.Source);
    }

    [Fact]
    public void InterfaceWithNoImplementation_HasNoEntry()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IUnused { }
            """));

        Assert.Empty(result.ServiceResolutions);
    }

    [Fact]
    public void ConflictingRegistrations_AreAmbiguous()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class FirstRepo : IRepo { }
            public class SecondRepo : IRepo { }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IRepo, FirstRepo>();
                    services.AddScoped<IRepo, SecondRepo>();
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.True(resolution.IsAmbiguous);
        Assert.Null(resolution.Implementation);
    }

    [Fact]
    public void RegistrationForExternalImplementation_IsRecorded()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IRepo, ExternalRepo>();
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("ExternalRepo", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Registration, resolution.Source);
        Assert.False(resolution.Candidates[0].DeclaredInScan);
        Assert.True(resolution.Candidates[0].FromRegistration);
    }

    [Fact]
    public void CommentsAndStrings_AreNotParsedAsRegistrations()
    {
        var result = Build(("File.cs", """
            namespace App;

            public interface IRepo { }

            public class Repo : IRepo { }

            public static class Setup
            {
                // services.AddScoped<IRepo, Missing>();
                public const string NotCode = "services.AddScoped<IRepo, Missing>();";
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("App.Repo", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Declaration, resolution.Source);
    }

    [Fact]
    public void NorthwindShapedInterface_ResolvesToTheDataContext()
    {
        var result = Build(("Infrastructure.cs", """
            namespace Northwind.Infrastructure;

            public interface INorthwindDbContext { }

            public class NorthwindDbContext : DbContext, INorthwindDbContext { }

            public static class Setup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<INorthwindDbContext, NorthwindDbContext>();
                }
            }
            """));

        var resolution = Assert.Single(result.ServiceResolutions);
        Assert.Equal("Northwind.Infrastructure.INorthwindDbContext", resolution.Interface);
        Assert.Equal("Northwind.Infrastructure.NorthwindDbContext", resolution.Implementation);
        Assert.Equal(ServiceResolutionSource.Both, resolution.Source);
        Assert.False(resolution.IsAmbiguous);
    }

    [Fact]
    public void Index_IsIdenticalInTextAndRoslynModes()
    {
        var files = new[]
        {
            new SourceFile("Contracts.cs", """
                namespace App;

                public interface IRepo { }
                public interface INorthwindDbContext { }
                """, "App"),
            new SourceFile("Infrastructure.cs", """
                namespace App;

                public class Repo : IRepo { }
                public class NorthwindDbContext : INorthwindDbContext { }
                """, "App"),
            new SourceFile("Setup.cs", """
                namespace App;

                public static class Setup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped<IRepo, Repo>();
                        services.AddScoped<INorthwindDbContext, NorthwindDbContext>();
                    }
                }
                """, "App"),
        };

        var text = Project(ScanWith(new TextArchitectureScanner(), files));
        var roslyn = Project(ScanWith(new RoslynArchitectureScanner(), files));

        Assert.Equal(text, roslyn);
        Assert.Contains("App.INorthwindDbContext|App.NorthwindDbContext", text[0]);
    }

    private static AnalysisResult ScanWith(IArchitectureScanner scanner, IReadOnlyList<SourceFile> files)
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var workspace = SourceWorkspace.FromFiles(files);
        scanner.Scan(
            workspace,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            result);

        var parsed = workspace.SuccessfullyParsed
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => (f.File, f.Scan!, f.Mask!))
            .ToList();
        ServiceResolutionIndexBuilder.Build(parsed, result);
        return result;
    }

    private static List<string> Project(AnalysisResult result)
        => result.ServiceResolutions
            .Select(resolution => string.Join("|",
                resolution.Interface,
                resolution.Implementation ?? "",
                resolution.Source.ToString(),
                resolution.IsAmbiguous.ToString(),
                string.Join(",", resolution.Candidates.Select(candidate =>
                    candidate.Type + (candidate.DeclaredInScan ? "D" : "") +
                    (candidate.FromRegistration ? "R" : "")))))
            .ToList();

    private static AnalysisResult Build(params (string Path, string Code)[] sources)
    {
        var files = sources.Select(source => new SourceFile(source.Path, source.Code, "App")).ToList();
        var result = new AnalysisResult { RootPath = "/tmp" };
        var workspace = SourceWorkspace.FromFiles(files);
        var parsed = workspace.SuccessfullyParsed
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => (f.File, f.Scan!, f.Mask!))
            .ToList();
        ServiceResolutionIndexBuilder.Build(parsed, result);
        return result;
    }
}
