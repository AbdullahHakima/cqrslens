using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Zero-network regression tests locking fixes uncovered by the open-source legacy
/// and enterprise repository gauntlet across both <see cref="AnalysisMode.Text"/> and
/// <see cref="AnalysisMode.Roslyn"/>.
/// </summary>
public sealed class LegacyCodebaseRegressionTests : IDisposable
{
    private readonly string _root;

    public LegacyCodebaseRegressionTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-legacy-reg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteSdkProject(string relativeFolder, string name, string body = "")
    {
        WriteFile(
            Path.Combine(relativeFolder, name + ".csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              {body}
            </Project>
            """);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void MultiArityIdentityDbContext_AndGlobalUsing_AreRecognized(AnalysisMode mode)
    {
        WriteSdkProject("Infrastructure", "Infrastructure");
        WriteFile("Infrastructure/_Imports.cs", """
            global using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
            global using Microsoft.EntityFrameworkCore;
            """);
        WriteFile("Infrastructure/IdentityModels.cs", """
            namespace App.Infrastructure;

            public sealed class ApplicationUser { }
            public sealed class ApplicationRole { }
            public sealed class UserClaim { }
            public sealed class UserRole { }
            public sealed class UserLogin { }
            public sealed class RoleClaim { }
            public sealed class UserToken { }
            public sealed class Product { public int Id { get; set; } }
            """);
        WriteFile("Infrastructure/ApplicationDbContext.cs", """
            namespace App.Infrastructure;

            public sealed class ApplicationDbContext : IdentityDbContext<
                ApplicationUser, ApplicationRole, string,
                UserClaim, UserRole, UserLogin, RoleClaim, UserToken>
            {
                public DbSet<Product> Products => Set<Product>();
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("ApplicationDbContext", ctx.Name);
        Assert.Contains(ctx.DbSets, s => s.Property == "Products" && s.Entity.EndsWith("Product", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == AnalysisResult.RoslynUnresolvedContractsCode);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void TransitiveDbContextInheritance_DiscoversConcreteSubclasses(AnalysisMode mode)
    {
        WriteSdkProject("Data", "Data");
        WriteFile("Data/Contexts.cs", """
            using Microsoft.EntityFrameworkCore;

            namespace RealWorld.Data;

            public sealed class Article { public int Id { get; set; } }
            public sealed class Comment { public int Id { get; set; } }

            public class ConduitContext : DbContext
            {
                public DbSet<Article> Articles { get; set; } = null!;
            }

            public sealed class SqlServerConduitContext : ConduitContext
            {
                public DbSet<Comment> Comments { get; set; } = null!;
            }

            public sealed class SqliteConduitContext : ConduitContext
            {
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Equal(3, result.DbContexts.Count);
        Assert.Contains(result.DbContexts, c => c.Name == "ConduitContext");
        Assert.Contains(result.DbContexts, c => c.Name == "SqlServerConduitContext" &&
            c.DbSets.Any(s => s.Property == "Comments"));
        Assert.Contains(result.DbContexts, c => c.Name == "SqliteConduitContext");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void NestedVerticalSlices_LinkSiblingsWithoutAmbiguity(AnalysisMode mode)
    {
        WriteSdkProject("App", "App");
        WriteFile("App/Slices.cs", """
            using MediatR;
            using FluentValidation;

            namespace Contoso.Pages.Students
            {
                public static class Create
                {
                    public sealed record Command(string Name) : IRequest<int>;

                    public sealed class CommandValidator : AbstractValidator<Command> { }

                    public sealed class CommandHandler : IRequestHandler<Command, int> { }
                }

                public static class Edit
                {
                    public sealed record Command(int Id, string Name) : IRequest<int>;

                    public sealed class CommandValidator : AbstractValidator<Command> { }

                    public sealed class CommandHandler : IRequestHandler<Command, int> { }
                }
            }

            namespace Contoso.Other
            {
                using Contoso.Pages.Students;

                public sealed class AuditCreateHandler : IRequestHandler<Create.Command, int> { }
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Equal(2, result.Requests.Count);
        var create = Assert.Single(result.Requests, r => r.FullName == "Contoso.Pages.Students.Create.Command");
        var edit = Assert.Single(result.Requests, r => r.FullName == "Contoso.Pages.Students.Edit.Command");

        Assert.Equal(
            new[] { "Contoso.Other.AuditCreateHandler", "Contoso.Pages.Students.Create.CommandHandler" },
            create.Handlers);
        Assert.Equal(new[] { "Contoso.Pages.Students.Create.CommandValidator" }, create.Validators);

        Assert.Equal(new[] { "Contoso.Pages.Students.Edit.CommandHandler" }, edit.Handlers);
        Assert.Equal(new[] { "Contoso.Pages.Students.Edit.CommandValidator" }, edit.Validators);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == AnalysisResult.AmbiguousLinkCode);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void LegacyMediatRv2v3v4Contracts_AreDetectedWithoutDiagnostics(AnalysisMode mode)
    {
        WriteSdkProject("Legacy", "Legacy");
        WriteFile("Legacy/Features.cs", """
            using MediatR;

            namespace Contoso.Legacy;

            public sealed class Ping : IAsyncRequest { }
            public sealed class PingHandler : AsyncRequestHandler<Ping> { }

            public sealed class GetCount : IAsyncRequest<int> { }
            public sealed class GetCountHandler : IAsyncRequestHandler<GetCount, int> { }

            public sealed class SyncCmd : IRequest { }
            public sealed class SyncCmdHandler : RequestHandler<SyncCmd> { }

            public sealed class LegacyEvent : IAsyncNotification { }
            public sealed class LegacyEventHandler : IAsyncNotificationHandler<LegacyEvent> { }

            // Non-generic decoy must never be classified as a CQRS handler.
            public class RequestHandler { }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Equal(3, result.Requests.Count);
        Assert.All(result.Requests, r => Assert.Null(r.ViaMarker));

        Assert.Single(result.Requests, r => r.Name == "Ping" && r.Handlers.Contains("Contoso.Legacy.PingHandler"));
        Assert.Single(result.Requests, r => r.Name == "GetCount" &&
            r.ResponseType == "int" &&
            r.Handlers.Contains("Contoso.Legacy.GetCountHandler"));
        Assert.Single(result.Requests, r => r.Name == "SyncCmd" && r.Handlers.Contains("Contoso.Legacy.SyncCmdHandler"));
        Assert.DoesNotContain(result.Handlers, h => h.Name == "RequestHandler");

        var notif = Assert.Single(result.Notifications);
        Assert.Equal("LegacyEvent", notif.Name);
        Assert.Null(notif.ViaMarker);
        Assert.Equal(new[] { "Contoso.Legacy.LegacyEventHandler" }, notif.Handlers);

        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == AnalysisResult.UnresolvedContractCode ||
            d.Code == AnalysisResult.RoslynUnresolvedContractsCode);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Ef6SystemDataEntityDbContext_IsRecognizedCleanly(AnalysisMode mode)
    {
        WriteSdkProject("Ef6App", "Ef6App");
        WriteFile("Ef6App/SchoolContext.cs", """
            using System.Data.Entity;

            namespace Contoso.Ef6;

            public sealed class Student { public int Id { get; set; } }

            public sealed class SchoolContext : DbContext
            {
                public SchoolContext() : base("SchoolContext") { }
                public DbSet<Student> Students { get; set; } = null!;
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("SchoolContext", ctx.Name);
        Assert.Contains(ctx.DbSets, s => s.Property == "Students" && s.Entity.EndsWith("Student", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == AnalysisResult.RoslynUnresolvedContractsCode);
    }

    [Fact]
    public void LegacyTargetFrameworkVersion_PackagesConfig_FixieAndXxeGuard_WorkSafely()
    {
        WriteFile("src/LegacyApp/LegacyApp.csproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8.1</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);
        WriteFile("src/LegacyApp/packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="MediatR" version="2.1.0" targetFramework="net481" />
              <package id="EntityFramework" version="6.1.3" targetFramework="net481" />
            </packages>
            """);
        WriteFile("src/LegacyApp/Ping.cs", """
            using MediatR;
            namespace LegacyApp;
            public sealed class Ping : IAsyncRequest { }
            public sealed class PingHandler : AsyncRequestHandler<Ping> { }
            """);

        WriteFile("src/IntegrationTests/IntegrationTests.csproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\LegacyApp\LegacyApp.csproj">
                  <Name>LegacyApp</Name>
                </ProjectReference>
              </ItemGroup>
            </Project>
            """);
        WriteFile("src/IntegrationTests/packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="Fixie" version="1.0.1" targetFramework="net48" />
              <package id="Shouldly" version="2.8.0" targetFramework="net48" />
            </packages>
            """);

        // Malicious XXE packages.config must be safely rejected without crashing the scan.
        WriteFile("src/PoisonedApp/PoisonedApp.csproj", """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);
        WriteFile("src/PoisonedApp/packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE packages [
              <!ENTITY xxe SYSTEM "file:///etc/passwd">
            ]>
            <packages>
              <package id="NUnit" version="3.13.3" />
            </packages>
            """);

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);

        var legacyApp = Assert.Single(result.Projects, p => p.Name == "LegacyApp");
        Assert.Equal(new[] { "net481" }, legacyApp.TargetFrameworks);
        Assert.Contains("MediatR", legacyApp.PackageReferences);
        Assert.Contains("EntityFramework", legacyApp.PackageReferences);

        var poisoned = Assert.Single(result.Projects, p => p.Name == "PoisonedApp");
        Assert.Equal(new[] { "net472" }, poisoned.TargetFrameworks);
        Assert.DoesNotContain("NUnit", poisoned.PackageReferences);

        var testProjects = TestProjectDiscovery.DiscoverTestProjects(result);
        var fixieProject = Assert.Single(testProjects, t => t.Name == "IntegrationTests");
        Assert.Equal(new[] { "Fixie" }, fixieProject.Frameworks);
        Assert.Equal(new[] { "Shouldly" }, fixieProject.AssertionLibraries);
        Assert.Contains("LegacyApp", fixieProject.ReferencedProductionProjects);
    }
}
